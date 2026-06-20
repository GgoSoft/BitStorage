using System;

namespace GgoSoft.Storage
{
	public static class Helpers
	{
		public static bool TryGetNativePrimitiveInfo(Type t, out int width, out bool signed)
		{
			switch (Type.GetTypeCode(t))
			{
				case TypeCode.Boolean:
					width = 1; signed = false; return true;

				case TypeCode.Char:
				case TypeCode.UInt16:
					width = 16; signed = false; return true;

				case TypeCode.Byte:
					width = 8; signed = false; return true;

				case TypeCode.SByte:
					width = 8; signed = true; return true;

				case TypeCode.Int16:
					width = 16; signed = true; return true;

				case TypeCode.Int32:
					width = 32; signed = true; return true;

				case TypeCode.UInt32:
					width = 32; signed = false; return true;

				case TypeCode.Int64:
					width = 64; signed = true; return true;

				case TypeCode.UInt64:
					width = 64; signed = false; return true;
			}

			width = default;
			signed = default;
			return false;
		}
		public static bool TryParseLargeNumberToBitwiseLong(string input, out long result)
		{
			// ulong handles the entire range from 0 up to ulong.MaxValue
			if (ulong.TryParse(input, out ulong ulongValue))
			{
				// unchecked allows the bitwise conversion even if it exceeds long.MaxValue
				result = unchecked((long)ulongValue);
				return true;
			}

			// If it fails ulong parsing, check if it's a valid negative standard long
			if (long.TryParse(input, out result))
			{
				return true;
			}

			result = 0;
			return false;
		}
	}
}
