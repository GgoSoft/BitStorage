//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Linq.Expressions;
//using System.Reflection;

//namespace GgoSoft
//{

//	/// <summary>
//	/// Marks a property to be included in bit-level serialization and carries schema hints.
//	/// All members are optional; the serializer resolves sensible defaults at preflight.
//	/// </summary>
//	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
//	public sealed class BitFieldAttribute : Attribute
//	{
//		// --- Core width and numeric semantics --------------------------------

//		/// <summary>
//		/// Number of bits to use for this field. <c>null</c> means "use the native width
//		/// of the property or element type" (resolved at preflight). Value must be between 1 and 
//		/// the number of bits appropriate for the field.  If field is enumerable, this applies to the element width.
//		/// </summary>
//		public int? Bits { get; set; } = null;

//		/// <summary>
//		/// Interpret the stored bits as a signed two's complement value when reading.  This is
//		/// required if <see cref="Bits"/> is less than the native width of the property type and 
//		/// you want to support negative values.
//		/// </summary>
//		public bool Signed { get; set; } = false;

//		/// <summary>
//		/// When true (default) the serializer validates runtime values against the representable
//		/// range derived from <see cref="Bits"/> and <see cref="Signed"/>. If <c>false</c>,
//		/// range checks are skipped at write time (<see cref="Default"/> is still validated at preflight).
//		/// </summary>
//		public bool ValidateRange { get; set; } = true;

//		/// <summary>
//		/// Optional explicit minimum (signed semantics). If provided, it must fit into the
//		/// representable range for the resolved bit width (validated at preflight).
//		/// </summary>
//		public long? Min { get; set; } = null;

//		/// <summary>
//		/// Optional explicit maximum (signed semantics). If provided, it must fit into the
//		/// representable range for the resolved bit width (validated at preflight).
//		/// </summary>
//		public long? Max { get; set; } = null;

//		// --- Ordering, optionality, defaults ----------------------------------

//		/// <summary>
//		/// Serialization order index. If null, order is inferred (declaration order via MetadataToken).
//		/// </summary>
//		public int? Order { get; set; } = null;

//		/// <summary>
//		/// If true, the field is omitted when its value equals the .NET default for the property
//		/// type or equals <see cref="Default"/> (if provided). A presence bit is written/read.
//		/// </summary>
//		public bool Optional { get; set; } = false;

//		/// <summary>
//		/// Default value to use when an optional field is omitted. Must be assignable to the
//		/// property type (or handled by the converter) and is always validated to fit the bit range.
//		/// </summary>
//		public object? Default { get; set; } = null;

//		// --- Enumerable support (mutually exclusive modes) ---------------------

//		/// <summary>
//		/// When the property is an IEnumerable, the number of bits used to encode the element count.
//		/// Provide exactly one of <see cref="CountBitLength"/> or <see cref="TerminatorValue"/>,
//		/// unless a different policy is configured in serializer options.
//		/// </summary>
//		public int? CountBitLength { get; set; } = null;

//		/// <summary>
//		/// When the property is an IEnumerable, a terminator value encoded using the element width.
//		/// The serializer will validate at preflight and/or write time that the enumerable does not
//		/// contain this terminator value (or the converter must handle escaping).
//		/// </summary>
//		public ulong? TerminatorValue { get; set; } = null;

//		// --- Conditional writing ------------------------------------------------

//		/// <summary>
//		/// Shorthand: name of a property or "ShouldSerializeXyz" style method on the same object.
//		/// The serializer will check ShouldSerializeXyz() first (if present), then evaluate this property.
//		/// </summary>
//		public string? ConditionalProperty { get; set; } = null;

//		/// <summary>
//		/// A type implementing <see cref="IFieldCondition"/>. The serializer will resolve/instantiate
//		/// it (DI first if available) and call it to decide inclusion. The condition receives the
//		/// <see cref="FieldMetadata"/> and the serializer context.
//		/// </summary>
//		public Type? ConditionalType { get; set; } = null;

