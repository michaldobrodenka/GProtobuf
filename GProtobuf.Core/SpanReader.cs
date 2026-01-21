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
    public static class SpanReaders
    {
        //public static byte[] ReadByteArray(this ref SpanReader reader)
        //{
        //    var len = reader.ReadVarInt32();

        //    return new byte[len];
        //}

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

        //public static int ReadInt32(this ref SpanReader reader, WireType wireType, bool zigZag)
        //{
        //    if (wireType == WireType.VarInt)
        //    {
        //        int rawValue = reader.ReadVarInt32();

        //        if (zigZag)
        //            return (rawValue >> 1) ^ -(rawValue & 1); // Zigzag decoding
        //        else
        //            return rawValue;
        //    }
        //    else if (wireType == WireType.Fixed32b)
        //    {
        //        return reader.ReadFixedInt32();
        //    }

        //    throw new InvalidOperationException($"WireType {wireType} is not valid for int32.");
        //}

        public static string ReadString(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.Len)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for string.");

            int length = reader.ReadVarInt32(); // Prečítaj dĺžku stringu

            return Encoding.UTF8.GetString(reader.GetSlice(length)); // Dekódovanie UTF-8 stringu
        }

        public static bool ReadBool(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for bool.");

            return reader.ReadVarInt32() != 0;
        }

        public static byte ReadByte(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for byte.");

            uint value = reader.ReadVarUInt32(); // Use optimized version for unsigned
            if (value > byte.MaxValue)
                throw new OverflowException($"Value {value} is out of range for byte.");
            
            return (byte)value;
        }

        public static sbyte ReadSByte(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for sbyte.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (sbyte)value; // Direct cast handles overflow correctly
        }

        public static short ReadInt16(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for short.");

            int value = zigZag ? reader.ReadZigZagVarInt32() : reader.ReadVarInt32();
            return (short)value; // Direct cast handles overflow correctly
        }

        public static ushort ReadUInt16(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for ushort.");

            uint value = reader.ReadVarUInt32(); // Use optimized version for unsigned
            if (value > ushort.MaxValue)
                throw new OverflowException($"Value {value} is out of range for ushort.");
            
            return (ushort)value;
        }

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

        public static uint ReadUInt32(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for uint.");

            return reader.ReadVarUInt32(); // Use optimized version for unsigned
        }

        public static long ReadInt64(this ref SpanReader reader, WireType wireType, bool zigZag = false)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for long.");

            return zigZag ? reader.ReadZigZagVarInt64() : reader.ReadVarInt64();
        }

        public static ulong ReadUInt64(this ref SpanReader reader, WireType wireType)
        {
            if (wireType != WireType.VarInt)
                throw new InvalidOperationException($"Unexpected wire type {wireType} for ulong.");

            return reader.ReadVarUInt64();
        }

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
            if (length != 18)
                throw new InvalidDataException($"Expected Guid BCL nested message length of 18 bytes, got {length}");

            // Initialize byte buffer for Guid construction
            Span<byte> guidBytes = stackalloc byte[16];
            bool hasLo = false;
            bool hasHi = false;

            // Parse nested message fields (support field order independence)
            while (reader.Position < endPosition)
            {
                reader.ReadWireTypeAndFieldId(out var innerWireType, out var fieldId);

                switch (fieldId)
                {
                    case 1: // lo (low 64 bits)
                        if (innerWireType != WireType.Fixed64b)
                            throw new InvalidDataException($"Expected Fixed64 for Guid.lo, got {innerWireType}");

                        // Read 8 bytes for low part (little-endian)
                        var loSlice = reader.GetSlice(8);
                        loSlice.CopyTo(guidBytes.Slice(0, 8));
                        hasLo = true;
                        break;

                    case 2: // hi (high 64 bits)
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
        /// Reads a TimeSpan value (serialized as Ticks - VarInt64).
        /// Zero allocations, fast serialization.
        /// </summary>
        public static TimeSpan ReadTimeSpan(this ref SpanReader reader, WireType wireType)
        {
            long ticks = reader.ReadVarInt64();
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
                    case 1: // value (sint64, ZigZag encoded)
                        if (innerWireType != WireType.VarInt)
                            throw new InvalidOperationException($"Expected VarInt for DateTime.value, got {innerWireType}");
                        scaledValue = reader.ReadZigZagVarInt64();
                        break;

                    case 2: // scale (int32)
                        if (innerWireType != WireType.VarInt)
                            throw new InvalidOperationException($"Expected VarInt for DateTime.scale, got {innerWireType}");
                        scale = reader.ReadVarInt32();
                        break;

                    case 3: // kind (int32) - Level200: IGNORED
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
        /// </summary>
        public static bool ReadBool(this ref SpanReader reader)
        {
            return reader.ReadVarInt32() != 0;
        }
    }

    public ref partial struct SpanReader
    {
        private ReadOnlySpan<byte> buffer;
        private int position;

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

        public SpanReader(ReadOnlySpan<byte> buffer)
        {
            this.buffer = buffer;
            this.position = 0;
        }

        public bool IsEnd => position >= buffer.Length;
        
        /// <summary>
        /// Gets a value indicating whether the reader has reached the end of the data.
        /// </summary>
        public bool EndOfData => position >= buffer.Length;

        //public byte ReadByte2()
        //{
        //    if (position >= buffer.Length) throw new InvalidOperationException("End of buffer");
        //    return buffer[position++];
        //}

        public void CheckLength(int length)
        {
            // Level200: Length prefix must be non-negative
            if (length < 0)
                throw new InvalidDataException($"Malformed length prefix: length cannot be negative (got {length})");

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");
        }

        public ReadOnlySpan<byte> GetSlice(int length)
        {
            CheckLength(length);

            ReadOnlySpan<byte> result = buffer.Slice(position, length);
            position += length; // Posun pozície

            return result;
        }

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
            if (b < 0x80) return b;

            long result = b & 0x7FL;

            // Byte 2
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return (int)(result | ((long)b << 7));
            result |= (b & 0x7FL) << 7;

            // Byte 3
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return (int)(result | ((long)b << 14));
            result |= (b & 0x7FL) << 14;

            // Byte 4
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return (int)(result | ((long)b << 21));
            result |= (b & 0x7FL) << 21;

            // Byte 5
            if (position >= buffer.Length) return (int)result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return (int)(result | ((long)b << 28));
            result |= (b & 0x7FL) << 28;

            // Bytes 6-10: Rare case, use loop
            for (int shift = 35; shift < 70; shift += 7)
            {
                if (position >= buffer.Length) return (int)result; // EOF tolerance
                b = buffer[position++];
                if (b < 0x80) return (int)(result | ((long)b << shift));
                result |= (b & 0x7FL) << shift;
            }

            // 11+ bytes - malformed
            throw new InvalidDataException("Malformed varint: varint exceeded maximum length of 10 bytes");
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
            if (b < 0x80) return b;

            uint result = (uint)(b & 0x7F);

            // Byte 2
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((uint)b << 7);
            result |= (uint)(b & 0x7F) << 7;

            // Byte 3
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((uint)b << 14);
            result |= (uint)(b & 0x7F) << 14;

            // Byte 4
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((uint)b << 21);
            result |= (uint)(b & 0x7F) << 21;

            // Byte 5 (optimal for uint32)
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((uint)b << 28);

            // Bytes 6-10: Rare case (backward compatibility), use ulong to avoid overflow
            ulong result64 = result | ((ulong)(b & 0x7F) << 28);
            for (int shift = 35; shift < 70; shift += 7)
            {
                if (position >= buffer.Length) return (uint)result64; // EOF tolerance
                b = buffer[position++];
                if (b < 0x80) return (uint)result64; // Truncate to uint32
                result64 |= (ulong)(b & 0x7F) << shift;
            }

            throw new InvalidDataException("Malformed varint: varint exceeded maximum length of 10 bytes");
        }

        public int ReadZigZagVarInt32()
        {
            uint result = ReadVarUInt32(); // Use unsigned version for ZigZag
            return (int)((result >> 1) ^ (0U - (result & 1))); // Zigzag decoding for 32-bit
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
            if (b < 0x80) return b;

            long result = b & 0x7FL;

            // Bytes 2-5: Unrolled
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((long)b << 7);
            result |= (b & 0x7FL) << 7;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((long)b << 14);
            result |= (b & 0x7FL) << 14;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((long)b << 21);
            result |= (b & 0x7FL) << 21;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((long)b << 28);
            result |= (b & 0x7FL) << 28;

            // Bytes 6-10: Loop
            for (int shift = 35; shift < 70; shift += 7)
            {
                if (position >= buffer.Length) return result; // EOF tolerance
                b = buffer[position++];
                if (b < 0x80) return result | ((long)b << shift);
                result |= (b & 0x7FL) << shift;
            }

            throw new InvalidDataException("Malformed varint: int64 varint exceeded maximum length of 10 bytes");
        }

        public long ReadZigZagVarInt64()
        {
            ulong result = ReadVarUInt64(); // Use unsigned version for ZigZag
            return (long)((result >> 1) ^ (0UL - (result & 1))); // Zigzag decoding for 64-bit
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
            if (b < 0x80) return b;

            ulong result = (ulong)(b & 0x7F);

            // Bytes 2-5: Unrolled
            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((ulong)b << 7);
            result |= (ulong)(b & 0x7F) << 7;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((ulong)b << 14);
            result |= (ulong)(b & 0x7F) << 14;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((ulong)b << 21);
            result |= (ulong)(b & 0x7F) << 21;

            if (position >= buffer.Length) return result; // EOF tolerance
            b = buffer[position++];
            if (b < 0x80) return result | ((ulong)b << 28);
            result |= (ulong)(b & 0x7F) << 28;

            // Bytes 6-10: Loop
            for (int shift = 35; shift < 70; shift += 7)
            {
                if (position >= buffer.Length) return result; // EOF tolerance
                b = buffer[position++];
                if (b < 0x80) return result | ((ulong)b << shift);
                result |= (ulong)(b & 0x7F) << shift;
            }

            throw new InvalidDataException("Malformed varint: uint64 varint exceeded maximum length of 10 bytes");
        }

        public double ReadFixedDouble()
        {
            if (position + sizeof(double) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadDoubleLittleEndian(buffer.Slice(position));
            position += sizeof(double); // Posun o 8 bajtov
            return value;
        }

        public float ReadFixedFloat()
        {
            if (position + sizeof(float) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            // Prečítať 32-bitový float a konvertovať na double
            float floatValue = BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(position));
            position += sizeof(float); // Posun o 4 bajty
            return floatValue;
        }

        public ulong ReadFixed64()
        {
            if (position + sizeof(ulong) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(position));
            position += sizeof(ulong); // Move by 8 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 64-bit signed integer (8 bytes, little-endian).
        /// </summary>
        public long ReadFixedInt64()
        {
            if (position + sizeof(long) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(position));
            position += sizeof(long); // Move by 8 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 16-bit signed integer (2 bytes, little-endian).
        /// </summary>
        public short ReadFixedInt16()
        {
            if (position + sizeof(short) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(position));
            position += sizeof(short); // Move by 2 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 16-bit unsigned integer (2 bytes, little-endian).
        /// </summary>
        public ushort ReadFixedUInt16()
        {
            if (position + sizeof(ushort) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(position));
            position += sizeof(ushort); // Move by 2 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 32-bit unsigned integer (4 bytes, little-endian).
        /// </summary>
        public uint ReadFixedUInt32()
        {
            if (position + sizeof(uint) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(position));
            position += sizeof(uint); // Move by 4 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 64-bit unsigned integer (8 bytes, little-endian).
        /// </summary>
        public ulong ReadFixedUInt64()
        {
            if (position + sizeof(ulong) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            var value = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(position));
            position += sizeof(ulong); // Move by 8 bytes
            return value;
        }

        /// <summary>
        /// Reads a fixed-size 32-bit signed integer (4 bytes, little-endian).
        /// </summary>
        public int ReadFixedInt32()
        {
            if (position + sizeof(int) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            int value = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(position));
            position += sizeof(int); // Move by 4 bytes
            return value;
        }

        //public int ReadVarIntAsDouble()
        //{
        //    throw new NotImplementedException();
        //    //int result = 0;
        //    //int shift = 0;
        //    //byte b;
        //    //do
        //    //{
        //    //    int readByte = GetByte();

        //    //    if (readByte < 0)
        //    //        break;

        //    //    b = (byte)readByte;

        //    //    result |= (b & 0x7F) << shift;
        //    //    shift += 7;
        //    //} while ((b & 0x80) != 0);

        //    //return result;
        //}

        public (WireType wireType, int fieldId) ReadWireTypeAndFieldId()
        {
            var typeAndFieldId = this.ReadVarInt32();

            var type = (WireType)(typeAndFieldId & 0b111);
            var fieldId = typeAndFieldId >> 3;

            return (type, fieldId);
        }

        /// <summary>
        /// Reads wire type and field ID using out parameters (zero allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadWireTypeAndFieldId(out WireType wireType, out int fieldId)
        {
            var typeAndFieldId = this.ReadVarInt32();
            wireType = (WireType)(typeAndFieldId & 0b111);
            fieldId = typeAndFieldId >> 3;
        }

        /// <summary>
        /// Reads a key (tag) and returns the field ID and wire type.
        /// </summary>
        public (WireType wireType, int fieldId) ReadKey()
        {
            return ReadWireTypeAndFieldId();
        }

   
        public void SkipField(WireType wireType)
        {
            switch (wireType)
            {
                case WireType.VarInt:
                    _ = ReadVarInt32(); // Varint validation already built-in
                    break;

                case WireType.Fixed32b:
                    // Level200: Validate buffer bounds before skipping
                    CheckLength(4);
                    position += 4;
                    break;

                case WireType.Fixed64b:
                    // Level200: Validate buffer bounds before skipping
                    CheckLength(8);
                    position += 8;
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

        //public ReadOnlySpan<byte> ReadBytes(int length)
        //{
        //    if (position + length > buffer.Length) throw new InvalidOperationException("Buffer overrun");
        //    var slice = buffer.Slice(position, length);
        //    position += length;
        //    return slice;
        //}
    }
}
