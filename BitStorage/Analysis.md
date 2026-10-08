# General
> 9/9/2026

- The class to be serialized needs to implement "IBitSerializable".  This
is empty and is only a tag specifying that it's serializable
- Each field should have something to specify that it's serializable. 
Currently, that's BitField, but BitFieldSerializable may be better, will
have to figure that out.
- If the field is an enumeration (e.g. List\<List\<int\>\>), each level of
the enumeration can have a BitLevel attribute with a "Depth" argument, so 
in this example, there can be 2 BitLevel attributes, the first would be
Depth=0, the 2nd would be Depth=1, but they are positional, so the "Depth"
argument isn't required unless you want to skip levels (e.g. just specify
the 1st and 3rd and have the 2nd as default).
- Each enumerable level can have a CountBitLength (specifying that the count
of objects should precede the objects, and how many bits to hold the count)
- Each enumerable level can have Terminator and Escape values.  These are
mutually exclusive of CountBitLength and CountProperty.
- ~~Each enumerable level can have a CountProperty, this should probably be
SerializeCountProperty and DeserializeCountProperty as well as adding
SerializeCount and DeserializeCount as methods.  This allows the count
of each level to be calculated on the fly.  These are mutually exclusive
of CountBitLength and Terminator/Escape~~ Removed since ShouldContinue
can be used instead of DeserializeCount and ShouldSerialize can be used
instead of SerializeCount.  It may be possible to add these back in later
- Each enumerable level can have a "ShouldContinue", but that should be
renamed to "DeserializeContinue" or something similar.  This allows the
enumeration to be able to stop deserializing based on any external data.
There is no "SerializeContinue" currently as this can be done with the
ShouldSerialize. (UPDATED 10/7/2026: These should be "ShouldSerialize" and
"ShouldDeserialize")
- All fields will have a bottom level (element/node/whatever).  This is
specified with the BitElement attribute. In the above example, this would 
be the "int" part, but it can also be an object that implements 
IBitSerializable.  If the field is not an enumerable, the field itself is 
the bottom (e.g. "int MyProperty {get;set;}" can only have a BitElement
but no BitLevel)
- The BitElement (as long as it's a primitive and not an IBitSerializable
type) can have Min/Max, Signed (specifying the primitive is signed or 
unsigned, signed=true can only be used with signed values -- signed=true 
is used for min/max validating)
- All levels (BitLevel and BitELement) can have ShouldSerialize and
ShouldDeserialize methods.  If it's an enumeration, these will be run for
each element.  The value of the current thing (element/primitive/whatever) 
is sent to the method along with some other data.  This allows the method to
evaluate the element being written and decide if it should be serialize. In 
the case of deserialization, the reader is sent to the method and the method
can determine by peeking in the reader or other calculations if there's data
to read for the field/element.

# Levels
### Both **BitLevel** and **BitElement**
  - **ShouldSerialize**
      - string value as method name
      - Parameters: 
          - BitStorage writer
          - object target
          - PropertyInfo propInfo
          - int depth
          - int currentCount
      - the current item value is the value of the current element being 
        serialized, or the value of the field if it's a BitElement.
		For depth=0, this is the value of the field, for depth>0, this is the 
		value of the current element being serialized
      - If it's a BitElement, the depth and current count are always 0, since 
        it's the bottom level
  - **ShouldDeserialize**
      - string value as method name
      - parameters: 
          - BitStorageReader reader
          - PropertyInfo propInfo
          - int depth
          - int currentCount
      - If it's a BitElement, the depth and current count are always 0, since 
        it's the bottom level
  - **Bits**
	  - Specifies how many bits to use for the field/element
	  - If it's a BitElement, this is required, but if it's a BitLevel, this is 
		optional and will be inferred from the type of the field (e.g. int=32, 
		long=64, etc)
  -- **DeserializeConverter**
  -- **SerializeConverter**
