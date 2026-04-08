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
    internal readonly struct NestingFrame
    {
        public readonly Stream ParentStream;

        public NestingFrame(Stream parentStream)
        {
            ParentStream = parentStream;
        }
    }

    [InlineArray(16)]
    internal struct NestingStack
    {
        private NestingFrame _element;
    }

    internal sealed class MemoryStreamPool
    {
        public static readonly MemoryStreamPool Shared = new();

        [ThreadStatic]
        private static Stack<MemoryStream> t_pool;

        private static Stack<MemoryStream> Pool => t_pool ??= new Stack<MemoryStream>();

        public MemoryStream Get()
        {
            if (Pool.TryPop(out var stream))
                return stream;
            return new MemoryStream();
        }

        public void Return(MemoryStream stream)
        {
            stream.Position = 0;
            stream.SetLength(0);
            Pool.Push(stream);
        }
    }

    [SkipLocalsInit]
    public ref struct OnePassStreamWriter
    {

        public Stream Stream { get; private set; }
        private int bufferPosition;

        private Span<byte> buffer;
        private NestingStack nestingStack;
        private int nestingDepth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref byte FirstRef() => ref MemoryMarshal.GetReference(buffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref byte RefAt(int pos) => ref Unsafe.Add(ref FirstRef(), pos);

        public OnePassStreamWriter(Stream stream, scoped Span<byte> buffer)
        {
            Stream = stream;
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
            uint tag = WireFormatHelpers.EncodeTag(fieldId, wireType);
            WriteVarUInt32(tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
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

        public void WriteVarInt32(int intValue)
        {
            WriteVarUInt32((uint)intValue);
        }

        public void WriteZigZag32(int value)
        {
            WriteVarUInt32(WireFormatHelpers.EncodeZigZag32(value));
        }

        public void WriteZigZag64(long value)
        {
            WriteVarUInt64(WireFormatHelpers.EncodeZigZag64(value));
        }

        public void WriteBool(bool value)
        {
            WriteVarUInt32(value ? 1u : 0u);
        }

        public void WriteBoolTrue()
        {
            WriteSingleByte(1);
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


        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteVarUInt32(0);
                return;
            }

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

            //if (string.IsNullOrEmpty(value))
            //{
            //    WriteVarUInt32(0);
            //    return;
            //}

            //int maxUtf8Bytes = value.Length * 4;
            //int reservedBytes = WireFormatHelpers.GetVarintSize((uint)maxUtf8Bytes);

            //Flush();
            //int lengthPosition = (int)Stream.Position;

            //// Write placeholder bytes for length varint
            //bufferPosition += reservedBytes;
            ////for (int i = 0; i < reservedBytes; i++)
            ////    WriteSingleByte(0);
            //Flush();

            //// Encode UTF8 content
            //if (maxUtf8Bytes <= buffer.Length)
            //{
            //    // Fast path: encode directly into buffer — no temp allocation
            //    int bytesWritten = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            //    bufferPosition = bytesWritten;
            //}
            //else
            //{
            //    // Large string: encode via rented array
            //    var rentedArray = ArrayPool<byte>.Shared.Rent(maxUtf8Bytes);
            //    try
            //    {
            //        int bytesWritten = Encoding.UTF8.GetBytes(value.AsSpan(), rentedArray.AsSpan());
            //        WriteToBuffer(rentedArray.AsSpan(0, bytesWritten));
            //    }
            //    finally
            //    {
            //        ArrayPool<byte>.Shared.Return(rentedArray);
            //    }
            //}

            //// Fix up length prefix
            //Flush();
            //int endPosition = (int)Stream.Position;
            //int contentLength = endPosition - lengthPosition - reservedBytes;

            //Stream.Position = lengthPosition;
            //WriteVarUInt32Padded((uint)contentLength, reservedBytes);
            //Flush();
            //Stream.Position = endPosition;
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
            WriteVarUInt32(BclTypeFormats.Guid.NestedContentSize);

            // Write field 1 (lo): tag 0x09 (field 1, WireType.Fixed64)
            WriteSingleByte(BclTypeFormats.Guid.FieldLoTag);

            // Write low 8 bytes (little-endian, directly from guidBytes)
            EnsureBufferSpace(8);
            guidBytes.Slice(0, 8).CopyTo(buffer.Slice(bufferPosition, 8));
            bufferPosition += 8;

            // Write field 2 (hi): tag 0x11 (field 2, WireType.Fixed64)
            WriteSingleByte(BclTypeFormats.Guid.FieldHiTag);

            // Write high 8 bytes (little-endian, directly from guidBytes)
            EnsureBufferSpace(8);
            guidBytes.Slice(8, 8).CopyTo(buffer.Slice(bufferPosition, 8));
            bufferPosition += 8;
        }

        /// <summary>
        /// Writes TimeSpan in protobuf-net BCL format (nested message with value/scale fields).
        /// Wire format: [length][field 1: tag 0x08][sint64 value][field 2: tag 0x10][int32 scale]
        /// Uses optimal Scale to minimize wire size.
        /// IMPORTANT: TimeSpan is a duration, NOT a timestamp, so no Unix Epoch offset is used.
        /// </summary>
        public void WriteTimeSpan(TimeSpan value)
        {
            // Get optimal scale for this TimeSpan value
            var (scaledValue, scale) = DateTimeHelper.GetOptimalScaleForTimeSpan(value);

            // Calculate nested message size
            int valueSize = GetZigZagVarintSize(scaledValue);
            int scaleSize = GetVarintSize((uint)scale);
            int contentSize = 1 + valueSize + 1 + scaleSize; // 2 tags + 2 values

            // Write nested message length prefix
            WriteVarUInt32((uint)contentSize);

            // Write field 1: value (sint64, ZigZag encoded)
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldValueTag);
            WriteZigZagVarInt64(scaledValue);

            // Write field 2: scale (int32)
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldScaleTag);
            WriteVarInt32(scale);
        }

        /// <summary>
        /// Writes DateTime in protobuf-net BCL format (nested message with value/scale fields).
        /// Wire format: [length][field 1: tag 0x08][sint64 value][field 2: tag 0x10][int32 scale]
        /// Uses optimal Scale to minimize wire size (Seconds for API timestamps, Ticks for high-precision).
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
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldValueTag);
            WriteZigZagVarInt64(scaledValue);

            // Write field 2: scale (int32)
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldScaleTag);
            WriteVarInt32(scale);

            // Level200: field 3 (kind) is NOT written
        }

        /// <summary>
        /// Helper method to calculate varint size for unsigned values.
        /// Delegates to WireFormatHelpers for canonical implementation.
        /// </summary>
        private static int GetVarintSize(uint value)
        {
            return WireFormatHelpers.GetVarintSize(value);
        }

        /// <summary>
        /// Helper method to calculate ZigZag varint size for signed values.
        /// Delegates to WireFormatHelpers for canonical implementation.
        /// </summary>
        private static int GetZigZagVarintSize(long value)
        {
            return WireFormatHelpers.GetZigZagVarintSize(value);
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
            WriteVarUInt64(WireFormatHelpers.EncodeZigZag64(value));
        }

        public void WriteFixed64(double value)
        {
            EnsureBufferSpace(8);
            var span = buffer.Slice(bufferPosition);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value); // natívna endianita
            bufferPosition += 8;
        }

        public void WriteFixed32(float value)
        {
            EnsureBufferSpace(4);
            var span = buffer.Slice(bufferPosition);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value); // natívna endianita
            bufferPosition += 4;
        }

        public void WriteFixed64(long value)
        {
            EnsureBufferSpace(8);
            var span = buffer.Slice(bufferPosition);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value);
            bufferPosition += 8;
        }

        public void WriteFixed64(ulong value)
        {
            EnsureBufferSpace(8);
            var span = buffer.Slice(bufferPosition);
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), value);
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingleByte(byte value)
        {
            if ((uint)bufferPosition >= (uint)buffer.Length)
                Flush();

            Unsafe.WriteUnaligned(ref RefAt(bufferPosition), value);
            bufferPosition++;
        }

        public void WriteTwoBytes(byte b0, byte b1)
        {
            if (buffer.Length - bufferPosition >= 2)
            {
                ref byte p = ref RefAt(bufferPosition);
                Unsafe.WriteUnaligned(ref p, b0);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref p, 1), b1);
                bufferPosition += 2;
            }
            else
            {
                WriteSingleByte(b0);
                WriteSingleByte(b1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flush()
        {
            if (bufferPosition > 0)
            {
                Stream.Write(buffer.Slice(0, bufferPosition));
                bufferPosition = 0;
            }
        }

        #region Nested Sub-Message Support

        /// <summary>
        /// Writes a varint using exactly <paramref name="targetBytes"/> bytes,
        /// padding with continuation bits if the value would fit in fewer bytes.
        /// </summary>
        public void WriteVarUInt32Padded(uint value, int targetBytes)
        {
            for (int i = 0; i < targetBytes - 1; i++)
            {
                WriteSingleByte((byte)((value & 0x7Fu) | 0x80u));
                value >>= 7;
            }
            WriteSingleByte((byte)(value & 0x7Fu));
        }

        /// <summary>
        /// Begins a nested sub-message. Reserves space for the length prefix.
        /// Call <see cref="EndSubMessage"/> after writing the sub-message content.
        /// </summary>
        /// <param name="reservedLengthBytes">
        /// How many bytes to reserve for the length varint (1-5).
        /// 1 byte covers lengths 0-127, 2 bytes up to 16383, etc.
        /// </param>

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void BeginSubMessage(int _ = 2)
        {
            Flush();
            nestingStack[nestingDepth++] = new NestingFrame(Stream);
            Stream = MemoryStreamPool.Shared.Get();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void EndSubMessage()
        {
            Flush();

            var frame = nestingStack[--nestingDepth];
            var childStream = Stream;
            Stream = frame.ParentStream;

            int contentLength = (int)childStream.Length;

            // 95%+ of nested IoT messages are <128 bytes
            if (contentLength <= 127)
            {
                // Fast path: single-byte length (no varint encoding needed)
                WriteSingleByte((byte)contentLength);
                Flush();
            }
            else
            {
                // Slow path: full varint
                WriteVarUInt32((uint)contentLength);
                Flush();
            }

            // Copy child content to parent stream
            childStream.Position = 0;
            childStream.CopyTo(Stream);

            MemoryStreamPool.Shared.Return((MemoryStream)childStream);
        }

        #endregion

        #region Field Writing Helpers (tag + default check + write combined)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStringField(byte tag, string value)
        { if (value != null) { WriteSingleByte(tag); WriteString(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteStringField(byte tag1, byte tag2, string value)
        { if (value != null) { WriteTwoBytes(tag1, tag2); WriteString(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesField(byte tag, byte[] value)
        { if (value != null) { WriteSingleByte(tag); WriteVarUInt32((uint)value.Length); WriteBytes(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesField(byte tag1, byte tag2, byte[] value)
        { if (value != null) { WriteTwoBytes(tag1, tag2); WriteVarUInt32((uint)value.Length); WriteBytes(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesField(byte tag, scoped ReadOnlySpan<byte> value)
        { WriteSingleByte(tag); WriteVarUInt32((uint)value.Length); WriteBytes(value); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytesField(byte tag1, byte tag2, scoped ReadOnlySpan<byte> value)
        { WriteTwoBytes(tag1, tag2); WriteVarUInt32((uint)value.Length); WriteBytes(value); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt32Field(byte tag, int value)
        { if (value != 0) { WriteSingleByte(tag); WriteVarInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt32Field(byte tag1, byte tag2, int value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteVarInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32Field(byte tag, uint value)
        { if (value != 0) { WriteSingleByte(tag); WriteVarUInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32Field(byte tag1, byte tag2, uint value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteVarUInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt64Field(byte tag, long value)
        { if (value != 0) { WriteSingleByte(tag); WriteVarInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt64Field(byte tag1, byte tag2, long value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteVarInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt64Field(byte tag, ulong value)
        { if (value != 0) { WriteSingleByte(tag); WriteVarUInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt64Field(byte tag1, byte tag2, ulong value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteVarUInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolField(byte tag, bool value)
        { if (value) { WriteSingleByte(tag); WriteBool(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolField(byte tag1, byte tag2, bool value)
        { if (value) { WriteTwoBytes(tag1, tag2); WriteBool(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDoubleField(byte tag, double value)
        { if (value != 0) { WriteSingleByte(tag); WriteDouble(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDoubleField(byte tag1, byte tag2, double value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteDouble(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloatField(byte tag, float value)
        { if (value != 0) { WriteSingleByte(tag); WriteFloat(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloatField(byte tag1, byte tag2, float value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteFloat(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag32Field(byte tag, int value)
        { if (value != 0) { WriteSingleByte(tag); WriteZigZag32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag32Field(byte tag1, byte tag2, int value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteZigZag32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag64Field(byte tag, long value)
        { if (value != 0) { WriteSingleByte(tag); WriteZigZagVarInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag64Field(byte tag1, byte tag2, long value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteZigZagVarInt64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedInt32Field(byte tag, int value)
        { if (value != 0) { WriteSingleByte(tag); WriteFixedSizeInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedInt32Field(byte tag1, byte tag2, int value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteFixedSizeInt32(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedInt64Field(byte tag, long value)
        { if (value != 0) { WriteSingleByte(tag); WriteFixed64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedInt64Field(byte tag1, byte tag2, long value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteFixed64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedUInt64Field(byte tag, ulong value)
        { if (value != 0) { WriteSingleByte(tag); WriteFixed64(value); } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedUInt64Field(byte tag1, byte tag2, ulong value)
        { if (value != 0) { WriteTwoBytes(tag1, tag2); WriteFixed64(value); } }

        #endregion
    }
}
