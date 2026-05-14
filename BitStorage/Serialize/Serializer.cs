using GgoSoft.Storage;
using System;
using System.Linq;
using System.Numerics;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// High-level entry point for serialization and deserialization operations.
	/// This static class orchestrates choosing metadata-driven serialization, converters, or
	/// calling <see cref="ICustomBitSerializable"/> when present.
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
		public static void SerializeObject(object obj, BitStorage storage, SerializerContext ctx)
		{
			var builder = new FieldMetadataBuilder(ctx.Options);
			var metadata = builder.BuildTypeMetadata(obj.GetType());
			foreach (var field in metadata.FieldsInOrder)
			{
				if(field.TypeResolution.IsPrimitive)
				{
					var value = field.Accessors.Getter(obj);
					if (TryGetBigInteger(value, out var bigInt))
					{
						if (bigInt < field.ResolvedMin)
						{
							throw new InvalidOperationException($"Field {field.Name} value {bigInt} is less than the minimum allowed {field.ResolvedMin}.");
						}
						else if (bigInt > field.ResolvedMax)
						{
							throw new InvalidOperationException($"Field {field.Name} value {bigInt} is greater than the maximum allowed {field.ResolvedMax}.");
						}
						else
						{
							// Here we would write bigInt to storage using the appropriate number of bits
							// based on field.TypeResolution.ResolvedBits, but since this is a placeholder, we'll just comment it.
							if (bigInt >= 0)
							{
								storage.Write((ulong)bigInt, field.ResolvedBits);
							} else
							{
								storage.Write((long)bigInt, field.ResolvedBits);
							}
						}
					}
					else
					{
						throw new InvalidOperationException($"Field {field.Name} is marked as primitive but its value cannot be converted to BigInteger for serialization.");
					}
				}
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
				if (field.TypeResolution.IsPrimitive)
				{
					ReadAndSetFast(reader, field, returnValue);
				}
			}
			return returnValue;
		}

		private static void ReadAndSetFast(BitStorageReader reader, FieldMetadata fm, object target)
		{
			var t = fm.FieldType;// Nullable.GetUnderlyingType(fm.Property!.PropertyType) ?? fm.Property.PropertyType;
			int bitsRead = 0;
			if (t == typeof(int))
			{
				bitsRead = reader.Read<int>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(uint))
			{
				bitsRead = reader.Read<uint>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(long))
			{
				bitsRead = reader.Read<long>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(ulong))
			{
				bitsRead = reader.Read<ulong>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(bool))
			{
				bitsRead = reader.Read<bool>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(byte))
			{
				bitsRead = reader.Read<byte>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(sbyte))
			{
				bitsRead = reader.Read<sbyte>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(short))
			{
				bitsRead = reader.Read<short>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else if (t == typeof(ushort))
			{
				bitsRead = reader.Read<ushort>(out var bits, fm.ResolvedBits, fm.ResolvedSigned);
				fm.Accessors.Setter(target, bits);
			}
			else { throw new NotSupportedException(); }

			if(bitsRead != fm.ResolvedBits)
			{
				throw new NotSupportedException();
			}
			// add other supported primitives...
			// fallback to reflection-based generic invocation
//			ReadAndSetValue(reader, fm, target);
		}

		private static bool TryGetBigInteger(object? value, out BigInteger result)
		{
			result = default;
			if (value is null) return false;

			// Unbox common numeric types explicitly to avoid boxing surprises
			switch (value)
			{
				case sbyte sb: result = new BigInteger(sb); return true;
				case byte b: result = new BigInteger(b); return true;
				case short s: result = new BigInteger(s); return true;
				case ushort us: result = new BigInteger(us); return true;
				case int i: result = new BigInteger(i); return true;
				case uint ui: result = new BigInteger(ui); return true;
				case long l: result = new BigInteger(l); return true;
				case ulong ul: result = new BigInteger(ul); return true;
				case BigInteger bi: result = bi; return true;
				case bool bo: result = bo ? BigInteger.One : BigInteger.Zero; return true;
				default:
					// If value is a boxed nullable numeric, try to convert via Convert.ToInt64/ToUInt64 carefully
					var t = value.GetType();
					if (t.IsEnum) return false;
					try
					{
						// Try to handle any IConvertible numeric by using Convert.ToDecimal then BigInteger
						if (value is IConvertible)
						{
							var dec = Convert.ToDecimal(value);
							result = new BigInteger(dec);
							return true;
						}
					}
					catch { /* fall through */ }
					return false;
			}
		}
	}
}
