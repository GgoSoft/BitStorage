using System;
using System.Collections.Generic;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
