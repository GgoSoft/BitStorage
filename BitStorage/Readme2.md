# BitStorage

A compact, bit container that lets you write and read arbitrary numbers of bits in sequence.  
Written bits keep their write order and bit positions (MSB-first within each storage element). Useful when you need to push and later read sequences of variable bit lengths (e.g. 3 bits, 15 bits, 87 bits, 1 bit, ...).

This README is current for the implementation in `BitStorage/Storage/BitStorage.cs` (MSB-first, backing store = `List<byte>`).

Highlights
- MSB-first within each byte: writing the value `0b1` stores `0b1000_0000` in the current byte.
- Read/write at bit granularity; `ReadIndex` and `WriteIndex` are expressed in bits.
- Supports typed bulk Read/Write for supported integral types and `bool`.
- Partial‑byte writes are supported (you can write N bits from any supported value).
- Operations that return new storages: `InsertAsCopy` (insert bits), `RemoveRangeAsCopy` and `TrimEndAsCopy` (remove bits). These methods are pure and return a new `BitStorage` instance.
- Fast raw access: `GetData()` returns a copy of the underlying bytes (may contain unused low bits in the final byte).
- Value equality helper: `ContentEquals` compares bit content (ignores unused low bits in the last element).

Supported element types (for Read/Write/ReadEnumerable)
- `ulong`, `uint`, `ushort`, `char`, `byte`
- `long`, `int`, `short`, `sbyte` — treated as non‑negative values (one bit less available to avoid sign handling)
Attempting to use an unsupported type or negative values throws `ArgumentException`.

Important properties
- `int Count` — number of bits stored.
- `int ReadIndex` — next bit index to read (0..Count). Setting moves internal read cursor.
- `int WriteIndex` — next bit index to write (0..Count). Setting moves internal write cursor.
- `int LastReadBitCount` — number of bits read by the last `ReadEnumerable` / `Read` operation (useful because enumerables yield values of varying bit lengths).

Key methods (short)
- `void Write<T>(T value, int? bitsToWrite = null)` — append up to `bitsToWrite` bits of `value` (or full element when null). Error if `bitsToRead` is more than available in `T`.
- `void Write<T>(IEnumerable<T> values, int? bitsToWrite = null, int? elementBitsToWrite = null)` — write many elements; supports truncation both globally and per-element. Error if `bitsToWrite` is more than available in `values` or `elementBitsToWrite` is more than available in `T`.
- `int Read<T>(out T value, int? bitsToRead = null)` — read up to `bitsToRead` bits into `value`; returns actual number of bits read. Error if `bitsToRead` is more than available in `T`.
- `IEnumerable<T> ReadEnumerable<T>(int? bitsToRead = null, BitsRead? bitsRead = null)` — enumerate values built from consecutive bit sequences; `bitsRead` (if supplied) is updated with the last element's bit count.
- `void Write(BitStorage other)` — append another BitStorage's bits.
- `BitStorage InsertAsCopy(int index, BitStorage bits)` / `InsertAsCopy<T>(int index, T bits, int? bitsToWrite)` — return a new storage with bits inserted.
- `BitStorage Insert(int index, BitStorage bits)` / `Insert<T>(int index, T bits, int? bitsToWrite)` — inserts bits into current object.
- `BitStorage RemoveRangeAsCopy(int index, int count)` / `TrimEndAsCopy(int count)` — return a new storage with the requested bits removed.
- `BitStorage RemoveRange(int index, int count)` / `TrimEnd(int count)` — removes requested bits from current object.
- `IEnumerable<byte> GetData()` — returns a copy of the underlying byte list (final byte may include unused low bits).
- `string PrintBits(int start = 0, int? end = null)` — debug helper that prints bytes and bit view (uses `GetMask` internally).

Behavior notes and gotchas
- Bit ordering: bits are stored and read MSB → LSB inside each byte. Example: writing single bits `1, 0, 1` produces the first byte `0b1010_0000` (assuming those are first bits written).
- Partial last byte: `GetData()` returns the raw bytes; unused low bits of the last byte are not guaranteed zeroed by all operations (some methods adjust them, other operations may keep prior byte contents). If you need a minimal byte array with the last byte masked, use `ReadEnumerable<byte>()` to reconstitute bytes or mask the last byte yourself using `Count`.
- Negative numbers are not supported for numeric writes — attempting to write a negative value throws `ArgumentOutOfRangeException`.
- Many API methods validate inputs and will throw `ArgumentOutOfRangeException` or `ArgumentException` for invalid ranges, unsupported types, or invalid bit counts.

Examples

Basic write / read (booleans)

```
var s = new BitStorage();
s.Write(true).
  Write(false).
  Write(true); // Count == 3
s.ReadIndex = 0;
bool first = s.Read<bool>(); // true
```

Write bytes and read them back as bytes

```
var s = new BitStorage();
s.Write(new byte[] { 0xAA, 0xBB }); // writes 16 bits
s.ReadIndex = 0;
var bytes = s.ReadEnumerable<byte>().ToArray(); // { 0xAA, 0xBB }
```

Partial-bit write and read
```
var s = new BitStorage();
s.Write((byte)5, 3); // write 3 bits from value 0b101 => stored bits: [1,0,1]
s.ReadIndex = 0;
bool[] bits = new bool[3];
for (int i = 0; i < 3; i++) bits[i] = s.Read<bool>();
// bits == [true, false, true]
```

Insert / remove (pure operations that return new storage)
```
var a = new BitStorage();
a.Write(new bool[] { false, true, false, true }); // 4 bits
var ins = new BitStorage(); 
ins.Write(new bool[] { true, true }); // 2 bits
var b = a.InsertAsCopy(2, ins); // b has bits: [false, true, true, true, false, true]
var c = b.RemoveRangeAsCopy(1, 2); // removes 2 bits at index 1
```

Comparing content (ignores unused low bits in last byte)
```
var s1 = new BitStorage(); 
s1.Write(new byte[] { 0xAA, 0xF0 }); 
s1.Count = 12; // example
var s2 = new BitStorage(s1);
bool equal = s1.ContentEquals(s2);
```

Testing tips
- Add unit tests for edge cases: partial-byte boundaries, Insert at 0/Count, RemoveRange at end, read with non-zero ReadIndex.
- When asserting byte-level roundtrips, prefer `ReadEnumerable<byte>()` or mask the final byte according to `Count`.

If you want, I can:
- Add `ToByteArray()` / `FromByteArray()` convenience methods (non‑destructive, mask final byte according to Count) and corresponding unit tests.
- Produce a short "migration" section describing expected behavior for callers who previously used `GetData()` + manual masking.

License / repository
See repository root for license and contribution notes.
```