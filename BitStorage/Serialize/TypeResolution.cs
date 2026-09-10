using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GgoSoft.Serialize
{
	public static class MetadataEngine
	{
		// 1. For Single Attributes (like [BitField])
		public static BitFieldAttribute? Hydrate(PropertyInfo prop) 
		{
			var attr = prop.GetCustomAttribute<BitFieldAttribute>();
			if (attr == null) return null;

			var data = prop.GetCustomAttributesData()
						   .FirstOrDefault(d => typeof(BitFieldAttribute).IsAssignableFrom(d.AttributeType));

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
			var type = attr.GetType();// typeof(TAttr);

			foreach (var arg in data.NamedArguments)
			{
				type.GetProperty($"Has{arg.MemberName}", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
					?.SetValue(attr, true);
			}
		}
	}
	public record LevelTypeResolution
	{
		public int? Depth { get; init; }
		public bool Optional { get; init; }
		//public bool IgnoreIfNull { get; init; }
		public int? CountBitLength { get; init; }
		public string? CountField { get; init; }
		//public object? TerminatorValue { get; init; }
		//public object? EscapeValue { get; init; }
		public string? ConditionalProperty { get; init; }
		public Type? ConditionalType { get; init; }
		//public string? ConditionalMethod { get; init; }
		public string? ShouldContinueMethod { get; init; }
		public string? ShouldSerializeMethod { get; init; }
		public string? ShouldDeserializeMethod { get; init; }
		//public string? ShouldContinueMethod { get; init; }
		//public Type? FieldType { get; init; }
		//public bool IsEnumerable { get; init; }
		//public bool IsPrimitive { get; init; } 
		public static LevelTypeResolution? Map(string fieldName, BitFieldLevelAttribute? levelAttr/*, Type? fieldType, bool isEnumerable, bool isPrimitive*/, BitFieldAttribute attr, bool isEnumerable)
		{
			int? defaultCountBitLength = attr.HasDefaultCountBitLength ? attr.DefaultCountBitLength : null;
			int? countBitLength = levelAttr?.HasCountBitLength == true ? levelAttr.CountBitLength : defaultCountBitLength;
			int? depth = levelAttr?.HasDepth == true ? levelAttr.Depth : null;
			//if(isEnumerable && countBitLength is null)
			//{
			//	throw new SerializationException($"Field: {fieldName} is missing either CountBitLength or DefaultCountBitLength at depth {depth}");
			//}
			if(levelAttr ==null)
			{
				return new()
				{
					Depth = depth,
					CountBitLength = countBitLength
				};
			}
			return new LevelTypeResolution
			{
				Depth = levelAttr.Depth,
				//Optional = levelAttr.Optional,
				//IgnoreIfNull = attr.IgnoreIfNull,
				CountBitLength = levelAttr.CountBitLength,
				CountField = levelAttr.CountProperty,
				//TerminatorValue = attr.TerminatorValue,
				//EscapeValue = attr.EscapeValue,
				//ConditionalProperty = levelAttr.ConditionalProperty,
				//ConditionalType = levelAttr.ConditionalType,
				ShouldContinueMethod = levelAttr.ShouldContinueMethod,
				ShouldSerializeMethod = levelAttr.ShouldSerialize,
				ShouldDeserializeMethod = levelAttr.ShouldDeserialize,
				//ShouldContinueMethod = levelAttr.ShouldContinueMethod
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
		public MethodDelegateFactory.ShouldContinueInvoker? ShouldContinueMethod { get; init; }
		public MethodDelegateFactory.ShouldSerializeInvoker? ShouldSerializeMethod { get; init; }
		public MethodDelegateFactory.ShouldDeserializeInvoker? ShouldDeserializeMethod { get; init;  }
		//public TypeResolution? EnumerableElement { get; init; }
		public LevelTypeResolution? LevelTypeResolution { get; init; }

		//public bool? EnumerableOptional { get; init; }
		//public string? EnumerableConditionalProperty { get; init; }
		//public string? EnumerableConditionalType { get; init; }
		//public TypeResolution? UnderlyingType {get;init;} 
		//public readonly bool IsNullable { get; }
		//public readonly bool IsElementNullable { get; } // specifies if the element of an enumerable is nullable, false for non-enumerable
	}
}
