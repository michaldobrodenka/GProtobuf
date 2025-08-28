using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class PrimitiveCollectionsTestModel
    {
        // Basic List<T> types
        [ProtoMember(1)]
        public List<int> IntList { get; set; }

        [ProtoMember(21243125)]
        public List<float> FloatList { get; set; }

        [ProtoMember(3)]
        public List<double> DoubleList { get; set; }

        [ProtoMember(4)]
        public List<long> LongList { get; set; }

        [ProtoMember(5)]
        public List<bool> BoolList { get; set; }

        // ICollection<T> types
        [ProtoMember(6)]
        public ICollection<int> IntICollection { get; set; }

        [ProtoMember(7)]
        public ICollection<float> FloatICollection { get; set; }

        [ProtoMember(8)]
        public ICollection<double> DoubleICollection { get; set; }

        // IList<T> types  
        [ProtoMember(9)]
        public IList<long> LongIList { get; set; }

        [ProtoMember(10)]
        public IList<bool> BoolIList { get; set; }

        // IEnumerable<T> types
        [ProtoMember(11)]
        public IEnumerable<int> IntIEnumerable { get; set; }

        [ProtoMember(12)]
        public IEnumerable<float> FloatIEnumerable { get; set; }

        // DataFormat variations
        [ProtoMember(13, DataFormat = DataFormat.ZigZag)]
        public List<int> ZigZagIntList { get; set; }

        [ProtoMember(14, DataFormat = DataFormat.FixedSize)]
        public List<int> FixedSizeIntList { get; set; }

        [ProtoMember(15, DataFormat = DataFormat.ZigZag)]
        public ICollection<long> ZigZagLongICollection { get; set; }

        [ProtoMember(16, DataFormat = DataFormat.FixedSize)]
        public IList<long> FixedSizeLongIList { get; set; }

        // IsPacked variations
        [ProtoMember(17, IsPacked = true)]
        public List<float> PackedFloatList { get; set; }

        [ProtoMember(18, IsPacked = false)]
        public List<double> NonPackedDoubleList { get; set; }

        [ProtoMember(19, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public ICollection<int> PackedZigZagIntICollection { get; set; }

        [ProtoMember(20, IsPacked = false, DataFormat = DataFormat.FixedSize)]
        public IList<int> NonPackedFixedSizeIntIList { get; set; }

        // Byte collections (should serialize as byte[])
        [ProtoMember(21)]
        public List<byte> ByteList { get; set; }

        [ProtoMember(22)]
        public ICollection<byte> ByteICollection { get; set; }

        [ProtoMember(23)]
        public IList<byte> ByteIList { get; set; }

        [ProtoMember(24)]
        public IEnumerable<byte> ByteIEnumerable { get; set; }

        // Additional primitive types
        [ProtoMember(25)]
        public List<sbyte> SByteList { get; set; }

        [ProtoMember(26)]
        public List<short> ShortList { get; set; }

        [ProtoMember(27)]
        public List<ushort> UShortList { get; set; }

        [ProtoMember(28)]
        public List<uint> UIntList { get; set; }

        [ProtoMember(29)]
        public List<ulong> ULongList { get; set; }

        // ZigZag for signed types
        [ProtoMember(30, DataFormat = DataFormat.ZigZag)]
        public ICollection<sbyte> ZigZagSByteICollection { get; set; }

        [ProtoMember(31, DataFormat = DataFormat.ZigZag)]
        public IList<short> ZigZagShortIList { get; set; }

        // FixedSize for appropriate types
        [ProtoMember(32, DataFormat = DataFormat.FixedSize)]
        public List<uint> FixedSizeUIntList { get; set; }

        [ProtoMember(33, DataFormat = DataFormat.FixedSize)]
        public ICollection<ulong> FixedSizeULongICollection { get; set; }

        // Test case for ReadPackedFixedSizeInt32List() optimization
        [ProtoMember(34, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public List<int> PackedFixedSizeIntList { get; set; }

        // Char collections
        [ProtoMember(35)]
        public List<char> CharList { get; set; }

        [ProtoMember(36, IsPacked = true)]
        public List<char> PackedCharList { get; set; }

        [ProtoMember(37)]
        public ICollection<char> CharICollection { get; set; }

        [ProtoMember(38)]
        public IList<char> CharIList { get; set; }
    }
}