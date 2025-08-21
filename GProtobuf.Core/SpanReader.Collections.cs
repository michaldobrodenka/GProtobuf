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
            var length = ReadVarUInt32();

            if (length == 0)
                return Array.Empty<int>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size array length.");

            var slice = buffer.Slice(position, (int)length);
            position += (int)length;

            int count = (int)length / 4;
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

        #region Float Array Methods

        /// <summary>
        /// Reads a packed float array from the buffer.
        /// Floats are always fixed-size (4 bytes each) in packed format.
        /// </summary>
        public float[] ReadPackedFloatArray()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<float>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed float array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 4;
            float[] result = new float[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadSingleLittleEndian(slice.Slice(p, 4));
                p += 4;
            }

            return result;
        }

        #endregion

        #region Double Array Methods

        /// <summary>
        /// Reads a packed double array from the buffer.
        /// Doubles are always fixed-size (8 bytes each) in packed format.
        /// </summary>
        public double[] ReadPackedDoubleArray()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<double>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 8 != 0)
                throw new InvalidOperationException("Invalid packed double array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 8;
            double[] result = new double[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadDoubleLittleEndian(slice.Slice(p, 8));
                p += 8;
            }

            return result;
        }

        #endregion

        #region Long Array Methods

        /// <summary>
        /// Reads a packed fixed-size long array (8 bytes each).
        /// </summary>
        public long[] ReadPackedFixedSizeInt64Array()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<long>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 8 != 0)
                throw new InvalidOperationException("Invalid packed fixed size long array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 8;
            long[] result = new long[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadInt64LittleEndian(slice.Slice(p, 8));
                p += 8;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed varint long array with optional ZigZag decoding.
        /// </summary>
        public long[] ReadPackedVarIntInt64Array(bool zigZag)
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarInt64ArrayLength(length);

            if (length == 0)
                return Array.Empty<long>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            long[] result = new long[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                result[i] = zigZag ? ReadZigZagVarInt64() : ReadVarInt64();
                i++;
            }

            return result;
        }

        /// <summary>
        /// Measures the number of VarInt64 values in a packed array.
        /// </summary>
        public int MeasureVarInt64ArrayLength(int length)
        {
            var initialPosition = this.position;
            int result = 0;
            int end = position + length;
            
            while (position < end)
            {
                _ = ReadVarInt64();
                result++;
            }

            this.position = initialPosition;
            return result;
        }

        #endregion

        #region Bool Array Methods

        /// <summary>
        /// Reads a packed boolean array.
        /// Booleans are encoded as varints (0 or 1).
        /// </summary>
        public bool[] ReadPackedBoolArray()
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureBoolArrayLength(length);

            if (length == 0)
                return Array.Empty<bool>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            bool[] result = new bool[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                result[i] = ReadVarInt32() != 0;
                i++;
            }

            return result;
        }

        /// <summary>
        /// Measures the number of boolean values in a packed array.
        /// </summary>
        public int MeasureBoolArrayLength(int length)
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

        #endregion

        #region SByte Array Methods

        /// <summary>
        /// Reads a packed sbyte array.
        /// SBytes are encoded as varints with ZigZag support.
        /// </summary>
        public sbyte[] ReadPackedSByteArray(bool zigZag = false)
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarIntArrayLength(length);

            if (length == 0)
                return Array.Empty<sbyte>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            sbyte[] result = new sbyte[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                int value = zigZag ? ReadZigZagVarInt32() : ReadVarInt32();
                result[i] = (sbyte)value;
                i++;
            }

            return result;
        }

        #endregion

        #region Short Array Methods

        /// <summary>
        /// Reads a packed short array.
        /// Shorts are encoded as varints with ZigZag support.
        /// </summary>
        public short[] ReadPackedInt16Array(bool zigZag = false)
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarIntArrayLength(length);

            if (length == 0)
                return Array.Empty<short>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            short[] result = new short[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                int value = zigZag ? ReadZigZagVarInt32() : ReadVarInt32();
                result[i] = (short)value;
                i++;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size short array (2 bytes each).
        /// </summary>
        public short[] ReadPackedFixedSizeInt16Array()
        {
            var length = ReadVarUInt32();

            if (length == 0)
                return Array.Empty<short>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size short array length (must be multiple of 4 bytes - protobuf uses fixed32 for 16-bit values).");

            var slice = buffer.Slice(position, (int)length);
            position += (int)length;

            int count = (int)length / 4;
            short[] result = new short[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(p, 4));
                result[i] = (short)value; // Convert from int32 to short (protobuf doesn't have fixed16)
                p += 4;
            }

            return result;
        }

        #endregion

        #region UShort Array Methods

        /// <summary>
        /// Reads a packed ushort array.
        /// UShorts are encoded as varints (always positive).
        /// </summary>
        public ushort[] ReadPackedUInt16Array()
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarIntArrayLength(length);

            if (length == 0)
                return Array.Empty<ushort>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            ushort[] result = new ushort[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                uint value = ReadVarUInt32();
                result[i] = (ushort)value;
                i++;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size ushort array (2 bytes each).
        /// </summary>
        public ushort[] ReadPackedFixedSizeUInt16Array()
        {
            var length = ReadVarUInt32();

            if (length == 0)
                return Array.Empty<ushort>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size ushort array length (must be multiple of 4 bytes - protobuf uses fixed32 for 16-bit values).");

            var slice = buffer.Slice(position, (int)length);
            position += (int)length;

            int count = (int)length / 4;
            ushort[] result = new ushort[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(slice.Slice(p, 4));
                result[i] = (ushort)value; // Convert from uint32 to ushort (protobuf doesn't have fixed16)
                p += 4;
            }

            return result;
        }

        #endregion

        #region UInt Array Methods

        /// <summary>
        /// Reads a packed uint array.
        /// UInts are encoded as varints (always positive).
        /// </summary>
        public uint[] ReadPackedUInt32Array()
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarIntArrayLength(length);

            if (length == 0)
                return Array.Empty<uint>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            uint[] result = new uint[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                result[i] = ReadVarUInt32();
                i++;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size uint array (4 bytes each).
        /// </summary>
        public uint[] ReadPackedFixedSizeUInt32Array()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<uint>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size uint array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 4;
            uint[] result = new uint[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt32LittleEndian(slice.Slice(p, 4));
                p += 4;
            }

            return result;
        }

        #endregion

        #region ULong Array Methods

        /// <summary>
        /// Reads a packed ulong array.
        /// ULongs are encoded as varints (always positive).
        /// </summary>
        public ulong[] ReadPackedUInt64Array()
        {
            var length = ReadVarInt32();

            var arrayLength = MeasureVarInt64ArrayLength(length);

            if (length == 0)
                return Array.Empty<ulong>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            ulong[] result = new ulong[arrayLength];
            int end = position + length;
            int i = 0;

            while (position < end)
            {
                result[i] = ReadVarUInt64();
                i++;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size ulong array (8 bytes each).
        /// </summary>
        public ulong[] ReadPackedFixedSizeUInt64Array()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<ulong>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 8 != 0)
                throw new InvalidOperationException("Invalid packed fixed size ulong array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 8;
            ulong[] result = new ulong[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt64LittleEndian(slice.Slice(p, 8));
                p += 8;
            }

            return result;
        }

        #endregion

        #region 16-bit Types Fixed Size Support (via 32-bit)

        /// <summary>
        /// Reads a packed fixed-size int32 array and converts to int16 array.
        /// Protocol Buffers doesn't have fixed16 - uses fixed32 for 16-bit values.
        /// </summary>
        public short[] ReadPackedFixedSizeInt32ArrayAsInt16()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<short>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size int32 array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 4;
            short[] result = new short[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(slice.Slice(p, 4));
                result[i] = (short)value; // Convert from int32 to int16
                p += 4;
            }

            return result;
        }

        /// <summary>
        /// Reads a packed fixed-size uint32 array and converts to uint16 array.
        /// Protocol Buffers doesn't have fixed16 - uses fixed32 for 16-bit values.
        /// </summary>
        public ushort[] ReadPackedFixedSizeUInt32ArrayAsUInt16()
        {
            var length = ReadVarInt32();

            if (length == 0)
                return Array.Empty<ushort>();

            if (position + length > buffer.Length)
                throw new InvalidOperationException("Buffer overrun");

            if (length % 4 != 0)
                throw new InvalidOperationException("Invalid packed fixed size uint32 array length.");

            var slice = buffer.Slice(position, length);
            position += length;

            int count = length / 4;
            ushort[] result = new ushort[count];
            int p = 0;
            for (int i = 0; i < count; i++)
            {
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(slice.Slice(p, 4));
                result[i] = (ushort)value; // Convert from uint32 to uint16
                p += 4;
            }

            return result;
        }

        #endregion
    }
}
