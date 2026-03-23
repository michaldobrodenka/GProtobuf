using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class SimpleTypesZigZag
    {
        [ProtoMember(1, DataFormat = DataFormat.ZigZag)]
        public long LongValue { get; set; }
    }
}