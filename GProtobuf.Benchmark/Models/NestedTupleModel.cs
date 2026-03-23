using ProtoBuf;
using System;

namespace GProtobuf.Benchmark.Models
{
    /// <summary>
    /// Benchmark model for nested tuple performance testing.
    /// Compares nested tuples vs flat structures.
    /// </summary>
    [ProtoContract]
    public class NestedTupleBenchmarkModel
    {
        // Simple 2-element tuple (baseline)
        [ProtoMember(1)]
        public Tuple<int, string> SimpleTuple { get; set; }

        // Nested 2-level tuple
        [ProtoMember(2)]
        public Tuple<int, Tuple<string, bool>> NestedLevel2 { get; set; }

        // Nested 3-level tuple
        [ProtoMember(3)]
        public Tuple<int, Tuple<int, Tuple<string, bool>>> NestedLevel3 { get; set; }

        // Nested 5-level tuple (stress test)
        [ProtoMember(4)]
        public Tuple<int, Tuple<int, Tuple<int, Tuple<int, Tuple<int, int>>>>> NestedLevel5 { get; set; }

        // 8-element tuple with Rest
        [ProtoMember(5)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int>> EightElement { get; set; }
    }

    /// <summary>
    /// Flat model for comparison (no nesting).
    /// </summary>
    [ProtoContract]
    public class FlatBenchmarkModel
    {
        [ProtoMember(1)]
        public int Field1 { get; set; }

        [ProtoMember(2)]
        public string Field2 { get; set; }

        [ProtoMember(3)]
        public int Field3 { get; set; }

        [ProtoMember(4)]
        public int Field4 { get; set; }

        [ProtoMember(5)]
        public string Field5 { get; set; }

        [ProtoMember(6)]
        public bool Field6 { get; set; }

        [ProtoMember(7)]
        public int Field7 { get; set; }

        [ProtoMember(8)]
        public int Field8 { get; set; }

        [ProtoMember(9)]
        public int Field9 { get; set; }

        [ProtoMember(10)]
        public int Field10 { get; set; }
    }
}
