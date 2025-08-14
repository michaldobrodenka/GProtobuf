using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public ref struct StreamWriter
    {
        public Stream Stream { get; private set; }

        public StreamWriter(Stream stream)
        {
            Stream = stream;
        }

        public void WriteTag(int fieldId, WireType wireType)
        {
            int tag = (fieldId << 3) | (int)wireType;
            WriteVarint32(tag);
        }

        public void WriteVarint32(uint value)
        {
            Span<byte> buffer = stackalloc byte[5];
            int position = 0;
            while (value > 0x7F)
            {
                buffer[position++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            buffer[position++] = (byte)value;
            Stream.Write(buffer.Slice(0, position));
        }

        public void WriteFixedSizeInt32(int intValue)
        {
            Stream.Write(MemoryMarshal.Cast<int, byte>(MemoryMarshal.CreateReadOnlySpan(ref intValue, 1)));
        }

        public void WriteVarint32(int intValue)
        {
            var value = (uint)intValue; // Prekonvertujeme int na uint pre spravne Varint kodovanie pre int32 v Protobuf

            Span<byte> buffer = stackalloc byte[5];
            int position = 0;
            while (value > 0x7F)
            {
                buffer[position++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            buffer[position++] = (byte)value;
            Stream.Write(buffer.Slice(0, position));
        }

        public void WriteVarint64(long value)
        {
            Span<byte> buffer = stackalloc byte[10];
            int position = 0;
            while (value > 0x7F)
            {
                buffer[position++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            buffer[position++] = (byte)value;
            Stream.Write(buffer.Slice(0, position));
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
            Stream.Write(MemoryMarshal.Cast<double, byte>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
        }

        public void WriteFloat(float value)
        {
            Stream.Write(MemoryMarshal.Cast<float, byte>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
        }

        public void WritePackedFixedSizeIntArray(int[] array)
        {
            Stream.Write(MemoryMarshal.Cast<int, byte>(array.AsSpan()));
        }

        public void WritePackedFixedSizeIntList(List<int> list)
        {
            Stream.Write(MemoryMarshal.Cast<int, byte>(CollectionsMarshal.AsSpan(list)));
        }


        // Write non null string value, for shorter strings we use stackalloc for performance
        public void WriteString(string value)
        {
            if (value.Length <= 256)
            {
                Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetMaxByteCount(value.Length)];
                int bytesWritten = Encoding.UTF8.GetBytes(value, buffer);
                Stream.Write(buffer.Slice(0, bytesWritten));
            }
            else
            {
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(value.Length));
                try
                {
                    var buffer = rentedBuffer.AsSpan();
                    int bytesWritten = Encoding.UTF8.GetBytes(value, buffer);
                    Stream.Write(buffer.Slice(0, bytesWritten));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }
    }
}
