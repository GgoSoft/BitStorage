using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace GgoSoft.Serialize
{
	public sealed class FieldMetadataBuilder
	{
		private readonly SerializerOptions _options;
		private readonly IServiceProvider? _services;

		public FieldMetadataBuilder(SerializerOptions options, IServiceProvider? services = null)
		{
			_options = options ?? throw new ArgumentNullException(nameof(options));
			_services = services ?? options.Services;
		}

		/// <summary>
		/// Discover properties annotated with <see cref="BitFieldAttribute"/>, resolve accessors
		/// (honoring per-field and global AllowNonPublicAccess opt-in), create accessor delegates,
		/// perform validation and inference, and return a populated <see cref="TypeMetadata"/>.
		/// </summary>
		public TypeMetadata BuildTypeMetadata(Type type)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));
			// S3011 is a false positive here, since later we validate that the property has accessible getters/setters
			// and we only use the accessors if allowed by the attribute or global options (AllowNonPublicAccess -- default=false).
			// We need to be able to read non-public properties when allowed, and there's no way to get the attributes without
			// doing a non-public search.
#pragma warning disable S3011
			var props = from p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
						let attr = p.GetCustomAttribute<BitFieldAttribute>()
						where attr != null
						orderby attr.OrderNullable ?? p.MetadataToken, p.MetadataToken
						select BuildFieldMetadata(p, attr); // new { Prop = p, Attr = attr };
			//type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			//			.Select(p => new { Prop = p, Attr = p.GetCustomAttribute<BitFieldAttribute>() })
			//			.Where(x => x.Attr != null)
			//			.OrderBy(x => x.Attr!.OrderNullable ?? x.Prop.MetadataToken)
			//			.ThenBy(x => x.Prop.MetadataToken);
