using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GgoSoft.Storage
{
	public class BitStorageData
	{
		private long _signedData;
		private ulong _unSignedData;
		private bool _signed;
		private Type _originalType;
		private BitStorageData(long signedData, ulong unSignedData, bool signed)
		{
			_signedData = signedData;
			_unSignedData = unSignedData;
			_signed = signed;
		}
		// this may have implicit converters, but I don't think it's required, there's plenty of other methods to work with
	}
}
