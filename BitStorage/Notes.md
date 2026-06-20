I wonder about making a serializer that can take an object and create a bit storage out of it.  It could require attributes before the fields that are going to be serialized and these attributes would specify how the data would be written
e.g.
```c#
[BitStorageSerializer(BitsToWrite: 8)]
public int Something {get;set;} // this would be serialized as the first 8 bits
```
there could be an optional order as well with the default order being whatever order the properties are read

---

You’re thinking in a direction that a *lot* of binary‑format designers eventually arrive at:  
a **declarative, attribute‑driven bit‑serializer** that turns an object into a `BitStorage` based on metadata.

And the good news is:  
**your BitStorage system is already the perfect foundation for this.**

Let me walk you through a design that is:

- idiomatic C#
- flexible
- predictable
- easy to extend
- fully compatible with your existing BitStorage API

I’ll also show you how ordering, nested objects, enums, arrays, and custom serializers can fit in.

---

# **1. The Attribute**

A simple, expressive attribute might look like this:

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BitStorageSerializerAttribute : Attribute
{
    public int BitsToWrite { get; }
    public int Order { get; set; } = int.MaxValue;

    public BitStorageSerializerAttribute(int bitsToWrite)
    {
        BitsToWrite = bitsToWrite;
    }
}
```

Usage:

```csharp
[BitStorageSerializer(8, Order = 0)]
public int Something { get; set; }
```

---

# **2. Basic Serialization Model**

You want something like:

```csharp
var storage = BitStorageSerializer.Serialize(myObject);
```

And internally:

1. Reflect over fields/properties  
2. Filter to those with the attribute  
3. Sort by `Order` (default = declaration order)  
4. Write each value using your existing `Write<T>(T, bitsToWrite)`  

---

# **3. Declaration Order as Default Order**

C# reflection does **not** guarantee declaration order, but you can retrieve it reliably:

- For fields: `Type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)` returns declaration order.
- For properties: you must read metadata tokens.

A helper:

```csharp
static int GetDeclarationOrder(MemberInfo member)
    => member.MetadataToken;
```

Then:

- If `Order` is set → use it  
- Else → use declaration order  

---

# **4. The Serializer**

Here’s a clean, extensible version:

```csharp
public static class BitStorageSerializer
{
    public static BitStorage Serialize<T>(T obj)
    {
        var storage = new BitStorage();
        SerializeInto(storage, obj);
        return storage;
    }

    public static void SerializeInto<T>(BitStorage storage, T obj)
    {
        var members = typeof(T)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<BitStorageSerializerAttribute>() != null)
            .Select(m => new
            {
                Member = m,
                Attr = m.GetCustomAttribute<BitStorageSerializerAttribute>(),
                Order = m.GetCustomAttribute<BitStorageSerializerAttribute>().Order == int.MaxValue
                    ? m.MetadataToken
                    : m.GetCustomAttribute<BitStorageSerializerAttribute>().Order
            })
            .OrderBy(x => x.Order);

        foreach (var entry in members)
        {
            object value = entry.Member switch
            {
                PropertyInfo p => p.GetValue(obj),
                FieldInfo f => f.GetValue(obj),
                _ => throw new InvalidOperationException("Unsupported member type")
            };

            WriteValue(storage, value, entry.Attr.BitsToWrite);
        }
    }

    private static void WriteValue(BitStorage storage, object value, int bits)
    {
        switch (value)
        {
            case bool b:
                storage.Write(b);
                break;

            case byte or sbyte or short or ushort or int or uint or long or ulong:
                storage.Write(Convert.ToUInt64(value), bits);
                break;

            case Enum e:
                storage.Write(Convert.ToUInt64(e), bits);
                break;

            default:
                throw new NotSupportedException($"Unsupported type: {value.GetType()}");
        }
    }
}
```

This gives you:

- Primitive support  
- Enum support  
- Order control  
- Bit‑width control  
- Extensibility  

---

# **5. Example Usage**

```csharp
public class Packet
{
    [BitStorageSerializer(3, Order = 0)]
    public int Version { get; set; }

    [BitStorageSerializer(5, Order = 1)]
    public int Type { get; set; }

    [BitStorageSerializer(16, Order = 2)]
    public int Length { get; set; }

    [BitStorageSerializer(1, Order = 3)]
    public bool IsCompressed { get; set; }
}
```

Serialize:

```csharp
var packet = new Packet
{
    Version = 5,
    Type = 12,
    Length = 1024,
    IsCompressed = true
};

