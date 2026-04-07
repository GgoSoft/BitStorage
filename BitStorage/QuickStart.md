# **BitStorage Quick‑Start Guide**

This guide shows how to use the BitStorage system effectively, with explicit examples of what is written, what is read, and why.

---

# **1. Creating a BitStorage**

```csharp
var storage = new BitStorage();
```

You now have an empty bit buffer with:

- `Count = 0`
- `WriteIndex = 0`

---

# **2. Writing Bits and Values**

## **2.1 Writing a single bit**

```csharp
storage.Write(true);   // writes 1
storage.Write(false);  // writes 0
```

---

## **2.2 Writing a value using its full width**

```csharp
storage.Write(42);
```

Since `42` is an **int**, this writes **31 bits**:

```
0000000000000000000000000101010
```

---

## **2.3 Writing only the lowest N bits**

Write only the lowest 3 bits:

```csharp
storage.Write(0b101011, bitsToWrite: 3);
```

Result:

```
011
```

Parameter explanation:

- `bitsToWrite` = number of **lowest** bits to write  

---

## **2.4 Method chaining**

```csharp
storage
    .Write(5, 3)     // 101
    .Write(7, 3)     // 111
    .Write(true);    // 1
```

Final storage:

```
1011111
```

---

# **3. Writing Collections**

## **3.1 Writing a sequence of bytes**

```csharp
storage.Write(new byte[] { 1, 2, 3 });
```

Writes:

```
00000001 00000010 00000011
```

---

## **3.2 Writing only the lowest N bits of each element**

```csharp
storage.Write("Hi", bitsPerElement: 8);
```

Writes:

```
01001000 01101001
```

Parameter explanation:

- `bitsPerElement` = number of bits written **per element**

---

## **3.3 Writing only the first M bits of the entire sequence**

```csharp
storage.Write(new byte[] { 0b10101010, 0b11110000 }, bitsToWrite: 12);
```

Writes:

```
10101010 1111
```

Parameter explanation:

- `bitsToRead` = total number of bits to write from the sequence  

---

# **4. Reading Bits and Values**

Create a reader:

```csharp
var reader = storage.CreateReader();
```

---

## **4.1 Reading a full-width value**

```csharp
int value = reader.Read<int>();  // reads 31 bits
```

---

## **4.2 Reading only N bits**

```csharp
int bitsRead = reader.Read(out byte b, bitsToRead: 3);
```

- `b` receives the 3 bits  
- `bitsRead` returns **3**

---

## **4.3 Using `Read` in a loop**

```csharp
while (reader.Read(out byte value, 3) > 0)
{
    Console.WriteLine($"Read 3-bit value: {value}");
}
```

Explanation:

- `Read(...)` returns the **actual number of bits read**  
- When no bits remain, it returns **0**  
- This makes the loop naturally stop at end‑of‑stream  

---

## **4.4 Why `LastReadBitCount` exists**

- Tracks exactly how many bits were consumed  
- Final element in a sequence may be **partial-width**  
- Useful for reconstructing or aligning bitstreams  

---

# **5. Reading Sequences**

```csharp
foreach (var x in reader.ReadEnumerable<int>(bitsToRead: 40, bitsPerElement: 5))
{
    Console.WriteLine($"Value: {x}, BitsRead: {reader.LastReadBitCount}");
}
```

Reads:

- 5 bits per element  
- Up to 40 bits total  
- Final element may be partial  

---

# **6. Typed Value Readers**

## **6.1 Creating from BitStorage (starts at bit 0)**

```csharp
var vr = storage.CreateValueReader<int>(6);
```

---

## **6.2 Creating from BitStorageReader (starts at current ReadIndex)**

```csharp
reader.ReadIndex = 10;
var vr = reader.CreateValueReader<int>(6);
```

- `vr[0]` always refers to the value starting at the reader’s starting bit index  
- `vr.Read()` advances the underlying reader  

---

## **6.3 Sequential reading**

```csharp
int value = vr.Read();  // reads the next 6 bits as an int
```

---

## **6.4 Random access**

```csharp
int third = vr[2];  // reads the 3rd 6-bit value from the starting bit index
```

---

## **6.5 Range access**

```csharp
int[] values = vr[5..10];
```

Reads:

- Values 5, 6, 7, 8, and 9  
- Each value is `bitsPerValue` bits wide (in this example) 
- All values are relative to the starting bit index  

---

# **7. Inserting and Removing Bits**

## **7.1 Insert a value**

Suppose storage contains:

```
1011001011
```

Insert 4 bits (`1100`) at index 4:

```csharp
storage.Insert(4, 0b1100, bitsToWrite: 4);
```

Result:

```
1011 1100 001011
```

---

## **7.2 Insert another BitStorage**

```csharp
var header = new BitStorage().Write(0b111000, 6);
storage.Insert(0, header);
```

Result:

```
111000 <original bits>
```

---

## **7.3 Remove a range**

```csharp
storage.RemoveRange(index: 3, count: 5);
```

Removes bits in the range:

```
[3..8)
```

And closes the gap.

---

## **7.4 Trim from the end**

```csharp
storage.TrimEnd(3);
```

Removes the last 3 bits.

---

# **8. Getting the Underlying Bytes**

```csharp
List<byte> bytes = storage.GetData();
```

- Returns a `List<byte>`  
- Masks unused bits in the final byte  

---

# **9. Thread‑Safety Basics**

### **BitStorage**
- Not thread‑safe  
- Must not be mutated concurrently  
- Can be read by multiple threads if each creates its own reader  

### **Readers**
- Never modify storage  
- Safe for concurrent use  
- Always read live data  
- Should not be shared between threads  

---

# **10. Minimal End‑to‑End Example**

```csharp
var storage = new BitStorage();

storage
    .Write(5, 3)     // 101
    .Write(7, 3)     // 111
    .Write(true);    // 1

var reader = storage.CreateReader();

reader.Read(out int a, 3);   // 5
reader.Read(out int b, 3);   // 7
reader.Read(out bool c, 1);  // true

Console.WriteLine($"{a}, {b}, {c}");
```

Output:

```
5, 7, True
```

---

If you'd like, I can now:

- Regenerate the **Public API Guide** to match this  
- Produce a **BitStorage Cookbook**  
- Create a **diagram of cursor behavior**  
- Write a **performance tuning guide**

Just tell me where you want to go next.