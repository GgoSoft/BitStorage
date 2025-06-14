# BitStorage
## Info

I was working on another personal project (nothing big, just a for fun project to keep my skills up) and I 
had to store a lot of bits. I thought it would be interesting to create a bit storage system that could store 
bits in a more efficient and easier way than using a byte array or BitArray. My requirements were that I 
wanted to push an arbitrary number of bits and be able to read them all or just a few bits at a time.

As an example, I may want to store 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, etc. and
be able to store all of them somewhere (file, memory, etc.). Later, I want to read all of them back in, then
be able to read 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, (but the read bit lengths don't
have to agree with the written bit lengths, so 8 bits or whatever could have been read first instead of 3) etc. 
and have them all be in the same order that was written.

Because of these requirements, I decided that it would be easier to create a class that would handle the bit 
storage and retrieval instead of using BitArray.

I was debating on how to store the underlying bits.  I finally settled for a list of bytes.  I believe this
can easily be changed to a list of other numeric types, but I haven't tried.  The storage is also big-endian,
meaning that if you push 0b1, it will be stored as 0b10000000, not 0b00000001.  Adding 0b011 will result in
0b10110000.  This is because I wanted to be able to read the bits back in the same order that they were written
and not have the underlying storage change.

I could have shifted the bits in (e.g. add 0b1 and getting 0b00000001, then 0b011 and getting 0b00001011), but 
that would mean that the individual locations of the bits would potentially change after every write and I 
didn't want that. It doesn't have much effect on anything, except if you read the data using GetData(), you
would see the exact data on the last byte written, not the converted data that ReadEnumerable<T>() would give you.
GetData() is faster than ReadEnumerable<T>() because it doesn't have to loop through each element, it just returns
a copy of the underlying byte list.  ReadEnumerable<T>() has the benefit of being able to read a specific number 
of bits, starting with the current ReadIndex.

Another limitation is that negative numbers are not allowed, only positive numbers.  This is because I wanted to
keep the code simple and not have to deal with negative numbers.  I may add this in the future, but for now,
I just wanted to keep it simple.  I also wanted to keep the code as fast as possible.  There is also the issue
of not using all the bits, so if a negative number was added and number of bits was less than the data type, what
should be pushed in.  It was easier to just not allow negative numbers.  I may revisit this in the future.

## Usage
### Classes

| Class | Description |
| ----- | ----------- |
| BitStorage.BitsRead | Contains a single int called 'BitsReadCount'.  This is a hack (similar to 'LastReadBitCount'). When reading through the enumerable, if an object of this type has been sent to the method, the 'BitsReadCount' will be updated.  The reason for this is a yield return doesn't allow multiple values, out, or ref objects. |

### Constructors
Each IEnumerable constructor has 2 optional parameters: `bitsToWrite` and `elementBitsToWrite`. Each of these
defaults to null, which means to write all bits.  In the case of `bitsToWrite`, it means to write all bits in
the collection.  If this parameter is specified, it will only write that many bits from the collection. In the
case of `elementBitsToWrite`, it means to write all bits in each element of the collection. If this parameter is
specified, it will only write that many bits from each element of the collection with a total of `bitsToWrite`
bits.

E.g. a string is made up of 16 bit characters, so if you wanted to write only the first 8 bits of each character, 
you would set `elementBitsToWrite` to 8. If you wanted to write only the first 2 characters at 8 bits/character
of the string, you would set `bitsToWrite` to 16.

```csharp
// This will write the first 2 characters of the string "Hello World" as 8 bits each, for a total of 16 bits.
new BitStorage("Hello World", bitsToWrite: 16, elementBitsToWrite: 8);
```


