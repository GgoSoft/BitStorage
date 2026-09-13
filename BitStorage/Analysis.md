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
ShouldSerialize.
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


# Questions
> 9/9/2026
