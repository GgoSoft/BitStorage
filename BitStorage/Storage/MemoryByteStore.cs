using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace GgoSoft.Storage
{

	/// <summary>
	/// Represents an internal, mutable collection of bytes that supports indexed access, range operations, and
	/// enumeration.  The collection makes sure that indices are within valid bounds.  
	/// </summary>
	/// <remarks>This class is intended for internal use and provides methods for manipulating and accessing a
	/// sequence of bytes. </remarks>
	internal sealed class MemoryByteStore : IByteStore
	{
		private List<byte> Data { get; } = new();
		public byte this[Index index]
		{
			get
			{
				int actualIndex = GetActualIndex(index);
				return Data[actualIndex];
			}
			set
			{
				int actualIndex = GetActualIndex(index);
				Data[actualIndex] = value;
			}
		}
		private int GetActualIndex(Index index)
		{
			int start = index.GetOffset(Data.Count);
			if (start < 0 || start >= Data.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(index), $"Cannot get index {index} from storage of length {Data.Count}");
			}
			return start;
		}

		public List<byte> GetRange(int start, int count)
		{
			if (start < 0 || count < 0 || start + count > Data.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(start), $"Cannot get range {start} to {start + count - 1} from storage of length {Data.Count}");
			}
			return Data.GetRange(start, count);
		}
		//public int Count => Data.Count;
		public void Clear() => Data.Clear();
		public Span<byte> AsSpan()
		{
			return CollectionsMarshal.AsSpan(Data);
		}

		// Helper method to ensure the capacity of the data list is at least byteIndex + 1
		public void EnsureCapacity(int index)
		{
			if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
			if (index < Data.Count) return;
			// EnsureCapacity exists on List<T> (.NET Core/.NET 5+)
			int needed = index + 1;
			if (Data.Capacity < needed)
			{
				int newCap = Math.Max(Data.Capacity == 0 ? 4 : Data.Capacity * 2, needed);
				Data.Capacity = newCap;
			}
			// add the missing bytes in one allocation
			int toAdd = needed - Data.Count;
			if (toAdd > 0)
			{
				Data.AddRange(Enumerable.Repeat((byte)0, toAdd));
			}
		}
		public IByteStore Clone()
		{
			var copy = new MemoryByteStore();
			copy.Data.AddRange(this.Data);
			return copy;
		}
		public void Flush()
		{
			// nothing needs to be flushed for a memory byte store
		}
		public void Dispose()
		{
			// nothing needs to be disposed
		}
		public bool IsAtEnd { get; set; } // not used for memory read/write

		public bool HasRandomAccess => true;

		public bool IsReadOnly => false;

		public bool IsWriteOnly => false;
		public byte[] Peek(int start, int count)
		{
			if (count < 0)
				throw new ArgumentOutOfRangeException(nameof(count));

			return Data.Skip(start).Take(count).ToArray();
		}
	}
}
