		/// <summary>
		/// Creates a copy of the current <see cref="BitStorage"/>, inserts the specified <see cref="BitStorage"/> at the given index and
		/// returns the new <see cref="BitStorage"/> containing the result.
		/// </summary>
		/// <remarks>The method does not modify the current <see cref="BitStorage"/> instance. Instead, it creates and
		/// returns  a new <see cref="BitStorage"/> with the specified bits inserted. The order of bits in the resulting  <see
		/// cref="BitStorage"/> is preserved, with the bits from the specified <paramref name="bits"/> inserted  at the
		/// specified index.</remarks>
		/// <param name="index">The zero-based index at which the specified <see cref="BitStorage"/> will be inserted.  Must be between 0 and <see
		/// cref="Count"/>, inclusive.</param>
		/// <param name="bits">The <see cref="BitStorage"/> to insert. Cannot be <see langword="null"/>.</param>
		/// <returns>A new <see cref="BitStorage"/> containing the bits from the current instance, with the specified  <see
		/// cref="BitStorage"/> inserted at the specified index.</returns>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="bits"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is less than 0 or greater than <see cref="Count"/>.</exception>
		[Pure]
		public BitStorage InsertAsCopy(int index, BitStorage bits)
		{
			if (bits is null) throw new ArgumentNullException(nameof(bits));
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");

			var reader = CreateReader();

			// Create result
			var result = new BitStorage();

			// starting from the beginning, read up to the index
			reader.ReadIndex = 0;
			var prefix = reader.ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// write the bits to be inserted
			result.Write(bits);

			// read the rest of the bits from the original storage
			var suffix = reader.ReadEnumerable<int>();

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, reader.LastReadBitCount);
			}
			return result;
		}

		/// <summary>
		/// Creates a copy of the current <see cref="BitStorage"/>, inserts the specified bits at the given index and
		/// returns the new <see cref="BitStorage"/> containing the result.
		/// </summary>
		/// <remarks>The method does not modify the current <see cref="BitStorage"/> instance. Instead, it creates and
		/// returns  a new <see cref="BitStorage"/> with the specified bits inserted. The order of bits in the resulting  <see
		/// cref="BitStorage"/> is preserved, with the bits from the specified <paramref name="bits"/> inserted  at the
		/// specified index.</remarks>
		/// <typeparam name="T">The value type containing the bits to insert.</typeparam>
		/// <param name="index">The zero-based index at which to insert the bits.</param>
		/// <param name="bits">The value containing the bits to be inserted.</param>
		/// <param name="bitsToWrite">The number of bits to write from the value. If null, all bits of the value are written.</param>
		/// <returns>A new BitStorage instance with the specified bits inserted at the given index.</returns>
		[Pure]
		public BitStorage InsertAsCopy<T>(int index, T bits, int? bitsToWrite = null) where T : struct
		{
			var storage = new BitStorage();
			storage.Write(bits, bitsToWrite);
			return InsertAsCopy(index, storage);
		}

		/// <summary>
		/// Removes a range of bits from the current storage and returns the current BitStorage instance.
		/// </summary>
		/// <param name="index">The zero-based starting position of the range to remove. Must be between 0 and Count, inclusive.</param>
		/// <param name="count">The number of bits to remove. Must be greater than or equal to 0, and the range defined by index and count must
		/// not exceed the total number of bits.</param>
		/// <returns>The currentBitStorage instance containing all existing bits except for those in the specified range.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when index or count is less than 0, index is greater than Count, or the range defined by index and count
		/// exceeds the total number of bits in the storage.</exception>
		public BitStorage RemoveRange(int index, int count)
		{
			var bitStorage = RemoveRangeAsCopy(index, count);
			var currentWriteIndex = WriteIndex;
			this.data = bitStorage.data.Clone();
			Count = bitStorage.Count;
			if (currentWriteIndex >= index)
			{
				if (currentWriteIndex < index + count)
				{
					WriteIndex = index;
				}
				else
				{
					WriteIndex = currentWriteIndex - count;
				}
			}
			return this;
		}

		/// <summary>
		/// Removes a specified number of bits from the end of the bit storage and returns the current BitStorage instance.
		/// </summary>
		/// <param name="count">The number of bits to remove from the end. Must be greater than or equal to 0 and less than or equal to the
		/// current bit count.</param>
		/// <returns>The current BitStorage instance with the specified number of bits removed from the end.</returns>
		public BitStorage TrimEnd(int count)
		{
			// Remove "count" bits from the end of the storage
			return RemoveRange(Count - count, count);
		}

		/// <summary>
		/// Creates a new BitStorage object, removes a range of bits and returns the new BitStorage instance with the specified range
		/// excluded.
		/// </summary>
		/// <remarks>The original BitStorage instance remains unchanged. The returned BitStorage contains a copy of
		/// the bits with the specified range removed.</remarks>
		/// <param name="index">The zero-based starting position of the range to remove. Must be between 0 and Count, inclusive.</param>
		/// <param name="count">The number of bits to remove. Must be greater than or equal to 0, and the range defined by index and count must
		/// not exceed the total number of bits.</param>
		/// <returns>A new BitStorage instance containing all bits from the original storage except for those in the specified range.</returns>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when index or count is less than 0, index is greater than Count, or the range defined by index and count
		/// exceeds the total number of bits in the storage.</exception>
		[Pure]
		public BitStorage RemoveRangeAsCopy(int index, int count)
		{
			if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Cannot be less than 0");
			if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be between 0 and {Count}");
			if (index + count > Count) throw new ArgumentOutOfRangeException(nameof(count), $"Range {index + count} exceeds storage length {Count}");

			var reader = CreateReader();

			// Create result
			BitStorage result;

			// if removing from the end, just create a new storage with the count reduced
			if (count + index == Count)
			{
				result = new(this);
				result.Count -= count;
				result.WriteIndex = result.Count;
				return result;
			}

			result = new BitStorage();
			// starting from the beginning, read up to the index
			reader.ReadIndex = 0;
			var prefix = reader.ReadEnumerable<int>(index);

			// write the prefix bits to the result
			foreach (var b in prefix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// skip the range to be removed
			reader.ReadIndex += count;

			// read the rest of the bits from the original storage
			var suffixLength = Count - (index + count);
			var suffix = reader.ReadEnumerable<int>(suffixLength);

			// write the suffix bits to the result
			foreach (var b in suffix)
			{
				result.Write(b, reader.LastReadBitCount);
			}

			// set the result write index to 0
			result.WriteIndex = result.Count;
			return result;
		}
		/// <summary>
		/// Creates a new BitStorage object, removes a specified number of bits from the end, and returns the new BitStorage 
		/// instance with the specified bits excluded.
		/// </summary>
		/// <remarks>The original BitStorage instance remains unchanged. The returned BitStorage contains a copy of
		/// the bits with the specified range removed.</remarks>
		/// <param name="count">The number of bits to remove from the end. Must be greater than or equal to 0 and less than or equal to the
		/// current bit count.</param>
		/// <returns>A new BitStorage instance with the specified number of bits removed from the end.</returns>
		[Pure]
		public BitStorage TrimEndAsCopy(int count)
		{
			// Remove "count" bits from the end of the storage
			return RemoveRangeAsCopy(Count - count, count);
		}

		[ExcludeFromCodeCoverage]
		public string PrintBits(int start = 0, int? end = null)
		{
			int byteStart = start / StorageElementLength;
			end ??= Count;
			int byteEnd = (end.Value + StorageElementLength - 1) / StorageElementLength;
			System.Text.StringBuilder sb = new();
			System.Text.StringBuilder footer = new();
			for (int i = byteStart; i < byteEnd; i++)
			{
				var padded = Convert.ToString(data[i], 2).PadLeft(StorageElementLength, '0');
				var extraBits = Count % StorageElementLength;
				var elementData = data[i];
				if (i == byteEnd - 1 && extraBits > 0)
				{
					var mask = (byte)(GetMask(8) & ~GetMask(StorageElementLength - extraBits));
					elementData &= mask;
					extraBits = StorageElementLength - extraBits;
					padded = $"{padded[..^extraBits]}{new string('.', extraBits)}";
				}
				footer.Append($"{padded} ");
				sb.Append($"{padded}\t{elementData}{Environment.NewLine}");
			}

			sb.Append(footer.ToString().Trim(' ', '.'));
			Console.WriteLine(sb.ToString());
			return sb.ToString();
		}

		/// <summary>
		/// Determines whether the current BitStorage instance is equal to another object.
		/// </summary>
		/// <remarks>Equality is determined by comparing the number of bits and the values of all bits in both
		/// instances. Any unused bits in the underlying storage are ignored during the comparison. This method provides
		/// value-based equality rather than reference equality.</remarks>
		/// <param name="obj">The object to compare with the current BitStorage instance.</param>
		/// <returns>true if the specified object is a BitStorage instance with the same number of bits and identical bit values;
		/// otherwise, false.</returns>
		public bool ContentEquals(BitStorage? other)
		{
			// Quick check for null or different counts
			if (other is null || other.Count != Count)
			{
				return false;
			}

			// quick check for empty storage
			if (Count == 0)
			{
				return true;
			}

			// There could be stuff in the last storage element that is not part of the data.  RemoveRange may just change the Count
			// and not clear the remaining data, so we need to only compare the bits that are part of the data.
			int fullElements = Count / StorageElementLength;
			int remainingBits = Count % StorageElementLength;

			var originalDataSpan = data.AsSpan();
			var otherDataSpan = other.data.AsSpan();
			int totalElements = remainingBits == 0 ? fullElements : fullElements + 1;
			

			// If the last storage element is the same, we can check all of the data
			if (other.data[totalElements - 1] == data[totalElements - 1])
			{
				return originalDataSpan[0..totalElements].SequenceEqual(otherDataSpan[0..totalElements]);
			}

			// If the last storage element is not the same and there are no remaining bits, they are not equal
			if (remainingBits == 0)
			{
				return false;
			}

			// The last storage element isn't the same, but it may be that the bits that are part of the data are the same
			if (data[fullElements] >> (StorageElementLength - remainingBits) != (other.data[fullElements] >> (StorageElementLength - remainingBits)))
			{
				return false;
			}

			// The last part of the data is the same, so check all of the full elements, this *should* be faster than checking each byte
			return originalDataSpan[0..fullElements].SequenceEqual(otherDataSpan[0..fullElements]);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsNegative<T>(T value) where T : struct
		{
			if (typeof(T) == typeof(sbyte))
				return (sbyte)(object)value < 0;
			if (typeof(T) == typeof(short))
				return (short)(object)value < 0;
			if (typeof(T) == typeof(int))
				return (int)(object)value < 0;
			if (typeof(T) == typeof(long))
				return (long)(object)value < 0;

			// All other supported types are unsigned or non-negative by definition
			return false;
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ulong ToUInt64<T>(T value) where T : struct
		{
			if (typeof(T) == typeof(byte))
				return (byte)(object)value;
			if (typeof(T) == typeof(ushort))
				return (ushort)(object)value;
			if (typeof(T) == typeof(uint))
				return (uint)(object)value;
			if (typeof(T) == typeof(ulong))
				return (ulong)(object)value;
			if (typeof(T) == typeof(char))
				return (char)(object)value;

			// signed types (you already enforce non-negative)
			if (typeof(T) == typeof(sbyte))
				return (ulong)(sbyte)(object)value;
			if (typeof(T) == typeof(short))
				return (ulong)(short)(object)value;
			if (typeof(T) == typeof(int))
				return (ulong)(int)(object)value;
			if (typeof(T) == typeof(long))
				return (ulong)(long)(object)value;

			throw new NotSupportedException($"Type {typeof(T)} is not supported.");
		}

	}
}
