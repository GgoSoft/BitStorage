using GgoSoft.Storage;
using System;
using System.Linq;

namespace GgoSoft.Serialize
{
	/// <summary>
	/// Optional custom serializer interface for types that want full control.
	/// </summary>
	public interface ICustomBitSerializable
	{
		void Serialize(BitStorage writer, SerializerContext context);
		void Deserialize(BitStorageReader reader, SerializerContext context);
	}
}