BitStorage storage = BitStorageSerializer.Serialize(packet);
```

Resulting bitstream:

```
Version (3 bits)
Type (5 bits)
Length (16 bits)
IsCompressed (1 bit)
```

---

# **6. Optional Enhancements**

## **6.1 Nested Objects**

Allow:

```csharp
[BitStorageSerializer(0)] // BitsToWrite ignored for nested types
public Header Header { get; set; }
```

Implementation:

- Detect non‑primitive types  
- Recursively call `SerializeInto`  

## **6.2 Arrays / Lists**

Allow:

```csharp
[BitStorageSerializer(6)]
public int[] TileIds { get; set; }
```

Serializer:

- Detect `IEnumerable<T>`  
- Write each element using the same bit width  

## **6.3 Custom Serializers**

Allow:

```csharp
[BitStorageCustomSerializer(typeof(MySerializer))]
public MyType Value { get; set; }
```

Where `MySerializer` implements:

```csharp
public interface IBitStorageCustomSerializer
{
    void Write(BitStorage storage, object value);
}
```

## **6.4 Deserialization**

You can mirror the same logic:

```csharp
public static T Deserialize<T>(BitStorage storage)
```

Using:

- `Read<T>(bitsToRead)`  
- `ReadEnumerable<T>`  
- `ValueReader<T>` for fixed‑width sequences  

---

# **7. Why This Design Works Well**

- **Declarative**: The object describes how it should be serialized  
- **Predictable**: Order is explicit or declaration‑based  
- **Extensible**: Nested types, arrays, enums, custom serializers  
- **Compatible**: Uses your existing BitStorage API  
- **Efficient**: No reflection during writing except initial metadata scan  
- **Flexible**: Supports partial‑width values, bit‑packed structures, and arbitrary layouts  

This is exactly the kind of system used in:

- network protocols  
- embedded systems  
- compression formats  
- game engines  
- binary file formats  

And BitStorage is already the perfect engine for it.

---

If you want, I can now generate:

- A **Deserializer** design  
- A **full working implementation** of the serializer/deserializer pair  
- A **more advanced attribute system** (e.g., bit‑endian, signed/unsigned, alignment, padding)  
- A **Cookbook section** showing how to serialize real‑world structures (PNG chunks, TCP headers, etc.)  

Just tell me where you want to take this.

# Attribute Field Notes
## BitField

- Bits
    - Specifies how many bits to write for this field. This is the core of the bit storage serializer, allowing you to control the exact bit width of each field.
    - Cannot be used with InferBits, as they serve different purposes.
    - If used on an enumerable, this is the underlying element width, not the count width. Count width is specified separately with CountBitLength.
- Signed
    - Specifies if the value should be treated as signed or unsigned when writing to the bit storage. This is important for correctly interpreting the bits, especially for negative numbers.
    - If true, high bit will be signed bit. If false, all bits are value bits.
    - If true, cannot be used with UnsignedMin/UnsignedMax
    - If false, cannot have a min < 0
    - Cannot be true if data type is unsigned (e.g., byte, ushort, uint, ulong)
    - If used on an enumerable, this refers to the underlying element.
- Min
    - Specifies the minimum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be negative if Signed is false.
    - Cannot be used with UnsignedMin, as they serve different purposes.
    - Must be less than either Max or UnsignedMax
    - If used on an enumerable, this refers to the underlying element.
- Max
    - Specifies the maximum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be negative if Signed is false.
    - Cannot be used with UnsignedMax, as they serve different purposes.
    - Must be greater than either Min or UnsignedMin
    - If used on an enumerable, this refers to the underlying element.
- UnsignedMin
    - Specifies the minimum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be used with Min, as they serve different purposes.
    - Must be less than either Max or UnsignedMax
    - If used on an enumerable, this refers to the underlying element.
- UnsignedMax
    - Specifies the maximum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be used with Max, as they serve different purposes.
    - Must be greater than either Min or UnsignedMin
    - If used on an enumerable, this refers to the underlying element.
- InferBits
    - Specifies whether the number of bits for this field should be inferred from either the min/max or data type range.
    - Cannot be used with Bits, as they serve different purposes.
    - If used on an enumerable, this refers to the underlying element.
- ConditionalProperty
    - Optional name of another property on the same object used for simple conditional inclusion.
    - If this property is null, false, or empty, the current field will be excluded.
    - If the property is excluded in serialization, it also must be excluded in deserialization.
    - If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionalType
    - Condition type implementing IFieldCondition
    - Field will be written based on IFieldCondition.Evaluate
    - Can be direct injected into the serializer, so can have constructor parameters for configuration
    - Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
    - May be reused across multiple fields, so should be designed to be reusable and stateless if possible
    - If the property is excluded in serialization, it also must be excluded in deserialization.
    - If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionCombine
    - How multiple conditional checks are combined when both ConditionalProperty and ConditionalType
		(or multiple conditions) are present.
	- Defaults to ConditionCombine.And
- ConditionMode
    - The evaluation mode for conditions: snapshot (evaluate against a snapshot of current values)
		or incremental (evaluate as values are processed)
- ConverterType
    - Optional converter type used to transform between the CLR property and the bit-level representation.
    - Converter will fully be responsible for read/write of field
    - Must implement IBitConverter
    - Can be direct injected into the serializer, so can have constructor parameters for configuration
    - Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
    - May be reused across multiple fields, so should be designed to be reusable and stateless if possible
- ConverterMethod
    - Optional method name on the converter type used to transform between the CLR property and the bit-level representation.
- OmitIfEquals
    - Optional value that, if the field equals this value, it will be omitted from serialization.
    - This requires the "Optional" property to be true, as the presence bit will indicate whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- Order
    - Specifies the order in which fields are serialized. Fields with lower order values are serialized first. If not specified, fields will be serialized in the order they are declared in the class.
- Description
- AllowNonPublicAccess
  - When true, the builder may use non-public accessors for this property.



## BitFieldLevel

- Depth
    - The depth of the field in the object graph, with 0 being the root level.
- Optional
    - When true, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- IgnoreIfNull
    - When true, if the field value is null, it will be omitted from serialization. This is useful for reference types or nullable value types where a null value indicates the absence of data.
    - If "Optional" is also true, the presence bit will indicate whether the field is included or not, regardless of whether it's null or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- CountBitLength
    - When serializing enumerables, the number of bits used to encode the element count
    - Can only be used with enumerations
    - Cannot be used with TerminatorValue, as they serve different purposes.
- CountField
    - Optional name of another property on the same object used to store the element count for enumerables.
	- If specified, this property will be used to read/write the count of elements in the enumerable instead of using a fixed bit length defined by CountBitLength. This allows for more flexible serialization of collections where the count may not fit within a predetermined number of bits.
- TerminatorValue
    - When serializing enumerables with a terminator, the terminator element value (encoded using the element width).
    - Can only be used with enumerations
    - Cannot be used with CountBitLength, as they serve different purposes.
- ConditionalProperty
    - Optional name of another property on the same object used for simple conditional inclusion.
    - If this property is null, false, or empty, the current field will be excluded.
    - If the property is excluded in serialization, it also must be excluded in deserialization.
    - If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionalType
    - Condition type implementing IFieldCondition
    - Field will be written based on IFieldCondition.Evaluate
    - Can be direct injected into the serializer, so can have constructor parameters for configuration
    - Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
    - May be reused across multiple fields, so should be designed to be reusable and stateless if possible
    - If the property is excluded in serialization, it also must be excluded in deserialization.
    - If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.


# Updated Attributes


## BitField
- Bits
    - Specifies how many bits to write for this field. This is the core of the bit storage serializer, allowing you to control the exact bit width of each field.
    - Cannot be used with InferBits, as they serve different purposes.
    - If used on an enumerable, this is the underlying element width, not the count width. Count width is specified separately with CountBitLength.
- Signed
    - Specifies if the value should be treated as signed or unsigned when writing to the bit storage. This is important for correctly interpreting the bits, especially for negative numbers.
    - If true, high bit will be signed bit. If false, all bits are value bits.
    - If true, cannot be used with UnsignedMin/UnsignedMax
    - If false, cannot have a min < 0
    - Cannot be true if data type is unsigned (e.g., byte, ushort, uint, ulong)
    - If used on an enumerable, this refers to the underlying element.
- Min
    - Specifies the minimum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be negative if Signed is false.
    - Cannot be used with UnsignedMin, as they serve different purposes.
    - Must be less than either Max or UnsignedMax
    - If used on an enumerable, this refers to the underlying element.
- Max
    - Specifies the maximum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
    - Cannot be negative if Signed is false.
    - Cannot be used with UnsignedMax, as they serve different purposes.
    - Must be greater than either Min or UnsignedMin
    - If used on an enumerable, this refers to the underlying element.
    - If used on an enumerable, this refers to the underlying element.
- UnsignedMin
	- Specifies the minimum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
	- Cannot be used with Min, as they serve different purposes.
	- Must be less than either Max or UnsignedMax
	- If used on an enumerable, this refers to the underlying element.
- UnsignedMax
	- Specifies the maximum value for a field. This can be used for validation or to optimize the number of bits needed to store the value.
	- Cannot be used with Max, as they serve different purposes.
	- Must be greater than either Min or UnsignedMin
	- If used on an enumerable, this refers to the underlying element.
- InferBits
	- Specifies whether the number of bits for this field should be inferred from either the min/max or data type range.
	- Cannot be used with Bits, as they serve different purposes.
	- If used on an enumerable, this refers to the underlying element.
- Optional
	- When true, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionalProperty
	- Optional name of another property on the same object used for simple conditional inclusion.
	- If this property is null, false, or empty, the current field will be excluded.
	- If the property is excluded in serialization, it also must be excluded in deserialization.
	- If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionalType
	- Condition type implementing IFieldCondition
	- Field will be written based on IFieldCondition.Evaluate
	- Can be direct injected into the serializer, so can have constructor parameters for configuration
	- Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
	- May be reused across multiple fields, so should be designed to be reusable and stateless if possible
	- If the property is excluded in serialization, it also must be excluded in deserialization.
	- If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionCombine
	- How multiple conditional checks are combined when both ConditionalProperty and ConditionalType (or multiple conditions) are present.
	- Defaults to ConditionCombine.And
- ConditionMode
	- The evaluation mode for conditions: snapshot (evaluate against a snapshot of current values) or incremental (evaluate as values are processed)
- ConverterType
	- Optional converter type used to transform between the CLR property and the bit-level representation.
	- Converter will fully be responsible for read/write of field
	- Must implement IBitConverter
	- Can be direct injected into the serializer, so can have constructor parameters for configuration
	- Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
	- May be reused across multiple fields, so should be designed to be reusable and stateless if possible
- ConverterMethod
	- Optional method name inside the current class used to transform between the CLR property and the bit-level representation.
	- This method will be called directly when the field is serialized/deserialized, allowing the use of private values and full internal encapsulation within the class.
- OmitIfEquals
	- Optional value that, if the field equals this value, it will be omitted from serialization.
	- This requires the "Optional" property to be true, as the presence bit will indicate whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- Order
	- Specifies the order in which fields are serialized. Fields with lower order values are serialized first. If not specified, fields will be serialized in the order they are declared in the class.
- Description
- AllowNonPublicAccess
	- When true, the builder may use non-public accessors for this property.

## BitFieldLevel
- Depth
    - The depth of the field in the object graph, with 0 being the root level (outermost collection).
- Optional
    - When true, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- IgnoreIfNull
	- When true, if the field value is null, it will be omitted from serialization. This is useful for reference types or nullable value types where a null value indicates the absence of data.
	- If "Optional" is also true, the presence bit will indicate whether the field is included or not, regardless of whether it's null or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- CountBitLength
	- When serializing enumerables, the number of bits used to encode the element count
	- Can only be used with enumerations
	- Cannot be used with TerminatorValue, as they serve different purposes.
- CountField
	- Optional name of another property on the parent object used to store the element count for enumerables.
	- If specified, this property will be used to read/write the count of elements in the enumerable instead of using a fixed bit length defined by CountBitLength. This allows for more flexible serialization of collections where the count may not fit within a predetermined number of bits.
	- Evaluates strictly on the current context class containing the collection field declaration; it will not look inside target complex elements.
- TerminatorValue
	- When serializing enumerables with a terminator, the terminator element value (encoded using the element width).
	- Can only be used with enumerations
	- Cannot be used with CountBitLength, as they serve different purposes.
- ConditionalProperty
	- Optional name of another property on the same object used for simple conditional inclusion.
	- If this property is null, false, or empty, the current field will be excluded.
	- If the property is excluded in serialization, it also must be excluded in deserialization.
	- If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
- ConditionalType
	- Condition type implementing IFieldCondition
	- Field will be written based on IFieldCondition.Evaluate
	- Can be direct injected into the serializer, so can have constructor parameters for configuration
	- Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
	- May be reused across multiple fields, so should be designed to be reusable and stateless if possible
	- If the property is excluded in serialization, it also must be excluded in deserialization.
	- If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
