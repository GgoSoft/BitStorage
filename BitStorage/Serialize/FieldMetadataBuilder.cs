using GgoSoft.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace GgoSoft.Serialize
{
	public enum FramingMode { None, Count, Terminator }
	public sealed record EnumerableFraming
	{
		public int Depth { get; init; }                 // 1-based depth
		public FramingMode Mode { get; init; } = FramingMode.None;
		public int? CountBitLength { get; init; }
		public long? TerminatorValue { get; init; }
		public long? EscapeValue { get; init; }

		public override string ToString()
		{
			return $"Depth: {Depth}\nMode: {Mode}\nCountBitLength: {CountBitLength}\nTerminatorValue: {TerminatorValue}\nEscapeValue: {EscapeValue}";
		}
	}
	public sealed class FieldMetadataBuilder(SerializerOptions options, IServiceProvider? services = null)
	{
		private readonly SerializerOptions _options = options ?? throw new ArgumentNullException(nameof(options));
		private readonly IServiceProvider? _services = services ?? options.Services;

		/// <summary>
		/// Discover properties annotated with <see cref="BitFieldAttribute"/>, resolve accessors
		/// (honoring per-field and global AllowNonPublicAccess opt-in), create accessor delegates,
		/// perform validation and inference, and return a populated <see cref="TypeMetadata"/>.
		/// </summary>
		public TypeMetadata BuildTypeMetadata(Type type)
		{
			ArgumentNullException.ThrowIfNull(type);
			// S3011 is a false positive here, since later we validate that the property has accessible getters/setters
			// and we only use the accessors if allowed by the attribute or global options (AllowNonPublicAccess -- default=false).
			// We need to be able to read non-public properties when allowed, and there's no way to get the attributes without
			// doing a non-public search.
#pragma warning disable S3011
			var props = from p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
						let bitField = MetadataEngine.Hydrate(p)
						let levels = MetadataEngine.HydrateMultiple<BitFieldLevelAttribute>(p)
						where bitField != null || levels.Count != 0
						orderby bitField.HasOrder ? bitField.Order : p.MetadataToken, p.MetadataToken
						select BuildFieldMetadata(p, bitField, [.. levels]);
#pragma warning restore S3011
			return new TypeMetadata
			{
				Type = type,
				FieldsInOrder = [.. props]
			};
		}

		private FieldMetadata BuildFieldMetadata(PropertyInfo prop, BitFieldAttribute attr, BitFieldLevelAttribute[] levels)
		{
			if(attr == null)
			{
				throw new SerializationException($"Property '{prop.Name}': Missing BitFieldAttribute.");
			}
			ArgumentNullException.ThrowIfNull(prop);
			ArgumentNullException.ThrowIfNull(levels);
			var metadata = new FieldMetadata()
			{
				Name = prop.Name,
				Property = prop,
				Attribute = attr,
				BitFieldBounds = (attr as IBitFieldBounds) ?? EmptyBitFieldBounds.Instance,
				TypeResolution = ResolveTypeInfo(prop/*.Name, prop.PropertyType*/, attr, levels),
				ResolvedOrder = attr.HasOrder ? attr.Order : prop.MetadataToken
			};

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

			if (metadata.UnderlyingType.CustomBitSerializable is null)
			{
				ResolveResolvedBits(metadata);
			}
			// 3) Enumerable-specific rules
			if (metadata.TypeResolution[0].IsEnumerable)
			{
				ValidateEnumerableRules(metadata);
			}

			// 6) Default value validation (assignability + range)
			ValidateDefaultValue(metadata, metadata.ResolvedDefaultValue);

			// 7) Resolve condition and converter instances (DI first)
			var conditionInstance = ResolveConditionInstanceIfNeeded(attr);
			metadata.ResolvedConditionalType = attr.ConditionalType;
			metadata.ResolvedConditionalInstance = conditionInstance;

			// 8) Accessor resolution (public fast delegates; non-public only when allowed)
			metadata.Accessors = ResolveAccessors(prop, attr);

			return metadata;
		}

		// -------------------------
		// Rule helpers
		// -------------------------

		public static void ValidateFieldMetadataPreliminary(FieldMetadata metadata) // null for scalars
		{
			void Fail(string msg) => throw new SerializationException($"Field '{metadata.Name}': {msg}");

			var attr = metadata.Attribute ?? throw new InvalidOperationException("missing attribute");
			var bounds = metadata.BitFieldBounds ?? throw new InvalidOperationException("missing bounds");
			var prop = metadata.Property ?? throw new InvalidOperationException("missing property");
			// --- Mutual exclusivity and obvious conflicts ---
			if (attr.HasBits && attr.InferBits)
				Fail("Bits cannot be specified when InferBits is true.");

			if (metadata.UnderlyingType.Signed)
			{
				if ((metadata.UnderlyingType?.NativeSigned) != true)
					Fail("Signed=true cannot be used with an unsigned type");
			} else
			{
				if (bounds.HasMin && bounds.SignedMin < 0)
					Fail("Signed=false cannot have a negative Min value");
			}

			if(metadata.TypeResolution.Length == 0)
			{
				Fail("Type resolution failed to produce any resolutions.");
			}
			if (!metadata.TypeResolution[^1].IsPrimitive && metadata.TypeResolution[^1].CustomBitSerializable is null)
			{
				Fail($"Unsupported type '{metadata.Name}'. Only primitive types and enumerables of primitive types are supported.");
			}
			if (!metadata.TypeResolution[0].IsEnumerable)
			{ 
				// TerminatorValue only makes sense for enumerables
				if (bounds.HasTerminator)
					Fail("TerminatorValue is only valid for enumerable fields.");
			}
			// Conditional fields: combine/mode only meaningful when a condition is present
			bool hasCondition = !string.IsNullOrEmpty(attr.ConditionalProperty) || attr.ConditionalType != null;
			if (!hasCondition && (attr.ConditionCombine != ConditionCombine.And || attr.ConditionMode != ConditionEvaluationMode.Snapshot))
				Fail("ConditionCombine/ConditionMode set but no ConditionalProperty or ConditionalType provided.");

			// --- Lightweight bounds ordering checks (same-signness only) ---
			// Only perform simple ordering checks when both bounds are present and of the same signedness.
			if (bounds.IsSigned && bounds.HasMax && bounds.HasMin && bounds.SignedMax < bounds.SignedMin)
				Fail($"Invalid bounds: Max ({bounds.SignedMax}) is less than Min ({bounds.SignedMin}).");

			if (!bounds.IsSigned && bounds.HasMax && bounds.HasMin && bounds.UnsignedMax < bounds.UnsignedMin)
				Fail($"Invalid bounds: UnsignedMax ({bounds.UnsignedMax}) is less than UnsignedMin ({bounds.UnsignedMin}).");

			// Do not attempt cross-signed comparisons here (e.g., Min vs UnsignedMax) — defer to numeric-resolution pass.

			// TerminatorValue cannot have a value if enumerableElementType is null, no need to check here too
			// If TerminatorValue is present, ensure it is within a plausible range (cheap check)
			// We cannot fully validate it without element width; just ensure it's non-negative (terminator is an encoded value).
			if (!attr.Signed && bounds.HasTerminator && (bounds.UnsignedTerminator < 0))
				Fail("Signed = false cannot have a negative Terminator");

			// --- Converter/CustomSerializer presence quick checks (no heavy validation) ---
			// We only flag obviously missing required attributes here; do not attempt to resolve converter-provided widths.
			if (attr.ConverterType != null || typeof(ICustomBitSerializable).IsAssignableFrom(prop.PropertyType))
			{
				// Mark metadata
				metadata.IsCustomSerializer = true; // TODO: metadata shouldn't change in validator

				// Disallowed attributes with custom serializer
				if (attr.HasBits) Fail("Bits cannot be used with a custom serializer.");
				if (attr.InferBits) Fail("InferBits cannot be used with a custom serializer.");
				if (bounds.HasMin || bounds.HasMax)
					Fail("Min/Max/UnsignedMin/UnsignedMax cannot be used with a custom serializer.");
			}
			else if ((metadata.UnderlyingType?.FieldType) == null)
			{
				Fail("Cannot resolve type to valid data type");
			}
			else if ((metadata.UnderlyingType?.Width) == null && metadata.TypeResolution[^1].CustomBitSerializable is null)
			{
				Fail("Typewidth cannot be null");
			}

			// --- Final quick sanity checks ---
			if (attr.HasBits && attr.Bits < 1)
				Fail("Bits must be >= 1.");

			// All preliminary checks passed
		}

		private static readonly ConcurrentDictionary<Type, Type?> _elementTypeCache = new();

		public static bool TryGetEnumerableElementType([NotNullWhen(true)]Type? type, [NotNullWhen(true)] out Type? elementType)
		{
			if(type == null)
			{
				elementType = null;
				return false;
			}
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

		public TypeResolution? TryGetPrimitiveWidth(string fieldName, Type fieldType, Type declaringType, BitFieldAttribute attr, int depth, BitFieldLevelAttribute?[] levels)	
		{
			// unwrap nullable<T>
			var underlying = Nullable.GetUnderlyingType(fieldType);
			bool isNullable = underlying != null;
			underlying ??= fieldType;

			// unwrap enum → underlying integral type
			if (underlying.IsEnum)
				underlying = Enum.GetUnderlyingType(underlying);

			// determine native width + native signedness
			if (!Helpers.TryGetNativePrimitiveInfo(underlying, out int nativeWidth, out bool nativeSigned))
				return null;

			if(depth + 1 < levels.Length)
			{
				throw new SerializationException($"Depth of BitFieldLevel attributes must match the nesting depth of the type. Expected depth {depth}, but found {levels.Length} in {fieldName}.");
			}

			// determine effective signedness
			bool effectiveSigned = attr.HasSigned ? attr.Signed : (_options.Signed ?? nativeSigned);

			// determine effective width
			int effectiveWidth = nativeWidth;

			// unsigned version of a signed type reduces width by 1
			if (!effectiveSigned && nativeSigned)
				effectiveWidth--;

			var level = GetAtDepth(levels, depth);
			return new TypeResolution {
				FieldType = underlying,
				NativeWidth = nativeWidth,
				Width = effectiveWidth,
				IsEnumerable = false,
				IsPrimitive = true,
				IsNullable = isNullable,
				NativeSigned = nativeSigned,
				Signed = effectiveSigned,
				//EnumerableElement = null,
				CustomBitSerializable = null,
				ShouldContinueMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldContinueInvoker>(declaringType, level?.ShouldContinueMethod),
				ShouldSerializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldSerializeInvoker>(declaringType, level?.ShouldSerialize),
				ShouldDeserializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldDeserializeInvoker>(declaringType, level?.ShouldDeserialize),
				LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, false),
			};
		}

		private static bool IsNullable(PropertyInfo property)
		{
			NullabilityInfoContext nullabilityInfoContext = new NullabilityInfoContext();
			var info = nullabilityInfoContext.Create(property);
			if (info.WriteState == NullabilityState.Nullable || info.ReadState == NullabilityState.Nullable)
			{
				return true;
			}

			return false;
		}
		public TypeResolution[] ResolveTypeInfo(PropertyInfo prop, BitFieldAttribute attr, BitFieldLevelAttribute[] levels) // TODO: maxAllowedBits should be added here
		{
			string fieldName = prop.Name;
			Type type = prop.PropertyType;
			var declaringType = prop.DeclaringType;
			int depth = 0;
			var normalizedLevels = NormalizeBitLevels(fieldName, levels);
			// Fast path for primitives
			var typeResolution = TryGetPrimitiveWidth(fieldName, type, declaringType, attr, depth, normalizedLevels);
			if (typeResolution is not null)
			{
				return [typeResolution];
			}
			bool isNullable = IsNullable(prop);
			List<TypeResolution> typeResolutionResult = [];
			_ = ResolveTypeInfoCore(
				fieldName,
				type,
				declaringType,
				depth: 0,
				attr,
				normalizedLevels,
				typeResolutionResult,
				isNullable
			);
			typeResolutionResult.Reverse();
			return typeResolutionResult.ToArray();
		}
		public static BitFieldLevelAttribute?[] NormalizeBitLevels(string fieldName, BitFieldLevelAttribute[] levels)
		{
			int currentDepth = 0;
			List<BitFieldLevelAttribute?> returnResult = [];
			foreach(var level in levels)
			{
				if(!level.HasDepth)
				{
					level.Depth = currentDepth;
					level.HasDepth = true;
				}
				if(level.Depth < 0)
				{
					throw new SerializationException($"Depth values cannot be negative. Found invalid value: {level.Depth} in field {fieldName}.");
				}
				if(level.HasDepth && level.Depth < currentDepth)
				{
					throw new SerializationException($"Depth values must be forward-only. Expected greater than or equal {currentDepth}, but found: {level.Depth} in {fieldName}.");
				}
				currentDepth = level.Depth;
				while (returnResult.Count <= currentDepth)
				{
					returnResult.Add(default);
				}
				returnResult[currentDepth] = level;
				currentDepth++;
			}
			return returnResult.ToArray();
		}
		private TypeResolution? ResolveTypeInfoCore(
			string fieldName,
			Type type,
			Type declaringType,
			int depth,
			BitFieldAttribute attr,
			BitFieldLevelAttribute?[] levels,
			List<TypeResolution> typeResolutionResult,
			bool? isNullable = null)//,
		{
			// Determine optional/conditional values for this depth
			var level = GetAtDepth(levels, depth);

			// If this type is an enumerable, recurse into its element type
			if (TryGetEnumerableElementType(type, out Type? elementType))
			{

				_ = ResolveTypeInfoCore(
					fieldName,
					elementType,
					declaringType,
					depth + 1,
					attr,
					levels,
					typeResolutionResult
				);
				var returnValue = new TypeResolution
				{
					FieldType = type,
					Width = null,
					IsEnumerable = true,
					LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, true),
					IsNullable = isNullable,
					IsString = type == typeof(string),
					ShouldContinueMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldContinueInvoker>(declaringType, level?.ShouldContinueMethod),
					ShouldSerializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldSerializeInvoker>(declaringType, level?.ShouldSerialize),
					ShouldDeserializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldDeserializeInvoker>(declaringType, level?.ShouldDeserialize),
				};
				typeResolutionResult.Add(returnValue);
				return returnValue;
			}

			// Primitive or terminal type
			TypeResolution? underlyingType = null;
			if(typeof(IBitSerializable).IsAssignableFrom(type))
			{
				underlyingType = new TypeResolution
				{
					FieldType = type,
					Width = null,
					IsEnumerable = false,
					IsPrimitive = false,
					IsNullable = isNullable,
					CustomBitSerializable = type,
					ShouldContinueMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldContinueInvoker>(declaringType, level?.ShouldContinueMethod),
					ShouldSerializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldSerializeInvoker>(declaringType, level?.ShouldSerialize),
					ShouldDeserializeMethod = MethodDelegateFactory.CreateMethod<MethodDelegateFactory.ShouldDeserializeInvoker>(declaringType, level?.ShouldDeserialize),
					LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, false)
				};
			}
			else
			{
				underlyingType = TryGetPrimitiveWidth(fieldName, type, declaringType, attr, depth, levels);
				if (underlyingType == null)
				{
					throw new SerializationException($"Unsupported type '{type.FullName}' at depth {depth} in field '{fieldName}'. Only primitive types and enumerables of primitive types are supported.");
				}
			}
			typeResolutionResult.Add(underlyingType);
			return underlyingType;
		}
		private static T? GetAtDepth<T>(T?[] arr, int depth)
		{
			if (depth < arr.Length)
				return arr[depth];
			return default;
		}

		private static void ValidateEnumerableRules(FieldMetadata metadata)//string name, BitFieldAttribute attr, Type? elemType)
		{
			// TODO: this needs to work
			if (metadata.BitFieldBounds?.HasTerminator ?? false)
			{
				//int elementWidth = metadata.ResolvedBits;
				//var terminatorValue = metadata.BitFieldBounds.Terminator;
				//if (!TerminatorFits(terminatorValue, elementWidth))
				//{
				//	throw new SerializationException($"Field '{metadata.Name}': TerminatorValue does not fit in element width {elementWidth}.");
				//}
				//metadata.ResolvedTerminatorValue = terminatorValue;
			}
		}
		private void ResolveResolvedBits(FieldMetadata metadata)
		{
			// Determine the "subject type" to consult for native width: element type if enumerable, otherwise the property type.
			var primitiveType = metadata.UnderlyingType;
			var subjectType = primitiveType?.FieldType;
			var name = metadata.Name;
			var attr = metadata.Attribute;
			int maxAllowed = primitiveType?.Width ?? throw new SerializationException("$Field '{name}': Width cannot be null");

			if (attr?.HasBits ?? false)
			{
				int requested = attr.Bits;
				const int absoluteMin = 1;
				if (requested < absoluteMin || requested > maxAllowed)
				{
					throw new SerializationException($"Field '{name}': Bits must be between {absoluteMin} and {maxAllowed} for type {subjectType.Name}.");
				}
				maxAllowed = requested;
				ValidateAndNormalizeBounds(metadata.UnderlyingType.Signed, maxAllowed, metadata);
			}
			// No explicit Bits: follow existing inference / policy logic
			else if (attr?.HasInferBits ?? _options.AutoInferBits) // if InferBits is true or if it's null and AutoInferBits is true
			{
				ValidateAndNormalizeBounds(metadata.UnderlyingType.Signed, maxAllowed, metadata);
				maxAllowed = InferBitsFromBounds(metadata, _options.MaxInferredBits);
			}
			else
			{
				throw new SerializationException($"Field '{name}': Bits not specified and global policy forbids inference.");
			}
			metadata.ResolvedBits = maxAllowed;
		}
		public static (long? valueSigned, ulong? valueUnsigned) ValidateDefaultValue(FieldMetadata metadata, object? value)
		{
			if(value == null) return (null, null); // nothing to validate

			bool signed = metadata.UnderlyingType.Signed;

			if (signed)
			{
				long min = metadata.ResolvedMinSigned;
				long max = metadata.ResolvedMaxSigned;

				// Extract the value as a signed 64-bit integer
				long valAsSigned = value switch
				{
					sbyte sb => sb,
					short s => s,
					int i => i,
					long l => l,
					byte b => b,
					ushort us => us,
					uint ui => ui,
					char c => c,
					bool bl => bl ? 1L : 0L,
					ulong ul when ul <= (ulong)long.MaxValue => (long)ul,
					_ => throw new SerializationException($"Field {metadata.Name}: Value {value} is an unsupported type ({value.GetType()}) invalid for signed bounds.")
				};

				// Compare against the guaranteed signed bounds
				if (valAsSigned < min || valAsSigned > max)
				{
					throw new SerializationException($"Field {metadata.Name}: Value {value} is out of signed bounds ({min}..{max}).");
				}
				return (valAsSigned, null);
			}
			else
			{
				ulong min = metadata.ResolvedMinUnsigned;
				ulong max = metadata.ResolvedMaxUnsigned;

				// Extract the value as an unsigned 64-bit integer
				ulong valAsUnsigned = value switch
				{
					byte b => b,
					ushort us => us,
					uint ui => ui,
					ulong ul => ul,
					char c => c,
					bool bl => bl ? 1UL : 0UL,
					sbyte sb when sb >= 0 => (ulong)sb,
					short s when s >= 0 => (ulong)s,
					int i when i >= 0 => (ulong)i,
					long l when l >= 0 => (ulong)l,
					_ => throw new SerializationException($"Field {metadata.Name}: Value {value} is negative or unsupported type ({value.GetType()}) invalid for unsigned bounds.")
				};

				// Compare against the guaranteed unsigned bounds
				if (valAsUnsigned < min || valAsUnsigned > max)
				{
					throw new SerializationException($"Field {metadata.Name}: Value {value} is out of unsigned bounds ({min}..{max}).");
				}
				return (null, valAsUnsigned);
			}
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
			bool allowNonPublic = attr.HasAllowNonPublicAccess ? attr.AllowNonPublicAccess : (_options.AllowNonPublicAccess ?? false);
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

		/// <summary>
		///  Helper for InferBitsFromBounds, used to figure out how many bits would be required for a value.
		///  
		/// </summary>
		/// <param name="v"></param>
		/// <returns></returns>
		private static int InferBitsFromBounds(
			FieldMetadata metadata,
			int maxAllowed) // nativeWidth in bits, if available
		{
			bool signed = metadata.UnderlyingType.Signed;
			ulong MagnitudeCandidate(long? v) => (ulong)((v < 0 ? ~v : v) ?? 0);

			ulong minAbs = signed ? MagnitudeCandidate(metadata.ResolvedMinSigned) : metadata.ResolvedMinUnsigned;
			ulong maxAbs = signed ? MagnitudeCandidate(metadata.ResolvedMaxSigned) : metadata.ResolvedMaxUnsigned;
			// If both are zero, 1 bit is enough (-0..0 in two's complement)
			if (minAbs == 0 && maxAbs == 0)
			{
				return 1;
			}

			ulong max = maxAbs > minAbs ? maxAbs : minAbs;

			int required = BitOperations.Log2(max) + 1 + (signed ? 1 : 0);

			if (required > maxAllowed)
			{
				throw new SerializationException($"Inferred bits {required} exceed allowed maximum {maxAllowed}.");
			}

			return required;
		}

		private static void ValidateAndNormalizeBounds(bool signed, int width, FieldMetadata metadata)
		{
			// Local helper function to deduplicate the check-and-throw logic
			T Validate<T>(T? input, T nativeMin, T nativeMax, bool isMinimum/*string typeLabel*/) where T : struct, IComparable<T>
			{
				T finalValue = input ?? (isMinimum?nativeMin:nativeMax);


				if (isMinimum && finalValue.CompareTo(nativeMin) < 0)
					throw new SerializationException($"Field '{metadata.Name}': Min value ({finalValue}) is less than what fits in width ({nativeMin}).");
				if (!isMinimum && finalValue.CompareTo(nativeMax) > 0)
					throw new SerializationException($"Field '{metadata.Name}': Max value ({finalValue}) is greater than what fits in width ({nativeMax}).");

				return finalValue;
			}
			var bounds = metadata.BitFieldBounds;
			var signedMin = bounds.HasMin && bounds.IsSigned ? (long?)bounds.SignedMin : null;
			var unsignedMin = bounds.HasMin && !bounds.IsSigned ? (ulong?)bounds.UnsignedMin : null;
			var signedMax = bounds.HasMax && bounds.IsSigned ? (long?)bounds.SignedMax : null;
			var unsignedMax = bounds.HasMax && !bounds.IsSigned ? (ulong?)bounds.UnsignedMax : null;
			if (signed)
			{
				long nativeMin = -1L << (width - 1);
				long nativeMax = (1L << (width - 1)) - 1;

				// if it's a signed number, unsignedMin/Max is not allowed
				metadata.ResolvedMinSigned = Validate(signedMin, nativeMin, nativeMax, true);
				metadata.ResolvedMaxSigned = Validate(signedMax, nativeMin, nativeMax, false);
			}
			else
			{
				ulong nativeMin = 0;
				ulong nativeMax = (1UL << width) - 1;

				// if it's an unsigned number, signedMin/Max have been verified to be non-negative
				metadata.ResolvedMinUnsigned = Validate(unsignedMin ?? (ulong?) signedMin, nativeMin, nativeMax, true);
				metadata.ResolvedMaxUnsigned = Validate(unsignedMax ?? (ulong?) signedMax, nativeMin, nativeMax, false);
			}
		}
	}
}
