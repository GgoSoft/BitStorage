using GgoSoft.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// High-level entry point for serialization and deserialization operations.
	/// This static class orchestrates choosing metadata-driven serialization, converters, or
	/// calling <see cref="ICustomBitSerializable"/> when present.gh, there goes your naked dancing in the int 
	/// The methods are placeholders and must be implemented to perform actual I/O with <see cref="BitStorage"/>.
	/// </summary>
	public static class Serializer
	{
		/// <summary>
		/// Serialize the provided <paramref name="obj"/> into the given <paramref name="storage"/> using the provided <paramref name="ctx"/>.
		/// Implementations should consult <see cref="SerializerContext.Metadata"/> and <see cref="FieldMetadata"/> to drive metadata-based serialization.
		/// </summary>
		/// <param name="obj">The object to serialize.</param>
		/// <param name="storage">Target bit storage writer.</param>
		/// <param name="ctx">Per-operation serializer context.</param>
		public static void SerializeObject<T>(T obj, BitStorage storage, SerializerContext ctx) where T: IBitSerializable
		{
			var builder = new FieldMetadataBuilder(ctx.Options);
			var metadata = builder.BuildTypeMetadata(obj.GetType());
			foreach (var field in metadata.FieldsInOrder)
			{
				Console.WriteLine($"Serializing {field.Name}");
				var valueObject = field.Accessors.Getter(obj);
				if (field.TypeResolution[0].CustomBitSerializable != null)
				{
					SerializeCustom(storage, ctx, field, valueObject);
				}
				if (field.TypeResolution[0].IsPrimitive)
				{
					WritePrimitive(storage, field, valueObject);
				}
				else if(field.TypeResolution[0].IsEnumerable)
				{
					var list = EvaluateEnumerable(field, valueObject, 0, storage, ctx);
					WriteEnumerable(storage, ctx, field, list, 0);
				}
				Console.WriteLine();
			}
		}

		private static void SerializeCustom(BitStorage storage, SerializerContext ctx, FieldMetadata field, object? valueObject)
		{
			var openMethod = typeof(Serializer).GetMethod(
				nameof(SerializeObject),
				BindingFlags.Public | BindingFlags.Static
				);
			var closedMethod = openMethod.MakeGenericMethod(field.UnderlyingType.FieldType);
			var obj2 = closedMethod.Invoke(null, [valueObject, storage, ctx]);
		}

		private static T ReaderHelper<T>(BitStorageReader reader, FieldMetadata field, int depth, int count) where T: struct
		{
			var bitsReadCount = reader.Read(out T bitsRead, count, false);
			if (bitsReadCount != count)
			{
				throw new SerializationException($"Expected to read {count}, read {bitsReadCount} instead on {field.Name}, depth {depth}");
			}
			return bitsRead;
		}
		private static List<object> ReadEnumerable(BitStorageReader reader, SerializerContext ctx, FieldMetadata field, int depth)// where T1:struct
		{
			List<object> returnValue = [];
			var countBitLength = field.TypeResolution[depth].LevelTypeResolution?.CountBitLength;
			var lastDepth = depth == field.TypeResolution.Length - 2;
			var hasTerminator = field.BitFieldBounds.HasTerminator && lastDepth;
			var hasEscape = field.BitFieldBounds.HasEscape && lastDepth;
			var terminator = field.BitFieldBounds.UnsignedTerminator;
			var escape = field.BitFieldBounds.UnsignedEscape;
			int count = 0;
			if (!hasTerminator)
			{
				if (countBitLength == null)
				{
					throw new SerializationException($"Field {field.Name}: Couldn't find count bit length on depth {depth}");
				}
				else
				{
					count = ReaderHelper<int>(reader, field, depth, countBitLength.Value);
					Console.WriteLine($"Read count {count}");
					if(count <= 0)
					{
						throw new SerializationException($"Field {field.Name}: invalid count read: {count}");
					}
				}
			}
			returnValue.Add(count);
			int newDepth = depth + 1;
			int i = 0;
			bool done = false;
			bool escaped = false;
			while(!done)
			{
				if (field.TypeResolution[newDepth].CustomBitSerializable is not null)
				{
					returnValue.Add(DeserializeCustom(reader, ctx, field));
				}
				else if (field.TypeResolution[newDepth].IsPrimitive)
				{
					Type classType = typeof(Serializer);
					MethodInfo openMethod = classType.GetMethod( 						
						nameof(ReaderHelper), 						
						BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
						)!; 					
					MethodInfo closedMethod = openMethod.MakeGenericMethod(field.UnderlyingType.FieldType); 					
					var value = closedMethod.Invoke(null, [reader, field, newDepth, field.ResolvedBits]); 
					//var value = ReaderHelper(reader, field, newDepth, field.ResolvedBits);
					if (hasTerminator)
					{
						ulong uvalue;
						if (field.TypeResolution[newDepth].NativeSigned)
						{
							uvalue = (ulong)Convert.ToInt64(value);
						}
						else
						{
							uvalue = Convert.ToUInt64(value);
						}
						bool prevEscaped = escaped;
						if(!escaped && uvalue == escape && hasEscape)
						{
							Console.WriteLine($"Read escape {escape}");
							escaped = true;
							i--;
						}
						if (!escaped && uvalue == terminator)// || (uvalue == escape && hasEscape))
						{
							Console.WriteLine($"Read terminator {terminator}");
							done = true;
						}
						if(prevEscaped)
						{
							escaped = false;
						}
					}
					if (!escaped && !done)
					{
						returnValue.Add(value);
					}
				}
				else if (field.TypeResolution[newDepth].IsEnumerable)
				{
					returnValue.Add(ReadEnumerable(reader, ctx, field, newDepth));
				}
				else
				{
					throw new SerializationException($"Was expecting primitive or enumerable on {field.Name} at depth {depth}");
				}
				i++;
				if (!hasTerminator)
				{
					done = i >= count;
				}
			}
			if(hasTerminator)
			{
				returnValue[0] = i;
			}
			return returnValue;
		}
		private static object ProcessNode(FieldMetadata field, int depth, object currentNode)
		{
			Type targetType = field.TypeResolution[depth].FieldType;
			if (field.TypeResolution[depth].CustomBitSerializable is not null)
			{
				return currentNode;
			}
			// Case 1: The current target type is a primitive/leaf type
			if (field.TypeResolution[depth].IsPrimitive)
			{
				// Convert to the exact integer/primitive type (e.g., short, char, bool, int)
				return Convert.ChangeType(currentNode, targetType);
			}

			// Case 2: The current node is an enumerable container (represented as List<object>)
			if (currentNode is List<object> subList)
			{
				// The first element is always the count
				int count = (int)subList[0];
				if(targetType == typeof(string))
				{
					return string.Concat(subList.Skip(1));
				}
				if (targetType.IsArray)
				{
					Type elementType = field.TypeResolution[depth + 1].FieldType;
					Array array = Array.CreateInstance(elementType, count);

					for (int i = 0; i < count; i++)
					{
						// Items in subList are offset by 1 because index 0 is the count
						object elementValue = ProcessNode(field, depth + 1, subList[i + 1]);
						array.SetValue(elementValue, i);
					}
					return array;
				}
				else if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
				{
					IList list = (IList)Activator.CreateInstance(targetType)!;

					for (int i = 0; i < count; i++)
					{
						object elementValue = ProcessNode(field, depth + 1, subList[i + 1]);
						list.Add(elementValue);
					}
					return list;
				}
			}

			throw new InvalidOperationException($"Structure mismatch at depth {depth} for {field.Name}");
		}
		static void WriteEnumerable(BitStorage storage, SerializerContext ctx, FieldMetadata field, object? valueObject, int depth)
		{
			if (valueObject == null)
			{
				throw new SerializationException($"Wasn't expecting null as a value on {field.Name} at depth {depth}");
			}
			IEnumerable enumerable = (IEnumerable)valueObject;
			bool first = true;
			var countBitLength = field.TypeResolution[depth].LevelTypeResolution?.CountBitLength;
			var maxCountAllowed = (1 << countBitLength) - 1;
			var lastDepth = depth == field.TypeResolution.Length - 2;
			var hasTerminator = field.BitFieldBounds.HasTerminator && lastDepth;
			var hasEscape = field.BitFieldBounds.HasEscape && lastDepth;
			var terminator = field.BitFieldBounds.UnsignedTerminator;
			var escape = field.BitFieldBounds.UnsignedEscape;
			var isCustom = field.TypeResolution[depth].CustomBitSerializable is not null;
			if (!hasTerminator && countBitLength == null/* && !isCustom*/)
			{
				throw new SerializationException($"Field {field.Name}: Couldn't find count bit length on depth {depth}");
			}

			foreach (var value in enumerable)
			{
				if (first)
				{
					if (!hasTerminator)
					{
						int count = (int)value;
						if (count > maxCountAllowed)
						{
							throw new SerializationException($"Field {field.Name}: Found {count} elements, but count bit length only allows up to {maxCountAllowed} at depth {depth}");
						}
						Console.WriteLine($"Write Count {count} bitlength {countBitLength}");
						storage.Write(count, countBitLength);
					}
				}
				else
				{
					int newDepth = depth + 1;
					if (field.TypeResolution[newDepth].CustomBitSerializable is not null)
					{
						SerializeCustom(storage, ctx, field, value);
					}
					else if (field.TypeResolution[newDepth].IsPrimitive)
					{
						if (hasTerminator)
						{
							ulong uvalue;
							if (field.TypeResolution[newDepth].NativeSigned)
							{
								uvalue = (ulong)Convert.ToInt64(value);
							}
							else
							{
								uvalue = Convert.ToUInt64(value);
							}
							if(uvalue == terminator  || (uvalue == escape && hasEscape))
							{
								if(hasEscape)
								{
									Console.WriteLine($"Write Escape {escape} bitlength {field.ResolvedBits}");
									storage.Write(escape, field.ResolvedBits);
								} else
								{
									throw new SerializationException($"{field.Name}: data has terminator value: {value} but no escape character");
								}
							}
						}
						WritePrimitive(storage, field, value);
					}
					else if (field.TypeResolution[newDepth].IsEnumerable)
					{
						WriteEnumerable(storage, ctx, field, value, newDepth);
					}
					else
					{
						throw new SerializationException($"Was expecting primitive or enumerable on {field.Name} at depth {depth}");
					}
				}
				first = false;
			}
			if(hasTerminator)
			{
				Console.WriteLine($"Write terminator {terminator} bitlength {field.ResolvedBits}");
				storage.Write(terminator, field.ResolvedBits);
			}
		}
		static List<object> EvaluateEnumerable(FieldMetadata field, object? valueObject, int depth, BitStorage storage, SerializerContext ctx)
		{
			if(valueObject == null)
			{
				throw new SerializationException($"Wasn't expecting null as a value on {field.Name} at depth {depth}");
			}
			IEnumerable enumerable = (IEnumerable)valueObject;
			List<object> returnValue = [0];
			int newDepth = depth + 1;
			foreach(var value in enumerable)
			{
				//if (field.TypeResolution[newDepth].CustomBitSerializable is not null)
				//{
				//	SerializeCustom(storage, ctx, field, value);
				//} 
				//else
				if (field.TypeResolution[newDepth].IsPrimitive || field.TypeResolution[newDepth].CustomBitSerializable is not null)
				{
					returnValue.Add(value);
				}
				else if (field.TypeResolution[newDepth].IsEnumerable)
				{
					returnValue.Add(EvaluateEnumerable(field, value, newDepth, storage, ctx));
				}
				else
				{
					throw new SerializationException($"Was expecting primitive or enumerable on {field.Name} at depth {depth}");
				}
			}
			returnValue[0] = returnValue.Count - 1;
			return returnValue;
		}

		private static void WritePrimitive(BitStorage storage, FieldMetadata field, object? valueObject)
		{
			if (valueObject != null)
			{
				if (field.UnderlyingType.Signed)
				{
					var (value, _) = FieldMetadataBuilder.ValidateDefaultValue(field, valueObject);
					if (value == null)
					{
						throw new SerializationException($"Wasn't expecting null for field {field.Name}");
					}
					storage.Write(value.Value, field.ResolvedBits);
				}
				else
				{
					var (_, value) = FieldMetadataBuilder.ValidateDefaultValue(field, valueObject);
					if (value == null)
					{
						throw new SerializationException($"Wasn't expecting null for field {field.Name}");
					}
					storage.Write(value.Value, field.ResolvedBits);
				}
			}
		}

		/// <summary>
		/// Deserialize an instance of <paramref name="type"/> from the provided <paramref name="reader"/> using the given <paramref name="ctx"/>.
		/// Implementations should construct the object, set fields via <see cref="FieldMetadata.Setter"/>, and return the instance.
		/// </summary>
		/// <param name="type">The CLR type to deserialize.</param>
		/// <param name="reader">Source bit storage reader.</param>
		/// <param name="ctx">Per-operation serializer context.</param>
		/// <returns>The deserialized object instance, or null when appropriate.</returns>
		public static T? DeserializeObject<T>(BitStorageReader reader, SerializerContext ctx) where T : new()
		{
			var builder = new FieldMetadataBuilder(ctx.Options);
			var returnValue = new T();
			var metadata = builder.BuildTypeMetadata(typeof(T));
			foreach (var field in metadata.FieldsInOrder)
			{
				Console.WriteLine($"Deserializing {field.Name}");
				if (field.TypeResolution[0].CustomBitSerializable != null)
				{
					var obj2 = DeserializeCustom(reader, ctx, field);
					field.Accessors.Setter(returnValue, obj2);

				}
				else if (field.TypeResolution[0].IsPrimitive)
				{
					ReadAndSetFast(reader, field, returnValue);
				}
				else
				{
					//Type classType = typeof(Serializer);

					//// 2. Fetch the open generic static method definition
					//MethodInfo openMethod = classType.GetMethod(
					//	nameof(ReadEnumerable),
					//	BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
					//)!;

					//// 3. Bind your runtime Type variable to create the concrete method
					//MethodInfo closedMethod = openMethod.MakeGenericMethod(field.UnderlyingType.FieldType);

					//// 4. Invoke it: First argument is null because it is a static method
					////object? list = closedMethod.Invoke(null, [reader, field, 0]);
					List<object> list = ReadEnumerable(reader, ctx, field, 0);
					//if (list is List<object> processingList)
					//{
						var processedList = ProcessNode(field, 0, list);
						field.Accessors.Setter(returnValue, processedList);
					//} else
					//{
					//	throw new SerializationException($"Expected List<object> non-null from ReadEnumerable in {field.Name}");
					//}
				}
				Console.WriteLine();
			}
			return returnValue;
		}

		private static object? DeserializeCustom(BitStorageReader reader, SerializerContext ctx/*, object returnValue*/, FieldMetadata field)
		{
			var openMethod = typeof(Serializer).GetMethod(
				nameof(DeserializeObject),
				BindingFlags.Public | BindingFlags.Static
			);

			var closedMethod = openMethod.MakeGenericMethod(field.UnderlyingType.FieldType);

			return closedMethod.Invoke(null, new object[] { reader, ctx });
			//field.Accessors.Setter(returnValue, obj2);
		}

		private static void ReadAndSetFast(BitStorageReader reader, FieldMetadata fm, object target)
		{
			var t = fm.UnderlyingType.FieldType;
			int bitsRead = 0;
			if (t == typeof(int))
			{
				bitsRead = reader.Read<int>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(uint))
			{
				bitsRead = reader.Read<uint>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(long))
			{
				bitsRead = reader.Read<long>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(ulong))
			{
				bitsRead = reader.Read<ulong>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(bool))
			{
				bitsRead = reader.Read<bool>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(byte))
			{
				bitsRead = reader.Read<byte>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(sbyte))
			{
				bitsRead = reader.Read<sbyte>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(short))
			{
				bitsRead = reader.Read<short>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(ushort))
			{
				bitsRead = reader.Read<ushort>(out var bits, fm.ResolvedBits, fm.UnderlyingType.Signed);
				fm.Accessors.Setter(target, bits);
			}
			else { throw new NotSupportedException(); }

			if(bitsRead != fm.ResolvedBits)
			{
				throw new NotSupportedException();
			}
		}
		/*
object CreateAndPopulate(Type targetType, IReadOnlyList<FieldMetadata> fields, IDictionary<string, object?> values, IServiceProvider? services = null)
{
    // 1. try DI/factory (optional)
    var factory = GetCachedFactoryForType(targetType);
    object instance = factory != null ? factory() : Activator.CreateInstance(targetType, nonPublic: true)!;

    // 2. if constructor binding used, instance already created with values
    // 3. otherwise set properties
    foreach (var fm in fields)
    {
        if (!values.TryGetValue(fm.Name!, out var val)) val = fm.ResolvedDefaultValue;
        fm.Accessors.Setter(instance, val);
    }

    return instance;
}
		 */

		//public static Action<object, BitStorage> CompileSerializer(Type parentObjectType, FieldMetadata field)
		//{
		//	if (field.TypeResolution == null || field.TypeResolution.Length == 0)
		//	{
		//		throw new ArgumentException($"Field '{field.Property?.Name}' must have a populated TypeResolution array configuration.");
		//	}

		//	// 1. Inputs: (object parent, BitStorage storage)
		//	var parentParam = Expression.Parameter(typeof(object), "parent");
		//	var storageParam = Expression.Parameter(typeof(BitStorage), "storage");

		//	// 2. Cast parent and read the root field property
		//	var concreteParent = Expression.Convert(parentParam, parentObjectType);
		//	MethodInfo propertyGetter = field.Property.GetGetMethod(nonPublic: true)
		//		?? throw new InvalidOperationException($"Missing getter on {field.Property.Name}");
		//	var rootValueExpr = Expression.Call(concreteParent, propertyGetter);

		//	// 3. Build the recursive expression loop using the TypeResolution metadata array
		//	Expression recursiveLoopBody = BuildRecursiveLoop(rootValueExpr, 0, storageParam, field);

		//	// 4. Compile into a single master action for this field
		//	var lambda = Expression.Lambda<Action<object, BitStorage>>(recursiveLoopBody, parentParam, storageParam);
		//	return lambda.Compile();
		//}

		//private static Expression BuildRecursiveLoop(
		//	Expression currentValExpr,
		//	int depth,
		//	ParameterExpression storageParam,
		//	FieldMetadata field)
		//{
		//	var currentResolution = field.TypeResolution[depth];
		//	Type currentType = currentResolution.FieldType;
		//	bool isCurrentLayerNullable = currentResolution.IsNullable != false;

		//	var fieldConstant = Expression.Constant(field);
		//	var depthConstant = Expression.Constant(depth);

		//	// --- LAYER-SPECIFIC NULL BRANCH ---
		//	Expression nullBranch;
		//	if (isCurrentLayerNullable)
		//	{
		//		MethodInfo nullHandler = typeof(Serializer).GetMethod(nameof(HandleNullValueAtDepth), BindingFlags.NonPublic | BindingFlags.Static)!;
		//		nullBranch = Expression.Call(null, nullHandler, fieldConstant, depthConstant, storageParam);
		//	}
		//	else
		//	{
		//		nullBranch = Expression.Throw(
		//			Expression.New(
		//				typeof(SerializationException).GetConstructor([ typeof(string) ]) ?? throw new InvalidOperationException("Missing constructor"),
		//				Expression.Constant($"Field '{field.Property?.Name}' at depth {depth} encountered a null value, but this layer is marked as non-nullable.")
		//			)
		//		);
		//	}

		//	// BASE CASE: We reached the primitive leaf node layer (e.g., int, int?, string)
		//	if(depth == field.TypeResolution.Length - 1)
		//	{
		//		Expression activeWriteCall;

		//		//if (currentType == typeof(string) || field.IsString)
		//		//{
		//		//	MethodInfo stringMethod = typeof(BitSerializationCompiler).GetMethod(nameof(ExecuteStringWritePipeline))!;
		//		//	activeWriteCall = Expression.Call(null, stringMethod, currentValExpr, storageParam, fieldConstant);
		//		//}
		//		if (currentType == typeof(bool))
		//		{
		//			MethodInfo boolMethod = typeof(Serializer).GetMethod(nameof(ExecuteBoolWritePipeline))!;
		//			activeWriteCall = Expression.Call(null, boolMethod, currentValExpr, storageParam, fieldConstant);
		//		}
		//		else if (field.UnderlyingType.Signed)
		//		{
		//			// Emit a zero-overhead hardware sign-extend cast (e.g. short/int -> long)
		//			var castToLong = Expression.Convert(currentValExpr, typeof(long));
		//			MethodInfo signedMethod = typeof(Serializer).GetMethod(nameof(ExecuteSignedWritePipeline))!;
		//			activeWriteCall = Expression.Call(null, signedMethod, castToLong, storageParam, fieldConstant, depthConstant);
		//		}
		//		else
		//		{
		//			// Emit a zero-overhead hardware zero-extend cast (e.g. ushort/uint -> ulong)
		//			var castToULong = Expression.Convert(currentValExpr, typeof(ulong));
		//			MethodInfo unsignedMethod = typeof(Serializer).GetMethod(nameof(ExecuteUnsignedWritePipeline))!;
		//			activeWriteCall = Expression.Call(null, unsignedMethod, castToULong, storageParam, fieldConstant, depthConstant);
		//		}

		//		// Rely on your metadata flag to wrap with a null check if the primitive layer is nullable
		//		if (isCurrentLayerNullable)
		//		{
		//			var isNullCondition = Expression.Equal(Expression.Convert(currentValExpr, typeof(object)), Expression.Constant(null));
		//			return Expression.IfThenElse(isNullCondition, nullBranch, activeWriteCall);
		//		}

		//		return activeWriteCall;
		//	}

		//	// RECURSIVE CASE: Generate an unrolling loop over the current collection layer
		//	var enumerableVar = Expression.Variable(typeof(IEnumerable), $"enumerable_d{depth}");
		//	var assignEnumerable = Expression.Assign(enumerableVar, Expression.Convert(currentValExpr, typeof(IEnumerable)));

		//	MethodInfo getEnumeratorMethod = typeof(IEnumerable).GetMethod(nameof(IEnumerable.GetEnumerator));
		//	MethodInfo moveNextMethod = typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext));
		//	PropertyInfo currentProp = typeof(IEnumerator).GetProperty(nameof(IEnumerator.Current));

		//	var enumeratorVar = Expression.Variable(typeof(IEnumerator), $"enumerator_d{depth}");
		//	var assignEnumerator = Expression.Assign(enumeratorVar, Expression.Call(enumerableVar, getEnumeratorMethod));

		//	var loopItemElement = Expression.Variable(typeof(object), $"item_d{depth}");
		//	var assignItem = Expression.Assign(loopItemElement, Expression.Property(enumeratorVar, currentProp));

		//	// Safely extract the exact type signature assigned to the next underlying layer
		//	Type nextDepthType = field.TypeResolution[depth + 1].FieldType;
		//	var stronglyTypedItem = Expression.Convert(loopItemElement, nextDepthType);

		//	// Recursively track down to the next depth tier
		//	var nextLayerExpression = BuildRecursiveLoop(stronglyTypedItem, depth + 1, storageParam, field);

		//	var breakLabel = Expression.Label($"LoopBreak_d{depth}");
		//	var loopBody = Expression.Block(
		//		[loopItemElement],
		//		assignItem,
		//		nextLayerExpression
		//	);

		//	var loopExpression = Expression.Loop(
		//		Expression.IfThenElse(
		//			Expression.Call(enumeratorVar, moveNextMethod),
		//			loopBody,
		//			Expression.Break(breakLabel)
		//		),
		//		breakLabel
		//	);

		//	// Wrap the collection unrolling block inside a layer-specific null checker
		//	var fullLoopBlock = Expression.Block(
		//		[ enumerableVar, enumeratorVar ],
		//		assignEnumerable,
		//		assignEnumerator,
		//		loopExpression
		//	);

		//	var isCollectionNull = Expression.Equal(Expression.Convert(currentValExpr, typeof(object)), Expression.Constant(null));
		//	return Expression.IfThenElse(isCollectionNull, nullBranch, fullLoopBlock);
		//}

		/// <summary>
		/// Generic worker for non-null leaf primitives. Handles int, int?, or string perfectly.
		/// </summary>
		//public static bool ExecuteWritePipeline<T>(T value, BitStorage storage, FieldMetadata field, int currentDepth) where T:struct
		//{
		//	//Type typeOfT = typeof(T);
		//	//Type underlyingType = Nullable.GetUnderlyingType(typeOfT) ?? typeOfT;
		//	var underlyingType = field.TypeResolution[currentDepth].FieldType;
		//	//if (field.IsString || underlyingType == typeof(string))
		//	//{
		//	//	storage.Write(value, field.ResolvedBits);
		//	//	return true;
		//	//}

		//	if (underlyingType != typeof(bool))
		//	{
		//		if (field.UnderlyingType.Signed)
		//		{
		//			long min = field.ResolvedMinSigned;
		//			long max = field.ResolvedMaxSigned;
		//			long valAsSigned = Convert.ToInt64(value);

		//			if (valAsSigned < min || valAsSigned > max)
		//			{
		//				throw new SerializationException($"Field '{field.Property?.Name}' value {value} at depth {currentDepth} is out of signed bounds ({min}..{max}).");
		//			}
		//		}
		//		else
		//		{
		//			ulong min = field.ResolvedMinUnsigned;
		//			ulong max = field.ResolvedMaxUnsigned;
		//			ulong valAsUnsigned = Convert.ToUInt64(value);

		//			if (valAsUnsigned < min || valAsUnsigned > max)
		//			{
		//				throw new SerializationException($"Field '{field.Property?.Name}' value {value} at depth {currentDepth} is out of unsigned bounds ({min}..{max}).");
		//			}
		//		}
		//	}
		//	storage.Write(value, field.ResolvedBits);
		//	return true;
		//}
		//public static void ExecuteSignedWritePipeline(long value, BitStorage storage, FieldMetadata field, int currentDepth)
		//{
		//	long min = field.ResolvedMinSigned;
		//	long max = field.ResolvedMaxSigned;

		//	if (value < min || value > max)
		//	{
		//		throw new SerializationException($"Field '{field.Property.Name}' value {value} at depth {currentDepth} is out of signed bounds ({min}..{max}).");
		//	}
		//	
		//	storage.Write(value, field.ResolvedBits);
		//}

		///// <summary>
		///// Handles all unsigned primitive types (byte, ushort, uint, ulong).
		///// </summary>
		//public static void ExecuteUnsignedWritePipeline(ulong value, BitStorage storage, FieldMetadata field, int currentDepth)
		//{
		//	ulong min = field.ResolvedMinUnsigned;
		//	ulong max = field.ResolvedMaxUnsigned;

		//	if (value < min || value > max)
		//	{
		//		throw new SerializationException($"Field '{field.Property.Name}' value {value} at depth {currentDepth} is out of unsigned bounds ({min}..{max}).");
		//	}
		//	

		//	storage.Write(value, field.ResolvedBits);
		//}

		/// <summary>
		/// Isolated channel specifically for booleans to bypass bounds checks entirely.
		/// </summary>
		//public static void ExecuteBoolWritePipeline(bool value, BitStorage storage, FieldMetadata field)
		//{
		//	storage.Write(value, field.ResolvedBits);
		//}

		/// <summary>
		/// Isolated channel specifically for string fields.
		/// </summary>
		//public static void ExecuteStringWritePipeline(string value, BitStorage storage, FieldMetadata field)
		//{
		//	storage.Write(value, field.ResolvedBits);
		//}
		//private static void HandleNullValueAtDepth(FieldMetadata field, int depth, BitStorage storage)
		//{
		//	// Your custom null indicator logic goes here (e.g., storage.Write(false, 1);)
		//}
		//public static Action<object, BitStorage> CompileSerializer(Type parentObjectType, FieldMetadata field)
		//{
		//	// Extract the field type directly from your existing metadata property configuration
		//	Type fieldType = field.Property.PropertyType;

		//	// 1. Define inputs: (object parent, BitStorage storage)
		//	var parentParam = Expression.Parameter(typeof(object), "parent");
		//	var storageParam = Expression.Parameter(typeof(BitStorage), "storage");

		//	// 2. Cast the parent object to its concrete type (e.g., TestClass)
		//	var concreteParent = Expression.Convert(parentParam, parentObjectType);

		//	// 3. Bind the property getter (nonPublic: true supports private properties)
		//	MethodInfo propertyGetter = field.Property.GetGetMethod(nonPublic: true)
		//		?? throw new InvalidOperationException($"Property {field.Property.Name} on {parentObjectType.Name} is missing a getter.");

		//	// 4. Read the property off the object: parent.get_Xyz()
		//	var readPropertyCall = Expression.Call(concreteParent, propertyGetter);

		//	// 5. Locate and construct our open generic pipeline method definition
		//	Expression<Func<bool>> placeholderExpr = () => ExecuteWritePipeline<int>(default, default, default);
		//	var callExpr = (MethodCallExpression)placeholderExpr.Body;

		//	// 2. Unwraps ExecuteWritePipeline<int> back to the open ExecuteWritePipeline<T>
		//	MethodInfo openPipelineMethod = callExpr.Method.GetGenericMethodDefinition(); 
		//	MethodInfo closedPipelineMethod = openPipelineMethod.MakeGenericMethod(fieldType);

		//	// 6. Generate the call pipeline: BitSerializationCompiler.ExecuteWritePipeline<T>(parent.Property, storage, field)
		//	var fieldInstanceConstant = Expression.Constant(field);
		//	var pipelineCall = Expression.Call(
		//		null, // null because ExecuteWritePipeline is a static method
		//		closedPipelineMethod,
		//		readPropertyCall,
		//		storageParam,
		//		fieldInstanceConstant
		//	);

		//	// 7. Compile and return the action delegate directly
		//	var lambda = Expression.Lambda<Action<object, BitStorage>>(pipelineCall, parentParam, storageParam);
		//	return lambda.Compile();
		//}

		///// <summary>
		///// The generic pipeline runner. All metadata configuration values are read purely from your 'field' instance.
		///// </summary>
		//public static bool ExecuteWritePipeline<T>(T value, BitStorage storage, FieldMetadata field) where T : struct
		//{
		//	// 1. Handle Null/Nullable values safely
		//	//if (value == null)
		//	//{
		//	//	return false;
		//	//}

		//	// 2. Extract underlying type to check for booleans
		//	Type type = typeof(T);
		//	Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

		//	// 3. Skip bounds validation completely for booleans
		//	if (underlyingType != typeof(bool))
		//	{
		//		if (field.ResolvedSigned)
		//		{
		//			long min = field.ResolvedMinSigned ?? 0;
		//			long max = field.ResolvedMaxSigned ?? 0;

		//			long valAsSigned = Convert.ToInt64(value);

		//			if (valAsSigned < min || valAsSigned > max)
		//			{
		//				throw new SerializationException($"Field {field.Property.Name} value {value} is out of signed bounds ({min}..{max}).");
		//			}
		//		}
		//		else
		//		{
		//			ulong min = field.ResolvedMinUnsigned ?? 0;
		//			ulong max = field.ResolvedMaxUnsigned ?? 0;

		//			ulong valAsUnsigned = Convert.ToUInt64(value);

		//			if (valAsUnsigned < min || valAsUnsigned > max)
		//			{
		//				throw new SerializationException($"Field {field.Property.Name} value {value} is out of unsigned bounds ({min}..{max}).");
		//			}
		//		}
		//	}

		//	// 4. Pass raw primitive straight into automatic BitStorage engine
		//	storage.Write(value, field.ResolvedBits);
		//	return true;
		//}

		/*
		public static Action<object, BitStorage> CompileSerializer(Type parentObjectType, FieldMetadata fm)
		{
			Type fieldType = fm.UnderlyingType.FieldType ?? throw new SerializationException($"Unknown type for field {fm.Name}");

			// 1. Define input parameters: (object parent, BitStorage writer)
			var parentParam = Expression.Parameter(typeof(object), "parent");
			var writerParam = Expression.Parameter(typeof(BitStorage), "writer");

			// 2. Cast the generic 'object' parent to its concrete class type
			var concreteParent = Expression.Convert(parentParam, parentObjectType);

			// 3. Secure the get accessor (nonPublic: true handles private/protected)
			MethodInfo propertyGetter = fm.Property.GetGetMethod(nonPublic: true)
				?? throw new InvalidOperationException($"Property {fm.Name} on {parentObjectType.Name} is missing a getter.");

			// 4. Generate the property value read: parent.get_Xyz()
			var readPropertyCall = Expression.Call(concreteParent, propertyGetter);

			// 5. Establish a local variable to hold the primitive value securely
			var localValue = Expression.Variable(fieldType, "fieldValue");
			var assignLocal = Expression.Assign(localValue, readPropertyCall);

			// 6. Build the Bounds Check Expression (Skipped entirely for Booleans)
			Expression boundsCheck = Expression.Empty();
			if (fieldType != typeof(bool))
			{
				// Widen the primitive to a 64-bit integer for boundary checks
				Expression checkValue = fm.ResolvedSigned
					? Expression.Convert(localValue, typeof(long))
					: Expression.Convert(localValue, typeof(ulong));

				boundsCheck = CreateBoundsCheckExpression(localValue, checkValue, fm);
			}

			// 7. Resolve the open generic method definition: public BitStorage Write<T>(T bits, int? bitsToWrite)
			MethodInfo openWriteMethod = typeof(BitStorage).GetMethods()
				.First(m => m.Name == "Write" && m.IsGenericMethod && m.GetParameters().Length == 2);

			// Bind the exact primitive type to create the closed generic method
			MethodInfo closedWriteMethod = openWriteMethod.MakeGenericMethod(fieldType);

			// 8. Wrap the bits requirement into a constant nullable parameter (int?)
			var bitsToWriteExpr = Expression.Constant(fm.ResolvedBits, typeof(int?));

			// 9. Generate the execution call line: writer.Write<T>(localValue, bitsToWriteExpr)
			var writeCall = Expression.Call(writerParam, closedWriteMethod, localValue, bitsToWriteExpr);

			// 10. Package local variables, property reading, bounds checks, and storage sequence
			var block = Expression.Block(
				[localValue],
				assignLocal,
				boundsCheck,
				writeCall
			);

			// 11. Compile directly down to raw, allocation-free machine instructions
			var lambda = Expression.Lambda<Action<object, BitStorage>>(block, parentParam, writerParam);
			return lambda.Compile();
		}

		private static Expression CreateBoundsCheckExpression(Expression rawValue, Expression checkValue, FieldMetadata fm)
		{
			Expression outOfBoundsCondition;
			string expectedRangeMessage;
			BinaryExpression underMin;
			BinaryExpression overMax;
			if (fm.ResolvedSigned)
			{
				long min = fm.ResolvedMinSigned ?? 0;
				long max = fm.ResolvedMaxSigned ?? 0;

				underMin = Expression.LessThan(checkValue, Expression.Constant(min));
				overMax = Expression.GreaterThan(checkValue, Expression.Constant(max));
				expectedRangeMessage = $"({min}..{max})";
			}
			else
			{
				ulong min = fm.ResolvedMinUnsigned ?? 0;
				ulong max = fm.ResolvedMaxUnsigned ?? 0;

				underMin = Expression.LessThan(checkValue, Expression.Constant(min));
				overMax = Expression.GreaterThan(checkValue, Expression.Constant(max));
				expectedRangeMessage = $"({min}..{max})";
			}
			outOfBoundsCondition = Expression.OrElse(underMin, overMax);

			// --- Dynamic String Construction via string.Concat ---
			// Equivalent to: $"Field {Property.Name} value " + rawValue + " is outside the allowed boundaries " + expectedRangeMessage
			var concatMethod = typeof(string).GetMethod("Concat", new[] { typeof(object[]) })
				?? throw new InvalidOperationException("Could not find string.Concat method.");

			var messageArrayElements = new Expression[]
			{
				Expression.Constant($"Field {fm.Name} value "),
				Expression.Convert(rawValue, typeof(object)), // Box raw value ONLY if we are crashing
				Expression.Constant($" is outside the allowed boundaries {expectedRangeMessage}")
			};

			var messageExpression = Expression.Call(concatMethod, Expression.NewArrayInit(typeof(object), messageArrayElements));

			// Create the exception instance: new SerializationException(compiledStringMessage)
			var exceptionConstructor = typeof(SerializationException).GetConstructor(new[] { typeof(string) })
				?? throw new InvalidOperationException("Could not find SerializationException constructor.");

			var throwExpr = Expression.Throw(Expression.New(exceptionConstructor, messageExpression));

			return Expression.IfThen(outOfBoundsCondition, throwExpr);
		}*/
		//public static Action<object, BitStorage> CompileSerializer(Type primitiveType, FieldMetadata fm)
		//{
		//	// 1. Define the input parameters: (object value, BitStorage writer)
		//	var valueParam = Expression.Parameter(typeof(object), "value");
		//	var writerParam = Expression.Parameter(typeof(BitStorage), "writer");

		//	// 2. Unbox the object directly to its exact underlying primitive type (e.g., int, short)
		//	var unboxedValue = Expression.Convert(valueParam, primitiveType);

		//	// 3. Convert that primitive to a long or ulong depending on signedness
		//	Expression convertedValue;
		//	if (fm.ResolvedSigned)
		//	{
		//		convertedValue = Expression.Convert(unboxedValue, typeof(long));
		//	}
		//	else
		//	{
		//		// If it's a signed type being packed as unsigned (like your Xyz example), 
		//		// we cast safely.
		//		convertedValue = Expression.Convert(unboxedValue, typeof(ulong));
		//	}

		//	// 4. Create the Bounds Check Expressions
		//	Expression boundsCheck;
		//	BinaryExpression underMin;
		//	BinaryExpression overMax;
		//	if (fm.ResolvedSigned)
		//	{
		//		long min = fm.ResolvedMinSigned ?? 0;
		//		long max = fm.ResolvedMaxSigned ?? 0;

		//		// valAsSigned < min || valAsSigned > max
		//		underMin = Expression.LessThan(convertedValue, Expression.Constant(min));
		//		overMax = Expression.GreaterThan(convertedValue, Expression.Constant(max));
		//		var outOfBounds = Expression.OrElse(underMin, overMax);

		//		boundsCheck = Expression.IfThen(outOfBounds,
		//			Expression.Throw(Expression.New(typeof(SerializationException).GetConstructor([typeof(string)]),
		//			Expression.Constant("Value out of signed bounds."))));
		//	}
		//	else
		//	{
		//		ulong min = fm.ResolvedMinUnsigned ?? 0;
		//		ulong max = fm.ResolvedMaxUnsigned ?? 0;

		//		underMin = Expression.LessThan(convertedValue, Expression.Constant(min));
		//		overMax = Expression.GreaterThan(convertedValue, Expression.Constant(max));
		//		var outOfBounds = Expression.OrElse(underMin, overMax);

		//		boundsCheck = Expression.IfThen(outOfBounds,
		//			Expression.Throw(Expression.New(typeof(SerializationException).GetConstructor([typeof(string)]),
		//			Expression.Constant("Value out of unsigned bounds."))));
		//	}

		//	// 5. Call your BitWriter method (assuming it looks like WriteBits(long val, int bits))
		//	MethodInfo genericDefinition = typeof(BitStorage).GetMethod(
		//		name: "Write",
		//		genericParameterCount: 1, // Targets the single <T>
		//		types: [Type.MakeGenericMethodParameter(0), typeof(int?)] // Pinpoints T and int?
		//	)
		//		?? throw new InvalidOperationException("Could not find matching Write<T> method.");

		//	MethodInfo genericWriteMethod = genericDefinition.MakeGenericMethod(primitiveType);

		//	var bitsToWriteExpr = Expression.Constant(fm.ResolvedBits, typeof(int?));
		//	var writeCall = Expression.Call(writerParam, genericWriteMethod, unboxedValue, bitsToWriteExpr);

		//	//var writeMethod = typeof(BitStorage).GetMethod(fm.ResolvedSigned ? "WriteSignedBits" : "WriteUnsignedBits");
		//	//var bitSizeConstant = Expression.Constant(fm.ResolvedBits); // e.g., 4 bits
		//	//var writeCall = Expression.Call(writerParam, writeMethod, convertedValue, bitSizeConstant);

		//	// 6. Combine bounds check and write call into a single block
		//	var block = Expression.Block(boundsCheck, writeCall);

		//	// 7. Compile it into a high-performance delegate
		//	var lambda = Expression.Lambda<Action<object, BitStorage>>(block, valueParam, writerParam);
		//	BlockUnpackerVisitor.Print(lambda);
		//	return lambda.Compile();
		//}
		/*
if (elementType == typeof(int))
{
    int v = reader.Read<int>();
    list.Add(v);
}
else if (elementType == typeof(uint))
{
    uint v = reader.Read<uint>();
    list.Add(v);
}
else if (elementType == typeof(long))
{
    long v = reader.Read<long>();
    list.Add(v);
}
else if (elementType == typeof(bool))
{
    bool v = reader.Read<bool>();
    list.Add(v);
}
else if (elementType == typeof(string))
{
    // string special-case
}
else
{
    // fallback to generic MakeGenericMethod or cached invoker
}
		 */
		//private static bool TryGetBigInteger(object? value, out BigInteger result)
		//{
		//	result = default;
		//	if (value is null) return false;

		//	// Unbox common numeric types explicitly to avoid boxing surprises
		//	switch (value)
		//	{
		//		case sbyte sb: result = new BigInteger(sb); return true;
		//		case byte b: result = new BigInteger(b); return true;
		//		case short s: result = new BigInteger(s); return true;
		//		case ushort us: result = new BigInteger(us); return true;
		//		case int i: result = new BigInteger(i); return true;
		//		case uint ui: result = new BigInteger(ui); return true;
		//		case long l: result = new BigInteger(l); return true;
		//		case ulong ul: result = new BigInteger(ul); return true;
		//		case BigInteger bi: result = bi; return true;
		//		case bool bo: result = bo ? BigInteger.One : BigInteger.Zero; return true;
		//		default:
		//			// If value is a boxed nullable numeric, try to convert via Convert.ToInt64/ToUInt64 carefully
		//			var t = value.GetType();
		//			if (t.IsEnum) return false;
		//			try
		//			{
		//				// Try to handle any IConvertible numeric by using Convert.ToDecimal then BigInteger
		//				if (value is IConvertible)
		//				{
		//					var dec = Convert.ToDecimal(value);
		//					result = new BigInteger(dec);
		//					return true;
		//				}
		//			}
		//			catch { /* fall through */ }
		//			return false;
		//	}
		//}
	}
	//public class BlockUnpackerVisitor : ExpressionVisitor
	//{
	//	private int _indentLevel = 0;

	//	public static void Print(Expression expression)
	//	{
	//		var visitor = new BlockUnpackerVisitor();
	//		visitor.Visit(expression);
	//	}

	//	// Intercept and format the root Lambda details
	//	protected override Expression VisitLambda<T>(Expression<T> node)
	//	{
	//		var parameters = string.Join(", ", node.Parameters.Select(p => $"{p.Type.Name} {p.Name}"));
	//		Console.WriteLine($"({parameters}) =>");

	//		Visit(node.Body);
	//		return node;
	//	}

	//	// Intercept the hidden block and unpack the lines inside
	//	protected override Expression VisitBlock(BlockExpression node)
	//	{
	//		string indent = new string(' ', _indentLevel * 4);
	//		Console.WriteLine($"{indent}{{");
	//		_indentLevel++;

	//		// 1. Print local variable declarations if any exist
	//		if (node.Variables.Any())
	//		{
	//			var vars = string.Join(", ", node.Variables.Select(v => $"{v.Type.Name} {v.Name}"));
	//			Console.WriteLine($"{new string(' ', _indentLevel * 4)}// Locals: {vars}");
	//		}

	//		// 2. Sequentially print every statement inside the block
	//		foreach (var expression in node.Expressions)
	//		{
	//			Console.Write(new string(' ', _indentLevel * 4));

	//			if (expression is BinaryExpression binary && binary.NodeType == ExpressionType.Assign)
	//			{
	//				// Format assignments cleanly (e.g., result = num * 2)
	//				Console.WriteLine($"{binary.Left} = {binary.Right};");
	//			}
	//			else
	//			{
	//				// Fallback for standalone values or final return expressions
	//				Console.WriteLine($"{expression};");
	//			}
	//		}

	//		_indentLevel--;
	//		Console.WriteLine($"{indent}}}");
	//		return node;
	//	}
	//}
}
