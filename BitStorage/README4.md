Absolutely — here is the **fully updated Public API Guide**, now aligned with:

- Your new `ReadEnumerable` behavior (permissive `bitsToRead`)
- Clearer explanations of partial-width final values
- Corrected examples
- Corrected bracket notation
- Clarified ValueReader indexing behavior
- All previous improvements (thread‑safety, reader creation paths, etc.)

This is the **complete, polished, ready‑to‑publish version**.

---

# **BitStorage System — Public API Guide (Updated Edition)**

This document describes the **public API** of the BitStorage system and how to use it to write, read, insert, remove, and interpret arbitrary-width bit sequences. Internal behavior is summarized only where it helps explain performance or storage characteristics.

---

# **1. Overview**

`BitStorage` is a dynamic bit-level buffer that allows:

- Writing **any number of bits** at any time  
- Reading **any number of bits** at any time  
- Writing collections where only the **lowest N bits** of each element are stored  
- Independent read and write cursors  
- Typed value-level reading (`BitStorageValueReader<T>`)  
- Insertion and removal of bit ranges  
- Efficient equality comparison  
- Fluent **method chaining** (all write methods return `this`)  

### **Thread‑Safety Overview**

- **`BitStorage` is not thread‑safe.**  
  It maintains a mutable write cursor and a mutable underlying `List<byte>`.  
  Concurrent writes or writes + reads on the *same* instance must be externally synchronized.

- **Readers (`BitStorageReader` and `BitStorageValueReader<T>`) never modify the underlying storage.**  
  They only maintain their own read cursor.

- **Readers always observe the live underlying data.**  
  They do **not** snapshot the buffer.

- **Multiple threads may safely create and use their own reader instances concurrently**, even while another thread is writing, as long as each thread uses its own reader.

---

# **2. BitStorage — Public API**

Below is a table of all public members of `BitStorage`, what they do, and examples of usage.

### **Thread‑Safety Note for BitStorage**

`BitStorage` is **not safe for concurrent mutation**.  
If multiple threads need to write or modify the same instance, callers must provide their own synchronization.

Multiple threads may **read** from a `BitStorage` *only if each thread uses its own `BitStorageReader`*.

---

## **2.1 Constructors**

| Signature | Description | Example |
|----------|-------------|---------|
| `BitStorage()` | Creates an empty bit buffer. | `var bs = new BitStorage();` |
| `BitStorage(BitStorage other)` | Creates a deep copy of another `BitStorage`. | `var copy = new BitStorage(original);` |

---

## **2.2 Properties**

| Signature | Description | Example |
|----------|-------------|---------|
| `int Count { get; }` | Number of meaningful bits stored. | `Console.WriteLine(bs.Count);` |
| `int WriteIndex { get; set; }` | Bit index where the next write will occur. Must be between `0` and `Count`. | `bs.WriteIndex = 0;` |

---

## **2.3 Indexers**

| Signature | Description | Example |
|----------|-------------|---------|
| `bool this[Index index] { set; }` | Sets a single bit at the given index. | `bs[^1] = true;` |
| `bool[] this[Range range] { set; }` | Sets a range of bits using a boolean array. | `bs[0..8] = new[]{true,false,...};` |

---

## **2.4 Core Methods**

| Signature | Description | Example |
|----------|-------------|---------|
| `BitStorage Write<T>(T bits, int? bitsToWrite = null)` | Writes a value of type `T` using `bitsToWrite` bits (or full width if null). Returns `this` for chaining. | `bs.Write(5, 3).Write(7, 3);` |
| `BitStorage Write<T>(IEnumerable<T> bits, int? bitsToWrite = null, int? bitsPerElement = null)` | Writes a sequence of values, optionally limiting bits per element and total bits. Returns `this`. | `bs.Write("Hello", bitsPerElement: 8).Write(0b101, 3);` |
| `BitStorage Write(BitStorage other)` | Appends all bits from another `BitStorage`. Returns `this`. | `bs.Write(other).Write(42, 6);` |
| `List<byte> GetData()` | Returns the underlying bytes (a `List<byte>`), masking unused bits in the final byte. | `var bytes = bs.GetData();` |
| `void Clear()` | Clears all bits and resets cursors. | `bs.Clear();` |
| `bool ContentEquals(BitStorage? other)` | Compares only meaningful bits (ignores garbage in unused bits). | `bs.ContentEquals(copy);` |

---

## **2.5 Structural Modification**

