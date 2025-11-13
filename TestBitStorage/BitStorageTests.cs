using GgoSoft.Storage;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Xunit;

namespace TestBitStorage
{
	public class BitStorageTests
	{

		[Fact]
		public void WriteAndRead_BoolSequence_Roundtrips()
		{
			var storage = new BitStorage();
			bool[] input = [true, false, true, true, false, false, true];

			// write booleans
			storage.Write(input);

			Assert.Equal(input.Length, storage.Count);

			var outBits = storage[..];

			Assert.Equal(input, outBits);
		}

		[Fact]
		public void WriteAndRead_Bytes_Roundtrips()
		{
			var storage = new BitStorage();
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];

			// write bytes enumerable
			storage.Write(input);

			Assert.Equal(input.Length * 8, storage.Count);

			// read back bytes using ReadEnumerable<byte>()
			storage.ReadIndex = 0;
			var read = storage.ReadEnumerable<byte>().ToArray();

			Assert.Equal(input, read);
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
			newStorage.ReadIndex = 0;
			var read = newStorage.ReadEnumerable<byte>().ToArray();

			Assert.Equal(input, read);
		}

		[Fact]
		public void Write_PartialBits_ReadBackIndividualBits()
		{
			var storage = new BitStorage();

			// write 3 bits of the value 0b101 (5)
			storage.Write(5, 3);

			Assert.Equal(3, storage.Count);

			storage.ReadIndex = 0;
			bool[] got = new bool[3];
			for (int i = 0; i < 3; i++)
			{
				got[i] = storage.Read<bool>();
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
			Assert.Equal("Number of bits (-1) cannot be less than 0 (Parameter 'bitsToWrite')", caughtException.Message);
		}

		[Fact]
		public void Write_EnumerableElementBitsToWriteOutOfRange_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write([1], elementBitsToWrite: -1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("elementBitsToWrite", caughtException.ParamName);
			Assert.Equal("Number of bits (-1) cannot be less than 0 (Parameter 'elementBitsToWrite')", caughtException.Message);
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
			Assert.Equal(64, storage.Count);
			Assert.Equal(data, storage[..]);
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
			Assert.Equal(8, storage.Count);
			Assert.Equal(data[0..8], storage[..]);
		}

		[Fact]
		public void Write_EnumerableElementBitsToWriteShorterThanDataLength()
		{
			var storage = new BitStorage();
			byte[] data = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			storage.Write(data, elementBitsToWrite: 1);
			Assert.Equal(data.Length, storage.Count);
			Assert.Equal(0b011, storage.Read<int>());
		}

		[Fact]
		public void Write_EnumerableBitsToWriteAcrossBoundarySpecified()
		{
			var storage = new BitStorage();
			byte[] data = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			storage.Write(data, bitsToWrite: 17);
			Assert.Equal(17, storage.Count);
			Assert.Equal(0b1010_1010, storage.Read<byte>());
			Assert.Equal(0b1100_0011, storage.Read<byte>());
			Assert.Equal(0b1, storage.Read<byte>());
		}

		[Fact]
		public void Write_EnumerableInvalidType_ThrowsException()
		{
			var storage = new BitStorage();
			var caughtException = Assert.Throws<ArgumentException>(() => storage.Write([DateTime.Now]));
			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void WriteAndRead_SingleBit_()
		{
			var storage = new BitStorage();
			storage.Write(true);
			storage.Write(false);
			storage.Write(true);
			Assert.Equal(3, storage.Count);
			storage.ReadIndex = 0;
			Assert.Equal(5, storage.Read<byte>());
		}

		[Fact]
		public void Read_OneBoolWith2BitsToRead_ThrowsException()
		{
			var storage = new BitStorage();
			var numBitsToRead = 2;

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Read(out bool _, numBitsToRead));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal($"Number of bits ({numBitsToRead}) is out of range of 1 (Parameter 'bitsToRead')", caughtException.Message);
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

			var caughtException = Assert.Throws<ArgumentException>(() => storage.Read(out DateTime _, 1));

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Write_InvalidType_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentException>(() => storage.Write(DateTime.Now));

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Read_EnumerableBitsToReadNegative_ThrowsException()
		{
			var storage = BitStorage.BitStorageFactory([5]);
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadEnumerable<byte>(-1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal($"Number of bits (-1) is out of range of 0-{storage.Count} (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Write_BitsToWriteNegative_ThrowsException()
		{
			var storage = new BitStorage();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write(1, -1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToWrite", caughtException.ParamName);
			Assert.Equal($"Number of bits (-1) is out of range of 0-31 (Parameter 'bitsToWrite')", caughtException.Message);
		}

		[Fact]
		public void Write_NegativeValue_ThrowsException()
		{
			var storage = new BitStorage();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Write(-1));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bits", caughtException.ParamName);
			Assert.Equal($"Value (-1) needs to be non-negative (Parameter 'bits')", caughtException.Message);
		}

		[Fact]
		public void Write_ChangeWriteIndex()
		{
			var storage = new BitStorage();
			storage.Write((byte)0b0110_0000); // write one byte
			storage.WriteIndex = 4; // change write index to bit 4
			storage.Write(0b1111, 4); // write 4 bits '1111' at bit index 4
			storage.ReadIndex = 0;
			Assert.Equal(8, storage.Count); // expect 8 bits total
			var readByte = storage.Read<byte>(); // read back one byte
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
		}

		[Fact]
		public void Read_ChangeReadIndexInvalidValue_ThrowsException()
		{
			var storage = new BitStorage();
			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.ReadIndex = 1);
			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("value", caughtException.ParamName);
			Assert.Equal($"Invalid ReadIndex: {storage.Count + 1}, values must be between 0 and {storage.Count} (Parameter 'value')", caughtException.Message);
		}

		[Fact]
		public void Read_EnumerableInvalidType_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentException>(() => storage.ReadEnumerable<DateTime>());

			Assert.Equal("Type System.DateTime is not supported", caughtException.Message);
		}

		[Fact]
		public void Read_TooManyBitsForType_ThrowsException()
		{
			var storage = new BitStorage();

			var caughtException = Assert.Throws<ArgumentOutOfRangeException>(() => storage.Read(out byte _, 9));

			Assert.NotNull(caughtException.ParamName);
			Assert.Equal("bitsToRead", caughtException.ParamName);
			Assert.Equal("Number of bits (9) is out of range of 8 (Parameter 'bitsToRead')", caughtException.Message);
		}

		[Fact]
		public void Read_PastEnd_TruncatesData()
		{
			var storage = new BitStorage();
			storage.Write((byte)0b1010_1010); // 8 bits
			storage.ReadIndex = 6; // position to read last 2 bits
			var read = storage.Read(out byte b, 4); // try to read 4 bits, only 2 available
			Assert.Equal(2, read); // only 2 bits read
			Assert.Equal(0b10, b); // last two bits are '10'
		}

		[Fact]
		public void Read_ReadZeroBool_GetZeroData()
		{
			var storage = new BitStorage();

			var read = storage.Read(out bool bit, 0);

			Assert.Equal(0, read);
			Assert.False(bit);
			Assert.Equal(0, storage.LastReadBitCount);
		}
		[Fact]
		public void Read_ReadBoolPastEnd_GetZeroData()
		{
			var storage = new BitStorage();

			var read = storage.Read(out bool bit, 1);

			Assert.Equal(0, read);
			Assert.False(bit);
			Assert.Equal(0, storage.LastReadBitCount);
		}

		[Fact]
		public void Read_GetData()
		{
			byte[] input = [0b1010_1010, 0b1100_0011, 0b1111_1111];
			var storage = BitStorage.BitStorageFactory(input);
			var data = storage.GetData().ToArray();
			Assert.Equal(input, data);
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

			Assert.Equal(8, storage.Count);
			Assert.True(storage[0]);
			Assert.True(storage[3]);
			Assert.True(storage[7]);
			Assert.False(storage[1]);
			Assert.False(storage[2]);
			Assert.False(storage[4]);
			Assert.False(storage[5]);
			Assert.False(storage[6]);
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBit()
		{
			var storage = new BitStorage();

			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write((byte)0); // ensures storage has capacity

			// set some bits via indexer
			storage[2..5] = [true, true, true];

			Assert.Equal(8, storage.Count);
			Assert.Equal(0b0011_1000, storage.Read<byte>()); // bits 2,3,4 set
		}

		[Fact]
		public void Indexer_SetAndGet_MultipleBitAcrossByteBoundary()
		{
			var storage = new BitStorage();

			// set bits 0..7 using Write(byte) for convenience (one byte)
			storage.Write<byte>([0, 0]); // ensures storage has capacity

			// set some bits via indexer
			storage[7..10] = [true, true, true];

			Assert.Equal(16, storage.Count);
			Assert.Equal(0b0000_0001, storage.Read<byte>()); // bits 2,3,4 set
			Assert.Equal(0b1100_0000, storage.Read<byte>()); // bits 2,3,4 set
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
			var s = BitStorage.BitStorageFactory(src);

			Assert.Equal(8, s.Count);

			s.ReadIndex = 0;
			var outBytes = s.ReadEnumerable<byte>().ToArray();
			Assert.Single(outBytes);
			Assert.Equal(src[0], outBytes[0]);
		}

		[Fact]
		public void Insert_InsertVariousEdgeCases()
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
						var result = baseStorage.Insert(k, ins);
						Assert.Equal(baseStorage.Count + ins.Count, result.Count);
						result.ReadIndex = 0;
						var expected = data.Take(k).Concat(insertData.Take(j)).Concat(data.Skip(k).Take(i - k)).ToArray();
						var r = result[..];
						Assert.Equal(expected, r);
					}
				}
			}
		}

