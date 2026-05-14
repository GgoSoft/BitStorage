using GgoSoft.Storage;
using System;
using System.Linq;

namespace GgoSoft.Serialize
{
	public interface IBitConverter
	{
		void Write(object? value, BitStorage writer, SerializerContext ctx);
		object? Read(BitStorageReader reader, SerializerContext ctx, Type targetType);
	}
}
