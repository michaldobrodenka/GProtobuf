using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class TupleMapTestModel
    {
        // Simple double Tuple
        [ProtoMember(1)]
        public Tuple<double, double> DoubleTuple { get; set; }

        // Dictionary with Tuple as value
        [ProtoMember(2)]
        public Dictionary<int, Tuple<double, double>> ThermostatOutputMap { get; set; }

        // Dictionary with string key and Tuple value
        [ProtoMember(3)]
        public Dictionary<string, Tuple<int, string>> NameValueMap { get; set; }

        // Dictionary with Tuple as key (interesting case!)
        [ProtoMember(4)]
        public Dictionary<Tuple<int, string>, double> TupleKeyMap { get; set; }
    }
}
