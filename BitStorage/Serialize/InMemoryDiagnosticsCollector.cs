using System;
using System.Collections.Generic;
using System.Linq;

namespace GgoSoft.Serialize
{
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
}
