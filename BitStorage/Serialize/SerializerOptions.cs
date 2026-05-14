using System;
using System.Linq;

namespace GgoSoft.Serialize
{
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

		/// <summary>
		/// Gets or sets a value indicating whether the item is signed.
		/// </summary>
		/// <remarks>A value of <see langword="null"/> indicates that signed should be inferred from the type.</remarks>
		public bool? Signed { get; set; } = null;
	}
}
