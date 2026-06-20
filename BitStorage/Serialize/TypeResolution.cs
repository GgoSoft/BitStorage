using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GgoSoft.Serialize
{
	public static class MetadataEngine
	{
		// 1. For Single Attributes (like [BitField])
		public static TAttr? Hydrate<TAttr>(PropertyInfo prop) where TAttr : Attribute
		{
			var attr = prop.GetCustomAttribute<TAttr>();
			if (attr == null) return null;

			var data = prop.GetCustomAttributesData()
						   .FirstOrDefault(d => d.AttributeType == typeof(TAttr));

			if (data != null)
			{
				FlagExplicitArguments(attr, data);
			}

			return attr;
		}

		// 2. For Multiple Attributes (like [BitFieldLevel])
		public static List<TAttr> HydrateMultiple<TAttr>(PropertyInfo prop) where TAttr : Attribute
		{
			var attrs = prop.GetCustomAttributes<TAttr>().ToList();
			var dataList = prop.GetCustomAttributesData()
							   .Where(d => d.AttributeType == typeof(TAttr))
							   .ToList();

			// .NET guarantees both lists are returned in the exact same metadata declaration order
			for (int i = 0; i < attrs.Count && i < dataList.Count; i++)
			{
				FlagExplicitArguments(attrs[i], dataList[i]);
			}

			return attrs;
		}

		// Direct flag-flipper on the live attribute instance
		private static void FlagExplicitArguments<TAttr>(TAttr attr, CustomAttributeData data) where TAttr : Attribute
		{
			var type = typeof(TAttr);

			foreach (var arg in data.NamedArguments)
			{
				type.GetProperty($"Has{arg.MemberName}", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					?.SetValue(attr, true);
			}
		}
	}
	public record LevelTypeResolution
	{
		public int Depth { get; init; }
		public bool Optional { get; init; }
		//public bool IgnoreIfNull { get; init; }
		public int CountBitLength { get; init; }
		public string? CountField { get; init; }
		//public object? TerminatorValue { get; init; }
		//public object? EscapeValue { get; init; }
		public string? ConditionalProperty { get; init; }
		public Type? ConditionalType { get; init; }
		public string? ConditionalMethod { get; init; }
		//public Type? FieldType { get; init; }
		//public bool IsEnumerable { get; init; }
		//public bool IsPrimitive { get; init; } 
		public static LevelTypeResolution? Map(BitFieldLevelAttribute? attr/*, Type? fieldType, bool isEnumerable, bool isPrimitive*/)
		{
			if(attr ==null)
			{
				return null;
			}
			return new LevelTypeResolution
			{
				Depth = attr.Depth,
				Optional = attr.Optional,
				//IgnoreIfNull = attr.IgnoreIfNull,
				CountBitLength = attr.CountBitLength,
				CountField = attr.CountField,
				//TerminatorValue = attr.TerminatorValue,
				//EscapeValue = attr.EscapeValue,
				ConditionalProperty = attr.ConditionalProperty,
				ConditionalType = attr.ConditionalType,
				ConditionalMethod = attr.ConditionalMethod
				//FieldType = fieldType,
				//IsEnumerable = isEnumerable,
				//IsPrimitive = isPrimitive
			};
		}
	}
	public record TypeResolution
	{
		//public TypeResolution(Type? fieldType, 
		//	int? nativeWidth, 
		//	int? width, 
		//	bool isEnumerable, 
		//	bool isPrimitive,
		//	//bool isNullable, 
		//	//bool isElementNullable, 
		//	bool nativeSigned, 
		//	bool signed, 
		//	Type? customBitSerializable, 
		//	Type? bitSerializable,
		//	TypeResolution? element)
		//{
		//	FieldType = fieldType;
		//	NativeWidth = nativeWidth;
		//	Width = width;
		//	IsEnumerable = isEnumerable;
		//	IsPrimitive = isPrimitive;
		//	//IsNullable = isNullable;
		//	//IsElementNullable = isElementNullable;
		//	NativeSigned = nativeSigned;
		//	Signed = signed;
		//	CustomBitSerializable = customBitSerializable;
		//	BitSerializable = bitSerializable;
		//	Element = element;
		//}
		public required Type FieldType { get; init; }
		public int? NativeWidth { get; init;}
		public int? Width { get; init; }        // native width in bits for the subject (element or type)
		public bool IsEnumerable { get; init;}
		public bool IsPrimitive { get; init; } // If IsEnumerable is false, the data type is a primitive bit type. If IsEnumerable is true, the element type is a primitive bit type. In either case, the primitive type must be one of the supported types (e.g. byte, int, long, etc.) that can be directly serialized as bits.
		public bool? IsNullable { get; init; } // Not all nullables can be figured out via reflection (e.g. nullable reference types), so this is a tri-state where null means "unknown". For enumerable types, this refers to the nullability of the element (e.g. List<int?> would have IsNullable=true because the element type int? is nullable). For non-enumerable types, this refers to the nullability of the type itself (e.g. int? would have IsNullable=true, while int would have IsNullable=false).
		public bool IsString { get; init; } // Even though strings are enumerable, and they will be handled by the enumerable logic, the ContitionalXYZ attributes can be useful for string-specific conditions (e.g. omit if empty), so we flag this separately for convenience.
		public bool NativeSigned { get; init;}
		public bool Signed { get; init;}
		public Type? CustomBitSerializable { get; init;}
		public Type? BitSerializable { get; init;}
		public TypeResolution? EnumerableElement { get; init; }
		public LevelTypeResolution? LevelTypeResolution { get; init; }

		//public bool? EnumerableOptional { get; init; }
		//public string? EnumerableConditionalProperty { get; init; }
		//public string? EnumerableConditionalType { get; init; }
		//public TypeResolution? UnderlyingType {get;init;} 
		//public readonly bool IsNullable { get; }
		//public readonly bool IsElementNullable { get; } // specifies if the element of an enumerable is nullable, false for non-enumerable
	}
}
