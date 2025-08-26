using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GProtobuf.Core
{
    /// <summary>
    /// High-performance writer for Protocol Buffers that writes directly to IBufferWriter<byte>
    /// </summary>
    [SkipLocalsInit]
    public ref struct BufferWriter
    {
        private IBufferWriter<byte> writer;
        private Span<byte> currentSpan;
        private int currentPosition;
        private const int MinBufferSize = 256;

        public BufferWriter(IBufferWriter<byte> writer)
        {
            this.writer = writer;
            currentSpan = writer.GetSpan(MinBufferSize);
            currentPosition = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureSpace(int bytesNeeded)
        {
            if (currentPosition + bytesNeeded > currentSpan.Length)
            {
                Flush();
                if (bytesNeeded > currentSpan.Length)
                {
                    currentSpan = writer.GetSpan(bytesNeeded);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            if (currentPosition > 0)
            {
                writer.Advance(currentPosition);
                currentSpan = writer.GetSpan(MinBufferSize);
                currentPosition = 0;
            }
        }

        public void WriteTag(int fieldId, WireType wireType)
        {
            int tag = (fieldId << 3) | (int)wireType;
            WriteVarInt32(tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingleByte(byte value)
        {
            EnsureSpace(1);
            currentSpan[currentPosition++] = value;
        }

        public void WriteVarUInt32(uint value)
        {
            EnsureSpace(5); // Max varint32 size
            
            while (value >= 0x80)
            {
                currentSpan[currentPosition++] = (byte)(value | 0x80);
                value >>= 7;
            }
            currentSpan[currentPosition++] = (byte)value;
        }

        public void WriteVarInt32(int value)
        {
            WriteVarUInt32((uint)value);
        }

        public void WriteVarint64(long value)
        {
            WriteVarUInt64((ulong)value);
        }

        public void WriteVarUInt64(ulong value)
        {
            EnsureSpace(10); // Max varint64 size
            
            while (value >= 0x80)
            {
                currentSpan[currentPosition++] = (byte)(value | 0x80);
                value >>= 7;
            }
            currentSpan[currentPosition++] = (byte)value;
        }

        public void WriteZigZagInt32(int value)
        {
            WriteVarUInt32((uint)((value << 1) ^ (value >> 31)));
        }

        public void WriteZigZagInt64(long value)
        {
            WriteVarUInt64((ulong)((value << 1) ^ (value >> 63)));
        }

        public void WriteFixed32(uint value)
        {
            EnsureSpace(4);
            BinaryPrimitives.WriteUInt32LittleEndian(currentSpan.Slice(currentPosition), value);
            currentPosition += 4;
        }

        public void WriteFixed64(ulong value)
        {
            EnsureSpace(8);
            BinaryPrimitives.WriteUInt64LittleEndian(currentSpan.Slice(currentPosition), value);
            currentPosition += 8;
        }

        public void WriteFloat(float value)
        {
            WriteFixed32(BitConverter.SingleToUInt32Bits(value));
        }

        public void WriteDouble(double value)
        {
            WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
        }

        public void WriteString(string value)
        {
            if (value == null) return;
            
            var byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
            WriteVarUInt32((uint)byteCount);
            
            EnsureSpace(byteCount);
            if (byteCount <= currentSpan.Length - currentPosition)
            {
                var written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), currentSpan.Slice(currentPosition));
                currentPosition += written;
            }
            else
            {
                // For large strings, flush and get a bigger buffer
                Flush();
                currentSpan = writer.GetSpan(byteCount);
                var written = System.Text.Encoding.UTF8.GetBytes(value.AsSpan(), currentSpan);
                currentPosition += written;
            }
        }

        public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
        {
            WriteVarUInt32((uint)bytes.Length);
            
            while (bytes.Length > 0)
            {
                EnsureSpace(1);
                var toCopy = Math.Min(bytes.Length, currentSpan.Length - currentPosition);
                bytes.Slice(0, toCopy).CopyTo(currentSpan.Slice(currentPosition));
                currentPosition += toCopy;
                bytes = bytes.Slice(toCopy);
            }
        }

        public void WriteBool(bool value)
        {
            WriteSingleByte((byte)(value ? 1 : 0));
        }

        // Additional methods to match StreamWriter API
        public void WriteFixedSizeInt32(int intValue)
        {
            WriteFixed32((uint)intValue);
        }

        public void WriteZigZag32(int value)
        {
            WriteZigZagInt32(value);
        }

        public void WriteZigZag64(long value)
        {
            WriteZigZagInt64(value);
        }

        public void WriteByte(byte value)
        {
            WriteSingleByte(value);
        }

        public void WriteSByte(sbyte value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZagInt32(value);
            else
                WriteVarInt32(value);
        }

        public void WriteInt16(short value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZagInt32(value);
            else
                WriteVarInt32(value);
        }

        public void WriteUInt16(ushort value)
        {
            WriteVarUInt32(value);
        }

        public void WriteUInt32(uint value)
        {
            WriteVarUInt32(value);
        }

        public void WriteInt64(long value, bool zigZag = false)
        {
            if (zigZag)
                WriteZigZagInt64(value);
            else
                WriteVarint64(value);
        }

        public void WriteUInt64(ulong value)
        {
            WriteVarUInt64(value);
        }

        public void WriteVarintUInt64(ulong value)
        {
            WriteVarUInt64(value);
        }

        public void WriteVarInt64(long value)
        {
            WriteVarint64(value);
        }

        public void WriteZigZagVarInt64(long value)
        {
            WriteZigZagInt64(value);
        }

        public void WriteFixedInt64(long value)
        {
            WriteFixed64((ulong)value);
        }

        public void WriteFixedInt32(short value)
        {
            WriteFixed32((uint)value);
        }

        public void WriteFixedUInt32(ushort value)
        {
            WriteFixed32(value);
        }

        public void WriteFixedUInt32(uint value)
        {
            WriteFixed32(value);
        }

        public void WriteFixedUInt64(ulong value)
        {
            WriteFixed64(value);
        }

        public void WriteGuid(Guid value)
        {
            // BCL Guid serialization - same as protobuf-net
            WriteVarUInt32(16); // Length
            EnsureSpace(16);
            value.TryWriteBytes(currentSpan.Slice(currentPosition));
            currentPosition += 16;
        }

        // Packed array methods
        public void WritePackedFixedSizeIntArray(int[] array)
        {
            if (array == null || array.Length == 0) return;
            
            WriteVarUInt32((uint)(array.Length * 4));
            foreach (var item in array)
            {
                WriteFixed32((uint)item);
            }
        }

        public void WritePackedFixedSizeIntList(List<int> list)
        {
            if (list == null || list.Count == 0) return;
            
            WriteVarUInt32((uint)(list.Count * 4));
            foreach (var item in list)
            {
                WriteFixed32((uint)item);
            }
        }

        // Precomputed tag support
        public void WritePrecomputedTag(byte tagByte)
        {
            WriteSingleByte(tagByte);
        }

        public void WritePrecomputedTag(ReadOnlySpan<byte> tagBytes)
        {
            EnsureSpace(tagBytes.Length);
            tagBytes.CopyTo(currentSpan.Slice(currentPosition));
            currentPosition += tagBytes.Length;
        }
    }
}