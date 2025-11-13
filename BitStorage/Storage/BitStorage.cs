using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;

// TODO: all writes should return BitStorage (this) for chaining, insert and remove would get confusing because they return new objects instead of the current
//       so they should be named CloneWithInsert and CloneWithRemove or something similar, or make them modify the current object instead of returning a new one

namespace GgoSoft.Storage
{

	/// <summary>
	/// The BitStorage class provides functionality for reading and writing bits to a storage of bytes.
	/// It maintains indices for reading and writing bits and ensures that the storage length is updated accordingly.
	/// </summary>
	public class BitStorage
	{
		/// <summary>
		/// Contains a single int called 'BitsReadCount'.  This is another hack (similar to 'LastReadBitCount'). When 
		/// reading through the enumerable, if an object of this type has been sent to the method, the 'BitsReadCount' 
		/// will be updated.  The reason for this is a yield return doesn't allow multiple values, out, or ref objects. 
		/// </summary>
		public class BitsRead
		{
			/// <summary>
			/// Number of bits read in the last read operation
			/// </summary>
			public int BitsReadCount { get; set; } = 0;
		}
		// This holds all the data types allowed and the size of them in bits
		private static readonly ImmutableDictionary<Type, int> TypeLengths = ImmutableDictionary.CreateRange(
			new KeyValuePair<Type, int>[] {
				new ( typeof(ulong), sizeof(ulong) * 8),
				new ( typeof(uint), sizeof(uint) * 8),
				new ( typeof(ushort), sizeof(ushort) * 8),
				new ( typeof(char), sizeof(char) * 8),
				new ( typeof(byte), sizeof(byte) * 8),
				new ( typeof(long), sizeof(long) * 8 - 1), // Only positive numbers are allowed, so -1
				new ( typeof(int), sizeof(int) * 8 - 1), // Only positive numbers are allowed, so -1
				new ( typeof(short), sizeof(short) * 8 - 1), // Only positive numbers are allowed, so -1
				new ( typeof(sbyte), sizeof(sbyte) * 8 - 1) // Only positive numbers are allowed, so -1
			});
		// Index of the bit within the current byte being read. This will go down as each bit is read and will reset
		// to the last bit of the next storage element if the read index is less than 0
		private int _readBitIndex = StorageElementLength - 1;
		// Index of the byte currently being read
		private int _readByteIndex;
		// Index of the bit within the current byte being written to. This will go down as each bit is written and
		// will reset to the last bit of the next storage element if the write index is less than 0
		private int _writeBitIndex = StorageElementLength - 1;
		// Index of the byte currently being written to
		private int _writeByteIndex;
		// List of bytes to store the bits
		private readonly List<byte> data = new();

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
		public static BitStorage BitStorageFactory<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null) where T : struct
		{
			BitStorage newStorage = new();
			newStorage.Write(bits, bitsToWrite, elementBitsToWrite);
			return newStorage;
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
			get
			{
				var (start, end) = GetActualIndex(range);
				// Calculate the length of the range and create the return array
				int length = end - start;
				bool[] returnValue = new bool[length];
				// get the data element location and bit mask for the start of the range, then loop over each item in the range
				var (element, bitMask) = GetLocation(start);
				for (int i = 0; i < length; i++)
				{
					// check if the bit is set in the data element and set the return value accordingly
					returnValue[i] = (data[element] & bitMask) > 0;
					// shift the bit mask to the right to get the next bit in the data element and reset the bit mask and element
					// index if it goes to 0
					bitMask >>= 1;
					if (bitMask == 0)
					{
						bitMask = 1 << (StorageElementLength - 1);
						element++;
					}
				}
				return returnValue;
			}
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
			get
			{
				int convertedIndex = GetActualIndex(index);
				// get the data element location and bit mask for the index
				var (element, bitMask) = GetLocation(convertedIndex);
				// check if the bit is set in the data element and return the result
				return (data[element] & bitMask) != 0;
			}
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
		private int GetActualIndex(Index index)
		{
			(int start, _) = GetActualIndex(Range.StartAt(index));
			return start;
		}

		// Helper method to convert a Range to actual start and end indices, taking into account the IsFromEnd property
		private (int start, int end) GetActualIndex(Range range)
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
		private static (int element, int bitMask) GetLocation(int index)
		{
			int element = index / StorageElementLength;
			int bitMask = 1 << (StorageElementLength - (index % StorageElementLength) - 1);
			return (element, bitMask);
		}

		// Helper method to get a mask length bits long, up to 64.  E.g. length = 6, this returns 0b111111
		private static ulong GetMask(int length)
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
			if (byteIndex < 0 || byteIndex >= data.Count)
			{
				throw new ArgumentOutOfRangeException(nameof(byteIndex), $"Byte index {byteIndex} is out of range of the data storage");
			}
			if (bit)
			{
				data[byteIndex] |= (byte)mask;
			}
			else
			{
				data[byteIndex] &= (byte)~mask;
			}
		}

