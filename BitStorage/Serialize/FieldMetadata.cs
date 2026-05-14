using System;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// Immutable metadata describing a single serializable field.
	/// Produced by <see cref="FieldMetadataBuilder"/> and consumed by the serializer at runtime.
	/// </summary>
	public sealed class FieldMetadata
	{
		// identity
		/// <summary>
		/// The CLR name of the field/property.
		/// </summary>
		public required string Name { get; init; }

		/// <summary>
		/// The original <see cref="PropertyInfo"/> for the property. May be null in some synthetic scenarios.
		/// </summary>
		public required PropertyInfo Property { get; init; }

		// raw attribute (single source of truth for user-supplied config)
		/// <summary>
		/// Gets the raw attribute that provides user-supplied configuration for the field.
		/// </summary>
		/// <remarks>This property is nullable. If no attribute is provided by the user, the value may be
		/// null.</remarks>
		public required BitFieldAttribute Attribute { get; init; }

		public TypeResolution TypeResolution { get; set; }
		public Type FieldType
		{
			get
			{
				return TypeResolution.FieldType ?? throw new SerializationException($"Field '{Name}': Cannot resolve type to valid data type");
			}
		}
		public bool IsCustomSerializer { get; set; }

		// resolved values (what runtime actually uses)
		public int ResolvedOrder { get; set; }                 // computed once
		public int ResolvedBits { get; set; }                 // final bit width (element or scalar)
		public bool ResolvedSigned { get => TypeResolution.Signed; }               // final signedness
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
			return $"{Name} ({Property?.Name}) bits={ResolvedBits} signed={ResolvedSigned} optional={Attribute?.Optional}";
		}

		/// <summary>
		/// Pre-resolved accessors for the property. The builder can populate this when AllowNonPublicAccess is enabled
		/// </summary>
		public Accessors Accessors { get; set; }
	}
}