| Constructor | Description |
| ----------- | ----------- |
| `BitStorage()` | Creates an empty BitStorage object. |
| `BitStorage(BitStorage bits)` | Creates a BitStorage object with the specified BitStorage object. This is a copy constructor. The `bits` parameter is the BitStorage object to copy. |
| `BitStorage(IEnumerable<bool> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of booleans. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`. The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<byte> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of bytes. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<sbyte> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of sbytes. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<short> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of shorts. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<ushort> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of ushorts. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<int> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of ints. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<uint> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of uints. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<long> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of longs. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|
| `BitStorage(IEnumerable<ulong> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection of ulongs. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|


### Properties

| Property | Description |
| -------- | ----------- |
| `int Count` | Returns the number of bits in the BitStorage object. |
| `int LastReadBitCount` | Returns the number of bits that were read from the last read operation. This is a bit of a hack because the ReadEnumerator can't return the number of bits that were read as the number of bits will only be known when the enumerator is done.|
| `int ReadIndex` | Gets or sets the index of the next bit to read. Valid values are 0 to `Count`. Technically, a `ReadIndex` of `Count` isn't valid, but after reading the last bit, it will be set to `Count`. Any bits that are read after this will be invalid. |
| `int WriteIndex` | Gets or sets the index of the next bit to write. Valid values are 0 to `Count`. Technically, a `WriteIndex` of `Count` isn't valid, but after writing the last bit, it will be set to `Count`. Any bits that are written after this will be invalid. |
| `bool Item[int]` | Gets or sets the specific bit at the specified index. The index is 0-based and valid values are 0 to `Count - 1`. The value is a boolean, so it can be true or false. If the index is out of range, an exception will be thrown. |
| `bool[] Item[Range]` | Gets or sets the specific bits at the specified range. The range is 0-based and valid values are 0 to `Count - 1` for the start range and `Count` for the end range. The value is a boolean array, so items can be true or false. If the range is out of range, an exception will be thrown. |


### Methods
| Method | Description |
| ------ | ----------- |
| `void Clear()` | Clears the BitStorage object and resets all indicies. |
| `IEnumerable<byte> GetData()` | Returns all the data stored as an Enumerable of bytes. This returns all the data and is independant of the 'ReadIndex'|
| `IEnumerable<T> ReadEnumerable<T>(int? bitsToRead=null, BitsRead? bitsRead = null)` | Returns an enumerable of the next `bitsToRead` bits. This will return the bits in the order they were written. The `ReadIndex` will be updated to the next bit after the last bit read. For every enumberable element, the `LastReadBitCount` property will be updated. The `bitsToRead` parameter is optional and defaults to null, which means to read all bits. If `bitsToRead` is greater than the number of bits in the BitStorage object, it will read all remaining bits. If `bitsRead` is not null, the `BitsReadCount` will be updated on that object. |
| `T Read<T>(out int bitsReadCount)` | Reads the next number of bits based on the T data type. The `ReadIndex` will be updated to the next bit after the last bit read. The `bitsReadCount` parameter will be set to the number of bits that were read. |
| `T Read<T>()` | Reads the next number of bits based on the T data type. The `ReadIndex` will be updated to the next bit after the last bit read.  The number of bits read will have to be assumed by the calling program, or use the `LastReadBitCount` property. |
| `int Read<T>(out T bitsRead, int? bitsToRead = null)` | Reads the next `bitsToRead` bits and stores them in `bitsRead`. The `ReadIndex` will be updated to the next bit after the last bit read. The `bitsToRead` parameter is optional and defaults to null, which means to read all bits. If `bitsToRead` is greater than the number of bits in the BitStorage object, it will read all remaining bits. |
| `void Write(BitStorage bits)` | Writes the BitStorage object to the current BitStorage object from the current `WriteIndex`. The `WriteIndex` will be updated to the next bit after the last bit written. |
| `void Write<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Writes the value to the BitStorage object. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`. The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`. The `WriteIndex` will be updated to the next bit after the last bit written. |
| `void Write<T>(T bits, int? bitsToWrite = null)` | Writes the value to the BitStorage object. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`. The `WriteIndex` will be updated to the next bit after the last bit written. |

#### Constructor Examples
```csharp
// creates an empty BitStorage object
BitStorage bs = new BitStorage();

// creates a BitStorage object with 0b10100000 as the first element with a length of 3 bits
BitStorage bs = new BitStorage(new List<bool> { true, false, true });

