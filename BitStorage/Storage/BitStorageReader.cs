using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static GgoSoft.Storage.BitStorage;

namespace GgoSoft.Storage
{
	public class BitStorageReader
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

		private readonly BitStorage _storage;
		// Index of the bit within the current byte being read. This will go down as each bit is read and will reset
		// to the last bit of the next storage element if the read index is less than 0
		private int _readBitIndex = BitStorage.StorageElementLength - 1;
		// Index of the byte currently being read
		internal int readByteIndex;

		/// <summary>
		/// Initializes a new instance of the BitStorageReader class using the specified BitStorage instance.
		/// </summary>
		/// <remarks>Ensure that the provided BitStorage instance is properly initialized before passing it to this
		/// constructor. The BitStorageReader will use this instance as its data source for subsequent read
		/// operations.</remarks>
		/// <param name="bitStorage">The BitStorage instance that provides the data to be read. This parameter cannot be null.</param>
		internal BitStorageReader(BitStorage bitStorage)
		{
			this._storage = bitStorage;
		}

		/// <summary>
		/// Gets the number of bits read during the most recent read operation.
		/// </summary>
		public int LastReadBitCount { get; private set; } = 0;

		/// <summary>
		/// Gets the number of bits in the storage.
		/// </summary>
		public int Count => _storage.Count;

