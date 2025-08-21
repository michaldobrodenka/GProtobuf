using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class CollectionTypesTestModel
    {
        [ProtoMember(1)]
        public List<SimpleMessage> MessageList { get; set; }

        [ProtoMember(2)]
        public ICollection<SimpleMessage> MessageICollection { get; set; }

        [ProtoMember(3)]
        public IList<SimpleMessage> MessageIList { get; set; }

        [ProtoMember(4)]
        public IEnumerable<SimpleMessage> MessageIEnumerable { get; set; }

        [ProtoMember(5)]
        public List<string> StringList { get; set; }

        [ProtoMember(6)]
        public ICollection<NestedMessage> NestedICollection { get; set; }

        [ProtoMember(7)]
        public List<NestedMessage> NestedMessageList { get; set; }
    }
}