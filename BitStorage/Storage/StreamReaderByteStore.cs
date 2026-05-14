using System;
using System.Collections.Generic;
using System.IO;

namespace GgoSoft.Storage
{
	internal sealed class StreamReaderByteStore : IByteStore
	{
		private readonly Stream _stream;

		private int _currentByteIndex = 0;
		//private byte _currentByte;
		private readonly byte[] _buffer;
		//private readonly int _bufferSize;
		private int _bufferReadIndex = 0;
		private int _endBufferIndex = 0;
		//private readonly Queue<byte> _peekBuffer = new();

		public bool IsAtEnd { get; set; }

		//public int Count => bitCount; // unknown length

		public StreamReaderByteStore(Stream stream, int bufferSize)
		{
			if (bufferSize <= 0 || bufferSize > 65536)
			{
				throw new ArgumentOutOfRangeException(nameof(bufferSize), $"Buffer must be from 1 to 65536, got {bufferSize}");
			}
			_buffer = new byte[bufferSize];
			//_bufferSize = bufferSize;
			_stream = stream ?? throw new ArgumentNullException(nameof(stream));
			if (!stream.CanRead)
				throw new ArgumentException("Stream must be readable.", nameof(stream));
		}

		public byte this[Index index]
		{
			get
			{
				int i = index.Value;

				if (i < _currentByteIndex)
					throw new NotSupportedException("Cannot read previous bytes from a stream.");

				if (i == _currentByteIndex)
					return _buffer[_bufferReadIndex];

				if (i != _currentByteIndex + 1)
				{
					if (IsAtEnd)
					{
						throw new NotSupportedException("Stream ended before the declared bit count was reached.");
					}
					else
					{
						throw new NotSupportedException("Stream-backed reader only supports sequential access.");
					}
				}

				_bufferReadIndex++;
				EnsureBuffered(1);
				_currentByteIndex = i;

				if (_bufferReadIndex >= _endBufferIndex - 1 && _endBufferIndex < _buffer.Length)
				{
					IsAtEnd = true;
				}
				return _buffer[_bufferReadIndex];
			}
			set => throw new NotSupportedException("Stream-backed reader does not support writing.");
		}

		public void Clear() =>
			throw new NotSupportedException("Stream-backed reader cannot clear data.");

		public void EnsureCapacity(int index) =>
			throw new NotSupportedException("Stream-backed reader does not support capacity management.");

		public Span<byte> AsSpan() =>
			throw new NotSupportedException("Stream-backed reader does not support span access.");

		public IByteStore Clone() =>
			throw new NotSupportedException("Stream-backed reader cannot be cloned.");

		public List<byte> GetRange(int start, int count) =>
			throw new NotSupportedException("Stream-backed reader does not support random access.");

		public void Dispose()
		{
			// Do NOT dispose the underlying stream — caller owns it.
		}
		public void Flush()
		{
			// nothing to flush for a reader
		}
		public bool HasRandomAccess => false;

		public bool IsReadOnly => true;

		public bool IsWriteOnly => false;

		private void EnsureBuffered(int count)
		{
			if (count <= 0)
				throw new ArgumentOutOfRangeException(nameof(count),
					"Count must be greater than zero.");

			if (count > _buffer.Length)
				throw new ArgumentOutOfRangeException(nameof(count),
					"Requested byte count exceeds buffer capacity.");

			int unread = _endBufferIndex - _bufferReadIndex;

			// Already enough data in buffer → nothing to do
			if (unread >= count)
				return;

			// Need more data
			// Step 1: compact unread bytes to the front
			if (unread > 0)
			{
				Buffer.BlockCopy(
					_buffer, _bufferReadIndex,
					_buffer, 0,
					unread);
			}

			_bufferReadIndex = 0;

			// Step 2: read as much as possible to fill the rest of the buffer
			int bytesNeeded = _buffer.Length - unread;
			int bytesRead = _stream.Read(_buffer, unread, bytesNeeded);
			_endBufferIndex = unread + bytesRead;
		}


		public byte[] Peek(int start, int count)
		{
			if (start < _currentByteIndex || start > _currentByteIndex + 1)
			{
				throw new ArgumentOutOfRangeException(nameof(start));
			}
			EnsureBuffered(count);
			int endIndex = _bufferReadIndex + count;
			if (endIndex > _endBufferIndex)
			{
				endIndex = _endBufferIndex;
			}
			return _buffer[_bufferReadIndex..endIndex];

			//// Fill peek buffer until it has at least count bytes
			//while (_peekBuffer.Count < count && !IsAtEnd)
			//{
			//	int b = _stream.ReadByte();
			//	if (b < 0)
			//	{
			//		IsAtEnd = true;
			//		break;
			//	}

			//	_peekBuffer.Enqueue((byte)b);
			//}

			//return _peekBuffer.Take(count).ToArray();
		}

	}
}