		// Index of the bit within the current byte being written to. This will go down as each bit is written and
		// will reset to the last bit of the next storage element if the write index is less than 0
		/// <summary>
		/// Gets or sets the read index in bits.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when the read index is out of the valid range.</exception>
		public int ReadIndex
		{
			get
			{
				return readByteIndex * BitStorage.StorageElementLength + BitStorage.StorageElementLength - ReadBitIndex - 1;
			}
			set
			{
				if (value < 0 || value > _storage.Count)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"Invalid ReadIndex: {value}, values must be between 0 and {_storage.Count}");
				}
				readByteIndex = value / BitStorage.StorageElementLength;
				ReadBitIndex = BitStorage.StorageElementLength - value % BitStorage.StorageElementLength - 1;
			}
		}
		// The index of the next bit to be read within the current element
		internal int ReadBitIndex
		{
			get
			{
				return _readBitIndex;
			}
			set
			{
				if (value < 0)
				{
					_readBitIndex = BitStorage.StorageElementLength - 1;
					readByteIndex++;
				}
				else if (value >= BitStorage.StorageElementLength)
				{
					throw new ArgumentOutOfRangeException(nameof(value), $"ReadBitIndex {value} cannot be greater than {BitStorage.StorageElementLength}");
				}
				else
				{
					_readBitIndex = value;
				}
			}
		}

		// Helper method to read a single boolean value
		private bool ReadBool()
		{
			if (ReadIndex >= _storage.Count)
			{
				LastReadBitCount = 0;
				return false;
			}
			int mask = 1 << ReadBitIndex;
			bool returnValue = (_storage.data[readByteIndex] & mask) != 0;
			LastReadBitCount = 1;
			ReadBitIndex--;
			return returnValue;
		}

		internal BitStorageReader Clone()
		{
			var other = new BitStorageReader(_storage)
			{
				readByteIndex = this.readByteIndex,
				ReadBitIndex = this.ReadBitIndex
			};
			return other;
		}
		public BitStorageValueReader<T> CreateValueReader<T>(int bitsPerValue) where T : struct
		{
			return new BitStorageValueReader<T>(this, bitsPerValue);
		}

		/// <summary>
		/// Gets or sets the bits at the specified range in the storage using a boolean array.  This does not use the
		/// <see cref="this[Index]"/> for speed reasons.
		/// </summary>
		/// <param name="range">The range of bits</param>
		/// <returns>A new boolean array holding the bits</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="range"/> is out of range</exception>
		public bool[] this[Range range]
		{
			get
			{
				var (start, end) = _storage.GetActualIndex(range);
				// Calculate the length of the range and create the return array
				int length = end - start;
				bool[] returnValue = new bool[length];
				// get the data element location and bit mask for the start of the range, then loop over each item in the range
				var (element, bitMask) = GetLocation(start);
				for (int i = 0; i < length; i++)
				{
					// check if the bit is set in the data element and set the return value accordingly
					returnValue[i] = (_storage.data[element] & bitMask) > 0;
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
				int convertedIndex = _storage.GetActualIndex(index);
				// get the data element location and bit mask for the index
				var (element, bitMask) = BitStorage.GetLocation(convertedIndex);
				// check if the bit is set in the data element and return the result
				return (_storage.data[element] & bitMask) != 0;
			}
		}

		// Extra method so the yield return can be used properly, otherwise, the thrown exception may not be
		// thrown until the enumeration is read
		internal IEnumerable<T> ReadEnumerableHelper<T>(int bitsToRead, int bitsPerElement, BitsRead? bitsRead = null) where T : struct
		{
			System.Diagnostics.Debug.Assert(bitsToRead >= 0, "bitsToRead must be validated by the caller.");
			System.Diagnostics.Debug.Assert(bitsPerElement > 0, "typeLength must be validated by the caller.");

			int expectedReadIndex = ReadIndex;
			int numValues = bitsToRead / bitsPerElement;
			int extraBits = bitsToRead % bitsPerElement;
			int end = extraBits == 0 ? numValues : numValues + 1;
			// Loop through the number of items to read, read the bits and yield return the value
			for (int i = 0; i < end; i++)
			{
				if (ReadIndex != expectedReadIndex)
				{
					throw new InvalidOperationException("BitStorageReader.ReadIndex was modified externally during enumeration.");
				}

				int bitsReadCount = Read(out T returnValue, (i < numValues ? bitsPerElement : extraBits));
				if (bitsRead != null)
				{
					bitsRead.BitsReadCount = bitsReadCount;
				}
				expectedReadIndex += bitsReadCount;

				yield return returnValue;
			}
		}

		/// <summary>
		/// Reads a sequence of values of type <typeparamref name="T"/> from the underlying bitstream,
		/// beginning at the current <see cref="ReadIndex"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This method reads values using the specified <paramref name="bitsPerElement"/> width.  
		/// If <paramref name="bitsPerElement"/> is <c>null</c>, the default bit width for
		/// <typeparamref name="T"/> (as defined in <see cref="BitStorage.TypeLengths"/>) is used.
		/// </para>
		///
		/// <para>
		/// The total number of bits to read is determined by <paramref name="bitsToRead"/>.  
		/// If <paramref name="bitsToRead"/> is <c>null</c>, all remaining bits in the storage
		/// are read.
		/// </para>
		///
		/// <para>
		/// If <paramref name="bitsToRead"/> exceeds the number of bits remaining in the bitstream,
		/// the enumeration simply ends when the end of the stream is reached.  
		/// No exception is thrown.  
		/// This allows callers to request an arbitrarily large number of bits without needing
		/// to pre-check how many bits remain.
		/// </para>
		///
		/// <para>
		/// Each yielded value consumes either:
		/// <list type="bullet">
		///   <item><description><paramref name="bitsPerElement"/> bits (or the default width for <typeparamref name="T"/>) for all full-width values, or</description></item>
		///   <item><description>the remaining bits for the final value if fewer than <paramref name="bitsPerElement"/> bits remain.</description></item>
		/// </list>
		/// As a result, the final value may be a <em>partial-width</em> value.
		/// </para>
		///
		/// <para>
		/// The number of bits consumed for each yielded value is exposed through
		/// <see cref="LastReadBitCount"/> and optionally through the <paramref name="bitsRead"/>
		/// parameter.  
		/// This matches the behavior of <see cref="Read{T}(out T, int?)"/>, which also returns
		/// fewer bits than requested when the end of the stream is reached.
		/// </para>
		///
		/// <para>
		/// During enumeration, the <see cref="ReadIndex"/> must not be modified externally.
		/// If it changes between iterations, an <see cref="InvalidOperationException"/> is thrown
		/// to prevent silent corruption of the read sequence.
		/// </para>
		/// </remarks>
		///
		/// <typeparam name="T">
		/// The value type to decode from the bitstream.  
		/// Must be a supported type listed in <see cref="BitStorage.TypeLengths"/>.
		/// </typeparam>
		///
		/// <param name="bitsToRead">
		/// The total number of bits to read.  
		/// If <c>null</c>, all remaining bits are read.  
		/// If greater than the number of bits remaining, the enumeration ends at the end of the stream.
		/// </param>
		///
		/// <param name="bitsPerElement">
		/// The number of bits to read for each value.  
		/// If <c>null</c>, the default bit width for <typeparamref name="T"/> is used.  
		/// Must be between <c>1</c> and the maximum width for <typeparamref name="T"/>.
		/// </param>
		///
		/// <param name="bitsRead">
		/// Optional object that receives the number of bits consumed for the most recently
		/// yielded value.  
		/// Useful when the final value is a partial read.  
		/// <see cref="LastReadBitCount"/> also includes this number.
		/// </param>
		///
		/// <returns>
		/// A lazy enumerable of decoded values.  
		/// Each iteration advances the reader and consumes either
		/// <paramref name="bitsPerElement"/> bits (or the default width for <typeparamref name="T"/>),
		/// or, for the final value, the remaining bits if fewer are available.
		/// </returns>
		///
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="bitsPerElement"/> is invalid.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// Thrown if <typeparamref name="T"/> is not a supported type.
		/// </exception>
		public IEnumerable<T> ReadEnumerable<T>(int? bitsToRead = null, int? bitsPerElement = null, BitsRead? bitsRead = null) where T : struct
		{
			if (bitsToRead < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(bitsToRead),
					$"Number of bits ({bitsToRead}) is out of range of 0-{_storage.Count}");
			}

			int remainingBits = _storage.Count - ReadIndex;
			int effectiveBits = bitsToRead ?? remainingBits;

			if (effectiveBits > remainingBits)
			{
				effectiveBits = remainingBits;
			}

			Type tType = typeof(T);
			LastReadBitCount = 0;

			if (!BitStorage.TypeLengths.TryGetValue(tType, out int typeLength))
			{
				throw new ArgumentException($"Type {tType} is not supported");
			}
			if(bitsPerElement is not null)
			{
				if(bitsPerElement > typeLength || bitsPerElement <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(bitsPerElement), $"bitsPerElement ({bitsPerElement}) must be between 1 and {typeLength}");
				}
				typeLength = bitsPerElement.Value;
			}
			return ReadEnumerableHelper<T>(effectiveBits, typeLength, bitsRead);
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
					throw new ArgumentOutOfRangeException(nameof(bitsToRead), $"Number of bits ({bitsToRead}) is out of range of 0-1");
				}
				if (bitsToRead == 0)
				{
					bitsRead = default;
					LastReadBitCount = 0;
					return 0;
				}
				bool returnBool = ReadBool();
				bitsRead = Unsafe.As<bool, T>(ref returnBool);
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
				throw new ArgumentOutOfRangeException(nameof(bitsToRead), $"Number of bits ({bitsToRead}) is out of range of 0-{typeLength}");
			}
			// take the index of the byte we are writing to minus the index where we are reading minus 1 gives the total number of whole
			// bytes left * 8 = bits in those bytes, add to that the read bit index on the front side (zero-based index, so add 1) and the
			// write bit index on the back side (when writeBitIndex = storageElementLength, the left-most bit of that byte will be added next)
			// this gives the total remaining data size.
			int remainingDataSize = _storage.Count - ReadIndex;
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
				ulong bitMask = BitStorage.GetMask(tempLength);
				tempReturnValue <<= tempLength;
				tempReturnValue += ((ulong)_storage.data[readByteIndex] >> endBits) & bitMask;
				ReadBitIndex -= tempLength;
				tempBits -= tempLength;
			}
			bitsRead = FromUInt64<T>(tempReturnValue);
			if(returnBits < 0)
			{
				returnBits = 0;
			}
			LastReadBitCount = returnBits;
			return returnBits;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static T FromUInt64<T>(ulong value) where T : struct
		{
			if (typeof(T) == typeof(byte))
				return (T)(object)(byte)value;
			if (typeof(T) == typeof(ushort))
				return (T)(object)(ushort)value;
			if (typeof(T) == typeof(uint))
				return (T)(object)(uint)value;
			if (typeof(T) == typeof(ulong))
				return (T)(object)value;
			if (typeof(T) == typeof(char))
				return (T)(object)(char)value;

			// signed types (you enforce non-negative already)
			if (typeof(T) == typeof(sbyte))
				return (T)(object)(sbyte)value;
			if (typeof(T) == typeof(short))
				return (T)(object)(short)value;
			if (typeof(T) == typeof(int))
				return (T)(object)(int)value;
			if (typeof(T) == typeof(long))
				return (T)(object)(long)value;

			throw new NotSupportedException($"Type {typeof(T)} is not supported.");
		}
	}
}
