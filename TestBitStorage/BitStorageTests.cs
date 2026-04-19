using GgoSoft.Storage;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace TestBitStorage
{
	public class BitStorageTests
	{

		[Fact]
		public void WriteAndRead_BoolSequence_RoundTrips()
		{
			var storage = new BitStorage();
			bool[] input = [true, false, true, true, false, false, true];

			// write booleans
			storage.Write(input);
			var reader = storage.CreateReader();

			Assert.Equal(input.Length, storage.Count);

			var outBits = reader[..];

			Assert.Equal(input, outBits);
		}

		[Fact]
		public void WriteAndRead_BytesPlus2Bits_RoundTrips()
		{
			var storage = new BitStorage();
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			byte extraInput = 0b11;

			// write bytes enumerable
			storage.Write(input);
			storage.Write((extraInput & 0b10) > 0).Write((extraInput & 0b01) > 0);
			Assert.Equal(input.Length * 8 + 2, storage.Count);

			// read back bytes using ReadEnumerable<byte>()
			var reader = storage.CreateReader();
			reader.ReadIndex = 0;
			var read = reader.ReadEnumerable<byte>().ToArray();

			Assert.Equal([..input, extraInput], read);
		}

		[Fact]
		public void WriteAndRead_Bytes_MultipleReaders_RoundTrips()
		{
			var storage = new BitStorage();
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];

			// write bytes enumerable
			storage.Write(input);

			Assert.Equal(input.Length * 8, storage.Count);

			// read back bytes using ReadEnumerable<byte>()
			var reader1 = storage.CreateReader();
			reader1.ReadIndex = 0;
			var read1 = reader1.ReadEnumerable<byte>().ToArray();

			var reader2 = storage.CreateReader();
			reader2.ReadIndex = 0;
			var read2 = reader2.ReadEnumerable<byte>().ToArray();

			Assert.Equal(input, read1);
			Assert.Equal(input, read2);
		}

		[Fact]
		public void WriteAndRead_BitStorageToBitStorage()
		{
			var storage = new BitStorage();
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];

			// write bytes enumerable
			storage.Write(input);

			// create new BitStorage from existing one
			var newStorage = new BitStorage(storage);

			Assert.Equal(input.Length * 8, newStorage.Count);

			// read back bytes using ReadEnumerable<byte>()
			var reader = newStorage.CreateReader();
			reader.ReadIndex = 0;
			var read = reader.ReadEnumerable<byte>().ToArray();

			Assert.Equal(input, read);
		}

		[Fact]
		public void Write_PartialBits_ReadBackIndividualBits()
		{
			var storage = new BitStorage();

			// write 3 bits of the value 0b101 (5)
			storage.Write(5, 3);

			Assert.Equal(3, storage.Count);

			var reader = storage.CreateReader();
			reader.ReadIndex = 0;
			bool[] got = new bool[3];
			for (int i = 0; i < 3; i++)
			{
				got[i] = reader.Read<bool>();
			}

			// value 5 (0b101) written as three bits => [1,0,1] (left-to-right in this storage)
			Assert.Equal([true, false, true], got);
		}

		[Fact]
		public void Write_BitStorageNotMultipleOfStorageElement()
		{
			var storage = new BitStorage();
			storage.Write([0b1010_1010, 0b1100_0011]);
			storage.Write(123, 7);

			var storage2 = new BitStorage(storage);

			Assert.Equal(storage.Count, storage2.Count);
			Assert.Equal(storage.GetData(), storage2.GetData());
		}

		[Fact]
		public void Write_EnumerableBitsToWriteOutOfRange_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write([1], bitsToWrite: -1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToWrite", caughtException.ParamName);
			Assert.Equal("Number of Bits (-1) cannot be less than 0 (Parameter 'bitsToWrite')", caughtException.Message);
		}

		[Fact]
		public void Write_EnumerableElementBitsToWriteOutOfRange_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write([1], bitsPerElement: -1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsPerElement", caughtException.ParamName);
			Assert.Equal("Bits Per Element (-1) must be between 1 and 32 (Parameter 'bitsPerElement')", caughtException.Message);
		}

		[Fact]
		public void Write_EnumerableAcrossUlongBoundary()
		{
			var storage = new BitStorage();
			var data = new bool[64];
			for (int i = 0; i < 64; i += 3)
			{
				data[i] = true;
			}
			storage.Write(data);
			var reader = storage.CreateReader();
			Assert.Equal(64, storage.Count);
			Assert.Equal(data, reader[..]);
		}

		[Fact]
		public void Write_EnumerableBool_DataLongerThanBitsWritten()
		{
			var storage = new BitStorage();
			var data = new bool[64];
			for (int i = 0; i < 64; i += 3)
			{
				data[i] = true;
			}
			storage.Write(data, 8);
			var reader = storage.CreateReader();
			Assert.Equal(8, storage.Count);
			Assert.Equal(data[0..8], reader[..]);
		}

		[Fact]
		public void Write_EnumerableElementBitsToWriteShorterThanDataLength()
		{
			var storage = new BitStorage();
			byte[] data = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			storage.Write(data, bitsPerElement: 1);
			var reader = storage.CreateReader();
			Assert.Equal(data.Length, storage.Count);
			Assert.Equal(0b011, reader.Read<int>());
		}

		[Fact]
		public void Write_EnumerableBitsToWriteAcrossBoundarySpecified()
		{
			var storage = new BitStorage();
			byte[] data = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			storage.Write(data, bitsToWrite: 17);
			var reader = storage.CreateReader();
			Assert.Equal(17, storage.Count);
			Assert.Equal(0b1010_1010, reader.Read<byte>());
			Assert.Equal(0b1100_0011, reader.Read<byte>());
			Assert.Equal(0b1, reader.Read<byte>());
		}

		[Fact]
		public void Write_EnumerableInvalidType_ThrowsException()
		{
			var storage = new BitStorage();
			var caughtException = Assert.Throws<NotSupportedException>(() => storage.Write([DateTime.Now]));
			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void WriteAndRead_SingleBit_()
		{
			var storage = new BitStorage();
			storage.Write(true);
			storage.Write(false);
			storage.Write(true);
			var reader = storage.CreateReader();
			Assert.Equal(3, storage.Count);
			reader.ReadIndex = 0;
			Assert.Equal(5, reader.Read<byte>());
		}

		[Fact]
		public void Read_OneBoolWith2BitsToRead_ThrowsException()
		{
			var storage = new BitStorage();
			var numBitsToRead = 2;
			var reader = storage.CreateReader();

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(out bool _, numBitsToRead));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal($"Number of bits ({numBitsToRead}) is out of range of 0-1 (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Write_OneBoolWith2BitsToWrite_ThrowsException()
		{
			var storage = new BitStorage();
			var numBitsToWrite = 2;

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write(true, numBitsToWrite));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToWrite", caughtException.ParamName);
			Assert.Equal($"Number of bits ({numBitsToWrite}) is out of range of 1 (Parameter 'bitsToWrite')", caughtException.Message);
		}

		[Fact]
		public void Read_InvalidType_ThrowsException()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();

			var caughtException = Assert.Throws<NotSupportedException>(() => reader.Read(out DateTime _, 1));

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Write_InvalidType_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<NotSupportedException>(() => storage.Write(DateTime.Now));

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Read_EnumerableBitsToReadNegative_ThrowsException()
		{
			var storage = BitStorage.Create([5]);
			var reader = storage.CreateReader();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadEnumerable<byte>(-1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal($"Number of bits (-1) is out of range of 0-{storage.Count} (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Write_ChangeWriteIndex()
		{
			var storage = new BitStorage();
			storage.Write((byte)0b0110_0000); // write one byte
			storage.WriteIndex = 4; // change write index to bit 4
			storage.Write(0b1111, 4); // write 4 bits '1111' at bit index 4
			var reader = storage.CreateReader();
			reader.ReadIndex = 0;
			Assert.Equal(8, storage.Count); // expect 8 bits total
			var readByte = reader.Read<byte>(); // read back one byte
			Assert.Equal(0b0110_1111, readByte); // expect byte to be 0110_1111
		}

		[Fact]
		public void Write_ChangeWriteIndexInvalidValue_ThrowsException()
		{
			var storage = new BitStorage();
			storage.Write((byte)0b0110_0000); // write one byte
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteIndex = storage.Count + 1);
			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("value", caughtException.ParamName);
			Assert.Equal($"Invalid WriteIndex: {storage.Count + 1}, values must be between 0 and {storage.Count} (Parameter 'value')", caughtException.Message);
			caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.WriteIndex = -1);
			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("value", caughtException.ParamName);
			Assert.Equal($"Invalid WriteIndex: -1, values must be between 0 and {storage.Count} (Parameter 'value')", caughtException.Message);
		}

		[Fact]
		public void Read_ChangeReadIndexInvalidValue_ThrowsException()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadIndex = 1);
			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("value", caughtException.ParamName);
			Assert.Equal($"Invalid ReadIndex: 1, values must be between 0 and {storage.Count} (Parameter 'value')", caughtException.Message);
			caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadIndex = -1);
			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("value", caughtException.ParamName);
			Assert.Equal($"Invalid ReadIndex: -1, values must be between 0 and {storage.Count} (Parameter 'value')", caughtException.Message);
		}

		[Fact]
		public void Read_EnumerableInvalidType_ThrowsException()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var caughtException = Assert.Throws<NotSupportedException>(() => reader.ReadEnumerable<DateTime>());

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Read_TooManyBitsForType_ThrowsException()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(out byte _, 9));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal("Number of bits (9) is out of range of 0-8 (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Read_NegativeNumberOfBits_ThrowsException()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => reader.Read(out byte _, -1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal("Number of bits (-1) is out of range of 0-8 (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Read_PastEnd_TruncatesData()
		{
			var storage = new BitStorage();
			storage.Write((byte)0b1010_1010); // 8 bits
			var reader = storage.CreateReader();
			reader.ReadIndex = 6; // position to read last 2 bits
			var read = reader.Read(out byte b, 4); // try to read 4 bits, only 2 available
			Assert.Equal(2, read); // only 2 bits read
			Assert.Equal(0b10, b); // last two bits are '10'
		}

		[Fact]
		public void Read_ReadZeroBool_GetZeroData()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var read = reader.Read(out bool bit, 0);

			Assert.Equal(0, read);
			Assert.False(bit);
			Assert.Equal(0, reader.LastReadBitCount);
		}
		[Fact]
		public void Read_ReadBoolPastEnd_GetZeroData()
		{
			var storage = new BitStorage();
			var reader = storage.CreateReader();
			var read = reader.Read(out bool bit, 1);

			Assert.Equal(0, read);
			Assert.False(bit);
			Assert.Equal(0, reader.LastReadBitCount);
		}

		[Fact]
		public void Read_GetData()
		{
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			var storage = BitStorage.Create(input);
			var data = storage.GetData().ToArray();
			Assert.Equal(input, data);
		}
		[Fact]
		public void Read_GetData_WithTrimmedEnd()
		{
			var storage = new BitStorage();
			var data = 0b10101010;
			// the trimmed data will be 3
			var expectedMask = 0b11111000;
			var expected = data & expectedMask;
			storage.Write(data, 8);
			storage.TrimEnd(3);
			var trimmedStorage = storage.GetData();
			Assert.Single(trimmedStorage);
			Assert.Equal(expected, trimmedStorage[0]);
		}
		[Fact]
		public void Read_GetData_WithTrimmedEndAcrossByteBoundary()
		{
			var storage = new BitStorage();
			var data = 0b10101010;
			// the trimmed data will be 8 + 3
			var expectedMask = 0b11111000;
			var expected = data & expectedMask;
			// write data twice, 2nd byte + 3 bits of 1st will be trimmed
			storage.Write(data, 8);
			storage.Write(data, 8);
			storage.TrimEnd(11);
			var trimmedStorage = storage.GetData();
			Assert.Single(trimmedStorage);
			Assert.Equal(expected, trimmedStorage[0]);
		}

		[Fact]
		public void Read_GetData_WithTrimmedEndAtByteBoundary()
		{
			var storage = new BitStorage();
			var data = 0b10101010;
			storage.Write(data, 8);
			storage.Write(data, 8);
			storage.TrimEnd(8);
			var trimmedStorage = storage.GetData();
			Assert.Single(trimmedStorage);
			Assert.Equal(data, trimmedStorage[0]);
		}

		[Fact]
		public void Read_GetData_WithNoData()
		{
			var storage = new BitStorage();
			var rawStorage = storage.GetData();
			Assert.Empty(rawStorage);
		}

		[Fact]
		public void Indexer_SetAndGet_SingleBit()
		{
			var storage = new BitStorage();

			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write((byte)0); // ensures storage has capacity

			// set some bits via indexer
			storage[0] = true;
			storage[7] = true;
			storage[3] = true;
			var reader = storage.CreateReader();

			Assert.Equal(8, storage.Count);
			Assert.True(reader[0]);
			Assert.True(reader[3]);
			Assert.True(reader[7]);
			Assert.False(reader[1]);
			Assert.False(reader[2]);
			Assert.False(reader[4]);
			Assert.False(reader[5]);
			Assert.False(reader[6]);
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBit()
		{
			var storage = new BitStorage();

			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write((byte)0); // ensures storage has capacity

			// set some bits via indexer
			storage[2..5] = [true, true, true];
			var reader = storage.CreateReader();

			Assert.Equal(8, storage.Count);
			Assert.Equal(0b0011_1000, reader.Read<byte>()); // bits 2,3,4 set
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBitAcrossByteBoundary()
		{
			var storage = new BitStorage();

			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write<byte>([0, 0]); // ensures storage has capacity

			// set some bits via indexer
			storage[7..10] = [true, true, true];
			var reader = storage.CreateReader();

			Assert.Equal(16, storage.Count);
			Assert.Equal(0b0000_0001, reader.Read<byte>()); // bits 2,3,4 set
			Assert.Equal(0b1100_0000, reader.Read<byte>()); // bits 2,3,4 set
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBitWrongNumberThrowsException()
		{
			var storage = new BitStorage();
			var startRange = 7;
			var endRange = 10;
			var data = new bool[] { true, true, true, true };
			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write<byte>([0, 0]); // ensures storage has capacity

			// set some bits via indexer
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage[startRange..endRange] = data);

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("range", caughtException.ParamName);
			Assert.Equal($"Range is specified as {startRange}..{endRange} ({endRange - startRange} bits), but length of array given is {data.Length} (Parameter 'range')", caughtException.Message);
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBitOutOfRangeThrowsException()
		{
			var storage = new BitStorage();
			var startRange = 15;
			var endRange = 19;
			var data = new bool[] { true, true, true, true };
			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write<byte>([0, 0]); // ensures storage has capacity

			// set some bits via indexer
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage[startRange..endRange] = data);

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("range", caughtException.ParamName);
			Assert.Equal($"Cannot get index {startRange}..{endRange} from storage of length {storage.Count} (Parameter 'range')", caughtException.Message);
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBitInvalidRangeThrowsException()
		{
			var storage = new BitStorage();
			var startRange = 5;
			var endRange = 1;
			var data = new bool[] { true, true, true, true };
			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write<byte>([0, 0]); // ensures storage has capacity

			// set some bits via indexer
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage[startRange..endRange] = data);

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("range", caughtException.ParamName);
			Assert.Equal($"Cannot get index {startRange}..{endRange} from storage of length 16 (Parameter 'range')", caughtException.Message);
		}

		[Fact]
		public void BitStorageFactory_CreatesStorageWithContent()
		{
			var src = new byte[] { 0b1111_0000 };
			var storage = BitStorage.Create(src);
			var reader = storage.CreateReader();

			Assert.Equal(8, storage.Count);

			reader.ReadIndex = 0;
			var outBytes = reader.ReadEnumerable<byte>().ToArray();
			Assert.Single(outBytes);
			Assert.Equal(src[0], outBytes[0]);
		}
		[Theory]
		[InlineData(0, 0)]
		[InlineData(1, 1)]
		[InlineData(2, 5)]
		[InlineData(3, 6)]
		[InlineData(4, 7)]
		[InlineData(5, 8)]
		[InlineData(6, 9)]
		public void InsertInPlace_MiddleRange_ReadAndWriteIndexCorrect(int writeIndex, int expectedWriteIndex)
		{
			var baseBits = new[] { true, false, true, false, true, false, true };
			var insertedBits = new[] { true, false, true };
			int insertedIndex = 2;

			byte insertedByte = 0;
			foreach(var bit in insertedBits)
			{
				insertedByte <<= 1;
				if(bit)
				{
					insertedByte++;
				}
			}
			var storage = new BitStorage();
			storage.Write(baseBits);
			var reader = storage.CreateReader();
			int count = insertedBits.Length;
			storage.WriteIndex = writeIndex;
			storage.Insert(insertedIndex, insertedByte, count);

			var expected = baseBits.Take(insertedIndex).Concat(insertedBits).Concat(baseBits.Skip(insertedIndex)).ToArray();
			Assert.Equal(expected.Length, storage.Count);

			var actual = reader[..];
			Assert.Equal(expected, actual);
			Assert.Equal(expectedWriteIndex, storage.WriteIndex);
		}

		[Fact]
		public void InsertInPlace_AtEnd_CallsWrite()
		{
			var data = (byte)123;
			var insertedData = (byte)255;

			var storage = new BitStorage();
			storage.Write(data);
			var reader = storage.CreateReader();
			var expectedReadIndex = reader.ReadIndex;
			var expectedWriteIndex = storage.WriteIndex;
			storage.Insert(storage.Count, insertedData);
			var actualReadIndex = reader.ReadIndex;
			var actualWriteIndex = storage.WriteIndex;

			var actual1 = reader.Read<byte>();
			var actual2 = reader.Read<byte>();
			Assert.Equal(data, actual1);
			Assert.Equal(insertedData, actual2);
			Assert.Equal(expectedReadIndex, actualReadIndex);
			Assert.Equal(expectedWriteIndex, actualWriteIndex);

			storage.Clear();
			reader.ReadIndex = 0;
			storage.Write(data);
			expectedReadIndex = reader.ReadIndex;
			expectedWriteIndex = storage.WriteIndex;
			var insertedStorage = new BitStorage().Write(insertedData);
			storage.Insert(storage.Count, insertedStorage);
			actualReadIndex = reader.ReadIndex;
			actualWriteIndex = storage.WriteIndex;

			actual1 = reader.Read<byte>();
			actual2 = reader.Read<byte>();
			Assert.Equal(data, actual1);
			Assert.Equal(insertedData, actual2);
			Assert.Equal(expectedReadIndex, actualReadIndex);
			Assert.Equal(expectedWriteIndex, actualWriteIndex);
		}

		[Fact]
		public void InsertAsCopy_InsertVariousEdgeCases()
		{
			int count = 0;
			var data = new bool[128];
			var insertData = new bool[16];
			var rand = new Random();
			for (int i = 0; i < data.Length; i++)
			{
				if (rand.Next(2) == 1)
				{
					data[i] = true;
				}
			}
			for (int i = 0; i < insertData.Length; i++)
			{
				if (rand.Next(2) == 1)
				{
					insertData[i] = true;
				}
			}
			for (int i = 0; i < data.Length; i++)
			{
				for (int j = 0; j < insertData.Length; j += 4)
				{
					var baseStorage = new BitStorage();
					baseStorage.Write(data[0..i]);
					var ins = new BitStorage();
					ins.Write(insertData[0..j]);
					for (int k = 0; k <= i; k++)
					{
						count++;
						var result = baseStorage.InsertAsCopy(k, ins);
						var reader = result.CreateReader();
						Assert.Equal(baseStorage.Count + ins.Count, result.Count);
						reader.ReadIndex = 0;
						var expected = data.Take(k).Concat(insertData.Take(j)).Concat(data.Skip(k).Take(i - k)).ToArray();
						var r = reader[..];
						Assert.Equal(expected, r);
					}
				}
			}
		}

		[Fact]
		public void InsertAsCopy_InsertEmptyBitStorage_NoChange()
		{
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true]; // length 4
			baseStorage.Write(baseBits);
			var ins = new BitStorage(); // empty insertion
			var result = baseStorage.InsertAsCopy(2, ins);
			var reader = result.CreateReader();
			Assert.Equal(baseStorage.Count, result.Count);
			reader.ReadIndex = 0;
			var r = reader[..];
			Assert.Equal(baseBits, r);
		}

		[Fact]
		public void InsertAsCopy_InsertBitStorageAtBeginningMiddleEnd_ProducesExpectedSequence()
		{
			// base storage: bits [A B C D] where each letter is a single bit value for clarity
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true, true, false]; // length 6
			baseStorage.Write(baseBits);

			// insertion storage: two bits [1,1]
			var ins = new BitStorage();
			ins.Write([true, true]);

			// Insert at beginning (index 0)
			var result0 = baseStorage.InsertAsCopy(0, ins);
			var reader0 = result0.CreateReader();
			Assert.Equal(baseBits.Length + 2, result0.Count);
			reader0.ReadIndex = 0;
			var r0 = reader0[..];
			Assert.Equal([true, true, .. baseBits], r0);

			// Insert in middle (index 3)
			var resultMid = baseStorage.InsertAsCopy(3, ins);
			var readerMid = resultMid.CreateReader();
			readerMid.ReadIndex = 0;
			var rMid = readerMid[..];
			var expectedMid = baseBits.Take(3).Concat([true, true]).Concat(baseBits.Skip(3)).ToArray();
			Assert.Equal(expectedMid, rMid);

			// Insert at end (index == Count)
			var resultEnd = baseStorage.InsertAsCopy(baseBits.Length, ins);
			var readerEnd = resultEnd.CreateReader();
			readerEnd.ReadIndex = 0;
			var rEnd = readerEnd[..];
			Assert.Equal([.. baseBits, true, true], rEnd);
		}

		[Fact]
		public void InsertAsCopy_InsertValueAtBeginningMiddleEnd_ProducesExpectedSequence()
		{
			// base storage: bits [A B C D] where each letter is a single bit value for clarity
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true, true, false]; // length 6
			baseStorage.Write(baseBits);

			// insertion storage: two bits [1,1]
			var ins = 3;

			// Insert at beginning (index 0)
			var result0 = baseStorage.InsertAsCopy(0, ins, 2);
			Assert.Equal(baseBits.Length + 2, result0.Count);
			var reader0 = result0.CreateReader();
			reader0.ReadIndex = 0;
			var r0 = reader0[..];
			Assert.Equal([true, true, .. baseBits], r0);

			// Insert in middle (index 3)
			var resultMid = baseStorage.InsertAsCopy(3, ins, 2);
			var readerMid = resultMid.CreateReader();
			readerMid.ReadIndex = 0;
			var rMid = readerMid[..];
			var expectedMid = baseBits.Take(3).Concat([true, true]).Concat(baseBits.Skip(3)).ToArray();
			Assert.Equal(expectedMid, rMid);

			// Insert at end (index == Count)
			var resultEnd = baseStorage.InsertAsCopy(baseBits.Length, ins, 2);
			var readerEnd = resultEnd.CreateReader();
			readerEnd.ReadIndex = 0;
			var rEnd = readerEnd[..];
			Assert.Equal([.. baseBits, true, true], rEnd);
		}

		[Fact]
		public void InsertAsCopy_WithByteAlignedPrefixAndSuffix_Works()
		{
			byte firstByte = 0xAB; // 10101011
			byte secondByte = 0x4C; // 01001100

			// construct storage with 16 bits: two full bytes (2x8 bits)
			var expectedBitStorage = new BitStorage();
			// write 2 bytes: 0xAB, 0x4C  (10101011, 01001100) => 16 bits, then we'll only consider prefix/suffix positions
			expectedBitStorage.Write([firstByte, secondByte]);
			var expectedReader = expectedBitStorage.CreateReader();

			// create storage with exactly 12 bits by building from bools
			var truncated1stByte = new BitStorage();
			truncated1stByte.Write(firstByte >> 4, 4) // top 4 bits of 0xAB (1010)
							.Write(secondByte); // second byte 0x4C (01001100)

			var truncated2ndByte = new BitStorage();
			truncated2ndByte.Write(firstByte) // first byte 0xAB (10101011)
							.Write(secondByte, 4); // bottom 4 bits of 0x4C (1100)

			// Insert 4 bits [1,0,1,1] at index 4 (within first byte)
			var ins = new BitStorage();
			ins.Write([true, false, true, true]);

			var result = truncated1stByte.InsertAsCopy(4, ins);
			var reader = result.CreateReader();
			reader.ReadIndex = 0;
			var outBits1st = reader[..];

			// Insert 4 bits [0,1,0,1] at index 8 (within second byte)
			ins.Clear();
			ins.Write([false, true, false, false]);

			result = truncated2ndByte.InsertAsCopy(8, ins);
			reader = result.CreateReader();
			reader.ReadIndex = 0;
			var outBits2nd = reader[..];

			// build expected
			var expected = expectedReader[..];
			// compare
			Assert.Equal(expected, outBits1st); 
			Assert.Equal(expected, outBits2nd);
		}

		[Fact]
		public void InsertInPlace_WithByteAlignedPrefixAndSuffix_Works()
		{
			byte firstByte = 0xAB; // 10101011
			byte secondByte = 0x4C; // 01001100

			// construct storage with 16 bits: two full bytes (2x8 bits)
			var expectedBitStorage = new BitStorage();
			// write 2 bytes: 0xAB, 0x4C  (10101011, 01001100) => 16 bits, then we'll only consider prefix/suffix positions
			expectedBitStorage.Write([firstByte, secondByte]);
			var expectedReader = expectedBitStorage.CreateReader();

			// create storage with exactly 12 bits by building from bools
			var truncatedByte = new BitStorage();
			truncatedByte.Write(firstByte >> 4, 4) // top 4 bits of 0xAB (1010)
							.Write(secondByte); // second byte 0x4C (01001100)

			// Insert 4 bits [1,0,1,1] at index 4 (within first byte)
			var ins = new BitStorage();
			ins.Write([true, false, true, true]);

			truncatedByte.Insert(4, ins);
			var reader = truncatedByte.CreateReader();
			reader.ReadIndex = 0;
			var outBits1st = reader[..];

			// build expected
			var expected = expectedReader[..];
			// compare
			Assert.Equal(expected, outBits1st);
		}

		[Fact]
		public void ReadEnumerable_WithBitsRead_OutParameter_IsUpdated()
		{

			var storage = new BitStorage();
			var reader = storage.CreateReader();
			storage.Write(new byte[] { 0x01, 0x02, 0x03 });
			reader.ReadIndex = 0;

			var bitsRead = new BitStorageReader.BitsRead();
			var items = reader.ReadEnumerable<byte>(bitsRead: bitsRead).ToArray();

			// LastReadBitCount should reflect the last read element's bit count (8)
			Assert.Equal(8, reader.LastReadBitCount);
			// bitsRead.BitsReadCount should also equal the last element bit count returned by ReadEnumerable iteration
			Assert.Equal(8, bitsRead.BitsReadCount);
			Assert.Equal(3, items.Length);
		}

		[Fact]
		public void Clear_ResetsState()
		{
			var s = new BitStorage();
			s.Write(new byte[] { 0xFF, 0xAA });
			Assert.True(s.Count > 0);

			s.Clear();
			Assert.Equal(0, s.Count);

			// write again after clear
			s.Write((byte)0x0F);
			Assert.Equal(8, s.Count);
		}
		[Fact]
		public void ReadEnumerable_WithNonZeroReadIndex_ReturnsSameAsManualReads()
		{
			var storage = new BitStorage();
			// 3 bytes = 24 bits with distinct pattern
			var input = new byte[] { 0xAA, 0xBB, 0xCC }; // 10101010 10111011 11001100
			storage.Write(input);
			// set a non-zero read index so remaining bits != Count

			// Build expected by manually reading chunks using Read<T>(out,..)
			var manual = new BitStorage(storage); // copy so we can consume manually
			var reader = manual.CreateReader();
			reader.ReadIndex = 4;// start at bit index 4, so first byte will be read as 0b1010 (the last 4 bits of 0xAA)
			var expectedList = new List<byte>();
			while (reader.ReadIndex < manual.Count)
			{
				int remaining = manual.Count - reader.ReadIndex;
				int toRead = Math.Min(8, remaining);
				Assert.Equal(toRead, reader.Read(out byte val, toRead));
				expectedList.Add(val);
			}

			// Now call ReadEnumerable on the original storage (this will also consume)
			reader.ReadIndex = 4;
			var actual = reader.ReadEnumerable<byte>().ToArray();

			Assert.Equal(expectedList.Count, actual.Length);
			Assert.Equal([.. expectedList], actual);
		}

		[Fact]
		public void InsertAsCopy_NearEndPartialByte_ProducesExpectedBitSequence()
		{
			// Create base storage with 10 bits: values 0..9 (false/true alternating)
			bool[] baseBits = new bool[10];
			for (int i = 0; i < baseBits.Length; i += 2)
			{
				baseBits[i] = true;
			}

			var baseStorage = new BitStorage();
			baseStorage.Write(baseBits);

			// Insertion storage: 3 bits (1,0,1)
			var ins = new BitStorage();
			ins.Write([true, false, true]);

			// Insert at index 9 (this is inside the last partial byte and exercises the suffix-trim logic)
			int insertIndex = 9;
			var result = baseStorage.InsertAsCopy(insertIndex, ins);

			// Build expected sequence: prefix (0..index-1), inserted bits, suffix (index..end-1)
			var expected = baseBits.Take(insertIndex)
				.Concat([true, false, true])
				.Concat(baseBits.Skip(insertIndex))
				.ToArray();
			var reader = result.CreateReader();
			reader.ReadIndex = 0;
			var actual = reader[..];

			Assert.Equal(expected.Length, result.Count);
			Assert.Equal(expected, actual);
		}

		[Fact]
		public void InsertAsCopy_AtEnd_AppendsBitsCorrectly()
		{
			bool[] baseBits = [true, false, true, false, false]; // 5 bits
			var baseStorage = new BitStorage();
			baseStorage.Write(baseBits);

			var ins = new BitStorage();
			ins.Write([false, true, true]); // 3 bits

			var result = baseStorage.InsertAsCopy(baseBits.Length, ins); // insert at end
			var reader = result.CreateReader();

			var expected = baseBits.Concat([false, true, true]).ToArray();

			reader.ReadIndex = 0;
			var actual = reader[..];

			Assert.Equal(expected.Length, result.Count);
			Assert.Equal(expected, actual);
		}

		[Fact]
		public void InsertAsCopy_NullBitStorage_ThrowsException()
		{
			var s = new BitStorage();
			// using the null forgiving operator as a test
			BitStorage ins = null!;
			var e = Assert.Throws<ArgumentNullException>(() => s.InsertAsCopy(0, ins));
			Assert.Equal("bits", e.ParamName);
		}

		[Fact]
		public void InsertAsCopy_IndexOutOfRange_ThrowsException()
		{
			var s = new BitStorage();
			var ins = new BitStorage();
			var e = Assert.Throws<ArgumentOutOfRangeException>(() => s.InsertAsCopy(-1, ins));
			Assert.Equal("index", e.ParamName);
			e = Assert.Throws<ArgumentOutOfRangeException>(() => s.InsertAsCopy(1, ins));
			Assert.Equal("index", e.ParamName);
		}

		[Fact]
		public void RemoveRange_MiddleRange_RemovesCorrectBits()
		{
			var baseBits = new[] { true, false, true, false, true, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);
			var reader = storage.CreateReader();

			int index = 2;
			int count = 3; // remove bits at 2,3,4
			var original = reader[..];
			var r = storage.RemoveRangeAsCopy(index, count);
			var removeReader = r.CreateReader();

			var expected = baseBits.Take(index).Concat(baseBits.Skip(index + count)).ToArray();
			Assert.Equal(expected.Length, r.Count);

			var actual = removeReader[..];
			Assert.Equal(expected, actual);

			// original unchanged
			Assert.Equal(baseBits, original);
		}

		[Theory]
		[InlineData(0, 0)]
		[InlineData(1, 1)]
		[InlineData(2, 2)]
		[InlineData(3, 2)]
		[InlineData(4, 2)]
		[InlineData(5, 2)]
		[InlineData(6, 3)]
		public void RemoveRangeInPlace_MiddleRange_ReadAndWriteIndexCorrect(int writeIndex, int expectedWriteIndex)
		{
			var baseBits = new[] { true, false, true, false, true, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);
			var reader = storage.CreateReader();
			storage.WriteIndex = writeIndex;
			int index = 2;
			int count = 3; // remove bits at 2,3,4
			_ = storage.RemoveRange(index, count);

			var expected = baseBits.Take(index).Concat(baseBits.Skip(index + count)).ToArray();
			Assert.Equal(expected.Length, storage.Count);

			var actual = reader[..];
			Assert.Equal(expected, actual);
			Assert.Equal(expectedWriteIndex, storage.WriteIndex);
		}

		[Fact]
		public void RemoveRange_FromStart_RemovesPrefix()
		{
			var baseBits = new[] { false, true, true, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);

			var r = storage.RemoveRangeAsCopy(0, 2);
			var reader = r.CreateReader();

			var expected = baseBits.Skip(2).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, reader[..]);
		}

		[Fact]
		public void RemoveRange_ToEnd_RemovesSuffix()
		{
			var baseBits = new[] { true, true, false, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);

			int index = 3;
			int count = 2; // remove last 2 bits
			var r = storage.RemoveRangeAsCopy(index, count);
			var reader = r.CreateReader();

			var expected = baseBits.Take(index).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, reader[..]);
		}

		[Fact]
		public void TrimEnd_RemovesSuffix()
		{
			var baseBits = new[] { true, true, false, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);

			int index = 3;
			int count = 2; // remove last 2 bits
			var r = storage.TrimEndAsCopy(count);
			var reader = r.CreateReader();

			var expected = baseBits.Take(index).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, reader[..]);
		}

		[Fact]
		public void TrimInPlaceEnd_RemovesSuffix()
		{
			var baseBits = new[] { true, true, false, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);
			var reader = storage.CreateReader();

			int index = 3;
			int count = 2; // remove last 2 bits
			storage.TrimEnd(count);

			var expected = baseBits.Take(index).ToArray();
			Assert.Equal(expected.Length, storage.Count);
			Assert.Equal(expected, reader[..]);
		}

		[Fact]
		public void RemoveRange_RemoveAll_ResultsEmptyStorage()
		{
			var baseBits = Enumerable.Range(0, 10).Select(i => i % 2 == 0).ToArray();
			var storage = new BitStorage();
			storage.Write(baseBits);

			var r = storage.RemoveRangeAsCopy(0, storage.Count);
			var reader = r.CreateReader();

			Assert.Equal(0, r.Count);
			Assert.Empty(reader[..]);
		}

		[Fact]
		public void RemoveRange_CountZero_ReturnsCopy()
		{
			var baseBits = new[] { true, false, true };
			var storage = new BitStorage();
			storage.Write(baseBits);

			var r = storage.RemoveRangeAsCopy(1, 0);
			var reader = r.CreateReader();

			Assert.Equal(storage.Count, r.Count);
			Assert.Equal(baseBits, reader[..]);
		}

		[Fact]
		public void RemoveRange_InvalidArguments_Throw()
		{
			var s = new BitStorage();
			s.Write([true, false, true]);

			var e = Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRangeAsCopy(-1, 1)); // invalid index
			Assert.Equal("index", e.ParamName);
			e = Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRangeAsCopy(1, -1)); // invalid count
			Assert.Equal("count", e.ParamName);
			e = Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRangeAsCopy(4, 1)); // index > Count
			Assert.Equal("index", e.ParamName);
			e = Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRangeAsCopy(1, 5)); // index+count > Count
			Assert.Equal("count", e.ParamName);
		}

		[Fact]
		public void Equals_Null_ReturnsFalse()
		{
			var s = new BitStorage();
			s.Write([true, false, true]);
			Assert.False(s.ContentEquals(null));
		}
		[Fact]
		public void Equals_DifferentType_ReturnsFalse()
		{
			var s = new BitStorage();
			s.Write([true, false, true]);
			Assert.False(s.Equals("not a BitStorage"));
		}
		[Fact]
		public void Equals_DifferentSizes_ReturnsFalse()
		{
			var s1 = new BitStorage();
			s1.Write(0, 5);
			var s2 = new BitStorage();
			s2.Write(0, 6);
			Assert.False(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_EmptyStorages_ReturnsTrue()
		{
			var s1 = new BitStorage();
			var s2 = new BitStorage();
			Assert.True(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_SameContent_ReturnsTrue()
		{
			var s1 = new BitStorage();
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			s1.Write(data);
			var s2 = new BitStorage();
			s2.Write(data);
			Assert.True(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_DifferentLastBitOn_WithBoundary_ReturnsFalse()
		{
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			var s1 = BitStorage.Create(data);
			var s2 = BitStorage.Create(data);
			var s2Reader = s2.CreateReader();
			s2[^1] = !s2Reader[^1]; // flip last bit
			Assert.False(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_DifferentLastBitOn_AcrossBoundary_ReturnsFalse()
		{
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			var s1 = BitStorage.Create(data);
			var s2 = BitStorage.Create(data);
			s1.Write(true);
			s2.Write(false);
			Assert.False(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_SameDataDifferentOutsideData_ReturnsTrue()
		{
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			var s1 = BitStorage.Create(data);
			s1.Write(false);
			s1.Write([true, true]);
			// Trim changes the count, but leaves the data, so the underlying storage still has the [true, true] at the end,
			// but the bitstorage count does not include them
			s1 = s1.TrimEndAsCopy(2);
			var s2 = BitStorage.Create(data);
			s2.Write(false);
			Assert.True(s1.ContentEquals(s2));
		}

		[Fact]
		public void Insert_Boundaries_And_ByteBoundary_Works()
		{
			var baseBits = Enumerable.Range(0, 16).Select(i => i % 3 == 0).ToArray(); // deterministic pattern
			var insBits = new[] { true, false, true };

			// insert at beginning
			var baseBitStorage = new BitStorage();
			baseBitStorage.Write(baseBits);
			var aCopy = baseBitStorage.InsertAsCopy(0, BitStorage.Create(insBits));
			var aReader = aCopy.CreateReader();
			var expected0 = insBits.Concat(baseBits).ToArray();
			Assert.Equal(expected0.Length, aCopy.Count);
			Assert.Equal(expected0, aReader[..]);

			// insert at byte boundary (8)
			var b = new BitStorage();
			b.Write(baseBits);
			var bCopy = baseBitStorage.InsertAsCopy(8, BitStorage.Create(insBits));
			var bReader = bCopy.CreateReader();
			var expected8 = baseBits.Take(8).Concat(insBits).Concat(baseBits.Skip(8)).ToArray();
			Assert.Equal(expected8.Length, bCopy.Count);
			Assert.Equal(expected8, bReader[..]);

			// insert at end (Count)
			var c = new BitStorage();
			c.Write(baseBits);
			var cCopy = baseBitStorage.InsertAsCopy(baseBitStorage.Count, BitStorage.Create(insBits));
			var cReader = cCopy.CreateReader();
			var expectedEnd = baseBits.Concat(insBits).ToArray();
			Assert.Equal(expectedEnd.Length, cCopy.Count);
			Assert.Equal(expectedEnd, cReader[..]);
		}

		[Fact]
		public void RemoveRange_Boundaries_And_InvalidArgs()
		{
			var baseBits = Enumerable.Range(0, 20).Select(i => (i % 2) == 0).ToArray();
			var storage = new BitStorage();
			storage.Write(baseBits);

			// remove prefix
			var r1 = new BitStorage(storage); // copy
			var r1Reader = r1.CreateReader();
			r1.RemoveRange(0, 3);
			var expected1 = baseBits.Skip(3).ToArray();
			Assert.Equal(expected1.Length, r1.Count);
			Assert.Equal(expected1, r1Reader[..]);

			// remove middle (crossing byte boundary)
			var r2 = new BitStorage(storage);
			var r2Reader = r2.CreateReader();
			r2.RemoveRange(7, 6);
			var expected2 = baseBits.Take(7).Concat(baseBits.Skip(13)).ToArray();
			Assert.Equal(expected2.Length, r2.Count);
			Assert.Equal(expected2, r2Reader[..]);

			// remove suffix (to end)
			var r3 = new BitStorage(storage);
			var r3Reader = r3.CreateReader();
			r3.RemoveRange(storage.Count - 5, 5);
			var expected3 = baseBits.Take(baseBits.Length - 5).ToArray();
			Assert.Equal(expected3.Length, r3.Count);
			Assert.Equal(expected3, r3Reader[..]);

			// invalid args: index+count > Count
			var rInvalid = new BitStorage(storage);
			Assert.Throws<ArgumentOutOfRangeException>(() => rInvalid.RemoveRange(18, 5));

			// invalid args: negative count
			Assert.Throws<ArgumentOutOfRangeException>(() => storage.RemoveRange(1, -1));

			// invalid args: index out of range
			Assert.Throws<ArgumentOutOfRangeException>(() => storage.RemoveRange(-1, 1));
			Assert.Throws<ArgumentOutOfRangeException>(() => storage.RemoveRange(storage.Count + 1, 1));
		}

		[Fact]
		public void ReadEnumerable_WithNonZeroReadIndex_EqualsManualReads()
		{
			var storage = new BitStorage();
			var bytes = new byte[] { 0xAA, 0xBB, 0xCC }; // distinct bytes
			storage.Write(bytes);
			var reader = storage.CreateReader();

			// set non-zero read index
			reader.ReadIndex = 4;

			// expected: manual reads from a clone
			var manual = new BitStorage(storage);
			var manualReader = manual.CreateReader();
			manualReader.ReadIndex = 4;
			var expected = new List<byte>();
			while (manualReader.ReadIndex < manual.Count)
			{
				int remaining = manual.Count - manualReader.ReadIndex;
				int toRead = Math.Min(8, remaining);
				manualReader.Read(out byte val, toRead);
				expected.Add(val);
			}

			// use ReadEnumerable on original
			reader.ReadIndex = 4;
			var actual = reader.ReadEnumerable<byte>().ToArray();

			Assert.Equal(expected.Count, actual.Length);
			Assert.Equal(expected.ToArray(), actual);
		}

		[Fact]
		public void ContentEquals_IgnoresUnusedLowBitsInLastByte()
		{
			// create 12 bits (1.5 bytes) with a pattern
			var bits = new bool[] { true, false, true, false, true, false, true, false, true, true, false, false }; // 12 bits
			var s1 = new BitStorage();
			s1.Write(bits);

			// copy and mutate underlying last byte's unused low bits via reflection
			var s2 = new BitStorage(s1);

			// access private 'data' (InternalData) and its Data list
			var dataField = typeof(BitStorage).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.NotNull(dataField);
			var internal1 = dataField.GetValue(s1);
			var internal2 = dataField.GetValue(s2);
			Assert.NotNull(internal1);
			Assert.NotNull(internal2);

			var dataListProp = internal2.GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Assert.NotNull(dataListProp);
			var list = (List<byte>?)dataListProp.GetValue(internal2);
			Assert.NotNull(list);

			// Flip only the unused low bits of the last byte (Count=12 -> rem=4 used bits -> 4 unused low bits)
			int rem = s2.Count % 8;
			int unusedLowBits = (8 - rem) % 8;
			Assert.Equal(4, unusedLowBits); // sanity for this test scenario

			int lastIndex = list.Count - 1;
			byte original = list[lastIndex];
			byte maskForUnused = (byte)((1 << unusedLowBits) - 1); // low unusedLowBits set
																   // toggle the unused bits
			list[lastIndex] = (byte)(original ^ maskForUnused);

			// s1 and s2 should be content-equal despite underlying low-bit differences
			Assert.True(s1.ContentEquals(s2));
			// and underlying raw bytes should not differ either
			Assert.True(s1.GetData().SequenceEqual(s2.GetData()));
		}
	}
}