//		/// <summary>
//		/// How multiple condition sources combine. Default is And (all conditions must be true).
//		/// </summary>
//		public ConditionCombine ConditionCombine { get; set; } = ConditionCombine.And;

//		/// <summary>
//		/// Preferred evaluation mode for this field's condition. Default is Snapshot (evaluate
//		/// against the full object snapshot, avoiding order dependence).
//		/// </summary>
//		public ConditionEvaluationMode ConditionMode { get; set; } = ConditionEvaluationMode.Snapshot;

//		// --- Converters, versioning, misc -------------------------------------

//		/// <summary>
//		/// Optional converter type that implements <see cref="IBitFieldConverter"/> or the generic
//		/// variant. Converters may be fixed-width (Bits &gt; 0) or variable-length (Bits == null).
//		/// </summary>
//		public Type? ConverterType { get; set; } = null;

//		/// <summary>
//		/// Field version for forward/backward compatibility. Serializer may skip fields with
//		/// Version &gt; targetVersion when writing.
//		/// </summary>
//		public int Version { get; set; } = 0;

//		/// <summary>
//		/// Optional human-readable description for tooling and diagnostics.
//		/// </summary>
//		public string? Description { get; set; } = null;
//	}

//	/// <summary>
//	/// How multiple condition sources combine for a single field.
//	/// </summary>
//	public enum ConditionCombine
//	{
//		And,
//		Or
//	}

//	/// <summary>
//	/// Which evaluation model the serializer should use when calling a condition.
//	/// Snapshot: evaluate against the full object snapshot (no order dependence).
//	/// Incremental: evaluate against already-processed values (order-sensitive).
//	/// </summary>
//	public enum ConditionEvaluationMode
//	{
//		Snapshot,
//		Incremental
//	}
//	// Marker interface: opt-in only, no methods required
//	public interface IBitSerializable { }

//	// Optional custom serializer: implement when you want full control
//	public interface ICustomBitSerializable
//	{
//		// Called by the serializer when writing this object
//		void Serialize(BitStorage writer, SerializerContext context);

//		// Called by the serializer when reading into a new instance
//		void Deserialize(BitStorageReader reader, SerializerContext context);
//	}

//	/// <summary>
//	/// Evaluate whether a field should be included during serialization/deserialization.
//	/// The serializer passes a FieldMetadata instance so the condition does not need to reflect.
//	/// </summary>
//	public interface IFieldCondition
//	{
//		/// <summary>
//		/// Evaluate whether the field described by <paramref name="field"/> should be included.
//		/// </summary>
//		/// <param name="instance">The object being serialized/deserialized.</param>
//		/// <param name="currentValues">Snapshot of already-processed field values (may be empty in snapshot mode).</param>
//		/// <param name="field">Metadata for the field being evaluated.</param>
//		/// <param name="mode">Evaluation mode (Snapshot or Incremental).</param>
//		/// <param name="context">Serializer context for helpers, options, and diagnostics.</param>
//		/// <returns>True to include the field; false to omit it.</returns>
//		bool Evaluate(object instance,
//					  IReadOnlyDictionary<string, object?> currentValues,
//					  FieldMetadata field,
//					  ConditionEvaluationMode mode,
//					  SerializerContext context);
//	}

//	// -------------------------
//	// FieldMetadata
//	// -------------------------
//	public sealed class FieldMetadata
//	{
//		// Identity
//		public string Name { get; init; } = string.Empty;
//		public PropertyInfo Property { get; init; } = null!;

//		// Fast accessors (cached delegates)
//		public Func<object, object?> Getter { get; init; } = null!;
//		public Action<object, object?> Setter { get; init; } = null!;

//		// Resolved schema
//		public int ResolvedBits { get; init; }
//		public bool Signed { get; init; }
//		public bool Optional { get; init; }
//		public object? DefaultValue { get; init; }
//		public int Order { get; init; }

