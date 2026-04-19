using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GgoSoft.Storage
{

	/// <summary>
	/// The BitStorage class provides functionality for reading and writing bits to a storage of bytes.
	/// It maintains indices for reading and writing bits and ensures that the storage length is updated accordingly.
	/// </summary>
	public class BitStorage
	{
		/// <summary>
		/// Represents an internal, mutable collection of bytes that supports indexed access, range operations, and
		/// enumeration.  The collection makes sure that indices are within valid bounds.  
		/// </summary>
		/// <remarks>This class is intended for internal use and provides methods for manipulating and accessing a
		/// sequence of bytes. It implements <see cref="IEnumerable{Byte}"/> to allow iteration over the contained bytes. The
		/// collection supports dynamic resizing and provides methods for efficient access to ranges and spans of
		/// data.</remarks>
		internal sealed class InternalData : IEnumerable<byte>
		{
			private List<byte> Data { get; } = new();
			internal byte this[Index index]
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

			internal List<byte> GetRange(int start, int totalBytes)
			{
				if(start < 0 || totalBytes < 0 || start + totalBytes > Data.Count)
				{
					throw new ArgumentOutOfRangeException(nameof(start), $"Cannot get range {start} to {start + totalBytes - 1} from storage of length {Data.Count}");
				}
				return Data.GetRange(start, totalBytes);
			}
			internal int Count => Data.Count;
			internal void Clear() => Data.Clear();
			internal Span<byte> AsSpan()
			{
				return CollectionsMarshal.AsSpan(Data);
			}

			// Helper method to ensure the capacity of the data list is at least byteIndex + 1
			internal void EnsureCapacity(int byteIndex)
			{
				if (byteIndex < 0) throw new ArgumentOutOfRangeException(nameof(byteIndex));
				if (byteIndex < Data.Count) return;
				// EnsureCapacity exists on List<T> (.NET Core/.NET 5+)
				int needed = byteIndex + 1;
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
			public IEnumerator<byte> GetEnumerator()
			{
				return ((IEnumerable<byte>)Data).GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return ((IEnumerable)Data).GetEnumerator();
			}
			internal InternalData Clone()
			{
				var copy = new InternalData();
				copy.Data.AddRange(this.Data);
				return copy;
			}
		}
		// This holds all the data types allowed and the size of them in bits
		internal static int GetTypeWidth<T>() => GetTypeWidth<T>();
		internal static int GetTypeWidth(Type T) => T switch
		{
			Type _ when T == typeof(int) => 32,
			Type _ when T == typeof(uint) => 32,
			Type _ when T == typeof(long) => 64,
			Type _ when T == typeof(ulong) => 64,
			Type _ when T == typeof(short) => 16,
			Type _ when T == typeof(ushort) => 16,
			Type _ when T == typeof(byte) => 8,
			Type _ when T == typeof(sbyte) => 8,
			Type _ when T == typeof(char) => 16,
			_ => throw new NotSupportedException($"Type {T} is not supported")
		};

		//// Index of the bit within the current byte being written to. This will go down as each bit is written and
		//// will reset to the last bit of the next storage element if the write index is less than 0
		private int _writeBitIndex = StorageElementLength - 1;
		// Index of the byte currently being written to
		private int _writeByteIndex;
		// List of bytes to store the bits
		internal InternalData data = new();

		/// <summary>
		/// Initializes a new instance of the BitStorage class with an empty storage.
		/// </summary>
		public BitStorage()
		{
		}

		/// <summary>
		/// Makes a copy of the specified BitStorage instance.
		/// </summary>
		/// <param name="bits">The initial data to store</param>
		public BitStorage(BitStorage bits)
		{
			this.Write(bits);
		}

		/// <summary>
		/// Creates a new instance of <see cref="BitStorage"/> and writes the specified bits to it.
		/// </summary>
		/// <typeparam name="T">The type of the elements in the <paramref name="bits"/> collection. Must be a value type.</typeparam>
		/// <param name="bits">A collection of bits to be written to the <see cref="BitStorage"/> instance.</param>
		/// <param name="bitsToWrite">The total number of bits to write. If null, all bits in the <paramref name="bits"/> collection are written.</param>
		/// <param name="elementBitsToWrite">The number of bits to write per element in the <paramref name="bits"/> collection. If null, all bits of each
		/// element are written.</param>
		/// <returns>A new <see cref="BitStorage"/> instance containing the written bits.</returns>
		[Pure]
		public static BitStorage Create<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null) where T : struct
		{
			BitStorage newStorage = new();
			newStorage.Write(bits, bitsToWrite, elementBitsToWrite);
			return newStorage;
		}

		/// <summary>
		/// Creates a <see cref="BitStorageReader"/> instance that provides read-only access to the underlying bit storage data.
		/// </summary>
		/// <returns>
		/// A new <see cref="BitStorageReader"/> instance bound to this <see cref="BitStorage"/>.
		/// </returns>
		public BitStorageReader CreateReader() => new (this);

		/// <summary>
		/// Creates a typed <see cref="BitStorageValueReader{T}"/> using the specified bit width.
		/// </summary>
		/// <typeparam name="T">The value type to decode from the bitstream.</typeparam>
		/// <param name="bitsPerValue">The number of bits used to encode each value.</param>
		/// <returns>
		/// A new <see cref="BitStorageValueReader{T}"/> instance bound to this <see cref="BitStorage"/>.
		/// </returns>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="bitsPerValue"/> is less than 1 or exceeds the maximum
		/// allowed bit width for <typeparamref name="T"/>.
		/// </exception>
		/// <exception cref="NotSupportedException">
		/// Thrown if <typeparamref name="T"/> does not have a registered default bit width
		/// in <see cref="TypeLengths"/>.
		/// </exception>
		/// <remarks>
		/// This overload creates a value reader beginning at bit index 0.
		/// For mid-stream sequences, use <see cref="BitStorageReader.CreateValueReader{T}(int)"/>.
		/// </remarks>
		public BitStorageValueReader<T> CreateValueReader<T>(int bitsPerValue, bool signed = false) where T : struct
		{
			return new BitStorageValueReader<T>(CreateReader(), bitsPerValue, signed);
		}

		/// <summary>
		/// Gets or sets the bits at the specified range in the storage using a boolean array.  This does not use the
		/// <see cref="this[int]"/> for speed reasons.
		/// </summary>
		/// <param name="range">The range of bits</param>
		/// <returns>A new boolean array holding the bits</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="range"/> is out of range</exception>
		public bool[] this[Range range]
		{
			set
			{
				var (start, end) = GetActualIndex(range);
				// Calculate the length of the range and check if the length of the array matches
				int length = end - start;
				if (length != value.Length)
				{
					throw new ArgumentOutOfRangeException(nameof(range), $"Range is specified as {range} ({length} bits), but length of array given is {value.Length}");
				}
				// get the data element location and bit mask for the start of the range, then loop over each item in the range
				var (element, bitMask) = GetLocation(start);
				for (int i = 0; i < length; i++)
				{
					// check if the bit is set in the data element and set the return value accordingly
					WriteBit(value[i], bitMask, element);
					// shift the bit mask to the right to get the next bit in the data element and reset the bit mask and element
					// index if it goes to 0
					bitMask >>= 1;
					if (bitMask == 0)
					{
						bitMask = 1 << (StorageElementLength - 1);
						element++;
					}
				}
			}
		}

		/// <summary>
		/// Gets or sets the bits at the specified index in the storage.  This does not use <see cref="this[Range]"/> for speed reasons
		/// </summary>
		/// <param name="index">The index of the bit requested</param>
		/// <returns>Bool representing the bit</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is out of range</exception>
		public bool this[Index index]
		{
			set
			{
				int convertedIndex = GetActualIndex(index);
				// get the data element location and bit mask for the index
				var (element, bitMask) = GetLocation(convertedIndex);
				// check if the bit is set in the supplied value and set the value accordingly
				WriteBit(value, bitMask, element);
			}
		}

		// Helper method to convert an Index to an actual index, taking into account the IsFromEnd property
		internal int GetActualIndex(Index index)
		{
			(int start, _) = GetActualIndex(Range.StartAt(index));
			return start;
		}

		// Helper method to convert a Range to actual start and end indices, taking into account the IsFromEnd property
		internal (int start, int end) GetActualIndex(Range range)
		{
			// Get the start and the end of the range, taking into account the IsFromEnd property
			int start = range.Start.IsFromEnd ? Count - range.Start.Value : range.Start.Value;
			int end = range.End.IsFromEnd ? Count - range.End.Value : range.End.Value;
			if (start < 0 || end > Count || start > end)
			{
				throw new ArgumentOutOfRangeException(nameof(range), $"Cannot get index {range} from storage of length {Count}");
			}
			return (start, end);
		}

		// Checks and updates the length of bits written to the storage.
		private void CheckMaxBits()
		{
			var writeIndex = WriteIndex; // WriteIndex is calculated, this makes it so it's only called once
			if (writeIndex > Count)
			{
				Count = writeIndex; // Update the length if the current write index exceeds it
			}
		}

		// Helper method to get the storage element and bit mask of the index
		internal static (int element, int bitMask) GetLocation(int index)
		{
			int element = index / StorageElementLength;
			int bitMask = 1 << (StorageElementLength - (index % StorageElementLength) - 1);
			return (element, bitMask);
		}

		// Helper method to get a mask length bits long, up to 64.  E.g. length = 6, this returns 0b111111
		internal static ulong GetMask(int length)
		{
			ulong returnResult = 0;
			if (length < 0 || length > 64)
			{
				throw new ArgumentOutOfRangeException(nameof(length), "Value must be between 0 and 64.");
			}
			if (length == 64)
			{
				returnResult = ulong.MaxValue;
			}
			else if (length != 0)
			{
				returnResult = (1UL << length) - 1;
			}
			return returnResult;
		}

		// Helper method to write a bool bit at a specific index
		private void WriteBit(bool bit, int mask, int byteIndex)
		{
			if (bit)
			{
				data[byteIndex] |= (byte)mask;
			}
			else
			{
				data[byteIndex] &= (byte)~mask;
			}
		}

		/// The number of bits in each element of the storage (e.g., byte = 8 bits).
		internal static int StorageElementLength { get; } = GetTypeWidth<byte>(); // Number of bits in a byte

		// The index of the next bit to be written within the current element
		private int WriteBitIndex
		{
			get
			{
				return _writeBitIndex;
			}
			set
			{
				// If the write bit index is less than 0, we need to reset the WriteBitIndex and move to the next byte
				if (value < 0)
				{
					_writeBitIndex = StorageElementLength - 1;
					WriteByteIndex++;
				}
				else if (value >= StorageElementLength)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"WriteBitIndex {value} cannot be greater than {StorageElementLength}");
				}
				else
				{
					_writeBitIndex = value;
				}
				CheckMaxBits(); // Ensure the length is updated if necessary
			}
		}

		// The index of the next byte to be written within the storage
		private int WriteByteIndex
		{
			get
			{
				return _writeByteIndex;
			}
			set
			{
				_writeByteIndex = value;
				CheckMaxBits(); // Ensure the length is updated if necessary
			}
		}

		/// <summary>
		/// Clears the storage and resets all indices.
		/// </summary>
		public void Clear()
		{
			data.Clear();
			WriteBitIndex = StorageElementLength - 1;
			WriteByteIndex = 0;
			Count = 0;
		}

		/// <summary>
		/// Returns the data stored as a List of bytes.
		/// </summary>
		/// <returns>A List of bytes representing the stored data.</returns>
		public List<byte> GetData()
		{
			// This is not needed, but slightly faster for the specific edge case.
			if (Count == 0)
			{
				return new();
			}
			// Calculate the total number of full/partial blocks required (ceiling division)
			var totalBytes = (Count + StorageElementLength - 1) / StorageElementLength;

			// Calculate the number of unused bits in the last element (0 if full block)
			var lastUnusedBits = (StorageElementLength - Count % StorageElementLength) % StorageElementLength;

			// Create a new List<byte> with the relevant data.
			var newData = data.GetRange(0, totalBytes);

			// If there are unused bits to mask off (i.e., not a full byte)
			if (lastUnusedBits > 0)
			{
				// Calculate the mask for the used bits
				var usedBitsMask = (byte)~GetMask(lastUnusedBits);

				// Mask off the unused bits from the last element
				newData[^1] &= usedBitsMask;
			}

			return newData;
		}

		/// <summary>
		/// Writes the bits from another BitStorage instance to this storage.
		/// </summary>
		/// <param name="bits">The BitStorage instance containing the bits to write.</param>
		public BitStorage Write(BitStorage bits)
		{
			int extraBits = bits.Count % StorageElementLength;
			// don't do anything if the data is empty
			if (bits.data.Count > 0)
			{
				// if the number of bits is a multiple of the storage element length, write the whole data
				if (extraBits == 0)
				{
					Write(bits.data);
				}
				else
				{
					// if the number of bits is not a multiple of the storage element length, write all but
					// the last element and then write the last element converted to big endian
					for (int i = 0; i < bits.data.Count - 1; i++)
					{
						Write(bits.data[i]);
					}
					Write(bits.data[^1] >> (StorageElementLength - extraBits), extraBits);
				}
			}
			return this;
		}

		/// <summary>
		/// Writes a single bit to the storage.
		/// </summary>
		/// <param name="bit">Bit to be written</param>
		private void Write(bool bit)
		{
			data.EnsureCapacity(WriteByteIndex);
			var mask = 1 << WriteBitIndex;

			WriteBit(bit, mask, WriteByteIndex);
			WriteBitIndex--;
		}

		/// <summary>
		/// Writes the specified values to the storage limited to the specified length in bits.
		/// E.g. if the values is a 4 byte Enumerable and length of 27, the first 3 bytes will be written as 8 bits each and the 
		/// first 3 bits (leftmost 3) will also be written.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="bits">The elements to be added</param>
		/// <param name="bitsToWrite">The total number of bits to write from the enumerable.
		/// This must be a non-negative value.  If this is null or greater than the number of bits
		/// total in the enum, all the bits will be written.</param>
		/// <param name="bitsPerElement">The number of bits for each element to write.
		/// This must be a non-negative value. If this is null or greater than the number of bits in
		/// each element, all bits in the element will be written, unless <paramref name="bitsToWrite"/> has been reached</param>
		/// <returns>The BitStorage current instance with the bits written, useful for method chaining</returns>
		/// <exception cref="ArgumentException">Thrown if the type is not valid</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range.</exception>
		public BitStorage Write<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? bitsPerElement = null) where T : struct
		{
			if (bitsToWrite < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToWrite), $"Number of Bits ({bitsToWrite}) cannot be less than 0");
			}

			// special edge case for boolean values.  This will take the boolean values, create a ulong with the appropriate bits
			// set and call the generic "Write{T}(IEnumerable{T}, int?)" method to write the bits.
			if (typeof(T) == typeof(bool))
			{
				if (bitsPerElement is not 1 and not null)
				{
					throw new ArgumentOutOfRangeException(nameof(bitsPerElement), $"Bits Per Element ({bitsPerElement}) must be 1");
				}
				return WriteBoolean(bits, bitsToWrite);
			}

			var typeWidth = GetTypeWidth<T>();
			// If the elementBitsToWrite (the number of bits to write for each element) is specified, use that,
			// otherwise use the type length (all bits in each element).
			if (bitsPerElement != null)
			{
				if (bitsPerElement > typeWidth || bitsPerElement <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(bitsPerElement), $"Bits Per Element ({bitsPerElement}) must be between 1 and {typeWidth}");
				}
				typeWidth = (int)bitsPerElement;
			}

			// Since "bits" is an enumerable, the length is not known until the end.  The "bitsToWrite" is
			// going to be either null (write all bits) or a non-negative value. 

			int dataLength = 0;
			int numElements = 0;
			int extraBits = 0;
			int end = 0;

			// If there are more bits to write than the number of bits in the enumerable, write all bits in the enumerable.
			// Otherwise, calculate the number of elements and bits to write
			if (bitsToWrite is not null)
			{
				dataLength = bitsToWrite.Value;
				numElements = dataLength / typeWidth;
				extraBits = dataLength % typeWidth;
				end = (extraBits == 0 ? numElements : numElements + 1);
			}

			// Since there is no index in an enumerable, we need to keep track of the number of elements written
			int i = 0;
			foreach (var value in bits)
			{
				var iterValue = ToUInt64(value);
				// bitLength will be the size of the number until the last number, which will be the extra bits
				int bitLength = typeWidth;
				// If the "bitsToWrite" is not null, this will be true on the last element to write
				if (bitsToWrite is not null && i == numElements)
				{
					iterValue >>= bitLength - extraBits; // Shift the value to the right to remove unwanted bits
					bitLength = extraBits;
				}
				Write(iterValue, bitLength);
				i++;
				// If the length is specified and the end of the data is reached, break out of the loop.
				if (bitsToWrite is not null && i >= end)
				{
					break;
				}
			}
			return this;
		}

		/// <summary>
		/// Writes a sequence of boolean values to the bit storage, grouping them according to the storage element's bit
		/// length.
		/// </summary>
		/// <remarks>If the number of bits written is not a multiple of the storage element's bit length, the
		/// remaining bits are written as a final, partial group. This method processes the input in groups determined by the
		/// storage element's bit length.</remarks>
		/// <typeparam name="T">The type of the elements in the bits collection. Must be a value type that can be cast to Boolean.</typeparam>
		/// <param name="bits">An enumerable collection of values representing bits to write. Each value is interpreted as a Boolean.</param>
		/// <param name="bitsToWrite">The maximum number of bits to write from the collection, or null to write all available bits.</param>
		/// <returns>The current BitStorage instance, enabling method chaining.</returns>
		private BitStorage WriteBoolean<T>(IEnumerable<T> bits, int? bitsToWrite) where T : struct
		{
			ulong value = 0;
			int bitLength = GetTypeWidth<ulong>();
			int i = 0;
			bool wroteLast = false;
			foreach (var bit in bits)
			{
				// Shift the value to the left and add the bit
				value <<= 1;
				value += (bool)(object)bit ? 1UL : 0UL;
				i++;
				// If the number of bits is equal to the number of bits in the storage element, write the value
				// and reset it. "wroteLast" is used to determine if the last value was written, so that the
				// last value can be written if it is not a multiple of the number of bits
				if (i % bitLength == 0)
				{
					Write(value, bitLength);
					value = 0;
					wroteLast = true;
				}
				else
				{
					wroteLast = false;
				}
				// If the length is specified and the end of the data is reached, break out of the loop.  This
				// is used instead of data.Count() because the length of the data is not known until the end of
				// the enumeration
				if (i == bitsToWrite)
				{
					break;
				}
			}
			// If the last value was not written, write the last value
			if (!wroteLast)
			{
				Write(value, i % bitLength);
			}
			return this;
		}

		/// <summary>
		/// Writes the specified bits to the storage.
		/// </summary>
		/// <typeparam name="T">The data type of the number holding the bits to be written</typeparam>
		/// <param name="bits">The bits to write.</param>
		/// <param name="bitsToWrite">The number of bits to write.</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range.</exception>
		public BitStorage Write<T>(T bits, int? bitsToWrite = null) where T : struct
		{
			// Edge case for boolean values.
			if (bits is bool boolBit)
			{
				if (bitsToWrite > 1 || bitsToWrite < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(bitsToWrite), $"Number of bits ({bitsToWrite}) is out of range of 1");
				}
				if (bitsToWrite != 0)
				{
					Write(boolBit);
				}
				return this;
			}

			var typeWidth = GetTypeWidth<T>();

			if (bitsToWrite < 0 || bitsToWrite > typeWidth)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToWrite), $"Number of bits ({bitsToWrite}) is out of range of 0-{typeWidth}");
			}
			// tempLength is the number of bits left to write
			int tempLength = bitsToWrite ?? typeWidth;
			// The mask is the used to mask off unwanted bits. The mask will be all 1's for the number of bits requested
			// e.g. if the number of bits requested is 5, the mask will be 0b11111
			ulong mask = GetMask(tempLength);
			// tempBits holds the bits to be stored.  The bits will be removed (shifted) as they are written
			// The bits are converted to a ulong so they can be manipulated easier
			ulong tempBits = ToUInt64(bits);
			// This is probably not needed, but mask off the extra bits just in case
			tempBits &= mask;
			// The written bits may not align with the storage element boundaries.  This loop will write the number of bits available in the current
			// storage element, then write the next bits in the next storage element, etc.  The maximum number of loops should be the number of bits
			// requested / the number of bits in the storage element + 1. E.g. if the number of bits requested is 27 and the storage element is 8,
			// the maximum number of loops is 4
			while (tempLength > 0)
			{
				// tempWriteLength is the number of bits to write in this loop.  If the number of bits requested is greater than the number of bits
				// available in the current storage element, set the number of bits to the number of bits available in the current storage element
				int tempWriteLength = tempLength;
				if (tempWriteLength > WriteBitIndex)
				{
					tempWriteLength = WriteBitIndex + 1;
				}
				// Remove the number of bits written from the number of bits remaining
				tempLength -= tempWriteLength;
				// Shift the bits to the write by the remaining number of bits, this will leave the bits to be written in the right-most bits
				ulong writeBits = tempBits >> tempLength;
				// Shift the bits to be written to the left to put them in the appropriate position for the current storage element
				writeBits <<= (WriteBitIndex + 1 - tempWriteLength);
				// make a mask for the bits to be written
				ulong tempMask = (mask >> tempLength) << (WriteBitIndex + 1 - tempWriteLength);
				data.EnsureCapacity(WriteByteIndex);
				// Set the storage element bits to 0's in the position of the bits to be written
				data[WriteByteIndex] &= (byte)~tempMask;
				// Add the bits to be written to the storage element
				data[WriteByteIndex] += (byte)writeBits;
				// Update the WriteBitIndex
				WriteBitIndex -= tempWriteLength;
			}
			return this;
		}

		/// <summary>
		/// Gets the number of bits in the storage.
		/// </summary>
		public int Count { get; private set; } = 0;

		/// <summary>
		/// Gets or sets the write index in bits.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the write index is out of the valid range.</exception>
		public int WriteIndex
		{
			get
			{
				return WriteByteIndex * StorageElementLength + StorageElementLength - WriteBitIndex - 1;
			}
			set
			{
				if (value < 0 || value > Count)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"Invalid WriteIndex: {value}, values must be between 0 and {Count}");
				}
				_writeByteIndex = value / StorageElementLength;
				WriteBitIndex = StorageElementLength - value % StorageElementLength - 1;
			}
		}

		// helper method to adjust the read and write indices when an insert at end has been done.
		private BitStorage InsertAtEnd(Func<BitStorage> insert)
		{
			var writeIndex = WriteIndex;
			WriteIndex = Count;
			insert();
			WriteIndex = writeIndex;
			return this;
		}

		/// <summary>
		/// Inserts the specified <see cref="BitStorage"/> at the given index within the current <see cref="BitStorage"/> and
		/// returns the current <see cref="BitStorage"/>.
		/// </summary>
		/// <remarks>The method modifies the current <see cref="BitStorage"/> instance. The order of bits in the <see cref="BitStorage"/> instance
		/// is preserved, with the bits from the specified <paramref name="bits"/> inserted at the specified index.</remarks>
		/// <param name="index">The zero-based index at which the specified <see cref="BitStorage"/> will be inserted.  Must be between 0 and 
		/// <see cref="Count"/>, inclusive.</param>
		/// <param name="bits">The <see cref="BitStorage"/> to insert. Cannot be <see langword="null"/>.</param>
		/// <returns>The current instance, with the specified <see cref="BitStorage"/> inserted at the specified index.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="bits"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is less than 0 or greater than <see cref="Count"/>.</exception>
		public BitStorage Insert(int index, BitStorage bits)
		{
			if (index == Count)
			{
				return InsertAtEnd(() => Write(bits));
			}
			var bitStorage = InsertAsCopy(index, bits);
			var currentWriteIndex = WriteIndex;
			this.data = bitStorage.data.Clone();
			Count = bitStorage.Count;
			if (currentWriteIndex >= index)
			{
				WriteIndex = currentWriteIndex + bits.Count;
			}
			return this;
		}

		/// <summary>
		/// Inserts the specified bits at the given index within the current <see cref="BitStorage"/> and
		/// returns the current <see cref="BitStorage"/>.
		/// </summary>
		/// <remarks>The method modifies the current <see cref="BitStorage"/> instance. The order of bits in the <see cref="BitStorage"/> instance
		/// is preserved, with the bits from the specified <paramref name="bits"/> inserted at the specified index.</remarks>
		/// <typeparam name="T">The value type containing the bits to insert.</typeparam>
		/// <param name="index">The zero-based index at which to insert the bits.</param>
		/// <param name="bits">The value containing the bits to be inserted.</param>
		/// <param name="bitsToWrite">The number of bits to write from the value. If null, all bits of the value are written.</param>
		/// <returns>The current <see cref="BitStorage"/> instance with the specified bits inserted at the given index.</returns>
		public BitStorage Insert<T>(int index, T bits, int? bitsToWrite = null) where T : struct
		{
			if(index == Count)
			{
				return InsertAtEnd(() => Write(bits, bitsToWrite));
			}
			var storage = new BitStorage();
			storage.Write(bits, bitsToWrite);
			return Insert(index, storage);
		}

		/// <summary>
		/// Creates a copy of the current <see cref="BitStorage"/>, inserts the specified <see cref="BitStorage"/> at the given index and
		/// returns the new <see cref="BitStorage"/> containing the result.
		/// </summary>
		/// <remarks>The method does not modify the current <see cref="BitStorage"/> instance. Instead, it creates and
		/// returns  a new <see cref="BitStorage"/> with the specified bits inserted. The order of bits in the resulting  <see
		/// cref="BitStorage"/> is preserved, with the bits from the specified <paramref name="bits"/> inserted  at the
		/// specified index.</remarks>
		/// <param name="index">The zero-based index at which the specified <see cref="BitStorage"/> will be inserted.  Must be between 0 and <see
		/// cref="Count"/>, inclusive.</param>
		/// <param name="bits">The <see cref="BitStorage"/> to insert. Cannot be <see langword="null"/>.</param>
		/// <returns>A new <see cref="BitStorage"/> containing the bits from the current instance, with the specified  <see
		/// cref="BitStorage"/> inserted at the specified index.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="bits"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is less than 0 or greater than <see cref="Count"/>.</exception>
		[Pure]
		public BitStorage InsertAsCopy(int index, BitStorage bits)
		{
			if (bits is null) throw new ArgumentNullException(nameof(bits));
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");

			var reader = CreateReader();

			// Create result
			var result = new BitStorage();

			// starting from the beginning, read up to the index
			reader.ReadIndex = 0;
			var prefix = reader.ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// write the bits to be inserted
			result.Write(bits);

			// read the rest of the bits from the original storage
			var suffix = reader.ReadEnumerable<int>();

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, reader.LastReadBitCount);
			}
			return result;
		}

		/// <summary>
		/// Creates a copy of the current <see cref="BitStorage"/>, inserts the specified bits at the given index and
		/// returns the new <see cref="BitStorage"/> containing the result.
		/// </summary>
		/// <remarks>The method does not modify the current <see cref="BitStorage"/> instance. Instead, it creates and
		/// returns  a new <see cref="BitStorage"/> with the specified bits inserted. The order of bits in the resulting  <see
		/// cref="BitStorage"/> is preserved, with the bits from the specified <paramref name="bits"/> inserted  at the
		/// specified index.</remarks>
		/// <typeparam name="T">The value type containing the bits to insert.</typeparam>
		/// <param name="index">The zero-based index at which to insert the bits.</param>
		/// <param name="bits">The value containing the bits to be inserted.</param>
		/// <param name="bitsToWrite">The number of bits to write from the value. If null, all bits of the value are written.</param>
		/// <returns>A new BitStorage instance with the specified bits inserted at the given index.</returns>
		[Pure]
		public BitStorage InsertAsCopy<T>(int index, T bits, int? bitsToWrite = null) where T : struct
		{
			var storage = new BitStorage();
			storage.Write(bits, bitsToWrite);
			return InsertAsCopy(index, storage);
		}

		/// <summary>
		/// Removes a range of bits from the current storage and returns the current BitStorage instance.
		/// </summary>
		/// <param name="index">The zero-based starting position of the range to remove. Must be between 0 and Count, inclusive.</param>
		/// <param name="count">The number of bits to remove. Must be greater than or equal to 0, and the range defined by index and count must
		/// not exceed the total number of bits.</param>
		/// <returns>The currentBitStorage instance containing all existing bits except for those in the specified range.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when index or count is less than 0, index is greater than Count, or the range defined by index and count
		/// exceeds the total number of bits in the storage.</exception>
		public BitStorage RemoveRange(int index, int count)
		{
			var bitStorage = RemoveRangeAsCopy(index, count);
			var currentWriteIndex = WriteIndex;
			this.data = bitStorage.data.Clone();
			Count = bitStorage.Count;
			if (currentWriteIndex >= index)
			{
				if (currentWriteIndex < index + count)
				{
					WriteIndex = index;
				}
				else
				{
					WriteIndex = currentWriteIndex - count;
				}
			}
			return this;
		}

		/// <summary>
		/// Removes a specified number of bits from the end of the bit storage and returns the current BitStorage instance.
		/// </summary>
		/// <param name="count">The number of bits to remove from the end. Must be greater than or equal to 0 and less than or equal to the
		/// current bit count.</param>
		/// <returns>The current BitStorage instance with the specified number of bits removed from the end.</returns>
		public BitStorage TrimEnd(int count)
		{
			// Remove "count" bits from the end of the storage
			return RemoveRange(Count - count, count);
		}

		/// <summary>
		/// Creates a new BitStorage object, removes a range of bits and returns the new BitStorage instance with the specified range
		/// excluded.
		/// </summary>
		/// <remarks>The original BitStorage instance remains unchanged. The returned BitStorage contains a copy of
		/// the bits with the specified range removed.</remarks>
		/// <param name="index">The zero-based starting position of the range to remove. Must be between 0 and Count, inclusive.</param>
		/// <param name="count">The number of bits to remove. Must be greater than or equal to 0, and the range defined by index and count must
		/// not exceed the total number of bits.</param>
		/// <returns>A new BitStorage instance containing all bits from the original storage except for those in the specified range.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when index or count is less than 0, index is greater than Count, or the range defined by index and count
		/// exceeds the total number of bits in the storage.</exception>
		[Pure]
		public BitStorage RemoveRangeAsCopy(int index, int count)
		{
			if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot be less than 0");
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");
			if (index + count > Count) throw new ArgumentOutOfRangeException(nameof(count), $"Range {index + count} exceeds storage length {Count}");

			var reader = CreateReader();

			// Create result
			BitStorage result;

			// if removing from the end, just create a new storage with the count reduced
			if (count + index == Count)
			{
				result = new(this);
				result.Count -= count;
				result.WriteIndex = result.Count;
				return result;
			}

			result = new BitStorage();
			// starting from the beginning, read up to the index
			reader.ReadIndex = 0;
			var prefix = reader.ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// skip the range to be removed
			reader.ReadIndex += count;

			// read the rest of the bits from the original storage
			var suffixLength = Count - (index + count);
			var suffix = reader.ReadEnumerable<int>(suffixLength);

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// set the result write index to 0
			result.WriteIndex = result.Count;
			return result;
		}
		/// <summary>
		/// Creates a new BitStorage object, removes a specified number of bits from the end, and returns the new BitStorage 
		/// instance with the specified bits excluded.
		/// </summary>
		/// <remarks>The original BitStorage instance remains unchanged. The returned BitStorage contains a copy of
		/// the bits with the specified range removed.</remarks>
		/// <param name="count">The number of bits to remove from the end. Must be greater than or equal to 0 and less than or equal to the
		/// current bit count.</param>
		/// <returns>A new BitStorage instance with the specified number of bits removed from the end.</returns>
		[Pure]
		public BitStorage TrimEndAsCopy(int count)
		{
			// Remove "count" bits from the end of the storage
			return RemoveRangeAsCopy(Count - count, count);
		}

		[ExcludeFromCodeCoverage]
		public string PrintBits(int start = 0, int? end = null)
		{
			int byteStart = start / StorageElementLength;
			end ??= Count;
			int byteEnd = (end.Value + StorageElementLength - 1) / StorageElementLength;
			System.Text.StringBuilder sb = new();
			System.Text.StringBuilder footer = new();
			for (int i = byteStart; i < byteEnd; i++)
			{
				var padded = Convert.ToString(data[i], 2).PadLeft(StorageElementLength, '0');
				var extraBits = Count % StorageElementLength;
				var elementData = data[i];
				if (i == byteEnd - 1 && extraBits > 0)
				{
					var mask = (byte)(GetMask(8) & ~GetMask(StorageElementLength - extraBits));
					elementData &= mask;
					extraBits = StorageElementLength - extraBits;
					padded = $"{padded[..^extraBits]}{new string('.', extraBits)}";
				}
				footer.Append($"{padded} ");
				sb.Append($"{padded}\t{elementData}{Environment.NewLine}");
			}

			sb.Append(footer.ToString().Trim(' ', '.'));
			Console.WriteLine(sb.ToString());
			return sb.ToString();
		}

		/// <summary>
		/// Determines whether the current BitStorage instance is equal to another object.
		/// </summary>
		/// <remarks>Equality is determined by comparing the number of bits and the values of all bits in both
		/// instances. Any unused bits in the underlying storage are ignored during the comparison. This method provides
		/// value-based equality rather than reference equality.</remarks>
		/// <param name="obj">The object to compare with the current BitStorage instance.</param>
		/// <returns>true if the specified object is a BitStorage instance with the same number of bits and identical bit values;
		/// otherwise, false.</returns>
		public bool ContentEquals(BitStorage? other)
		{
			// Quick check for null or different counts
			if (other is null || other.Count != Count)
			{
				return false;
			}

			// quick check for empty storage
			if (Count == 0)
			{
				return true;
			}

			// There could be stuff in the last storage element that is not part of the data.  RemoveRange may just change the Count
			// and not clear the remaining data, so we need to only compare the bits that are part of the data.
			int fullElements = Count / StorageElementLength;
			int remainingBits = Count % StorageElementLength;

			var originalDataSpan = data.AsSpan();
			var otherDataSpan = other.data.AsSpan();
			int totalElements = remainingBits == 0 ? fullElements : fullElements + 1;
			

			// If the last storage element is the same, we can check all of the data
			if (other.data[totalElements - 1] == data[totalElements - 1])
			{
				return originalDataSpan[0..totalElements].SequenceEqual(otherDataSpan[0..totalElements]);
			}

			// If the last storage element is not the same and there are no remaining bits, they are not equal
			if (remainingBits == 0)
			{
				return false;
			}

			// The last storage element isn't the same, but it may be that the bits that are part of the data are the same
			if (data[fullElements] >> (StorageElementLength - remainingBits) != (other.data[fullElements] >> (StorageElementLength - remainingBits)))
			{
				return false;
			}

			// The last part of the data is the same, so check all of the full elements, this *should* be faster than checking each byte
			return originalDataSpan[0..fullElements].SequenceEqual(otherDataSpan[0..fullElements]);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ulong ToUInt64<T>(T value) where T : struct
		{
			if (typeof(T) == typeof(byte))   return (byte)(object)value;
			if (typeof(T) == typeof(ushort)) return (ushort)(object)value;
			if (typeof(T) == typeof(uint))   return (uint)(object)value;
			if (typeof(T) == typeof(ulong))  return (ulong)(object)value;
			if (typeof(T) == typeof(char))   return (char)(object)value;

			if (typeof(T) == typeof(sbyte))  return (ulong)(sbyte)(object)value;
			if (typeof(T) == typeof(short))  return (ulong)(short)(object)value;
			if (typeof(T) == typeof(int))    return (ulong)(int)(object)value;
			if (typeof(T) == typeof(long))   return (ulong)(long)(object)value;

			throw new NotSupportedException($"Type {typeof(T)} is not supported.");
		}
	}
}
