using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

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
        private int lastAdvancePosition;
        private const int MinBufferSize = 1024;

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
                this.writer.Advance(currentPosition- this.lastAdvancePosition);
                //if (bytesNeeded > currentSpan.Length)
                //{
                currentSpan = writer.GetSpan(this.currentPosition + (bytesNeeded < MinBufferSize ? MinBufferSize : bytesNeeded));
                //}

                this.lastAdvancePosition = 0;
                this.currentPosition = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            if (currentPosition > 0)
            {
                this.writer.Advance(currentPosition - this.lastAdvancePosition);
                //currentSpan = writer.GetSpan(MinBufferSize);
                //currentPosition = 0;
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

        public void WriteFixed64(double value)
        {
            EnsureSpace(8);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(currentSpan.Slice(currentPosition)), value); // nat�vna endianita
            currentPosition += 8;
        }

        public void WriteFixed32(float value)
        {
            EnsureSpace(4);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(currentSpan.Slice(currentPosition)), value); // nat�vna endianita
            currentPosition += 4;
        }

        public void WriteFixed64(long value)
        {
            EnsureSpace(8);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(currentSpan.Slice(currentPosition)), value);
            currentPosition += 8;
        }

        public void WriteFixed64(ulong value)
        {
            EnsureSpace(8);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(currentSpan.Slice(currentPosition)), value);
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
            if (value.Length < 256)
            {
                Span<byte> tempBuffer = stackalloc byte[value.Length * 4];
                int bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);
                WriteVarUInt32((uint)bytesWritten);
                this.WriteBytes(tempBuffer);
                //WriteToBuffer(tempBuffer.Slice(0, bytesWritten));
            }
            else
            {
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(value.Length * 4);
                try
                {
                    var tempBuffer = rentedBuffer.AsSpan();
                    int bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);
                    WriteVarUInt32((uint)bytesWritten);
                    this.WriteBytes(tempBuffer.Slice(0, bytesWritten));
                    //WriteToBuffer(tempBuffer.Slice(0, bytesWritten));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
        {
            EnsureSpace(bytes.Length);
            bytes.Slice(0, bytes.Length).CopyTo(currentSpan.Slice(currentPosition));
            currentPosition += bytes.Length;
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

        /// <summary>
        /// Writes Guid in protobuf-net BCL format (nested message with lo/hi fixed64 fields).
        /// Wire format: [length=18][tag 0x09][8 bytes lo][tag 0x11][8 bytes hi]
        /// Total: 19 bytes (1 length prefix + 18 nested content)
        /// </summary>
        public void WriteGuid(Guid value)
        {
            // Convert Guid to byte array (16 bytes, little-endian)
            Span<byte> guidBytes = stackalloc byte[16];
            if (!value.TryWriteBytes(guidBytes))
                throw new InvalidOperationException("Failed to convert Guid to bytes");

            // Write nested message length = 18 bytes total
            // (1 byte tag + 8 bytes lo + 1 byte tag + 8 bytes hi)
            WriteVarUInt32(18);

            // Write field 1 (lo): tag 0x09 (field 1, WireType.Fixed64)
            WriteSingleByte(0x09);

            // Write low 8 bytes (little-endian, directly from guidBytes)
            EnsureSpace(8);
            guidBytes.Slice(0, 8).CopyTo(currentSpan.Slice(currentPosition, 8));
            currentPosition += 8;

            // Write field 2 (hi): tag 0x11 (field 2, WireType.Fixed64)
            WriteSingleByte(0x11);

            // Write high 8 bytes (little-endian, directly from guidBytes)
            EnsureSpace(8);
            guidBytes.Slice(8, 8).CopyTo(currentSpan.Slice(currentPosition, 8));
            currentPosition += 8;
        }

        /// <summary>
        /// Writes a TimeSpan value (serialized as Ticks - VarInt64).
        /// Zero allocations, fast serialization.
        /// </summary>
        public void WriteTimeSpan(TimeSpan value)
        {
            WriteVarInt64(value.Ticks);
        }

        /// <summary>
        /// Writes DateTime in protobuf-net BCL format (nested message with value/scale fields).
        /// Wire format: [length][field 1: tag 0x08][sint64 value][field 2: tag 0x10][int32 scale]
        /// Uses optimal Scale to minimize wire size.
        /// Level200: DateTimeKind is NOT serialized.
        /// </summary>
        public void WriteDateTime(DateTime value)
        {
            // Get optimal scale for this DateTime value
            var (scaledValue, scale) = DateTimeHelper.GetOptimalScale(value);

            // Calculate nested message size
            int valueSize = GetZigZagVarintSize(scaledValue);
            int scaleSize = GetVarintSize((uint)scale);
            int contentSize = 1 + valueSize + 1 + scaleSize; // 2 tags + 2 values

            // Write nested message length prefix
            WriteVarUInt32((uint)contentSize);

            // Write field 1: value (sint64, ZigZag encoded)
            WriteSingleByte(0x08); // tag for field 1, WireType.VarInt
            WriteZigZagVarInt64(scaledValue);

            // Write field 2: scale (int32)
            WriteSingleByte(0x10); // tag for field 2, WireType.VarInt
            WriteVarInt32(scale);

            // Level200: field 3 (kind) is NOT written
        }

        /// <summary>
        /// Helper method to calculate varint size for unsigned values.
        /// </summary>
        private static int GetVarintSize(uint value)
        {
            if (value < (1 << 7)) return 1;
            if (value < (1 << 14)) return 2;
            if (value < (1 << 21)) return 3;
            if (value < (1 << 28)) return 4;
            return 5;
        }

        /// <summary>
        /// Helper method to calculate ZigZag varint size for signed values.
        /// </summary>
        private static int GetZigZagVarintSize(long value)
        {
            ulong zigzag = (ulong)((value << 1) ^ (value >> 63));

            if (zigzag < (1UL << 7)) return 1;
            if (zigzag < (1UL << 14)) return 2;
            if (zigzag < (1UL << 21)) return 3;
            if (zigzag < (1UL << 28)) return 4;
            if (zigzag < (1UL << 35)) return 5;
            if (zigzag < (1UL << 42)) return 6;
            if (zigzag < (1UL << 49)) return 7;
            if (zigzag < (1UL << 56)) return 8;
            if (zigzag < (1UL << 63)) return 9;
            return 10;
        }

        // Packed array methods
        public void WritePackedFixedSizeIntArray(int[] array)
        {
            WriteBytes(MemoryMarshal.Cast<int, byte>(array.AsSpan()));
        }

        public void WritePackedFixedSizeIntList(List<int> list)
        {
            WriteBytes(MemoryMarshal.Cast<int, byte>(CollectionsMarshal.AsSpan(list)));
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