//		// Range info (precomputed)
//		public bool HasRange { get; init; }
//		public long SignedMin { get; init; }
//		public long SignedMax { get; init; }
//		public ulong UnsignedMax { get; init; }

//		// Type helpers
//		public Type PropertyType { get; init; } = null!;
//		public bool IsEnumerable { get; init; }
//		public Type? ElementType { get; init; }

//		// Extensibility points (may be null)
//		public IFieldCondition? ConditionInstance { get; init; }
//		public object? ConverterInstance { get; init; } // IBitFieldConverter or generic variant

//		// Diagnostics / description
//		public string? Description { get; init; }

//		// Convenience checks
//		public bool ValueFitsRange(object? value)
//		{
//			if (!HasRange) return true;
//			if (value == null) return false;

//			// Handle signed vs unsigned based on Signed flag and numeric types
//			if (Signed)
//			{
//				try
//				{
//					long v = Convert.ToInt64(value);
//					return v >= SignedMin && v <= SignedMax;
//				}
//				catch
//				{
//					return false;
//				}
//			}
//			else
//			{
//				try
//				{
//					// Convert to ulong for unsigned comparison
//					ulong uv = Convert.ToUInt64(value);
//					return uv <= UnsignedMax;
//				}
//				catch
//				{
//					return false;
//				}
//			}
//		}

//		public override string ToString()
//		{
//			return $"{Name} ({PropertyType.Name}) bits={ResolvedBits} signed={Signed} optional={Optional}";
//		}
//	}

//	// -------------------------
//	// SerializerOptions
//	// -------------------------
//	public sealed class SerializerOptions
//	{
//		/// <summary>When true, the serializer throws on the first error. When false, diagnostics are collected.</summary>
//		public bool Strict { get; set; } = true;

//		/// <summary>Maximum number of elements allowed when reading/writing enumerables (safety).</summary>
//		public int MaxEnumerableElements { get; set; } = 1_000_000;

//		/// <summary>Default condition evaluation mode (Snapshot or Incremental).</summary>
//		public ConditionEvaluationMode DefaultConditionMode { get; set; } = ConditionEvaluationMode.Snapshot;

//		/// <summary>Whether to pack presence bits for runs of optional fields (implementation choice).</summary>
//		public bool PackPresenceBits { get; set; } = false;

//		/// <summary>Maximum allowed bits per object to avoid runaway allocations.</summary>
//		public long MaxBitsPerObject { get; set; } = 1_000_000 * 8L;

//		/// <summary>Optional service provider for DI resolution of converters/conditions.</summary>
//		public IServiceProvider? Services { get; set; } = null;
//	}

//	// -------------------------
//	// IMetadataCache
//	// -------------------------
//	/// <summary>
//	/// Minimal metadata cache contract. Implementations should be thread-safe and perform preflight validation.
//	/// </summary>
//	public interface IMetadataCache
//	{
//		/// <summary>
//		/// Get cached metadata for a type. Returns null if the type has no bitfield metadata.
//		/// Implementations should perform preflight validation and throw SchemaException on invalid schemas.
//		/// </summary>
//		TypeMetadata? GetTypeMetadata(Type type);

//		/// <summary>
//		/// Try to get a single field metadata by name for a given type.
//		/// </summary>
//		bool TryGetField(Type type, string fieldName, out FieldMetadata? field);
//	}

//	/// <summary>
//	/// Container for per-type metadata (immutable after creation).
//	/// </summary>
//	public sealed class TypeMetadata
//	{
//		public Type Type { get; init; } = null!;
//		public IReadOnlyList<FieldMetadata> FieldsInOrder { get; init; } = Array.Empty<FieldMetadata>();
//		public bool HasFields => FieldsInOrder.Count > 0;
//	}

//	// -------------------------
//	// IDiagnosticsCollector
//	// -------------------------
//	public interface IDiagnosticsCollector
//	{
//		void AddWarning(string message, FieldMetadata? field = null);
//		void AddError(string message, FieldMetadata? field = null);
//		IReadOnlyList<DiagnosticEntry> GetAll();
//	}

