using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    /// <summary>
    /// Extension methods for SpanReader providing type-aware deserialization.
    /// Mirrors StreamReaders API for consistency.
    /// </summary>
    public static class SpanReaders
    {
        #region Primitive Types

        /// <summary>Reads a boolean value (WireType.VarInt).</summary>
        public static bool ReadBool(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "bool");
            return reader.ReadVarInt32() != 0;
        }

        /// <summary>Reads a boolean value as a varint (no wire type validation).</summary>
        public static bool ReadBool(this ref SpanReader reader)
            => reader.ReadVarInt32() != 0;

        /// <summary>Reads an unsigned byte value (WireType.VarInt).</summary>
        public static byte ReadByte(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "byte");
            return ReaderHelpers.ToByte(reader.ReadVarUInt32());
        }

        /// <summary>Reads a signed byte value (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static sbyte ReadSByte(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "sbyte");
            return (sbyte)(zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32());
        }

        /// <summary>Reads a signed 16-bit integer (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static short ReadInt16(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "short");
            return (short)(zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32());
        }

        /// <summary>Reads an unsigned 16-bit integer (WireType.VarInt).</summary>
        public static ushort ReadUInt16(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "ushort");
            return ReaderHelpers.ToUInt16(reader.ReadVarUInt32());
        }

        /// <summary>Reads a signed 32-bit integer, supporting VarInt and Fixed32 wire types.</summary>
        public static int ReadInt32(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            return wireType switch
            {
                WireType.VarInt => zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32(),
                WireType.Fixed32b => reader.ReadFixedInt32(),
                _ => throw new InvalidOperationException($"Unexpected wire type {wireType} for int32.")
            };
        }

        /// <summary>Reads an unsigned 32-bit integer (WireType.VarInt).</summary>
        public static uint ReadUInt32(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "uint");
            return reader.ReadVarUInt32();
        }

        /// <summary>Reads a signed 64-bit integer (WireType.VarInt), optionally with ZigZag encoding.</summary>
        public static long ReadInt64(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "long");
            return zigZag ? reader.ReadZigZagVarInt64() : reader.ReadVarInt64();
        }

        /// <summary>Reads an unsigned 64-bit integer (WireType.VarInt).</summary>
        public static ulong ReadUInt64(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.VarInt, "ulong");
            return reader.ReadVarUInt64();
        }

        /// <summary>Reads a float value, supporting Fixed32 and VarInt wire types.</summary>
        public static float ReadFloat(this ref SpanReader reader, WireType wireType)
        {
            return wireType switch
            {
                WireType.Fixed32b => reader.ReadFixedFloat(),
                WireType.VarInt => reader.ReadVarInt32(),
                _ => throw new InvalidOperationException($"Unexpected wire type {wireType} for float.")
            };
        }

        /// <summary>Reads a double value, supporting Fixed64, Fixed32, and VarInt wire types.</summary>
        public static double ReadDouble(this ref SpanReader reader, WireType wireType)
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

        /// <summary>Reads a UTF-8 encoded string (WireType.Len).</summary>
        public static string ReadString(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.Len, "string");
            int length = reader.ReadVarInt32();
            return Encoding.UTF8.GetString(reader.GetSlice(length));
        }

        /// <summary>Reads a byte array (WireType.Len). Allocates new byte[].</summary>
        public static byte[] ReadByteArray(this ref SpanReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length).ToArray();
        }

        /// <summary>Reads a byte array span (WireType.Len). Zero-copy.</summary>
        public static ReadOnlySpan<byte> ReadByteArraySpan(this ref SpanReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length);
        }

        #endregion

        #region BCL Types (Guid, DateTime, TimeSpan)

        /// <summary>Reads Guid in protobuf-net BCL format (nested message with lo/hi fixed64 fields).</summary>
        public static Guid ReadGuid(this ref SpanReader reader, WireType wireType)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.Len, "Guid");

            int length = reader.ReadVarInt32();
            if (length == 0)
                return Guid.Empty;

            if (length != BclTypeFormats.Guid.NestedContentSize)
                ReaderHelpers.ThrowInvalidGuidLength(length);

            int endPosition = reader.Position + length;
            Span<byte> guidBytes = stackalloc byte[ProtobufConstants.GuidByteSize];
            bool hasLo = false, hasHi = false;

            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.Guid.FieldLoNumber:
                        if (innerWireType != WireType.Fixed64b)
                            ReaderHelpers.ThrowGuidFieldWireType(innerWireType, "lo");
                        reader.GetSlice(8).CopyTo(guidBytes.Slice(0, 8));
                        hasLo = true;
                        break;

                    case BclTypeFormats.Guid.FieldHiNumber:
                        if (innerWireType != WireType.Fixed64b)
                            ReaderHelpers.ThrowGuidFieldWireType(innerWireType, "hi");
                        reader.GetSlice(8).CopyTo(guidBytes.Slice(8, 8));
                        hasHi = true;
                        break;

                    default:
                        reader.SkipField(innerWireType);
                        break;
                }
            }

            if (!hasLo || !hasHi)
                ReaderHelpers.ThrowIncompleteGuid(hasLo, hasHi);

            return new Guid(guidBytes);
        }

        /// <summary>Reads TimeSpan in protobuf-net BCL format.</summary>
        public static TimeSpan ReadTimeSpan(this ref SpanReader reader, WireType wireType)
        {
            var (scaledValue, scale) = ReadBclTimeFormat(ref reader, wireType, "TimeSpan");
            return new TimeSpan(DateTimeHelper.ConvertTimeSpanToTicks(scaledValue, scale));
        }

        /// <summary>Reads DateTime in protobuf-net BCL format.</summary>
        public static DateTime ReadDateTime(this ref SpanReader reader, WireType wireType)
        {
            var (scaledValue, scale) = ReadBclTimeFormat(ref reader, wireType, "DateTime");
            return new DateTime(DateTimeHelper.ConvertToTicks(scaledValue, scale), DateTimeKind.Unspecified);
        }

        /// <summary>Parses the BCL time format used by DateTime and TimeSpan.</summary>
        private static (long scaledValue, int scale) ReadBclTimeFormat(ref SpanReader reader, WireType wireType, string typeName)
        {
            ReaderHelpers.ValidateWireType(wireType, WireType.Len, typeName);

            int length = reader.ReadVarInt32();
            int endPosition = reader.Position + length;

            long scaledValue = 0;
            int scale = 5;

            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.DateTimeTimeSpan.FieldValueNumber:
                        scaledValue = reader.ReadZigZagVarInt64();
                        break;
                    case BclTypeFormats.DateTimeTimeSpan.FieldScaleNumber:
                        scale = reader.ReadVarInt32();
                        break;
                    default:
                        reader.SkipField(innerWireType);
                        break;
                }
            }

            return (scaledValue, scale);
        }

        #endregion
    }

    /// <summary>
    /// High-performance zero-allocation Protocol Buffers deserializer.
    /// Uses ref struct for stack-only allocation and ReadOnlySpan&lt;byte&gt; for zero-copy parsing.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Rationale:</b></para>
    /// - ref struct: Cannot escape to heap, enables aggressive JIT optimizations
    /// - ReadOnlySpan&lt;byte&gt;: Zero-copy wire-format parsing, no allocations
    /// - Unrolled varint loops: 90%+ of varints are 1-5 bytes, unrolling eliminates branches
    /// - AggressiveInlining: Hot path methods inline to 2-3 CPU instructions
    ///
    /// <para><b>CompatibilityLevel.Level200 Compliance:</b></para>
    /// - Varint encoding: Tolerates incomplete varints at EOF (ignores continuation bit)
    /// - Unknown fields: Skip (do not preserve for round-tripping)
    /// - Packed repeated: Supported via WireType.Len
    /// - Recursion depth: Enforced via RecursionGuard (max 100 levels)
    ///
    /// <para><b>Performance Characteristics:</b></para>
    /// - ReadVarInt32: ~2ns for 1-byte values, ~15ns for 5-byte values (3.5 GHz CPU)
    /// - ReadString: UTF-8 decode only, no intermediate allocations
    /// - ReadGuid: Stack-allocated 16-byte buffer, zero heap allocations
    /// - SkipField: Validates buffer bounds before advancing (prevents overrun)
    ///
    /// <para><b>Thread Safety:</b></para>
    /// Not thread-safe. Each thread must use separate SpanReader instance.
    /// </remarks>
    public ref partial struct SpanReader
    {
        private ReadOnlySpan<byte> buffer;
        private int position;
        private ReadOnlyMemory<byte> _sourceMemory;
        private int _sourceOffset;

        /// <summary>
        /// Gets or sets the current read position in the buffer.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when attempting to set position outside buffer bounds [0, buffer.Length].
        /// </exception>
        public int Position
        {
            get
            {
                return position;
            }
            set
            {
                if (value < 0 || value > buffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(value), "Position is out of range.");

                position = value;
            }
        }

        /// <summary>
        /// Initializes a new SpanReader over the specified buffer.
        /// </summary>
        /// <param name="buffer">ReadOnlySpan containing protobuf wire-format data.</param>
        public SpanReader(ReadOnlySpan<byte> buffer)
        {
            this.buffer = buffer;
            this.position = 0;
            this._sourceMemory = default;
            this._sourceOffset = 0;
        }

        /// <summary>
        /// Creates a SpanReader over the specified memory buffer with zero-copy support.
        /// ReadOnlyMemory&lt;byte&gt; fields will be sliced from the source memory instead of allocating new byte[].
        /// The source memory must outlive any deserialized ReadOnlyMemory&lt;byte&gt; fields.
        /// </summary>
        public static SpanReader FromMemory(ReadOnlyMemory<byte> memory)
        {
            return new SpanReader(memory.Span, memory, 0);
        }

        private SpanReader(ReadOnlySpan<byte> buffer, ReadOnlyMemory<byte> sourceMemory, int sourceOffset)
        {
            this.buffer = buffer;
            this.position = 0;
            this._sourceMemory = sourceMemory;
            this._sourceOffset = sourceOffset;
        }

        /// <summary>
        /// Gets whether the reader has reached the end of the buffer.
        /// </summary>
        public bool IsEnd => position >= buffer.Length;

        /// <summary>
        /// Gets a value indicating whether the reader has reached the end of the data.
        /// </summary>
        public bool EndOfData => position >= buffer.Length;

        /// <summary>
        /// Peeks at the next byte without advancing the position.
        /// </summary>
        /// <returns>The byte at the current position.</returns>
        /// <exception cref="InvalidOperationException">If no bytes remain in the buffer.</exception>
        public byte PeekByte()
        {
            if (position >= buffer.Length)
                throw new InvalidOperationException("Cannot peek: no bytes remaining in buffer.");
            return buffer[position];
        }

        /// <summary>
        /// Validates that the specified number of bytes are available in the buffer.
        /// </summary>
        /// <param name="length">Number of bytes to check availability for.</param>
        /// <exception cref="InvalidDataException">
        /// Thrown when length is negative (malformed length-delimited field).
        /// Level200: Negative length prefixes are invalid wire-format.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when insufficient bytes remain (would cause buffer overrun).
        /// </exception>
        public void CheckLength(int length)
        {
            // Level200: Length prefix must be non-negative
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");
        }

        /// <summary>
        /// Extracts a ReadOnlySpan slice of the specified length and advances position.
        /// Zero-copy operation - no allocations, returns view into original buffer.
        /// </summary>
        /// <param name="length">Number of bytes to extract.</param>
        /// <returns>ReadOnlySpan view of the extracted bytes.</returns>
        /// <exception cref="InvalidDataException">If length is negative.</exception>
        /// <exception cref="InvalidOperationException">If insufficient bytes remain.</exception>
        public ReadOnlySpan<byte> GetSlice(int length)
        {
            CheckLength(length);

            ReadOnlySpan<byte> result = buffer.Slice(position, length);
            position += length;

            return result;
        }

        /// <summary>
        /// Creates a sub-reader for reading a length-delimited nested message.
        /// Extracts the specified number of bytes and returns a new SpanReader scoped to that slice.
        /// Combines GetSlice + new SpanReader into a single convenient call.
        /// </summary>
        /// <param name="length">Number of bytes for the nested message.</param>
        /// <returns>A new SpanReader positioned at the start of the nested content.</returns>
        /// <exception cref="InvalidDataException">If length is negative.</exception>
        /// <exception cref="InvalidOperationException">If insufficient bytes remain.</exception>
        public SpanReader CreateSubReader(int length)
        {
            CheckLength(length);
            int startPos = position;
            var slice = buffer.Slice(position, length);
            position += length;

            if (_sourceMemory.Length > 0)
                return new SpanReader(slice, _sourceMemory, _sourceOffset + startPos);
            return new SpanReader(slice);
        }

        /// <summary>
        /// Reads a single byte without validation (returns -1 at EOF).
        /// Used internally for low-level parsing where EOF is expected.
        /// </summary>
        /// <returns>Byte value [0-255] or -1 if at end of buffer.</returns>
        public int GetByte()
        {
            if (position >= buffer.Length) return -1;
            return buffer[position++];
        }

        /// <summary>
        /// Reads a varint-encoded int32 using hybrid unrolled/loop approach.
        /// Unrolled for 1-5 bytes (99% of cases), loop for 6-10 bytes (rare).
        /// Tolerates incomplete varints at EOF (treats last byte as final, ignores continuation bit).
        /// </summary>
        public int ReadVarInt32()
        {
            // Boundary check
            if (position >= buffer.Length)
                throw new InvalidOperationException("Unexpected end of buffer while reading VarInt32");

            // Byte 1: Fast path (90% of cases)
            byte b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return b;

            long result = b & ProtobufConstants.VarintValueMask;

            // Byte 2
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return (int)(result | ((long)b << ProtobufConstants.VarintShift));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            // Byte 3
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 2)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            // Byte 4
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 3)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            // Byte 5
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return (int)(result | ((long)b << (ProtobufConstants.VarintShift * 4)));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4);

            // Bytes 6-10: Rare case, use loop
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                if (position >= buffer.Length) return (int)result; // EOF tolerance
                b = buffer[position++];
                if (b < ProtobufConstants.VarintContinuationBit) return (int)(result | ((long)b << shift));
                result |= (long)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            // 11+ bytes - malformed
            throw new InvalidDataException($"Malformed varint: varint exceeded maximum length of {ProtobufConstants.MaxVarint64Size} bytes");
        }

        /// <summary>
        /// Reads a varint-encoded uint32 using hybrid unrolled/loop approach.
        /// Optimized for unsigned values (lengths, byte, ushort, uint).
        /// Tolerates incomplete varints at EOF (treats last byte as final, ignores continuation bit).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt32()
        {
            if (position >= buffer.Length)
                throw new InvalidOperationException("Unexpected end of buffer while reading VarUInt32");

            // Byte 1: Fast path
            byte b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return b;

            uint result = (uint)(b & ProtobufConstants.VarintValueMask);

            // Byte 2
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((uint)b << ProtobufConstants.VarintShift);
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            // Byte 3
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((uint)b << (ProtobufConstants.VarintShift * 2));
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            // Byte 4
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((uint)b << (ProtobufConstants.VarintShift * 3));
            result |= (uint)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            // Byte 5 (optimal for uint32)
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((uint)b << (ProtobufConstants.VarintShift * 4));

            // Bytes 6-10: Rare case (backward compatibility), use ulong to avoid overflow
            ulong result64 = result | ((ulong)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4));
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                if (position >= buffer.Length) return (uint)result64; // EOF tolerance
                b = buffer[position++];
                if (b < ProtobufConstants.VarintContinuationBit) return (uint)result64; // Truncate to uint32
                result64 |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException($"Malformed varint: varint exceeded maximum length of {ProtobufConstants.MaxVarint64Size} bytes");
        }

        public int ReadZigZagVarInt32()
        {
            uint encoded = ReadVarUInt32();
            return WireFormatHelpers.DecodeZigZag32(encoded);
        }

        /// <summary>
        /// Reads a varint-encoded int64 using hybrid unrolled/loop approach.
        /// Unrolled for 1-5 bytes (most common), loop for 6-10 bytes.
        /// Tolerates incomplete varints at EOF (treats last byte as final, ignores continuation bit).
        /// </summary>
        public long ReadVarInt64()
        {
            if (position >= buffer.Length)
                throw new InvalidOperationException("Unexpected end of buffer while reading VarInt64");

            // Byte 1: Fast path
            byte b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return b;

            long result = b & ProtobufConstants.VarintValueMask;

            // Bytes 2-5: Unrolled
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((long)b << ProtobufConstants.VarintShift);
            result |= (long)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((long)b << (ProtobufConstants.VarintShift * 2));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((long)b << (ProtobufConstants.VarintShift * 3));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((long)b << (ProtobufConstants.VarintShift * 4));
            result |= (long)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4);

            // Bytes 6-10: Loop
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                if (position >= buffer.Length) return result; // EOF tolerance
                b = buffer[position++];
                if (b < ProtobufConstants.VarintContinuationBit) return result | ((long)b << shift);
                result |= (long)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException($"Malformed varint: int64 varint exceeded maximum length of {ProtobufConstants.MaxVarint64Size} bytes");
        }

        public long ReadZigZagVarInt64()
        {
            ulong encoded = ReadVarUInt64();
            return WireFormatHelpers.DecodeZigZag64(encoded);
        }

        /// <summary>
        /// Reads a varint-encoded uint64 using hybrid unrolled/loop approach.
        /// Unrolled for 1-5 bytes (most common), loop for 6-10 bytes.
        /// Tolerates incomplete varints at EOF (treats last byte as final, ignores continuation bit).
        /// </summary>
        public ulong ReadVarUInt64()
        {
            if (position >= buffer.Length)
                throw new InvalidOperationException("Unexpected end of buffer while reading VarUInt64");

            // Byte 1: Fast path
            byte b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return b;

            ulong result = (ulong)(b & ProtobufConstants.VarintValueMask);

            // Bytes 2-5: Unrolled
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((ulong)b << ProtobufConstants.VarintShift);
            result |= (ulong)(b & ProtobufConstants.VarintValueMask) << ProtobufConstants.VarintShift;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((ulong)b << (ProtobufConstants.VarintShift * 2));
            result |= (ulong)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 2);

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((ulong)b << (ProtobufConstants.VarintShift * 3));
            result |= (ulong)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 3);

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < ProtobufConstants.VarintContinuationBit) return result | ((ulong)b << (ProtobufConstants.VarintShift * 4));
            result |= (ulong)(b & ProtobufConstants.VarintValueMask) << (ProtobufConstants.VarintShift * 4);

            // Bytes 6-10: Loop
            for (int shift = 35; shift < 70; shift += ProtobufConstants.VarintShift)
            {
                if (position >= buffer.Length) return result; // EOF tolerance
                b = buffer[position++];
                if (b < ProtobufConstants.VarintContinuationBit) return result | ((ulong)b << shift);
                result |= (ulong)(b & ProtobufConstants.VarintValueMask) << shift;
            }

            throw new InvalidDataException($"Malformed varint: uint64 varint exceeded maximum length of {ProtobufConstants.MaxVarint64Size} bytes");
        }

        /// <summary>
        /// Internal helper: validates buffer has required bytes and returns slice at current position.
        /// Reduces duplication in fixed-size read methods.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> EnsureAndSlice(int byteCount)
        {
            if (position + byteCount > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var slice = buffer.Slice(position, byteCount);
            position += byteCount;
            return slice;
        }

        /// <summary>
        /// Reads an 8-byte double value (WireType.Fixed64b, little-endian).
        /// Used for protobuf double fields with DataFormat.FixedSize.
        /// </summary>
        /// <returns>IEEE 754 double-precision floating-point value.</returns>
        /// <exception cref="InvalidOperationException">If fewer than 8 bytes remain.</exception>
        public double ReadFixedDouble()
        {
            return BinaryPrimitives.ReadDoubleLittleEndian(EnsureAndSlice(8));
        }

        /// <summary>
        /// Reads a 4-byte float value (WireType.Fixed32b, little-endian).
        /// Used for protobuf float fields with DataFormat.FixedSize.
        /// </summary>
        /// <returns>IEEE 754 single-precision floating-point value.</returns>
        /// <exception cref="InvalidOperationException">If fewer than 4 bytes remain.</exception>
        public float ReadFixedFloat()
        {
            return BinaryPrimitives.ReadSingleLittleEndian(EnsureAndSlice(4));
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (WireType.Fixed64b, little-endian).
        /// Used for protobuf fixed64 fields.
        /// Performance: Preferred over varint for values > 2^56.
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 8 bytes remain.</exception>
        public ulong ReadFixed64()
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(EnsureAndSlice(8));
        }

        /// <summary>
        /// Reads an 8-byte signed integer (WireType.Fixed64b, little-endian).
        /// Used for protobuf sfixed64 fields.
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 8 bytes remain.</exception>
        public long ReadFixedInt64()
        {
            return BinaryPrimitives.ReadInt64LittleEndian(EnsureAndSlice(8));
        }

        /// <summary>
        /// Reads a 2-byte signed integer (little-endian).
        /// Used for protobuf sfixed16 fields (non-standard extension).
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 2 bytes remain.</exception>
        public short ReadFixedInt16()
        {
            return BinaryPrimitives.ReadInt16LittleEndian(EnsureAndSlice(2));
        }

        /// <summary>
        /// Reads a 2-byte unsigned integer (little-endian).
        /// Used for protobuf fixed16 fields (non-standard extension).
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 2 bytes remain.</exception>
        public ushort ReadFixedUInt16()
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(EnsureAndSlice(2));
        }

        /// <summary>
        /// Reads a 4-byte unsigned integer (WireType.Fixed32b, little-endian).
        /// Used for protobuf fixed32 fields.
        /// Performance: Preferred over varint for values > 2^28.
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 4 bytes remain.</exception>
        public uint ReadFixedUInt32()
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(EnsureAndSlice(4));
        }

        /// <summary>
        /// Reads an 8-byte unsigned integer (WireType.Fixed64b, little-endian).
        /// Alias for ReadFixed64 - prefer that method for clarity.
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 8 bytes remain.</exception>
        public ulong ReadFixedUInt64()
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(EnsureAndSlice(8));
        }

        /// <summary>
        /// Reads a 4-byte signed integer (WireType.Fixed32b, little-endian).
        /// Used for protobuf sfixed32 fields.
        /// </summary>
        /// <exception cref="InvalidOperationException">If fewer than 4 bytes remain.</exception>
        public int ReadFixedInt32()
        {
            return BinaryPrimitives.ReadInt32LittleEndian(EnsureAndSlice(4));
        }

        /// <summary>
        /// Reads a protobuf tag (field number + wire type) and decomposes it.
        /// Tag encoding: tag = (field_number &lt;&lt; 3) | wire_type (3 bits).
        /// </summary>
        /// <returns>Tuple containing wire type and field number.</returns>
        /// <remarks>
        /// Level200: Tags are always varint-encoded (1-5 bytes for field IDs up to 2^29).
        /// Field ID 0 is reserved and invalid. Field IDs 19000-19999 are reserved.
        /// </remarks>
        public (WireType wireType, int fieldId) ReadWireTypeAndFieldId()
        {
            uint tag = (uint)this.ReadVarInt32();
            var (fieldNumber, wireType) = WireFormatHelpers.DecodeTag(tag);
            return (wireType, fieldNumber);
        }

        /// <summary>
        /// Reads wire type and field ID using out parameters (zero allocation).
        /// Preferred over tuple-returning version for hot paths (avoids tuple allocation).
        /// </summary>
        /// <param name="wireType">Output parameter for wire type.</param>
        /// <param name="fieldId">Output parameter for field number.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadWireTypeAndFieldId(out WireType wireType, out int fieldId)
        {
            uint tag = (uint)this.ReadVarInt32();
            WireFormatHelpers.DecodeTag(tag, out fieldId, out wireType);
        }

        /// <summary>
        /// Reads a protobuf tag and returns field ID and wire type.
        /// Alias for ReadWireTypeAndFieldId for legacy compatibility.
        /// </summary>
        /// <returns>Tuple containing wire type and field number.</returns>
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
            var savedPosition = position;
            var (wt, fid) = ReadKey();
            if (fid == expectedFieldId && wt == expectedWireType) return true;
            position = savedPosition;
            return false;
        }

        /// <summary>
        /// Peeks at the next tag without advancing the position.
        /// Returns 0 if at end of buffer.
        /// Used for ProtoInclude wrapper detection in nested fields.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekTag()
        {
            if (position >= buffer.Length)
                return 0;

            // Fast path: single-byte varint (< 128) - covers ~90% of field IDs
            byte b = buffer[position];
            if (b < ProtobufConstants.VarintContinuationBit)
                return b;

            // Slow path: multi-byte varint - read without advancing position
            return PeekTagSlow();
        }

        /// <summary>
        /// Peeks at multi-byte varint tag without advancing position.
        /// </summary>
        private uint PeekTagSlow()
        {
            int tempPos = position;
            uint result = 0;
            int shift = 0;

            for (int i = 0; i < ProtobufConstants.MaxVarint32Size; i++) // Tag is at most 5 bytes (field ID up to 2^29)
            {
                if (tempPos >= buffer.Length)
                    return result; // EOF - return partial result

                byte b = buffer[tempPos++];
                result |= (uint)(b & ProtobufConstants.VarintValueMask) << shift;

                if (b < ProtobufConstants.VarintContinuationBit)
                    return result; // Last byte

                shift += ProtobufConstants.VarintShift;
            }

            // Malformed tag (> 5 bytes) - return what we have
            return result;
        }


        /// <summary>
        /// Skips an unknown field based on its wire type.
        /// Critical for forward compatibility - allows newer messages with additional fields
        /// to be parsed by older code without errors.
        /// </summary>
        /// <param name="wireType">Wire type of the field to skip.</param>
        /// <remarks>
        /// <para><b>Level200 Compliance:</b></para>
        /// - Unknown fields are DISCARDED (not preserved for round-tripping)
        /// - Length-delimited fields: Read length prefix, validate, then skip N bytes
        /// - Fixed-size fields: Validate buffer bounds before advancing position
        /// - Varint fields: Read full varint (validates structure)
        ///
        /// <para><b>Supported Wire Types:</b></para>
        /// - VarInt (0): Reads and discards varint
        /// - Fixed64b (1): Skips 8 bytes
        /// - Len (2): Reads length prefix, skips N bytes
        /// - StartGroup (3): Skips all fields until matching EndGroup (protobuf-net compatibility)
        /// - EndGroup (4): No-op, just a marker
        /// - Fixed32b (5): Skips 4 bytes
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// </exception>
        public void SkipField(WireType wireType)
        {
            switch (wireType)
            {
                case WireType.VarInt:
                    _ = ReadVarInt32(); // Varint validation already built-in
                    break;

                case WireType.Fixed32b:
                    // Level200: Validate buffer bounds before skipping
                    CheckLength(ProtobufConstants.Fixed32Size);
                    position += ProtobufConstants.Fixed32Size;
                    break;

                case WireType.Fixed64b:
                    // Level200: Validate buffer bounds before skipping
                    CheckLength(ProtobufConstants.Fixed64Size);
                    position += ProtobufConstants.Fixed64Size;
                    break;

                case WireType.Len:
                    // Level200: Read and validate length, then skip
                    int length = ReadVarInt32(); // Already validates non-negative and varint length
                    CheckLength(length); // Validate buffer bounds
                    position += length;
                    break;

                case WireType.StartGroup:
                    // protobuf-net compatibility: Skip all fields until EndGroup
                    SkipGroup();
                    break;

                case WireType.EndGroup:
                    // EndGroup is just a marker, nothing to skip
                    break;

                default:
                    throw new InvalidOperationException($"Unknown WireType: {wireType}. Position: {position}, Buffer length: {buffer.Length}");
            }
        }

        /// <summary>
        /// Skips a group (fields between StartGroup and EndGroup).
        /// Supports nested groups by tracking depth.
        /// Used for protobuf-net backward compatibility.
        /// </summary>
        private void SkipGroup()
        {
            int depth = 1; // We're already inside one group

            while (depth > 0 && !IsEnd)
            {
                ReadWireTypeAndFieldId(out var innerWireType, out var _);

                switch (innerWireType)
                {
                    case WireType.StartGroup:
                        depth++;
                        break;

                    case WireType.EndGroup:
                        depth--;
                        break;

                    default:
                        SkipField(innerWireType);
                        break;
                }
            }
        }
    }
}
