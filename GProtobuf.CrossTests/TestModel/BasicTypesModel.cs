using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class BasicTypesModel
    {
        [ProtoMember(1)]
        public byte ByteValue { get; set; }

        [ProtoMember(2)]
        public sbyte SByteValue { get; set; }

        [ProtoMember(3)]
        public short ShortValue { get; set; }

        [ProtoMember(4)]
        public ushort UShortValue { get; set; }

        [ProtoMember(5)]
        public int IntValue { get; set; }

        [ProtoMember(6)]
        public uint UIntValue { get; set; }

        [ProtoMember(7)]
        public long LongValue { get; set; }

        [ProtoMember(8)]
        public ulong ULongValue { get; set; }

        [ProtoMember(9)]
        public float FloatValue { get; set; }

        [ProtoMember(10)]
        public double DoubleValue { get; set; }

        [ProtoMember(11)]
        public bool BoolValue { get; set; }

        [ProtoMember(12)]
        public string StringValue { get; set; }

        [ProtoMember(13)]
        public byte[] BytesValue { get; set; }
    }
}