using System;
using System.Runtime.CompilerServices;

// TODO:
/*
1. Optional = true
	Serializer writes a 1‑bit exists flag.
	If exists = 0 → value is null (or if the value is OmitIfEquals, or the value of the OmitIfEqualsField field).
	If exists = 1 → value is serialized normally.
	Costs 1 bit.
	Works for any type.
	Safest and most explicit.
	-- should options be true/false/null -- if the field is nullable but the value shouldn't be null, optional should be required to be false
	-- i.e. if the field is nullable, "Optional" is required, but can be set to false, no exists bit, and an exception will be thrown if the field is null
2. DefaultIfNull = X
	No exists bit.
	If value is null → write X.
	If value is non‑null → write the actual value.
	On read: if the stored value equals X → return null.
	Zero overhead.
	Loses the ability to distinguish null from X.
	Must be opt‑in only.
3. ConditionalProperty (or ConditionalType)
	A method or condition determines whether the field is present.
	If condition is false → field is omitted entirely.
	If condition is true → field is written normally.
	Null is allowed only if Optional=true or DefaultIfNull is also set.
	No exists bit unless Optional=true.
4. If none of the above are specified
	And the field is nullable → throw.
	Because the serializer cannot guess how null should be represented.

bool? Optional = null // 
object? DefaultIfNull = null
string? ConditionalProperty = null
string? ConditionalType = null
object? OmitIfEquals = null

string? ElementOptional = null
string? ElementConditionalProperty = null
string? ElementConditionalType = null
bool? ElementIgnoreIfNull = null

Bits
Signed
Min
Max
UnsignedMin
UnsignedMax
InferBits
CountBitLength
TerminatorValue
ConditionalProperty
ConditionalType
ConditionCombine
ConditionMode
ConverterType
Version
OmitIfEquals
Order
Description
AllowNonPublicAccess
EnumerableCountField

All these "omit" fields  (except DefaultIfNull and OmitIfEqual, these only refer to the innermost type) should also have an "ElementXYZ" as well specifying each element of an enumerable
(e.g. [BitField(ElementOptional=true)] would mean that the field must be an enumerable, and each element of the enumerable would have a presence bit)
The regular "omit" fields would be referring to the enumeratable itself, not the elements of the enumerable

All the ElementXYZ attributes should have strings as their type.  The value of the string can be "index:value" or just "value" with the index inferred.
These are also comman separated, so ElementOptional="index1:value1,index2:value2,etc." is allowed.  A mixture of them is also allowed, so "true,3:true,true" would mean
first element (i.e. List<int> -- would mean the "int" part because "Optional" is referring to the outer field "List<T>") is optional true, the 2nd element 
(if List<List<int>>) is default optional false (because it's not been specified), the 3rd element is optional true, and the 4th element is optional true
(because it's the next in sequence after the specified "3").  All specified indexed fields have to be in order (i.e. "3:true,1:true" is an error), and
an index can't be used twice inferred or specified (i.e. "3:true,true,4:true" is an error because the middle true is inferred 4, so the specified 4 is duplicate).

"DefaultIfNull" and "OmitIfEqual" don't make any sense to have for an enumeration, so these would be for the innermost type (i.e. List<List<List<int>>> -- 
would refer to the "int" type)

Enumeration IsNullable and IsElementNullable aren't working -- check TestrConsole for update

Enumerations with IBitSerializable and ICustomBitSerializable don't work


The difference between IBitSerializable and ICustomeBitSerializable is that all objects that are going to be serialized must be IBitSerializable, this is not 
custom, this is the automatic part.  If an object inside the class that implements IBitSerializable also implements IBitSerializable, then serialize will 
automatically be called on it.  ICustomBitSerializable has a Serialize/Deserialize method that will be implemented and called and serialization/deserialization 
will be done manually by those methods.
e.g.
```c#
class A implements IBitSerializable
{
   [BitField(...)]
   B b {get;set;} // will use the built in serializer
   [BitField(...)]
   C c {get;set;} // will be manually serialized
}
class B implements IBitSerializable
{
   // other fields
}
class C implements IBitSerializable
{
   // other fields
   public void Serialize(...) // may possibly return something different
   {
      // custom serialize stuff
   }
   public void Deserialize(...) // may possibly return something different
   {
      // custom deserialize stuff
   }
}
```


I've changed my mind.  As the serializer is going through, it's storing IBitSerializable objects in dictionary<object, int> with the int being the 
index of the object (if there's something like a List<object> that holds unique elements in order, that would be better).  As it's being serialized, 
if it finds a new object, it will add that to the collection, write a 1 followed by the serialized object.  If it finds an object that's already 
been written, it will write a 0 followed by a number that's the number of bits that will fit a count of the dictionary (or whatever collection).  
When it's being deserialized, the opposite will happen, it will read objects and store them in a some sort of collection, and when it hits a 0 for 
an object, it looks at the number of elements in the collection, reads the number of bits that fits the number and assigns the existing object to 
the field.  E.g. it writes 10 new objects and has stored all of them in a collection, then it comes across an object that it's already written (let's
say the 3rd object), so it looks at the collection count, finds 10, which is stored in 4 bits then writes 0011.  The reused object won't be added 
to the list.  Deserialization is the same.


should there be a serializable option to say "throw an error, or use an extra bit" or something?  The issue with this is that it doesn't know if it's 
a recursive object or a reused object.  I would think that any extra bit used should be an option for the user so if there's a possibility of a reused 
object, they should manually specify that they want an extra bit to be used for each object or else it's an error.  Also, there could be an option to 
include ICustomBitStorage objects as well.  The way it would work is the same as a IBitSerializable.  Is there a better way?

public enum ObjectReuseMode
{
    Error,
    Ignore,
    Track,
    TrackIncludeCustom
}
the attribute per field should be "ObjectReuseMode" and the global should be "DefaultObjectReuseMode" with the default of error

for ObjectReuseMode.Ignore, the serializer will not track objects at all and will serialize them as if they were new every time.  This is the simplest 
option but can lead to large serialized sizes if there are many reused objects.  This can also lead to recursion.  To prevent that, the following
pseudocode should be used:

void SerializeIgnore(object obj)
{
    if (seen.Contains(obj))
        throw new BitSerializationException(
            "Cycle detected in ObjectReuseMode.Ignore");

    if (objectToIndex.ContainsKey(obj))
        throw new BitSerializationException(
            "Shared reference detected in ObjectReuseMode.Ignore");

    seen.Add(obj);

    SerializeObjectNormally(obj);

    seen.Remove(obj);
}
This only affects IBitSerializable.  ICustomBitSerializable would have to deal with this themselves, but they can use the same pattern if they want to 
support Ignore mode.  For TrackIncludeCustom, the serializer will track all objects that are either IBitSerializable or ICustomBitSerializable and will 
write an extra bit for each object to indicate whether it's a new object or a reused object.  This is the most flexible option but can lead to larger 
serialized sizes due to the extra bits and tracking overhead.



Summary: What comes after FieldMetadataBuilder?
Here’s the pipeline in order:

FieldMetadataBuilder  
→ metadata for one field

TypeMetadataBuilder   -- DEPRECATED
→ metadata for the entire type (recursive)

Metadata Cache  
→ avoid repeated reflection

Encoding Plan Builder  
→ flatten metadata into a fast execution plan

Encoder  
→ write bits according to the plan

Decoder  
→ read bits according to the plan

Public API  
→ Serialize<T>, Deserialize<T>


The correct architecture for your serializer
Here is the correct, minimal, clean pipeline:

1. FieldMetadataBuilder
Builds metadata for a single type

Cached in IMetadataCache

No recursion

No nested type inspection

2. Serializer
Walks fields in order

For each field:

If primitive → encode directly

If IBitSerializable → call Serialize recursively

If ICustomBitSerializable → call custom Serialize

If enumerable → iterate and apply same rules

Apply ObjectReuseMode logic

Apply optionality/omit/default rules

3. Deserializer
Mirrors serializer

Uses same FieldMetadata

Recursively calls Deserialize for IBitSerializable

Calls custom Deserialize for ICustomBitSerializable


If a class implements both interfaces:

✔ Treat it as ICustomBitSerializable
✔ Ignore the IBitSerializable automatic behavior
✔ Use the custom Serialize/Deserialize methods
✔ Apply ObjectReuseMode normally (TrackIncludeCustom allows tracking)
This gives you:

Predictability

No silent overrides

No double‑serialization

No corrupted bitstreams

Full user control when they ask for it





Add an option for enumerations to allow pausing of serialization after each element, allowing for interleaving of other data or for 
streaming scenarios.  This could be a simple boolean flag like "PauseAfterElement" that, when true, causes the serializer to yield 
control back to the caller after writing each element of an enumerable field.  The caller could then perform additional actions 
(e.g., write other fields, flush buffers, etc.) before resuming serialization of the next element.  This would be especially useful 
for large collections or for scenarios where you want to interleave metadata or other fields between elements of a collection.  
The deserializer would also need to support this by yielding control back to the caller after reading each element when the 
corresponding flag is set.

there should also be another option for ending the enumeration (i.e the value is sent to a method that returns true/false saying this is the
end of the enumeration).  This would allow for more flexible encoding of enumerables without needing to specify the count upfront, which can 
be useful in streaming scenarios or when the count is not known in advance.  The value can potentially be "peeked" from the stream without 
consuming it, allowing the condition method to make a decision based on the next element's value or other context.

*/

