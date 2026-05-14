using System;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
