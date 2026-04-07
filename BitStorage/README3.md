# BitStorage & BitStorageReader — In‑Depth Guide

## Overview

`BitStorage` is a high‑performance, bit‑level container designed for scenarios where you need:

- Compact, bit‑precise storage  
- Arbitrary write operations at any time  
- Arbitrary read operations at any time (non‑concurrently)  
- Able to read/write any integer numeric or bool data type at any time  
- Efficient insertion and removal of bit ranges  
- Random‑access bit reads  
- Sequential cursor‑based reads  
- A clean, canonical snapshot of the stored bits  

Unlike typical byte‑oriented buffers, `BitStorage` treats the underlying data as a **continuous stream of bits**, not bytes. This makes it ideal for:

- Custom codecs  
- Serialization formats  
- Compression schemes  
- Bit‑packed data structures  
- Game engines and retro‑style encodings  
- Network protocols with non‑byte‑aligned fields  

`BitStorageReader` provides a **cursor‑based, read‑only view** into the same bitstream, allowing you to read values sequentially without interfering with writes.

---

# Architecture

|    BitStorage     |   BitStorageReader    | 
|-------------------|-----------------------| 
| Owns the bytes    | Owns read cursor      | 
| Owns write cursor | Reads bits sequentially| 
| Mutates data      | Random-access reads   | 
| Insert/remove     | No mutation           | 

### Key principles

- **BitStorage is mutable**  
  You can write, insert, remove, and trim bits at any time.

- **BitStorageReader is read‑only**  
  It never mutates the underlying storage.

- **Readers always read from the live storage**  
  They are not snapshots; they reflect the current state.

- **Snapshots are produced explicitly**  
  `GetData()` returns a clean, masked, canonical byte array.

---

# Bit Layout

Bits are stored left to right within each byte.  If a bits '101'
are written, it will be in the ***MSB***:

(NOTE: Spaces are used in boolean numbers for visibility only)

```text
101 00000
```

If '101' is written again:

```text
101101 00
```

And if it's written again:

```text
10110110
1 0000000
```
Bit index `0` corresponds to the highest order bit of the first byte.

---

# Quick Examples

## Writing bits

```c#
var storage = new BitStorage();

// Write a single bit
storage.Write(true);          // writes 1

// Write 3 bits from the value 5 (0b101)
storage.Write(5, 3);          // writes 101

// Write 2 bits from a byte
storage.Write<byte>(0b1100_0000, 2); // writes 11

```

Resulting bitstream (conceptually):

```
1 101 11
```

## Reading bits sequentially

```c#
var reader = storage.CreateReader();

bool first = reader.Read<bool>();          // reads 1 bit
int value = reader.Read<int>(out int n);   // reads up to 31 bits (or fewer if not enough remain)
```

## Random-access reads

```c#
var reader = storage.CreateReader();

bool bit5 = reader[5];          // read a single bit at index 5
bool[] slice = reader[3..8];    // read bits 3 through 7 as a bool[]
```

## Inserting bits

```c#
// Insert 3 bits (0b101) at bit index 3
storage.Insert(3, 0b101, 3);
```

## Removing bits

```c#
// Remove 4 bits starting at bit index 5
storage.RemoveRange(5, 4);
```

## Getting a clean snapshot

```c#
// Returns only the bytes that contain data bits,
// masking off unused bits in the last byte
List<byte> bytes = storage.GetData();
```

---

# BitStorage API Reference

## Puropose

`BitStorage` is the mutable, bit‑level backing store. It owns:

* The underlying byte collection
* The write cursor (`WriteIndex`)
* The total bit count (`Count`)
* All mutation operations (write, insert, remove, trim)

