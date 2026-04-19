using System;
using System.Collections.Generic;
using System.Linq;

namespace GgoSoft.Storage
{
	/// <summary>
	/// Provides typed, value-level reading over a <see cref="BitStorage"/> instance,
	/// interpreting the underlying bitstream as a sequence of fixed-width values.
	/// </summary>
	/// <typeparam name="T">The decoded value type.</typeparam>
	public sealed class BitStorageValueReader<T> where T : struct
	{
		private readonly BitStorageReader _seqReader;
		private readonly BitStorageReader _idxReader;
		private BitStorageReader _lastReader; // used to track which reader was used for the most recent read operation
		private readonly int _bitsPerElement;
		private readonly int _startBitIndex; // used for alignment checks
		private readonly bool _signed;

		/// <summary>
		/// Creates a typed reader using the default bit width for <typeparamref name="T"/>.
		/// </summary>
		internal BitStorageValueReader(BitStorageReader reader, bool signed = false)
			: this(reader, BitStorage.GetTypeWidth<T>(), signed) { }

		/// <summary>
		/// Creates a typed reader using the specified bit width.
		/// </summary>
		/// <param name="storage">The underlying bit storage.</param>
		/// <param name="bitsPerElement">The number of bits used to encode each value.</param>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="bitsPerElement"/> is less than 1 or exceeds the maximum allowed for <typeparamref name="T"/>.
		/// </exception>
		internal BitStorageValueReader(BitStorageReader reader, int bitsPerElement, bool signed = false)
		{
			_seqReader = reader ?? throw new ArgumentNullException(nameof(reader));

			int maxWidth = BitStorage.GetTypeWidth<T>();
			if (bitsPerElement <= 0 || bitsPerElement > maxWidth)
				throw new ArgumentOutOfRangeException(nameof(bitsPerElement),
					$"bitsPerElement ({bitsPerElement}) must be between 1 and {maxWidth} for type {typeof(T).Name}.");

			_bitsPerElement = bitsPerElement;
			_startBitIndex = reader.ReadIndex;

			_idxReader = reader.Clone();
			_lastReader = _seqReader; // either reader is fine, but we'll default to the sequential reader for tracking purposes
			_signed = signed;
		}

		/// <summary>
		/// Gets the number of values available in the bitstream.
		/// The final value may be partial if the total bit count is not a multiple of <see cref="_bitsPerElement"/>.
		/// </summary>
		public int Count
		{
			get
			{
				int totalBits = _seqReader.Count - _startBitIndex;
				if (totalBits < 0)
				{
					totalBits = 0;
				}
				return (totalBits + _bitsPerElement - 1) / _bitsPerElement;
			}
		}
		/// <summary>
		/// Gets or sets the current sequential value index.
		/// Setting this property repositions the sequential reader.
		/// </summary>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if the index is outside the range [0, Count).
		/// </exception>
		public int ValueIndex
		{
			get => (_seqReader.ReadIndex - _startBitIndex) / _bitsPerElement;
			set
			{
				if (value < 0 || value >= Count)
					throw new ArgumentOutOfRangeException(nameof(value),
						$"ValueIndex ({value}) is out of range 0–{Count - 1}.");

				_seqReader.ReadIndex = _startBitIndex + value * _bitsPerElement;
			}
		}

		/// <summary>
		/// Gets the number of bits read during the most recent read operation.
		/// </summary>
		public int LastReadBitCount => _lastReader.LastReadBitCount;

		/// <summary>
		/// Reads the value at the specified index.
		/// </summary>
		/// <remarks>
		/// This operation does not modify the sequential cursor. It uses an independent
		/// internal reader to compute the aligned bit position for the requested index.
		/// </remarks>
		public T this[Index index]
		{
			get
			{
				int intIndex = index.GetOffset(Count);
				if (intIndex < 0 || intIndex >= Count)
					throw new ArgumentOutOfRangeException(nameof(index),
						$"Index ({index}) is out of range 0–{Count - 1}.");

				_idxReader.ReadIndex = _startBitIndex + intIndex * _bitsPerElement;
				_idxReader.Read(out T value, _bitsPerElement, _signed);
				_lastReader = _idxReader;
				return value;
			}
		}

