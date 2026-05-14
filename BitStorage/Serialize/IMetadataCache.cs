using System;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
