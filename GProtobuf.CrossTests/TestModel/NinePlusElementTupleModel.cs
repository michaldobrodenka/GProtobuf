using ProtoBuf;
using System;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for Tuples with 9+ elements.
    ///
    /// C# Tuple structure for 9+ elements:
    /// Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>
    /// where TRest itself is a Tuple.
    ///
    /// For 9 elements: Tuple<int,int,int,int,int,int,int, Tuple<int, int>>
    /// - Item1-Item7: direct access
    /// - Rest: Tuple<int, int> containing Item8 and Item9
    ///   - Access via: tuple.Rest.Item1 (for Item8)
    ///   - Access via: tuple.Rest.Item2 (for Item9)
    /// </summary>
    [ProtoContract]
    public class NinePlusElementTupleModel
    {
        // 9 elements: Tuple<T1..T7, Tuple<T8, T9>>
        [ProtoMember(1)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int>> NineElements { get; set; }

        // 10 elements: Tuple<T1..T7, Tuple<T8, T9, T10>>
        [ProtoMember(2)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>> TenElements { get; set; }

        // 14 elements: Tuple<T1..T7, Tuple<T8..T14>>
        [ProtoMember(3)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int>> FourteenElements { get; set; }

        // 15 elements: Tuple<T1..T7, Tuple<T8..T14, Tuple<T15>>>
        // This tests doubly-nested Rest!
        [ProtoMember(4)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int, Tuple<int>>> FifteenElements { get; set; }
    }
}
