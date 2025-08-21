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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetTagAndWireType(int tag, WireType wireType)
        {
            return (tag << 3) | (int)wireType;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintSize(uint value)
        {
            if (value == 0)
            {
                return 1;
            }

            int count = 0;
            while (value > 0)
            {
                count++;
                value >>= 7; // Posun o 7 bitov doprava
            }
            return count;

            // Alternativna, bitovejsia implementacia:
            // int count = 1; // Zatial 1 byt
            // while ((value & ~0x7F) != 0) // Kym existuju bity vyssie ako spodnych 7
            // {
            //     count++;
            //     value >>= 7; // Posunieme, aby sme skontrolovali dalsiu 7-bitovu skupinu
            // }
            // return count;
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
                totalSize += GetVarintSize((uint)array[i]);
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
                totalSize += GetVarintSize((uint)list[i]);
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
                totalSize += GetVarintSize((uint)item);
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
                totalSize += GetVarintSize(item);
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
                // ZigZag encode the value first, then calculate varint size
                uint zigzagValue = (uint)((array[i] << 1) ^ (array[i] >> 31));
                totalSize += GetVarintSize(zigzagValue);
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
                // ZigZag encode the value first, then calculate varint size
                uint zigzagValue = (uint)((list[i] << 1) ^ (list[i] >> 31));
                totalSize += GetVarintSize(zigzagValue);
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
                // ZigZag encode the value first, then calculate varint size
                uint zigzagValue = (uint)((item << 1) ^ (item >> 31));
                totalSize += GetVarintSize(zigzagValue);
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
                totalSize += GetVarInt64Size(item);
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
                // ZigZag encode the long value first, then calculate varint size
                ulong zigzagValue = (ulong)((item << 1) ^ (item >> 63));
                totalSize += GetVarUInt64Size(zigzagValue);
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
        /// Calculates the size in bytes for a VarInt64 value.
        /// </summary>
        public static int GetVarInt64Size(long value)
        {
            return GetVarUInt64Size((ulong)value);
        }

        /// <summary>
        /// Calculates the size in bytes for a VarUInt64 value.
        /// </summary>
        public static int GetVarUInt64Size(ulong value)
        {
            int size = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                size++;
            }
            return size;
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
                totalSize += GetVarintSize((uint)item);
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
                // ZigZag encode the sbyte value first, then calculate varint size
                uint zigzagValue = (uint)((item << 1) ^ (item >> 31));
                totalSize += GetVarintSize(zigzagValue);
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
                totalSize += GetVarintSize((uint)item);
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
                // ZigZag encode the short value first, then calculate varint size
                uint zigzagValue = (uint)((item << 1) ^ (item >> 31));
                totalSize += GetVarintSize(zigzagValue);
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
                totalSize += GetVarintSize((uint)item);
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
                totalSize += GetVarintSize(item);
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
                totalSize += GetVarUInt64Size(item);
            }
            return totalSize;
        }

        #endregion
    }
}
