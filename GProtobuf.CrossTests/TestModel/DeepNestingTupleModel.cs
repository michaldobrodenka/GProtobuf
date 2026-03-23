using ProtoBuf;
using System;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for validating tuple nesting depth limits.
    ///
    /// WARNING: This file contains intentionally deep nesting that may exceed limits.
    /// Some fields here are designed to FAIL compilation to test the nesting limit protection.
    /// </summary>
    [ProtoContract]
    public class DeepNestingTupleModel
    {
        // Depth 1: Should work fine
        [ProtoMember(1)]
        public Tuple<int, Tuple<int, int>> Depth1 { get; set; }

        // Depth 2: Should work fine
        [ProtoMember(2)]
        public Tuple<int, Tuple<int, Tuple<int, int>>> Depth2 { get; set; }

        // Depth 3: Should work fine
        [ProtoMember(3)]
        public Tuple<int, Tuple<int, Tuple<int, Tuple<int, int>>>> Depth3 { get; set; }

        // Depth 5: Should work (within limit)
        [ProtoMember(4)]
        public Tuple<int, Tuple<int, Tuple<int, Tuple<int, Tuple<int, Tuple<int, int>>>>>> Depth5 { get; set; }

        // Depth 10: Should work (at limit MAX_TUPLE_NESTING_DEPTH = 10)
        [ProtoMember(5)]
        public Tuple<int,
            Tuple<int,
                Tuple<int,
                    Tuple<int,
                        Tuple<int,
                            Tuple<int,
                                Tuple<int,
                                    Tuple<int,
                                        Tuple<int,
                                            Tuple<int, int>
                                        >
                                    >
                                >
                            >
                        >
                    >
                >
            >
        > Depth10 { get; set; }

        // TODO: Uncomment to test that depth 11 is rejected at compile-time
        // This should cause InvalidOperationException during source generation
        /*
        [ProtoMember(6)]
        public Tuple<int,
            Tuple<int,
                Tuple<int,
                    Tuple<int,
                        Tuple<int,
                            Tuple<int,
                                Tuple<int,
                                    Tuple<int,
                                        Tuple<int,
                                            Tuple<int,
                                                Tuple<int, int>
                                            >
                                        >
                                    >
                                >
                            >
                        >
                    >
                >
            >
        > Depth11_ShouldFail { get; set; }
        */
    }
}
