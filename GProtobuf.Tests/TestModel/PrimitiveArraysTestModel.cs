using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class PrimitiveArraysTestModel
    {
        // Float arrays
        [ProtoMember(1125124)]
        public float[] FloatArray { get; set; }
        
        [ProtoMember(2, IsPacked = true)]
        public float[] FloatArrayPacked { get; set; }

        // Double arrays  
        [ProtoMember(3)]
        public double[] DoubleArray { get; set; }
        
        [ProtoMember(4, IsPacked = true)]
        public double[] DoubleArrayPacked { get; set; }

        // Long arrays with different DataFormats
        [ProtoMember(5)]
        public long[] LongArray { get; set; }
        
        [ProtoMember(6, IsPacked = true)]
        public long[] LongArrayPacked { get; set; }
        
        [ProtoMember(7, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public long[] LongArrayPackedZigZag { get; set; }
        
        [ProtoMember(8, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public long[] LongArrayPackedFixed { get; set; }

        // Non-packed long arrays with different formats
        [ProtoMember(9, DataFormat = DataFormat.ZigZag)]
        public long[] LongArrayNonPackedZigZag { get; set; }
        
        [ProtoMember(10, DataFormat = DataFormat.FixedSize)]
        public long[] LongArrayNonPackedFixed { get; set; }

        // Boolean arrays
        [ProtoMember(11)]
        public bool[] BoolArray { get; set; }
        
        [ProtoMember(12, IsPacked = true)]
        public bool[] BoolArrayPacked { get; set; }

        // Edge case arrays
        [ProtoMember(13)]
        public float[] FloatArrayEmpty { get; set; }
        
        [ProtoMember(14)]
        public float[] FloatArrayNull { get; set; }
        
        [ProtoMember(15, IsPacked = true)]
        public double[] DoubleArrayPackedEmpty { get; set; }
        
        [ProtoMember(16, IsPacked = true)]
        public double[] DoubleArrayPackedNull { get; set; }

        // Large arrays for performance testing
        [ProtoMember(17, IsPacked = true)]
        public float[] FloatArrayPackedLarge { get; set; }
        
        [ProtoMember(18, IsPacked = true)]
        public long[] LongArrayPackedLarge { get; set; }

        // Mixed values arrays for testing edge cases
        [ProtoMember(19)]
        public float[] FloatArrayWithSpecialValues { get; set; } // NaN, Infinity, etc.
        
        [ProtoMember(20)]
        public double[] DoubleArrayWithSpecialValues { get; set; } // NaN, Infinity, etc.

        [ProtoMember(21)]
        public long[] LongArrayWithExtremeValues { get; set; } // Min/Max values

        [ProtoMember(22)]
        public bool[] BoolArrayMixed { get; set; } // Various true/false patterns

        // SByte arrays
        [ProtoMember(23)]
        public sbyte[] SByteArray { get; set; }
        
        [ProtoMember(24, IsPacked = true)]
        public sbyte[] SByteArrayPacked { get; set; }
        
        [ProtoMember(25, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public sbyte[] SByteArrayPackedZigZag { get; set; }

        // Short arrays
        [ProtoMember(26)]
        public short[] ShortArray { get; set; }
        
        [ProtoMember(27, IsPacked = true)]
        public short[] ShortArrayPacked { get; set; }
        
        [ProtoMember(28, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public short[] ShortArrayPackedZigZag { get; set; }
        
        [ProtoMember(29, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public short[] ShortArrayPackedFixed { get; set; }

        // UShort arrays
        [ProtoMember(30)]
        public ushort[] UShortArray { get; set; }
        
        [ProtoMember(31, IsPacked = true)]
        public ushort[] UShortArrayPacked { get; set; }
        
        [ProtoMember(32, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public ushort[] UShortArrayPackedFixed { get; set; }

        // UInt arrays
        [ProtoMember(33)]
        public uint[] UIntArray { get; set; }
        
        [ProtoMember(34, IsPacked = true)]
        public uint[] UIntArrayPacked { get; set; }
        
        [ProtoMember(35, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public uint[] UIntArrayPackedFixed { get; set; }

        // ULong arrays
        [ProtoMember(36)]
        public ulong[] ULongArray { get; set; }
        
        [ProtoMember(37, IsPacked = true)]
        public ulong[] ULongArrayPacked { get; set; }
        
        [ProtoMember(38, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public ulong[] ULongArrayPackedFixed { get; set; }

        // Edge case arrays for new types
        [ProtoMember(39)]
        public sbyte[] SByteArrayEmpty { get; set; }
        
        [ProtoMember(40)]
        public short[] ShortArrayNull { get; set; }
        
        [ProtoMember(41, IsPacked = true)]
        public ushort[] UShortArrayPackedEmpty { get; set; }
        
        [ProtoMember(42, IsPacked = true)]
        public uint[] UIntArrayPackedNull { get; set; }

        // Arrays with extreme values
        [ProtoMember(43)]
        public sbyte[] SByteArrayExtremes { get; set; } // Min/Max values
        
        [ProtoMember(44)]
        public ushort[] UShortArrayExtremes { get; set; } // Min/Max values
        
        [ProtoMember(45)]
        public uint[] UIntArrayExtremes { get; set; } // Min/Max values
        
        [ProtoMember(46)]
        public ulong[] ULongArrayExtremes { get; set; } // Min/Max values
    }
}