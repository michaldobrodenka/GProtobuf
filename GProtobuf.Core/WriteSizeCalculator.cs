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
            while (value > 0x7F)
            {
                Length++;
                value >>= 7;
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
                WriteVarint32((uint)byteCount); // String length as varint
                Length += byteCount; // String bytes themselves
            }
        }

        public void WriteBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                WriteVarint32((uint)bytes.Length); // Length as varint
                Length += bytes.Length; // Bytes themselves
            }
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            WriteVarint32((uint)bytes.Length); // Length as varint
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

        // Packed array methods
        public void WritePackedVarintArray(int[] array)
        {
            if (array != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(array);
                WriteVarint32(packedSize);
                Length += packedSize;
            }
        }

        public void WritePackedVarintList(List<int> list)
        {
            if (list != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(list);
                WriteVarint32(packedSize);
                Length += packedSize;
            }
        }

        public void WritePackedZigZagArray(int[] array)
        {
            if (array != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(array); // approximation
                WriteVarint32(packedSize);
                Length += packedSize;
            }
        }

        public void WritePackedZigZagList(List<int> list)
        {
            if (list != null)
            {
                var packedSize = Utils.GetVarintPackedCollectionSize(list); // approximation
                WriteVarint32(packedSize);
                Length += packedSize;
            }
        }
    }
}