		/// <summary>
		/// Reads a range of values specified by a <see cref="Range"/>.
		/// </summary>
		/// <remarks>
		/// This operation does not modify the sequential cursor. The range is interpreted 
		/// relative to the starting bit offset captured when this reader was created. 
		/// </remarks>
		public T[] this[Range range]
		{
			get
			{
				var (offset, length) = range.GetOffsetAndLength(Count);

				// Position the indexed reader
				_idxReader.ReadIndex = _startBitIndex + offset * _bitsPerElement;

				// Convert value count → bit count
				int bitsToRead = length * _bitsPerElement;

				_lastReader = _idxReader;
				// Use the existing low-level enumerator and materialize
				return _idxReader.ReadEnumerableHelper<T>(bitsToRead, _bitsPerElement, signed: _signed).ToArray();
			}
		}

		/// <summary>
		/// Reads the next value of type <typeparamref name="T"/> using the sequential cursor.
		/// The read must begin on a valid <c>_bitsPerElement</c> boundary relative to the starting offset.
		/// If the cursor is misaligned, an <see cref="InvalidOperationException"/> is thrown.
		/// This should not be an issue unless the underlying <see cref="BitStorage"/> was modified externally 
		/// or the reader was used in an unsupported way.
		/// </summary>
		public int Read(out T value)
		{
			EnsureAligned();
			var returnValue = _seqReader.Read(out value, _bitsPerElement, signed: _signed);
			_lastReader = _seqReader;
			return returnValue;
		}

		/// <summary>
		/// Reads the next value and discards the bit count.
		/// </summary>
		public T Read()
		{
			EnsureAligned();
			_seqReader.Read(out T returnValue, _bitsPerElement, signed: _signed);
			_lastReader = _seqReader;
			return returnValue;
		}

		/// <summary>
		/// Enumerates a specified number of values from the current sequential position.
		/// The enumeration enforces alignment and cursor stability.
		/// If <paramref name="valuesToRead"/> is null, all remaining values are returned.
		/// </summary>
		/// <param name="valuesToRead">The number of values to read, or null to read all remaining.</param>
		/// <returns>An enumerable sequence of decoded values.</returns>
		/// <exception cref="ArgumentOutOfRangeException">
		/// Thrown if <paramref name="valuesToRead"/> is negative or exceeds the remaining values.
		/// </exception>
		/// <remarks>
		/// Before each sequential read, the reader verifies that the underlying
		/// <see cref="BitStorageReader.ReadIndex"/> is aligned to the starting offset.
		/// If the cursor has been externally modified or is not on a valid boundary,
		/// an <see cref="InvalidOperationException"/> is thrown.
		/// </remarks>
		public IEnumerable<T> Enumerate(int? valuesToRead = null)
		{
			EnsureAligned();
			if (valuesToRead is < 0)
				throw new ArgumentOutOfRangeException(nameof(valuesToRead),
					$"Requested value count ({valuesToRead}) cannot be negative.");

			int remainingValues = Count - ValueIndex;
			int values = valuesToRead ?? remainingValues;

			if (values > remainingValues)
				throw new ArgumentOutOfRangeException(nameof(valuesToRead),
					$"Requested value count ({values}) exceeds remaining values ({remainingValues}).");

			long bitsToReadLong = (long)values * _bitsPerElement;
			if (bitsToReadLong > int.MaxValue)
				throw new OverflowException("Bit count exceeds Int32 range.");
			_lastReader = _seqReader;
			return _seqReader.ReadEnumerableHelper<T>((int)bitsToReadLong, _bitsPerElement, signed: _signed);
		}

		private void EnsureAligned()
		{
			int offset = _seqReader.ReadIndex - _startBitIndex;

			if (offset < 0 || offset % _bitsPerElement != 0)
			{
				throw new InvalidOperationException(
					$"BitStorageValueReader is misaligned. Expected ReadIndex to be " +
					$"{_startBitIndex} + N×{_bitsPerElement}, but found {_seqReader.ReadIndex}.");
			}
		}
	}
}
