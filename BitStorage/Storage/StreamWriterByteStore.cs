using System;
using System.Collections.Generic;
using System.IO;

namespace GgoSoft.Storage
{
	internal sealed class StreamWriterByteStore : IByteStore
	{
		private readonly Stream _stream;
		private readonly byte[] _buffer;
		private int _bufferCount;

		// The byte index BitStorage is currently writing to
		private int _currentByteIndex;

		// The in-progress byte (BitStorage writes to this repeatedly)
		private byte _currentByteValue;
		private bool _hasCurrentByte;

		public StreamWriterByteStore(Stream stream, int bufferSize)
		{
			_stream = stream ?? throw new ArgumentNullException(nameof(stream));
			if (!stream.CanWrite)
				throw new ArgumentException("Stream must be writable.", nameof(stream));

			if (bufferSize <= 0)
				throw new ArgumentOutOfRangeException(nameof(bufferSize));

			_buffer = new byte[bufferSize];
			_bufferCount = 0;

			_currentByteIndex = 0;
			_currentByteValue = 0;
			_hasCurrentByte = false;
		}

		public void EnsureCapacity(int index)
		{
			// No-op for streams
		}

		public byte this[Index index]
		{
			get => throw new NotSupportedException("Reading from a stream-backed store is not supported.");

			set
			{
				int i = index.Value;

				if (!_hasCurrentByte)
				{
					// First write must be to byte 0
					if (i != 0)
						throw new NotSupportedException("Stream-backed storage must start at byte index 0.");

					_currentByteIndex = 0;
					_currentByteValue = value;
					_hasCurrentByte = true;
					return;
				}

				if (i < _currentByteIndex)
					throw new NotSupportedException("Cannot rewrite flushed bytes in stream-backed storage.");

				if (i > _currentByteIndex + 1)
					throw new NotSupportedException("Cannot skip bytes in stream-backed storage.");

				if (i == _currentByteIndex)
				{
					// Still writing bits into the same byte
					_currentByteValue = value;
					return;
				}

				// i == _currentByteIndex + 1
				// Rollover: flush previous byte, begin new one
				FinalizePreviousByte();

				_currentByteIndex = i;
				_currentByteValue = value;
				_hasCurrentByte = true;
			}
		}

		private void FinalizePreviousByte()
		{
			if (!_hasCurrentByte)
				return;

			// Write the completed byte into the buffer
			_buffer[_bufferCount++] = _currentByteValue;
			_hasCurrentByte = false;
			_currentByteValue = 0;

			// Flush buffer only if full
			if (_bufferCount == _buffer.Length)
				FlushCompletedBytes();
		}
		public void Dispose()
		{
			FinalizePreviousByte();
			Flush();
		}
		public void Flush()
		{
			FlushCompletedBytes();
			_stream.Flush();
		}
		private void FlushCompletedBytes()
		{
			if (_bufferCount > 0)
			{
				_stream.Write(_buffer, 0, _bufferCount);
				_bufferCount = 0;
			}
		}

		public List<byte> GetRange(int start, int count)
			=> throw new NotSupportedException("Random access is not supported in stream-backed storage.");

		public Span<byte> AsSpan()
			=> throw new NotSupportedException("Span access is not supported in stream-backed storage.");

		public IByteStore Clone()
			=> throw new NotSupportedException("Cloning is not supported for stream-backed storage.");

		public void Clear()
			=> throw new NotSupportedException("Cannot clear a stream-backed store.");
		public bool IsAtEnd { get; set; } // not used for stream writes
		public bool HasRandomAccess => false;

		public bool IsReadOnly => false;

		public bool IsWriteOnly => true;

		public byte[] Peek(int start, int count)
			=> throw new NotSupportedException("Peek is not supported for Write Only stream-backed storage");
	}
}