| Signature | Description | Example |
|----------|-------------|---------|
| `BitStorage Insert(int index, BitStorage bits)` | Inserts another bit buffer at a given bit index. | `bs.Insert(10, other);` |
| `BitStorage Insert<T>(int index, T bits, int? bitsToWrite = null)` | Inserts a value at a given bit index. | `bs.Insert(0, 0b1011, 4);` |
| `BitStorage InsertAsCopy(int index, BitStorage bits)` | Returns a new buffer with bits inserted. | `var newBs = bs.InsertAsCopy(5, other);` |
| `BitStorage InsertAsCopy<T>(int index, T bits, int? bitsToWrite = null)` | Returns a new buffer with a value inserted. | `var newBs = bs.InsertAsCopy(0, 42, 6);` |
| `BitStorage RemoveRange(int index, int count)` | Removes `count` bits starting at `index`. | `bs.RemoveRange(8, 4);` |
| `BitStorage RemoveRangeAsCopy(int index, int count)` | Returns a new buffer with bits removed. | `var trimmed = bs.RemoveRangeAsCopy(0, 16);` |
| `BitStorage TrimEnd(int count)` | Removes bits from the end. | `bs.TrimEnd(3);` |
| `BitStorage TrimEndAsCopy(int count)` | Returns a new buffer with bits removed from the end. | `var trimmed = bs.TrimEndAsCopy(8);` |

---

## **2.6 Reader Creation**

| Signature | Description | Example |
|----------|-------------|---------|
| `BitStorageReader CreateReader()` | Creates a read-only cursor over the bitstream. Safe for concurrent use if each thread creates its own reader. | `var reader = bs.CreateReader();` |
| `BitStorageValueReader<T> CreateValueReader<T>(int bitsPerValue)` | Creates a typed reader starting at bit index **0**. | `var vr = bs.CreateValueReader<int>(5);` |

---

# **3. BitStorageReader — Public API**

A `BitStorageReader` provides **read-only access** to a `BitStorage` with its own independent cursor.

### **Thread‑Safety Note for Readers**

- Readers never modify the underlying storage  
- Each reader maintains its own cursor  
- Readers always observe **live data** (no snapshots)  
- Multiple threads may safely create and use their own readers concurrently  
- Readers themselves should not be shared between threads  

---

## **3.1 Properties**

| Signature | Description | Example |
|----------|-------------|---------|
| `int Count { get; }` | Total number of bits in the underlying storage. | `reader.Count` |
| `int ReadIndex { get; set; }` | Bit index where the next read will occur. | `reader.ReadIndex = 0;` |
| `int LastReadBitCount { get; }` | Number of bits consumed by the last read. | `Console.WriteLine(reader.LastReadBitCount);` |

### **Why `LastReadBitCount` Exists**

`LastReadBitCount` is essential when reading:

- **Partial-width values**  
- **Sequences where the final element may not be full-width**  
- **Insert/remove operations**, which reconstruct bitstreams using the exact number of bits consumed per element  

When reading a sequence:

- **Every element except possibly the last** consumes exactly `bitsPerElement` bits  
- The **final element** may consume fewer bits if the underlying bitstream ends mid-value  

---

## **3.2 Indexers**

| Signature | Description | Example |
|----------|-------------|---------|
| `bool this[Index index] { get; }` | Reads a single bit. | `bool bit = reader[^1];` |
| `bool[] this[Range range] { get; }` | Reads a range of bits. | `var bits = reader[0..16];` |

---

## **3.3 Core Methods**

| Signature | Description | Example |
|----------|-------------|---------|
| `T Read<T>()` | Reads a full-width value of type `T`. | `int x = reader.Read<int>();` |
| `T Read<T>(out int bitsReadCount)` | Reads a full-width value and returns bit count. | `var v = reader.Read<int>(out var n);` |
| `int Read<T>(out T bitsRead, int? bitsToRead = null)` | Reads `bitsToRead` bits into a value of type `T`. | `reader.Read(out byte b, 3);` |
| `IEnumerable<T> ReadEnumerable<T>(int? bitsToRead = null, int? bitsPerElement = null, BitsRead? bitsRead = null)` | Reads a sequence of values. All elements except possibly the last will be `bitsPerElement` bits wide. | `foreach(var x in reader.ReadEnumerable<int>(40, 5)) ...` |
| `BitStorageValueReader<T> CreateValueReader<T>(int bitsPerValue)` | Creates a typed reader starting at the **current `ReadIndex`**. | `var vr = reader.CreateValueReader<int>(6);` |