//	public sealed class DiagnosticEntry
//	{
//		public DateTime Timestamp { get; init; } = DateTime.UtcNow;
//		public string Message { get; init; } = string.Empty;
//		public string? FieldName { get; init; }
//		public bool IsError { get; init; }
//	}

//	public sealed class InMemoryDiagnosticsCollector : IDiagnosticsCollector
//	{
//		private readonly List<DiagnosticEntry> _entries = new();

//		public void AddWarning(string message, FieldMetadata? field = null)
//		{
//			_entries.Add(new DiagnosticEntry { Message = message, FieldName = field?.Name, IsError = false });
//		}

//		public void AddError(string message, FieldMetadata? field = null)
//		{
//			_entries.Add(new DiagnosticEntry { Message = message, FieldName = field?.Name, IsError = true });
//		}

//		public IReadOnlyList<DiagnosticEntry> GetAll() => _entries.AsReadOnly();
//	}

//	// -------------------------
//	// SerializerContext
//	// -------------------------
//	public sealed class SerializerContext
//	{
//		public SerializerOptions Options { get; }
//		public IMetadataCache Metadata { get; }
//		public IDiagnosticsCollector Diagnostics { get; }
//		public IServiceProvider? Services => Options.Services;

//		// Condition evaluation mode for this operation (defaults to options)
//		public ConditionEvaluationMode ConditionMode { get; }

//		// CurrentValues is updated by the serializer as fields are processed.
//		// Expose as IReadOnlyDictionary to consumers.
//		private Dictionary<string, object?> _currentValues = new();
//		public IReadOnlyDictionary<string, object?> CurrentValues => _currentValues;

//		public SerializerContext(SerializerOptions options, IMetadataCache metadata, IDiagnosticsCollector diagnostics,
//								 ConditionEvaluationMode? conditionMode = null)
//		{
//			Options = options ?? throw new ArgumentNullException(nameof(options));
//			Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
//			Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
//			ConditionMode = conditionMode ?? options.DefaultConditionMode;
//		}

//		/// <summary>
//		/// Update the current value snapshot for a field. Called by the serializer after a field is written/read.
//		/// </summary>
//		public void UpdateCurrentValue(string name, object? value)
//		{
//			_currentValues[name] = value;
//		}

//		/// <summary>
//		/// Validate a value against the field's precomputed range. Throws or records diagnostics depending on Options.Strict.
//		/// </summary>
//		public void ValidateRange(FieldMetadata field, object? value)
//		{
//			if (field == null) throw new ArgumentNullException(nameof(field));
//			if (!field.HasRange) return;

//			if (!field.ValueFitsRange(value))
//			{
//				var msg = $"Field '{field.Name}' value out of range for {field.ResolvedBits}-bit {(field.Signed ? "signed" : "unsigned")} field.";
//				if (Options.Strict)
//					throw new SerializationException(msg);
//				Diagnostics.AddError(msg, field);
//			}
//		}

//		/// <summary>
//		/// Resolve a condition instance (DI first, then Activator). Caller should have validated the type at preflight.
//		/// </summary>
//		public IFieldCondition ResolveCondition(Type conditionType)
//		{
//			if (conditionType == null) throw new ArgumentNullException(nameof(conditionType));
//			var svc = Services?.GetService(conditionType) as IFieldCondition;
//			if (svc != null) return svc;
//			return (IFieldCondition)Activator.CreateInstance(conditionType)!;
//		}

//		/// <summary>
//		/// Resolve a converter instance (DI first, then Activator). Returns null if converterType is null.
//		/// </summary>
//		public object? ResolveConverter(Type? converterType)
//		{
//			if (converterType == null) return null;
//			var svc = Services?.GetService(converterType);
//			if (svc != null) return svc;
//			return Activator.CreateInstance(converterType);
//		}

