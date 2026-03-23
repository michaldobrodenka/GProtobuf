using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class ExtendedTupleModel
    {
        #region 3-7 Element Tuples

        [ProtoMember(1)]
        public Tuple<int, int, int> ThreeElementTuple { get; set; }

        [ProtoMember(2)]
        public Tuple<int, string, double, bool> FourElementTuple { get; set; }

        [ProtoMember(3)]
        public Tuple<int, int, int, int, int> FiveElementTuple { get; set; }

        [ProtoMember(4)]
        public Tuple<string, int, double, bool, float, long> SixElementTuple { get; set; }

        [ProtoMember(5)]
        public Tuple<int, int, int, int, int, int, int> SevenElementTuple { get; set; }

        #endregion

        #region Collections as Tuple Elements
        // NOTE: Collections inside tuples not yet fully supported by V2 generator
        // Generator creates invalid method names for tuple elements with collections

        // [ProtoMember(10)]
        // public Tuple<List<int>, List<string>> ListTuple { get; set; }

        // [ProtoMember(11)]
        // public Tuple<int[], string[]> ArrayTuple { get; set; }

        // [ProtoMember(12)]
        // public Tuple<Dictionary<int, string>, bool> DictionaryTuple { get; set; }

        // [ProtoMember(13)]
        // public Tuple<HashSet<int>, HashSet<string>> HashSetTuple { get; set; }

        #endregion

        #region Tuples of Tuples (non-nested)
        // NOTE: Tuples of tuples cause duplicate variable declarations in generator
        // Generator bug: creates multiple `var itemLength` and `var itemReader` in same scope

        // [ProtoMember(20)]
        // public Tuple<Tuple<int, int>, Tuple<string, string>> TupleOfTuples { get; set; }

        // [ProtoMember(21)]
        // public Tuple<Tuple<Guid, TimeSpan>, Tuple<int, bool>> ComplexTupleOfTuples { get; set; }

        #endregion

        #region Nested Tuples (8+ elements using Rest)

        [ProtoMember(30)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int>> EightElementNestedTuple { get; set; }

        [ProtoMember(31)]
        public Tuple<int, string, double, bool, float, long, Guid, Tuple<TimeSpan, int>> NineElementNestedTuple { get; set; }

        [ProtoMember(32)]
        public Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>> TenElementNestedTuple { get; set; }

        #endregion

        #region Collections of Tuples

        [ProtoMember(40)]
        public List<Tuple<int, string>> TupleList { get; set; }

        [ProtoMember(41)]
        public List<Tuple<int, int, int>> ThreeElementTupleList { get; set; }

        #endregion

        #region Mixed Complex Scenarios
        // NOTE: Collections inside tuples not yet fully supported by V2 generator

        // [ProtoMember(50)]
        // public Tuple<List<int>, Dictionary<string, int>> CollectionMixTuple { get; set; }

        // [ProtoMember(51)]
        // public Tuple<Tuple<int, string>, List<int>> NestedWithCollectionTuple { get; set; }

        #endregion

        #region Additional 3-element variations

        [ProtoMember(60)]
        public Tuple<Guid, Guid, Guid> ThreeGuidTuple { get; set; }

        [ProtoMember(61)]
        public Tuple<string, string, string> ThreeStringTuple { get; set; }

        // NOTE: DateTime not yet supported in tuples by V2 generator
        // [ProtoMember(62)]
        // public Tuple<TimeSpan, TimeSpan, DateTime> MixedTimeTuple { get; set; }

        [ProtoMember(63)]
        public Tuple<TimeSpan, TimeSpan, TimeSpan> ThreeTimeSpanTuple { get; set; }

        #endregion
    }
}
