using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    [SkipLocalsInit]
    public ref struct WriteSizeCalculator
    {
        public int Length { get; private set; }

        public WriteSizeCalculator()
        {
            Length = 0;
        }

        //public void WriteVarInt32(uint value)
        //{
        //    //WriteVarUInt32(value); // Delegate to optimized version
        //}

        // Optimized version for unsigned/positive values only (lengths, byte, ushort, uint)
        public void WriteVarUInt32(uint value)
        {
            // 0 → 1 bajt; inak zaokrúhlenie nahor po 7 bitoch
            int nbits = 32 - BitOperations.LeadingZeroCount(value);
            this.Length += nbits == 0 ? 1 : (nbits + 6) / 7;
            //while (value > 0x7F)
            //{
            //    Length++;
            //    value >>= 7;
            //}
            //Length++;
        }

        public void WriteFixedSizeInt32(int intValue)
        {
            Length += sizeof(int);
        }

        public void WriteVarInt32(int value)
        {
            //var value = (uint)intValue; // Convert int to uint for proper Varint encoding for int32 in Protobuf
            //while (value > 0x7F)
            //{
            //    Length++;
            //    value >>= 7;
            //}
            //Length++;
            int nbits = 32 - BitOperations.LeadingZeroCount((uint)value);
            this.Length += nbits == 0 ? 1 : (nbits + 6) / 7;
        }

        //public void WriteVarInt64(long value)
        //{
        //    //ulong uValue = (ulong)value; // Convert to unsigned for proper bit operations
        //    //while (uValue > 0x7F)
        //    //{
        //    //    Length++;
        //    //    uValue >>= 7;
        //    //}
        //    //Length++;
        //    int nbits = 64 - BitOperations.LeadingZeroCount((ulong)value);
        //    this.Length += nbits == 0 ? 1 : (nbits + 6) / 7;
        //}

        public void WriteZigZag32(int value)
        {
            WriteVarInt32((value << 1) ^ (value >> 31));
        }

        public void WriteZigZag64(long value)
        {
            WriteVarInt64((value << 1) ^ (value >> 63));
        }

        public void WriteDouble(double value)
        {
            Length += sizeof(double);
        }

        public void WriteFloat(float value)
        {
            Length += sizeof(float);
        }

        public void WritePackedFixedSizeIntArray(int[] array)
        {
            if (array != null)
            {
                Length += array.Length * sizeof(int);
            }
        }

        public void WritePackedFixedSizeIntList(List<int> list)
        {
            if (list != null)
            {
                Length += list.Count * sizeof(int);
            }
        }

        public void WriteString(string value)
        {
            if (value != null)
            {
                int byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
                WriteVarUInt32((uint)byteCount); // String length as varint - use optimized version
                Length += byteCount; // String bytes themselves
            }
        }

        public void WriteBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                WriteVarUInt32((uint)bytes.Length); // Length as varint - use optimized version
                Length += bytes.Length; // Bytes themselves
            }
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            WriteVarUInt32((uint)bytes.Length); // Length as varint - use optimized version
            Length += bytes.Length; // Bytes themselves
        }

        /// <summary>
        /// Writes raw bytes without length prefix (for internal use)
        /// </summary>
        public void WriteRawBytesOnly(byte[] bytes)
        {
            if (bytes != null)
            {
                Length += bytes.Length;
            }
        }

        /// <summary>
        /// Writes raw bytes without length prefix (for internal use)
        /// </summary>
        public void WriteRawBytesOnly(ReadOnlySpan<byte> bytes)
        {
            Length += bytes.Length;
        }

        /// <summary>
        /// Adds byte length to the calculator without allocating bytes (zero-allocation size calculation)
        /// </summary>
        public void AddByteLength(int byteLength)
        {
            Length += byteLength;
        }

        public void WriteRawBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                Length += bytes.Length;
            }
        }

        public void WriteRawBytes(ReadOnlySpan<byte> bytes)
        {
            Length += bytes.Length;
        }

        public void WriteBool(bool value)
        {
            Length++; // Bool is always 1 byte in protobuf
        }

        public void WriteByte(byte value)
        {
            WriteVarUInt32(value); // Use optimized version for unsigned
        }

        public void WriteSByte(sbyte value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZag32(value);
            else
                WriteVarInt32(value); // Keep as signed to handle negatives correctly
        }

        public void WriteInt16(short value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZag32(value);
            else
                WriteVarInt32(value); // Keep as signed to handle negatives correctly
        }

        public void WriteUInt16(ushort value)
        {
            WriteVarUInt32(value); // Use optimized version for unsigned
        }

        public void WriteUInt32(uint value)
        {
            WriteVarUInt32(value); // Use optimized version for unsigned
        }

        public void WriteInt64(long value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZag64(value);
            else
                WriteVarInt64(value);
        }

        public void WriteUInt64(ulong value)
        {
            WriteVarUInt64(value);
        }

        //public void WriteVarUInt64(ulong value)
        //{
        //    int nbits = 64 - BitOperations.LeadingZeroCount((ulong)value);
        //    this.Length += nbits == 0 ? 1 : (nbits + 6) / 7;
        //    //while (value > 0x7F)
        //    //{
        //    //    Length++;
        //    //    value >>= 7;
        //    //}WriteUInt64
        //    //Length++;
        //}

        public void WriteFixed32(uint value)
        {
            Length += 4;
        }

        public void WriteFixed64(ulong value)
        {
            Length += 8;
        }

        public void WriteSFixed32(int value)
        {
            Length += 4;
        }

        public void WriteSFixed64(long value)
        {
            Length += 8;
        }

        // Helper method to reset the counter
        public void Reset()
        {
            Length = 0;
        }

        // Helper method to get current length and reset
        public int GetLengthAndReset()
        {
            int currentLength = Length;
            Length = 0;
            return currentLength;
        }

        public void WriteGuid(Guid value)
        {
            // Guid is always 16 bytes
            Length += 16;
        }

        // Packed array methods
        public void WritePackedVarintArray(int[] array)
        {
            if (array != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(array);
                WriteVarUInt32((uint)packedSize); // Use optimized version for size/length
                Length += packedSize;
            }
        }

        public void WritePackedVarintList(List<int> list)
        {
            if (list != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(list);
                WriteVarUInt32((uint)packedSize); // Use optimized version for size/length
                Length += packedSize;
            }
        }

        public void WritePackedZigZagArray(int[] array)
        {
            if (array != null)
            {
                var packedSize = Utils.GetZigZagPackedCollectionSize(array);
                WriteVarUInt32((uint)packedSize); // Use optimized version for size/length
                Length += packedSize;
            }
        }

        public void WritePackedZigZagList(List<int> list)
        {
            if (list != null)
            {
                var packedSize = Utils.GetZigZagPackedCollectionSize(list);
                WriteVarUInt32((uint)packedSize); // Use optimized version for size/length
                Length += packedSize;
            }
        }

        #region Long Array Methods

        /// <summary>
        /// Writes a packed varint long array (calculates size only).
        /// </summary>
        public void WritePackedVarintInt64Array(long[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a packed ZigZag long array (calculates size only).
        /// </summary>
        public void WritePackedZigZagInt64Array(long[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetZigZagPackedCollectionSize(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a single fixed-size long (8 bytes) for size calculation.
        /// </summary>
        public void WriteFixedInt64(long value)
        {
            Length += 8;
        }

        /// <summary>
        /// Writes a ZigZag encoded VarInt64 for size calculation.
        /// </summary>
        public void WriteZigZagVarInt64(long value)
        {
            ulong zigzagValue = (ulong)((value << 1) ^ (value >> 63));
            WriteVarUInt64(zigzagValue);
        }

        /// <summary>
        /// Writes a VarInt64 for size calculation.
        /// </summary>
        public void WriteVarInt64(long value)
        {
            WriteVarUInt64((ulong)value);
        }

        /// <summary>
        /// Writes a VarUInt64 for size calculation.
        /// </summary>
        public void WriteVarUInt64(ulong value)
        {
            //Length += Utils.GetVarUInt64Size(value);
            int nbits = 64 - BitOperations.LeadingZeroCount(value);
            this.Length += nbits == 0 ? 1 : (nbits + 6) / 7;
        }

        #endregion

        #region Bool Array Methods

        /// <summary>
        /// Writes a packed boolean array (calculates size only).
        /// </summary>
        public void WritePackedBoolArray(bool[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetBoolPackedCollectionSize(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        #endregion

        #region SByte Array Methods

        /// <summary>
        /// Writes a packed signed byte array (calculates size only).
        /// </summary>
        public void WritePackedSByteArray(sbyte[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSizeSByte(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a packed ZigZag signed byte array (calculates size only).
        /// </summary>
        public void WritePackedZigZagSByteArray(sbyte[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetZigZagPackedCollectionSizeSByte(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        #endregion

        #region Int16 Array Methods

        /// <summary>
        /// Writes a packed int16 array (calculates size only).
        /// </summary>
        public void WritePackedInt16Array(short[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSizeInt16(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a packed ZigZag int16 array (calculates size only).
        /// </summary>
        public void WritePackedZigZagInt16Array(short[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetZigZagPackedCollectionSizeInt16(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a single fixed-size int16 as int32 (4 bytes) for size calculation.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedInt32(short value)
        {
            Length += 4;
        }

        #endregion

        #region UInt16 Array Methods

        /// <summary>
        /// Writes a packed uint16 array (calculates size only).
        /// </summary>
        public void WritePackedUInt16Array(ushort[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSizeUInt16(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a single fixed-size uint16 as uint32 (4 bytes) for size calculation.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedUInt32(ushort value)
        {
            Length += 4;
        }

        #endregion

        #region UInt32 Array Methods

        /// <summary>
        /// Writes a packed uint32 array (calculates size only).
        /// </summary>
        public void WritePackedUInt32Array(uint[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSizeUInt32(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a single fixed-size uint32 (4 bytes) for size calculation.
        /// </summary>
        public void WriteFixedUInt32(uint value)
        {
            Length += 4;
        }

        #endregion

        #region UInt64 Array Methods

        /// <summary>
        /// Writes a packed uint64 array (calculates size only).
        /// </summary>
        public void WritePackedUInt64Array(ulong[] array)
        {
            if (array != null && array.Length > 0)
            {
                var packedSize = Utils.GetVarintPackedCollectionSizeUInt64(array);
                WriteVarUInt32((uint)packedSize);
                Length += packedSize;
            }
        }

        /// <summary>
        /// Writes a single fixed-size uint64 (8 bytes) for size calculation.
        /// </summary>
        public void WriteFixedUInt64(ulong value)
        {
            Length += 8;
        }

        #endregion
    }
}