namespace GgoSoft.Serialize
{
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
	public class BitFieldLevelAttribute : Attribute
	{
		public int Depth { get; set; }
		public bool HasDepth { get; internal set; } = false; // this is needed to distinguish between explicitly set Depth=0 and default Depth=0 for positional levels
		public bool Optional { get; set; }
		public bool HasOptional { get; internal set; } = false; // this is needed to distinguish between explicitly set Optional=false and default Optional=false

		public object? OmitIfEqual { get; set; }
		public bool HasOmitIfEqual { get; internal set; } = false; // this is needed to distinguish between explicitly set OmitIfEqual=null and default OmitIfEqual=null
		//When true, if the field value is null, it will be omitted from serialization. This is useful for reference types or nullable value types where a null value indicates the absence of data.
		//If "Optional" is also true, the presence bit will indicate whether the field is included or not, regardless of whether it's null or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
		//public bool IgnoreIfNull { get; set; }
		public bool HasIgnoreIfNull { get; internal set; } = false;

		//When serializing enumerables, the number of bits used to encode the element count
		//Can only be used with enumerations
		//Cannot be used with TerminatorValue, as they serve different purposes.
		public int CountBitLength { get; set; }
		public bool HasCountBitLength { get; internal set; } = false;

		//Optional name of another property on the parent object used to store the element count for enumerables.
		//If specified, this property will be used to read/write the count of elements in the enumerable instead of using a fixed bit length defined by CountBitLength. This allows for more flexible serialization of collections where the count may not fit within a predetermined number of bits.
		//Evaluates strictly on the current context class containing the collection field declaration; it will not look inside target complex elements.
		public string? CountProperty { get; set; }
		public bool HasCountProperty { get; internal set; } = false;

