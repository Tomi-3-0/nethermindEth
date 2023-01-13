// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nethermind.Core.Extensions;

namespace Nethermind.Core.Crypto
{
    public unsafe struct ValueKeccak
    {
        internal const int Size = 32;
        public fixed byte Bytes[Size];

        public Span<byte> BytesAsSpan => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref this, 1));

        /// <returns>
        ///     <string>0xc5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470</string>
        /// </returns>
        public static readonly ValueKeccak OfAnEmptyString = InternalCompute(new byte[] { });

        public ValueKeccak()
        {
        }

        public ValueKeccak(byte[] data)
        {
            data.CopyTo(BytesAsSpan);
        }

        [DebuggerStepThrough]
        public static ValueKeccak Compute(ReadOnlySpan<byte> input)
        {
            if (input.Length == 0)
            {
                return OfAnEmptyString;
            }

            ValueKeccak result = new();
            byte* ptr = result.Bytes;
            Span<byte> output = new(ptr, KeccakHash.HASH_SIZE);
            KeccakHash.ComputeHashBytesToSpan(input, output);
            return result;
        }

        private static ValueKeccak InternalCompute(byte[] input)
        {
            ValueKeccak result = new();
            byte* ptr = result.Bytes;
            Span<byte> output = new(ptr, KeccakHash.HASH_SIZE);
            KeccakHash.ComputeHashBytesToSpan(input, output);
            return result;
        }
    }

    [DebuggerStepThrough]
    public class Keccak : IEquatable<Keccak>, IComparable<Keccak>
    {
        public const int Size = 32;

        public const int MemorySize =
            MemorySizes.SmallObjectOverhead +
            MemorySizes.RefSize +
            MemorySizes.ArrayOverhead +
            Size -
            MemorySizes.SmallObjectFreeDataSize;

        /// <returns>
        ///     <string>0xc5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470</string>
        /// </returns>
        public static readonly Keccak OfAnEmptyString = InternalCompute(new byte[] { });

        /// <returns>
        ///     <string>0x1dcc4de8dec75d7aab85b567b6ccd41ad312451b948a7413f0a142fd40d49347</string>
        /// </returns>
        public static readonly Keccak OfAnEmptySequenceRlp = InternalCompute(new byte[] { 192 });

        /// <summary>
        ///     0x56e81f171bcc55a6ff8345e692c0f86e5b48e01b996cadc001622fb5e363b421
        /// </summary>
        public static Keccak EmptyTreeHash = InternalCompute(new byte[] { 128 });

        /// <returns>
        ///     <string>0x0000000000000000000000000000000000000000000000000000000000000000</string>
        /// </returns>
        public static Keccak Zero { get; } = new(new byte[Size]);

        /// <summary>
        ///     <string>0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff</string>
        /// </summary>
        public static Keccak MaxValue { get; } = new("0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

        public byte[] Bytes => _innerKeccak.BytesAsSpan.ToArray();

        public Span<byte> BytesAsSpan => _innerKeccak.BytesAsSpan;

        public ValueKeccak _innerKeccak;

        public Keccak(string hexString)
            : this(Core.Extensions.Bytes.FromHexString(hexString)) { }

        public Keccak(byte[] bytes)
        {
            if (bytes.Length != Size)
            {
                throw new ArgumentException($"{nameof(Keccak)} must be {Size} bytes and was {bytes.Length} bytes", nameof(bytes));
            }

            _innerKeccak = new ValueKeccak(bytes);
        }

        public override string ToString()
        {
            return ToString(true);
        }

        public string ToShortString(bool withZeroX = true)
        {
            string hash = BytesAsSpan.ToHexString(withZeroX);
            return $"{hash.Substring(0, withZeroX ? 8 : 6)}...{hash.Substring(hash.Length - 6)}";
        }

        public string ToString(bool withZeroX)
        {
            return BytesAsSpan.ToHexString(withZeroX);
        }

        [DebuggerStepThrough]
        public static Keccak Compute(byte[]? input)
        {
            if (input is null || input.Length == 0)
            {
                return OfAnEmptyString;
            }

            return new Keccak(KeccakHash.ComputeHashBytes(input));
        }

        [DebuggerStepThrough]
        public static Keccak Compute(ReadOnlySpan<byte> input)
        {
            if (input.Length == 0)
            {
                return OfAnEmptyString;
            }

            return new Keccak(KeccakHash.ComputeHashBytes(input));
        }

        private static Keccak InternalCompute(byte[] input)
        {
            return new(KeccakHash.ComputeHashBytes(input.AsSpan()));
        }

        [DebuggerStepThrough]
        public static Keccak Compute(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return OfAnEmptyString;
            }

            return InternalCompute(System.Text.Encoding.UTF8.GetBytes(input));
        }

        public bool Equals(Keccak? other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Core.Extensions.Bytes.AreEqual(other.BytesAsSpan, BytesAsSpan);
        }

        public int CompareTo(Keccak? other)
        {
            if (other == null) return -1;
            return Extensions.Bytes.Comparer.Compare(BytesAsSpan, other!.BytesAsSpan);
        }

        public override bool Equals(object? obj)
        {
            return obj?.GetType() == typeof(Keccak) && Equals((Keccak)obj);
        }

        public override int GetHashCode()
        {
            return MemoryMarshal.Read<int>(BytesAsSpan);
        }

        public static bool operator ==(Keccak? a, Keccak? b)
        {
            if (ReferenceEquals(a, null))
            {
                return ReferenceEquals(b, null);
            }

            if (ReferenceEquals(b, null))
            {
                return false;
            }

            return Core.Extensions.Bytes.AreEqual(a.BytesAsSpan, b.BytesAsSpan);
        }

        public static bool operator !=(Keccak? a, Keccak? b)
        {
            return !(a == b);
        }

        public static bool operator >(Keccak? k1, Keccak? k2)
        {
            if (k1 == null || k2 == null)
            {
                return (k1 is null) && (k2 is not null);
            }
            return Extensions.Bytes.Comparer.Compare(k1.BytesAsSpan, k2.BytesAsSpan) > 0;
        }

        public static bool operator <(Keccak? k1, Keccak? k2)
        {
            if (k1 == null || k2 == null)
            {
                return (k1 is not null) && (k2 is null);
            }
            return Extensions.Bytes.Comparer.Compare(k1.BytesAsSpan, k2.BytesAsSpan) < 0;
        }

        public static bool operator >=(Keccak? k1, Keccak? k2)
        {
            if (k1 == null || k2 == null)
            {
                return (k2 is not null);
            }
            return Extensions.Bytes.Comparer.Compare(k1.BytesAsSpan, k2.BytesAsSpan) >= 0;
        }

        public static bool operator <=(Keccak? k1, Keccak? k2)
        {
            if (k1 == null || k2 == null)
            {
                return (k1 is not null);
            }
            return Extensions.Bytes.Comparer.Compare(k1.BytesAsSpan, k2.BytesAsSpan) <= 0;
        }

        public KeccakStructRef ToStructRef() => new(BytesAsSpan);
    }

    public ref struct KeccakStructRef
    {
        public const int Size = 32;

        public int MemorySize => MemorySizes.ArrayOverhead + Size;

        public Span<byte> Bytes { get; }

        public KeccakStructRef(Span<byte> bytes)
        {
            if (bytes.Length != Size)
            {
                throw new ArgumentException($"{nameof(Keccak)} must be {Size} bytes and was {bytes.Length} bytes", nameof(bytes));
            }

            Bytes = bytes;
        }

        public override string ToString()
        {
            return ToString(true);
        }

        public string ToShortString(bool withZeroX = true)
        {
            string hash = Bytes.ToHexString(withZeroX);
            return $"{hash.Substring(0, withZeroX ? 8 : 6)}...{hash.Substring(hash.Length - 6)}";
        }

        public string ToString(bool withZeroX)
        {
            return Bytes.ToHexString(withZeroX);
        }

        [DebuggerStepThrough]
        public static KeccakStructRef Compute(byte[]? input)
        {
            if (input is null || input.Length == 0)
            {
                return new KeccakStructRef(Keccak.OfAnEmptyString.Bytes);
            }

            var result = new KeccakStructRef();
            KeccakHash.ComputeHashBytesToSpan(input, result.Bytes);
            return result;
        }

        [DebuggerStepThrough]
        public static KeccakStructRef Compute(Span<byte> input)
        {
            if (input.Length == 0)
            {
                return new KeccakStructRef(Keccak.OfAnEmptyString.Bytes);
            }

            var result = new KeccakStructRef();
            KeccakHash.ComputeHashBytesToSpan(input, result.Bytes);
            return result;
        }

        private static KeccakStructRef InternalCompute(Span<byte> input)
        {
            var result = new KeccakStructRef();
            KeccakHash.ComputeHashBytesToSpan(input, result.Bytes);
            return result;
        }

        [DebuggerStepThrough]
        public static KeccakStructRef Compute(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new KeccakStructRef(Keccak.OfAnEmptyString.Bytes);
            }

            var result = new KeccakStructRef();
            KeccakHash.ComputeHashBytesToSpan(System.Text.Encoding.UTF8.GetBytes(input), result.Bytes);
            return result;
        }

        public bool Equals(Keccak? other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return Core.Extensions.Bytes.AreEqual(other.Bytes, Bytes);
        }

        public bool Equals(KeccakStructRef other) => Core.Extensions.Bytes.AreEqual(other.Bytes, Bytes);

        public override bool Equals(object? obj)
        {
            return obj?.GetType() == typeof(Keccak) && Equals((Keccak)obj);
        }

        public override int GetHashCode()
        {
            return MemoryMarshal.Read<int>(Bytes);
        }

        public static bool operator ==(KeccakStructRef a, Keccak? b)
        {
            if (ReferenceEquals(b, null))
            {
                return false;
            }

            return Core.Extensions.Bytes.AreEqual(a.Bytes, b.Bytes);
        }

        public static bool operator ==(Keccak? a, KeccakStructRef b)
        {
            if (ReferenceEquals(a, null))
            {
                return false;
            }

            return Core.Extensions.Bytes.AreEqual(a.Bytes, b.Bytes);
        }

        public static bool operator ==(KeccakStructRef a, KeccakStructRef b)
        {
            return Core.Extensions.Bytes.AreEqual(a.Bytes, b.Bytes);
        }

        public static bool operator !=(KeccakStructRef a, Keccak b)
        {
            return !(a == b);
        }

        public static bool operator !=(Keccak a, KeccakStructRef b)
        {
            return !(a == b);
        }

        public static bool operator !=(KeccakStructRef a, KeccakStructRef b)
        {
            return !(a == b);
        }

        public Keccak ToKeccak() => new(Bytes.ToArray());
    }
}
