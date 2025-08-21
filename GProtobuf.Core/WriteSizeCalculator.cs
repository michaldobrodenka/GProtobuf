using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public ref struct WriteSizeCalculator
    {
        public long Length { get; private set; }

        public WriteSizeCalculator()
        {
            Length = 0;
        }

        public void WriteTag(int fieldId, WireType wireType)
        {
            int tag = (fieldId << 3) | (int)wireType;
            WriteVarint32(tag);
        }

        public void WriteVarint32(uint value)
        {
            WriteVarUInt32(value); // Delegate to optimized version
        }

        // Optimized version for unsigned/positive values only (lengths, byte, ushort, uint)
        public void WriteVarUInt32(uint value)
        {
            while (value > 0x7F)
            {
                Length++;
                value >>= 7;
            }
            Length++;
        }

        public void WriteFixedSizeInt32(int intValue)
        {
            Length += sizeof(int);
        }

        public void WriteVarint32(int intValue)
        {
            var value = (uint)intValue; // Convert int to uint for proper Varint encoding for int32 in Protobuf
            while (value > 0x7F)
            {
                Length++;
                value >>= 7;
            }
            Length++;
        }

        public void WriteVarint64(long value)
        {
            ulong uValue = (ulong)value; // Convert to unsigned for proper bit operations
            while (uValue > 0x7F)
            {
                Length++;
                uValue >>= 7;
            }
            Length++;
        }

        public void WriteZigZag32(int value)
        {
            WriteVarint32((value << 1) ^ (value >> 31));
        }

        public void WriteZigZag64(long value)
        {
            WriteVarint64((value << 1) ^ (value >> 63));
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
                WriteVarint32(value); // Keep as signed to handle negatives correctly
        }

        public void WriteInt16(short value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZag32(value);
            else
                WriteVarint32(value); // Keep as signed to handle negatives correctly
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
                WriteVarint64(value);
        }

        public void WriteUInt64(ulong value)
        {
            WriteVarintUInt64(value);
        }

        public void WriteVarintUInt64(ulong value)
        {
            while (value > 0x7F)
            {
                Length++;
                value >>= 7;
            }
            Length++;
        }

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
        public long GetLengthAndReset()
        {
            long currentLength = Length;
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
    }
}