		public string? HasMoreItemsMethod { get; internal set; }
		//When serializing enumerables with a terminator, the terminator element value (encoded using the element width).
		//Can only be used with enumerations
		//Cannot be used with CountBitLength, as they serve different purposes.
		//public object? TerminatorValue { get; set; }
		//public bool HasTerminatorValue { get; internal set; } = false;

		/// <summary>
		/// When serializing enumerables with a terminator, if the value of the current element equals the specified terminator value, 
		/// the EscapeValue will be serialized first, then the terminator value will be written.  This allows for escaping of the terminator 
		/// value in the data. For example, if the terminator value is 0 and the EscapeValue is 1, then whenever an element with value 0 is 
		/// encountered, a 1 will be written followed by the 0. The deserializer can then recognize that the 0 is an escaped value rather 
		/// than a terminator. This is useful for cases where the data may contain values that are the same as the terminator, allowing 
		/// them to be included in the serialized output without prematurely ending the enumeration.
		/// If the current element equals the escape value, the same thing will happen.  This allows for the escape value itself to be 
		/// included in the data without ambiguity.  If the EscapeValue is not set and the current element equals the TerminatorValue, it 
		/// will throw an exception.
		/// </summary>
		//public object? EscapeValue { get; set; }
		//public bool HasEscapeValue { get; internal set; } = false;

		//Optional name of another property on the same object used for simple conditional inclusion.
		//If this property is null, false, or empty, the current field will be excluded.
		//If the property is excluded in serialization, it also must be excluded in deserialization.
		//If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
		public string? ConditionalProperty { get; set; }
		public bool HasConditionalProperty { get; internal set; } = false;