		// Helper method to ensure the capacity of the data list is at least byteIndex + 1
		private void EnsureCapacity(int byteIndex)
		{
			if (byteIndex < 0) throw new ArgumentOutOfRangeException(nameof(byteIndex));
			if (byteIndex < data.Count) return;
			// EnsureCapacity exists on List<T> (.NET Core/.NET 5+)
			int needed = byteIndex + 1;
			if (data.Capacity < needed)
			{
				int newCap = Math.Max(data.Capacity == 0 ? 4 : data.Capacity * 2, needed);
				data.Capacity = newCap;
			}
			// add the missing bytes in one allocation
			int toAdd = needed - data.Count;
			if (toAdd > 0)
			{
				data.AddRange(Enumerable.Repeat((byte)0, toAdd));
			}
		}

		/// The number of bits in each element of the storage (e.g., byte = 8 bits).
		private static int StorageElementLength { get; } = TypeLengths[typeof(byte)]; // Number of bits in a byte

		// The index of the next bit to be read within the current element
		private int ReadBitIndex
		{
			get
			{
				return _readBitIndex;
			}
			set
			{
				if (value < 0)
				{
					_readBitIndex = StorageElementLength - 1;
					_readByteIndex++;
				}
				else if (value >= StorageElementLength)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"ReadBitIndex {value} is cannot be greater than {StorageElementLength}");
				}
				else
				{
					_readBitIndex = value;
				}
			}
		}

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
					throw new ArgumentOutOfRangeException(nameof(value), $"WriteBitIndex {value} is cannot be greater than {StorageElementLength}");
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

		// Helper method to read a single boolean value
		private bool ReadBool()
		{
			if (ReadIndex >= Count)
			{
				LastReadBitCount = 0;
				return false;
			}
			int mask = 1 << ReadBitIndex;
			bool returnValue = (data[_readByteIndex] & mask) != 0;
			LastReadBitCount = 1;
			ReadBitIndex--;
			return returnValue;
		}

		// Extra method so the yield return can be used properly, otherwise, the thrown exception may not be
		// thrown until the enumeration is read
		private IEnumerable<T> ReadEnumerable<T>(int bitsToRead, int typeLength, BitsRead? bitsRead = null) where T : struct
		{
			int numBytes = bitsToRead / typeLength;
			int extraBits = bitsToRead % typeLength;
			int end = extraBits == 0 ? numBytes : numBytes + 1;
			// Loop through the number of items to read, read the bits and yield return the value
			for (int i = 0; i < end; i++)
			{
				int bitsReadCount = Read(out T returnValue, (i < numBytes ? typeLength : extraBits));
				if (bitsRead != null)
				{
					bitsRead.BitsReadCount = bitsReadCount;
				}
				yield return returnValue;
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
			ReadBitIndex = StorageElementLength - 1;
			_readByteIndex = 0;
			Count = 0;
		}

		/// <summary>
		/// Returns the data stored as an Enumerable of bytes.
		/// </summary>
		/// <returns>An Enumerable of bytes representing the stored data.</returns>
		public IEnumerable<byte> GetData()
		{
			return new List<byte>(data);
		}

		/// <summary>
		/// Reads the specified number of bits from the storage and returns them as a Enumerable of numbers.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="bitsToRead">The number of bits to read. Null will read all remaining bits</param>
		/// <param name="bitsRead"> The number of bits read. This is used to get the number of bits read in the enumerable</param>
		/// <returns>An Enumerable of values representing the read bits</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public IEnumerable<T> ReadEnumerable<T>(int? bitsToRead = null, BitsRead? bitsRead = null) where T : struct
		{
			if (bitsToRead < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToRead), $"Number of bits ({bitsToRead}) is out of range of 0-{Count}");
			}
			bitsToRead ??= Count - ReadIndex;
			Type tType = typeof(T);
			// Hack to find the last bit count read since the bits are read in an enumerable with yield return
			LastReadBitCount = 0;
			if (!TypeLengths.TryGetValue(tType, out int typeLength))
			{
				throw new ArgumentException($"Type {tType} is not supported");
			}
			return ReadEnumerable<T>(bitsToRead.Value, typeLength, bitsRead);
		}

		/// <summary>
		/// Wrapper method for <see cref="Read{T}(out T, int, bool)"/> to read a single value of type T and return that directly instead
		/// of an "out" parameter.  This assumes the count is the maximum number of bits of T.
		/// </summary>
		/// <param name="bitsReadCount">Out parameter with the number of bits actually read</param>
		/// <typeparam name="T">The data type to be read, this assumes the # of bits to be read is the length of T</typeparam>
		/// <returns>The value read</returns>
		public T Read<T>(out int bitsReadCount) where T : struct
		{
			bitsReadCount = Read(out T returnValue);
			return returnValue;
		}
		/// <summary>
		/// Wrapper method for <see cref="Read{T}(out int)"/> to read a single value of type T and return that directly and ignore the out parameter.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T Read<T>() where T : struct
		{
			return Read<T>(out _);
		}

		/// <summary>
		/// Reads a specified number of bits from the storage and returns them as the out variable.  
		/// E.g. if the <paramref name="bitsToRead"/> is 3, <typeparamref name="T"/> is a byte, 
		/// and the next 3 bits are 0b101, the out variable will be 0b10100000
		/// </summary>
		/// <typeparam name="T">The data type of the number holding the bits to be written</typeparam>
		/// <param name="bitsRead">The value of the bits read.</param>
		/// <param name="bitsToRead">The number of bits to read. Must be between 0 and the maximum number of bits in <typeparamref name="T"/>.</param>
		/// <returns>The actual number of bits read.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range of <typeparamref name="T"/>.</exception>
		public int Read<T>(out T bitsRead, int? bitsToRead = null) where T : struct
		{
			if (typeof(T) == typeof(bool))
			{
				bitsToRead ??= 1;
				if (bitsToRead > 1 || bitsToRead < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(bitsToRead), $"Number of bits ({bitsToRead}) is out of range of 1");
				}
				if (bitsToRead == 0)
				{
					bitsRead = default;
					LastReadBitCount = 0;
					return 0;
				}
				bool returnBool = ReadBool();
				bitsRead = (T)Convert.ChangeType(returnBool, typeof(bool));
				return LastReadBitCount;
			}
			LastReadBitCount = 0;
			// if the number of bits is more than can be put into a ulong, throw an error
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentException($"Type {typeof(T)} is not supported");
			}
			ulong tempReturnValue = 0;
			if (bitsToRead < 0 || bitsToRead > typeLength)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToRead), $"Number of bits ({bitsToRead}) is out of range of {typeLength}");
			}
			// take the index of the byte we are writing to minus the index where we are reading minus 1 gives the total number of whole
			// bytes left * 8 = bits in those bytes, add to that the read bit index on the front side (zero-based index, so add 1) and the
			// write bit index on the back side (when writeBitIndex = storageElementLength, the left-most bit of that byte will be added next)
			// this gives the total remaining data size.
			int remainingDataSize = (WriteByteIndex - 1 - _readByteIndex) * (StorageElementLength) + ReadBitIndex + StorageElementLength - WriteBitIndex;
			// if there aren't enough bits remaining, set the number of bits to the remaining
			int tempBits = bitsToRead ?? typeLength;
			if (tempBits > remainingDataSize)
			{
				tempBits = remainingDataSize;
			}
			int returnBits = tempBits;
			// keep going around until there are no more bits requested
			while (tempBits > 0)
			{
				// if the number of bits requested is greater than the number of bits remaining in this byte, set the requested bits to the number of remaining bits in this byte
				int tempLength = tempBits;
				if (tempLength > ReadBitIndex)
				{
					tempLength = ReadBitIndex + 1;
				}

				int endBits = ReadBitIndex - tempLength + 1;
				ulong bitMask = GetMask(tempLength);
				tempReturnValue <<= tempLength;
				tempReturnValue += ((ulong)data[_readByteIndex] >> endBits) & bitMask;
				ReadBitIndex -= tempLength;
				tempBits -= tempLength;
			}
			bitsRead = (T)Convert.ChangeType(tempReturnValue, typeof(T));
			LastReadBitCount = returnBits;
			return returnBits;
		}

		/// <summary>
		/// Writes the bits from another BitStorage instance to this storage.
		/// </summary>
		/// <param name="bits">The BitStorage instance containing the bits to write.</param>
		public void Write(BitStorage bits)
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
					Write(bits.data[^1] >> StorageElementLength - extraBits, extraBits);
				}
			}
		}

		/// <summary>
		/// Writes a single bit to the storage.
		/// </summary>
		/// <param name="bit">Bit to be written</param>
		private void Write(bool bit)
		{
			EnsureCapacity(WriteByteIndex);
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
		/// <param name="elementBitsToWrite">The number of bits for each element to write.
		/// This must be a non-negative value. If this is null or greater than the number of bits in
		/// each element, all bits in the element will be written, unless <paramref name="bitsToWrite"/> has been reached</param>
		/// <exception cref="ArgumentException">Thrown if the type is not valid</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range.</exception>
		public void Write<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null) where T : struct
		{
			if (bitsToWrite < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToWrite), $"Number of bits ({bitsToWrite}) cannot be less than 0");
			}
			if (elementBitsToWrite < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(elementBitsToWrite), $"Number of bits ({elementBitsToWrite}) cannot be less than 0");
			}
			// special edge case for boolean values.  This will take the boolean values, create a ulong with the appropriate bits
			// set and call the generic <see cref="Write{T}(IEnumerable{T}, int?)"/> method to write the bits.
			if (typeof(T) == typeof(bool))
			{
				ulong value = 0;
				int bitLength = TypeLengths[value.GetType()];
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
			}
			else
			{
				if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
				{
					throw new ArgumentException($"Type {typeof(T)} is not supported");
				}
				// If the elementBitsToWrite (the number of bits to write for each element) is specified, use that,
				// otherwise use the type length (all bits in each element).
				if (elementBitsToWrite != null && elementBitsToWrite < typeLength)
				{
					typeLength = (int)elementBitsToWrite;
				}
				// Since "bits" is an enumerable, the length is not known until the end.  The "bitsToWrite" is
				// going to be either null (write all bits) or a non-negative value.  If the value is null, the
				// length of bits to write (dataLength) will be set to a negative value to indicate all bits,
				// otherwise, it will be set to the value of bitsToWrite.  This will be used to determine when
				// to stop writing. 
				int dataLength = bitsToWrite ?? -typeLength; // Number of bits to write, negative means all bits
				int numElements = dataLength / typeLength; // Number of whole elements to write
				int extraBits = dataLength % typeLength; // Remainder of bits to write
														 // If the dataLength is a multiple of the type length, the end is the number of elements, otherwise,
														 // the end is the number of elements + 1 (for the extra bits)
				int end = extraBits == 0 ? numElements : numElements + 1;
				// Since there is no index in an enumerable, we need to keep track of the number of elements written
				int i = 0;
				foreach (var value in bits)
				{
					var iterValue = (ulong)Convert.ChangeType(value, typeof(ulong));
					// bitLength will be the size of the number until the last number, which will be the extra bits
					int bitLength = typeLength;
					// If the "bitsToWrite" is null, all bits will be written, so the "end" will be negative and this
					// will never be true.  If the "bitsToWrite" is not null, this will be true on the last element to write
					if (i == numElements)
					{
						iterValue >>= bitLength - extraBits; // Shift the value to the right to remove unwanted bits
						bitLength = extraBits;
					}
					Write(iterValue, bitLength);
					i++;
					// If the length is specified and the end of the data is reached, break out of the loop.
					// Note that if bitsToWrite is null, end will be negative and i >= 0 will always be true
					// so the check for bitsToWrite != null is needed
					if (bitsToWrite != null && i >= end)
					{
						break;
					}
				}
			}
		}

		/// <summary>
		/// Writes the specified bits to the storage.
		/// </summary>
		/// <typeparam name="T">The data type of the number holding the bits to be written</typeparam>
		/// <param name="bits">The bits to write.</param>
		/// <param name="bitsToWrite">The number of bits to write.</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range.</exception>
		public void Write<T>(T bits, int? bitsToWrite = null) where T : struct
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
				return;
			}
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentException($"Type {typeof(T)} is not supported");
			}
			if (bitsToWrite < 0 || bitsToWrite > typeLength)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToWrite), $"Number of bits ({bitsToWrite}) is out of range of 0-{typeLength}");
			}
			if (Comparer<T>.Default.Compare(bits, (T)Convert.ChangeType(0, typeof(T))) < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(bits), $"Value ({bits}) needs to be non-negative");
			}
			// tempLength is the number of bits left to write
			int tempLength = bitsToWrite ?? typeLength;
			// The mask is the used to mask off unwanted bits. The mask will be all 1's for the number of bits requested
			// e.g. if the number of bits requested is 5, the mask will be 0b11111
			ulong mask = GetMask(tempLength);
			// tempBits holds the bits to be stored.  The bits will be removed (shifted) as they are written
			// The bits are converted to a ulong so they can be manipulated easier
			ulong tempBits = (ulong)Convert.ChangeType(bits, typeof(ulong));
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
				writeBits <<= WriteBitIndex + 1 - tempWriteLength;
				// make a mask for the bits to be written
				ulong tempMask = (mask >> tempLength) << WriteBitIndex + 1 - tempWriteLength;
				EnsureCapacity(WriteByteIndex);
				// Set the storage element bits to 0's in the position of the bits to be written
				data[WriteByteIndex] &= (byte)~tempMask;
				// Add the bits to be written to the storage element
				data[WriteByteIndex] += (byte)writeBits;
				// Update the WriteBitIndex
				WriteBitIndex -= tempWriteLength;
			}
		}

		/// <summary>
		/// Gets the number of bits read during the most recent read operation.
		/// </summary>
		public int LastReadBitCount { get; private set; } = 0;

		/// <summary>
		/// Gets the number of bits in the storage.
		/// </summary>
		public int Count { get; private set; } = 0;

		/// <summary>
		/// Gets or sets the read index in bits.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the read index is out of the valid range.</exception>
		public int ReadIndex
		{
			get
			{
				return _readByteIndex * StorageElementLength + StorageElementLength - ReadBitIndex - 1;
			}
			set
			{
				if (value < 0 || value > Count)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"Invalid ReadIndex: {value}, values must be between 0 and {Count}");
				}
				_readByteIndex = value / StorageElementLength;
				ReadBitIndex = StorageElementLength - value % StorageElementLength - 1;
			}
		}

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

		/// <summary>
		/// Inserts the specified <see cref="BitStorage"/> at the given index within the current <see cref="BitStorage"/>  and
		/// returns a new <see cref="BitStorage"/> containing the result.
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
		public BitStorage Insert(int index, BitStorage bits)
		{
			if (bits is null) throw new ArgumentNullException(nameof(bits));
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");

			var tempReadIndex = ReadIndex;

			// Create result
			var result = new BitStorage();

			// starting from the beginning, read up to the index
			ReadIndex = 0;
			var prefix = ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, LastReadBitCount);
			}

			// write the bits to be inserted
			result.Write(bits);

			// read the rest of the bits from the original storage
			var suffix = ReadEnumerable<int>();

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, LastReadBitCount);
			}
			// restore the read index
			ReadIndex = tempReadIndex;
			return result;
		}

		/// <summary>
		/// Inserts the specified bits into the storage at the given index.
		/// </summary>
		/// <typeparam name="T">The value type containing the bits to insert.</typeparam>
		/// <param name="index">The zero-based index at which to insert the bits.</param>
		/// <param name="bits">The value containing the bits to be inserted.</param>
		/// <param name="bitsToWrite">The number of bits to write from the value. If null, all bits of the value are written.</param>
		/// <returns>A new BitStorage instance with the specified bits inserted at the given index.</returns>
		[Pure]
		public BitStorage Insert<T>(int index, T bits, int? bitsToWrite = null) where T : struct
		{
			var storage = new BitStorage();
			storage.Write(bits, bitsToWrite);
			return Insert(index, storage);
		}

		/// <summary>
		/// Removes a range of bits from the current storage and returns a new BitStorage instance with the specified range
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
		public BitStorage RemoveRange(int index, int count)
		{
			if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot be less than 0");
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");
			if (index + count > Count) throw new ArgumentOutOfRangeException(nameof(count), $"Range (index + count) exceeds storage length {Count}");

			var tempReadIndex = ReadIndex;

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
			ReadIndex = 0;
			var prefix = ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, LastReadBitCount);
			}

			// skip the range to be removed
			ReadIndex += count;

			// read the rest of the bits from the original storage
			var suffixLength = Count - (index + count);
			var suffix = ReadEnumerable<int>(suffixLength);

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, LastReadBitCount);
			}
			// restore the read index
			ReadIndex = tempReadIndex;

			// set the result write index to 0
			result.WriteIndex = result.Count;
			result.ReadIndex = 0;
			return result;
		}

		/// <summary>
		/// Removes a specified number of bits from the end of the bit storage and returns the resulting BitStorage instance.
		/// </summary>
		/// <param name="count">The number of bits to remove from the end. Must be greater than or equal to 0 and less than or equal to the
		/// current bit count.</param>
		/// <returns>A new BitStorage instance with the specified number of bits removed from the end.</returns>
		[Pure]
		public BitStorage TrimEnd(int count)
		{
			// Remove "count" bits from the end of the storage
			return RemoveRange(Count - count, count);
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

			var originalDataSpan = CollectionsMarshal.AsSpan(data);
			var otherDataSpan = CollectionsMarshal.AsSpan(other.data);
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
	}
}
