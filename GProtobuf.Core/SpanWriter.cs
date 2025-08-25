//using System;
//using System.Buffers.Binary;
//using System.Text;

//namespace GProtobuf.Core
//{
//    public ref struct SpanWriter
//    {
//        private Span<byte> _buffer;
//        private int _position;

//        public SpanWriter(Span<byte> buffer)
//        {
//            _buffer = buffer;
//            _position = 0;
//        }

//        public int Position => _position;
//        public Span<byte> Span => _buffer;

//        private void EnsureSpace(int length)
//        {
//            if (_position + length > _buffer.Length)
//                throw new InvalidOperationException("Buffer overflow.");
//        }

//        // Write Tag (field ID + wire type)
//        public void WriteTag(int fieldId, WireType wireType)
//        {
//            int tag = (fieldId << 3) | (int)wireType;
//            WriteVarInt32(tag);
//        }

//        // Write VarInt (int32, uint32)
//        public void WriteVarInt32(int value)
//        {
//            EnsureSpace(5); // Max 5 bajtov pre 32-bit VarInt
//            while (value > 0x7F)
//            {
//                _buffer[_position++] = (byte)((value & 0x7F) | 0x80);
//                value >>= 7;
//            }
//            _buffer[_position++] = (byte)value;
//        }

//        // Write VarInt (int64, uint64)
//        public void WriteVarInt64(long value)
//        {
//            EnsureSpace(10); // Max 10 bajtov pre 64-bit VarInt
//            while (value > 0x7F)
//            {
//                _buffer[_position++] = (byte)((value & 0x7F) | 0x80);
//                value >>= 7;
//            }
//            _buffer[_position++] = (byte)value;
//        }

//        // Write ZigZag-encoded int32
//        public void WriteZigZag32(int value)
//        {
//            WriteVarInt32((value << 1) ^ (value >> 31));
//        }

//        // Write ZigZag-encoded int64
//        public void WriteZigZag64(long value)
//        {
//            WriteVarInt64((value << 1) ^ (value >> 63));
//        }

//        // Write Fixed32 (float, int32)
//        public void WriteFixed32(uint value)
//        {
//            EnsureSpace(4);
//            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.Slice(_position), value);
//            _position += 4;
//        }

//        // Write Fixed64 (double, int64)
//        public void WriteFixed64(ulong value)
//        {
//            EnsureSpace(8);
//            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_position), value);
//            _position += 8;
//        }

//        // Write String (LengthPrefixed)
//        public void WriteString(string value)
//        {
//            byte[] utf8Bytes = Encoding.UTF8.GetBytes(value);
//            WriteVarInt32(utf8Bytes.Length); // Dĺžka ako VarInt
//            EnsureSpace(utf8Bytes.Length);
//            utf8Bytes.CopyTo(_buffer.Slice(_position));
//            _position += utf8Bytes.Length;
//        }

//        // Write Bytes (LengthPrefixed)
//        public void WriteBytes(ReadOnlySpan<byte> value)
//        {
//            WriteVarInt32(value.Length);
//            EnsureSpace(value.Length);
//            value.CopyTo(_buffer.Slice(_position));
//            _position += value.Length;
//        }

//        // Write Double (Fixed64)
//        public void WriteDouble(double value)
//        {
//            WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
//        }

//        // Write Float (Fixed32)
//        public void WriteFloat(float value)
//        {
//            WriteFixed32(BitConverter.SingleToUInt32Bits(value));
//        }

//        // Write Bool (VarInt)
//        public void WriteBool(bool value)
//        {
//            WriteVarInt32(value ? 1 : 0);
//        }

//        // Write Length-prefixed message (nested)
//        public void WriteMessage(Action<SpanWriter> writeAction)
//        {
//            int lengthPos = _position; // Rezervuj miesto na dĺžku
//            _position += 4; // Max 4 bajty pre VarInt dĺžku

//            int messageStart = _position;
//            writeAction(this); // Zapíš vnorenú správu

//            int messageLength = _position - messageStart;

//            // Spätné prepísanie dĺžky ako VarInt
//            Span<byte> lengthSpan = _buffer.Slice(lengthPos, 4);
//            int written = WriteVarIntToSpan(messageLength, lengthSpan);

//            // Posun ak sme nepoužili celé 4 bajty
//            if (written < 4)
//            {
//                lengthSpan.Slice(written).CopyTo(lengthSpan.Slice(4 - written));
//                _position -= (4 - written);
//            }
//        }

//        private int WriteVarIntToSpan(int value, Span<byte> span)
//        {
//            int count = 0;
//            while (value > 0x7F)
//            {
//                span[count++] = (byte)((value & 0x7F) | 0x80);
//                value >>= 7;
//            }
//            span[count++] = (byte)value;
//            return count;
//        }
//    }
//}