// creates a BitStorage object with 0b01111011, 0b00101010, 0b00000011 as the first elements
BitStorage bs = new BitStorage(new List<byte> { 123, 42, 3 });

// creates a BitStorage object with the string "Hello World!" with 8 bits per character, for a total of 96 bits
BitStorage bs = new BitStorage("Hello World!", elementBitsToWrite: 8);
```

#### Full Example
```csharp
// set up the test data
BitStorage bitStorage = new();
byte individualByte1 = 73;
int individualByte2Size = 5;
byte individualByte2 = 27;
byte[] bytes = [235, 83, 192, 48, 12, 192, 115, 78];
string testString = "Hello, World!";

// write the boolean test data
bitStorage.Write(true);
bitStorage.Write(false);
bitStorage.Write(true);
bitStorage.Write(true);
// write the first individual byte test data
bitStorage.Write(individualByte1);
// write the test array data, but only write 8 bits per element
bitStorage.Write(testString, elementBitsToWrite: 8); // default would be 16 bits for char data
bitStorage.Write(bytes); // default is 8 bits for byte types
// write the second individual byte with only 5 bits
bitStorage.Write(individualByte2, individualByte2Size);

// read the test boolean data
bool boolValue = bitStorage.Read<bool>();
Console.WriteLine($"Bit Read: {boolValue}");
boolValue = bitStorage.Read<bool>();
Console.WriteLine($"Bit Read: {boolValue}");
boolValue = bitStorage.Read<bool>();
Console.WriteLine($"Bit Read: {boolValue}");
boolValue = bitStorage.Read<bool>();
Console.WriteLine($"Bit Read: {boolValue}");
// read the first individual byte
Console.WriteLine($"Byte Manually Read: {ToBinary(bitStorage.Read<byte>(), 8)}\t Byte Expected: {ToBinary(individualByte1, 8)}");
// variable to hold the number of bits read
BitStorage.BitsRead bitsReadObject = new();
int count = 0;
Console.WriteLine("Reading 8 bit character data:");
// read the test string, which is 8 bits per character, and print each byte read
foreach (var byteRead in bitStorage.ReadEnumerable<byte>(8 * testString.Length, bitsRead: bitsReadObject))
{
	// Since these were all 8 bits, we don't need to check the BitsReadCount, but this is an example of how to use it
	string byteReadString = ToBinary(byteRead, bitsReadObject.BitsReadCount);
	// convert the expected byte to a binary string of length BitsReadCount and compare it to the array
	string byteExpectedString = ToBinary(testString[count], bitsReadObject.BitsReadCount);
	Console.WriteLine($"Byte/Char Read: {byteReadString}/{(char)byteRead}\t Byte/Char Expected: {byteExpectedString}/{testString[count]}   \tNum Bits Read: {bitsReadObject.BitsReadCount}");
	count++;
}
count = 0;
Console.WriteLine("Reading 8 bit byte data:");
// read the test string, which is 8 bits per character, and print each byte read
foreach (var byteRead in bitStorage.ReadEnumerable<byte>(8 * bytes.Length, bitsRead: bitsReadObject))
{
	// Since these were all 8 bits, we don't need to check the BitsReadCount, but this is an example of how to use it
	string byteReadString = ToBinary(byteRead, bitsReadObject.BitsReadCount);
	// convert the expected byte to a binary string of length BitsReadCount and compare it to the array
	string byteExpectedString = ToBinary(bytes[count], bitsReadObject.BitsReadCount);
	Console.WriteLine($"Byte Read: {byteReadString}\t Byte Expected: {byteExpectedString}   \tNum Bits Read: {bitsReadObject.BitsReadCount}");
	count++;
}
Console.WriteLine($"Reading last manual byte (written as {individualByte2Size} bits)");
int bitsRead = bitStorage.Read(out byte byteRead2);
string byteReadString2 = ToBinary(byteRead2, bitsRead);
string byteExpectedString2 = ToBinary(individualByte2, individualByte2Size);
Console.WriteLine($"Byte Read: {byteReadString2}\t Byte Expected: {byteExpectedString2}   \tNum Bits Read: {bitsRead}");