---

# **4. BitStorageValueReader<T> — Public API**

A typed, fixed-width reader for interpreting the bitstream as a sequence of values.

### **Thread‑Safety Note**

Like `BitStorageReader`, this class:

- Never mutates the underlying storage  
- Maintains its own cursor  
- Always reads **live data**  
- Is safe for multiple threads to use concurrently if each thread creates its own instance  

---

## **4.1 Origin and Indexing Behavior**

A `BitStorageValueReader<T>` can be created in two ways:

### **1. From BitStorage**

```csharp
var vr = storage.CreateValueReader<T>(bitsPerValue);
```

- Starts at **bit index 0**  
- `Count`, `ValueIndex`, and indexers are relative to **0**  

### **2. From BitStorageReader**

```csharp
var vr = reader.CreateValueReader<T>(bitsPerValue);
```

- Starts at the reader’s **current `ReadIndex`**  
- `Count`, `ValueIndex`, and indexers are relative to **that starting bit index**  
- Does not snapshot — reads live data  

---

## **4.2 Properties**

| Signature | Description | Example |
|----------|-------------|---------|
| `int Count { get; }` | Number of values available from the starting bit index. | `vr.Count` |
| `int ValueIndex { get; set; }` | Sequential read position (in values). | `vr.ValueIndex = 0;` |
| `int LastReadBitCount { get; }` | Bits consumed by the last read. | `Console.WriteLine(vr.LastReadBitCount);` |

---

## **4.3 Indexers**

| Signature | Description | Example |
|----------|-------------|---------|
| `T this[int index]` | Reads the value at the given index. | `var x = vr[10];` |
| `T this[Index index]` | Reads from the end. | `var last = vr[^1];` |
| `T[] this[Range range]` | Reads a range of values. | `var arr = vr[5..10];` |

---

## **4.4 Core Methods**

| Signature | Description | Example |
|----------|-------------|---------|
| `int Read(out T value)` | Reads the next value and advances the cursor. | `vr.Read(out var x);` |
| `T Read()` | Reads the next value. | `var x = vr.Read();` |
| `IEnumerable<T> Enumerate(int? valuesToRead = null)` | Enumerates values from the current cursor. | `foreach(var x in vr.Enumerate()) ...` |

---

# **5. Internal Behavior (High-Level Summary)**

### **5.1 Storage Format**

- Bits stored in a dynamically sized **`List<byte>`**  
- Big-endian within each byte  
- `Count` tracks meaningful bits  

### **5.2 Cursor Independence**

- Each reader has its own cursor  
- Readers never modify storage  
- Readers always observe **live data**  
- Writers are not thread-safe  

### **5.3 Partial-Width Values**

Supported for both reading and writing:

- Write only lowest N bits  
- Read only next N bits  
- Partial final values in sequences  

### **5.4 Insert/Remove**

Performed by reconstructing the bitstream:

1. Read prefix  
2. Write inserted bits  
3. Read suffix  

### **5.5 Equality**

`ContentEquals` compares only meaningful bits and ignores unused bits in the final byte.

---

# **6. Summary**

The BitStorage system provides:

- A flexible bit-level buffer  
- Arbitrary-width read/write operations  
- Typed value-level reading  
- Structural editing (insert/remove)  
- Independent cursors  
- Efficient equality  
- **Safe concurrent reading (per-thread readers)**  
- **Non-thread-safe writing**  
- **Value readers that can start at bit 0 or at any reader position**  
- **Accurate bit tracking via `LastReadBitCount`**  
- **Enumerator behavior where all elements are full-width except possibly the last**  
- **Fluent method chaining**  

It is well-suited for encoding, compression, serialization, and any domain requiring precise bit-level control.

---

If you'd like, I can also regenerate the **Quick‑Start Guide** to match this updated API guide, or produce a **Cookbook** of real-world usage patterns.

If you'd like, I can also generate:

- A **quick-start guide**  
- A **diagram of cursor behavior**  
- A **performance tuning guide**  
- A **set of usage patterns** (strings, structs, bitfields, etc.)

Just tell me what direction you want to go next.
If you'd like, I can now produce:

A BitStorage Cookbook with real-world patterns

A cheat sheet for all read/write combinations

A diagram showing cursor movement

A performance guide for high‑volume bit operations

Just tell me what direction you want to explore.