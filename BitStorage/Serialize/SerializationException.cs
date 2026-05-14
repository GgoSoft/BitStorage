using System;
using System.Linq;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// Exception type thrown for fatal schema or runtime serialization errors when strict behavior is required.
	/// </summary>
	public sealed class SerializationException : Exception
	{
		/// <summary>
		/// Create a new <see cref="SerializationException"/> with the specified message.
		/// </summary>
		/// <param name="message">Error message describing the failure.</param>
		public SerializationException(string message) : base(message) { }
	}
}
