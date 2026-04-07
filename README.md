# BitStorage
## Info

I was working on another personal project (nothing big, just a for fun project to keep my skills up) and I 
had to store a lot of bits. I thought it would be interesting to create a bit storage system that could store 
bits in a more efficient and easier way than using a byte/bool array or BitArray. My requirements were that I 
wanted to push an arbitrary number of bits and be able to read them all or just a few bits at a time.

As an example, I may want to store 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, etc. and
be able to store all of them somewhere (file, memory, etc.). Later, I want to read all of them back in, then
be able to read 3 bits, then 15 bits, then 87 bits, then 1 bit, then 2 bits, (but the read bit lengths don't
have to agree with the written bit lengths, so 8 bits or whatever could have been read first instead of 3) etc. 
and have them all be in the same order that was written.

Because of these requirements, I decided that it would be easier to create a class that would handle the bit 
storage and retrieval instead of using BitArray.

I was debating on how to store the underlying bits.  I finally settled for a list of bytes.  I believe this
can easily be changed to a list of other numeric types, but I haven't tried.  The storage is also MSB-first 
(big-endian inside each byte), meaning that if you push 0b1, it will be stored as 0b10000000, not 0b00000001.
Adding 0b011 will result in 0b10110000.  This is because I wanted to be able to read the bits back in the 
same order that they were written and not have the underlying storage change.

I could have shifted the bits in (e.g. add 0b1 and getting 0b00000001, then 0b011 and getting 0b00001011), but 
that would mean that the individual locations of the bits would potentially change after every write and I 
didn't want that. It doesn't have much effect on anything, except if you read the data using GetData(), you
would see the exact data on the last byte written, not the converted data that ReadEnumerable<T>() would give you.
GetData() is faster than ReadEnumerable<T>() because it doesn't have to loop through each element, it just returns
a copy of the underlying byte list.  ReadEnumerable<T>() has the benefit of being able to read a specific number 
of bits, starting with the current ReadIndex.

Another limitation is that negative numbers are not allowed to be stored, only positive numbers  Signed integer types
are accepted, but must be non-negative (this will be validated).  This is because I wanted to keep the code simple 
and not have to deal with negative numbers.  I may add this in the future, but for now, it's easiest to not allow 
them.  I also wanted to keep the code as fast as possible.  There is also the issue of not using all the bits when 
writing (you can specify how many bits of a number to write), so if a negative number was added and number of bits 
was less than the data type, what should be pushed in.  It was easier to just not allow negative numbers.  
I may revisit this.

## Usage
### Classes

| Class | Description |
| ----- | ----------- |
| BitStorage | The main class that contains the bit storage functionality. |
| BitStorage.BitsRead | Helper class containing a single int called 'BitsReadCount'.  This is a hack (similar to 'LastReadBitCount'). When reading through the enumerable, if an object of this type has been sent to the ReadEnumerable method, the 'BitsReadCount' will be updated.  The reason for this is a yield return doesn't allow multiple values, out, or ref objects. |

### Constructors
There are only 2 constructors: the no-argument constructor and one that takes an existing BitStorage object and 
makes a copy of the data.

In addition to the constructors, there is a generic static `Create` method used to create a BitStorage object with an
enumerable collection of any valid numeric data type.  There are 2 optional parameters for this method: `bitsToWrite` 
and `elementBitsToWrite`. Each of these defaults to null, which means to write all bits in the collection and all 
bits in each element of the collection.  In the case of `bitsToWrite`, null means to write all elements in the collection.  
If this parameter is specified, it will only write that many bits from the collection. In the case of `elementBitsToWrite`, 
null means to write the entire element. If this parameter is specified, it will only write that 
many bits from each element of the collection with a total of `bitsToWrite` bits.

E.g. a string is made up of 16 bit characters, so if you wanted to write only the lower 8 bits of each character, 
you would set `elementBitsToWrite` to 8. If you wanted to write only the first 2 characters at 8 bits/character
of the string, you would set `bitsToWrite` to 16 (8 bits per character x 2 characters written).

```csharp
// This will write the first 2 characters of the string "Hello World" as 8 bits each, for a total of 16 bits.
BitStorage.Create("Hello World", bitsToWrite: 16, elementBitsToWrite: 8);
```

