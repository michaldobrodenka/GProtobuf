using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class DictionaryTupleModel
    {
        [ProtoMember(1)]
        public Tuple<double, double> DoubleTuple { get; set; }

        [ProtoMember(2)]
        public Dictionary<int, Tuple<double, double>> ThermostatOutputMap { get; set; }

        [ProtoMember(3)]
        public Dictionary<string, Tuple<int, string>> NameValueMap { get; set; }

        [ProtoMember(4)]
        public Dictionary<Tuple<int, string>, double> TupleKeyMap { get; set; }
    }
}