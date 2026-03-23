using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class NestedTupleModel
    {
        // Simple nested Tuple: Tuple<int, Tuple<string, bool>>
        [ProtoMember(1)]
        public Tuple<int, Tuple<string, bool>> SimpleNested { get; set; }

        // Deeply nested Tuple: Tuple<int, Tuple<int, Tuple<int, int>>>
        [ProtoMember(2)]
        public Tuple<int, Tuple<int, Tuple<int, int>>> DeeplyNested { get; set; }

        // Collection with nested Tuple: List<Tuple<int, Tuple<string, bool>>>
        [ProtoMember(3)]
        public List<Tuple<int, Tuple<string, bool>>> NestedTupleList { get; set; }

        // Dictionary with nested Tuple as value: Dictionary<int, Tuple<string, Tuple<int, bool>>>
        [ProtoMember(4)]
        public Dictionary<int, Tuple<string, Tuple<int, bool>>> DictWithNestedTuple { get; set; }

        // 8-element Tuple with nested Tuple in 8th position (real-world use case)
        [ProtoMember(5)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int>> EightElementTuple { get; set; }
    }
}
