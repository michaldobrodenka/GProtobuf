using System;
using System.Buffers;
using System.Buffers.Binary;
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
            WriteVarUInt32(value); // Delegate to optimized version
        }

        // Optimized version for unsigned/positive values only (lengths, byte, ushort, uint)
        public void WriteVarUInt32(uint value)
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

        public void WriteFixed64(ulong ulongValue)
        {
            Stream.Write(MemoryMarshal.Cast<ulong, byte>(MemoryMarshal.CreateReadOnlySpan(ref ulongValue, 1)));
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
            ulong uValue = (ulong)value; // Convert to unsigned for proper bit operations
            Span<byte> buffer = stackalloc byte[10];
            int position = 0;
            while (uValue > 0x7F)
            {
                buffer[position++] = (byte)((uValue & 0x7F) | 0x80);
                uValue >>= 7;
            }
            buffer[position++] = (byte)uValue;
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

        public void WriteBool(bool value)
        {
            WriteVarint32(value ? 1u : 0u);
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

        public void WriteGuid(Guid value)
        {
            // Write Guid as 16-byte array (same as protobuf-net)
            Span<byte> buffer = stackalloc byte[16];
            value.TryWriteBytes(buffer);
            Stream.Write(buffer);
        }

        #region Long/Int64 Methods

        /// <summary>
        /// Writes a VarInt64 value to the stream.
        /// </summary>
        public void WriteVarInt64(long value)
        {
            WriteVarUInt64((ulong)value);
        }

        /// <summary>
        /// Writes a VarUInt64 value to the stream.
        /// </summary>
        public void WriteVarUInt64(ulong value)
        {
            while (value >= 0x80)
            {
                Stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            Stream.WriteByte((byte)value);
        }

        /// <summary>
        /// Writes a ZigZag encoded VarInt64 value to the stream.
        /// </summary>
        public void WriteZigZagVarInt64(long value)
        {
            ulong zigzagValue = (ulong)((value << 1) ^ (value >> 63));
            WriteVarUInt64(zigzagValue);
        }

        /// <summary>
        /// Writes a fixed-size 64-bit signed integer (8 bytes, little-endian) to the stream.
        /// </summary>
        public void WriteFixedInt64(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            Stream.Write(buffer);
        }

        #endregion

        #region Fixed Size Methods for New Primitive Types

        /// <summary>
        /// Writes a fixed-size 16-bit signed integer as 32-bit (4 bytes, little-endian) to the stream.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedInt32(short value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)value);
            Stream.Write(buffer);
        }

        /// <summary>
        /// Writes a fixed-size 16-bit unsigned integer as 32-bit (4 bytes, little-endian) to the stream.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedUInt32(ushort value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value);
            Stream.Write(buffer);
        }

        /// <summary>
        /// Writes a fixed-size 32-bit unsigned integer (4 bytes, little-endian) to the stream.
        /// </summary>
        public void WriteFixedUInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Stream.Write(buffer);
        }

        /// <summary>
        /// Writes a fixed-size 64-bit unsigned integer (8 bytes, little-endian) to the stream.
        /// </summary>
        public void WriteFixedUInt64(ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            Stream.Write(buffer);
        }

        #endregion
    }
}