//		/// <summary>
//		/// Convenience wrapper to call nested serialization. Implementations should call into the serializer entry points.
//		/// </summary>
//		public void WriteNested(object? value, BitStorage storage)
//		{
//			if (value == null) return;
//			// The serializer implementation should provide SerializeInternal; this is a thin wrapper.
//			Serializer.SerializeObject(value, storage, this);
//		}

//		/// <summary>
//		/// Convenience wrapper to call nested deserialization.
//		/// </summary>
//		public object? ReadNested(Type type, BitStorageReader reader)
//		{
//			return Serializer.DeserializeObject(type, reader, this);
//		}
//	}

//	// -------------------------
//	// Exceptions and helpers
//	// -------------------------
//	public sealed class SerializationException : Exception
//	{
//		public SerializationException(string message) : base(message) { }
//	}

//	// Placeholder types for BitStorage/Reader and Serializer entry points.
//	// Replace these with your actual implementations.
//	public sealed class BitStorage { /* writer implementation */ }
//	public sealed class BitStorageReader { /* reader implementation */ }

//	public static class Serializer
//	{
//		// These are placeholders. Replace with your serializer implementation.
//		public static void SerializeObject(object obj, BitStorage storage, SerializerContext ctx)
//		{
//			// Implementation should follow the selection rules:
//			// - ICustomBitSerializable -> call custom methods
//			// - metadata-driven -> use ctx.Metadata.GetTypeMetadata
//			// - converter -> use converter
//			throw new NotImplementedException("Serializer.SerializeObject must be implemented by the library.");
//		}

//		public static object? DeserializeObject(Type type, BitStorageReader reader, SerializerContext ctx)
//		{
//			throw new NotImplementedException("Serializer.DeserializeObject must be implemented by the library.");
//		}
//	}
//	/// <summary>
//	/// Builds FieldMetadata and TypeMetadata from types annotated with BitFieldAttribute.
//	/// </summary>
//	public sealed class FieldMetadataBuilder
//	{
//		private readonly SerializerOptions _options;
//		private readonly IServiceProvider? _services;

//		public FieldMetadataBuilder(SerializerOptions options, IServiceProvider? services = null)
//		{
//			_options = options ?? throw new ArgumentNullException(nameof(options));
//			_services = services ?? options.Services;
//		}

//		/// <summary>
//		/// Build TypeMetadata for a given type. Performs preflight validation and returns immutable metadata.
//		/// Throws SerializationException on schema errors.
//		/// </summary>
//		public TypeMetadata BuildTypeMetadata(Type type)
//		{
//			if (type == null) throw new ArgumentNullException(nameof(type));

//			var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
//							.Select(p => new { Prop = p, Attr = p.GetCustomAttribute<BitFieldAttribute>() })
//							.Where(x => x.Attr != null)
//							.Select(x => new { x.Prop, Attr = x.Attr!, Order = x.Attr!.Order ?? x.Prop.MetadataToken })
//							.OrderBy(x => x.Order)
//							.ToArray();

//			var fields = new List<FieldMetadata>(props.Length);
//			foreach (var p in props)
//			{
//				var fm = BuildFieldMetadata(p.Prop, p.Attr!);
//				fields.Add(fm);
//			}

//			return new TypeMetadata
//			{
//				Type = type,
//				FieldsInOrder = fields.ToArray()
//			};
//		}

//		private FieldMetadata BuildFieldMetadata(PropertyInfo prop, BitFieldAttribute attr)
//		{
//			if (prop == null) throw new ArgumentNullException(nameof(prop));
//			if (attr == null) throw new ArgumentNullException(nameof(attr));

//			var name = prop.Name;
//			var propertyType = prop.PropertyType;
//			var isEnumerable = TryGetEnumerableElementType(propertyType, out var elemType);

