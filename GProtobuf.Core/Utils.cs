using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintPackedCollectionSize(int[] array)
        {
            if (array == null)
            {
                return 0;
            }

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
        /// Calculates the total size in bytes for a packed varint uint32 array.
        /// </summary>
        public static int GetVarintPackedCollectionSizeUInt32(uint[] array)
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
