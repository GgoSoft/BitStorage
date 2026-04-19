using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace GgoSoft.Serialize
{
	// -------------------------
	// Enums
	// -------------------------
	public enum ConditionCombine
	{
		And,
		Or
	}

	public enum ConditionEvaluationMode
	{
		Snapshot,
		Incremental
	}

	/// <summary>
	/// Marks a property as a bit-field for the serializer and provides schema hints.
	/// Use this attribute to declare explicit bit widths, signedness, bounds, converters,
	/// conditional inclusion, enumerable encoding hints, and other per-field metadata.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public sealed class BitFieldAttribute : Attribute
	{
		/// <summary>
		/// Explicit bit width for the field. When set, this value takes highest precedence
		/// and no inference will be performed for this field.
		/// Value must be between 1 and the number of bits appropriate for the field.  
		/// If field is enumerable, this applies to the element width.		
		/// </summary>
		public int? Bits { get; set; } = null;

		/// <summary>
		/// Interpret the stored bits as a signed two's complement value when reading.  This is
		/// required if <see cref="Bits"/> (or the inferred number of bits) is less than the native 
		/// width of the property type and you want to support negative values.
		/// </summary>
		public bool Signed { get; set; } = false;

		/// <summary>
		/// Optional signed minimum bound for the field. Use only for signed semantics
		/// or when you want to express a signed lower bound. Mutually exclusive with <see cref="UnsignedMin"/>.
		/// </summary>
		public long? Min { get; set; } = null;

		/// <summary>
		/// Optional signed maximum bound for the field. Use only for signed semantics
		/// or when you want to express a signed upper bound. Mutually exclusive with <see cref="UnsignedMax"/>.
		/// </summary>
		public long? Max { get; set; } = null;

		/// <summary>
		/// Optional unsigned minimum bound for the field. Use this to express bounds
		/// in the full 0..2^64-1 range. Mutually exclusive with <see cref="Min"/>.
		/// </summary>
		public ulong? UnsignedMin { get; set; } = null;

		/// <summary>
		/// Optional unsigned maximum bound for the field. Use this to express bounds
		/// in the full 0..2^64-1 range. Mutually exclusive with <see cref="Max"/>.
		/// </summary>
		public ulong? UnsignedMax { get; set; } = null;

		/// <summary>
		/// Per-field opt-in for bit-width inference.
		/// <list type="bullet">
		/// <item><description><c>true</c> — force inference for this field (<see cref="Bits"/> must not be set). 
		/// If bounds are present, infer from them; otherwise fall back to the native width of the 
		/// field type.</description></item>
		/// <item><description><c>false</c> — do not infer for this field; use <see cref="Bits"/> if present</description></item>
		/// <item><description><c>null</c> fall back on global policy</description></item>
		/// </list>
		/// Default is <c>false</c>.
		/// </summary>
		public bool? InferBits { get; set; } = null;

		/// <summary>
		/// When serializing enumerables, the number of bits used to encode the element count.
		/// Provide exactly one of <see cref="CountBitLength"/> or <see cref="TerminatorValue"/> for enumerable fields.
		/// Valid range: 1..32.
		/// </summary>
		public int? CountBitLength { get; set; } = null;

		/// <summary>
		/// When serializing enumerables with a terminator, the terminator element value (encoded using the element width).
		/// Provide exactly one of <see cref="CountBitLength"/> or <see cref="TerminatorValue"/>.
		/// </summary>
		public ulong? TerminatorValue { get; set; } = null;

		/// <summary>
		/// Optional name of another property on the same object used for simple conditional inclusion.
		/// The serializer or a condition implementation can use this to decide whether to include the field.
		/// If the other property is null, false, or empty, the current field will be excluded. This is a simple alternative 
		/// to implementing a full <see cref="IFieldCondition"/>.
		/// </summary>
		public string? ConditionalProperty { get; set; } = null;

		/// <summary>
		/// Optional condition type implementing <see cref="IFieldCondition"/>. When provided, the serializer
		/// will resolve and invoke the condition to decide inclusion. The type must implement <see cref="IFieldCondition"/>.
		/// </summary>
		public Type? ConditionalType { get; set; } = null;

		/// <summary>
		/// How multiple conditional checks are combined when both <see cref="ConditionalProperty"/> and <see cref="ConditionalType"/>
		/// (or multiple conditions) are present. Defaults to <see cref="ConditionCombine.And"/>.
		/// </summary>
		public ConditionCombine ConditionCombine { get; set; } = ConditionCombine.And;

		/// <summary>
		/// The evaluation mode for conditions: snapshot (evaluate against a snapshot of current values)
		/// or incremental (evaluate as values are processed). Defaults to <see cref="ConditionEvaluationMode.Snapshot"/>.
		/// </summary>
		public ConditionEvaluationMode ConditionMode { get; set; } = ConditionEvaluationMode.Snapshot;

		/// <summary>
		/// Optional converter type used to transform between the CLR property and the bit-level representation.
		/// The serializer will resolve this type (DI first, then Activator) and use it when present.
		/// </summary>
		public Type? ConverterType { get; set; } = null;

		/// <summary>
		/// Version number for the field schema. Consumers can use this to implement versioned converters or conditional logic.
		/// </summary>
		public int Version { get; set; } = 0;

		/// <summary>
		/// When true, the field is considered optional (presence may be encoded separately).
		/// Semantics are implementation-defined; the serializer may pack presence bits when configured.
		/// </summary>
		public bool Optional { get; set; } = false;

		/// <summary>
		/// Default value for the field used during deserialization when the field is omitted.
		/// The builder validates that this value is assignable to the property type and fits the resolved range.
		/// </summary>
		public object? Default { get; set; } = null;

		/// <summary>
		/// Optional ordering hint for fields. When not set, the builder uses the property's metadata token.
		/// Lower values are serialized earlier.
		/// </summary>
		public int? Order { get; set; } = null;

		/// <summary>
		/// Human-readable description for the field. Useful for generated documentation or diagnostics.
		/// </summary>
		public string? Description { get; set; } = null;

		/// <summary>
		/// When true, the builder may use non-public accessors for this property.
		/// Default: false.
		/// </summary>
		public bool? AllowNonPublicAccess { get; set; } = null;
	}

	/// <summary>
	/// Marker interface to opt a type into metadata-driven bit serialization.
	/// </summary>
	public interface IBitSerializable { }

	/// <summary>
	/// Optional custom serializer interface for types that want full control.
	/// </summary>
	public interface ICustomBitSerializable
	{
		void Serialize(BitStorage writer, SerializerContext context);
		void Deserialize(BitStorageReader reader, SerializerContext context);
	}

	public interface IBitConverter
	{
		void Write(object? value, BitStorage writer, SerializerContext ctx);
		object? Read(BitStorageReader reader, SerializerContext ctx, Type targetType);
	}

	// -------------------------
	// Conditions
	// -------------------------
	public interface IFieldCondition
	{
		bool Evaluate(object instance,
					  IReadOnlyDictionary<string, object?> currentValues,
					  FieldMetadata field,
					  ConditionEvaluationMode mode,
					  SerializerContext context);
	}

	/// <summary>
	/// Immutable metadata describing a single serializable field.
	/// Produced by <see cref="FieldMetadataBuilder"/> and consumed by the serializer at runtime.
	/// </summary>
	public sealed class FieldMetadata
	{
		///// <summary>
		///// The CLR name of the field/property.
		///// </summary>
		//public string Name { get; init; } = string.Empty;

		///// <summary>
		///// The declaring CLR type that owns the property.
		///// </summary>
		//public Type? DeclaringType { get; set; }

		///// <summary>
		///// The original <see cref="PropertyInfo"/> for the property. May be null in some synthetic scenarios.
		///// </summary>
		//public PropertyInfo? PropertyInfo { get; set; }

		///// <summary>
		///// The reflected <see cref="PropertyInfo"/> for the field.
		///// </summary>
		//public PropertyInfo Property { get; init; } = null!;

		///// <summary>
		///// Fast compiled getter delegate: (object instance) => object? value.
		///// </summary>
		//public Func<object, object?> Getter { get; init; } = null!;

		///// <summary>
		///// Fast compiled setter delegate: (object instance, object? value) => void.
		///// </summary>
		//public Action<object, object?> Setter { get; init; } = null!;

		///// <summary>
		///// True when the resolved getter is non-public (the builder had to bypass visibility).
		///// Useful for diagnostics and trimming/AOT guidance.
		///// </summary>
		//public bool GetterIsNonPublic { get; set; }

		///// <summary>
		///// True when the resolved setter is non-public (the builder had to bypass visibility).
		///// Useful for diagnostics and trimming/AOT guidance.
		///// </summary>
		//public bool SetterIsNonPublic { get; set; }

		///// <summary>
		///// Final resolved bit width used on the wire for this field.
		///// </summary>
		//public int ResolvedBits { get; init; }

		///// <summary>
		///// Whether the field is interpreted as signed two's-complement.
		///// </summary>
		//public bool Signed { get; init; }

		///// <summary>
		///// Whether the field is optional (presence may be encoded separately).
		///// </summary>
		//public bool Optional { get; init; }

		///// <summary>
		///// Default value to use when the field is omitted during deserialization.
		///// </summary>
		//public object? DefaultValue { get; init; }

		///// <summary>
		///// Ordering hint for serialization. Lower values are serialized earlier.
		///// </summary>
		//public int Order { get; init; }

		///// <summary>
		///// Parsed minimum bound as a <see cref="BigInteger"/>, if provided.
		///// </summary>
		//public BigInteger? ParsedMin { get; init; }

		///// <summary>
		///// Parsed maximum bound as a <see cref="BigInteger"/>, if provided.
		///// </summary>
		//public BigInteger? ParsedMax { get; init; }

		///// <summary>
		///// True when a representable numeric range has been computed for this field.
		///// </summary>
		//public bool HasRange { get; init; }

		///// <summary>
		///// Precomputed signed minimum (primitive) for quick checks.
		///// </summary>
		//public long SignedMin { get; init; }

		///// <summary>
		///// Precomputed signed maximum (primitive) for quick checks.
		///// </summary>
		//public long SignedMax { get; init; }

		///// <summary>
		///// Precomputed unsigned maximum (primitive) for quick checks.
		///// </summary>
		//public ulong UnsignedMax { get; init; }

		///// <summary>
		///// The CLR property type.
		///// </summary>
		//public Type PropertyType { get; init; } = null!;

		///// <summary>
		///// True if the property is an enumerable (array or IEnumerable&lt;T&gt;), false otherwise.
		///// </summary>
		//public bool IsEnumerable { get; init; }

		///// <summary>
		///// Element type for enumerable properties; null for non-enumerables.
		///// </summary>
		//public Type? ElementType { get; init; }

		///// <summary>
		///// Resolved condition instance used to decide inclusion at runtime, if any.
		///// </summary>
		//public IFieldCondition? ConditionInstance { get; init; }

		///// <summary>
		///// Resolved converter instance (boxed) used to transform values to/from bit representation, if any.
		///// The serializer should cast this to the expected converter interface before use.
		///// </summary>
		//public object? ConverterInstance { get; init; }

		///// <summary>
		///// Optional human-readable description for the field.
		///// </summary>
		//public string? Description { get; init; }

		///// <summary>
		///// Checks whether the provided CLR value fits the precomputed representable range.
		///// Returns true when no range is defined.
		///// </summary>
		///// <param name="value">The CLR value to validate.</param>
		///// <returns>True if the value fits the range or no range is defined; false otherwise.</returns>
		//public bool ValueFitsRange(object? value)
		//{
		//	if (!HasRange) return true;
		//	if (value == null) return false;

		//	if (Signed)
		//	{
		//		try
		//		{
		//			long v = Convert.ToInt64(value, CultureInfo.InvariantCulture);
		//			return v >= SignedMin && v <= SignedMax;
		//		}
		//		catch
		//		{
		//			return false;
		//		}
		//	}
		//	else
		//	{
		//		try
		//		{
		//			ulong uv = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
		//			return uv <= UnsignedMax;
		//		}
		//		catch
		//		{
		//			return false;
		//		}
		//	}
		//}

		// identity
		public string Name { get; init; } = null!;
		public PropertyInfo Property { get; init; } = null!;

		// raw attribute (single source of truth for user-supplied config)
		public BitFieldAttribute Attribute { get; init; } = null!;

		// structural flags (precheck)
		//public bool IsEnumerable { get; set; }
		//public Type? ElementType { get; set; }
		public TypeResolution TypeResolution { get; set; }
		public int Width
		{
			get
			{
				return TypeResolution.Width ?? throw new SerializationException($"Field '{Name}': TypeWidth cannot be null");
			}
		}
		public Type ElementType
		{
			get
			{
				return TypeResolution.ElementType ?? throw new SerializationException($"Field '{Name}': Cannot resolve type to valid data type");
			}
		}
		public bool IsCustomSerializer { get; set; }

		// resolved values (what runtime actually uses)
		public int ResolvedOrder { get; set; }                 // computed once
		public int? ResolvedBits { get; set; }                 // final bit width (element or scalar)
		public bool ResolvedSigned { get; set; }               // final signedness
		public BigInteger? ResolvedMin { get; set; }           // normalized bounds
		public BigInteger? ResolvedMax { get; set; }
		public int? ResolvedCountBitLength { get; set; }       // enumerable length encoding
		public ulong? ResolvedTerminatorValue { get; set; }    // element terminator (if any)

		// converter/Contitional / runtime hooks
		public Type? ResolvedConverterType { get; set; }       // type to instantiate at runtime
		public IBitConverter? ResolvedConverterInstance { get; set; } // optional DI-resolved instance

		public Type? ResolvedConditionalType { get; set; }
		public IFieldCondition? ResolvedConditionalInstance { get; set; } // optional DI-resolved instance

		// runtime hints (only if normalized)
		public object? ResolvedDefaultValue { get; set; }      // normalized to property type
		public bool ResolvedOptional { get; set; }             // final presence semantics

		// diagnostics
		public string? ValidationNote { get; set; }
		/// <summary>
		/// Human-friendly representation for diagnostics and logging.
		/// </summary>
		public override string ToString()
		{
			return $"{Name} ({Property.Name}) bits={ResolvedBits} signed={Attribute.Signed} optional={Attribute.Optional}";
		}

		/// <summary>
		/// Pre-resolved accessors for the property. The builder can populate this when AllowNonPublicAccess is enabled
		/// </summary>
		public Accessors Accessors { get; set; }
	}
	/// <summary>
	/// Immutable container holding metadata for a CLR type used by the serializer.
	/// Produced by <see cref="FieldMetadataBuilder"/> and retrieved from an <see cref="IMetadataCache"/>.
	/// </summary>
	public sealed class TypeMetadata
	{
		/// <summary>
		/// The CLR <see cref="Type"/> that this metadata describes.
		/// </summary>
		public Type Type { get; init; } = null!;

		/// <summary>
		/// Ordered, immutable list of <see cref="FieldMetadata"/> entries describing the serializable fields.
		/// The order reflects the serialization order (lower <see cref="FieldMetadata.Order"/> values appear earlier).
		/// </summary>
		public IReadOnlyList<FieldMetadata> FieldsInOrder { get; init; } = Array.Empty<FieldMetadata>();

		/// <summary>
		/// True when the type has at least one serializable field described in <see cref="FieldsInOrder"/>.
		/// </summary>
		public bool HasFields => FieldsInOrder.Count > 0;
	}

	public struct Accessors
	{
		public Func<object, object?> Getter { get; init; }
		public Action<object, object?> Setter { get; init; }
		public bool GetterIsPublic { get; init; }
		public bool SetterIsPublic { get; init; }
	}

	/// <summary>
	/// Global configuration controlling serializer behavior, safety limits, and policy defaults.
	/// Instances of this class are provided to <see cref="FieldMetadataBuilder"/>, <see cref="SerializerContext"/>,
	/// and other components to influence preflight validation and runtime behavior.
	/// </summary>
	public sealed class SerializerOptions
	{
		/// <summary>
		/// When true, schema or runtime violations are treated as fatal and will throw
		/// <see cref="SerializationException"/>. When false, non-fatal issues are recorded
		/// in the configured <see cref="IDiagnosticsCollector"/> instead.
		/// Default: <c>true</c>.
		/// </summary>
		public bool Strict { get; set; } = true;

		/// <summary>
		/// Global policy used when a field does not explicitly opt into or out of inference.
		/// Controls whether the builder should infer bit widths from declared bounds for
		/// non-annotated fields or when an attribute explicitly disables per-field inference.
		/// Default: <c>false</c>.
		/// </summary>
		public bool AutoInferBits { get; set; } = false;

		/// <summary>
		/// Upper bound on the number of bits the builder is allowed to infer for a single field.
		/// Used to prevent runaway inference or accidental extremely wide fields.
		/// Valid range is typically 1..64. Default: <c>64</c>.
		/// </summary>
		public int MaxInferredBits { get; set; } = 64;

		/// <summary>
		/// Safety limit for the number of elements allowed in an enumerable during serialization/deserialization.
		/// Implementations should enforce this to avoid unbounded memory or CPU usage when reading untrusted data.
		/// Default: <c>1_000_000</c>.
		/// </summary>
		public int MaxEnumerableElements { get; set; } = 1_000_000;

		/// <summary>
		/// When true, the serializer may pack presence bits for optional fields to reduce wire size.
		/// The exact packing strategy is implementation-defined; set to false to disable presence packing.
		/// Default: <c>false</c>.
		/// </summary>
		public bool PackPresenceBits { get; set; } = false;

		/// <summary>
		/// Global cap on the total number of bits that may be produced for a single top-level object.
		/// Implementations should use this to guard against pathological or malicious inputs.
		/// Default: <c>1_000_000 * 8L</c> (eight million bits).
		/// </summary>
		public long MaxBitsPerObject { get; set; } = 1_000_000 * 8L;

		/// <summary>
		/// Optional service provider used to resolve condition and converter instances during preflight
		/// and at runtime. The builder and serializer will consult this provider before falling back to
		/// <c>Activator.CreateInstance</c>.
		/// </summary>
		public IServiceProvider? Services { get; set; } = null;

		/// <summary>
		/// When true, the builder may use non-public accessors when a field opts in.
		/// Default: false.
		/// </summary>
		public bool? AllowNonPublicAccess { get; set; } = null;
	}

	/// <summary>
	/// Thread-safe cache abstraction that provides <see cref="TypeMetadata"/> for CLR types
	/// and allows lookup of individual <see cref="FieldMetadata"/> entries by name.
	/// Implementations should avoid expensive reflection on every call and typically cache results.
	/// </summary>
	public interface IMetadataCache
	{
		/// <summary>
		/// Get the <see cref="TypeMetadata"/> for the specified <paramref name="type"/>.
		/// Implementations may return null if metadata cannot be produced.
		/// </summary>
		/// <param name="type">The CLR type to retrieve metadata for.</param>
		/// <returns>The <see cref="TypeMetadata"/> instance or null when metadata cannot be produced.</returns>
		TypeMetadata? GetTypeMetadata(Type type);

		/// <summary>
		/// Try to get a single <see cref="FieldMetadata"/> for a given <paramref name="type"/> and <paramref name="fieldName"/>.
		/// </summary>
		/// <param name="type">The CLR type that owns the field.</param>
		/// <param name="fieldName">The CLR property name to look up.</param>
		/// <param name="field">When the method returns, contains the <see cref="FieldMetadata"/> if found; otherwise null.</param>
		/// <returns><c>true</c> if the field metadata was found; otherwise <c>false</c>.</returns>
		bool TryGetField(Type type, string fieldName, out FieldMetadata? field);
	}

	/// <summary>
	/// Collector interface for recording diagnostics (warnings and non-fatal errors)
	/// produced during preflight or runtime when <see cref="SerializerOptions.Strict"/> is false.
	/// Implementations may store entries in memory, forward them to logging, or aggregate them for tooling.
	/// </summary>
	public interface IDiagnosticsCollector
	{
		/// <summary>
		/// Record a non-fatal warning message associated with an optional <see cref="FieldMetadata"/>.
		/// </summary>
		/// <param name="message">Human-readable diagnostic message.</param>
		/// <param name="field">Optional field metadata related to the warning.</param>
		void AddWarning(string message, FieldMetadata? field = null);

		/// <summary>
		/// Record a non-fatal error message associated with an optional <see cref="FieldMetadata"/>.
		/// When running in strict mode callers may instead throw <see cref="SerializationException"/>.
		/// </summary>
		/// <param name="message">Human-readable diagnostic message.</param>
		/// <param name="field">Optional field metadata related to the error.</param>
		void AddError(string message, FieldMetadata? field = null);

		/// <summary>
		/// Retrieve all collected diagnostic entries in chronological order.
		/// </summary>
		/// <returns>Read-only list of <see cref="DiagnosticEntry"/> instances.</returns>
		IReadOnlyList<DiagnosticEntry> GetAll();
	}

	/// <summary>
	/// Single diagnostic record produced by the serializer or metadata builder.
	/// Contains a timestamp, message, optional field name, and a flag indicating whether it is an error.
	/// </summary>
	public sealed class DiagnosticEntry
	{
		/// <summary>
		/// UTC timestamp when the diagnostic was created.
		/// </summary>
		public DateTime Timestamp { get; init; } = DateTime.UtcNow;

		/// <summary>
		/// Human-readable diagnostic message.
		/// </summary>
		public string Message { get; init; } = string.Empty;

		/// <summary>
		/// Optional CLR field/property name associated with the diagnostic.
		/// </summary>
		public string? FieldName { get; init; }

		/// <summary>
		/// True when this entry represents an error; false for warnings.
		/// </summary>
		public bool IsError { get; init; }
	}


	/// <summary>
	/// Simple in-memory implementation of <see cref="IDiagnosticsCollector"/> suitable for tests and tooling.
	/// Stores entries in a list and returns them via <see cref="GetAll"/>.
	/// This implementation is intentionally lightweight; if you require concurrent writers from multiple threads,
	/// wrap calls to <see cref="AddWarning"/> and <see cref="AddError"/> with external synchronization or replace
	/// the internal storage with a concurrent collection.
	/// </summary>
	public sealed class InMemoryDiagnosticsCollector : IDiagnosticsCollector
	{
		private readonly List<DiagnosticEntry> _entries = new();

		/// <summary>
		/// Record a non-fatal warning message associated with an optional field.
		/// The entry timestamp is set to UTC now.
		/// </summary>
		/// <param name="message">Human-readable diagnostic message.</param>
		/// <param name="field">Optional field metadata related to the warning.</param>
		public void AddWarning(string message, FieldMetadata? field = null)
		{
			if (message == null) throw new ArgumentNullException(nameof(message));
			_entries.Add(new DiagnosticEntry
			{
				Message = message,
				FieldName = field?.Name,
				IsError = false,
				Timestamp = DateTime.UtcNow
			});
		}

		/// <summary>
		/// Record a non-fatal error message associated with an optional field.
		/// The entry timestamp is set to UTC now.
		/// </summary>
		/// <param name="message">Human-readable diagnostic message.</param>
		/// <param name="field">Optional field metadata related to the error.</param>
		public void AddError(string message, FieldMetadata? field = null)
		{
			if (message == null) throw new ArgumentNullException(nameof(message));
			_entries.Add(new DiagnosticEntry
			{
				Message = message,
				FieldName = field?.Name,
				IsError = true,
				Timestamp = DateTime.UtcNow
			});
		}

		/// <summary>
		/// Retrieve all collected diagnostic entries in chronological order as a read-only list.
		/// </summary>
		/// <returns>Read-only list of <see cref="DiagnosticEntry"/> instances.</returns>
		public IReadOnlyList<DiagnosticEntry> GetAll() => _entries.AsReadOnly();
	}

	/// <summary>
	/// Exception type thrown for fatal schema or runtime serialization errors when strict behavior is required.
	/// </summary>
	public sealed class SerializationException : Exception
	{
		/// <summary>
		/// Create a new <see cref="SerializationException"/> with the specified message.
		/// </summary>
		/// <param name="message">Error message describing the failure.</param>
		public SerializationException(string message) : base(message) { }
	}

	/// <summary>
	/// Per-operation context passed to serializer entry points and lower-level components.
	/// Encapsulates <see cref="SerializerOptions"/>, diagnostics collector, service provider,
	/// and references to the metadata cache. The context also provides helpers for enforcing
	/// safety limits and for recording or throwing diagnostics according to the configured policy.
	/// </summary>
	public sealed class SerializerContext
	{
		/// <summary>
		/// Create a new <see cref="SerializerContext"/> for a single serialization or deserialization operation.
		/// </summary>
		/// <param name="options">Global serializer options that influence behavior and safety limits.</param>
		/// <param name="diagnostics">Diagnostics collector used to record non-fatal warnings and errors. If null, a default in-memory collector is created.</param>
		/// <param name="metadataCache">Metadata cache used to resolve <see cref="TypeMetadata"/> for CLR types. May be null if metadata is not required.</param>
		/// <param name="services">Optional service provider used to resolve converters, conditions, and other services at runtime.</param>
		public SerializerContext(
			SerializerOptions? options = null,
			IDiagnosticsCollector? diagnostics = null,
			IMetadataCache? metadataCache = null,
			IServiceProvider? services = null)
		{
			Options = options ?? new SerializerOptions();
			Diagnostics = diagnostics ?? new InMemoryDiagnosticsCollector();
			MetadataCache = metadataCache;
			Services = services;
		}

		/// <summary>
		/// Global serializer options for this operation.
		/// </summary>
		public SerializerOptions Options { get; }

		/// <summary>
		/// Diagnostics collector used to record warnings and non-fatal errors produced during this operation.
		/// </summary>
		public IDiagnosticsCollector Diagnostics { get; }

		/// <summary>
		/// Metadata cache used to resolve <see cref="TypeMetadata"/> for CLR types.
		/// May be null if the caller does not require metadata-driven serialization.
		/// </summary>
		public IMetadataCache? MetadataCache { get; }

		/// <summary>
		/// Optional service provider used to resolve converters, conditions, and other services.
		/// </summary>
		public IServiceProvider? Services { get; }

		/// <summary>
		/// Tracks the number of bits produced or consumed so far for the current top-level object.
		/// Implementations should update this value as they read or write bits and call <see cref="EnforceBitLimit"/>
		/// to ensure the configured <see cref="SerializerOptions.MaxBitsPerObject"/> is not exceeded.
		/// </summary>
		public long BitsProcessed { get; private set; }

		/// <summary>
		/// Increment the internal bit counter by <paramref name="bits"/> and enforce the configured per-object limit.
		/// When the limit is exceeded this method will either throw <see cref="SerializationException"/> (in strict mode)
		/// or record an error via <see cref="Diagnostics"/> and return false.
		/// </summary>
		/// <param name="bits">Number of bits to add to the processed total.</param>
		/// <returns><c>true</c> when the increment was accepted; <c>false</c> when the limit was exceeded and strict mode is not enabled.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bits"/> is negative.</exception>
		/// <exception cref="SerializationException">Thrown when the bit limit is exceeded and <see cref="SerializerOptions.Strict"/> is true.</exception>
		public bool AddBits(long bits)
		{
			if (bits < 0) throw new ArgumentOutOfRangeException(nameof(bits));
			BitsProcessed += bits;
			return EnforceBitLimit();
		}

		/// <summary>
		/// Check whether the current <see cref="BitsProcessed"/> exceeds the configured <see cref="SerializerOptions.MaxBitsPerObject"/>.
		/// When the limit is exceeded this method will either throw <see cref="SerializationException"/> (in strict mode)
		/// or record an error via <see cref="Diagnostics"/> and return false.
		/// </summary>
		/// <returns><c>true</c> if the bit usage is within limits; otherwise <c>false</c>.</returns>
		/// <exception cref="SerializationException">Thrown when the bit limit is exceeded and <see cref="SerializerOptions.Strict"/> is true.</exception>
		public bool EnforceBitLimit()
		{
			if (BitsProcessed <= Options.MaxBitsPerObject)
				return true;

			string msg = $"Exceeded maximum allowed bits per object: {BitsProcessed} > {Options.MaxBitsPerObject}.";
			if (Options.Strict)
				throw new SerializationException(msg);

			Diagnostics.AddError(msg, null);
			return false;
		}

		/// <summary>
		/// Helper to record a warning or throw depending on strictness. When <see cref="Options.Strict"/> is true,
		/// this method throws a <see cref="SerializationException"/>; otherwise it records the message as a warning.
		/// </summary>
		/// <param name="message">Diagnostic message to record or include in the thrown exception.</param>
		/// <param name="field">Optional field metadata associated with the diagnostic.</param>
		public void ReportWarningOrThrow(string message, FieldMetadata? field = null)
		{
			if (Options.Strict)
				throw new SerializationException(message);
			Diagnostics.AddWarning(message, field);
		}

		/// <summary>
		/// Helper to record an error or throw depending on strictness. When <see cref="Options.Strict"/> is true,
		/// this method throws a <see cref="SerializationException"/>; otherwise it records the message as an error.
		/// </summary>
		/// <param name="message">Diagnostic message to record or include in the thrown exception.</param>
		/// <param name="field">Optional field metadata associated with the diagnostic.</param>
		public void ReportErrorOrThrow(string message, FieldMetadata? field = null)
		{
			if (Options.Strict)
				throw new SerializationException(message);
			Diagnostics.AddError(message, field);
		}

		/// <summary>
		/// Resolve a service of type <typeparamref name="T"/> from the configured <see cref="Services"/> provider.
		/// Returns null when no provider is configured or the service cannot be resolved.
		/// </summary>
		/// <typeparam name="T">Service type to resolve.</typeparam>
		/// <returns>Resolved service instance or null.</returns>
		public T? GetService<T>() where T : class
		{
			return Services == null ? null : Services.GetService(typeof(T)) as T;
		}

		/// <summary>
		/// Reset per-operation counters (e.g., <see cref="BitsProcessed"/>). This can be used when the same context
		/// instance is reused for multiple top-level objects, though creating a fresh context per operation is recommended.
		/// </summary>
		public void Reset()
		{
			BitsProcessed = 0;
		}
	}

	/// <summary>
	/// High-level entry point for serialization and deserialization operations.
	/// This static class orchestrates choosing metadata-driven serialization, converters, or
	/// calling <see cref="ICustomBitSerializable"/> when present.
	/// The methods are placeholders and must be implemented to perform actual I/O with <see cref="BitStorage"/>.
	/// </summary>
	public static class Serializer
	{
		/// <summary>
		/// Serialize the provided <paramref name="obj"/> into the given <paramref name="storage"/> using the provided <paramref name="ctx"/>.
		/// Implementations should consult <see cref="SerializerContext.Metadata"/> and <see cref="FieldMetadata"/> to drive metadata-based serialization.
		/// </summary>
		/// <param name="obj">The object to serialize.</param>
		/// <param name="storage">Target bit storage writer.</param>
		/// <param name="ctx">Per-operation serializer context.</param>
		public static void SerializeObject(object obj, BitStorage storage, SerializerContext ctx)
		{
			throw new NotImplementedException("Serializer.SerializeObject must be implemented.");
		}

		/// <summary>
		/// Deserialize an instance of <paramref name="type"/> from the provided <paramref name="reader"/> using the given <paramref name="ctx"/>.
		/// Implementations should construct the object, set fields via <see cref="FieldMetadata.Setter"/>, and return the instance.
		/// </summary>
		/// <param name="type">The CLR type to deserialize.</param>
		/// <param name="reader">Source bit storage reader.</param>
		/// <param name="ctx">Per-operation serializer context.</param>
		/// <returns>The deserialized object instance, or null when appropriate.</returns>
		public static object? DeserializeObject(Type type, BitStorageReader reader, SerializerContext ctx)
		{
			throw new NotImplementedException("Serializer.DeserializeObject must be implemented.");
		}
	}
	public readonly struct TypeResolution
	{
		public TypeResolution(Type? elementType, int? width, bool isEnumerable)
		{
			ElementType = elementType;
			Width = width;
			IsEnumerable = isEnumerable;
		}
		public readonly Type? ElementType { get; }
		public readonly int? Width { get; }        // native width in bits for the subject (element or type)
		public readonly bool IsEnumerable { get; }
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

			var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
							.Select(p => new { Prop = p, Attr = p.GetCustomAttribute<BitFieldAttribute>() })
							.Where(x => x.Attr != null)
							.OrderBy(x => x.Attr!.Order ?? x.Prop.MetadataToken)
							.ThenBy(x => x.Prop.MetadataToken);

			var fields = props.Select(x => BuildFieldMetadata(x.Prop, x.Attr!))
							.ToList()
							.AsReadOnly();
			return new TypeMetadata
			{
				Type = type,
				FieldsInOrder = fields
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
				TypeResolution = ResolveTypeInfo(prop.PropertyType, _options.MaxBitsPerObject),
			};
			// 1) Basic validations
			ValidateFieldMetadataPreliminary(metadata);

			if(metadata.IsCustomSerializer)
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
				if(attr.Default != null)
				{
					throw new SerializationException($"Field '{metadata.Name}': Default is not allowed with an enumerator.");
				}
				ValidateEnumerableRules(metadata);
			}

			// 4) Decide resolvedBits (inference, policy, converters, custom types)
			int resolvedBits = ResolveResolvedBits(metadata, parsedMin, parsedMax);// name, prop, attr, parsedMin, parsedMax, elemType);

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
			if (attr.Bits.HasValue && attr.InferBits == true)
				Fail("Bits cannot be specified when InferBits is true.");

			if (attr.CountBitLength.HasValue && attr.TerminatorValue.HasValue)
				Fail("for IEnumerable<T> needs to provide exactly one of CountBitLength or TerminatorValue.");

			if (attr.Min.HasValue && attr.UnsignedMin.HasValue)
				Fail("Min and UnsignedMin are mutually exclusive.");

			if (attr.Max.HasValue && attr.UnsignedMax.HasValue)
				Fail("Max and UnsignedMax are mutually exclusive.");

			if (attr.Signed && (attr.UnsignedMin.HasValue || attr.UnsignedMax.HasValue))
				Fail("Signed=true cannot be used together with UnsignedMin/UnsignedMax.");

			// CountBitLength only makes sense for enumerables
			if (!metadata.TypeResolution.IsEnumerable && attr.CountBitLength.HasValue)
				Fail("CountBitLength is only valid for enumerable fields.");

			// TerminatorValue only makes sense for enumerables
			if (metadata.TypeResolution.IsEnumerable && attr.TerminatorValue.HasValue)
				Fail("TerminatorValue is only valid for enumerable fields.");

			// CountBitLength range check (cheap)
			if (attr.CountBitLength < 1 || attr.CountBitLength > 32)
				Fail("CountBitLength must be in range 1..32.");

			// Default conflicts with TerminatorValue (collection-level terminator semantics)
			if (attr.Default != null && attr.TerminatorValue.HasValue)
				Fail("Default is not allowed when TerminatorValue is used (terminator semantics conflict).");

			// Conditional fields: combine/mode only meaningful when a condition is present
			bool hasCondition = !string.IsNullOrEmpty(attr.ConditionalProperty) || attr.ConditionalType != null;
			if (!hasCondition && (attr.ConditionCombine != ConditionCombine.And || attr.ConditionMode != ConditionEvaluationMode.Snapshot))
				Fail("ConditionCombine/ConditionMode set but no ConditionalProperty or ConditionalType provided.");

			// --- Lightweight bounds ordering checks (same-signness only) ---
			// Only perform simple ordering checks when both bounds are present and of the same signedness.
			if (attr.Max < attr.Min)
				Fail($"Invalid bounds: Max ({attr.Max.Value}) is less than Min ({attr.Min.Value}).");

			if (attr.UnsignedMax < attr.UnsignedMin)
					Fail($"Invalid bounds: UnsignedMax ({attr.UnsignedMax.Value}) is less than UnsignedMin ({attr.UnsignedMin.Value}).");

			// Do not attempt cross-signed comparisons here (e.g., Min vs UnsignedMax) — defer to numeric-resolution pass.

			// TerminatorValue cannot have a value if enumerableElementType is null, no need to check here too
			// If TerminatorValue is present, ensure it is within a plausible range (cheap check)
			// We cannot fully validate it without element width; just ensure it's non-negative (terminator is an encoded value).
			if (attr.TerminatorValue < 0)
				Fail("TerminatorValue must be non-negative.");

			// --- Converter/CustomSerializer presence quick checks (no heavy validation) ---
			// We only flag obviously missing required attributes here; do not attempt to resolve converter-provided widths.
			if (attr.ConverterType != null || typeof(ICustomBitSerializable).IsAssignableFrom(prop.PropertyType))
			{
				// Mark metadata
				metadata.IsCustomSerializer = true;

				// Disallowed attributes with custom serializer
				if (attr.Bits.HasValue) Fail("Bits cannot be used with a custom serializer.");
				if (attr.InferBits == true) Fail("InferBits cannot be used with a custom serializer.");
				if (attr.Min.HasValue || attr.Max.HasValue || attr.UnsignedMin.HasValue || attr.UnsignedMax.HasValue)
					Fail("Min/Max/UnsignedMin/UnsignedMax cannot be used with a custom serializer.");
				if (attr.CountBitLength.HasValue || attr.TerminatorValue.HasValue)
					Fail("CountBitLength and TerminatorValue cannot be used with a custom serializer.");
				// Default allowed only if documented; otherwise reject
				if (attr.Default != null) Fail("Default is not allowed with a custom serializer unless the serializer documents support.");
			} else if(metadata.TypeResolution.ElementType == null)
			{
				Fail("Cannot resolve type to valid data type");
			} else if(metadata.TypeResolution.Width == null)
			{
				Fail("Typewidth cannot be null");
			}

			// --- Final quick sanity checks ---
			if (attr.Bits.HasValue && attr.Bits.Value < 1)
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

		public static TypeResolution ResolveTypeInfo(Type fieldType, long maxAllowedBits) // TODO: maxAllowedBits is wrong
		{
			// Fast primitive/unwrapping checks first (cheap)
			var underlying = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
			if (underlying.IsEnum) underlying = Enum.GetUnderlyingType(underlying);

			if (underlying == typeof(bool)) return new TypeResolution(null, 1, false);
			if (underlying == typeof(char)) return new TypeResolution(null, 16, false);
			if (underlying == typeof(byte) || underlying == typeof(sbyte)) return new TypeResolution(null, 8, false);
			if (underlying == typeof(short) || underlying == typeof(ushort)) return new TypeResolution(null, 16, false);
			if (underlying == typeof(int) || underlying == typeof(uint)) return new TypeResolution(null, 32, false);
			if (underlying == typeof(long) || underlying == typeof(ulong)) return new TypeResolution(null, 64, false);

			// Cached slow path for non-primitives
			return _typeResolutionCache.GetOrAdd(fieldType, t =>
			{
				// If it's an enumerable, resolve the element type info (recurses but uses cache)
				if (TryGetEnumerableElementType(t, out var elemType))
				{
					if (elemType == null)
						return new TypeResolution(null, null, true); // non-generic IEnumerable -> unknown element

					// Resolve element info (calls back into ResolveTypeInfo but will hit cache for primitives)
					var elemInfo = ResolveTypeInfo(elemType, maxAllowedBits);

					// If element width is unknown, caller should require a converter or explicit Bits
					return new TypeResolution(elemType, elemInfo.Width, true);
				}

				// Unknown/unsupported type
				return new TypeResolution(null, null, false);
			});
		}

		//private static readonly ConcurrentDictionary<Type, Type?> _elementTypeCache = new();

		//public static bool TryGetEnumerableElementType(Type type, out Type? elementType)
		//{
		//	elementType = _elementTypeCache.GetOrAdd(type, t =>
		//	{
		//		// Treat string as an enumerable of char (UTF-16 code units)
		//		if (t == typeof(string)) return typeof(char);

		//		// Arrays
		//		if (t.IsArray) return t.GetElementType();

		//		// Generic type like List<T>, IReadOnlyList<T>, IEnumerable<T>
		//		if (t.IsGenericType)
		//		{
		//			// If the generic type itself is IEnumerable<T> or implements it directly
		//			var genDef = t.GetGenericTypeDefinition();
		//			if (genDef == typeof(IEnumerable<>) || genDef == typeof(IReadOnlyList<>) || genDef == typeof(IList<>)
		//				|| genDef == typeof(List<>))
		//			{
		//				return t.GetGenericArguments()[0];
		//			}
		//		}

		//		// Check implemented interfaces for IEnumerable<T>
		//		foreach (var iface in t.GetInterfaces())
		//		{
		//			if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
		//				return iface.GetGenericArguments()[0];
		//		}

		//		// Non-generic IEnumerable or other complex cases: unknown element type (defer to resolution)
		//		return null;
		//	});

		//	return elementType != null;
		//}

		private static (BigInteger? parsedMin, BigInteger? parsedMax) ParseBounds(BitFieldAttribute attr)
		{
			BigInteger? parsedMin = null, parsedMax = null;
			if (attr.Min.HasValue) parsedMin = new BigInteger(attr.Min.Value);
			if (attr.Max.HasValue) parsedMax = new BigInteger(attr.Max.Value);
			if (attr.UnsignedMin.HasValue) parsedMin = new BigInteger(attr.UnsignedMin.Value);
			if (attr.UnsignedMax.HasValue) parsedMax = new BigInteger(attr.UnsignedMax.Value);
			return (parsedMin, parsedMax);
		}

		private static void ValidateEnumerableRules(FieldMetadata metadata)//string name, BitFieldAttribute attr, Type? elemType)
		{
			if (metadata.Attribute.TerminatorValue.HasValue)
			{
				int elementWidth = metadata.Width;
				if (!TerminatorFits(metadata.Attribute.TerminatorValue.Value, elementWidth))
					throw new SerializationException($"Field '{metadata.Name}': TerminatorValue does not fit in element width {elementWidth}.");
			}
		}
		private int ResolveResolvedBits(FieldMetadata metadata, 
			BigInteger? parsedMin,
			BigInteger? parsedMax)
		{
			// Determine the "subject type" to consult for native width: element type if enumerable, otherwise the property type.
			BigInteger min, max;
			var subjectType = metadata.ElementType;
			var name = metadata.Name;
			var attr = metadata.Attribute;
			int maxAllowed = metadata.Width;

			if (attr.Bits.HasValue)
			{
				int requested = attr.Bits.Value;
				const int absoluteMin = 1;
				if (requested < absoluteMin || requested > maxAllowed)
				{
					throw new SerializationException($"Field '{name}': Bits must be between {absoluteMin} and {maxAllowed} for type {subjectType.Name}.");
				}
				maxAllowed = requested;
				(min, max) = TryGetMinMax(parsedMin, parsedMax, attr.Signed, maxAllowed);
			}
			// No explicit Bits: follow existing inference / policy logic
			else if (attr.InferBits ?? _options.AutoInferBits) // if InferBits is true or if it's null and AutoInferBits is true
			{
				(min, max) = TryGetMinMax(parsedMin, parsedMax, attr.Signed, maxAllowed);
				maxAllowed = InferBitsFromBounds(min, max, attr.Signed, _options.MaxInferredBits);
			}
			else
			{
				throw new SerializationException($"Field '{name}': Bits not specified and global policy forbids inference.");
			}
			metadata.ResolvedMin = min;
			metadata.ResolvedMax = max;
			metadata.ResolvedBits = maxAllowed;
			return maxAllowed;
		}

		//private static void ValidateBoundsAgainstBits(string name, BitFieldAttribute attr, BigInteger? parsedMin, BigInteger? parsedMax, int resolvedBits)
		//{
		//	if (parsedMin.HasValue)
		//	{
		//		if (attr.Signed)
		//		{
		//			var (sMinBig, sMaxBig) = SignedRangeBig(resolvedBits);
		//			if (parsedMin.Value < sMinBig || parsedMin.Value > sMaxBig)
		//				throw new SerializationException($"Field '{name}': Min is out of representable signed range for {resolvedBits} bits.");
		//		}
		//		else
		//		{
		//			var uMaxBig = UnsignedMaxBig(resolvedBits);
		//			if (parsedMin.Value < BigInteger.Zero || parsedMin.Value > uMaxBig)
		//				throw new SerializationException($"Field '{name}': UnsignedMin is out of representable unsigned range for {resolvedBits} bits.");
		//		}
		//	}

		//	if (parsedMax.HasValue)
		//	{
		//		if (attr.Signed)
		//		{
		//			var (sMinBig, sMaxBig) = SignedRangeBig(resolvedBits);
		//			if (parsedMax.Value < sMinBig || parsedMax.Value > sMaxBig)
		//				throw new SerializationException($"Field '{name}': Max is out of representable signed range for {resolvedBits} bits.");
		//		}
		//		else
		//		{
		//			var uMaxBig = UnsignedMaxBig(resolvedBits);
		//			if (parsedMax.Value < BigInteger.Zero || parsedMax.Value > uMaxBig)
		//				throw new SerializationException($"Field '{name}': UnsignedMax is out of representable unsigned range for {resolvedBits} bits.");
		//		}
		//	}

		//	if (parsedMin.HasValue && parsedMax.HasValue && parsedMin.Value > parsedMax.Value)
		//		throw new SerializationException($"Field '{name}': Min > Max.");
		//}

		private static void ValidateDefaultValue(FieldMetadata metadata) //string name, object? defaultValue, Type propertyType, BigInteger min, BigInteger max)
		{
			if (metadata.Attribute.Default is null) return; // nothing to validate

			BigInteger defaultBig;
			try
			{
				defaultBig = metadata.Attribute.Default switch
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
						? new BigInteger(Convert.ToInt64(metadata.Attribute.Default, CultureInfo.InvariantCulture))
						: new BigInteger(Convert.ToUInt64(metadata.Attribute.Default, CultureInfo.InvariantCulture))
				};
			}
			catch (Exception ex)
			{
				throw new SerializationException($"Field '{metadata.Name}': error converting Default value: {ex.Message}");
			}

			// simple range check (min and max are assumed valid and min < max)
			if (defaultBig < metadata.ResolvedMin || defaultBig > metadata.ResolvedMax)
				throw new SerializationException($"Field '{metadata.Name}': Default value out of representable range.");
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

		ConcurrentDictionary<(PropertyInfo info, bool allowNonPublic), Accessors> _accessorCache = new();
		/// <summary>
		/// Resolve getter/setter delegates. Public accessors use expression-compiled delegates (fast).
		/// Non-public accessors are only permitted when the attribute or global options opt-in; in that case
		/// we create reflection-invoke wrappers.
		/// </summary>
		private Accessors ResolveAccessors(PropertyInfo prop, BitFieldAttribute attr)
		{
			bool allowNonPublic = attr.AllowNonPublicAccess ?? _options.AllowNonPublicAccess ?? false;
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

		// -------------------------
		// Reflection helper wrappers for non-public accessors
		// -------------------------
		//private static Func<object, object?> CreateReflectionGetter(MethodInfo method)
		//{
		//	return instance =>
		//	{
		//		if (instance == null) return null;
		//		try
		//		{
		//			return method.Invoke(instance, null);
		//		}
		//		catch (TargetInvocationException tie)
		//		{
		//			throw tie.InnerException ?? tie;
		//		}
		//	};
		//}

		//private static Action<object, object?> CreateReflectionSetter(MethodInfo method)
		//{
		//	return (instance, value) =>
		//	{
		//		if (instance == null) return;
		//		try
		//		{
		//			method.Invoke(instance, new[] { value });
		//		}
		//		catch (TargetInvocationException tie)
		//		{
		//			throw tie.InnerException ?? tie;
		//		}
		//	};
		//}
		// -------------------------
		// Helpers
		// -------------------------
		//private static bool HasAnyBounds(BitFieldAttribute attr)
		//{
		//	return attr.Min.HasValue || attr.Max.HasValue || attr.UnsignedMin.HasValue || attr.UnsignedMax.HasValue;
		//}

		private static bool TerminatorFits(ulong term, int bits)
		{
			if (bits == 64) return true;
			return term <= ((1UL << bits) - 1UL);
		}

		//private static (long min, long max) SignedRange(int bits)
		//{
		//	if (bits == 64) return (long.MinValue, long.MaxValue);
		//	long min = -(1L << (bits - 1));
		//	long max = (1L << (bits - 1)) - 1;
		//	return (min, max);
		//}

		//private static ulong UnsignedMax(int bits)
		//{
		//	if (bits == 64) return ulong.MaxValue;
		//	return (1UL << bits) - 1UL;
		//}

		//private static (BigInteger min, BigInteger max) SignedRangeBig(int bits)
		//{
		//	if (bits == 64) return (new BigInteger(long.MinValue), new BigInteger(long.MaxValue));
		//	var one = new BigInteger(1);
		//	var min = -(one << (bits - 1));
		//	var max = (one << (bits - 1)) - 1;
		//	return (min, max);
		//}

		//private static BigInteger UnsignedMaxBig(int bits)
		//{
		//	if (bits == 64) return new BigInteger(ulong.MaxValue);
		//	var one = new BigInteger(1);
		//	return (one << bits) - 1;
		//}

		//private static bool IsAssignableOrConvertible(object value, Type targetType)
		//{
		//	if (value == null) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
		//	var valType = value.GetType();
		//	if (targetType.IsAssignableFrom(valType)) return true;
		//	try
		//	{
		//		Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType, CultureInfo.InvariantCulture);
		//		return true;
		//	}
		//	catch { return false; }
		//}

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

		//private static Func<object, object?> CreateGetter(PropertyInfo prop)
		//{
		//	var instance = Expression.Parameter(typeof(object), "instance");
		//	var convertInstance = Expression.Convert(instance, prop.DeclaringType!);
		//	var propertyAccess = Expression.Property(convertInstance, prop);
		//	var convertResult = Expression.Convert(propertyAccess, typeof(object));
		//	var lambda = Expression.Lambda<Func<object, object?>>(convertResult, instance);
		//	return lambda.Compile();
		//}

		//private static Action<object, object?> CreateSetter(PropertyInfo prop)
		//{
		//	var instance = Expression.Parameter(typeof(object), "instance");
		//	var value = Expression.Parameter(typeof(object), "value");
		//	var convertInstance = Expression.Convert(instance, prop.DeclaringType!);
		//	var convertValue = Expression.Convert(value, prop.PropertyType);
		//	var propertyAccess = Expression.Property(convertInstance, prop);
		//	var assign = Expression.Assign(propertyAccess, convertValue);
		//	var lambda = Expression.Lambda<Action<object, object?>>(assign, instance, value);
		//	return lambda.Compile();
		//}

		//private static int? GetTypeWidth(Type t)
		//{
		//	if (t.IsEnum) return GetTypeWidth(Enum.GetUnderlyingType(t));
		//	if (t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(char)) return 8;
		//	if (t == typeof(short) || t == typeof(ushort)) return 16;
		//	if (t == typeof(int) || t == typeof(uint)) return 32;
		//	if (t == typeof(long) || t == typeof(ulong)) return 64;
		//	var nt = Nullable.GetUnderlyingType(t);
		//	if (nt != null) return GetTypeWidth(nt);
		//	//if (t == typeof(float)) return 32;
		//	//if (t == typeof(double)) return 64;
		//	return null;
		//}

		//private static int GetTypeWidthOrThrow(Type t, string context)
		//{
		//	var w = GetTypeWidth(t);
		//	if (w == null)
		//	{
		//		throw new SerializationException($"{context}: cannot infer native width for type {t} (provide Bits or a converter).");
		//	}
		//	return w.Value;
		//}

		//private static bool TryGetEnumerableElementType(Type t, out Type? elementType)
		//{
		//	elementType = null;
		//	if (t.IsArray)
		//	{
		//		elementType = t.GetElementType();
		//		return true;
		//	}
		//	var ifaces = t.GetInterfaces().Concat(new[] { t });
		//	foreach (var iface in ifaces)
		//	{
		//		if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
		//		{
		//			elementType = iface.GetGenericArguments()[0];
		//			return true;
		//		}
		//	}
		//	return false;
		//}
		// Convert each bound to a magnitude candidate:
		// - take absolute value
		// - if the original bound was negative, subtract 1
		private static BigInteger MagnitudeCandidate(BigInteger v)
		{
			if (v >= 0) return v;
			return BigInteger.Abs(v) - 1;
		}
		private static int InferBitsFromBounds(
			BigInteger min,
			BigInteger max,
			bool signed,
			//int nativeWidth,
			int maxAllowed) // nativeWidth in bits, if available
		{
			// If native width is provided, use it to fill missing bound(s)
			//var (min, max) = TryGetMinMax(parsedMin, parsedMax, signed, nativeWidth);
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

		private static (BigInteger min, BigInteger max) TryGetMinMax(BigInteger? parsedMin, BigInteger? parsedMax, bool signed, int nativeWidth)
		{
			if (nativeWidth <= 0 || nativeWidth > 1024) // sanity guard for absurd widths
			{
				throw new SerializationException($"Invalid native width {nativeWidth}.");
			}

			BigInteger nativeMin = signed ? -(BigInteger.One << (nativeWidth - 1)) : BigInteger.Zero;
			BigInteger nativeMax = signed ? (BigInteger.One << (nativeWidth - 1)) - 1 : (BigInteger.One << nativeWidth) - 1;

			BigInteger min = parsedMin ?? nativeMin;
			BigInteger max = parsedMax ?? nativeMax;
			if (min < nativeMin)
			{
				throw new SerializationException($"Min value ({min}) is less than what fits in native width ({nativeMin}).");
			}
			if (max > nativeMax)
			{
				throw new SerializationException($"Max value ({max}) is greater than what fits in native width ({nativeMax}).");
			}
			if (max < min)
			{
				throw new SerializationException($"Invalid bounds: max ({max}) is less than min ({min}).");
			}
			return (min, max);
		}

		//	/// <summary>
		//	/// Infer minimal bits from parsed bounds. Requires at least one bound to be present.
		//	/// Returns a value in [1, maxAllowed].
		//	/// </summary>
		//	private static int InferBitsFromBounds(BigInteger? parsedMin, BigInteger? parsedMax, bool signed, int maxAllowed)
		//	{
		//		if (maxAllowed < 1 || maxAllowed > 64) throw new ArgumentOutOfRangeException(nameof(maxAllowed));

		//		if (!signed)
		//		{
		//			if (!parsedMin.HasValue && !parsedMax.HasValue)
		//				throw new SerializationException("Cannot infer unsigned bits without at least one bound.");

		//			BigInteger min = parsedMin ?? BigInteger.Zero;
		//			BigInteger max = parsedMax ?? UnsignedMaxBig(maxAllowed);

		//			if (min > max) throw new SerializationException("Min > Max during inference.");

		//			BigInteger needed = (max - min) + 1; // number of distinct values
		//			if (needed <= 1) return 1;

		//			int bits = 0;
		//			BigInteger cap = BigInteger.One;
		//			while (cap < needed)
		//			{
		//				cap <<= 1;
		//				bits++;
		//				if (bits > maxAllowed) throw new SerializationException($"Cannot infer bits within {maxAllowed} bits for unsigned range.");
		//			}
		//			return Math.Max(1, bits);
		//		}
		//		else
		//		{
		//			if (!parsedMin.HasValue && !parsedMax.HasValue)
		//				throw new SerializationException("Cannot infer signed bits without at least one bound.");

		//			BigInteger min = parsedMin ?? SignedRangeBig(maxAllowed).min;
		//			BigInteger max = parsedMax ?? SignedRangeBig(maxAllowed).max;

		//			if (min > max) throw new SerializationException("Min > Max during inference.");

		//			for (int b = 1; b <= maxAllowed; b++)
		//			{
		//				var (minB, maxB) = SignedRangeBig(b);
		//				if (min >= minB && max <= maxB) return b;
		//			}
		//			throw new SerializationException($"Cannot infer bits within {maxAllowed} bits for signed range.");
		//		}
		//	}
	}

	/// <summary>
	/// Minimal thread-safe in-memory metadata cache. The cache delegates discovery and
	/// construction of TypeMetadata to <see cref="FieldMetadataBuilder"/> and simply
	/// stores the result for subsequent lookups.
	/// </summary>
	public sealed class InMemoryMetadataCache : IMetadataCache
	{
		private readonly ConcurrentDictionary<Type, TypeMetadata?> _cache = new();
		private readonly FieldMetadataBuilder _builder;
		private readonly IDiagnosticsCollector? _diagnostics;

		/// <summary>
		/// Create a new cache that uses the provided <paramref name="builder"/> to construct metadata.
		/// </summary>
		/// <param name="builder">Builder responsible for discovery, validation and accessor resolution.</param>
		/// <param name="diagnostics">Optional diagnostics collector forwarded to the builder.</param>
		public InMemoryMetadataCache(FieldMetadataBuilder builder, IDiagnosticsCollector? diagnostics = null)
		{
			_builder = builder ?? throw new ArgumentNullException(nameof(builder));
			_diagnostics = diagnostics;
		}

		/// <inheritdoc />
		public TypeMetadata? GetTypeMetadata(Type type)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));
			// Call the builder's BuildTypeMetadata(Type) which uses the builder's injected options/services.
			return _cache.GetOrAdd(type, t => _builder.BuildTypeMetadata(t));
		}

		/// <inheritdoc />
		public bool TryGetField(Type type, string fieldName, out FieldMetadata? field)
		{
			field = null;
			if (type == null) throw new ArgumentNullException(nameof(type));
			if (string.IsNullOrEmpty(fieldName)) throw new ArgumentNullException(nameof(fieldName));

			var meta = GetTypeMetadata(type);
			if (meta == null || meta.FieldsInOrder == null || meta.FieldsInOrder.Count == 0)
				return false;

			// Exact (case-sensitive) match first, then case-insensitive fallback.
			var match = meta.FieldsInOrder.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.Ordinal));
			if (match != null)
			{
				field = match;
				return true;
			}

			match = meta.FieldsInOrder.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
			if (match != null)
			{
				field = match;
				return true;
			}

			return false;
		}
	}

	// -------------------------
	// Placeholder bit storage types
	// -------------------------
	public sealed class BitStorage { /* implement writer */ }
	public sealed class BitStorageReader { /* implement reader */ }

}
