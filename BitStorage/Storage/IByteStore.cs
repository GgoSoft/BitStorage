using System;
using System.Collections.Generic;

namespace GgoSoft.Storage
{

	internal interface IByteStore : IDisposable
	{
		void EnsureCapacity(int index);

		byte this[Index index] { get; set; }

		// Optional, only for memory-backed stores
		List<byte> GetRange(int start, int count);
		Span<byte> AsSpan();
		IByteStore Clone();
		void Clear();
		void Flush();
		bool IsAtEnd { get; set; }
		bool HasRandomAccess { get; }
		bool IsReadOnly { get; }
		bool IsWriteOnly { get; }
		byte[] Peek(int start, int count);
	}
}
