using System;
using System.Collections.Generic;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