		[Fact]
		public void Insert_InsertEmptyBitStorage_NoChange()
		{
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true]; // length 4
			baseStorage.Write(baseBits);
			var ins = new BitStorage(); // empty insertion
			var result = baseStorage.Insert(2, ins);
			Assert.Equal(baseStorage.Count, result.Count);
			result.ReadIndex = 0;
			var r = result[..];
			Assert.Equal(baseBits, r);
		}

		[Fact]
		public void Insert_InsertBitStorageAtBeginningMiddleEnd_ProducesExpectedSequence()
		{
			// base storage: bits [A B C D] where each letter is a single bit value for clarity
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true, true, false]; // length 6
			baseStorage.Write(baseBits);

			// insertion storage: two bits [1,1]
			var ins = new BitStorage();
			ins.Write([true, true]);

			// Insert at beginning (index 0)
			var result0 = baseStorage.Insert(0, ins);
			Assert.Equal(baseBits.Length + 2, result0.Count);
			result0.ReadIndex = 0;
			var r0 = result0[..];
			Assert.Equal([true, true, .. baseBits], r0);

			// Insert in middle (index 3)
			var resultMid = baseStorage.Insert(3, ins);
			resultMid.ReadIndex = 0;
			var rMid = resultMid[..];
			var expectedMid = baseBits.Take(3).Concat([true, true]).Concat(baseBits.Skip(3)).ToArray();
			Assert.Equal(expectedMid, rMid);

			// Insert at end (index == Count)
			var resultEnd = baseStorage.Insert(baseBits.Length, ins);
			resultEnd.ReadIndex = 0;
			var rEnd = resultEnd[..];
			Assert.Equal([.. baseBits, true, true], rEnd);
		}

		[Fact]
		public void Insert_InsertValueAtBeginningMiddleEnd_ProducesExpectedSequence()
		{
			// base storage: bits [A B C D] where each letter is a single bit value for clarity
			var baseStorage = new BitStorage();
			bool[] baseBits = [false, true, false, true, true, false]; // length 6
			baseStorage.Write(baseBits);

			// insertion storage: two bits [1,1]
			var ins = 3;

			// Insert at beginning (index 0)
			var result0 = baseStorage.Insert(0, ins, 2);
			Assert.Equal(baseBits.Length + 2, result0.Count);
			result0.ReadIndex = 0;
			var r0 = result0[..];
			Assert.Equal([true, true, .. baseBits], r0);

			// Insert in middle (index 3)
			var resultMid = baseStorage.Insert(3, ins, 2);
			resultMid.ReadIndex = 0;
			var rMid = resultMid[..];
			var expectedMid = baseBits.Take(3).Concat([true, true]).Concat(baseBits.Skip(3)).ToArray();
			Assert.Equal(expectedMid, rMid);

			// Insert at end (index == Count)
			var resultEnd = baseStorage.Insert(baseBits.Length, ins, 2);
			resultEnd.ReadIndex = 0;
			var rEnd = resultEnd[..];
			Assert.Equal([.. baseBits, true, true], rEnd);
		}

		[Fact]
		public void Insert_WithByteAlignedPrefixAndSuffix_Works()
		{
			// construct storage with 12 bits: two full bytes (8) + 4 bits
			var s = new BitStorage();
			// write 2 bytes: 0xAA, 0xCC  (10101010, 11001100) => 16 bits, then we'll only consider prefix/suffix positions
			s.Write(new byte[] { 0xAA, 0xCC });
			// truncate to 12 bits by writing only first 12 bits (read back truncated)
			// create storage with exactly 12 bits by building from bools
			var truncated = new BitStorage();
			truncated.Write([
				true,false,true,false,true,false,true,false, // first byte 0xAA
				true,true,false, false // top 4 bits of 0xCC (1100)
			]);
			// Insert 3 bits [1,0,1] at index 5 (within first byte)
			var ins = new BitStorage();
			ins.Write([true, false, true]);

			var result = truncated.Insert(5, ins);
			result.ReadIndex = 0;
			var outBits = result[..];

			// build expected
			var expected = truncated[new Range(0, 5)].Concat([true, false, true]).Concat(truncated[new Range(5, truncated.Count)]).ToArray();
			// compare
			Assert.Equal(expected, outBits);
		}

		[Fact]
		public void ReadEnumerable_WithBitsRead_OutParameter_IsUpdated()
		{
			var s = new BitStorage();
			// write three bytes (24 bits)
			s.Write(new byte[] { 0x01, 0x02, 0x03 });
			s.ReadIndex = 0;

			var bitsRead = new BitStorage.BitsRead();
			var items = s.ReadEnumerable<byte>(bitsRead: bitsRead).ToArray();

			// LastReadBitCount should reflect the last read element's bit count (8)
			Assert.Equal(8, s.LastReadBitCount);
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
			var s = new BitStorage();
			// 3 bytes = 24 bits with distinct pattern
			var input = new byte[] { 0xAA, 0xBB, 0xCC }; // 10101010 10111011 11001100
			s.Write(input);
			// set a non-zero read index so remaining bits != Count

			// Build expected by manually reading chunks using Read<T>(out,..)
			var manual = new BitStorage(s); // copy so we can consume manually
			manual.ReadIndex = 4;
			var expectedList = new List<byte>();
			while (manual.ReadIndex < manual.Count)
			{
				int remaining = manual.Count - manual.ReadIndex;
				int toRead = Math.Min(8, remaining);
				Assert.Equal(toRead, manual.Read(out byte val, toRead));
				expectedList.Add(val);
			}

			// Now call ReadEnumerable on the original storage (this will also consume)
			s.ReadIndex = 4;
			var actual = s.ReadEnumerable<byte>().ToArray();

			Assert.Equal(expectedList.Count, actual.Length);
			Assert.Equal([.. expectedList], actual);
		}

		[Fact]
		public void Insert_NearEndPartialByte_ProducesExpectedBitSequence()
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
			var result = baseStorage.Insert(insertIndex, ins);

			// Build expected sequence: prefix (0..index-1), inserted bits, suffix (index..end-1)
			var expected = baseBits.Take(insertIndex)
				.Concat([true, false, true])
				.Concat(baseBits.Skip(insertIndex))
				.ToArray();

			result.ReadIndex = 0;
			var actual = result[..];

			Assert.Equal(expected.Length, result.Count);
			Assert.Equal(expected, actual);
		}

		[Fact]
		public void Insert_AtEnd_AppendsBitsCorrectly()
		{
			bool[] baseBits = [true, false, true, false, false]; // 5 bits
			var baseStorage = new BitStorage();
			baseStorage.Write(baseBits);

			var ins = new BitStorage();
			ins.Write([false, true, true]); // 3 bits

			var result = baseStorage.Insert(baseBits.Length, ins); // insert at end

			var expected = baseBits.Concat([false, true, true]).ToArray();

			result.ReadIndex = 0;
			var actual = result[..];

			Assert.Equal(expected.Length, result.Count);
			Assert.Equal(expected, actual);
		}
		[Fact]
		public void RemoveRange_MiddleRange_RemovesCorrectBits()
		{
			var baseBits = new[] { true, false, true, false, true, false, true };
			var s = new BitStorage();
			s.Write(baseBits);

			int index = 2;
			int count = 3; // remove bits at 2,3,4
			var original = s[..];
			var r = s.RemoveRange(index, count);

			var expected = baseBits.Take(index).Concat(baseBits.Skip(index + count)).ToArray();
			Assert.Equal(expected.Length, r.Count);

			var actual = r[..];
			Assert.Equal(expected, actual);

			// original unchanged
			Assert.Equal(baseBits, original);
		}

		[Fact]
		public void RemoveRange_FromStart_RemovesPrefix()
		{
			var baseBits = new[] { false, true, true, false, true };
			var s = new BitStorage();
			s.Write(baseBits);

			var r = s.RemoveRange(0, 2);

			var expected = baseBits.Skip(2).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, r[..]);
		}

		[Fact]
		public void RemoveRange_ToEnd_RemovesSuffix()
		{
			var baseBits = new[] { true, true, false, false, true };
			var s = new BitStorage();
			s.Write(baseBits);

			int index = 3;
			int count = 2; // remove last 2 bits
			var r = s.RemoveRange(index, count);

			var expected = baseBits.Take(index).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, r[..]);
		}

		[Fact]
		public void TrimEnd_RemovesSuffix()
		{
			var baseBits = new[] { true, true, false, false, true };
			var s = new BitStorage();
			s.Write(baseBits);

			int index = 3;
			int count = 2; // remove last 2 bits
			var r = s.TrimEnd(count);

			var expected = baseBits.Take(index).ToArray();
			Assert.Equal(expected.Length, r.Count);
			Assert.Equal(expected, r[..]);
		}

		[Fact]
		public void RemoveRange_RemoveAll_ResultsEmptyStorage()
		{
			var baseBits = Enumerable.Range(0, 10).Select(i => i % 2 == 0).ToArray();
			var s = new BitStorage();
			s.Write(baseBits);

			var r = s.RemoveRange(0, s.Count);

			Assert.Equal(0, r.Count);
			Assert.Empty(r[..]);
		}

		[Fact]
		public void RemoveRange_CountZero_ReturnsCopy()
		{
			var baseBits = new[] { true, false, true };
			var s = new BitStorage();
			s.Write(baseBits);

			var r = s.RemoveRange(1, 0);

			Assert.Equal(s.Count, r.Count);
			Assert.Equal(baseBits, r[..]);
		}

		[Fact]
		public void RemoveRange_InvalidArguments_Throw()
		{
			var s = new BitStorage();
			s.Write([true, false, true]);

			Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRange(-1, 1)); // invalid index
			Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRange(1, -1)); // invalid count
			Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRange(4, 1)); // index > Count
			Assert.Throws<ArgumentOutOfRangeException>(() => s.RemoveRange(1, 5)); // index+count > Count
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
			var s1 = BitStorage.BitStorageFactory(data);
			var s2 = BitStorage.BitStorageFactory(data);
			s2[^1] = !s2[^1]; // flip last bit
			Assert.False(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_DifferentLastBitOn_AcrossBoundary_ReturnsFalse()
		{
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			var s1 = BitStorage.BitStorageFactory(data);
			var s2 = BitStorage.BitStorageFactory(data);
			s1.Write(true);
			s2.Write(false);
			Assert.False(s1.ContentEquals(s2));
		}
		[Fact]
		public void Equals_SameDataDifferentOutsideData_ReturnsTrue()
		{
			var data = new byte[] { 0b1011_0010, 0b1101_1110, 0b0110_1011 };
			var s1 = BitStorage.BitStorageFactory(data);
			s1.Write(false);
			s1.Write([true, true]);
			// Trim changes the cound, but leaves the data, so the underlying storage still has the [true, true] at the end, but the bitstorage count does not include them
			s1 = s1.TrimEnd(2);
			var s2 = BitStorage.BitStorageFactory(data);
			s2.Write(false);
			Assert.True(s1.ContentEquals(s2));
		}
	}
}