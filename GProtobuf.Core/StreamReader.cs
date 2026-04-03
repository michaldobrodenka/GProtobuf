using System;
using System.Buffers;
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
        #region Primitive Types

        /// <summary>Reads a boolean value (WireType.VarInt).</summary>
        public static bool ReadBool(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.VarInt, "bool");
            return reader.ReadVarInt32() != 0;
        }

        /// <summary>Reads a boolean value as a varint (no wire type validation).</summary>
        public static bool ReadBool(this ref StreamReader reader)
            => reader.ReadVarInt32() != 0;

        /// <summary>Reads an unsigned byte value (WireType.VarInt).</summary>
        public static byte ReadByte(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.VarInt, "byte");
            return ReaderHelpers.ToByte(reader.ReadVarUInt32());
        }

        /// <summary>Reads a signed byte value (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static sbyte ReadSByte(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            ValidateWireType(wireType, WireType.VarInt, "sbyte");
            return (sbyte)(zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32());
        }

        /// <summary>Reads a signed 16-bit integer (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static short ReadInt16(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            ValidateWireType(wireType, WireType.VarInt, "short");
            return (short)(zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32());
        }

        /// <summary>Reads an unsigned 16-bit integer (WireType.VarInt).</summary>
        public static ushort ReadUInt16(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.VarInt, "ushort");
            return ReaderHelpers.ToUInt16(reader.ReadVarUInt32());
        }

        /// <summary>Reads a signed 32-bit integer, supporting VarInt and Fixed32 wire types.</summary>
        public static int ReadInt32(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            return wireType switch
            {
                WireType.VarInt => zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32(),
                WireType.Fixed32b => reader.ReadFixedInt32(),
                _ => throw new InvalidOperationException($"Unexpected wire type {wireType} for int32.")
            };
        }

        /// <summary>Reads an unsigned 32-bit integer (WireType.VarInt).</summary>
        public static uint ReadUInt32(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.VarInt, "uint");
            return reader.ReadVarUInt32();
        }

        /// <summary>Reads a signed 64-bit integer (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static long ReadInt64(this ref StreamReader reader, WireType wireType, bool zigZag = false)
        {
            ValidateWireType(wireType, WireType.VarInt, "long");
            return zigZag ? reader.ReadZigZagVarInt64() : reader.ReadVarInt64();
        }

        /// <summary>Reads an unsigned 64-bit integer (WireType.VarInt).</summary>
        public static ulong ReadUInt64(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.VarInt, "ulong");
            return reader.ReadVarUInt64();
        }

        /// <summary>Reads a float value, supporting Fixed32 and VarInt wire types.</summary>
        public static float ReadFloat(this ref StreamReader reader, WireType wireType)
        {
            return wireType switch
            {
                WireType.Fixed32b => reader.ReadFixedFloat(),
                WireType.VarInt => reader.ReadVarInt32(),
                _ => throw new InvalidOperationException($"Unexpected wire type {wireType} for float.")
            };
        }

        /// <summary>Reads a double value, supporting Fixed64, Fixed32, and VarInt wire types.</summary>
        public static double ReadDouble(this ref StreamReader reader, WireType wireType)
        {
            return wireType switch
            {
                WireType.Fixed64b => reader.ReadFixedDouble(),
                WireType.Fixed32b => reader.ReadFixedFloat(),
                WireType.VarInt => reader.ReadVarInt32(),
                _ => throw new InvalidOperationException($"WireType {wireType} is not valid for double.")
            };
        }

        #endregion

        #region String and Bytes

        /// <summary>
        /// Reads a UTF-8 encoded string (WireType.Len).
        /// Uses ArrayPool for strings larger than the buffer.
        /// </summary>
        public static string ReadString(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.Len, "string");

            int length = reader.ReadVarInt32();
            var slice = reader.GetSliceWithPool(length, out byte[] rentedBuffer);
            try
            {
                return Encoding.UTF8.GetString(slice);
            }
            finally
            {
                if (rentedBuffer != null)
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        /// <summary>
        /// Reads a byte array (WireType.Len).
        /// Uses ArrayPool internally for large arrays.
        /// </summary>
        public static byte[] ReadByteArray(this ref StreamReader reader)
        {
            int length = reader.ReadVarInt32();
            var slice = reader.GetSliceWithPool(length, out byte[] rentedBuffer);
            try
            {
                return slice.ToArray();
            }
            finally
            {
                if (rentedBuffer != null)
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        /// <summary>
        /// Reads a byte array span (WireType.Len) with zero-copy when data fits in buffer.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when data exceeds buffer size.</exception>
        public static ReadOnlySpan<byte> ReadByteArraySpan(this ref StreamReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length);
        }

        #endregion

        #region BCL Types (Guid, DateTime, TimeSpan)

        /// <summary>Reads Guid in protobuf-net BCL format (nested message with lo/hi fixed64 fields).</summary>
        public static Guid ReadGuid(this ref StreamReader reader, WireType wireType)
        {
            ValidateWireType(wireType, WireType.Len, "Guid");

            int length = reader.ReadVarInt32();
            if (length == 0)
                return Guid.Empty;

            if (length != BclTypeFormats.Guid.NestedContentSize)
                ReaderHelpers.ThrowInvalidGuidLength(length);

            var nestedReader = reader.CreateSubReader(length);
            Span<byte> guidBytes = stackalloc byte[ProtobufConstants.GuidByteSize];
            bool hasLo = false, hasHi = false;

            while (!nestedReader.IsEnd)
            {
                nestedReader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.Guid.FieldLoNumber:
                        if (innerWireType != WireType.Fixed64b)
                            ReaderHelpers.ThrowGuidFieldWireType(innerWireType, "lo");
                        nestedReader.GetSlice(8).CopyTo(guidBytes.Slice(0, 8));
                        hasLo = true;
                        break;

                    case BclTypeFormats.Guid.FieldHiNumber:
                        if (innerWireType != WireType.Fixed64b)
                            ReaderHelpers.ThrowGuidFieldWireType(innerWireType, "hi");
                        nestedReader.GetSlice(8).CopyTo(guidBytes.Slice(8, 8));
                        hasHi = true;
                        break;

                    default:
                        nestedReader.SkipField(innerWireType);
                        break;
                }
            }

            if (!hasLo || !hasHi)
                ReaderHelpers.ThrowIncompleteGuid(hasLo, hasHi);

            return new Guid(guidBytes);
        }

        /// <summary>Reads TimeSpan in protobuf-net BCL format.</summary>
        public static TimeSpan ReadTimeSpan(this ref StreamReader reader, WireType wireType)
        {
            var (scaledValue, scale) = ReadBclTimeFormat(ref reader, wireType, "TimeSpan");
            return new TimeSpan(DateTimeHelper.ConvertTimeSpanToTicks(scaledValue, scale));
        }

        /// <summary>Reads DateTime in protobuf-net BCL format.</summary>
        public static DateTime ReadDateTime(this ref StreamReader reader, WireType wireType)
        {
            var (scaledValue, scale) = ReadBclTimeFormat(ref reader, wireType, "DateTime");
            return new DateTime(DateTimeHelper.ConvertToTicks(scaledValue, scale), DateTimeKind.Unspecified);
        }

        /// <summary>Parses the BCL time format used by DateTime and TimeSpan.</summary>
        private static (long scaledValue, int scale) ReadBclTimeFormat(ref StreamReader reader, WireType wireType, string typeName)
        {
            ValidateWireType(wireType, WireType.Len, typeName);

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

            return (scaledValue, scale);
        }

        #endregion

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateWireType(WireType actual, WireType expected, string typeName)
            => ReaderHelpers.ValidateWireType(actual, expected, typeName);

        #endregion
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
        private int _bufferPos;              // current read position in buffer
        private int _bufferLen;              // valid bytes in buffer
        private bool _endOfStream;           // true when Stream.Read returned 0
        private long _totalBytesConsumed;    // absolute position from stream start (for limit tracking)
        private long _currentLimit;          // max bytes allowed to read (long.MaxValue = unlimited)

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
            _totalBytesConsumed = 0;
            _currentLimit = long.MaxValue;
        }

        /// <summary>
        /// Gets whether the reader has reached the end of data or the current limit.
        /// Proactively tries to read more data if buffer is empty.
        /// </summary>
        public bool IsEnd
        {
            get
            {
                // Check limit FIRST (fast path for nested messages)
                if (_totalBytesConsumed >= _currentLimit)
                    return true;

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
        /// Compacts the buffer by moving remaining unread bytes to the start.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompactBuffer()
        {
            int remaining = _bufferLen - _bufferPos;
            if (remaining > 0 && _bufferPos > 0)
            {
                _buffer.Slice(_bufferPos, remaining).CopyTo(_buffer);
            }
            _bufferPos = 0;
            _bufferLen = remaining;
        }

        /// <summary>
        /// Attempts to fill the buffer with more data from the stream.
        /// Sets _endOfStream if no more data is available.
        /// </summary>
        private void TryFillBuffer()
        {
            CompactBuffer();

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

        /// <summary>
        /// Gets the total number of bytes consumed from the stream.
        /// </summary>
        public long TotalBytesConsumed => _totalBytesConsumed;

        /// <summary>
        /// Gets the number of bytes remaining until the current limit.
        /// Returns int.MaxValue if no limit is set.
        /// </summary>
        public int BytesUntilLimit => _currentLimit == long.MaxValue
            ? int.MaxValue
            : (int)Math.Min(_currentLimit - _totalBytesConsumed, int.MaxValue);

        /// <summary>
        /// Sets a limit on the number of bytes that can be read from this point.
        /// Returns the previous limit which should be passed to PopLimit.
        /// </summary>
        /// <param name="byteLimit">Number of bytes allowed to read from current position.</param>
        /// <returns>The previous limit (to be passed to PopLimit).</returns>
        public long PushLimit(int byteLimit)
        {
            long oldLimit = _currentLimit;
            _currentLimit = _totalBytesConsumed + byteLimit;
            return oldLimit;
        }

        /// <summary>
        /// Restores the previous limit after reading a nested message.
        /// Throws if the current position exceeds the restored limit (malformed data).
        /// </summary>
        /// <param name="oldLimit">The limit returned by PushLimit.</param>
        public void PopLimit(long oldLimit)
        {
            // Check if we read past the limit (malformed message)
            if (_totalBytesConsumed > _currentLimit)
            {
                throw new InvalidDataException(
                    $"Nested message exceeded its declared size by {_totalBytesConsumed - _currentLimit} bytes.");
            }
            _currentLimit = oldLimit;
        }

        /// <summary>
        /// Gets or sets the current position within the buffer.
        /// Used for peek-ahead and rewind during non-packed repeated field reading.
        /// Note: Can only move within the current buffer bounds.
        /// Warning: Adjusts TotalBytesConsumed accordingly - use carefully with active limits.
        /// </summary>
        public int Position
        {
            get => _bufferPos;
            set
            {
                if (value < 0 || value > _bufferLen)
                    throw new ArgumentOutOfRangeException(nameof(value), $"Position {value} is out of buffer bounds [0, {_bufferLen}].");
                // Adjust total bytes consumed to maintain consistency with limit tracking
                int delta = value - _bufferPos;
                _totalBytesConsumed += delta;
                _bufferPos = value;
            }
        }

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
            CompactBuffer();

            // Read more data from stream until we have enough
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
        /// For data larger than the buffer, throws - use GetSliceWithPool instead.
        /// </summary>
        public ReadOnlySpan<byte> GetSlice(int length)
        {
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            // Fast path: data fits in buffer
            if (length <= _buffer.Length)
            {
                EnsureBytes(length);
                var slice = _buffer.Slice(_bufferPos, length);
                _bufferPos += length;
                _totalBytesConsumed += length;
                return slice;
            }

            // Large data: cannot fit in buffer, caller should use GetSliceWithPool
            throw new InvalidOperationException(
                $"Data of {length} bytes exceeds buffer size of {_buffer.Length}. Use GetSliceWithPool for large data.");
        }

        /// <summary>
        /// Gets a slice using ArrayPool for large data.
        /// For data that fits in the buffer, returns a view (zero-copy) and rentedBuffer is null.
        /// For data larger than the buffer, rents from ArrayPool and caller MUST return it.
        /// </summary>
        /// <param name="length">Number of bytes to read.</param>
        /// <param name="rentedBuffer">If rented from pool, the buffer to return; otherwise null.</param>
        /// <returns>Span over the data (either buffer view or rented array).</returns>
        public ReadOnlySpan<byte> GetSliceWithPool(int length, out byte[] rentedBuffer)
        {
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            // Fast path: data fits in buffer (zero-copy)
            if (length <= _buffer.Length)
            {
                rentedBuffer = null;
                EnsureBytes(length);
                var slice = _buffer.Slice(_bufferPos, length);
                _bufferPos += length;
                _totalBytesConsumed += length;
                return slice;
            }

            // Large data path: rent from ArrayPool
            rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
            ReadBytesInto(rentedBuffer.AsSpan(0, length));
            return rentedBuffer.AsSpan(0, length);
        }

        /// <summary>
        /// Reads exactly the specified number of bytes into the destination span.
        /// Combines buffered data with stream reads as needed.
        /// </summary>
        /// <param name="destination">Destination span to read into.</param>
        public void ReadBytesInto(Span<byte> destination)
        {
            int length = destination.Length;
            if (length == 0)
                return;

            int bytesRead = 0;

            // Copy any available buffered data first
            int bufferedAvailable = _bufferLen - _bufferPos;
            if (bufferedAvailable > 0)
            {
                int toCopy = Math.Min(bufferedAvailable, length);
                _buffer.Slice(_bufferPos, toCopy).CopyTo(destination);
                _bufferPos += toCopy;
                bytesRead = toCopy;
            }

            // Read remaining bytes directly from stream
            while (bytesRead < length)
            {
                int read = _stream.Read(destination.Slice(bytesRead));
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Expected {length} bytes but stream ended after {bytesRead} bytes");
                }
                bytesRead += read;
            }

            _totalBytesConsumed += length;
        }

        /// <summary>
        /// Creates a SpanReader for reading a nested message.
        /// For large messages, uses ArrayPool (caller doesn't need to manage).
        /// Note: For large messages, this returns a SpanReader over pooled memory
        /// which is only valid until the next read operation.
        /// </summary>
        /// <param name="length">Length of the nested message.</param>
        /// <returns>SpanReader positioned over the nested message data.</returns>
        public SpanReader CreateSubReader(int length)
        {
            // For nested messages, use GetSlice which throws if too large
            // Large nested messages should use PushLimit/PopLimit pattern instead
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
            {
                _totalBytesConsumed += 1;
                return b;
            }

            return ReadVarInt32Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int ReadVarInt32Slow(byte firstByte)
        {
            long result = firstByte & ProtobufConstants.VarintValueMask;
            int bytesRead = 1;

            // Read remaining bytes (max 9 more for 10-byte negative int32)
            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                bytesRead++;
                if (b < ProtobufConstants.VarintContinuationBit)
                {
                    _totalBytesConsumed += bytesRead;
                    return (int)(result | ((long)b << shift));
                }
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
            {
                _totalBytesConsumed += 1;
                return b;
            }

            return ReadVarUInt32Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private uint ReadVarUInt32Slow(byte firstByte)
        {
            ulong result = (ulong)(firstByte & ProtobufConstants.VarintValueMask);
            int bytesRead = 1;

            // Read remaining bytes (max 9 more for overflow handling)
            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                bytesRead++;
                if (b < ProtobufConstants.VarintContinuationBit)
                {
                    _totalBytesConsumed += bytesRead;
                    return (uint)(result | ((ulong)b << shift));
                }
                result |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
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
            {
                _totalBytesConsumed += 1;
                return b;
            }

            return ReadVarInt64Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private long ReadVarInt64Slow(byte firstByte)
        {
            long result = firstByte & ProtobufConstants.VarintValueMask;
            int bytesRead = 1;

            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                bytesRead++;
                if (b < ProtobufConstants.VarintContinuationBit)
                {
                    _totalBytesConsumed += bytesRead;
                    return result | ((long)b << shift);
                }
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
            {
                _totalBytesConsumed += 1;
                return b;
            }

            return ReadVarUInt64Slow(b);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ulong ReadVarUInt64Slow(byte firstByte)
        {
            ulong result = (ulong)(firstByte & ProtobufConstants.VarintValueMask);
            int bytesRead = 1;

            for (int shift = 7; shift < 70; shift += 7)
            {
                EnsureBytes(1);
                byte b = _buffer[_bufferPos++];
                bytesRead++;
                if (b < ProtobufConstants.VarintContinuationBit)
                {
                    _totalBytesConsumed += bytesRead;
                    return result | ((ulong)b << shift);
                }
                result |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException("Malformed varint: exceeded maximum length");
        }

        #endregion

        #region Fixed Size Reading

        /// <summary>
        /// Reads an 8-byte double (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadFixedDouble()
        {
            EnsureBytes(8);
            double result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<double>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadDoubleLittleEndian(_buffer.Slice(_bufferPos, 8));
            }
            _bufferPos += 8;
            _totalBytesConsumed += 8;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte float (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFixedFloat()
        {
            EnsureBytes(4);
            float result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<float>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_bufferPos, 4));
            }
            _bufferPos += 4;
            _totalBytesConsumed += 4;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte signed integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadFixedInt32()
        {
            EnsureBytes(4);
            int result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<int>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_bufferPos, 4));
            }
            _bufferPos += 4;
            _totalBytesConsumed += 4;
            return result;
        }

        /// <summary>
        /// Reads a 4-byte unsigned integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadFixedUInt32()
        {
            EnsureBytes(4);
            uint result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<uint>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_bufferPos, 4));
            }
            _bufferPos += 4;
            _totalBytesConsumed += 4;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte signed integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadFixedInt64()
        {
            EnsureBytes(8);
            long result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<long>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadInt64LittleEndian(_buffer.Slice(_bufferPos, 8));
            }
            _bufferPos += 8;
            _totalBytesConsumed += 8;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixedUInt64()
        {
            EnsureBytes(8);
            ulong result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<ulong>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_bufferPos, 8));
            }
            _bufferPos += 8;
            _totalBytesConsumed += 8;
            return result;
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (fixed64).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixed64()
        {
            return ReadFixedUInt64();
        }

        /// <summary>
        /// Reads a 2-byte signed integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadFixedInt16()
        {
            EnsureBytes(2);
            short result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<short>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadInt16LittleEndian(_buffer.Slice(_bufferPos, 2));
            }
            _bufferPos += 2;
            _totalBytesConsumed += 2;
            return result;
        }

        /// <summary>
        /// Reads a 2-byte unsigned integer (little-endian).
        /// Uses Unsafe.ReadUnaligned for optimal performance on little-endian systems.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadFixedUInt16()
        {
            EnsureBytes(2);
            ushort result;
            if (BitConverter.IsLittleEndian)
            {
                result = Unsafe.ReadUnaligned<ushort>(ref _buffer[_bufferPos]);
            }
            else
            {
                result = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_bufferPos, 2));
            }
            _bufferPos += 2;
            _totalBytesConsumed += 2;
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
        /// Peeks at the next field key. If it matches the expected field and wire type,
        /// advances past the key and returns true. Otherwise, rewinds and returns false.
        /// Returns false immediately if at end of buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeekSameField(int expectedFieldId, WireType expectedWireType)
        {
            if (IsEnd) return false;
            var savedPosition = Position;
            var (wt, fid) = ReadKey();
            if (fid == expectedFieldId && wt == expectedWireType) return true;
            Position = savedPosition;
            return false;
        }

        /// <summary>
        /// Skips an unknown field based on wire type.
        /// For large Len fields, reads and discards in chunks.
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
                    _totalBytesConsumed += 4;
                    break;

                case WireType.Fixed64b:
                    EnsureBytes(8);
                    _bufferPos += 8;
                    _totalBytesConsumed += 8;
                    break;

                case WireType.Len:
                    int length = ReadVarInt32();
                    SkipBytes(length);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown WireType: {wireType}");
            }
        }

        /// <summary>
        /// Skips the specified number of bytes, handling large data efficiently.
        /// For seekable streams, uses Seek instead of reading bytes.
        /// </summary>
        private void SkipBytes(int length)
        {
            if (length <= 0)
                return;

            // Fast path: data is already in buffer
            int bufferedRemaining = _bufferLen - _bufferPos;
            if (length <= bufferedRemaining)
            {
                _bufferPos += length;
                _totalBytesConsumed += length;
                return;
            }

            // Consume all buffered data first
            int bytesSkipped = bufferedRemaining;
            _bufferPos = _bufferLen;
            _totalBytesConsumed += bufferedRemaining;

            int toSkip = length - bytesSkipped;

            // Optimization: for seekable streams, seek instead of reading
            if (_stream.CanSeek)
            {
                _stream.Position += toSkip;
                _totalBytesConsumed += toSkip;
                // Reset buffer state since stream position changed
                _bufferPos = 0;
                _bufferLen = 0;
                return;
            }

            // Non-seekable stream: read and discard bytes
            while (toSkip > 0)
            {
                // Reuse the buffer for skipping
                int toRead = Math.Min(toSkip, _buffer.Length);
                int read = _stream.Read(_buffer.Slice(0, toRead));
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Expected to skip {length} bytes but stream ended after {bytesSkipped} bytes");
                }
                bytesSkipped += read;
                toSkip -= read;
                _totalBytesConsumed += read;
            }

            // Reset buffer state since we used it for skipping
            _bufferPos = 0;
            _bufferLen = 0;
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
        /// Counts the number of varints in a span by scanning continuation bits.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountVarints(ReadOnlySpan<byte> data)
        {
            int count = 0;
            for (int i = 0; i < data.Length; i++)
            {
                // Count bytes without continuation bit (end of varint)
                if (data[i] < 0x80)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Reads a packed varint int32 array.
        /// </summary>
        public int[] ReadPackedVarIntInt32Array(bool zigZag)
        {
            int length = ReadVarInt32();
            if (length == 0)
                return Array.Empty<int>();

            EnsureBytes(length);

            var slice = _buffer.Slice(_bufferPos, length);

            // Fast count: just scan for bytes without continuation bit
            int count = CountVarints(slice);

            var subReader = new SpanReader(slice);
            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = zigZag ? subReader.ReadZigZagVarInt32() : subReader.ReadVarInt32();
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size int32 array.
        /// Uses MemoryMarshal.Cast for optimal performance on little-endian systems.
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

            // Fast path: direct memory copy on little-endian systems (99% of systems)
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, int>(slice).CopyTo(result);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(i * 4, 4));
                }
            }

            _bufferPos += (int)length;
            _totalBytesConsumed += (int)length;
            return result;
        }

        /// <summary>
        /// Reads a packed float array.
        /// Uses MemoryMarshal.Cast for optimal performance on little-endian systems.
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

            // Fast path: direct memory copy on little-endian systems
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, float>(slice).CopyTo(result);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(i * 4, 4));
                }
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
            return result;
        }

        /// <summary>
        /// Reads a packed double array.
        /// Uses MemoryMarshal.Cast for optimal performance on little-endian systems.
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

            // Fast path: direct memory copy on little-endian systems
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, double>(slice).CopyTo(result);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = BinaryPrimitives.ReadDoubleLittleEndian(slice.Slice(i * 8, 8));
                }
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
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

            // Fast count using helper
            int count = CountVarints(slice);

            var subReader = new SpanReader(slice);
            long[] result = new long[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = zigZag ? subReader.ReadZigZagVarInt64() : subReader.ReadVarInt64();
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size int64 array.
        /// Uses MemoryMarshal.Cast for optimal performance on little-endian systems.
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

            // Fast path: direct memory copy on little-endian systems
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.Cast<byte, long>(slice).CopyTo(result);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] = BinaryPrimitives.ReadInt64LittleEndian(slice.Slice(i * 8, 8));
                }
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
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

            // Fast count using helper
            int count = CountVarints(slice);

            var subReader = new SpanReader(slice);
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarInt32() != 0;
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
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

            // Fast count using helper
            int count = CountVarints(slice);

            var subReader = new SpanReader(slice);
            uint[] result = new uint[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarUInt32();
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
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

            // Fast count using helper
            int count = CountVarints(slice);

            var subReader = new SpanReader(slice);
            ulong[] result = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = subReader.ReadVarUInt64();
            }

            _bufferPos += length;
            _totalBytesConsumed += length;
            return result;
        }

        /// <summary>
        /// Reads bytes directly with zero-copy when data fits in buffer.
        /// For data larger than buffer, throws - use GetSliceWithPool instead.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when data exceeds buffer size.</exception>
        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            // Use GetSlice which throws if data exceeds buffer - prevents memory leak
            return GetSlice(length);
        }

        /// <summary>
        /// Reads a byte array with length prefix.
        /// For large arrays, uses ArrayPool internally.
        /// </summary>
        public byte[] ReadByteArray()
        {
            int length = ReadVarInt32();
            var slice = GetSliceWithPool(length, out byte[] rentedBuffer);
            try
            {
                return slice.ToArray();
            }
            finally
            {
                if (rentedBuffer != null)
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        #endregion
    }
}
