using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    [SkipLocalsInit]
    public ref struct StreamWriter
    {
        public Stream Stream { get; private set; }
        //private byte[] buffer;
        private int bufferPosition;

        private Span<byte> buffer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref byte FirstRef() => ref MemoryMarshal.GetReference(buffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref byte RefAt(int pos) => ref Unsafe.Add(ref FirstRef(), pos);

        public StreamWriter(Stream stream, scoped Span<byte> buffer)
        {
            Stream = stream;
            //buffer = ArrayPool<byte>.Shared.Rent(1024);
            bufferPosition = 0;
            unsafe
            {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
                this.buffer = buffer;
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
            }
        }

        public void WriteTag(int fieldId, WireType wireType)
        {
            int tag = (fieldId << 3) | (int)wireType;
            WriteVarInt32(tag);
        }

        //// Optimized version for unsigned/positive values only (lengths, byte, ushort, uint)
        //public void WriteVarUInt32(uint value)
        //{
        //    while (value > 0x7F)
        //    {
        //        WriteSingleByte((byte)((value & 0x7F) | 0x80));
        //        value >>= 7;
        //    }
        //    WriteSingleByte((byte)value);
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32(uint value)
        {
            int pos = bufferPosition;
            int space = buffer.Length - pos;

            if (space >= 5)
            {
                ref byte p = ref RefAt(pos);

                while (value > 0x7Fu)
                {
                    Unsafe.WriteUnaligned(ref p, (byte)((value & 0x7Fu) | 0x80u));
                    p = ref Unsafe.Add(ref p, 1);
                    pos++;
                    value >>= 7;
                }

                Unsafe.WriteUnaligned(ref p, (byte)value);
                pos++;

                bufferPosition = pos;
            }
            else
            {
                // fallback
                while (value > 0x7Fu)
                {
                    WriteSingleByte((byte)((value & 0x7Fu) | 0x80u));
                    value >>= 7;
                }
                WriteSingleByte((byte)value);
            }
        }

        public void WriteFixedSizeInt32(int intValue)
        {
            WriteToBuffer(MemoryMarshal.Cast<int, byte>(MemoryMarshal.CreateReadOnlySpan(ref intValue, 1)));
        }

        public void WriteFixed64(ulong ulongValue)
        {
            WriteToBuffer(MemoryMarshal.Cast<ulong, byte>(MemoryMarshal.CreateReadOnlySpan(ref ulongValue, 1)));
        }

        public void WriteVarInt32(int intValue)
        {
            WriteVarUInt32((uint)intValue);
        }

        public void WriteZigZag32(int value)
        {
            WriteVarInt32((value << 1) ^ (value >> 31));
        }

        public void WriteZigZag64(long value)
        {
            WriteVarInt64((value << 1) ^ (value >> 63));
        }

        public void WriteBool(bool value)
        {
            WriteVarUInt32(value ? 1u : 0u);
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
            WriteVarintUInt64(value);
        }

        public void WriteVarintUInt64(ulong value)
        {
            while (value > 0x7F)
            {
                WriteSingleByte((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            WriteSingleByte((byte)value);
        }

        public void WriteDouble(double value)
        {
            WriteToBuffer(MemoryMarshal.Cast<double, byte>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
        }

        public void WriteFloat(float value)
        {
            WriteToBuffer(MemoryMarshal.Cast<float, byte>(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
        }

        public void WritePackedFixedSizeIntArray(int[] array)
        {
            WriteToBuffer(MemoryMarshal.Cast<int, byte>(array.AsSpan()));
        }

        public void WritePackedFixedSizeIntList(List<int> list)
        {
            WriteToBuffer(MemoryMarshal.Cast<int, byte>(CollectionsMarshal.AsSpan(list)));
        }


        // Write non null string value, for shorter strings we use stackalloc for performance
        public void WriteString(string value)
        {
            if (value.Length < 256)
            {
                Span<byte> tempBuffer = stackalloc byte[value.Length * 4];
                int bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);
                WriteVarUInt32((uint)bytesWritten);
                WriteToBuffer(tempBuffer.Slice(0, bytesWritten));
            }
            else
            {
                var rentedBuffer = ArrayPool<byte>.Shared.Rent(value.Length * 4);
                try
                {
                    var tempBuffer = rentedBuffer.AsSpan();
                    int bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);
                    WriteVarUInt32((uint)bytesWritten);
                    WriteToBuffer(tempBuffer.Slice(0, bytesWritten));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                }
            }
        }

        //public void WriteString(string value)
        //{
        //    // FAST PATH: ak sa zmestí aj v "worst-case" (UTF-8 max 4 B/char),
        //    // urob jedno GetBytes bez dočasných alokácií.
        //    int available = buffer.Length - bufferPosition;
        //    int worstCase = Encoding.UTF8.GetMaxByteCount(value.Length);
        //    if (available >= worstCase)
        //    {
        //        int written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer.Slice(bufferPosition));
        //        bufferPosition += written;
        //        return;
        //    }

        //    // STREAM PATH: kóduj po častiach priamo do bufferu, bez ArrayPoolu.
        //    Encoder enc = Encoding.UTF8.GetEncoder();
        //    ReadOnlySpan<char> chars = value.AsSpan();

        //    while (true)
        //    {
        //        if (bufferPosition == buffer.Length)
        //            Flush(); // po návrate nech je k dispozícii aspoň pár bajtov

        //        Span<byte> dest = buffer.Slice(bufferPosition);
        //        // 'flush' nastavíme na true v momente, keď máme šancu dobehnúť koniec
        //        bool flushNow = chars.Length <= dest.Length;

        //        enc.Convert(chars, dest, flushNow,
        //                    out int charsUsed, out int bytesUsed, out bool completed);

        //        bufferPosition += bytesUsed;
        //        chars = chars.Slice(charsUsed);

        //        if (completed) break; // všetko zakódované + stav encoderu vyprázdnený
        //    }
        //}

        public void WriteGuid(Guid value)
        {
            // Write Guid as 16-byte array (same as protobuf-net)
            EnsureBufferSpace(16);
            if (!value.TryWriteBytes(buffer.Slice(bufferPosition, 16)))
                throw new InvalidOperationException("Failed to write Guid to buffer");
            bufferPosition += 16;
        }


        /// <summary>
        /// Writes a span of bytes directly to stream (for protobuf bytes fields)
        /// </summary>
        public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
        {
            WriteToBuffer(bytes);
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
                WriteSingleByte((byte)(value | 0x80));
                value >>= 7;
            }
            WriteSingleByte((byte)value);
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
            EnsureBufferSpace(8);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bufferPosition, 8), value);
            bufferPosition += 8;
        }

        #endregion

        #region Fixed Size Methods for New Primitive Types

        /// <summary>
        /// Writes a fixed-size 16-bit signed integer as 32-bit (4 bytes, little-endian) to the stream.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedInt32(short value)
        {
            EnsureBufferSpace(4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(bufferPosition, 4), (int)value);
            bufferPosition += 4;
        }

        /// <summary>
        /// Writes a fixed-size 16-bit unsigned integer as 32-bit (4 bytes, little-endian) to the stream.
        /// Protocol Buffers uses fixed32 for 16-bit values.
        /// </summary>
        public void WriteFixedUInt32(ushort value)
        {
            EnsureBufferSpace(4);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(bufferPosition, 4), (uint)value);
            bufferPosition += 4;
        }

        /// <summary>
        /// Writes a fixed-size 32-bit unsigned integer (4 bytes, little-endian) to the stream.
        /// </summary>
        public void WriteFixedUInt32(uint value)
        {
            EnsureBufferSpace(4);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(bufferPosition, 4), value);
            bufferPosition += 4;
        }

        /// <summary>
        /// Writes a fixed-size 64-bit unsigned integer (8 bytes, little-endian) to the stream.
        /// </summary>
        public void WriteFixedUInt64(ulong value)
        {
            EnsureBufferSpace(8);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(bufferPosition, 8), value);
            bufferPosition += 8;
        }

        #endregion

        private void EnsureBufferSpace(int neededBytes)
        {
            if (bufferPosition + neededBytes > buffer.Length)
            {
                Flush();
            }
        }

        private void WriteToBuffer(scoped ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return;

            if (bufferPosition + data.Length > buffer.Length)
            {
                Flush();
                if (data.Length > buffer.Length)
                {
                    Stream.Write(data);
                    return;
                }
            }

            data.CopyTo(buffer.Slice(bufferPosition));
            bufferPosition += data.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingleByte(byte value)
        {
            if ((uint)bufferPosition >= (uint)buffer.Length)
                Flush();

            Unsafe.WriteUnaligned(ref RefAt(bufferPosition), value);
            bufferPosition++;
        }

        public void Flush()
        {
            if (bufferPosition > 0)
            {
                Stream.Write(buffer.Slice(0, bufferPosition));
                bufferPosition = 0;
            }
            //ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
