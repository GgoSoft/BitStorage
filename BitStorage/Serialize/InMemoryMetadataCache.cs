using System;
using System.Collections.Concurrent;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