| Member | Description | Usage example |
| --- | --- | --- |
| `BitStorage()` | Creates an empty bit storage. | `var storage = new BitStorage();` |
| `BitStorage(BitStorage bits)` | Copy constructor; writes all bits from another instance. | `var copy = new BitStorage(existing);` |
| `static BitStorage Create(IEnumerable bits, int? bitsToWrite = null, int? elementBitsToWrite = null)` | Creates a new `BitStorage` and writes bits from a sequence. | `var s = BitStorage.Create(new byte[]{1,2,3});` |
| `BitStorage Write(BitStorage bits)` | Appends all bits from another `BitStorage`. | `storage.Write(other);` |
| `BitStorage Write(T bits, int? bitsToWrite = null) where T : struct` | Writes up to `bitsToWrite` bits from a numeric value (or full width if null). | `storage.Write(5, 3);` |
| `BitStorage Write(IEnumerable bits, int? bitsToWrite = null, int? elementBitsToWrite = null) where T : struct` | Writes bits from a sequence of values, optionally limiting total bits and per‑element bits. | `storage.Write(new byte[]{1,2,3}, 20);` |
| `BitStorage Insert(int index, BitStorage bits)` | Inserts another `BitStorage` at a bit index, mutating the current instance. | `storage.Insert(10, other);` |
| `BitStorage Insert(int index, T bits, int? bitsToWrite = null) where T : struct` | Inserts bits from a value at a bit index. | `storage.Insert(5, 0b101, 3);` |
| `BitStorage InsertAsCopy(int index, BitStorage bits)` | Returns a new `BitStorage` with `bits` inserted at `index`; original is unchanged. | `var copy = storage.InsertAsCopy(5, other);` |
| `BitStorage InsertAsCopy(int index, T bits, int? bitsToWrite = null) where T : struct` | Returns a new `BitStorage` with bits from `bits` inserted at `index`. | `var copy = storage.InsertAsCopy(5, 0b101, 3);` |
| `BitStorage RemoveRange(int index, int count)` | Removes `count` bits starting at `index`, mutating the current instance. | `storage.RemoveRange(3, 5);` |
| `BitStorage RemoveRangeAsCopy(int index, int count)` | Returns a new `BitStorage` with the specified range removed. | `var copy = storage.RemoveRangeAsCopy(3, 5);` |
| `BitStorage TrimEnd(int count)` | Removes `count` bits from the end, mutating the instance. | `storage.TrimEnd(8);` |
| `BitStorage TrimEndAsCopy(int count)` | Returns a new `BitStorage` with `count` bits removed from the end. | `var copy = storage.TrimEndAsCopy(8);` |
| `List<byte> GetData()</byte>` | Returns a new `List<byte></byte>` containing only the bytes that hold data bits; unused bits in the last byte are masked off. | `var bytes = storage.GetData();` |
| `BitStorageReader CreateReader()` | Creates a new `BitStorageReader` bound to this storage. | `var reader = storage.CreateReader();` |
| `bool[] this[Range range]` (setter) | Sets a range of bits from a bool[]`. Length must match the range length. | `storage[3..7] = new[]{true,false,true,false};` |
| `bool this[Index index]` (setter) | Sets a single bit at the given index. Supports `^1` from‑end indexing. | `storage[^1] = true;` |
| `int Count { get; }` | Total number of bits stored. | `int bits = storage.Count;` |
| `int WriteIndex { get; set; }` | Current write cursor in bits. Setting it moves the write position (0..Count). | `storage.WriteIndex = 0;` |
| `void Clear()` | Clears all bits and resets indices. | `storage.Clear();` |
| `bool ContentEquals(BitStorage? other)` | Compares bit content (ignores unused bits in last byte). | `bool same = storage.ContentEquals(other);` |

---

## BitStorageReader API

### Purpose

`BitStorageReader` is a read‑only, cursor‑based view over a `BitStorage` instance. It:\
* Tracks its own read cursor (ReadIndex)
* Reads bits sequentially or via random access
* Never mutates the underlying storage
* Can be recreated at any time to start from a fresh position

| Member | Description | Usage example |
|--------|-------------|----------------|
| `internal BitStorageReader(BitStorage bitStorage)` | Constructs a reader bound to a specific `BitStorage`. Use `CreateReader()` instead of calling this directly. | `var reader = storage.CreateReader();` |
| `int Count { get; }` | Total number of bits available in the underlying storage. | `int bits = reader.Count;` |
| `int ReadIndex { get; set; }` | Current read cursor in bits (0..Count). | `reader.ReadIndex = 0;` |
| `int LastReadBitCount { get; }` | Number of bits read by the most recent read operation. | `int n = reader.LastReadBitCount;` |
| `T Read(out int bitsRead) where T : struct` | Reads a value of type `T` using its full bit width (or remaining bits), returns the value and the number of bits consumed. | `int value = reader.Read(out int bitsUsed);` |
| `T Read() where T : struct` | Reads a value of type `T` and discards the bit count. | `bool flag = reader.Read();` |
| `int Read(out T bitsRead, int? bitsToRead = null) where T : struct` | Reads up to `bitsToRead` bits into `bitsRead`. If `bitsToRead` is null, uses the full width of `T`. Returns the actual number of bits read. | `reader.Read(out byte b, 5);` |
| `IEnumerable ReadEnumerable(int? bitsToRead = null, BitsRead? bitsRead = null) where T : struct` | Reads a sequence of values of type `T` until `bitsToRead` bits are consumed (or until no bits remain). Optionally updates a `BitsRead` object with the bit count of the last element. | `foreach (var v in reader.ReadEnumerable(32)) { ... }` |
| `bool[] this[Range range]` (getter) | Reads a range of bits as a `bool[]`. | `bool[] slice = reader[5..12];` |
| `bool this[Index index]` (getter) | Reads a single bit at the given index. | `bool bit = reader[10];` |

---

# Typical workflow

## 1. Build a bitstream

```c#
var storage = new BitStorage();

