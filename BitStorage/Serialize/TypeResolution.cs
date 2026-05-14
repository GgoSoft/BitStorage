using System;
using System.Linq;

namespace GgoSoft.Serialize
{
	public readonly struct TypeResolution
	{
		public TypeResolution(Type? fieldType, 
			int? nativeWidth, 
			int? width, 
			bool isEnumerable, 
			bool isPrimitive,
			//bool isNullable, 
			//bool isElementNullable, 
			bool nativeSigned, 
			bool signed, 
			Type? customBitSerializable, 
			Type? bitSerializable)
		{
			FieldType = fieldType;
			NativeWidth = nativeWidth;
			Width = width;
			IsEnumerable = isEnumerable;
			IsPrimitive = isPrimitive;
			//IsNullable = isNullable;
			//IsElementNullable = isElementNullable;
			NativeSigned = nativeSigned;
			Signed = signed;
			CustomBitSerializable = customBitSerializable;
			BitSerializable = bitSerializable;
		}
		public readonly Type? FieldType { get; }
		public readonly int? NativeWidth { get; }
		public readonly int? Width { get; }        // native width in bits for the subject (element or type)
		public readonly bool IsEnumerable { get; }
		public readonly bool IsPrimitive { get; } // If IsEnumerable is false, the data type is a primitive bit type. If IsEnumerable is true, the element type is a primitive bit type. In either case, the primitive type must be one of the supported types (e.g. byte, int, long, etc.) that can be directly serialized as bits.
		//public readonly bool IsNullable { get; }
		//public readonly bool IsElementNullable { get; } // specifies if the element of an enumerable is nullable, false for non-enumerable
		public readonly bool NativeSigned { get; }
		public readonly bool Signed { get; }
		public readonly Type? CustomBitSerializable { get; }
		public readonly Type? BitSerializable { get; }
	}
}
