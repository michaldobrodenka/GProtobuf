using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public ref partial struct SpanReader
    {
        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (position + length > buffer.Length) throw new InvalidOperationException("Buffer overrun");
            var slice = buffer.Slice(position, length);
            position += length;
            return slice;
        }

        public byte[] ReadByteArray()
        {
            var length = ReadVarInt32();

            if (position + length > buffer.Length) throw new InvalidOperationException("Buffer overrun");
            var slice = buffer.Slice(position, length);
            position += length;
            return slice.ToArray();
        }

        public List<byte> ReadListByte(int length)
        {
            if (position + length > buffer.Length) throw new InvalidOperationException("Buffer overrun");
            var slice = buffer.Slice(position, length);
            position += length;

            var result = new List<byte>(length);
            result.AddRange(slice);

            return result;
        }

        public int[] ReadPackedFixedSizeInt32Array()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<int>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 4;
            int[] fixedSizeResult = new int[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                fixedSizeResult[i] = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(p, 4));
                p += 4;
            }

            return fixedSizeResult;
        }

        //public int[] ReadPackedVarIntInt32Array(bool zigZag)
        //{
        //    var length = ReadVarInt32();

        //    if (length == 0)
        //        return Array.Empty<int>();

        //    if (position + length > buffer.Length)
        //        throw new InvalidOperationException("Buffer overrun");

        //    if (zigZag)
        //    {
        //        //position += length;

        //        List<int> result = new();
        //        int end = position + length;

        //        while (position < end)
        //        {
        //            result.Add(ReadZigZagVarInt32());
        //        }

        //        return result.ToArray();
        //    }
        //    else
        //    {
        //        //position += length;

        //        List<int> result = new();
        //        int end = position + length;

        //        while (position < end)
        //        {
        //            result.Add(ReadVarInt32());
        //        }

        //        return result.ToArray();
        //    }
        //}

        public int[] ReadPackedVarIntInt32Array(bool zigZag)
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarIntArrayLength(length);

            if (length == 0)
                return Array.Empty<int>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");
            int i = 0;
            if (zigZag)
            {
                int[] result = new int[arrayLength]; 
                //List<int> result = new();
                int end = position + length;

                while (position < end)
                {
                    result[i] = ReadZigZagVarInt32();
                    i++;
                }

                return result;
            }
            else
            {
                int[] result = new int[arrayLength];
                //List<int> result = new();
                int end = position + length;

                while (position < end)
                {
                    result[i] = ReadVarInt32();
                    i++;
                }

                return result;
            }
        }

        public int MeasureVarIntArrayLength(int length)
        {
            var initialPosition = this.position;
            int result = 0;
            int end = position + length;
            while (position < end)
            {
                _ = ReadVarInt32();
                result++;
            }

            this.position = initialPosition;

            return result;
        }
    }
}
