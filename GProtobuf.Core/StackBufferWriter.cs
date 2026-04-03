using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GProtobuf.Core
{
    /// <summary>
    /// Ultra-lightweight stack-only Protocol Buffer writer for small messages.
    /// Writes directly to a Span&lt;byte&gt; with zero heap allocations.
    /// </summary>
    /// <remarks>
    /// <para><b>Design for IoT:</b></para>
    /// - Zero heap allocations (ideal for battery-powered devices)
    /// - No IBufferWriter overhead (direct span access)
    /// - Deterministic timing (no GC pauses)
    /// - Suitable for messages &lt;512 bytes (typical IoT telemetry)
    ///
    /// <para><b>Usage:</b></para>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[256];
    /// var writer = new StackBufferWriter(buffer);
    /// writer.WriteTag(1, WireType.VarInt);
    /// writer.WriteVarInt32(42);
    /// var result = writer.WrittenSpan; // Use the written bytes
    /// </code>
    ///
    /// <para><b>Thread Safety:</b></para>
    /// Not thread-safe. Each thread must use separate StackBufferWriter instance.
    /// </remarks>
    [SkipLocalsInit]
    public ref struct StackBufferWriter
    {
        private Span<byte> _buffer;
        private int _position;

        /// <summary>
        /// Initializes a new StackBufferWriter over the specified span.
        /// </summary>
        /// <param name="buffer">Target buffer for writing. Must be pre-allocated (stackalloc or array).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StackBufferWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        /// <summary>
        /// Gets the number of bytes written.
        /// </summary>
        public int Written => _position;

        /// <summary>
        /// Gets the remaining capacity in bytes.
        /// </summary>
        public int Remaining => _buffer.Length - _position;

        /// <summary>
        /// Gets the written portion of the buffer.
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan => _buffer.Slice(0, _position);

        /// <summary>
        /// Writes a protobuf tag (field number + wire type).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTag(int fieldId, WireType wireType)
        {
            uint tag = WireFormatHelpers.EncodeTag(fieldId, wireType);
            WriteVarUInt32(tag);
        }

        /// <summary>
        /// Writes a single byte.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingleByte(byte value)
        {
            _buffer[_position++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTwoBytes(byte b0, byte b1)
        {
            _buffer[_position++] = b0;
            _buffer[_position++] = b1;
        }

        /// <summary>
        /// Writes a single byte (alias for WriteSingleByte).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            _buffer[_position++] = value;
        }

        /// <summary>
        /// Writes an unsigned 32-bit integer using varint encoding.
        /// Optimized: Unrolled thresholds for 1-3 byte varints (90%+ of real-world values).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt32(uint value)
        {
            // Unrolled fast path for 1-3 byte varints (covers 90%+ of values)
            if (value < 0x80u)
            {
                _buffer[_position++] = (byte)value;
                return;
            }
            if (value < 0x4000u)
            {
                _buffer[_position++] = (byte)(value | 0x80u);
                _buffer[_position++] = (byte)(value >> 7);
                return;
            }
            if (value < 0x200000u)
            {
                _buffer[_position++] = (byte)(value | 0x80u);
                _buffer[_position++] = (byte)((value >> 7) | 0x80u);
                _buffer[_position++] = (byte)(value >> 14);
                return;
            }

            WriteVarUInt32Slow(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WriteVarUInt32Slow(uint value)
        {
            while (value >= 0x80u)
            {
                _buffer[_position++] = (byte)(value | 0x80u);
                value >>= 7;
            }
            _buffer[_position++] = (byte)value;
        }

        /// <summary>
        /// Writes a signed 32-bit integer using varint encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt32(int value)
        {
            WriteVarUInt32((uint)value);
        }

        /// <summary>
        /// Writes an unsigned 64-bit integer using varint encoding.
        /// </summary>
        public void WriteVarUInt64(ulong value)
        {
            while (value >= 0x80)
            {
                _buffer[_position++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_position++] = (byte)value;
        }

        /// <summary>
        /// Writes a signed 64-bit integer using varint encoding.
        /// </summary>
        public void WriteVarint64(long value)
        {
            WriteVarUInt64((ulong)value);
        }

        /// <summary>
        /// Writes a signed 32-bit integer using ZigZag encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZagInt32(int value)
        {
            WriteVarUInt32(WireFormatHelpers.EncodeZigZag32(value));
        }

        /// <summary>
        /// Writes a signed 32-bit integer using ZigZag encoding (alias).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag32(int value)
        {
            WriteZigZagInt32(value);
        }

        /// <summary>
        /// Writes a signed 64-bit integer using ZigZag encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZagInt64(long value)
        {
            WriteVarUInt64(WireFormatHelpers.EncodeZigZag64(value));
        }

        /// <summary>
        /// Writes a signed 64-bit integer using ZigZag encoding (alias).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteZigZag64(long value)
        {
            WriteZigZagInt64(value);
        }

        /// <summary>
        /// Writes a fixed 32-bit unsigned integer (4 bytes, little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixed32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position), value);
            _position += 4;
        }

        /// <summary>
        /// Writes a fixed 32-bit signed integer (4 bytes, little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixedSizeInt32(int value)
        {
            WriteFixed32((uint)value);
        }

        /// <summary>
        /// Writes a fixed 64-bit unsigned integer (8 bytes, little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixed64(ulong value)
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(_buffer.Slice(_position)), value);
            _position += 8;
        }

        /// <summary>
        /// Writes a fixed 64-bit signed integer (8 bytes, little-endian).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixed64(long value)
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(_buffer.Slice(_position)), value);
            _position += 8;
        }

        /// <summary>
        /// Writes a double value (8 bytes, IEEE 754).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixed64(double value)
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(_buffer.Slice(_position)), value);
            _position += 8;
        }

        /// <summary>
        /// Writes a float value (4 bytes, IEEE 754).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFixed32(float value)
        {
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(_buffer.Slice(_position)), value);
            _position += 4;
        }

        /// <summary>
        /// Writes a float value using fixed 32-bit encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            WriteFixed32(BitConverter.SingleToUInt32Bits(value));
        }

        /// <summary>
        /// Writes a double value using fixed 64-bit encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
        }

        /// <summary>
        /// Writes a UTF-8 encoded string with length prefix.
        /// </summary>
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteVarUInt32(0);
                return;
            }

            // ASCII fast path
            if (value.Length < 128 && System.Text.Ascii.IsValid(value))
            {
                WriteVarUInt32((uint)value.Length);
                for (int i = 0; i < value.Length; i++)
                    _buffer[_position++] = (byte)value[i];
                return;
            }

            // UTF-8 path
            int maxBytes = value.Length * 4;
            Span<byte> tempBuffer = maxBytes <= 256
                ? stackalloc byte[maxBytes]
                : new byte[maxBytes]; // Rare fallback for very long strings

            int bytesWritten = Encoding.UTF8.GetBytes(value, tempBuffer);
            WriteVarUInt32((uint)bytesWritten);
            tempBuffer.Slice(0, bytesWritten).CopyTo(_buffer.Slice(_position));
            _position += bytesWritten;
        }

        /// <summary>
        /// Writes raw bytes without length prefix.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(scoped ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(_buffer.Slice(_position));
            _position += bytes.Length;
        }

        /// <summary>
        /// Writes a boolean value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            _buffer[_position++] = value ? (byte)1 : (byte)0;
        }

        /// <summary>
        /// Writes a boolean true value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBoolTrue()
        {
            _buffer[_position++] = 1;
        }

        // Additional methods to match BufferWriter API
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

        public void WriteVarInt64(long value)
        {
            WriteVarint64(value);
        }

        public void WriteVarintUInt64(ulong value)
        {
            WriteVarUInt64(value);
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
        /// Writes Guid in protobuf-net BCL format.
        /// </summary>
        public void WriteGuid(Guid value)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            if (!value.TryWriteBytes(guidBytes))
                throw new InvalidOperationException("Failed to convert Guid to bytes");

            WriteVarUInt32(BclTypeFormats.Guid.NestedContentSize);
            WriteSingleByte(BclTypeFormats.Guid.FieldLoTag);
            guidBytes.Slice(0, 8).CopyTo(_buffer.Slice(_position));
            _position += 8;
            WriteSingleByte(BclTypeFormats.Guid.FieldHiTag);
            guidBytes.Slice(8, 8).CopyTo(_buffer.Slice(_position));
            _position += 8;
        }

        /// <summary>
        /// Writes TimeSpan in protobuf-net BCL format.
        /// </summary>
        public void WriteTimeSpan(TimeSpan value)
        {
            var (scaledValue, scale) = DateTimeHelper.GetOptimalScaleForTimeSpan(value);
            int valueSize = WireFormatHelpers.GetZigZagVarintSize(scaledValue);
            int scaleSize = WireFormatHelpers.GetVarintSize((uint)scale);
            int contentSize = 1 + valueSize + 1 + scaleSize;

            WriteVarUInt32((uint)contentSize);
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldValueTag);
            WriteZigZagVarInt64(scaledValue);
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldScaleTag);
            WriteVarInt32(scale);
        }

        /// <summary>
        /// Writes DateTime in protobuf-net BCL format.
        /// </summary>
        public void WriteDateTime(DateTime value)
        {
            var (scaledValue, scale) = DateTimeHelper.GetOptimalScale(value);
            int valueSize = WireFormatHelpers.GetZigZagVarintSize(scaledValue);
            int scaleSize = WireFormatHelpers.GetVarintSize((uint)scale);
            int contentSize = 1 + valueSize + 1 + scaleSize;

            WriteVarUInt32((uint)contentSize);
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldValueTag);
            WriteZigZagVarInt64(scaledValue);
            WriteSingleByte(BclTypeFormats.DateTimeTimeSpan.FieldScaleTag);
            WriteVarInt32(scale);
        }

        // Packed array methods
        public void WritePackedFixedSizeIntArray(int[] array)
        {
            WriteBytes(MemoryMarshal.Cast<int, byte>(array.AsSpan()));
        }

        public void WritePackedFixedSizeIntList(System.Collections.Generic.List<int> list)
        {
            WriteBytes(MemoryMarshal.Cast<int, byte>(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list)));
        }

        // Precomputed tag support
        public void WritePrecomputedTag(byte tagByte)
        {
            WriteSingleByte(tagByte);
        }

        public void WritePrecomputedTag(ReadOnlySpan<byte> tagBytes)
        {
            tagBytes.CopyTo(_buffer.Slice(_position));
            _position += tagBytes.Length;
        }

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

        #region Custom Buffer Support

        /// <summary>
        /// Gets a span of the specified size for direct writing.
        /// </summary>
        public Span<byte> GetSpan(int size)
        {
            return _buffer.Slice(_position, size);
        }

        /// <summary>
        /// Advances the buffer position by the specified number of bytes.
        /// </summary>
        public void Advance(int count)
        {
            _position += count;
        }

        #endregion
    }
}
