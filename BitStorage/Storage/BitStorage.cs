using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
namespace GgoSoft.Storage
{

	/// <summary>
	/// The BitStorage class provides functionality for reading and writing bits to a storage of bytes.
	/// It maintains indices for reading and writing bits and ensures that the storage length is updated accordingly.
	/// </summary>
	public class BitStorage
	{
		private static readonly ImmutableDictionary<Type, int> TypeLengths = ImmutableDictionary.CreateRange(
			new List<KeyValuePair<Type, int>>() {
				KeyValuePair.Create( typeof(ulong), sizeof(ulong) * 8),
				KeyValuePair.Create( typeof(uint), sizeof(uint) * 8),
				KeyValuePair.Create( typeof(ushort), sizeof(ushort) * 8),
				KeyValuePair.Create( typeof(byte), sizeof(byte) * 8),
				KeyValuePair.Create( typeof(long), sizeof(long) * 8 - 1), // ignore negatives for signed types
				KeyValuePair.Create( typeof(int), sizeof(int) * 8 - 1),
				KeyValuePair.Create( typeof(short), sizeof(short) * 8 - 1),
				KeyValuePair.Create( typeof(sbyte), sizeof(sbyte) * 8 - 1)
			});
		// Index of the byte currently being read
		private int readByteIndex = 0;
		// Index of the bit within the current byte being read. This will go down as each bit is read and will reset
		// to the last bit of the next storage element if the read index is less than 0
		private int readBitIndex = StorageElementLength - 1;
		// Index of the byte currently being written to
		private int writeByteIndex = 0;
		// Index of the bit within the current byte being written to. This will go down as each bit is written and
		// will reset to the last bit of the next storage element if the write index is less than 0
		private int writeBitIndex = StorageElementLength - 1;

		/// <summary>
		/// Gets or sets the byte index for writing bits.
		/// </summary>
		private int WriteByteIndex
		{
			get
			{
				return writeByteIndex;
			}
			set
			{
				writeByteIndex = value;
				CheckMaxBits(); // Ensure the length is updated if necessary
			}
		}

		/// <summary>
		/// Gets or sets the bit index for writing bits.
		/// </summary>
		private int WriteBitIndex
		{
			get
			{
				return writeBitIndex;
			}
			set
			{
				writeBitIndex = value;
				// If the write bit index is less than 0, we need to reset the WriteBitIndex and move to the next byte
				if (writeBitIndex < 0)
				{
					writeBitIndex = StorageElementLength - 1;
					WriteByteIndex++;
				} else if(writeBitIndex >= StorageElementLength)
				{
					throw new ArgumentOutOfRangeException($"WriteBitIndex {writeBitIndex} is cannot be greater than {StorageElementLength}");
				}
				CheckMaxBits(); // Ensure the length is updated if necessary
			}
		}

		/// <summary>
		/// Gets the length of the storage in bits.
		/// </summary>
		public int Length { get; private set; } = 0; // Total number of bits in the storage
		public int LastReadBitCount { get; private set; } = 0; // Number of bits read in the last read operation
		private readonly List<byte> data = new(); // List of bytes to store the bits