#pragma warning restore S3011
			//var fields = props//.Select(x => BuildFieldMetadata(x.Prop, x.Attr))
			//				.ToList()
			//				.AsReadOnly();
			return new TypeMetadata
			{
				Type = type,
				FieldsInOrder = props.ToImmutableArray()
			};
		}

		private FieldMetadata BuildFieldMetadata(PropertyInfo prop, BitFieldAttribute attr)
		{
			if (prop == null) throw new ArgumentNullException(nameof(prop));
			if (attr == null) throw new ArgumentNullException(nameof(attr));

			var metadata = new FieldMetadata()
			{
				Name = prop.Name,
				Property = prop,
				Attribute = attr,
				TypeResolution = ResolveTypeInfo(prop.PropertyType, _options.MaxBitsPerObject, attr),
				ResolvedOrder = attr.OrderNullable ?? prop.MetadataToken
			};
			//ResolveSigned(metadata);
			// 1) Basic validations
			ValidateFieldMetadataPreliminary(metadata);

			if (metadata.IsCustomSerializer)
			{
				var converterInstance = ResolveConverterInstanceIfNeeded(attr);
				metadata.ResolvedConverterInstance = converterInstance;
				metadata.ResolvedConverterType = attr.ConverterType;
				return metadata;
			}

			// 2) Parse min/max
			var (parsedMin, parsedMax) = ParseBounds(attr);

			// 3) Enumerable-specific rules
			if (metadata.TypeResolution.IsEnumerable)
			{
				if (attr.OmitIfEquals != null)
				{
					throw new SerializationException($"Field '{metadata.Name}': Default is not allowed with an enumerator.");
				}
				ValidateEnumerableRules(metadata);
			}

			// 4) Decide resolvedBits (inference, policy, converters, custom types)
			ResolveResolvedBits(metadata, parsedMin, parsedMax);// name, prop, attr, parsedMin, parsedMax, elemType);

			// 5) Compute representable ranges and validate provided bounds
			//var (signedMin, signedMax) = SignedRange(resolvedBits);
			//var unsignedMax = UnsignedMax(resolvedBits);
			//bool hasRange = true;
			//ValidateBoundsAgainstBits(name, attr, parsedMin, parsedMax, resolvedBits);

			// 6) Default value validation (assignability + range)
			ValidateDefaultValue(metadata);// name, attr, propertyType, resolvedBits);

			// 7) Resolve condition and converter instances (DI first)
			var conditionInstance = ResolveConditionInstanceIfNeeded(attr);
			metadata.ResolvedConditionalType = attr.ConditionalType;
			metadata.ResolvedConditionalInstance = conditionInstance;

			// 8) Accessor resolution (public fast delegates; non-public only when allowed)
			metadata.Accessors = ResolveAccessors(prop, attr);

			// 9) Build FieldMetadata
			//var fm = new FieldMetadata
			//{
			//	Name = name,
			//	Property = prop,
			//	Getter = getter,
			//	Setter = setter,
			//	ResolvedBits = resolvedBits,
			//	Signed = attr.Signed,
			//	Optional = attr.Optional,
			//	DefaultValue = attr.Default,
			//	Order = attr.Order ?? prop.MetadataToken,
			//	HasRange = hasRange,
			//	SignedMin = signedMin,
			//	SignedMax = signedMax,
			//	UnsignedMax = unsignedMax,
			//	PropertyType = propertyType,
			//	IsEnumerable = isEnumerable,
			//	ElementType = elemType,
			//	ConditionInstance = conditionInstance,
			//	ConverterInstance = converterInstance,
			//	ParsedMin = parsedMin,
			//	ParsedMax = parsedMax,
			//	Description = attr.Description
			//};

			return metadata;
		}

		// -------------------------
		// Rule helpers
		// -------------------------

		public static void ValidateFieldMetadataPreliminary(FieldMetadata metadata) // null for scalars
		{
			void Fail(string msg) => throw new SerializationException($"Field '{metadata.Name}': {msg}");

			var attr = metadata.Attribute ?? throw new InvalidOperationException("missing attribute");
			var prop = metadata.Property ?? throw new InvalidOperationException("missing property");
			// --- Mutual exclusivity and obvious conflicts ---
			if (attr.BitsNullable.HasValue && attr.InferBitsNullable == true)
				Fail("Bits cannot be specified when InferBits is true.");

			if (attr.CountBitLengthNullable.HasValue && attr.TerminatorValueNullable.HasValue)
				Fail("for IEnumerable<T> needs to provide exactly one of CountBitLength or TerminatorValue.");

			if (attr.MinNullable.HasValue && attr.UnsignedMinNullable.HasValue)
				Fail("Min and UnsignedMin are mutually exclusive.");

			if (attr.MaxNullable.HasValue && attr.UnsignedMaxNullable.HasValue)
				Fail("Max and UnsignedMax are mutually exclusive.");

			if (metadata.ResolvedSigned)
			{
				if (attr.UnsignedMinNullable.HasValue || attr.UnsignedMaxNullable.HasValue)
					Fail("Signed=true cannot be used together with UnsignedMin/UnsignedMax.");

				if (!metadata.TypeResolution.NativeSigned)
					Fail("Signed=true cannot be used with an unsigned type");
			} else
			{
				if (attr.MinNullable < 0)
					Fail("Signed=false cannot have a negative Min value");
			}

			if (!metadata.TypeResolution.IsEnumerable)
			{ 
				// CountBitLength only makes sense for enumerables
				if (attr.CountBitLengthNullable.HasValue)
					Fail("CountBitLength is only valid for enumerable fields.");

				// TerminatorValue only makes sense for enumerables
				if (attr.TerminatorValueNullable.HasValue)
					Fail("TerminatorValue is only valid for enumerable fields.");
			}
			// CountBitLength range check (cheap)
			if (attr.CountBitLengthNullable < 1 || attr.CountBitLengthNullable > 32)
				Fail("CountBitLength must be in range 1..32.");

			// Default conflicts with TerminatorValue (collection-level terminator semantics)
			if (attr.OmitIfEquals != null && attr.TerminatorValueNullable.HasValue)
				Fail("Default is not allowed when TerminatorValue is used (terminator semantics conflict).");

			// Conditional fields: combine/mode only meaningful when a condition is present
			bool hasCondition = !string.IsNullOrEmpty(attr.ConditionalProperty) || attr.ConditionalType != null;
			if (!hasCondition && (attr.ConditionCombine != ConditionCombine.And || attr.ConditionMode != ConditionEvaluationMode.Snapshot))
				Fail("ConditionCombine/ConditionMode set but no ConditionalProperty or ConditionalType provided.");

			// --- Lightweight bounds ordering checks (same-signness only) ---
			// Only perform simple ordering checks when both bounds are present and of the same signedness.
			if (attr.MaxNullable < attr.MinNullable)
				Fail($"Invalid bounds: Max ({attr.MaxNullable.Value}) is less than Min ({attr.MinNullable.Value}).");

			if (attr.UnsignedMaxNullable < attr.UnsignedMinNullable)
				Fail($"Invalid bounds: UnsignedMax ({attr.UnsignedMaxNullable.Value}) is less than UnsignedMin ({attr.UnsignedMinNullable.Value}).");

			// Do not attempt cross-signed comparisons here (e.g., Min vs UnsignedMax) — defer to numeric-resolution pass.

			// TerminatorValue cannot have a value if enumerableElementType is null, no need to check here too
			// If TerminatorValue is present, ensure it is within a plausible range (cheap check)
			// We cannot fully validate it without element width; just ensure it's non-negative (terminator is an encoded value).
			if (attr.TerminatorValueNullable < 0)
				Fail("TerminatorValue must be non-negative.");

			// --- Converter/CustomSerializer presence quick checks (no heavy validation) ---
			// We only flag obviously missing required attributes here; do not attempt to resolve converter-provided widths.
			if (attr.ConverterType != null || typeof(ICustomBitSerializable).IsAssignableFrom(prop.PropertyType))
			{
				// Mark metadata
				metadata.IsCustomSerializer = true;

				// Disallowed attributes with custom serializer
				if (attr.BitsNullable.HasValue) Fail("Bits cannot be used with a custom serializer.");
				if (attr.InferBitsNullable == true) Fail("InferBits cannot be used with a custom serializer.");
				if (attr.MinNullable.HasValue || attr.MaxNullable.HasValue || attr.UnsignedMinNullable.HasValue || attr.UnsignedMaxNullable.HasValue)
					Fail("Min/Max/UnsignedMin/UnsignedMax cannot be used with a custom serializer.");
				if (attr.CountBitLengthNullable.HasValue || attr.TerminatorValueNullable.HasValue)
					Fail("CountBitLength and TerminatorValue cannot be used with a custom serializer.");
				// Default allowed only if documented; otherwise reject
				if (attr.OmitIfEquals != null) Fail("Default is not allowed with a custom serializer unless the serializer documents support.");
			}
			else if (metadata.TypeResolution.FieldType == null)
			{
				Fail("Cannot resolve type to valid data type");
			}
			else if (metadata.TypeResolution.Width == null)
			{
				Fail("Typewidth cannot be null");
			}

			// --- Final quick sanity checks ---
			if (attr.BitsNullable.HasValue && attr.BitsNullable.Value < 1)
				Fail("Bits must be >= 1.");

			// All preliminary checks passed
		}

		private static readonly ConcurrentDictionary<Type, TypeResolution> _typeResolutionCache = new();
		private static readonly ConcurrentDictionary<Type, Type?> _elementTypeCache = new();

		public static bool TryGetEnumerableElementType(Type type, out Type? elementType)
		{
			elementType = _elementTypeCache.GetOrAdd(type, t =>
			{
				if (t == typeof(string)) return typeof(char);
				if (t.IsArray) return t.GetElementType();

				// If the type itself is IEnumerable<T>
				if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
					return t.GetGenericArguments()[0];

				// Look at implemented interfaces for IEnumerable<T>
				foreach (var iface in t.GetInterfaces())
				{
					if (!iface.IsGenericType) continue;
					if (iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
						return iface.GetGenericArguments()[0];
				}

				return null;
			});

			return elementType != null;
		}

		//private void ResolveSigned(FieldMetadata metadata)
		//{
		//	var specifiedSigned = metadata.Attribute.SignedNullable;
		//	var nativeSigned = metadata.TypeResolution.NativeSigned;
		//	var globalSigned = _options.Signed;
		//	if (specifiedSigned.HasValue)
		//	{
		//		metadata.ResolvedSigned = specifiedSigned.Value;
		//	}
		//	else if (globalSigned.HasValue)
		//	{
		//		metadata.ResolvedSigned = globalSigned.Value;
		//	}
		//	else
		//	{
		//		metadata.ResolvedSigned = nativeSigned;
		//	}
		//}

		public TypeResolution ResolveTypeInfo(Type fieldType, long maxAllowedBits, BitFieldAttribute attr) // TODO: maxAllowedBits is wrong
		{
			// Fast primitive/unwrapping checks first (cheap)
			var underlying = Nullable.GetUnderlyingType(fieldType);
			//var isNullable = (underlying != null); 
			underlying ??= fieldType;
			if (underlying.IsEnum) underlying = Enum.GetUnderlyingType(underlying);
			//int signedAdjustment = signed ? 0 : 1;
			(Type dataType, int width, bool signed, bool isPrimitive) returnValue = (typeof(void), 0, false, false);
			if (underlying == typeof(bool)) returnValue = (typeof(bool), 1, false, true);
			if (underlying == typeof(char)) returnValue = (typeof(char), 16, false, true);
			if (underlying == typeof(byte)) returnValue = (typeof(byte), 8, false, true);
			if (underlying == typeof(sbyte)) returnValue = (typeof(sbyte), 8, true, true);
			if (underlying == typeof(short)) returnValue = (typeof(short), 16, true, true);
			if (underlying == typeof(ushort)) returnValue = (typeof(ushort), 16, false, true);
			if (underlying == typeof(int)) returnValue = (typeof(int), 32, true, true);
			if (underlying == typeof(uint)) returnValue = (typeof(uint), 32, false, true);
			if (underlying == typeof(long)) returnValue = (typeof(long), 64, true, true);
			if (underlying == typeof(ulong)) returnValue = (typeof(ulong), 64, false, true);

			if (returnValue.width > 0)
			{
				bool nativeSigned = returnValue.signed;
				int nativeWidth = returnValue.width;
				if (attr.SignedNullable.HasValue)
				{
					returnValue.signed = attr.SignedNullable.Value;
				}
				else if (_options.Signed.HasValue)
				{
					returnValue.signed = _options.Signed.Value;
				}
				else
				{
					returnValue.signed = nativeSigned;
				}
				int width = nativeWidth;
				if (!returnValue.signed && nativeSigned)
				{
					width--;
				}
				return new TypeResolution(returnValue.dataType, nativeWidth, width, false, returnValue.isPrimitive, /*isNullable, false,*/ nativeSigned, returnValue.signed, null, null);
			}

			// Cached slow path for non-primitives
			return _typeResolutionCache.GetOrAdd(fieldType, t =>
			{
				Console.WriteLine(t.Name);
				// If it's an enumerable, resolve the element type info (recurses but uses cache)
				if (TryGetEnumerableElementType(t, out var elemType))
				{
					if (elemType == null)
						return new TypeResolution(null, null, null, true, false, /*false, false,*/ false, false, null, null); // non-generic IEnumerable -> unknown element

					// Resolve element info (calls back into ResolveTypeInfo but will hit cache for primitives)
					var elemInfo = ResolveTypeInfo(elemType, maxAllowedBits, attr);

					// If element width is unknown, caller should require a converter or explicit Bits
					return new TypeResolution(elemType, elemInfo.NativeWidth, elemInfo.Width, true, elemInfo.IsPrimitive, /*isNullable, elemInfo.IsNullable,*/ elemInfo.NativeSigned, elemInfo.Signed, null, null);
				}

				// Unknown/unsupported type
				return new TypeResolution(null, null, null, false, false, /*false, false,*/ false, false, null, null);
			});
		}

		private static (BigInteger? parsedMin, BigInteger? parsedMax) ParseBounds(BitFieldAttribute attr)
		{
			BigInteger? parsedMin = null, parsedMax = null;
			if (attr.MinNullable.HasValue) parsedMin = new BigInteger(attr.MinNullable.Value);
			if (attr.MaxNullable.HasValue) parsedMax = new BigInteger(attr.MaxNullable.Value);
			if (attr.UnsignedMinNullable.HasValue) parsedMin = new BigInteger(attr.UnsignedMinNullable.Value);
			if (attr.UnsignedMaxNullable.HasValue) parsedMax = new BigInteger(attr.UnsignedMaxNullable.Value);
			return (parsedMin, parsedMax);
		}

		private static void ValidateEnumerableRules(FieldMetadata metadata)//string name, BitFieldAttribute attr, Type? elemType)
		{
			if (metadata.Attribute?.TerminatorValueNullable.HasValue ?? false)
			{
				int elementWidth = metadata.ResolvedBits;
				if (!TerminatorFits(metadata.Attribute.TerminatorValueNullable.Value, elementWidth))
					throw new SerializationException($"Field '{metadata.Name}': TerminatorValue does not fit in element width {elementWidth}.");
			}
		}
		private void ResolveResolvedBits(FieldMetadata metadata,
			BigInteger? parsedMin,
			BigInteger? parsedMax)
		{
			// Determine the "subject type" to consult for native width: element type if enumerable, otherwise the property type.
			BigInteger min, max;
			var subjectType = metadata.FieldType;
			var name = metadata.Name;
			var attr = metadata.Attribute;
			int maxAllowed = metadata.TypeResolution.Width ?? throw new SerializationException("$Field '{name}': Width cannot be null");
			bool nativeSigned = metadata.TypeResolution.NativeSigned;

			if (attr?.BitsNullable.HasValue ?? false)
			{
				int requested = attr.BitsNullable.Value;
				const int absoluteMin = 1;
				if (requested < absoluteMin || requested > maxAllowed)
				{
					throw new SerializationException($"Field '{name}': Bits must be between {absoluteMin} and {maxAllowed} for type {subjectType.Name}.");
				}
				maxAllowed = requested;
				(min, max) = TryGetMinMax(parsedMin, parsedMax, metadata.ResolvedSigned, maxAllowed, metadata);
			}
			// No explicit Bits: follow existing inference / policy logic
			else if (attr?.InferBitsNullable ?? _options.AutoInferBits) // if InferBits is true or if it's null and AutoInferBits is true
			{
				(min, max) = TryGetMinMax(parsedMin, parsedMax, metadata.ResolvedSigned, maxAllowed, metadata);
				maxAllowed = InferBitsFromBounds(min, max, metadata.ResolvedSigned, _options.MaxInferredBits);
			}
			else
			{
				throw new SerializationException($"Field '{name}': Bits not specified and global policy forbids inference.");
			}
			metadata.ResolvedMin = min;
			metadata.ResolvedMax = max;
			metadata.ResolvedBits = maxAllowed;
		}

		private static void ValidateDefaultValue(FieldMetadata metadata) //string name, object? defaultValue, Type propertyType, BigInteger min, BigInteger max)
		{
			if (metadata.Attribute?.OmitIfEquals == null) return; // nothing to validate

			BigInteger defaultBig;
			try
			{
				defaultBig = metadata.Attribute.OmitIfEquals switch
				{
					BigInteger bi => bi,
					bool b => b ? BigInteger.One : BigInteger.Zero,
					char c => new BigInteger((ulong)c),
					sbyte sb => new BigInteger(sb),
					byte bb => new BigInteger(bb),
					short ss => new BigInteger(ss),
					ushort us => new BigInteger(us),
					int ii => new BigInteger(ii),
					uint uii => new BigInteger(uii),
					long ll => new BigInteger(ll),
					ulong ull => new BigInteger(ull),

					// Optional: accept numeric strings
					string s => (metadata.ResolvedMin < 0)
						? new BigInteger(Convert.ToInt64(s, CultureInfo.InvariantCulture))
						: new BigInteger(Convert.ToUInt64(s, CultureInfo.InvariantCulture)),

					// Fallback for other convertible boxed values
					_ => (metadata.ResolvedMin < 0)
						? new BigInteger(Convert.ToInt64(metadata.Attribute.OmitIfEquals, CultureInfo.InvariantCulture))
						: new BigInteger(Convert.ToUInt64(metadata.Attribute.OmitIfEquals, CultureInfo.InvariantCulture))
				};
			}
			catch (Exception ex)
			{
				throw new SerializationException($"Field '{metadata.Name}': error converting Default value: {ex.Message}");
			}

			// simple range check (min and max are assumed valid and min < max)
			if (defaultBig < metadata.ResolvedMin || defaultBig > metadata.ResolvedMax)
				throw new SerializationException($"Field '{metadata.Name}': Default value ({defaultBig}) out of range {metadata.ResolvedMin}..{metadata.ResolvedMax}.");
		}

		private IFieldCondition? ResolveConditionInstanceIfNeeded(BitFieldAttribute attr)
		{
			if (attr.ConditionalType == null) return null;
			if (!typeof(IFieldCondition).IsAssignableFrom(attr.ConditionalType))
				throw new SerializationException($"ConditionalType must implement IFieldCondition.");
			return ResolveConditionInstance(attr.ConditionalType);
		}

		private IBitConverter? ResolveConverterInstanceIfNeeded(BitFieldAttribute attr)
		{
			if (attr.ConverterType == null) return null;
			if (!typeof(IBitConverter).IsAssignableFrom(attr.ConverterType))
				throw new SerializationException($"ConverterType must implement IBitConverter.");
			return ResolveConverterInstance(attr.ConverterType);
		}

		private readonly ConcurrentDictionary<(PropertyInfo info, bool allowNonPublic), Accessors> _accessorCache = new();
		/// <summary>
		/// Resolve getter/setter delegates. Public accessors use expression-compiled delegates (fast).
		/// Non-public accessors are only permitted when the attribute or global options opt-in; in that case
		/// we create reflection-invoke wrappers.
		/// </summary>
		private Accessors ResolveAccessors(PropertyInfo prop, BitFieldAttribute attr)
		{
			bool allowNonPublic = attr.AllowNonPublicAccessNullable ?? _options.AllowNonPublicAccess ?? false;
			return _accessorCache.GetOrAdd((prop, allowNonPublic), pa =>
			{
				var p = pa.info;

				if (prop.GetIndexParameters().Length > 0)
					throw new SerializationException($"Indexers are not supported: {prop.Name}");

				// Get underlying accessor MethodInfos (allow non-public lookup here, but only use them if allowed)
				var getterMethod = p.GetGetMethod(nonPublic: true);
				var setterMethod = p.GetSetMethod(nonPublic: true);

				// If a non-public accessor exists but non-public access is not allowed, fail fast.
				if (!allowNonPublic)
				{
					if (getterMethod != null && !getterMethod.IsPublic)
						throw new SerializationException($"Field '{p.DeclaringType?.FullName}.{p.Name}': getter is non-public, AllowNonPublicAccess on field, or global options needs to be set.");
					if (setterMethod != null && !setterMethod.IsPublic)
						throw new SerializationException($"Field '{p.DeclaringType?.FullName}.{p.Name}': setter is non-public, AllowNonPublicAccess on field, or global options needs to be set.");
				}

				// If accessors are missing, fail (we require both getter and setter for simplicity; can relax if needed)
				if (getterMethod == null)
					throw new SerializationException($"Field '{p.DeclaringType?.FullName}.{p.Name}': getter is missing.");
				if (setterMethod == null)
					throw new SerializationException($"Field '{p.DeclaringType?.FullName}.{p.Name}': setter is missing.");

				// For simplicity, we require properties to be declared on reference types (classes); we can relax this if needed by adding struct-specific handling.
				if (p.DeclaringType == null)
				{
					throw new SerializationException($"Declaring type for field {p.Name} cannot be null.");
				}
				if (p.DeclaringType.IsValueType)
				{
					throw new SerializationException("Struct properties cannot be deserialized via setters.");
				}
				var instance = Expression.Parameter(typeof(object), "instance");
				var castInstance = Expression.Convert(instance, p.DeclaringType);

				// Getter: (object instance) => (object) ((DeclaringType)instance).Property
				var getter = Expression.Lambda<Func<object, object?>>(
						Expression.Convert(Expression.Call(castInstance, getterMethod), typeof(object)),
						instance
					).Compile();

				// Setter: (object instance, object value) => ((DeclaringType)instance).Property = (PropertyType)value
				var value = Expression.Parameter(typeof(object), "value");
				var setter = Expression.Lambda<Action<object, object?>>(
					Expression.Call(castInstance, setterMethod, Expression.Convert(value, p.PropertyType)),
					instance, value
				).Compile();

				return new Accessors
				{
					Getter = getter,
					Setter = setter,
					GetterIsPublic = getterMethod.IsPublic,
					SetterIsPublic = setterMethod.IsPublic
				};
			});

		}

		private static bool TerminatorFits(ulong term, int bits)
		{
			if (bits == 64) return true;
			return term <= ((1UL << bits) - 1UL);
		}

		private IFieldCondition ResolveConditionInstance(Type conditionType)
		{
			if (_services?.GetService(conditionType) is IFieldCondition svc) return svc;
			if (Activator.CreateInstance(conditionType) is IFieldCondition inst) return inst;

			throw new SerializationException($"Unable to instantiate condition type {conditionType}.");
		}

		private IBitConverter ResolveConverterInstance(Type converterType)
		{
			if (_services?.GetService(converterType) is IBitConverter svc) return svc;
			if (Activator.CreateInstance(converterType) is IBitConverter inst) return inst;

			throw new SerializationException($"Unable to instantiate converter type {converterType}.");
		}

		private static BigInteger MagnitudeCandidate(BigInteger v)
		{
			if (v >= 0) return v;
			return BigInteger.Abs(v) - 1;
		}
		private static int InferBitsFromBounds(
			BigInteger min,
			BigInteger max,
			bool signed,
			int maxAllowed) // nativeWidth in bits, if available
		{
			// If both are zero, 1 bit is enough (-0..0 in two's complement)
			if (min == 0 && max == 0)
			{
				return 1;
			}

			BigInteger a = MagnitudeCandidate(min);
			BigInteger b = MagnitudeCandidate(max);
			BigInteger m = BigInteger.Max(a, b);

			// bit length of 0 is 0; we need at least 1 bit
			int required = Math.Max(1, (int)m.GetBitLength() + (signed ? 1 : 0));

			if (required > maxAllowed)
			{
				throw new SerializationException($"Inferred bits {required} exceed allowed maximum {maxAllowed}.");
			}

			return required;
		}

		private static (BigInteger min, BigInteger max) TryGetMinMax(BigInteger? parsedMin, BigInteger? parsedMax, bool signed, int width, FieldMetadata metadata)
		{
			if (width <= 0 || width > 1024) // sanity guard for absurd widths
			{
				throw new SerializationException($"Invalid width {width}.");
			}

			BigInteger nativeMin = signed ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
			BigInteger nativeMax = signed ? (BigInteger.One << (width - 1)) - 1 : (BigInteger.One << width) - 1;

			BigInteger min = parsedMin ?? nativeMin;
			BigInteger max = parsedMax ?? nativeMax;
			if (min < nativeMin)
			{
				throw new SerializationException($"Field '{metadata.Name}': Min value ({min}) is less than what fits in width ({nativeMin}).");
			}
			if (max > nativeMax)
			{
				throw new SerializationException($"Field '{metadata.Name}': Max value ({max}) is greater than what fits in width ({nativeMax}).");
			}
			if (max < min)
			{
				throw new SerializationException($"Field '{metadata.Name}': Invalid bounds: max ({max}) is less than min ({min}).");
			}
			return (min, max);
		}
	}
}
