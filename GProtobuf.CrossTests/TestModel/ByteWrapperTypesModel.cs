using System;
using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class ByteWrapperTypesModel
    {
        [ProtoMember(1)]
        public ArraySegment<byte> SegmentField { get; set; }

        [ProtoMember(2)]
        public Memory<byte> MemoryField { get; set; }

        [ProtoMember(3)]
        public ReadOnlyMemory<byte> ReadOnlyMemoryField { get; set; }

        // Coexistence with byte[]
        [ProtoMember(4)]
        public byte[] ByteArrayField { get; set; }
    }
}