### **BitLevel** 
The enumerable levels, e.g. List\<List\<int\>\> has 2 BitLevels
  - **Depth**
      - Zero based
      - If it's not specified, the depth is assumed to be the next available depth 
        (e.g. if there are 2 BitLevels, the first is Depth=0 and the second is Depth=1)
      - There can only be 1 BitLevel per depth (inferred or specified).  If there are
        multiple BitLevels with the same depth, an exception will be thrown
  - **CountBitLength**
      - Specifies that the count of objects should precede the objects, and how many 
        bits to hold the count
      ~~- Mutually exclusive of Terminator and Escape~~
  - 10/7/2026 - Terminator, TerminatorBits and Escape removed
	  - these can be handled with ShouldSerialize, ShouldDeserialize, StartEnumerable, and EndEnumerable
	  - ~~- **Terminator**~~
      ~~- Specifies a value that will terminate the enumeration, and the value of the~~
		~~terminator~~
      ~~- Mutually exclusive of CountBitLength~~
  ~~- **TerminatorBits**~~
      ~~- Specifies how many bits to use for the Terminator value~~
      ~~- Required if using Terminator~~
      ~~- Mutually exclusive of CountBitLength~~
  ~~- **Escape**~~
      ~~- Optional value that will escape the next value, and the value of the escape~~
      ~~- Requires Terminator~~
      ~~- Mutually exclusive of CountBitLength~~
      ~~- If the Escape or Terminator values are found in the data, the Escape value~~
        ~~will be written first, then either the Escape value or the Terminator value~~
		~~will be written~~
      ~~- If this value is not given and the data contains the Terminator value,~~
        ~~an exception will be thrown~~
      ~~- Number of bits for Escape is the same as TerminatorBits~~
  - **StartSerializeEnumeration**
      - string value as method name
      - parameters: 
          - BitStorage writer
          - object target
          - PropertyInfo propInfo
          - int depth
  - **EndSerializeEnumeration**
      - string value as method name
      - parameters: 
          - BitStorage writer
          - object target
          - PropertyInfo propInfo
          - int depth
  - **StartDeserializeEnumeration**
      - string value as method name
      - parameters: 
          - BitStorageReader reader
          - PropertyInfo propInfo
          - int depth
  - **EndDeserializeEnumeration**
      - string value as method name
      - parameters: 
          - BitStorageReader reader
          - PropertyInfo propInfo
          - int depth
## **BitElement** 
The bottom level, each field has exactly 1 BitElement.  This is a generic due
to the Min/Max values.  The generic needs to be the same as the bottom of the
field type (e.g. List\<int\> would need the BitElement to be generic type int).
This has to be generic because a ulong max value won't fit in a long, so in order
for the min/max to be legal values, the compiler needs to know the data type.
  - **Signed**
	- Specifies if the primitive is signed or unsigned
  - **Min**
    - Minimum value for the primitive.  Used for validation and InferBits
  - **Max**
    - Maximum value for the primitive.  Used for validation and InferBits
  - **Bits**
    - Number of bits to use for the primitive
    - Required unless InferBits is specified
    - Cannot be used with InferBits
  - **InferBits**
    - Specifies that the number of bits should be inferred from the Min 
      and Max values as well as Signed or the data type of the primitive
### **BitField**
Specifies that the field is serializable.  This must be specified for each
field that is going to be serialized.
  - **AllowNonPublicAccess**
    - Specifies if non-public fields (e.g. private, protected, etc) can be serialized.
    - default = false
    - If this is not specified to be true, only the public properties will be serialized.
  - **Description**
    - Optional description for the field.
 
# Questions
> 10/7/2026

The CountBitLength in BitLevel is mutually exclusive of Terminator and Escape,
But TerminatorBits is required for Terminator, can "Bits" be used for both? That
may be confusing.

Should TerminatorBits be assumed to be the BitElement bits instead?  Each element
would have to be looked at and evaluated to see if it matches the Terminator or
Escape value, but if the number of TerminatorBits is not the same as the BitElement 
bits, this could cause issues (i.e. if the BitElement is 32 bits, but the 
TerminatorBits is 8 bits, then the first 8 bits of each element would have to be 
masked, or if the BitElement is 8 bits, but the Terminator Bits is 32, then 4 
elements would have to be checked).

Should the Terminator and Escape just be removed altogether?  ShouldSerialize/ShouldDeserialize
and SerializeConverter/DeserializeConverter can be used to handle this instead.

For now, I'll go with removing Terminator and Escape.  If CountBitLength is not 
specified, then ShouldDeserialize has to be specified.

Having said that, I wonder if I should add "StartEnumerable" and "EndEnumerable" 
attributes to specify the start and end of an enumeration (e.g. ). This would 
allow for more complex enumerations that may not have a CountBitLength or a 
Terminator/Escape value.  This may be confusing as the "StartEnumerable" and 
"EndEnumerable" would be on the same level as the enumerable itself, not the values
of the enumerable.  This is unlike "ShouldSerialize" and "ShouldDeserialize", which
are on the same level as the enumerable values.  E.g. List\<int\> can have a
BitLevel(depth=0) and BitElement, but the StartEnumerable and EndEnumerable would 
be on the BitLevel itself, not the BitElement.  ShouldSerialize and ShouldDeserialize
could be on either level.  If they are on the BitLevel, they would specify if the
entire property should be serialized/deserialized, but if they are on the BitElement,
they would be called and evaluated for each element of the enumerable.

Serialize and Deserialize need to be available for StartEnumerable and EndEnumerable, 
so that's 4 extra methods, but I think this will be a simpler way to do it overall than
to try to figure out what to do with Terminator and Escape.
