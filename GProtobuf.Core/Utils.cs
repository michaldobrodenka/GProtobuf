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
    }
}
