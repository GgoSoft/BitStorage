using System.Collections.Immutable;
namespace GGOSoft.Storage
{

	/// <summary>
	/// The BitStorage class provides functionality for reading and writing bits to a storage of bytes.
	/// It maintains indices for reading and writing bits and ensures that the storage length is updated accordingly.
	/// </summary>
	public class BitStorage
	{
		private static readonly ImmutableDictionary<Type, int> TypeLengths = ImmutableDictionary.CreateRange(
			[
				KeyValuePair.Create( typeof(ulong), sizeof(ulong) * 8),
				KeyValuePair.Create( typeof(uint), sizeof(uint) * 8),
				KeyValuePair.Create( typeof(ushort), sizeof(ushort) * 8),
				KeyValuePair.Create( typeof(byte), sizeof(byte) * 8),
				KeyValuePair.Create( typeof(long), sizeof(long) * 8 - 1), // ignore negatives for signed types
				KeyValuePair.Create( typeof(int), sizeof(int) * 8 - 1),
				KeyValuePair.Create( typeof(short), sizeof(short) * 8 - 1),
				KeyValuePair.Create( typeof(sbyte), sizeof(sbyte) * 8 - 1)
			]);
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
				CheckMaxBits(); // Ensure the length is updated if necessary
			}
		}

		/// <summary>
		/// Gets the length of the storage in bits.
		/// </summary>
		public int Length { get; private set; } = 0; // Total number of bits in the storage
		public int LastReadBitCount { get; private set; } = 0; // Number of bits read in the last read operation
		private readonly List<byte> data = []; // List of bytes to store the bits

		/// <summary>
		/// Checks and updates the maximum number of bits written to the storage.
		/// </summary>
		private void CheckMaxBits()
		{
			if (WriteIndex > Length)
			{
				Length = WriteIndex; // Update the length if the current write index exceeds it
			}
		}
		private static ulong GetMask(int length)
		{
			return length switch
			{
				>= 0 and < 64 => ((ulong)1 << length) - 1,
				64 => ulong.MaxValue,
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
				if (value > Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot set Write index to greater than Length ({Length})");
				}
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException($"Cannot set Write index to less than 0: {value}");
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
				if (value > Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot set Read index to greater than Length ({Length})");
				}
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException($"Cannot set Read index to less than 0: {value}");
				}
				readByteIndex = value / StorageElementLength;
				readBitIndex = StorageElementLength - value % StorageElementLength - 1;
			}
		}
		/// <summary>
		/// Gets or sets the bits at the specified range in the storage using a boolean array.  This does not use the
		/// <see cref="this[long]"/> for speed reasons.
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
				int start = range.Start.Value;
				int end = range.End.Value;
				int length = end - start;
				bool[] returnValue = new bool[length];
				var (element, bitMask) = GetLocation(start);
				for (int i = 0; i < length; i++)
				{
					returnValue[i] = (data[element] & bitMask) > 0;
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
				int start = range.Start.Value;
				int end = range.End.Value;
				int length = end - start;
				var (element, bitMask) = GetLocation(start);
				for (int i = 0; i < length; i++)
				{
					if (value[i])
					{
						data[element] |= (byte)bitMask;
					}
					else
					{
						data[element] &= (byte)~bitMask;
					}
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
		public bool this[long index]
		{
			get
			{
				if (index < 0 || index >= Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot get index {index} from storage of length {Length}");
				}
				var (element, bitMask) = GetLocation(index);
				return (data[element] & bitMask) > 0;
			}
			set
			{
				if (index < 0 || index >= Length)
				{
					throw new ArgumentOutOfRangeException($"Cannot set index {index} from storage of length {Length}");
				}
				var (element, bitMask) = GetLocation(index);
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
		private static (int elememt, int bitMask) GetLocation(long index)
		{
			int element = (int)(index / StorageElementLength);
			int bitMask = 1 << (StorageElementLength - (int)(index % StorageElementLength) - 1);
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
		public static BitStorage Create<T>(IEnumerable<T> data, long? length = null) where T : struct
		{
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {typeof(T)} is not supported");
			}
			BitStorage returnValue = new();
			returnValue.Write(data, length ?? typeLength * data.Count());
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

		/// <summary>
		/// Writes the specified values to the storage limited to the specified length in bits.
		/// E.g. if the values is a 4 byte Enumerable and length of 27, the first 3 bytes will be written as 8 bits each and the 
		/// first 3 bits (leftmost 3) will also be written.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="values">The elements to be added</param>
		/// <param name="length">How many bits of the Enumerable to be written, all of them if null</param>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public void Write<T>(IEnumerable<T> values, long? length = null) where T : struct
		{
			if (!TypeLengths.TryGetValue(typeof(T), out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {typeof(T)} is not supported");
			}
			// The length of bits to be written, if null, use a negative value to indicate all bits
			long dataLength = length ?? -typeLength;
			long numBytes = dataLength / typeLength;
			long extraBits = dataLength % typeLength;
			long end = extraBits == 0 ? numBytes : numBytes + 1;
			int i = 0;
			foreach (var value in values)
			{
				Console.WriteLine("Reading bits for write: " + value);
				int bitLength = typeLength;
				if (i == numBytes)
				{
					bitLength = (int)extraBits;

				}
				WriteBits(value, bitLength);
				i++;
				// If the length is specified and the end of the data is reached, break out of the loop
				if (length != null && i >= end)
				{
					break;
				}
			}

		}

		/// <summary>
		/// Reads the specified number of bits from the storage and returns them as a Enumerable of numbers.
		/// </summary>
		/// <typeparam name="T">The data type of the Enumerable elements</typeparam>
		/// <param name="length">The number of bits to read.</param>
		/// <returns>An Enumerable of values representing the read bits</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if the type is not valid</exception>
		public IEnumerable<T> Read<T>(long length) where T : struct
		{
			Type tType = typeof(T);
			LastReadBitCount = 0;
			if (!TypeLengths.TryGetValue(tType, out int typeLength))
			{
				throw new ArgumentOutOfRangeException($"Type {tType} is not supported");
			}
			return ReadEnumerable<T>(length, typeLength);
		}

		private IEnumerable<T> ReadEnumerable<T>(long length, int typeLength) where T : struct
		{
			long numBytes = length / typeLength;
			long extraBits = length % typeLength;
			long end = extraBits == 0 ? numBytes : numBytes + 1;
			for (int i = 0; i < end; i++)
			{
				ReadBits((int)(i < numBytes ? typeLength : extraBits), out T returnValue);
				Console.WriteLine("Enumerable bits for read: " + returnValue);
				yield return returnValue;
			}
		}

		/// <summary>
		/// Writes the bits from another BitStorage instance to this storage.
		/// </summary>
		/// <param name="bits">The BitStorage instance containing the bits to write.</param>
		public void WriteBits(BitStorage bits)
		{
			Write(bits.GetData(), bits.Length);
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
		public void WriteBits<T>(T bits, int? length = null, bool writeLeft = true) where T : struct//, int offset = 0)
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
				// If the write bit index is less than 0, we need to reset the WriteBitIndex and move to the next byte
				if (WriteBitIndex < 0)
				{
					WriteBitIndex = StorageElementLength - 1;
					WriteByteIndex++;
				}
			}
		}

		/// <summary>
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
		public int ReadBits<T>(int bitsToRead, out T returnValue, bool readLeft = true) where T : struct
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
