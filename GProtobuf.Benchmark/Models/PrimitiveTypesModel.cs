using ProtoBuf;

namespace GProtobuf.Benchmark.Models
{
    [ProtoContract]
    public class PrimitiveTypesModel
    {
        [ProtoMember(1)]
        public int IntValue { get; set; }

        [ProtoMember(2)]
        public long LongValue { get; set; }

        [ProtoMember(3)]
        public float FloatValue { get; set; }

        [ProtoMember(4)]
        public double DoubleValue { get; set; }

        [ProtoMember(5)]
        public bool BoolValue { get; set; }

        [ProtoMember(6)]
        public string StringValue { get; set; }

        [ProtoMember(7)]
        public byte[] ByteArrayValue { get; set; }

        [ProtoMember(8, DataFormat = DataFormat.FixedSize)]
        public int FixedIntValue { get; set; }

        [ProtoMember(9, DataFormat = DataFormat.FixedSize)]
        public long FixedLongValue { get; set; }

        [ProtoMember(10, DataFormat = DataFormat.ZigZag)]
        public int ZigZagIntValue { get; set; }

        [ProtoMember(11, DataFormat = DataFormat.ZigZag)]
        public long ZigZagLongValue { get; set; }
    }
}