		/// <summary>
		/// Checks and updates the maximum number of bits written to the storage.
		/// </summary>
		private void CheckMaxBits()
		{
			var writeIndex = WriteIndex; // WriteIndex is calculated, this makes it so it's only called once
			if (writeIndex > Length)
			{
				Length = writeIndex; // Update the length if the current write index exceeds it
			}
		}
		private static ulong GetMask(int length)
		{
			return length switch
			{
				0 => 0, // ulong.MaxValue >> 64 = ulong.MaxValue, it seems like the RHS is % 64, so this has to be an edge case
				> 0 and < 64 => ulong.MaxValue >> TypeLengths[typeof(ulong)] - length, // could have hard coded to 64 for ulong but this is more flexible
				64 => ulong.MaxValue, // the calculation above will work, but if there are conditions, may as well be explicit
				_ => throw new ArgumentOutOfRangeException($"Invalid length: {length}, values must be between 0 and 64")
			};
		}
		/// <summary>
		/// Gets the number of bits in each element of the storage (e.g., byte = 8 bits).
		/// </summary>
		public static int StorageElementLength { get; } = TypeLengths[typeof(byte)]; // Number of bits in a byte

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
				if (value < 0 || value > Length)
				{
					throw new ArgumentOutOfRangeException($"Invalid WriteIndex: {value}, values must be between 0 and {Length}");
				}
				writeByteIndex = value / StorageElementLength;
				writeBitIndex = StorageElementLength - value % StorageElementLength - 1;
			}
		}

		/// <summary>
		/// Gets or sets the read index in bits.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the read index is out of the valid range.</exception>
		public int ReadIndex
		{
			get
			{
				return readByteIndex * StorageElementLength + StorageElementLength - readBitIndex - 1;
			}
			set
			{
				if (value < 0 || value > Length)
				{
					throw new ArgumentOutOfRangeException($"Invalid ReadIndex: {value}, values must be between 0 and {Length}");
				}
				readByteIndex = value / StorageElementLength;
				readBitIndex = StorageElementLength - value % StorageElementLength - 1;
			}
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
				if (range.Start.Value < 0 || range.End.Value > Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot get index {range} from storage of length {Length}");
				}
				// Get the start and the end of the range, taking into account the IsFromEnd property
				int start = range.Start.IsFromEnd? Length - range.Start.Value : range.Start.Value;
				int end = range.End.IsFromEnd? Length - range.End.Value : range.End.Value;
				if (start > end)
				{
					throw new ArgumentOutOfRangeException($"Specified argument {range} was out of the range of valid values.");
				}
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
				if (range.Start.Value < 0 || range.End.Value > Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot get index {range} from storage of length {Length}");
				}
				// Get the start and the end of the range, taking into account the IsFromEnd property
				int start = range.Start.IsFromEnd ? Length - range.Start.Value : range.Start.Value;
				int end = range.End.IsFromEnd ? Length - range.End.Value : range.End.Value;
				if (start > end)
				{
					throw new ArgumentOutOfRangeException($"Specified argument {range} was out of the range of valid values.");
				}
				// Calculate the length of the range and check if the length of the array matches
				int length = end - start;
				if(length != value.Length)
				{
					throw new ArgumentOutOfRangeException($"Range is specified as {range} ({length} bits), but length of array given is {value.Length}");
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
				// get the index of the bit requested, taking into account the IsFromEnd property
				int convertedIndex = index.IsFromEnd ? Length - index.Value : index.Value;
				if (convertedIndex < 0 || convertedIndex >= Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot get index {index} from storage of length {Length}");
				}
				// get the data element location and bit mask for the index
				var (element, bitMask) = GetLocation(convertedIndex);
				// check if the bit is set in the data element and return the result
				return (data[element] & bitMask) != 0;
			}
			set
			{
				// get the index of the bit requested, taking into account the IsFromEnd property
				int convertedIndex = index.IsFromEnd ? Length - index.Value : index.Value;
				if (convertedIndex < 0 || convertedIndex >= Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot set index {index} from storage of length {Length}");
				}
				// get the data element location and bit mask for the index
				var (element, bitMask) = GetLocation(convertedIndex);
				// check if the bit is set in the supplied value and set the value accordingly
				if (value)
				{
					data[element] |= (byte)bitMask;
				}
				else
				{
					data[element] &= (byte)~bitMask;
				}
			}
		}
		//helper method to get the storage element and bit mask of the index
		private static (int elememt, int bitMask) GetLocation(int index)
		{
			int element = index / StorageElementLength;
			int bitMask = 1 << (StorageElementLength - (index % StorageElementLength) - 1);
			return (element, bitMask);
		}

		/// <summary>
		/// Clears the storage and resets all indices.
		/// </summary>
		public void Clear()
		{
			data.Clear();
			WriteBitIndex = StorageElementLength - 1;
			WriteByteIndex = 0;
			readBitIndex = StorageElementLength - 1;
			readByteIndex = 0;
			Length = 0;
		}

		/// <summary>
		/// Initializes a new instance of the BitStorage class with an empty storage.
		/// </summary>
		public BitStorage()
		{
		}

		/// <summary>
		/// Initializes a new instance of the BitStorage class with the specified data.
		/// </summary>
		/// <param name="data">The initial data to store.</param>
		public BitStorage(IEnumerable<byte> data, int? length = null)
		{
			this.Write(data, length);
		}
		/// <summary>
		/// Factory method to create a BitStorage object from any numeric Enumerable
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="data">The elements to be added</param>
		/// <param name="length">How many bits of the Enumerable to be written, all of them if null</param>
		/// <returns>A new BitStorage object containing the bits from the Enumerable</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public static BitStorage Create<T>(IEnumerable<T> data, int? length = null) where T : struct
		{
			BitStorage returnValue = new();
			returnValue.Write(data, length);
			return returnValue;
		}

		/// <summary>
		/// Gets the data stored as an Enumerable of bytes.
		/// </summary>
		/// <returns>An Enumerable of bytes representing the stored data.</returns>
		public IEnumerable<byte> GetData()
		{
			return data;
		}
		// Extra method so the yield return can be used properly, otherwise, the thrown exception may not be
		// thrown until the enumeration is read
		private IEnumerable<T> ReadEnumerable<T>(int length, int typeLength) where T : struct
		{
			int numBytes = length / typeLength;
			int extraBits = length % typeLength;
			int end = extraBits == 0 ? numBytes : numBytes + 1;
			// Loop through the number of items to read, read the bits and yield return the value
			for (int i = 0; i < end; i++)
			{
				Read((i < numBytes ? typeLength : extraBits), out T returnValue);
				yield return returnValue;
			}
		}

		/// <summary>
		/// Special edge case for boolean values.  This will take the boolean values, create a ulong with the appropriate bits
		/// set and call the generic <see cref="Write{T}(IEnumerable{T}, int?)"/> method to write the bits.
		/// </summary>
		/// <param name="data">The boolean collection to write</param>
		/// <param name="length">The number of elements to write, null if all of them</param>
		public void Write(IEnumerable<bool> data, int? length = null)
		{
			ulong value = 0;
			int bits = TypeLengths[value.GetType()];
			int i = 0;
			bool wroteLast = false;
			foreach (bool bit in data)
			{
				// Shift the value to the left and add the bit
				value <<= 1;
				value += (ulong)(bit ? 1 : 0);
				i++;
				// If the number of bits is equal to the number of bits in the storage element, write the value and reset it
				// wroteLast is used to determine if the last value was written, so that the last value can be written if it is not a multiple of the number of bits
				if (i % bits == 0)
				{
					Write(value, bits, false);
					value = 0;
					wroteLast = true;
				}
				else
				{
					wroteLast = false;
				}
				// If the length is specified and the end of the data is reached, break out of the loop.  This is used instead
				// of data.Count() because the length of the data is not known until the end of the enumeration
				if (i == length)
				{
					break;
				}
			}
			// If the last value was not written, write the last value
			if (!wroteLast)
			{
				Write(value, i % bits, false);
			}
		}
		/// <summary>
		/// Writes the specified values to the storage limited to the specified length in bits.
		/// E.g. if the values is a 4 byte Enumerable and length of 27, the first 3 bytes will be written as 8 bits each and the 
		/// first 3 bits (leftmost 3) will also be written.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="values">The elements to be added</param>
		/// <param name="length">How many bits of the Enumerable to be written, all of them if null</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public void Write<T>(IEnumerable<T> values, int? length = null) where T : struct
		{
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {typeof(T)} is not supported");
			}
			// The length of bits to be written, if null, use a negative value to indicate all bits
			int dataLength = length ?? -typeLength;
			int numBytes = dataLength / typeLength;
			int extraBits = dataLength % typeLength;
			int end = extraBits == 0 ? numBytes : numBytes + 1;
			int i = 0;
			foreach (var value in values)
			{
				// bitLength will be the size of the number until the last number, which will be the extra bits
				int bitLength = typeLength;
				if (i == numBytes)
				{
					bitLength = extraBits;

				}
				Write(value, bitLength);
				i++;
				// If the length is specified and the end of the data is reached, break out of the loop
				// Doing this instead of a "for" loop because if the length is not given, the enumeration
				// length will not be known until the end
				if (length != null && i >= end)
				{
					break;
				}
			}

		}

		/// <summary>
		/// Writes the bits from another BitStorage instance to this storage.
		/// </summary>
		/// <param name="bits">The BitStorage instance containing the bits to write.</param>
		public void Write(BitStorage bits)
		{
			Write(bits.GetData(), bits.Length);
		}
		public void Write(bool bit)
		{
			while (WriteByteIndex >= data.Count)
			{
				data.Add(0);
			}
			var mask = 1 << WriteBitIndex;

			WriteBit(bit, mask, WriteByteIndex);
			WriteBitIndex--;
		}
		/// <summary>
		/// Writes the specified bits to the storage.  The bits are written in big-endian order, so the left-most bits are written first up
		/// to the <paramref name="length"/> and the rest of the bits (if any) on the right are ignored, unless <paramref name="writeLeft"/> is set to false.
		/// </summary>
		/// <typeparam name="T">The data type of the number holding the bits to be written</typeparam>
		/// <param name="bits">The bits to write.</param>
		/// <param name="length">The number of bits to write.</param>
		/// <param name="readLeft">True if the bits should be read from the left, false if they should be read from the right.</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range.</exception>
		public void Write<T>(T bits, int? length = null, bool writeLeft = true) where T : struct//, int offset = 0)
		{
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {typeof(T)} is not supported");
			}
			if (length < 0 || length > typeLength)
			{
				throw new ArgumentOutOfRangeException($"Number of bits ({length}) is out of range of {typeLength}");
			}
			// tempLength is the number of bits left to write
			int tempLength = length ?? typeLength;
			// The mask is the used to mask off unwanted bits. The mask will be all 1's for the number of bits requested
			// e.g. if the number of bits requested is 5, the mask will be 0b11111
			ulong mask = GetMask(tempLength);
			// tempBits holds the bits to be stored.  The bits will be removed (shifted) as they are written
			// The bits are converted to a ulong so they can be manipulated easier
			ulong tempBits = (ulong)Convert.ChangeType(bits, typeof(ulong));
			// The bits are "big-endian" so the left-most bits are the first to be written, but this process is "little-endian", so 
			// The temp bits need to be shifted to the right.  The extra bits will be shifted off, but they wouldn't be written anyway
			if (writeLeft)
			{
				tempBits >>= typeLength - tempLength;
			}
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
				ulong tempMask = (mask >> tempLength) << writeBitIndex + 1 - tempWriteLength;
				// if the WriteByteIndex is greater than the size of the data list, add new bytes to the data list until it isn't
				while (WriteByteIndex >= data.Count)
				{
					data.Add(0);
				}
				// Set the storage element bits to 0's in the position of the bits to be written
				data[WriteByteIndex] ^= (byte)(data[writeByteIndex] & tempMask);
				// Add the bits to be written to the storage element
				data[WriteByteIndex] += (byte)writeBits;
				// Update the WriteBitIndex
				WriteBitIndex -= tempWriteLength;
			}
		}

		/// <summary>
		/// Reads the specified number of bits from the storage and returns them as a Enumerable of numbers.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="length">The number of bits to read.</param>
		/// <returns>An Enumerable of values representing the read bits</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public IEnumerable<T> Read<T>(int length) where T : struct
		{
			Type tType = typeof(T);
			// Hack to find the last bit count read since the bits are read in an enumerable with yield return
			LastReadBitCount = 0;
			if (!TypeLengths.TryGetValue(tType, out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {tType} is not supported");
			}
			return ReadEnumerable<T>(length, typeLength);
		}

		public bool Read(int index)
		{
			return this[index];
		}       /// <summary>
				/// Reads a specified number of bits from the storage and returns them as the out variable.  The bits will be returned big-endian,
				/// so the left most bits will contain the bits returned, unless <paramref name="writeLeft"/> is set to false.  
				/// E.g. if the <paramref name="bitsToRead"/> is 3, <typeparamref name="T"/> is a byte, 
				/// and the next 3 bits are 0b101, the out variable will be 0b10100000
				/// </summary>
				/// <typeparam name="T">The data type of the number holding the bits to be written</typeparam>
				/// <param name="bitsToRead">The number of bits to read. Must be between 0 and the maximum number of bits in <typeparamref name="T"/>.</param>
				/// <param name="returnValue">The value of the bits read.</param>
				/// <param name="readLeft">True if the bits should be read from the left, false if they should be read from the right.</param>
				/// <returns>The actual number of bits read.</returns>
				/// <exception cref="ArgumentOutOfRangeException">Thrown when the number of bits is out of the valid range of <typeparamref name="T"/>.</exception>
		public int Read<T>(int bitsToRead, out T returnValue, bool readLeft = true) where T : struct
		{
			LastReadBitCount = 0;
			// if the number of bits is more than can be put into a ulong, throw an error
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {typeof(T)} is not supported");
			}
			ulong tempReturnValue = 0;
			if (bitsToRead < 0 || bitsToRead > typeLength)
			{
				throw new ArgumentOutOfRangeException($"Number of bits ({bitsToRead}) is out of range of {typeLength}");
			}
			// take the index of the byte we are writing to minus the index where we are reading minus 1 gives the total number of whole
			// bytes left * 8 = bits in those bytes, add to that the read bit index on the front side (zero-based index, so add 1) and the
			// write bit index on the back side (when writeBitIndex = storageElementLength, the left-most bit of that byte will be added next)
			// this gives the total remaining data size.
			int remainingDataSize = (WriteByteIndex - 1 - readByteIndex) * (StorageElementLength) + readBitIndex + StorageElementLength - WriteBitIndex;
			// if there aren't enough bits remaining, set the number of bits to the remaining
			int tempBits = bitsToRead;
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
				if (tempLength > readBitIndex)
				{
					tempLength = readBitIndex + 1;
				}

				int endBits = readBitIndex - tempLength + 1;
				ulong bitMask = GetMask(tempLength);
				tempReturnValue <<= tempLength;
				tempReturnValue += ((ulong)data[readByteIndex] >> endBits) & bitMask;
				readBitIndex -= tempLength;
				tempBits -= tempLength;
				if (readBitIndex < 0)
				{
					readBitIndex = StorageElementLength - 1;
					readByteIndex++;
				}
			}
			// Adjust the temp return value so the bits are on the left side, instead of the right
			if (readLeft)
			{
				tempReturnValue <<= typeLength - returnBits;
			}
			returnValue = (T)Convert.ChangeType(tempReturnValue, typeof(T));
			LastReadBitCount = returnBits;
			return returnBits;
		}

	}
}