Console.WriteLine("Reading raw data");
// reset the read index to the beginning, read all the bytes and print them
bitStorage.ReadIndex = 0;
foreach (var byteRead in bitStorage.ReadEnumerable<byte>(bitsRead:bitsReadObject))
{
	Console.WriteLine($"{ToBinary(byteRead, 8)}\tNum Bits Read: {bitsReadObject.BitsReadCount}" );
}

```

Output
```
Bit Read: True
Bit Read: False
Bit Read: True
Bit Read: True
Byte Manually Read: 01001001     Byte Expected: 01001001
Reading 8 bit character data:
Byte/Char Read: 01001000/H       Byte/Char Expected: 01001000/H         Num Bits Read: 8
Byte/Char Read: 01100101/e       Byte/Char Expected: 01100101/e         Num Bits Read: 8
Byte/Char Read: 01101100/l       Byte/Char Expected: 01101100/l         Num Bits Read: 8
Byte/Char Read: 01101100/l       Byte/Char Expected: 01101100/l         Num Bits Read: 8
Byte/Char Read: 01101111/o       Byte/Char Expected: 01101111/o         Num Bits Read: 8
Byte/Char Read: 00101100/,       Byte/Char Expected: 00101100/,         Num Bits Read: 8
Byte/Char Read: 00100000/        Byte/Char Expected: 00100000/          Num Bits Read: 8
Byte/Char Read: 01010111/W       Byte/Char Expected: 01010111/W         Num Bits Read: 8
Byte/Char Read: 01101111/o       Byte/Char Expected: 01101111/o         Num Bits Read: 8
Byte/Char Read: 01110010/r       Byte/Char Expected: 01110010/r         Num Bits Read: 8
Byte/Char Read: 01101100/l       Byte/Char Expected: 01101100/l         Num Bits Read: 8
Byte/Char Read: 01100100/d       Byte/Char Expected: 01100100/d         Num Bits Read: 8
Byte/Char Read: 00100001/!       Byte/Char Expected: 00100001/!         Num Bits Read: 8
Reading 8 bit byte data:
Byte Read: 11101011      Byte Expected: 11101011        Num Bits Read: 8
Byte Read: 01010011      Byte Expected: 01010011        Num Bits Read: 8
Byte Read: 11000000      Byte Expected: 11000000        Num Bits Read: 8
Byte Read: 00110000      Byte Expected: 00110000        Num Bits Read: 8
Byte Read: 00001100      Byte Expected: 00001100        Num Bits Read: 8
Byte Read: 11000000      Byte Expected: 11000000        Num Bits Read: 8
Byte Read: 01110011      Byte Expected: 01110011        Num Bits Read: 8
Byte Read: 01001110      Byte Expected: 01001110        Num Bits Read: 8
Reading last manual byte (written as 5 bits)
Byte Read: 11011         Byte Expected: 11011           Num Bits Read: 5
Reading raw data
10110100        Num Bits Read: 8
10010100        Num Bits Read: 8
10000110        Num Bits Read: 8
01010110        Num Bits Read: 8
11000110        Num Bits Read: 8
11000110        Num Bits Read: 8
11110010        Num Bits Read: 8
11000010        Num Bits Read: 8
00000101        Num Bits Read: 8
01110110        Num Bits Read: 8
11110111        Num Bits Read: 8
00100110        Num Bits Read: 8
11000110        Num Bits Read: 8
01000010        Num Bits Read: 8
00011110        Num Bits Read: 8
10110101        Num Bits Read: 8
00111100        Num Bits Read: 8
00000011        Num Bits Read: 8
00000000        Num Bits Read: 8
11001100        Num Bits Read: 8
00000111        Num Bits Read: 8
00110100        Num Bits Read: 8
11101101        Num Bits Read: 8
00000001        Num Bits Read: 1	
```

In the previous example, the last loop could have used `bitStorage.GetData()` in conjunction with the `Count` 
property.  The biggest difference is that the last number would be "10000000" instead of "00000001".  The number of 
bits for each byte would have to be calculated (all would have 8 bits, but the last one may have fewer).