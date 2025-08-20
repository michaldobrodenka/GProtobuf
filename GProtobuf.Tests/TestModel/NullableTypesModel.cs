using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class NullableTypesModel
    {
        [ProtoMember(1)]
        public byte? NullableByteValue { get; set; }

        [ProtoMember(2)]
        public sbyte? NullableSByteValue { get; set; }

        [ProtoMember(3)]
        public short? NullableShortValue { get; set; }

        [ProtoMember(4)]
        public ushort? NullableUShortValue { get; set; }

        [ProtoMember(5)]
        public int? NullableIntValue { get; set; }

        [ProtoMember(6)]
        public uint? NullableUIntValue { get; set; }

        [ProtoMember(7)]
        public long? NullableLongValue { get; set; }

        [ProtoMember(8)]
        public ulong? NullableULongValue { get; set; }

        [ProtoMember(9)]
        public float? NullableFloatValue { get; set; }

        [ProtoMember(10)]
        public double? NullableDoubleValue { get; set; }

        [ProtoMember(11)]
        public bool? NullableBoolValue { get; set; }

        // Test with ZigZag encoding
        [ProtoMember(12, DataFormat = DataFormat.ZigZag)]
        public int? NullableZigZagIntValue { get; set; }

        [ProtoMember(13, DataFormat = DataFormat.ZigZag)]
        public long? NullableZigZagLongValue { get; set; }

        // Test with FixedSize encoding
        [ProtoMember(14, DataFormat = DataFormat.FixedSize)]
        public int? NullableFixedSizeIntValue { get; set; }
    }
}