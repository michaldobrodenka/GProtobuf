using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class TupleModel
    {
        [ProtoMember(1)]
        public Tuple<int, string> IntStringTuple { get; set; }

        [ProtoMember(2)]
        public Tuple<double, double> DoubleTuple { get; set; }

        [ProtoMember(3)]
        public Tuple<string, int> StringIntTuple { get; set; }

        [ProtoMember(4)]
        public Tuple<long, bool> LongBoolTuple { get; set; }

        [ProtoMember(5)]
        public Tuple<float, float> FloatTuple { get; set; }

        [ProtoMember(6)]
        public List<Tuple<int, string>> TupleList { get; set; }

        [ProtoMember(7)]
        public Tuple<TestEnum, string> EnumStringTuple { get; set; }

        [ProtoMember(8)]
        public Tuple<char, int> CharIntTuple { get; set; }
    }
}