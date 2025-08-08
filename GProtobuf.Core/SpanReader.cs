using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
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

        //public byte ReadByte2()
        //{
        //    if (position >= buffer.Length) throw new InvalidOperationException("End of buffer");
        //    return buffer[position++];
        //}

        public void CheckLength(int length)
        {
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

        public int ReadVarInt32()
        {
            int result = 0;
            int shift = 0;
            byte b;
            do
            {
                int readByte = GetByte();

                if (readByte < 0)
                    break;

                b = (byte)readByte;

                result |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return result;
        }

        public int ReadFixedInt32()
        {
            if (position + sizeof(int) > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            int intValue = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(position));
            position += sizeof(int); // Posun o 4 bajty
            return intValue;
        }

        public int ReadZigZagVarInt32()
        {
            int result = 0;
            int shift = 0;
            byte b;
            do
            {
                int readByte = GetByte();

                if (readByte < 0)
                    break;

                b = (byte)readByte;

                result |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return (result >> 1) ^ -(result & 1); // Zigzag decoding;
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

        //public int ReadFixedInt32()
        //{
        //    if (position + sizeof(int) > buffer.Length)
        //        throw new InvalidOperationException("Buffer overrun");

        //    int value = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(position));
        //    position += sizeof(int); // Posun o 4 bajty
        //    return value;
        //}

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

   
        public void SkipField(WireType wireType)
        {
            switch (wireType)
            {
                case WireType.VarInt:
                    _ = ReadVarInt32();
                    //throw new NotImplementedException();
                    //ReadVarint64(); // Preskočí celé varint číslo
                    break;

                case WireType.Fixed32b:
                    position += 4; // Preskočí 4 bajty (Fixed32)
                    break;

                case WireType.Fixed64b:
                    position += 8; // Preskočí 8 bajtov (Fixed64)
                    break;

                case WireType.Len:
                    //throw new NotImplementedException();
                    int length = ReadVarInt32();
                    position += length; // Preskočí celé length-prefixed dáta
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