//			// Resolve Bits
//			int resolvedBits;
//			if (attr.Bits.HasValue)
//			{
//				resolvedBits = attr.Bits.Value;
//				if (resolvedBits < 1 || resolvedBits > 64)
//					throw new SerializationException($"Field '{name}': Bits must be between 1 and 64.");
//			}
//			else
//			{
//				// infer native width
//				if (isEnumerable)
//				{
//					if (elemType == null)
//						throw new SerializationException($"Field '{name}': enumerable element type could not be resolved.");
//					resolvedBits = GetTypeWidthOrThrow(elemType, $"Field '{name}' element type");
//				}
//				else
//				{
//					resolvedBits = GetTypeWidthOrThrow(propertyType, $"Field '{name}'");
//				}
//			}

//			// Enumerable rules
//			if (isEnumerable)
//			{
//				bool hasCount = attr.CountBitLength.HasValue;
//				bool hasTerm = attr.TerminatorValue.HasValue;
//				if (hasCount == hasTerm)
//				{
//					throw new SerializationException($"Field '{name}': for IEnumerable<T> provide exactly one of CountBitLength or TerminatorValue.");
//				}

//				if (hasCount)
//				{
//					var cb = attr.CountBitLength!.Value;
//					if (cb < 1 || cb > 32)
//						throw new SerializationException($"Field '{name}': CountBitLength must be between 1 and 32.");
//				}

//				if (hasTerm)
//				{
//					var term = attr.TerminatorValue!.Value;
//					if (!TerminatorFits(term, resolvedBits))
//						throw new SerializationException($"Field '{name}': TerminatorValue does not fit in element width {resolvedBits}.");
//				}
//			}

//			// Compute representable ranges
//			var (signedMin, signedMax) = SignedRange(resolvedBits);
//			var unsignedMax = UnsignedMax(resolvedBits);
//			bool hasRange = true;

//			// Validate Min/Max if provided
//			if (attr.Min.HasValue || attr.Max.HasValue)
//			{
//				if (attr.Min.HasValue && attr.Max.HasValue && attr.Min.Value > attr.Max.Value)
//					throw new SerializationException($"Field '{name}': Min > Max.");

//				if (attr.Signed)
//				{
//					if (attr.Min.HasValue && (attr.Min.Value < signedMin || attr.Min.Value > signedMax))
//						throw new SerializationException($"Field '{name}': Min is out of representable signed range [{signedMin},{signedMax}].");
//					if (attr.Max.HasValue && (attr.Max.Value < signedMin || attr.Max.Value > signedMax))
//						throw new SerializationException($"Field '{name}': Max is out of representable signed range [{signedMin},{signedMax}].");
//				}
//				else
//				{
//					if (attr.Min.HasValue && (attr.Min.Value < 0 || (ulong)attr.Min.Value > unsignedMax))
//						throw new SerializationException($"Field '{name}': Min is out of representable unsigned range [0,{unsignedMax}].");
//					if (attr.Max.HasValue && (attr.Max.Value < 0 || (ulong)attr.Max.Value > unsignedMax))
//						throw new SerializationException($"Field '{name}': Max is out of representable unsigned range [0,{unsignedMax}].");
//				}
//			}

//			// Default validation (always validated)
//			if (attr.Default != null)
//			{
//				if (!IsAssignableOrConvertible(attr.Default, propertyType))
//					throw new SerializationException($"Field '{name}': Default value is not assignable to property type {propertyType}.");

//				// Validate numeric range for default
//				if (!ValueFitsRange(attr.Default, attr.Signed, signedMin, signedMax, unsignedMax))
//					throw new SerializationException($"Field '{name}': Default value does not fit in the representable range for {resolvedBits}-bit {(attr.Signed ? "signed" : "unsigned")} field.");
//			}

//			// Resolve Condition instance if provided (validate type)
//			IFieldCondition? conditionInstance = null;
//			if (attr.ConditionalType != null)
//			{
//				if (!typeof(IFieldCondition).IsAssignableFrom(attr.ConditionalType))
//					throw new SerializationException($"Field '{name}': ConditionalType must implement IFieldCondition.");
//				conditionInstance = ResolveConditionInstance(attr.ConditionalType);
//			}

