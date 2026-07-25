using GgoSoft.Storage;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

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
						let bitField = MetadataEngine.Hydrate(p)
						let levels = MetadataEngine.HydrateMultiple<BitFieldLevelAttribute>(p)
						where bitField != null || levels.Any()
						orderby bitField.HasOrder ? bitField.Order : p.MetadataToken, p.MetadataToken
						select BuildFieldMetadata(p, bitField, levels.ToArray());
			//var props = from p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			//			let attr = p.GetCustomAttribute<BitFieldAttribute>()
			//			where attr != null
			//			orderby attr.OrderNullable ?? p.MetadataToken, p.MetadataToken
			//			select BuildFieldMetadata(p, attr); // new { Prop = p, Attr = attr };
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
				//LevelTypes = [] // TODO: fill this in
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
			//var (parsedMin, parsedMax) = ParseBounds(attr, metadata);

			if (metadata.UnderlyingType.CustomBitSerializable is null)
			{
				ResolveResolvedBits(metadata);// name, prop, attr, parsedMin, parsedMax, elemType);
			}
			// 3) Enumerable-specific rules
			if (metadata.TypeResolution[0].IsEnumerable)
			{
				if (attr.OmitIfEquals != null)
				{
					throw new SerializationException($"Field '{metadata.Name}': Default is not allowed with an enumerator.");
				}
				ValidateEnumerableRules(metadata);
			}

			// 4) Decide resolvedBits (inference, policy, converters, custom types)

			// 5) Compute representable ranges and validate provided bounds
			//var (signedMin, signedMax) = SignedRange(resolvedBits);
			//var unsignedMax = UnsignedMax(resolvedBits);
			//bool hasRange = true;
			//ValidateBoundsAgainstBits(name, attr, parsedMin, parsedMax, resolvedBits);

			// 6) Default value validation (assignability + range)
			ValidateDefaultValue(metadata, metadata.ResolvedDefaultValue);// name, attr, propertyType, resolvedBits);

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
			var bounds = metadata.BitFieldBounds ?? throw new InvalidOperationException("missing bounds");
			var prop = metadata.Property ?? throw new InvalidOperationException("missing property");
			// --- Mutual exclusivity and obvious conflicts ---
			if (attr.HasBits && attr.InferBits)
				Fail("Bits cannot be specified when InferBits is true.");

			//if (attr.HasCountBitLength && attr.HasTerminatorValue)
			//	Fail("for IEnumerable<T> needs to provide exactly one of CountBitLength or TerminatorValue.");

			//if (bounds.HasMin && attr.HasUnsignedMin)
			//	Fail("Min and UnsignedMin are mutually exclusive.");

			//if (attr.HasMax && attr.HasUnsignedMax)
			//	Fail("Max and UnsignedMax are mutually exclusive.");

			if (metadata.UnderlyingType.Signed)
			{
				//if (attr.HasUnsignedMin || attr.HasUnsignedMax)
				//	Fail("Signed=true cannot be used together with UnsignedMin/UnsignedMax.");

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
				// CountBitLength only makes sense for enumerables
				//if (attr.HasCountBitLength)
				//	Fail("CountBitLength is only valid for enumerable fields.");

				// TerminatorValue only makes sense for enumerables
				if (bounds.HasTerminator)
					Fail("TerminatorValue is only valid for enumerable fields.");
			}
			// CountBitLength range check (cheap)
			//if (attr.HasCountBitLength && (attr.CountBitLength < 1 || attr.CountBitLength > 32))
			//	Fail("CountBitLength must be in range 1..32.");

			// Default conflicts with TerminatorValue (collection-level terminator semantics)
			if (attr.OmitIfEquals != null && bounds.HasTerminator)
				Fail("Default is not allowed when TerminatorValue is used (terminator semantics conflict).");

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
				//if (attr.HasCountBitLength || attr.HasTerminatorValue)
				//	Fail("CountBitLength and TerminatorValue cannot be used with a custom serializer.");
				// Default allowed only if documented; otherwise reject
				if (attr.OmitIfEquals != null) Fail("Default is not allowed with a custom serializer unless the serializer documents support.");
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

		private static readonly ConcurrentDictionary<Type, TypeResolution> _typeResolutionCache = new();
		private static readonly ConcurrentDictionary<Type, Type?> _elementTypeCache = new();

		public static bool TryGetEnumerableElementType([NotNullWhen(true)]Type? type, [NotNullWhen(true)] out Type? elementType)
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

		public TypeResolution? TryGetPrimitiveWidth(string fieldName, Type fieldType, BitFieldAttribute attr, int depth, BitFieldLevelAttribute?[] levels)	
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

			//if (depth == 0 && levels.Length != 0)
			//{
			//	throw new SerializationException($"A primitive type cannot have BitFieldLevel attributes in {fieldName}");
			//}
			if(depth < levels.Length)
			{
				throw new SerializationException($"Depth of BitFieldLevel attributes must match the nesting depth of the type. Expected depth {depth}, but found {levels.Length} in {fieldName}.");
			}

			// determine effective signedness
			bool effectiveSigned = attr.HasSigned ? attr.Signed : (_options.Signed ?? nativeSigned);
				//attr.SignedNullable ??
				//_options.Signed ??
				//nativeSigned;

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
				LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, false),
			};
		}
		//public TypeResolution? TryGetPrimitiveWidth(Type fieldType, BitFieldAttribute attr)
		//{
		//	var underlying = Nullable.GetUnderlyingType(fieldType);
		//	if (fieldType.IsEnum)
		//	{
		//		var underlyingType = Enum.GetUnderlyingType(fieldType);
		//		return TryGetPrimitiveWidth(underlyingType, attr);
		//	}
		//	underlying ??= fieldType;
		//	if (underlying.IsEnum) underlying = Enum.GetUnderlyingType(underlying);
		//	(Type dataType, int width, bool signed, bool isPrimitive) returnValue = (typeof(void), 0, false, false);
		//	if (underlying == typeof(bool)) returnValue = (typeof(bool), 1, false, true);
		//	if (underlying == typeof(char)) returnValue = (typeof(char), 16, false, true);
		//	if (underlying == typeof(byte)) returnValue = (typeof(byte), 8, false, true);
		//	if (underlying == typeof(sbyte)) returnValue = (typeof(sbyte), 8, true, true);
		//	if (underlying == typeof(short)) returnValue = (typeof(short), 16, true, true);
		//	if (underlying == typeof(ushort)) returnValue = (typeof(ushort), 16, false, true);
		//	if (underlying == typeof(int)) returnValue = (typeof(int), 32, true, true);
		//	if (underlying == typeof(uint)) returnValue = (typeof(uint), 32, false, true);
		//	if (underlying == typeof(long)) returnValue = (typeof(long), 64, true, true);
		//	if (underlying == typeof(ulong)) returnValue = (typeof(ulong), 64, false, true);

		//	if (returnValue.isPrimitive)
		//	{
		//		bool nativeSigned = returnValue.signed;
		//		int nativeWidth = returnValue.width;
		//		if (attr.SignedNullable.HasValue)
		//		{
		//			returnValue.signed = attr.SignedNullable.Value;
		//		}
		//		else if (_options.Signed.HasValue)
		//		{
		//			returnValue.signed = _options.Signed.Value;
		//		}
		//		else
		//		{
		//			returnValue.signed = nativeSigned;
		//		}
		//		int width = nativeWidth;
		//		if (!returnValue.signed && nativeSigned)
		//		{
		//			width--;
		//		}
		//		return new TypeResolution(returnValue.dataType, nativeWidth, width, false, returnValue.isPrimitive, /*isNullable, false,*/ nativeSigned, returnValue.signed, null, null);
		//	}
		//	return null;
		//}
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
		public TypeResolution[] ResolveTypeInfo(PropertyInfo prop, /*string fieldName, Type type,*/ BitFieldAttribute attr, BitFieldLevelAttribute[] levels) // TODO: maxAllowedBits should be added here
		{
			string fieldName = prop.Name;
			Type type = prop.PropertyType;
			int depth = 0;
			var normalizedLevels = NormalizeBitLevels(fieldName, levels);
			// Fast path for primitives
			var typeResolution = TryGetPrimitiveWidth(fieldName, type, attr, depth, normalizedLevels);
			if (typeResolution is not null)
			{
				//typeResolutionResult.Add(typeResolution);
				return [typeResolution];
			}
			// Parse attribute metadata (already using your new parser)
			//var attr = type.GetCustomAttribute<YourAttribute>();
			//bool?[] optionalByDepth = ParseElementNullBoolList(attr.ElementOptional);
			//if(optionalByDepth.Length > 0)
			//{
			//	optionalByDepth[0] = attr.Optional;
			//}
			//string?[] condPropByDepth = ParseElementStringList(attr.ElementConditionalProperty);
			//string?[] condTypeByDepth = ParseElementStringList(attr.ElementConditionalType);
			bool isNullable = IsNullable(prop);
			List<TypeResolution> typeResolutionResult = [];
			_ = ResolveTypeInfoCore(
				fieldName,
				type,
				depth: 0,
				//optionalByDepth,
				//condPropByDepth,
				//condTypeByDepth,
				attr,
				normalizedLevels,
				typeResolutionResult,
				isNullable
			//out _
			);
			typeResolutionResult.Reverse();
			return typeResolutionResult.ToArray();
			//return ResolveTypeInfoCore(
			//	fieldName,
			//	type,
			//	depth: 0,
			//	//optionalByDepth,
			//	//condPropByDepth,
			//	//condTypeByDepth,
			//	attr,
			//	normalizedLevels,
			//	typeResolutionResult
			//	//out _
			//);
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
		//public static int[] ParseElementIntList(string? input) => ParseElementList(input, default, int.Parse);
		//public static bool?[] ParseElementNullBoolList(string? input) => ParseElementList<bool?>(input, null, s=>bool.Parse(s));
		//public static bool[] ParseElementBoolList(string? input) => ParseElementList(input, default, bool.Parse);
		//public static string?[] ParseElementStringList(string? input) => ParseElementList(input, default, s => s);
		//public static T[] ParseElementList<T>(string? input, T defaultValue, Func<string, T> parseValue)
		//{
		//	if (string.IsNullOrWhiteSpace(input))
		//		return [];

		//	var tokens = input.Split(',');
		//	var list = new List<T>();

		//	int positionalDepth = 1;

		//	foreach (var raw in tokens)
		//	{
		//		var token = raw;

		//		if (token.Length == 0)
		//		{
		//			positionalDepth++;
		//			continue;
		//		}

		//		// keyed entry: N:value
		//		var parts = token.Split(':', 2);

		//		if (parts.Length == 2)
		//		{
		//			if (!int.TryParse(parts[0], out int depth) || depth < 1)
		//				throw new FormatException($"Invalid depth index '{parts[0]}' in '{token}'.");
		//			if (depth < positionalDepth)
		//			{
		//				throw new FormatException($"Depth ({depth}) cannot be less than the current index ({positionalDepth})");
		//			}
		//			positionalDepth = depth;
		//			token = parts[1];
		//		}
		//		while (list.Count <= positionalDepth)
		//		{
		//			list.Add(defaultValue);
		//		}
		//		list[positionalDepth] = parseValue(token);
		//		positionalDepth++;
		//	}

		//	return [.. list];
		//}

		//public static EnumerableFraming[] ParseCountTerminator(string?[] values, int maxCountBits = 31)
		//{
		//	if(maxCountBits <= 0)
		//	{
		//		throw new ArgumentException("Value must be greater than 0", nameof(maxCountBits));
		//	}
		//	if (values.Length == 0) return [];
		//	if (values.Length == 1) throw new SerializationException($"Unknown error, Count/terminator length is 1: {values[1]}");
		//	EnumerableFraming[] returnValue = new EnumerableFraming[values.Length];
		//	for (int depth = 1; depth < values.Length; depth++)
		//	{
		//		string? entry = values[depth];
		//		if(entry == null)
		//		{
		//			returnValue[depth] = new () {Depth = depth};
		//			continue;
		//		}
		//		long? countValue = null;
		//		long? termValue = null;
		//		long? escValue = null;
		//		string[] pairs = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		//		foreach (string pair in pairs)
		//		{
		//			// 2. Split into key/value and automatically trim whitespace around the equals sign
		//			string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
		//			if(parts.Length != 2)
		//			{
		//				throw new SerializationException($"Depth {depth}: invalid attribute in {entry}: {pair}");
		//			}
		//			if (!Helpers.TryParseLargeNumberToBitwiseLong(parts[1], out long value))
		//			{
		//				throw new FormatException($"Depth {depth}: invalid number for '{parts[0]}' in '{pair}'.");
		//			}
		//			var key = parts[0];
		//			if(string.Equals(key, "count", StringComparison.OrdinalIgnoreCase))
		//			{
		//				if(countValue != null)
		//				{
		//					throw new SerializationException($"Depth {depth}: Duplicate 'count' ({countValue}) already specified in {entry}");
		//				}
		//				if(value < 1 || value > maxCountBits)
		//				{
		//					throw new SerializationException($"Depth {depth}: count ({value}) value must be greater than 0 and less than or equal to {maxCountBits}");
		//				}
		//				countValue = value;
		//			} else if(string.Equals(key, "term", StringComparison.OrdinalIgnoreCase))
		//			{
		//				if (termValue != null)
		//				{
		//					throw new SerializationException($"Depth {depth}: duplicate 'term' ({termValue}) already specified in {entry}");
		//				}
		//				termValue = value;
		//			}
		//			else if (string.Equals(key, "esc", StringComparison.OrdinalIgnoreCase))
		//			{
		//				if (escValue != null)
		//				{
		//					throw new SerializationException($"Depth {depth}: duplicate 'esc' ({countValue}) already specified in {entry}");
		//				}

		//				escValue = value;
		//			} else
		//			{
		//				throw new SerializationException($"Depth {depth}: unknown parameter '{key}' in {entry}");
		//			}
		//		}
		//		if (countValue is not null && (termValue is not null || escValue is not null))
		//		{
		//			throw new SerializationException($"Depth {depth}: count cannot be used with term or esc in {entry}");
		//		}
		//		if (escValue is not null)
		//		{
		//			if (termValue is null)
		//			{
		//				throw new SerializationException($"Depth {depth}: 'esc' requires 'term' to be present in {entry}");
		//			}
		//			if (escValue == termValue)
		//			{
		//				throw new SerializationException($"Depth {depth}: esc value must differ from term value in {entry}");
		//			}
		//		}
		//		if(countValue is null && termValue is null)
		//		{
		//			throw new SerializationException($"Depth {depth}: term or count value must be present");
		//		}
		//		returnValue[depth] = new()
		//		{
		//			CountBitLength = (int?)countValue,
		//			Depth = depth,
		//			EscapeValue = escValue,
		//			Mode = countValue != null?FramingMode.Count:FramingMode.Terminator,
		//			TerminatorValue = termValue
		//		};
		//	}
		//	return returnValue;
		//}
//		public static bool TryParseLargeNumberToBitwiseLong(string input, out long result)
//{
//    // ulong handles the entire range from 0 up to ulong.MaxValue
//    if (ulong.TryParse(input, out ulong ulongValue))
//    {
//        // unchecked allows the bitwise conversion even if it exceeds long.MaxValue
//        result = unchecked((long)ulongValue);
//        return true;
//    }

//    // If it fails ulong parsing, check if it's a valid negative standard long
//    if (long.TryParse(input, out result))
//    {
//        return true;
//    }

//    result = 0;
//    return false;
//}
		private TypeResolution? ResolveTypeInfoCore(
			string fieldName,
			Type type,
			int depth,
			//bool?[] optionalByDepth,
			//string?[] condPropByDepth,
			//string?[] condTypeByDepth,
			BitFieldAttribute attr,
			BitFieldLevelAttribute?[] levels,
			List<TypeResolution> typeResolutionResult,
			bool? isNullable = null)//,
			//out TypeResolution? underlyingType)
		{
			// Determine optional/conditional values for this depth
			var level = GetAtDepth(levels, depth);
			//string? condProp = GetAtDepth(levels, depth);
			//string? condType = GetAtDepth(levels, depth);

			// If this type is an enumerable, recurse into its element type
			if (TryGetEnumerableElementType(type, out Type? elementType))
			{

				_ = ResolveTypeInfoCore(
					fieldName,
					elementType,
					depth + 1,
					//optionalByDepth,
					//condPropByDepth,
					//condTypeByDepth,
					attr,
					levels,
					typeResolutionResult
					//out underlyingType
				);
				var returnValue = new TypeResolution
				{
					FieldType = type,
					//EnumerableElement = elementResolution,
					Width = null,
					IsEnumerable = true,
					LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, true),
					IsNullable = isNullable,
					IsString = type == typeof(string)
					//EnumerableOptional = level?.HasOptional==true?level.Optional:null,
					//EnumerableConditionalProperty = condProp,
					//EnumerableConditionalType = condType,
					//UnderlyingType = underlyingType
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
					LevelTypeResolution = LevelTypeResolution.Map(fieldName, level, attr, false)
				};
			}
			else
			{
				underlyingType = TryGetPrimitiveWidth(fieldName, type, attr, depth, levels);
				if (underlyingType == null)
				{
					throw new SerializationException($"Unsupported type '{type.FullName}' at depth {depth} in field '{fieldName}'. Only primitive types and enumerables of primitive types are supported.");
				}
			}
			typeResolutionResult.Add(underlyingType);
			return underlyingType;
			//if (TryGetPrimitiveWidth(type, attr))
			//{
			//	return new TypeResolution { 
			//		EnumerableElement = null,
			//		Width = width,
			//		IsEnumerable = false,
			//		EnumerableOptional = optional,
			//		EnumerableConditionalProperty = condProp,
			//		EnumerableConditionalType = condType
			//	};
			//}

			// Unsupported type
//			return null;
		}
		private static T? GetAtDepth<T>(T?[] arr, int depth)
		{
			if (depth < arr.Length)
				return arr[depth];
			return default;
		}
		//public TypeResolution ResolveTypeInfo(Type fieldType, long maxAllowedBits, BitFieldAttribute attr) // TODO: maxAllowedBits is wrong
		//{
		//	// Fast primitive/unwrapping checks first (cheap)
		//	var underlying = Nullable.GetUnderlyingType(fieldType);
		//	//var isNullable = (underlying != null); 
		//	underlying ??= fieldType;
		//	if (underlying.IsEnum) underlying = Enum.GetUnderlyingType(underlying);
		//	//int signedAdjustment = signed ? 0 : 1;
		//	(Type dataType, int width, bool signed, bool isPrimitive) returnValue = (typeof(void), 0, false, false);
		//	if (underlying == typeof(bool)) returnValue = (typeof(bool), 1, false, true);
		//	if (underlying == typeof(char)) returnValue = (typeof(char), 16, false, true);
		//	if (underlying == typeof(byte)) returnValue = (typeof(byte), 8, false, true);
		//	if (underlying == typeof(sbyte)) returnValue = (typeof(sbyte), 8, true, true);
		//	if (underlying == typeof(short)) returnValue = (typeof(short), 16, true, true);
		//	if (underlying == typeof(ushort)) returnValue = (typeof(ushort), 16, false, true);
		//	if (underlying == typeof(int)) returnValue = (typeof(int), 32, true, true);
		//	if (underlying == typeof(uint)) returnValue = (typeof(uint), 32, false, true);
		//	if (underlying == typeof(long)) returnValue = (typeof(long), 64, true, true);
		//	if (underlying == typeof(ulong)) returnValue = (typeof(ulong), 64, false, true);

		//	if (returnValue.width > 0)
		//	{
		//		bool nativeSigned = returnValue.signed;
		//		int nativeWidth = returnValue.width;
		//		if (attr.SignedNullable.HasValue)
		//		{
		//			returnValue.signed = attr.SignedNullable.Value;
		//		}
		//		else if (_options.Signed.HasValue)
		//		{
		//			returnValue.signed = _options.Signed.Value;
		//		}
		//		else
		//		{
		//			returnValue.signed = nativeSigned;
		//		}
		//		int width = nativeWidth;
		//		if (!returnValue.signed && nativeSigned)
		//		{
		//			width--;
		//		}
		//		return new TypeResolution(returnValue.dataType, nativeWidth, width, false, returnValue.isPrimitive, /*isNullable, false,*/ nativeSigned, returnValue.signed, null, null);
		//	}

		//	// Cached slow path for non-primitives
		//	return _typeResolutionCache.GetOrAdd(fieldType, t =>
		//	{
		//		Console.WriteLine(t.Name);
		//		// If it's an enumerable, resolve the element type info (recurses but uses cache)
		//		if (TryGetEnumerableElementType(t, out var elemType))
		//		{
		//			if (elemType == null)
		//				return new TypeResolution(null, null, null, true, false, /*false, false,*/ false, false, null, null); // non-generic IEnumerable -> unknown element

		//			// Resolve element info (calls back into ResolveTypeInfo but will hit cache for primitives)
		//			var elemInfo = ResolveTypeInfo(elemType, maxAllowedBits, attr);

		//			// If element width is unknown, caller should require a converter or explicit Bits
		//			return new TypeResolution(elemType, elemInfo.NativeWidth, elemInfo.Width, true, elemInfo.IsPrimitive, /*isNullable, elemInfo.IsNullable,*/ elemInfo.NativeSigned, elemInfo.Signed, null, null);
		//		}

		//		// Unknown/unsupported type
		//		return new TypeResolution(null, null, null, false, false, /*false, false,*/ false, false, null, null);
		//	});
		//}

		//private static (BigInteger? parsedMin, BigInteger? parsedMax) ParseBounds(BitFieldAttribute attr, FieldMetadata metadata)
		//{
		//	//BigInteger? parsedMin = null, parsedMax = null;
		//	if (attr.MinNullable.HasValue) parsedMin = new BigInteger(attr.MinNullable.Value);
		//	if (attr.MaxNullable.HasValue) parsedMax = new BigInteger(attr.MaxNullable.Value);
		//	if (attr.UnsignedMinNullable.HasValue) parsedMin = new BigInteger(attr.UnsignedMinNullable.Value);
		//	if (attr.UnsignedMaxNullable.HasValue) parsedMax = new BigInteger(attr.UnsignedMaxNullable.Value);
		//	return (parsedMin, parsedMax);
		//}

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
			//BigInteger min, max;
			var primitiveType = metadata.UnderlyingType;
			var subjectType = primitiveType?.FieldType;
			var name = metadata.Name;
			var attr = metadata.Attribute;
			int maxAllowed = primitiveType?.Width ?? throw new SerializationException("$Field '{name}': Width cannot be null");
			//bool nativeSigned = primitiveType?.NativeSigned == true;

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
				//(min, max) = TryGetMinMax(parsedMin, parsedMax, metadata.ResolvedSigned, maxAllowed, metadata);
			}
			// No explicit Bits: follow existing inference / policy logic
			else if (attr?.HasInferBits ?? _options.AutoInferBits) // if InferBits is true or if it's null and AutoInferBits is true
			{
				ValidateAndNormalizeBounds(metadata.UnderlyingType.Signed, maxAllowed, metadata);
//				(min, max) = TryGetMinMax(parsedMin, parsedMax, metadata.ResolvedSigned, maxAllowed, metadata);
				maxAllowed = InferBitsFromBounds(metadata, _options.MaxInferredBits);
			}
			else
			{
				throw new SerializationException($"Field '{name}': Bits not specified and global policy forbids inference.");
			}
			// metadata.ResolvedMin = min;
			// metadata.ResolvedMax = max;
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

		//private static void ValidateDefaultValue(FieldMetadata metadata) //string name, object? defaultValue, Type propertyType, BigInteger min, BigInteger max)
		//{
		//	if (metadata.Attribute?.OmitIfEquals == null) return; // nothing to validate

		//	BigInteger defaultBig;
		//	try
		//	{
		//		defaultBig = metadata.Attribute.OmitIfEquals switch
		//		{
		//			BigInteger bi => bi,
		//			bool b => b ? BigInteger.One : BigInteger.Zero,
		//			char c => new BigInteger((ulong)c),
		//			sbyte sb => new BigInteger(sb),
		//			byte bb => new BigInteger(bb),
		//			short ss => new BigInteger(ss),
		//			ushort us => new BigInteger(us),
		//			int ii => new BigInteger(ii),
		//			uint uii => new BigInteger(uii),
		//			long ll => new BigInteger(ll),
		//			ulong ull => new BigInteger(ull),

		//			// Optional: accept numeric strings
		//			string s => (metadata.ResolvedMin < 0)
		//				? new BigInteger(Convert.ToInt64(s, CultureInfo.InvariantCulture))
		//				: new BigInteger(Convert.ToUInt64(s, CultureInfo.InvariantCulture)),

		//			// Fallback for other convertible boxed values
		//			_ => (metadata.ResolvedMin < 0)
		//				? new BigInteger(Convert.ToInt64(metadata.Attribute.OmitIfEquals, CultureInfo.InvariantCulture))
		//				: new BigInteger(Convert.ToUInt64(metadata.Attribute.OmitIfEquals, CultureInfo.InvariantCulture))
		//		};
		//	}
		//	catch (Exception ex)
		//	{
		//		throw new SerializationException($"Field '{metadata.Name}': error converting Default value: {ex.Message}");
		//	}

		//	// simple range check (min and max are assumed valid and min < max)
		//	if (defaultBig < metadata.ResolvedMin || defaultBig > metadata.ResolvedMax)
		//		throw new SerializationException($"Field '{metadata.Name}': Default value ({defaultBig}) out of range {metadata.ResolvedMin}..{metadata.ResolvedMax}.");
		//}

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

		/// <summary>
		///  Helper for InferBitsFromBounds, used to figure out how many bits would be required for a value.
		///  
		/// </summary>
		/// <param name="v"></param>
		/// <returns></returns>
		private static int InferBitsFromBounds(
			//BigInteger min,
			//BigInteger max,
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
			//var attr = metadata.Attribute;
			var bounds = metadata.BitFieldBounds;
			var signedMin = bounds.HasMin && bounds.IsSigned ? (long?)bounds.SignedMin : null;// attr.MinNullable;
			var unsignedMin = bounds.HasMin && !bounds.IsSigned ? (ulong?)bounds.UnsignedMin : null;// attr.UnsignedMinNullable;
			var signedMax = bounds.HasMax && bounds.IsSigned ? (long?)bounds.SignedMax : null;// attr.MaxNullable;
			var unsignedMax = bounds.HasMax && !bounds.IsSigned ? (ulong?)bounds.UnsignedMax : null;// attr.UnsignedMaxNullable;
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

		//private static (BigInteger min, BigInteger max) TryGetMinMax(BigInteger? parsedMin, BigInteger? parsedMax, bool signed, int width, FieldMetadata metadata)
		//{
		//	if (width <= 0 || width > 1024) // sanity guard for absurd widths
		//	{
		//		throw new SerializationException($"Invalid width {width}.");
		//	}
		//	long? minSigned = null;
		//	long? maxSigned = null;
		//	ulong? minUnsigned = null;
		//	ulong? maxUnsigned = null;
		//	if(signed)
		//	{

		//	}

		//	BigInteger nativeMin = signed ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
		//	BigInteger nativeMax = signed ? (BigInteger.One << (width - 1)) - 1 : (BigInteger.One << width) - 1;

		//	BigInteger min = parsedMin ?? nativeMin;
		//	BigInteger max = parsedMax ?? nativeMax;
		//	if (min < nativeMin)
		//	{
		//		throw new SerializationException($"Field '{metadata.Name}': Min value ({min}) is less than what fits in width ({nativeMin}).");
		//	}
		//	if (max > nativeMax)
		//	{
		//		throw new SerializationException($"Field '{metadata.Name}': Max value ({max}) is greater than what fits in width ({nativeMax}).");
		//	}
		//	if (max < min)
		//	{
		//		throw new SerializationException($"Field '{metadata.Name}': Invalid bounds: max ({max}) is less than min ({min}).");
		//	}
		//	return (min, max);
		//}
	}
}
