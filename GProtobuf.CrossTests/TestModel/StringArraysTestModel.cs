using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class StringArraysTestModel
    {
        [ProtoMember(1)]
        public string[] BasicStringArray { get; set; }

        [ProtoMember(2)]
        public string[] EmptyStringArray { get; set; }

        [ProtoMember(3)]
        public string[] NullStringArray { get; set; }

        [ProtoMember(4)]
        public string[] StringArrayWithNulls { get; set; }

        [ProtoMember(5)]
        public string[] LongStringArray { get; set; }

        [ProtoMember(6)]
        public string[] SpecialCharStringArray { get; set; }

        [ProtoMember(7)]
        public string[] UnicodeStringArray { get; set; }

        [ProtoMember(8)]
        public string[] EmptyStringsArray { get; set; }
    }
}