//			// Resolve converter instance if provided (no strict interface check here; builder stores instance)
//			object? converterInstance = null;
//			if (attr.ConverterType != null)
//			{
//				converterInstance = ResolveConverterInstance(attr.ConverterType);
//			}

//			// Create getter/setter delegates
//			var getter = CreateGetter(prop);
//			var setter = CreateSetter(prop);

//			// Determine order (if attribute.Order null, we used MetadataToken earlier in caller)
//			int order = attr.Order ?? prop.MetadataToken;

//			// Build FieldMetadata
//			var fm = new FieldMetadata
//			{
//				Name = name,
//				Property = prop,
//				Getter = getter,
//				Setter = setter,
//				ResolvedBits = resolvedBits,
//				Signed = attr.Signed,
//				Optional = attr.Optional,
//				DefaultValue = attr.Default,
//				Order = order,
//				HasRange = hasRange,
//				SignedMin = signedMin,
//				SignedMax = signedMax,
//				UnsignedMax = unsignedMax,
//				PropertyType = propertyType,
//				IsEnumerable = isEnumerable,
//				ElementType = elemType,
//				ConditionInstance = conditionInstance,
//				ConverterInstance = converterInstance,
//				Description = attr.Description
//			};

//			return fm;
//		}

//		// -------------------------
//		// Helpers
//		// -------------------------

//		private static bool TerminatorFits(ulong term, int bits)
//		{
//			if (bits == 64) return true;
//			return term <= ((1UL << bits) - 1UL);
//		}

//		private static (long min, long max) SignedRange(int bits)
//		{
//			if (bits == 64) return (long.MinValue, long.MaxValue);
//			long min = -(1L << (bits - 1));
//			long max = (1L << (bits - 1)) - 1;
//			return (min, max);
//		}

//		private static ulong UnsignedMax(int bits)
//		{
//			if (bits == 64) return ulong.MaxValue;
//			return (1UL << bits) - 1UL;
//		}

//		private static bool ValueFitsRange(object value, bool signed, long signedMin, long signedMax, ulong unsignedMax)
//		{
//			if (value == null) return false;
//			try
//			{
//				if (signed)
//				{
//					long v = Convert.ToInt64(value);
//					return v >= signedMin && v <= signedMax;
//				}
//				else
//				{
//					ulong uv = Convert.ToUInt64(value);
//					return uv <= unsignedMax;
//				}
//			}
//			catch
//			{
//				return false;
//			}
//		}

//		private static bool IsAssignableOrConvertible(object value, Type targetType)
//		{
//			if (value == null) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
//			var valType = value.GetType();
//			if (targetType.IsAssignableFrom(valType)) return true;
//			try
//			{
//				// attempt simple convert
//				Convert.ChangeType(value, Nullable.GetUnderlyingType(targetType) ?? targetType);
//				return true;
//			}
//			catch
//			{
//				return false;
//			}
//		}

//		private IFieldCondition ResolveConditionInstance(Type conditionType)
//		{
//			var svc = _services?.GetService(conditionType) as IFieldCondition;
//			if (svc != null) return svc;
//			var inst = Activator.CreateInstance(conditionType) as IFieldCondition;
//			if (inst == null) throw new SerializationException($"Unable to instantiate condition type {conditionType}.");
//			return inst;
//		}

//		private object ResolveConverterInstance(Type converterType)
//		{
//			var svc = _services?.GetService(converterType);
//			if (svc != null) return svc;
//			var inst = Activator.CreateInstance(converterType);
//			if (inst == null) throw new SerializationException($"Unable to instantiate converter type {converterType}.");
//			return inst;
//		}

//		private static Func<object, object?> CreateGetter(PropertyInfo prop)
//		{
//			var instance = Expression.Parameter(typeof(object), "instance");
//			var convertInstance = Expression.Convert(instance, prop.DeclaringType!);
//			var propertyAccess = Expression.Property(convertInstance, prop);
//			var convertResult = Expression.Convert(propertyAccess, typeof(object));
//			var lambda = Expression.Lambda<Func<object, object?>>(convertResult, instance);
//			return lambda.Compile();
//		}