		//Condition type implementing IFieldCondition
		//Field will be written based on IFieldCondition.Evaluate
		//Can be direct injected into the serializer, so can have constructor parameters for configuration
		//Can also be instantiated with Activator.CreateInstance, so must have a public parameterless constructor
		//May be reused across multiple fields, so should be designed to be reusable and stateless if possible
		//If the property is excluded in serialization, it also must be excluded in deserialization.
		//If the "Optional" property is used, the serializer will write a presence bit before the field, indicating whether the field is included or not. This allows for optional fields that can be omitted without breaking the structure of the bit storage.
		public Type? ConditionalType { get; set; }
		public bool HasConditionalType { get; internal set; } = false;

		public string? ConditionalMethod { get; set; }
		public bool HasConditionalMethod { get; internal set; } = false;

		public BitFieldLevelAttribute() { }
	}
	public interface IBitFieldBounds
	{
		bool HasMin { get; }
		bool HasMax { get; }
		bool HasTerminator { get; }
		bool HasEscape { get; }
		bool IsSigned { get; }
		long SignedMin{ get; }
		long SignedMax { get; }
		ulong UnsignedMin { get; }
		ulong UnsignedMax { get; }
		//long SignedTerminator { get; }
		ulong UnsignedTerminator { get; }
		//long SignedEscape { get; }
		ulong UnsignedEscape { get; }
	}
	public class EmptyBitFieldBounds : IBitFieldBounds
	{
		// A single shared instance to avoid allocating memory repeatedly
		public static readonly EmptyBitFieldBounds Instance = new();

		private EmptyBitFieldBounds() { } // Prevent external instantiation

		public bool IsSigned => false;

		public bool HasMin => false;
		public bool HasMax => false;
		public bool HasTerminator => false;
		public bool HasEscape => false;