NOTE: In the above code, if `bitsToWrite` and `elementBitsToWrite` were both null, there would be 176 bits written
(char is 16 bits, so 11 characters x 16 bits = 176 bits).  If only `bitsToWrite` was set to 32, there would be 32 bits written
(2 characters x 16 bits). If only `elementBitsToWrite` was set to 8, there would be 88 bits written (11 characters x 8 bits).

| Constructor | Description |
| ----------- | ----------- |
| `BitStorage()` | Creates an empty BitStorage object. |
| `BitStorage(BitStorage bits)` | Creates a BitStorage object with the specified BitStorage object. This is a copy constructor. The `bits` parameter is the BitStorage object to copy. |

| Factory Method | Description |
| -------------- | ----------- |
| `Create<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a BitStorage object with the specified collection. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`|


### Properties

| Property | Description |
| -------- | ----------- |
| `int Count` | Returns the number of bits in the BitStorage object. |
| `int LastReadBitCount` | Returns the number of bits that were read from the last read operation (any read, including enumerator). This is a bit of a hack because the enumerator returned by `ReadEnumerable<T>()` can't return the number of bits that were read as the number of bits will only be known when the enumerator is done.|
| `int ReadIndex` | Gets or sets the index of the next bit to read. Valid values are 0 to `Count`. If the `ReadIndex` is set to `Count`, the data from any read method will be 0 (no bits can be read after the end of the storage) and the `LastReadBitCount` will be 0.
| `int WriteIndex` | Gets or sets the index of the next bit to write. Valid values are 0 to `Count`. |
| `bool [int]` | Gets or sets the specific bit at the specified index. The index is 0-based and valid values are 0 to `Count - 1`. The value is a boolean, so it can be true or false. If the index is out of range, an exception will be thrown. |
| `bool[] [Range]` | Gets or sets the specific bits at the specified range. The range is 0-based and valid values are 0 to `Count - 1` for the start range and `Count` for the end range. The value is a boolean array, so items can be true or false. If the range is out of range, an exception will be thrown. |