//		private static Action<object, object?> CreateSetter(PropertyInfo prop)
//		{
//			var instance = Expression.Parameter(typeof(object), "instance");
//			var value = Expression.Parameter(typeof(object), "value");
//			var convertInstance = Expression.Convert(instance, prop.DeclaringType!);
//			var convertValue = Expression.Convert(value, prop.PropertyType);
//			var propertyAccess = Expression.Property(convertInstance, prop);
//			var assign = Expression.Assign(propertyAccess, convertValue);
//			var lambda = Expression.Lambda<Action<object, object?>>(assign, instance, value);
//			return lambda.Compile();
//		}

//		private static int GetTypeWidthOrThrow(Type t, string context)
//		{
//			var w = GetTypeWidth(t);
//			if (w == null)
//				throw new SerializationException($"{context}: cannot infer native width for type {t} (provide Bits or a converter).");
//			return w.Value;
//		}

//		/// <summary>
//		/// Returns native width in bits for common primitive/integral types and enums.
//		/// Returns null for reference types, complex types, or types that require converters.
//		/// </summary>
//		private static int? GetTypeWidth(Type t)
//		{
//			if (t.IsEnum)
//			{
//				var underlying = Enum.GetUnderlyingType(t);
//				return GetTypeWidth(underlying);
//			}

//			if (t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(char))
//				return 8;
//			if (t == typeof(short) || t == typeof(ushort))
//				return 16;
//			if (t == typeof(int) || t == typeof(uint))
//				return 32;
//			if (t == typeof(long) || t == typeof(ulong))
//				return 64;

//			// Nullable<T> -> width of T
//			var nt = Nullable.GetUnderlyingType(t);
//			if (nt != null) return GetTypeWidth(nt);

//			// For other primitives like float/double, you may choose to treat them as 32/64 bits
//			if (t == typeof(float)) return 32;
//			if (t == typeof(double)) return 64;

//			return null;
//		}

//		private static bool TryGetEnumerableElementType(Type t, out Type? elementType)
//		{
//			elementType = null;
//			if (t == typeof(string)) return false; // string is IEnumerable<char> but treat as scalar unless converter provided

//			if (t.IsArray)
//			{
//				elementType = t.GetElementType();
//				return true;
//			}

//			// Look for IEnumerable<T>
//			var ifaces = t.GetInterfaces().Concat(new[] { t });
//			foreach (var iface in ifaces)
//			{
//				if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
//				{
//					elementType = iface.GetGenericArguments()[0];
//					return true;
//				}
//			}

//			return false;
//		}
//	}

//	/// <summary>
//	/// Simple thread-safe in-memory metadata cache that uses FieldMetadataBuilder.
//	/// </summary>
//	public sealed class InMemoryMetadataCache : IMetadataCache
//	{
//		private readonly FieldMetadataBuilder _builder;
//		private readonly ConcurrentDictionary<Type, TypeMetadata?> _cache = new();

//		public InMemoryMetadataCache(SerializerOptions options, IServiceProvider? services = null)
//		{
//			_builder = new FieldMetadataBuilder(options, services);
//		}

//		public TypeMetadata? GetTypeMetadata(Type type)
//		{
//			return _cache.GetOrAdd(type, t =>
//			{
//				// Build metadata; if no fields, return a TypeMetadata with empty fields
//				var meta = _builder.BuildTypeMetadata(t);
//				return meta;
//			});
//		}

//		public bool TryGetField(Type type, string fieldName, out FieldMetadata? field)
//		{
//			field = null;
//			var meta = GetTypeMetadata(type);
//			if (meta == null) return false;
//			field = meta.FieldsInOrder.FirstOrDefault(f => f.Name == fieldName);
//			return field != null;
//		}
//	}
//}