		public long SignedMin => 0;
		public long SignedMax => 0;
		public ulong UnsignedMin => 0;
		public ulong UnsignedMax => 0;
		//public long SignedTerminator => 0;
		public ulong UnsignedTerminator => 0;
		//public long SignedEscape => 0;
		public ulong UnsignedEscape => 0;
	}

	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class BitFieldAttribute<T> : BitFieldAttribute, IBitFieldBounds where T: struct
	{
		public BitFieldAttribute()
		{
			IsSigned = typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) ||
				   typeof(T) == typeof(int) || typeof(T) == typeof(long);
		}
		private T _min;
		private T _max;
		private T _escape;
		private T _terminator;
		public bool IsSigned { get; private set; } 
		public T Min { get => _min; set { _min = value; if (IsSigned) { SignedMin = Convert.ToInt64(value); UnsignedMin = 0; } else { UnsignedMin = Convert.ToUInt64(value);SignedMin = 0; } } }
		public bool HasMin { get; internal set; }
		public T Max { get => _max; set { _max = value; if (IsSigned) { SignedMax = Convert.ToInt64(value); UnsignedMax = 0; } else { UnsignedMax = Convert.ToUInt64(value); SignedMax = 0; } } }
		public bool HasMax { get; internal set; }
		public T Terminator { get => _terminator; set { _terminator = value; if (IsSigned) { UnsignedTerminator = (ulong)Convert.ToInt64(value);} else { UnsignedTerminator = Convert.ToUInt64(value); } } }
		public bool HasTerminator { get; internal set; }
		public T Escape { get => _escape; set { _escape = value; if (IsSigned) { UnsignedEscape = (ulong)Convert.ToInt64(value); } else { UnsignedEscape = Convert.ToUInt64(value); } } }
		public bool HasEscape { get; internal set; } = false;
		public long SignedMin { get; private set; }
		public long SignedMax { get; private set;}
		public ulong UnsignedMin { get; private set;}
		public ulong UnsignedMax { get; private set;}
		//public long SignedTerminator { get; private set;}
		public ulong UnsignedTerminator { get; private set;}
		//public long SignedEscape { get; private set;}
		public ulong UnsignedEscape { get; private set;}

	}
	/// <summary>
	/// Marks a property as a bit-field for the serializer and provides schema hints.
	/// Use this attribute to declare explicit bit widths, signedness, bounds, converters,
	/// conditional inclusion, enumerable encoding hints, and other per-field metadata.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public class BitFieldAttribute : Attribute 
	{
		/// <summary>
		/// Explicit bit width for the field. When set, this value takes highest precedence
		/// and no inference will be performed for this field.
		/// Value must be between 1 and the number of bits appropriate for the field.  
		/// If field is enumerable, this applies to the element width.		
		/// </summary>
		public int Bits { get; set; }
		public bool HasBits { get; internal set; }
//		public int? BitsNullable { get; private set; } = null;

		/// <summary>
		/// Interpret the stored bits as a signed two's complement value when reading.  This is
		/// required if <see cref="Bits"/> (or the inferred number of bits) is less than the native 
		/// width of the property type and you want to support negative values.
		/// </summary>
		public bool Signed { get; set; }
		public bool HasSigned { get; internal set; }
		//public bool? SignedNullable { get; private set; } = null;

		///// <summary>
		///// Optional signed minimum bound for the field. Use only for signed semantics
		///// or when you want to express a signed lower bound. Mutually exclusive with <see cref="UnsignedMin"/>.
		///// </summary>
		//public long Min { get; set; }
		//public bool HasMin { get; internal set; }
		////public long? MinNullable { get; private set; } = null;


		///// <summary>
		///// Optional signed maximum bound for the field. Use only for signed semantics
		///// or when you want to express a signed upper bound. Mutually exclusive with <see cref="UnsignedMax"/>.
		///// </summary>
		//public long Max { get; set; }
		//public bool HasMax { get; internal set; }
		//public long? MaxNullable { get; private set; } = null;

		///// <summary>
		///// Optional unsigned minimum bound for the field. Use this to express bounds
		///// in the full 0..2^64-1 range. Mutually exclusive with <see cref="Min"/>.
		///// </summary>
		//public ulong UnsignedMin { get; set; }
		//public bool HasUnsignedMin { get; internal set; }
		////public ulong? UnsignedMinNullable { get; private set; } = null;

		///// <summary>
		///// Optional unsigned maximum bound for the field. Use this to express bounds
		///// in the full 0..2^64-1 range. Mutually exclusive with <see cref="Max"/>.
		///// </summary>
		//public ulong UnsignedMax { get; set; }
		//public bool HasUnsignedMax { get; internal set; }
		//public ulong? UnsignedMaxNullable { get; private set; } = null;

		/// <summary>
		/// Per-field opt-in for bit-width inference.
		/// <list type="bullet">
		/// <item><description><c>true</c> — force inference for this field (<see cref="Bits"/> must not be set). 
		/// If bounds are present, infer from them; otherwise fall back to the native width of the 
		/// field type.</description></item>
		/// <item><description><c>false</c> — do not infer for this field; use <see cref="Bits"/> if present</description></item>
		/// <item><description><c>null</c> fall back on global policy</description></item>
		/// </list>
		/// Default is <c>false</c>.
		/// </summary>
		public bool InferBits { get; set; }
		public bool HasInferBits { get; internal set; }
		//public bool? InferBitsNullable { get; private set; } = null;

		/// <summary>
		/// When serializing enumerables, the number of bits used to encode the element count.
		/// Provide exactly one of <see cref="CountBitLength"/> or <see cref="TerminatorValue"/> for enumerable fields.
		/// Valid range: 1..32.
		/// </summary>
		//public int CountBitLength { get; set; }
		//public bool HasCountBitLength { get; internal set; }
		//public int? CountBitLengthNullable { get; private set; } = null;

		public int DefaultCountBitLength { get; set; }
		public bool HasDefaultCountBitLength { get; internal set; }
		/// <summary>
		/// When serializing enumerables with a terminator, the terminator element value (encoded using the element width).
		/// Provide exactly one of <see cref="CountBitLength"/> or <see cref="TerminatorValue"/>.
		/// </summary>
		//public long TerminatorValue { get; set; }
		//public bool HasTerminatorValue { get; internal set; }

		//public ulong EscapeValue { get; set; }
		//public bool HasEscapeValue { get; internal set; } = false;

		//public ulong? TerminatorValueNullable { get; private set; } = null;

		/// <summary>
		/// Optional name of another property on the same object used for simple conditional inclusion.
		/// The serializer or a condition implementation can use this to decide whether to include the field.
		/// If the other property is null, false, or empty, the current field will be excluded. This is a simple alternative 
		/// to implementing a full <see cref="IFieldCondition"/>.
		/// </summary>
		public string? ConditionalProperty { get; set; } = null; // TODO: this is currently ignored

		/// <summary>
		/// Optional condition type implementing <see cref="IFieldCondition"/>. When provided, the serializer
		/// will resolve and invoke the condition to decide inclusion. The type must implement <see cref="IFieldCondition"/>.
		/// </summary>
		public Type? ConditionalType { get; set; } = null;

		/// <summary>
		/// Optional name of a method on the same object used for conditional inclusion.  This method must have the signature of
		/// <code>
		/// bool Method(<see cref="FieldMetadata"/> fm)
		/// bool Method(<see cref="FieldMetadata"/> fm, object? sourceValue)
		/// bool Method(<see cref="FieldMetadata"/> fm, int elementIndex) // for enumerables
		/// bool Method(<see cref="FieldMetadata"/> fm, int elementIndex, object? elementPreview) // for enumerables
		/// bool Method() //fallback; no context
		/// </code>
		/// </summary>
		public Type? ConditionalMethod { get; set; } = null;

		/// <summary>
		/// How multiple conditional checks are combined when both <see cref="ConditionalProperty"/> and <see cref="ConditionalType"/>
		/// (or multiple conditions) are present. Defaults to <see cref="ConditionCombine.And"/>.
		/// </summary>
		public ConditionCombine ConditionCombine { get; set; } = ConditionCombine.And; // TODO: this is currently ignored

		/// <summary>
		/// The evaluation mode for conditions: snapshot (evaluate against a snapshot of current values)
		/// or incremental (evaluate as values are processed). Defaults to <see cref="ConditionEvaluationMode.Snapshot"/>.
		/// </summary>
		public ConditionEvaluationMode ConditionMode { get; set; } = ConditionEvaluationMode.Snapshot; // TODO: this is currently ignored

		/// <summary>
		/// Optional converter type used to transform between the CLR property and the bit-level representation.
		/// The serializer will resolve this type (DI first, then Activator) and use it when present.
		/// </summary>
		public Type? ConverterType { get; set; } = null;

		/// <summary>
		/// Optional method name on the converter type used to transform between the CLR property and the bit-level representation.
		/// The serializer will resolve this method when present.
		/// </summary>
		public string? ConverterMethod { get; set; } = null; // TODO: this is currently ignored -- the signature of the method should include SerializerDirection.Serialize or Deserialize

		/// <summary>
		/// Version number for the field schema. Consumers can use this to implement versioned converters or conditional logic.
		/// </summary>
		// public int Version { get; set; } = 0; // TODO: add versioning later if needed

		/// <summary>
		/// When true, the field is considered optional (presence may be encoded separately).
		/// Semantics are implementation-defined; the serializer may pack presence bits when configured.
		/// </summary>
		//public bool Optional { get; set; } = false; // TODO: make this work, OmitIfEquals must be set for this to work

		/// <summary>
		/// Default value for the field used during deserialization when the field is omitted.
		/// The builder validates that this value is assignable to the property type and fits the resolved range.
		/// </summary>
		public object? OmitIfEquals { get; set; } = null; // TODO: OmitIfEquals only makes sense with Optional, which isn't working

		/// <summary>
		/// Optional ordering hint for fields. When not set, the builder uses the property's metadata token.
		/// Lower values are serialized earlier.
		/// </summary>
		public int Order { get; set; }
		public bool HasOrder { get; internal set; }
		//public int? OrderNullable { get; private set; } = null;

		/// <summary>
		/// Human-readable description for the field. Useful for generated documentation or diagnostics.
		/// </summary>
		public string Description { get; set; } = "";

		/// <summary>
		/// When true, the builder may use non-public accessors for this property.
		/// Default: false.
		/// </summary>
		public bool AllowNonPublicAccess { get; set; } = false;
		public bool HasAllowNonPublicAccess { get; internal set; }
		//public bool? AllowNonPublicAccessNullable { get; private set; } = null;

		/// <summary>
		/// Optional name of another property on the same object that holds the count of elements for an enumerable field.
		/// </summary>
		//public string? EnumerableCountField { get; set; } // TODO: this is currently ignored
		//public string? ElementOptional { get; set; }
		//public string? ElementConditionalProperty { get; set; }
		//public string? ElementConditionalType { get; set; }
	}
	//// -------------------------
	//// Placeholder bit storage types
	//// -------------------------
	//public sealed class BitStorage { /* implement writer */ }
	//public sealed class BitStorageReader { /* implement reader */ }

}
