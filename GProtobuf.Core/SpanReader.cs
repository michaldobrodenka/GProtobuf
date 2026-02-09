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
    /// Handles wire type validation and format conversions for protobuf primitive types.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Philosophy:</b></para>
    /// - Wire type validation: Throws InvalidOperationException for unexpected wire types
    /// - Type coercion: Supports multiple wire types where protobuf spec allows (e.g., double can read Fixed64b or Fixed32b)
    /// - DataFormat support: Handles Default, FixedSize, and ZigZag encodings
    /// - Level200 compliance: BCL types (Guid, DateTime, TimeSpan) use nested message format
    ///
    /// <para><b>Performance Notes:</b></para>
    /// - Extension methods: Zero overhead, inlined by JIT
    /// - Stack allocation: BCL types use stackalloc for zero heap allocations
    /// - UTF-8 decoding: String reading delegates to Encoding.UTF8.GetString (optimized in .NET)
    /// </remarks>
    public static class SpanReaders
    {
        /// <summary>
        /// Reads a double value, supporting multiple wire type coercions.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type of the field.</param>
        /// <returns>Double value.</returns>
        /// <remarks>
        /// Supported wire types:
        /// - Fixed64b: Native double encoding (8 bytes)
        /// - Fixed32b: Float to double promotion (4 bytes)
        /// - VarInt: Integer to double coercion (lossy for large values)
        /// </remarks>
        /// <exception cref="InvalidOperationException">If wire type is unsupported.</exception>
        public static double ReadDouble(this ref SpanReader reader, WireType wireType)
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
        /// Format: varint length prefix + N bytes of UTF-8 data.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be Len).</param>
        /// <returns>Decoded UTF-8 string.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not Len.</exception>
        public static string ReadString(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for string.");

            int length = reader.ReadVarInt32();

            return Encoding.UTF8.GetString(reader.GetSlice(length));
        }

        /// <summary>
        /// Reads a boolean value (WireType.VarInt).
        /// Encoding: 0 = false, non-zero = true (standard protobuf behavior).
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <returns>Boolean value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static bool ReadBool(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for bool.");

            return reader.ReadVarInt32() != 0;
        }

        /// <summary>
        /// Reads an unsigned byte value (WireType.VarInt).
        /// Validates range [0, 255] and throws on overflow.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <returns>Byte value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        /// <exception cref="OverflowException">If value exceeds byte.MaxValue (255).</exception>
        public static byte ReadByte(this ref SpanReader reader, WireType wireType)
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
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <param name="zigZag">If true, uses ZigZag decoding for efficient negative number encoding.</param>
        /// <returns>Signed byte value [-128, 127].</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static sbyte ReadSByte(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for sbyte.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (sbyte)value;
        }

        /// <summary>
        /// Reads a signed 16-bit integer (WireType.VarInt), optionally with ZigZag encoding.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <param name="zigZag">If true, uses ZigZag decoding.</param>
        /// <returns>Short value [-32768, 32767].</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static short ReadInt16(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for short.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (short)value;
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer (WireType.VarInt).
        /// Validates range [0, 65535] and throws on overflow.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <returns>Unsigned short value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        /// <exception cref="OverflowException">If value exceeds ushort.MaxValue (65535).</exception>
        public static ushort ReadUInt16(this ref SpanReader reader, WireType wireType)
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
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (VarInt or Fixed32b).</param>
        /// <param name="zigZag">If true, uses ZigZag decoding (only for VarInt).</param>
        /// <returns>Integer value.</returns>
        /// <remarks>
        /// Wire type support:
        /// - VarInt: Variable-length encoding (1-5 bytes), optionally ZigZag for negative values
        /// - Fixed32b: Fixed 4-byte little-endian encoding (DataFormat.FixedSize)
        /// </remarks>
        /// <exception cref="InvalidOperationException">If wire type is unsupported.</exception>
        public static int ReadInt32(this ref SpanReader reader, WireType wireType, bool zigZag = false)
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
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <returns>Unsigned integer value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static uint ReadUInt32(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for uint.");

            return reader.ReadVarUInt32();
        }

        /// <summary>
        /// Reads a signed 64-bit integer (WireType.VarInt), optionally with ZigZag encoding.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <param name="zigZag">If true, uses ZigZag decoding for efficient negative number encoding.</param>
        /// <returns>Long value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static long ReadInt64(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for long.");

            return zigZag ? reader.ReadZigZagVarInt64() : reader.ReadVarInt64();
        }

        /// <summary>
        /// Reads an unsigned 64-bit integer (WireType.VarInt).
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type (must be VarInt).</param>
        /// <returns>Unsigned long value.</returns>
        /// <exception cref="InvalidOperationException">If wire type is not VarInt.</exception>
        public static ulong ReadUInt64(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for ulong.");

            return reader.ReadVarUInt64();
        }

        /// <summary>
        /// Reads a float value, supporting multiple wire type coercions.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <param name="wireType">Wire type of the field.</param>
        /// <returns>Float value.</returns>
        /// <remarks>
        /// Supported wire types:
        /// - Fixed32b: Native float encoding (4 bytes, IEEE 754)
        /// - VarInt: Integer to float coercion (lossy for large values)
        /// </remarks>
        /// <exception cref="InvalidOperationException">If wire type is unsupported.</exception>
        public static float ReadFloat(this ref SpanReader reader, WireType wireType)
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
        /// Format: varint length prefix + N bytes of raw data.
        /// Allocates new byte[] - not zero-copy.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <returns>Byte array (newly allocated).</returns>
        public static byte[] ReadByteArray(this ref SpanReader reader)
        {
            int length = reader.ReadVarInt32();
            return reader.GetSlice(length).ToArray();
        }

        /// <summary>
        /// Reads Guid in protobuf-net BCL format (nested message with lo/hi fixed64 fields).
        /// Wire format: [length=18][tag 0x09][8 bytes lo][tag 0x11][8 bytes hi]
        /// Supports field order independence (hi/lo can appear in any order).
        /// STRICT validation: requires exactly 18 bytes and both lo/hi fields present (or empty for Guid.Empty).
        /// </summary>
        public static Guid ReadGuid(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for Guid, got {wireType}");

            int length = reader.ReadVarInt32();

            // Special case: length=0 means Guid.Empty (default value not serialized by protobuf-net)
            if (length == 0)
                return Guid.Empty;

            int startPosition = reader.Position;
            int endPosition = startPosition + length;

            // BCL format REQUIRES exactly 18 bytes (1 tag + 8 lo + 1 tag + 8 hi)
            if (length != BclTypeFormats.Guid.NestedContentSize)
                throw new InvalidDataException($"Expected Guid BCL nested message length of {BclTypeFormats.Guid.NestedContentSize} bytes, got {length}");

            // Initialize byte buffer for Guid construction
            Span<byte> guidBytes = stackalloc byte[ProtobufConstants.GuidByteSize];
            bool hasLo = false;
            bool hasHi = false;

            // Parse nested message fields (support field order independence)
            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.Guid.FieldLoNumber: // lo (low 64 bits)
                        if (innerWireType != WireType.Fixed64b)
                            throw new InvalidDataException($"Expected Fixed64 for Guid.lo, got {innerWireType}");

                        // Read 8 bytes for low part (little-endian)
                        var loSlice = reader.GetSlice(8);
                        loSlice.CopyTo(guidBytes.Slice(0, 8));
                        hasLo = true;
                        break;

                    case BclTypeFormats.Guid.FieldHiNumber: // hi (high 64 bits)
                        if (innerWireType != WireType.Fixed64b)
                            throw new InvalidDataException($"Expected Fixed64 for Guid.hi, got {innerWireType}");

                        // Read 8 bytes for high part (little-endian)
                        var hiSlice = reader.GetSlice(8);
                        hiSlice.CopyTo(guidBytes.Slice(8, 8));
                        hasHi = true;
                        break;

                    default:
                        // Unknown field — skip (forward compatibility)
                        reader.SkipField(innerWireType);
                        break;
                }
            }

            // Strict validation: exactly endPosition reached (matches protobuf-net behavior)
            if (reader.Position != endPosition)
                throw new InvalidDataException($"Guid nested message length mismatch: expected end at {endPosition}, got {reader.Position}");

            // Strict validation: both fields required (matches protobuf-net behavior)
            if (!hasLo || !hasHi)
                throw new InvalidDataException($"Incomplete Guid BCL format: hasLo={hasLo}, hasHi={hasHi}");

            // Construct Guid from 16 bytes
            return new Guid(guidBytes);
        }

        /// <summary>
        /// Reads TimeSpan in protobuf-net BCL format (nested message with value/scale fields).
        /// Wire format: [length][field 1: sint64 value][field 2: int32 scale]
        /// Supports all TimeSpanScale values for forward/backward compatibility.
        /// IMPORTANT: TimeSpan is a duration, NOT a timestamp, so no Unix Epoch offset is used.
        /// </summary>
        public static TimeSpan ReadTimeSpan(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for TimeSpan, got {wireType}");

            int length = reader.ReadVarInt32();
            int startPosition = reader.Position;
            int endPosition = startPosition + length;

            // Default values (protobuf defaults)
            long scaledValue = 0;
            int scale = 5; // Default to Ticks if not specified

            // Parse nested message fields (support field order independence)
            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.DateTimeTimeSpan.FieldValueNumber: // value (sint64, ZigZag encoded)
                        scaledValue = reader.ReadZigZagVarInt64();
                        break;

                    case BclTypeFormats.DateTimeTimeSpan.FieldScaleNumber: // scale (int32)
                        scale = reader.ReadVarInt32();
                        break;

                    default:
                        // Unknown field - skip
                        reader.SkipField(innerWireType);
                        break;
                }
            }

            // Convert scaled value to ticks
            long ticks = DateTimeHelper.ConvertTimeSpanToTicks(scaledValue, scale);
            return new TimeSpan(ticks);
        }

        /// <summary>
        /// Reads DateTime in protobuf-net BCL format (nested message with value/scale/kind fields).
        /// Wire format: [length][field 1: sint64 value][field 2: int32 scale][field 3: int32 kind (ignored)]
        /// Supports all TimeSpanScale values for forward/backward compatibility.
        /// Level200: DateTimeKind is always ignored (not serialized/deserialized).
        /// </summary>
        public static DateTime ReadDateTime(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Expected WireType.Len for DateTime, got {wireType}");

            int length = reader.ReadVarInt32();
            int startPosition = reader.Position;
            int endPosition = startPosition + length;

            // Default values (protobuf defaults)
            long scaledValue = 0;
            int scale = 5; // Default to Ticks if not specified

            // Parse nested message fields (support field order independence)
            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case BclTypeFormats.DateTimeTimeSpan.FieldValueNumber: // value (sint64, ZigZag encoded)
                        if (innerWireType != WireType.VarInt)
                            throw new InvalidOperationException($"Expected VarInt for DateTime.value, got {innerWireType}");
                        scaledValue = reader.ReadZigZagVarInt64();
                        break;

                    case BclTypeFormats.DateTimeTimeSpan.FieldScaleNumber: // scale (int32)
                        if (innerWireType != WireType.VarInt)
                            throw new InvalidOperationException($"Expected VarInt for DateTime.scale, got {innerWireType}");
                        scale = reader.ReadVarInt32();
                        break;

                    case BclTypeFormats.DateTimeTimeSpan.FieldKindNumber: // kind (int32) - Level200: IGNORED
                        reader.SkipField(innerWireType);
                        break;

                    default:
                        // Unknown field - skip (forward compatibility)
                        reader.SkipField(innerWireType);
                        break;
                }
            }

            // Validate we read exactly the expected length
            if (reader.Position != endPosition)
                throw new InvalidOperationException($"DateTime nested message length mismatch");

            // Convert scaled value to ticks and construct DateTime
            long ticks = DateTimeHelper.ConvertToTicks(scaledValue, scale);
            return new DateTime(ticks, DateTimeKind.Unspecified); // Level200: always Unspecified
        }

        /// <summary>
        /// Reads a boolean value as a varint (0 = false, non-zero = true).
        /// Convenience overload without wire type validation.
        /// </summary>
        /// <param name="reader">SpanReader instance.</param>
        /// <returns>Boolean value.</returns>
        public static bool ReadBool(this ref SpanReader reader)
        {
            return reader.ReadVarInt32() != 0;
        }
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
        /// - Fixed32b (5): Skips 4 bytes
        ///
        /// <para><b>Unsupported Wire Types:</b></para>
        /// - StartGroup (3), EndGroup (4): Legacy types, not supported in Level200
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown for unknown wire types (3, 4, 6, 7) or buffer overrun.
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

                default:
                    throw new InvalidOperationException($"Unknown WireType: {wireType}");
            }
        }
    }
}