storage.Write(true);          // 1
storage.Write(0b1011, 4);     // 1011
storage.Write(3, 2);          // 11

// Bitstream: 1 1011 11 (spaces for visibility)
```

## 2. Read from it

```c#
var reader = storage.CreateReader();

bool first = reader.Read<bool>();          // 1
int next = reader.Read<int>(out int bitsRead); // reads as many bits as available (up to 31)

// first: true
// next: 47 (101111)
// bitsRead: 6
```

## 3. Insert and remove

```c#
// Insert 3 bits (0b010) at index 2
storage.Insert(2, 0b010, 3);
// storage: 11 010 01111 (spaces for visibility)

// Remove 5 bits starting at index 4
storage.RemoveRange(4, 5);
// storage: 1101 1 (spaces where the bits were removed)

```

## 4. Export a clean snapshot

```c#
List<byte> bytes = storage.GetData();
int count = storage.Count
// bytes contains only the bytes that hold data bits,
// with unused bits in the last byte masked off.

//bytes: [216] (11011 000 -- space deliniating used/unused bits)
//count: 5
```

---

# Design rationale
* Separation of concerns  
  `BitStorage` mutates; `BitStorageReader` observes. This keeps responsibilities clear and avoids accidental writes during reads.
* Live reading vs snapshots  
  Readers always see the current state of the storage. If you need a stable snapshot, you call `GetData()`.
* Canonical snapshots  
  `GetData()` masks unused bits in the last byte, ensuring that exported data is canonical and suitable for hashing, comparison, or persistence.
* Bit‑level precision  
  All operations are defined in terms of bit indices, not bytes, which makes the API expressive for protocols and encodings that are not byte‑aligned.


# Conclusion

`BitStorage` and `BitStorageReader` form a powerful, flexible, and efficient bit‑level storage system. The architecture cleanly separates mutation from traversal, supports arbitrary read/write interleaving, and provides both random‑access and cursor‑based reading.



If you want, I can also generate:
a version with comments explaining why each Markdown escape is used
a shorter “quick reference” version
or a version formatted specifically for GitHub, GitLab, or a documentation generator like DocFX

Just tell me what direction you want to take this.
