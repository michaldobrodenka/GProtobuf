using ProtoBuf;
using System;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class GuidTypesModel
    {
        [ProtoMember(1)]
        public Guid GuidValue { get; set; }

        [ProtoMember(2)]
        public Guid EmptyGuidValue { get; set; }

        [ProtoMember(3)]
        public Guid AnotherGuidValue { get; set; }
    }
}