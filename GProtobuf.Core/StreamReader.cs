using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GProtobuf.Core
{
    /// <summary>
    /// Extension methods for StreamReader providing type-aware deserialization.
    /// Mirrors SpanReaders API for consistency.
    /// </summary>
    public static class StreamReaders
    {
        /// <summary>
        /// Reads a double value, supporting multiple wire type coercions.
        /// </summary>
        public static double ReadDouble(this ref StreamReader reader, WireType wireType)
        {
            if (wireType == WireType.Fixed64b)
            {
                return reader.ReadFixedDouble();
            }
            else if (wireType == WireType.Fixed32b)
            {
                return reader.ReadFixedFloat();
            }
            else if (wireType == WireType.VarInt)
            {
                return reader.ReadVarInt32();
            }

            throw new InvalidOperationException($"WireType {wireType} is not valid for double.");
        }

        /// <summary>
        /// Reads a UTF-8 encoded string (WireType.Len).
        /// </summary>
        public static string ReadString(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for string.");

            int length = reader.ReadVarInt32();
            return Encoding.UTF8.GetString(reader.GetSlice(length));
        }

        /// <summary>
        /// Reads a boolean value (WireType.VarInt).
        /// </summary>
        public static bool ReadBool(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for bool.");

            return reader.ReadVarInt32() != 0;
        }

        /// <summary>
        /// Reads an unsigned byte value (WireType.VarInt).
        /// </summary>
        public static byte ReadByte(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for byte.");

            uint value = reader.ReadVarUInt32();
            if (value > byte.MaxValue)
                throw new OverflowException($"Value {value} is out of range for byte.");

            return (byte)value;
        }

        /// <summary>
        /// Reads a signed byte value (WireType.VarInt), optionally with ZigZag encoding.
        /// </summary>
        public static sbyte ReadSByte(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for sbyte.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (sbyte)value;
        }

        /// <summary>
        /// Reads a signed 16-bit integer (WireType.VarInt), optionally with ZigZag encoding.
        /// </summary>
        public static short ReadInt16(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for short.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (short)value;
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer (WireType.VarInt).
        /// </summary>
        public static ushort ReadUInt16(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for ushort.");

            uint value = reader.ReadVarUInt32();
            if (value > ushort.MaxValue)
                throw new OverflowException($"Value {value} is out of range for ushort.");

            return (ushort)value;
        }

        /// <summary>
        /// Reads a signed 32-bit integer, supporting multiple wire types and ZigZag encoding.
        /// </summary>
        public static int ReadInt32(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType == WireType.VarInt)
            {
                return zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            }
            else if (wireType == WireType.Fixed32b)
            {
                return reader.ReadFixedInt32();
            }

            throw new InvalidOperationException($"Unexpected wire type {wireType} for int32.");
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer (WireType.VarInt).
        /// </summary>
        public static uint ReadUInt32(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for uint.");

            return reader.ReadVarUInt32();
        }

        /// <summary>
        /// Reads a signed 64-bit integer (WireType.VarInt), optionally with ZigZag encoding.
        /// </summary>
        public static long ReadInt64(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for long.");

            return zigZag ? reader.ReadZigZagVarInt64() : reader.ReadVarInt64();
        }

        /// <summary>
        /// Reads an unsigned 64-bit integer (WireType.VarInt).
        /// </summary>
        public static ulong ReadUInt64(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for ulong.");

            return reader.ReadVarUInt64();
        }

        /// <summary>
        /// Reads a float value, supporting multiple wire type coercions.
        /// </summary>
        public static float ReadFloat(this ref StreamReader reader, WireType wireType)
        {
            if (wireType == WireType.Fixed32b)
            {
                return reader.ReadFixedFloat();
            }
            else if (wireType == WireType.VarInt)
            {
                return reader.ReadVarInt32();
            }

            throw new InvalidOperationException($"Unexpected wire type {wireType} for float.");
        }

        /// <summary>
        /// Reads a byte array (WireType.Len).
        /// </summary>
        public static byte[] ReadByteArray(this ref StreamReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length).ToArray();
        }

        /// <summary>
        /// Reads a byte array span (WireType.Len).
        /// </summary>
        public static ReadOnlySpan<byte> ReadByteArraySpan(this ref StreamReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length);
        }

        /// <summary>
        /// Reads Guid in protobuf-net BCL format (nested message with lo/hi fixed64 fields).
        /// </summary>
        public static Guid ReadGuid(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for Guid, got {wireType}");

            int length = reader.ReadVarInt32();

            if (length == 0)
                return Guid.Empty;

            if (length != BclTypeFormats.Guid.NestedContentSize)
                throw new InvalidDataException($"Expected Guid BCL nested message length of {BclTypeFormats.Guid.NestedContentSize} bytes, got {length}");

            // Read nested content into SpanReader for parsing
            var nestedReader = reader.CreateSubReader(length);

            // Initialize byte buffer for Guid construction
            Span<byte> guidBytes = stackalloc byte[ProtobufConstants.GuidByteSize];
            bool hasLo = false;
            bool hasHi = false;

            // Parse nested message fields (support field order independence)
            while (!nestedReader.IsEnd)
            {
                nestedReader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.Guid.FieldLoNumber:
                        if (innerWireType != WireType.Fixed64b)
                            throw new InvalidDataException($"Expected Fixed64 for Guid.lo, got {innerWireType}");
                        var loSlice = nestedReader.GetSlice(8);
                        loSlice.CopyTo(guidBytes.Slice(0, 8));
                        hasLo = true;
                        break;

                    case BclTypeFormats.Guid.FieldHiNumber:
                        if (innerWireType != WireType.Fixed64b)
                            throw new InvalidDataException($"Expected Fixed64 for Guid.hi, got {innerWireType}");
                        var hiSlice = nestedReader.GetSlice(8);
                        hiSlice.CopyTo(guidBytes.Slice(8, 8));
                        hasHi = true;
                        break;

                    default:
                        nestedReader.SkipField(innerWireType);
                        break;
                }
            }

            if (!hasLo || !hasHi)
                throw new InvalidDataException($"Incomplete Guid BCL format: hasLo={hasLo}, hasHi={hasHi}");

            return new Guid(guidBytes);
        }

        /// <summary>
        /// Reads TimeSpan in protobuf-net BCL format.
        /// </summary>
        public static TimeSpan ReadTimeSpan(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for TimeSpan, got {wireType}");

            int length = reader.ReadVarInt32();
            var nestedReader = reader.CreateSubReader(length);

            long scaledValue = 0;
            int scale = 5;

            while (!nestedReader.IsEnd)
            {
                nestedReader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.DateTimeTimeSpan.FieldValueNumber:
                        scaledValue = nestedReader.ReadZigZagVarInt64();
                        break;
                    case BclTypeFormats.DateTimeTimeSpan.FieldScaleNumber:
                        scale = nestedReader.ReadVarInt32();
                        break;
                    default:
                        nestedReader.SkipField(innerWireType);
                        break;
                }
            }

            long ticks = DateTimeHelper.ConvertTimeSpanToTicks(scaledValue, scale);
            return new TimeSpan(ticks);
        }

        /// <summary>
        /// Reads DateTime in protobuf-net BCL format.
        /// </summary>
        public static DateTime ReadDateTime(this ref StreamReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for DateTime, got {wireType}");

            int length = reader.ReadVarInt32();
            var nestedReader = reader.CreateSubReader(length);

            long scaledValue = 0;
            int scale = 5;

            while (!nestedReader.IsEnd)
            {
                nestedReader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.DateTimeTimeSpan.FieldValueNumber:
                        scaledValue = nestedReader.ReadZigZagVarInt64();
                        break;
                    case BclTypeFormats.DateTimeTimeSpan.FieldScaleNumber:
                        scale = nestedReader.ReadVarInt32();
                        break;
                    case BclTypeFormats.DateTimeTimeSpan.FieldKindNumber:
                        nestedReader.SkipField(innerWireType);
                        break;
                    default:
                        nestedReader.SkipField(innerWireType);
                        break;
                }
            }

            long ticks = DateTimeHelper.ConvertToTicks(scaledValue, scale);
            return new DateTime(ticks, DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Reads a boolean value as a varint.
        /// </summary>
        public static bool ReadBool(this ref StreamReader reader)
        {
            return reader.ReadVarInt32() != 0;
        }
    }


    /// <summary>
    /// High-performance Protocol Buffers deserializer for Stream input.
    /// Uses internal buffer for efficient sequential reading.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Rationale:</b></para>
    /// - ref struct: Cannot escape to heap, enables aggressive JIT optimizations
    /// - Internal buffering: Minimizes Stream.Read calls
    /// - CreateSubReader returns SpanReader: Zero-copy for nested messages within buffer
    /// - 4KB default buffer: L1 cache friendly, optimal for network I/O
    /// </remarks>
    [SkipLocalsInit]
    public ref struct StreamReader
    {
        private Stream _stream;
        private Span<byte> _buffer;
        private int _bufferPos;      // current read position in buffer
        private int _bufferLen;      // valid bytes in buffer
        private bool _endOfStream;   // true when Stream.Read returned 0

        /// <summary>
        /// Default buffer size (4KB).
        /// </summary>
        public const int DefaultBufferSize = 4096;

        /// <summary>
        /// Initializes a new StreamReader over the specified stream.
        /// </summary>
        /// <param name="stream">Source stream to read from.</param>
        /// <param name="buffer">Buffer for internal use (stackalloc recommended).</param>
        public StreamReader(Stream stream, scoped Span<byte> buffer)
        {
            _stream = stream;
            unsafe
            {
#pragma warning disable CS9080
                _buffer = buffer;
#pragma warning restore CS9080
            }
            _bufferPos = 0;
            _bufferLen = 0;
            _endOfStream = false;
        }

        /// <summary>
        /// Gets whether the reader has reached the end of data.
        /// Proactively tries to read more data if buffer is empty.
        /// </summary>
        public bool IsEnd
        {
            get
            {
                if (_bufferPos < _bufferLen)
                    return false;
                if (_endOfStream)
                    return true;
                // Buffer is empty but we haven't confirmed end of stream yet
                // Try to read more data to determine if stream is exhausted
                TryFillBuffer();
                return _bufferPos >= _bufferLen && _endOfStream;
            }
        }

        /// <summary>
        /// Attempts to fill the buffer with more data from the stream.
        /// Sets _endOfStream if no more data is available.
        /// </summary>
        private void TryFillBuffer()
        {
            // Move remaining bytes to start of buffer
            int remaining = _bufferLen - _bufferPos;
            if (remaining > 0 && _bufferPos > 0)
            {
                _buffer.Slice(_bufferPos, remaining).CopyTo(_buffer);
            }
            _bufferPos = 0;
            _bufferLen = remaining;

            // Try to read more data from stream
            int read = _stream.Read(_buffer.Slice(_bufferLen));
            if (read == 0)
            {
                _endOfStream = true;
            }
            else
            {
                _bufferLen += read;
            }
        }

        /// <summary>
        /// Gets whether the reader has reached the end of data.
        /// </summary>
        public bool EndOfData => IsEnd;

        /// <summary>
        /// Gets the number of bytes remaining in the current buffer.
        /// </summary>
        public int Remaining => _bufferLen - _bufferPos;

        #region Buffer Management

        /// <summary>
        /// Ensures that at least the specified number of bytes are available in the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureBytes(int count)
        {
            if (_bufferPos + count <= _bufferLen)
                return;

            EnsureBytesSlow(count);
        }

        /// <summary>
        /// Slow path for EnsureBytes when buffer needs refilling.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureBytesSlow(int count)
        {
            // Move remaining bytes to start of buffer
            int remaining = _bufferLen - _bufferPos;
            if (remaining > 0 && _bufferPos > 0)
            {
                _buffer.Slice(_bufferPos, remaining).CopyTo(_buffer);
            }
            _bufferPos = 0;
            _bufferLen = remaining;

            // Read more data from stream
            while (_bufferLen < count && !_endOfStream)
            {
                int read = _stream.Read(_buffer.Slice(_bufferLen));
                if (read == 0)
                {
                    _endOfStream = true;
                    break;
                }
                _bufferLen += read;
            }

            if (_bufferLen < count)
                throw new EndOfStreamException($"Expected {count} bytes but only {_bufferLen} available");
        }

        #endregion

        #region Slice Operations

        /// <summary>
        /// Gets a slice of the specified length and advances position.
        /// Returns a view into the internal buffer (zero-copy).
        /// </summary>
        public ReadOnlySpan<byte> GetSlice(int length)
        {
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            EnsureBytes(length);
            var slice = _buffer.Slice(_bufferPos, length);
            _bufferPos += length;
            return slice;
        }

        /// <summary>
        /// Creates a SpanReader for reading a nested message.
        /// </summary>
        /// <param name="length">Length of the nested message.</param>
        /// <returns>SpanReader positioned over the nested message data.</returns>
        public SpanReader CreateSubReader(int length)
        {
            return new SpanReader(GetSlice(length));
        }

        #endregion

        #region VarInt Reading

        /// <summary>
        /// Reads a varint-encoded int32.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadVarInt32()
        {
            // Fast path: single byte (90% of cases)
            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return b;

            return ReadVarInt32Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int ReadVarInt32Slow(byte firstByte)
        {
            long result = firstByte & ProtobufConstants.VarintValueMask;

            // Bytes 2-5 (unrolled)
            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return (int)(result | ((long)b << ProtobufConstants.VarintShift));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 2)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 3)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 4)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4);

            // Bytes 6-10 (rare)
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                EnsureBytes(1);
                b = _buffer[_bufferPos++];
                if (b < ProtobufConstants.VarintContinuationBit)
                    return (int)(result | ((long)b << shift));
                result |= (long)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException("Malformed varint: exceeded maximum length");
        }

        /// <summary>
        /// Reads a varint-encoded uint32.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt32()
        {
            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return b;

            return ReadVarUInt32Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private uint ReadVarUInt32Slow(byte firstByte)
        {
            uint result = (uint)(firstByte & ProtobufConstants.VarintValueMask);

            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return result | ((uint)b << ProtobufConstants.VarintShift);
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return result | ((uint)b << (ProtobufConstants.VarintShift * 2));
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return result | ((uint)b << (ProtobufConstants.VarintShift * 3));
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            EnsureBytes(1);
            b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return result | ((uint)b << (ProtobufConstants.VarintShift * 4));

            // Handle overflow for bytes 6-10
            ulong result64 = result | ((ulong)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4));
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                EnsureBytes(1);
                b = _buffer[_bufferPos++];
                if (b < ProtobufConstants.VarintContinuationBit)
                    return (uint)result64;
                result64 |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException("Malformed varint: exceeded maximum length");
        }

        /// <summary>
        /// Reads a ZigZag-encoded int32.
        /// </summary>
        public int ReadZigZagVarInt32()
        {
            uint encoded = ReadVarUInt32();
            return WireFormatHelpers.DecodeZigZag32(encoded);
        }

        /// <summary>
        /// Reads a varint-encoded int64.
        /// </summary>
        public long ReadVarInt64()
        {
            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return b;

            return ReadVarInt64Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private long ReadVarInt64Slow(byte firstByte)
        {
            long result = firstByte & ProtobufConstants.VarintValueMask;

            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                if (b < ProtobufConstants.VarintContinuationBit)
                    return result | ((long)b << shift);
                result |= (long)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException("Malformed varint: exceeded maximum length");
        }

        /// <summary>
        /// Reads a ZigZag-encoded int64.
        /// </summary>
        public long ReadZigZagVarInt64()
        {
            ulong encoded = ReadVarUInt64();
            return WireFormatHelpers.DecodeZigZag64(encoded);
        }

        /// <summary>
        /// Reads a varint-encoded uint64.
        /// </summary>
        public ulong ReadVarUInt64()
        {
            EnsureBytes(1);
            byte b = _buffer[_bufferPos++];
            if (b < ProtobufConstants.VarintContinuationBit)
                return b;

            return ReadVarUInt64Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ulong ReadVarUInt64Slow(byte firstByte)
        {
            ulong result = (ulong)(firstByte & ProtobufConstants.VarintValueMask);

            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                if (b < ProtobufConstants.VarintContinuationBit)
                    return result | ((ulong)b << shift);
                result |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException("Malformed varint: exceeded maximum length");
        }

        #endregion

        #region Fixed Size Reading

        /// <summary>
        /// Reads an 8-byte double (little-endian).
        /// </summary>
        public double ReadFixedDouble()
        {
            EnsureBytes(8);
            var result = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Slice(_bufferPos, 8));
            _bufferPos += 8;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte float (little-endian).
        /// </summary>
        public float ReadFixedFloat()
        {
            EnsureBytes(4);
            var result = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_bufferPos, 4));
            _bufferPos += 4;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte signed integer (little-endian).
        /// </summary>
        public int ReadFixedInt32()
        {
            EnsureBytes(4);
            var result = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_bufferPos, 4));
            _bufferPos += 4;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte unsigned integer (little-endian).
        /// </summary>
        public uint ReadFixedUInt32()
        {
            EnsureBytes(4);
            var result = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_bufferPos, 4));
            _bufferPos += 4;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte signed integer (little-endian).
        /// </summary>
        public long ReadFixedInt64()
        {
            EnsureBytes(8);
            var result = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Slice(_bufferPos, 8));
            _bufferPos += 8;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (little-endian).
        /// </summary>
        public ulong ReadFixedUInt64()
        {
            EnsureBytes(8);
            var result = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_bufferPos, 8));
            _bufferPos += 8;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (fixed64).
        /// </summary>
        public ulong ReadFixed64()
        {
            return ReadFixedUInt64();
        }

        /// <summary>
        /// Reads a 2-byte signed integer (little-endian).
        /// </summary>
        public short ReadFixedInt16()
        {
            EnsureBytes(2);
            var result = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Slice(_bufferPos, 2));
            _bufferPos += 2;
            return result;
        }

        /// <summary>
        /// Reads a 2-byte unsigned integer (little-endian).
        /// </summary>
        public ushort ReadFixedUInt16()
        {
            EnsureBytes(2);
            var result = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_bufferPos, 2));
            _bufferPos += 2;
            return result;
        }

        #endregion

        #region Wire Format

        /// <summary>
        /// Reads wire type and field ID from a tag.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadWireTypeAndFieldId(out WireType wireType, out int fieldId)
        {
            uint tag = ReadVarUInt32();
            WireFormatHelpers.DecodeTag(tag, out fieldId, out wireType);
        }

        /// <summary>
        /// Reads wire type and field ID as tuple.
        /// </summary>
        public (WireType wireType, int fieldId) ReadWireTypeAndFieldId()
        {
            uint tag = ReadVarUInt32();
            var (fieldNumber, wireType) = WireFormatHelpers.DecodeTag(tag);
            return (wireType, fieldNumber);
        }

        /// <summary>
        /// Reads a tag and returns field ID and wire type.
        /// </summary>
        public (WireType wireType, int fieldId) ReadKey()
        {
            return ReadWireTypeAndFieldId();
        }

        /// <summary>
        /// Skips an unknown field based on wire type.
        /// </summary>
        public void SkipField(WireType wireType)
        {
            switch (wireType)
            {
                case WireType.VarInt:
                    _ = ReadVarInt32();
                    break;

                case WireType.Fixed32b:
                    EnsureBytes(4);
                    _bufferPos += 4;
                    break;

                case WireType.Fixed64b:
                    EnsureBytes(8);
                    _bufferPos += 8;
                    break;

                case WireType.Len:
                    int length = ReadVarInt32();
                    EnsureBytes(length);
                    _bufferPos += length;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown WireType: {wireType}");
            }
        }

        /// <summary>
        /// Validates that the specified number of bytes are available.
        /// </summary>
        public void CheckLength(int length)
        {
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            EnsureBytes(length);
        }

        #endregion

        #region Collection Reading (Packed Arrays)

        /// <summary>
        /// Reads a packed varint int32 array.
        /// </summary>
        public int[] ReadPackedVarIntInt32Array(bool zigZag)
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<int>();

            EnsureBytes(length);

            // Create sub-reader for counting and reading
            var slice = _buffer.Slice(_bufferPos, length);
            var subReader = new SpanReader(slice);

            // Count elements first
            int count = 0;
            int tempPos = 0;
            while (tempPos < length)
            {
                byte b = slice[tempPos++];
                while (b >= 0x80 && tempPos < length)
                    b = slice[tempPos++];
                count++;
            }

            // Read elements
            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = zigZag ? subReader.ReadZigZagVarInt32() : subReader.ReadVarInt32();
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size int32 array.
        /// </summary>
        public int[] ReadPackedFixedSizeInt32Array()
        {
            uint length = ReadVarUInt32();
            if (length == 0)
                return Array.Empty<int>();

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size array length.");

            EnsureBytes((int)length);

            int count = (int)(length / 4);
            int[] result = new int[count];

            var slice = _buffer.Slice(_bufferPos, (int)length);
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(i * 4, 4));
            }

            _bufferPos += (int)length;
            return result;
        }

        /// <summary>
        /// Reads a packed float array.
        /// </summary>
        public float[] ReadPackedFloatArray()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<float>();

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed float array length.");

            EnsureBytes(length);

            int count = length / 4;
            float[] result = new float[count];

            var slice = _buffer.Slice(_bufferPos, length);
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(i * 4, 4));
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed double array.
        /// </summary>
        public double[] ReadPackedDoubleArray()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<double>();

            if (length % 8 != 0)
                throw new InvalidOperationException("Invalid packed double array length.");

            EnsureBytes(length);

            int count = length / 8;
            double[] result = new double[count];

            var slice = _buffer.Slice(_bufferPos, length);
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadDoubleLittleEndian(slice.Slice(i * 8, 8));
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed varint int64 array.
        /// </summary>
        public long[] ReadPackedVarIntInt64Array(bool zigZag)
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<long>();

            EnsureBytes(length);

            var slice = _buffer.Slice(_bufferPos, length);
            var subReader = new SpanReader(slice);

            // Count elements
            int count = 0;
            int tempPos = 0;
            while (tempPos < length)
            {
                byte b = slice[tempPos++];
                while (b >= 0x80 && tempPos < length)
                    b = slice[tempPos++];
                count++;
            }

            long[] result = new long[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = zigZag ? subReader.ReadZigZagVarInt64() : subReader.ReadVarInt64();
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size int64 array.
        /// </summary>
        public long[] ReadPackedFixedSizeInt64Array()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<long>();

            if (length % 8 != 0)
                throw new InvalidOperationException("Invalid packed fixed size long array length.");

            EnsureBytes(length);

            int count = length / 8;
            long[] result = new long[count];

            var slice = _buffer.Slice(_bufferPos, length);
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadInt64LittleEndian(slice.Slice(i * 8, 8));
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed boolean array.
        /// </summary>
        public bool[] ReadPackedBoolArray()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<bool>();

            EnsureBytes(length);

            var slice = _buffer.Slice(_bufferPos, length);
            var subReader = new SpanReader(slice);

            // Count elements
            int count = 0;
            int tempPos = 0;
            while (tempPos < length)
            {
                byte b = slice[tempPos++];
                while (b >= 0x80 && tempPos < length)
                    b = slice[tempPos++];
                count++;
            }

            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarInt32() != 0;
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed uint32 array.
        /// </summary>
        public uint[] ReadPackedUInt32Array()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<uint>();

            EnsureBytes(length);

            var slice = _buffer.Slice(_bufferPos, length);
            var subReader = new SpanReader(slice);

            // Count elements
            int count = 0;
            int tempPos = 0;
            while (tempPos < length)
            {
                byte b = slice[tempPos++];
                while (b >= 0x80 && tempPos < length)
                    b = slice[tempPos++];
                count++;
            }

            uint[] result = new uint[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarUInt32();
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads a packed ulong array.
        /// </summary>
        public ulong[] ReadPackedUInt64Array()
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<ulong>();

            EnsureBytes(length);

            var slice = _buffer.Slice(_bufferPos, length);
            var subReader = new SpanReader(slice);

            // Count elements
            int count = 0;
            int tempPos = 0;
            while (tempPos < length)
            {
                byte b = slice[tempPos++];
                while (b >= 0x80 && tempPos < length)
                    b = slice[tempPos++];
                count++;
            }

            ulong[] result = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarUInt64();
            }

            _bufferPos += length;
            return result;
        }

        /// <summary>
        /// Reads bytes directly from buffer.
        /// </summary>
        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            EnsureBytes(length);
            var slice = _buffer.Slice(_bufferPos, length);
            _bufferPos += length;
            return slice;
        }

        /// <summary>
        /// Reads a byte array with length prefix.
        /// </summary>
        public byte[] ReadByteArray()
        {
            int length = ReadVarInt32();
            EnsureBytes(length);
            var slice = _buffer.Slice(_bufferPos, length);
            _bufferPos += length;
            return slice.ToArray();
        }

        /// <summary>
        /// Reads all remaining bytes from the buffer and stream.
        /// Used for delegating to SpanReaders when full buffering is acceptable.
        /// </summary>
        public byte[] ReadRemainingBytes()
        {
            // Get remaining bytes in buffer
            int bufferedRemaining = _bufferLen - _bufferPos;

            if (_endOfStream)
            {
                // No more data from stream, just return buffered data
                if (bufferedRemaining == 0)
                    return Array.Empty<byte>();

                var result = _buffer.Slice(_bufferPos, bufferedRemaining).ToArray();
                _bufferPos = _bufferLen;
                return result;
            }

            // Need to read remaining stream data
            using var memoryStream = new MemoryStream();

            // First write buffered data
            if (bufferedRemaining > 0)
            {
                memoryStream.Write(_buffer.Slice(_bufferPos, bufferedRemaining));
                _bufferPos = _bufferLen;
            }

            // Read remaining stream data in chunks
            Span<byte> tempBuffer = stackalloc byte[4096];
            int read;
            while ((read = _stream.Read(tempBuffer)) > 0)
            {
                memoryStream.Write(tempBuffer.Slice(0, read));
            }

            _endOfStream = true;
            return memoryStream.ToArray();
        }

        #endregion
    }
}
