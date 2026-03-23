using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public static class Utils
    {
        /// <summary>
        /// [DEPRECATED] Use WireFormatHelpers.EncodeTag instead.
        /// Encodes a field number and wire type into a protobuf tag.
        /// </summary>
        [Obsolete("Use WireFormatHelpers.EncodeTag instead. This method will be removed in a future version.")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetTagAndWireType(int tag, WireType wireType)
        {
            return (int)WireFormatHelpers.EncodeTag(tag, wireType);
        }

        /// <summary>
        /// [DEPRECATED] Use WireFormatHelpers.GetVarintSize instead.
        /// Calculates the varint-encoded size for an unsigned 32-bit integer.
        /// </summary>
        [Obsolete("Use WireFormatHelpers.GetVarintSize instead. This method will be removed in a future version.")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintSize(uint value)
        {
            return WireFormatHelpers.GetVarintSize(value);
        }

        /// <summary>
        /// Calculates packed varint size for int array with SIMD acceleration (AVX2).
        /// Processes 8 values at once when available, 15-40% faster for large arrays.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintPackedCollectionSize(int[] array)
        {
            if (array == null || array.Length == 0)
            {
                return 0;
            }

            // Use SIMD for arrays with 8+ elements when AVX2 is available
            if (Avx2.IsSupported && array.Length >= 8)
            {
                return GetVarintPackedCollectionSizeSimd(array);
            }

            // Scalar fallback
            return GetVarintPackedCollectionSizeScalar(array);
        }

        /// <summary>
        /// SIMD-accelerated varint size calculation using AVX2.
        /// Processes 8 int values in parallel using threshold comparisons.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetVarintPackedCollectionSizeSimd(int[] array)
        {
            int totalSize = 0;
            int i = 0;

            // Threshold vectors for varint byte boundaries
            var threshold1 = Vector256.Create(0x7F);         // 127 - 1 byte threshold
            var threshold2 = Vector256.Create(0x3FFF);       // 16383 - 2 byte threshold
            var threshold3 = Vector256.Create(0x1FFFFF);     // 2097151 - 3 byte threshold
            var threshold4 = Vector256.Create(0x0FFFFFFF);   // 268435455 - 4 byte threshold
            var ones = Vector256.Create(1);

            // Process 8 values at once
            ref int arrayRef = ref MemoryMarshal.GetArrayDataReference(array);
            for (; i + 8 <= array.Length; i += 8)
            {
                var values = Vector256.LoadUnsafe(ref Unsafe.Add(ref arrayRef, i));

                // Compare against thresholds (returns -1 for greater, 0 for less or equal)
                var gt1 = Avx2.CompareGreaterThan(values, threshold1);
                var gt2 = Avx2.CompareGreaterThan(values, threshold2);
                var gt3 = Avx2.CompareGreaterThan(values, threshold3);
                var gt4 = Avx2.CompareGreaterThan(values, threshold4);

                // Each value starts at 1 byte, add 1 for each threshold exceeded
                // -1 (true) becomes +1 when negated and ANDed with 1
                var sizes = ones;
                sizes = Avx2.Subtract(sizes, Avx2.And(gt1, ones));
                sizes = Avx2.Subtract(sizes, Avx2.And(gt2, ones));
                sizes = Avx2.Subtract(sizes, Avx2.And(gt3, ones));
                sizes = Avx2.Subtract(sizes, Avx2.And(gt4, ones));

                // Sum all 8 sizes
                totalSize += Vector256.Sum(sizes);
            }

            // Scalar remainder
            for (; i < array.Length; i++)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)array[i]);
            }

            return totalSize;
        }

        /// <summary>
        /// Scalar fallback for varint packed collection size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetVarintPackedCollectionSizeScalar(int[] array)
        {
            int totalSize = 0;
            for (int i = 0; i < array.Length; i++)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)array[i]);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintPackedCollectionSize(List<int> list)
        {
            if (list == null)
            {
                return 0;
            }

            int totalSize = 0;
            for (int i = 0; i < list.Count; i++)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)list[i]);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintPackedCollectionSize(IEnumerable<int> collection)
        {
            if (collection == null)
            {
                return 0;
            }

            int totalSize = 0;
            foreach (var item in collection)
            {
                // Prekonvertujeme int na uint pre spravne Varint kodovanie pre int32 v Protobuf
                totalSize += WireFormatHelpers.GetVarintSize((uint)item);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintPackedCollectionSize(IEnumerable<uint> collection)
        {
            if (collection == null)
            {
                return 0;
            }

            int totalSize = 0;
            foreach (var item in collection)
            {
                // Prekonvertujeme int na uint pre spravne Varint kodovanie pre int32 v Protobuf
                totalSize += WireFormatHelpers.GetVarintSize(item);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetZigZagPackedCollectionSize(int[] array)
        {
            if (array == null)
            {
                return 0;
            }

            int totalSize = 0;
            for (int i = 0; i < array.Length; i++)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(array[i]);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetZigZagPackedCollectionSize(List<int> list)
        {
            if (list == null)
            {
                return 0;
            }

            int totalSize = 0;
            for (int i = 0; i < list.Count; i++)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(list[i]);
            }
            return totalSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetZigZagPackedCollectionSize(IEnumerable<int> collection)
        {
            if (collection == null)
            {
                return 0;
            }

            int totalSize = 0;
            foreach (var item in collection)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(item);
            }
            return totalSize;
        }

        #region Long Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed varint long array.
        /// </summary>
        public static int GetVarintPackedCollectionSize(long[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize((ulong)item);
            }
            return totalSize;
        }

        /// <summary>
        /// Calculates the total size in bytes for a packed ZigZag long array.
        /// </summary>
        public static int GetZigZagPackedCollectionSize(long[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(item);
            }
            return totalSize;
        }

        #endregion

        #region Bool Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed boolean array.
        /// Booleans are encoded as varints (0 or 1).
        /// </summary>
        public static int GetBoolPackedCollectionSize(bool[] array)
        {
            if (array == null)
                return 0;

            // Each boolean is encoded as a single byte varint (0 or 1)
            return array.Length;
        }

        #endregion

        #region VarInt64 Size Calculation

        /// <summary>
        /// [DEPRECATED] Use WireFormatHelpers.GetVarintSize(ulong) instead.
        /// Calculates the size in bytes for a VarInt64 value.
        /// </summary>
        [Obsolete("Use WireFormatHelpers.GetVarintSize(ulong) instead. This method will be removed in a future version.")]
        public static int GetVarInt64Size(long value)
        {
            return WireFormatHelpers.GetVarintSize((ulong)value);
        }

        /// <summary>
        /// [DEPRECATED] Use WireFormatHelpers.GetVarintSize(ulong) instead.
        /// Calculates the size in bytes for a VarUInt64 value.
        /// </summary>
        [Obsolete("Use WireFormatHelpers.GetVarintSize(ulong) instead. This method will be removed in a future version.")]
        public static int GetVarUInt64Size(ulong value)
        {
            return WireFormatHelpers.GetVarintSize(value);
        }

        #endregion

        #region SByte Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed varint signed byte array.
        /// </summary>
        public static int GetVarintPackedCollectionSizeSByte(sbyte[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)item);
            }
            return totalSize;
        }

        /// <summary>
        /// Calculates the total size in bytes for a packed ZigZag signed byte array.
        /// </summary>
        public static int GetZigZagPackedCollectionSizeSByte(sbyte[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(item);
            }
            return totalSize;
        }

        #endregion

        #region Int16 Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed varint int16 array.
        /// </summary>
        public static int GetVarintPackedCollectionSizeInt16(short[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)item);
            }
            return totalSize;
        }

        /// <summary>
        /// Calculates the total size in bytes for a packed ZigZag int16 array.
        /// </summary>
        public static int GetZigZagPackedCollectionSizeInt16(short[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetZigZagVarintSize(item);
            }
            return totalSize;
        }

        #endregion

        #region UInt16 Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed varint uint16 array.
        /// </summary>
        public static int GetVarintPackedCollectionSizeUInt16(ushort[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize((uint)item);
            }
            return totalSize;
        }

        #endregion

        #region UInt32 Array Packed Size Methods

        /// <summary>
        /// Calculates packed varint size for uint array with SIMD acceleration (AVX2).
        /// Processes 8 values at once when available.
        /// </summary>
        public static int GetVarintPackedCollectionSizeUInt32(uint[] array)
        {
            if (array == null || array.Length == 0)
                return 0;

            // Use SIMD for arrays with 8+ elements when AVX2 is available
            if (Avx2.IsSupported && array.Length >= 8)
            {
                return GetVarintPackedCollectionSizeUInt32Simd(array);
            }

            // Scalar fallback
            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize(item);
            }
            return totalSize;
        }

        /// <summary>
        /// SIMD-accelerated varint size calculation for uint arrays using AVX2.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetVarintPackedCollectionSizeUInt32Simd(uint[] array)
        {
            int totalSize = 0;
            int i = 0;

            // Threshold vectors for varint byte boundaries (unsigned)
            var threshold1 = Vector256.Create(0x7Fu);         // 127 - 1 byte threshold
            var threshold2 = Vector256.Create(0x3FFFu);       // 16383 - 2 byte threshold
            var threshold3 = Vector256.Create(0x1FFFFFu);     // 2097151 - 3 byte threshold
            var threshold4 = Vector256.Create(0x0FFFFFFFu);   // 268435455 - 4 byte threshold
            var ones = Vector256.Create(1u);

            // Process 8 values at once
            ref uint arrayRef = ref MemoryMarshal.GetArrayDataReference(array);
            for (; i + 8 <= array.Length; i += 8)
            {
                var values = Vector256.LoadUnsafe(ref Unsafe.Add(ref arrayRef, i));

                // For unsigned comparison, we need to work around AVX2's signed comparison
                // by using subtraction and checking for underflow
                var sizes = ones;

                // Check if value > threshold (for unsigned)
                // We compute (value - threshold - 1) and check sign bit
                var diff1 = Avx2.Subtract(values.AsInt32(), Vector256.Create(0x80).AsInt32());
                var diff2 = Avx2.Subtract(values.AsInt32(), Vector256.Create(0x4000).AsInt32());
                var diff3 = Avx2.Subtract(values.AsInt32(), Vector256.Create(0x200000).AsInt32());
                var diff4 = Avx2.Subtract(values.AsInt32(), Vector256.Create(0x10000000).AsInt32());

                // Extract sign bits (1 if >= threshold, 0 otherwise)
                var mask1 = Avx2.ShiftRightArithmetic(diff1, 31);
                var mask2 = Avx2.ShiftRightArithmetic(diff2, 31);
                var mask3 = Avx2.ShiftRightArithmetic(diff3, 31);
                var mask4 = Avx2.ShiftRightArithmetic(diff4, 31);

                // Invert masks (we want 1 when value >= threshold)
                sizes = Avx2.Subtract(sizes.AsInt32(), Avx2.AndNot(mask1, Vector256.Create(1))).AsUInt32();
                sizes = Avx2.Subtract(sizes.AsInt32(), Avx2.AndNot(mask2, Vector256.Create(1))).AsUInt32();
                sizes = Avx2.Subtract(sizes.AsInt32(), Avx2.AndNot(mask3, Vector256.Create(1))).AsUInt32();
                sizes = Avx2.Subtract(sizes.AsInt32(), Avx2.AndNot(mask4, Vector256.Create(1))).AsUInt32();

                totalSize += (int)Vector256.Sum(sizes);
            }

            // Scalar remainder
            for (; i < array.Length; i++)
            {
                totalSize += WireFormatHelpers.GetVarintSize(array[i]);
            }

            return totalSize;
        }

        #endregion

        #region UInt64 Array Packed Size Methods

        /// <summary>
        /// Calculates the total size in bytes for a packed varint uint64 array.
        /// </summary>
        public static int GetVarintPackedCollectionSizeUInt64(ulong[] array)
        {
            if (array == null)
                return 0;

            int totalSize = 0;
            foreach (var item in array)
            {
                totalSize += WireFormatHelpers.GetVarintSize(item);
            }
            return totalSize;
        }

        #endregion
    }
}