### Methods
| Method | Description |
| ------ | ----------- |
| `void Clear()` | Clears the BitStorage object and resets all indices. |
| `List<byte> GetData()` | Returns all the data stored as a List of bytes. This returns all the data and is independent of the 'ReadIndex'.  This will also mask the end bits if necessary due to removing bits |
| `IEnumerable<T> ReadEnumerable<T>(int? bitsToRead=null, BitsRead? bitsRead = null)` | Returns an enumerable of the next `bitsToRead` bits. This will return the bits in the order they were written. The `ReadIndex` will be updated to the next bit after the last bit read. The `bitsToRead` parameter is optional and defaults to null, which means to read all bits. If `bitsToRead` is greater than the number of bits in the BitStorage object, it will read all remaining bits. For every enumerable element, the `LastReadBitCount` property will be updated. If you supply a `BitsRead` object, the `BitsReadCount` will receive the last element's bit count as well. |
| `T Read<T>(out int bitsReadCount)` | Reads the next number of bits based on the T data type. The `ReadIndex` will be updated to the next bit after the last bit read. The `bitsReadCount` parameter will be set to the number of bits that were read. The `LastReadBitCount` property will also be updated |
| `T Read<T>()` | Reads the next number of bits based on the T data type. The `ReadIndex` will be updated to the next bit after the last bit read.  The number of bits read will have to be assumed by the calling program, or use the `LastReadBitCount` property. |
| `int Read<T>(out T bitsRead, int? bitsToRead = null)` | Reads the next `bitsToRead` bits and stores them in `bitsRead`. The `ReadIndex` will be updated to the next bit after the last bit read. The `bitsToRead` parameter is optional and defaults to null, which means to read all bits. If `bitsToRead` is greater than the number of bits in the BitStorage object, it will read all remaining bits. This is different than the `T Read<T>` methods because it returns the number of bits read instead of the actual bits read.  This can be useful in an if or loop (e.g. `if(s.Read(out int bits) != 0)...`) |
| `BitStorage Write(BitStorage bits)` | Writes the BitStorage object to the current BitStorage object from the current `WriteIndex`. The `WriteIndex` will be updated to the next bit after the last bit written. |
| `BitStorage Write<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Writes the value to the BitStorage object. The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`. The `elementBitsToWrite` parameter is also optional and defaults to null, which means to write all bits in each element of `bits`. The `WriteIndex` will be updated to the next bit after the last bit written. |
| `BitStorage Write<T>(T bits, int? bitsToWrite = null)` | Writes the bits to the BitStorage object.  This must be a valid data type (i.e. non-float numeric type). The `bitsToWrite` parameter is optional and defaults to null, which means to write all bits in `bits`.  If `bitsToWrite` is negative, or greater than the number of bits in the data type (e.g. 31 for `int` -- negatives are not allowed), an error will be thrown. The `WriteIndex` will be updated to the next bit after the last bit written. |
| `BitStorage Insert(int index, BitStorage bits)` | Inserts the specified BitStorage object at the given index within the current object. This will adjust the Read and Write indices to continue to point to the actual bit (not the position) they were pointing to prior to the insert.  E.g. if the `ReadIndex` was pointing to the 2nd to the last bit, and 3 bits were inserted in the 3rd to the last bit, it will still be pointing to the 2nd to the last bit after an insert. |
| `BitStorage Insert<T>(int index, T bits, int? bitsToWrite = null)` | Inserts the bits to the BitStorage object.  This must be a valid data type (i.e. non-float numeric type). This will adjust the Read and Write indices to continue to point to the actual bit they were pointing to prior to the insert. |
| `BitStorage InsertAsCopy(int index, BitStorage bits) ` | This behaves like the `Insert(int index, BitStorage bits)`, except it doesn't modify the existing object, it creates a new one with the bits inserted. |
| `BitStorage InsertAsCopy<T>(int index, T bits, int? bitsToWrite = null)` | This behaves like the `Insert<T>(int index, T bits, int? bitsToWrite = null)`, except it doesn't modify the existing object, it creates a new one with the bits inserted. |
| `BitStorage RemoveRange(int index, int count)` | Removes the number of bits starting at `index` for `count` bits.  E.g. `Remove(2,3)` will remove bits 2, 3, and 4. |
| `BitStorage TrimEnd(int count)` | Removes the `count` of bits from the end |
| `BitStorage RemoveRangeAsCopy(int index, int count)` | This behaves like `RemoveRange(int index, int count)`, except it doesn't modify the existing object, it creates a new one with the range removed. |
| `BitStorage TrimEndAsCopy(int count)` | This behaves like `TrimEnd(int count)`, except it doesn't modify the existing object, it creates a new one with the range removed. |
| `bool ContentEquals(BitStorage? other)` | Examines the current `BitStorage` object and compares it to the `other`.  Returns true if all bits are equal. |

All data manipulation methods will return the current `BitStorage` instance (Write, Insert, RemoveRange, TrimEnd) to allow fluent coding.
E.g. `s.Write((byte)123).Write(true).Write("hello, world");` will write 0b01111011, then 1, then "hello, world" (all characters as 16-bit numbers).

`InsertAsCopy`, `RemoveRangeAsCopy`, and `TrimEndAsCopy` do not manipulate existing data so they return a new object and should not be used for chaining.

#### Constructor Examples
```csharp
// creates an empty BitStorage object
BitStorage bs = new BitStorage();

// creates a BitStorage object with 0b10100000 as the first element with a length of 3 bits
BitStorage bs = BitStorage.Create(new List<bool> { true, false, true });

// creates a BitStorage object with 0b01111011, 0b00101010, 0b00000011 as the first elements
BitStorage bs = BitStorage.Create(new List<byte> { 123, 42, 3 });

// creates a BitStorage object with the string "Hello World!" with 8 bits per character, for a total of 96 bits
BitStorage bs = BitStorage.Create("Hello World!", elementBitsToWrite: 8);
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
bitStorage.Write(true)
          .Write(false)
          .Write(true)
          .Write(true);
// write the first individual byte test data
bitStorage.Write(individualByte1);
// write the test array data, but only write 8 bits per element
bitStorage.Write(testString, elementBitsToWrite: 8); // default would be 16 bits for char data
bitStorage.Write(bytes); // default is 8 bits for byte types
// write the second individual byte with only 5 bits
bitStorage.Write(individualByte2, individualByte2Size);

// at this point, the bitStorage can be written somewhere using bitStorage.GetData()
// depending on how it's written, the current bitStorage.Count can be inserted at the beginning or end and used to read the original data back in

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