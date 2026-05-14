using System;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
