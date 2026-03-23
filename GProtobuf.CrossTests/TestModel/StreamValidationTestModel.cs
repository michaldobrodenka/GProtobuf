using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    #region Duplicate Message Field Merge Tests

    /// <summary>
    /// Model for testing duplicate nested message field handling.
    /// Per protobuf spec, duplicate message fields should MERGE contents.
    /// </summary>
    [ProtoContract]
    public class DuplicateFieldMergeModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public NestedMergeMessage Nested { get; set; }

        [ProtoMember(3)]
        public string Name { get; set; }
    }

    [ProtoContract]
    public class NestedMergeMessage
    {
        [ProtoMember(1)]
        public int Field1 { get; set; }

        [ProtoMember(2)]
        public string Field2 { get; set; }

        [ProtoMember(3)]
        public double Field3 { get; set; }

        [ProtoMember(4)]
        public List<int> RepeatedField { get; set; }
    }

    /// <summary>
    /// Model with deeply nested structure for merge testing.
    /// </summary>
    [ProtoContract]
    public class DeepNestedMergeModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public Level1Merge Level1 { get; set; }
    }

    [ProtoContract]
    public class Level1Merge
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public Level2Merge Level2 { get; set; }
    }

    [ProtoContract]
    public class Level2Merge
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public string Description { get; set; }
    }

    #endregion

    #region Packed/Non-Packed Compatibility Tests

    /// <summary>
    /// Model containing ALL packable primitive types for compatibility testing.
    /// Packable types: int32, int64, uint32, uint64, sint32, sint64, bool, enum, fixed32, fixed64, sfixed32, sfixed64, float, double
    /// </summary>
    [ProtoContract]
    public class AllPackableTypesModel
    {
        [ProtoMember(1, IsPacked = true)]
        public List<int> PackedInt32 { get; set; }

        [ProtoMember(2, IsPacked = true)]
        public List<long> PackedInt64 { get; set; }

        [ProtoMember(3, IsPacked = true)]
        public List<uint> PackedUInt32 { get; set; }

        [ProtoMember(4, IsPacked = true)]
        public List<ulong> PackedUInt64 { get; set; }

        [ProtoMember(5, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public List<int> PackedSInt32 { get; set; }

        [ProtoMember(6, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public List<long> PackedSInt64 { get; set; }

        [ProtoMember(7, IsPacked = true)]
        public List<bool> PackedBool { get; set; }

        [ProtoMember(8, IsPacked = true)]
        public List<PackableEnum> PackedEnum { get; set; }

        [ProtoMember(9, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public List<int> PackedFixed32 { get; set; }

        [ProtoMember(10, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public List<long> PackedFixed64 { get; set; }

        [ProtoMember(11, IsPacked = true)]
        public List<float> PackedFloat { get; set; }

        [ProtoMember(12, IsPacked = true)]
        public List<double> PackedDouble { get; set; }
    }

    /// <summary>
    /// Same model but explicitly non-packed for testing compatibility.
    /// </summary>
    [ProtoContract]
    public class AllNonPackedTypesModel
    {
        [ProtoMember(1, IsPacked = false)]
        public List<int> NonPackedInt32 { get; set; }

        [ProtoMember(2, IsPacked = false)]
        public List<long> NonPackedInt64 { get; set; }

        [ProtoMember(3, IsPacked = false)]
        public List<uint> NonPackedUInt32 { get; set; }

        [ProtoMember(4, IsPacked = false)]
        public List<ulong> NonPackedUInt64 { get; set; }

        [ProtoMember(5, IsPacked = false, DataFormat = DataFormat.ZigZag)]
        public List<int> NonPackedSInt32 { get; set; }

        [ProtoMember(6, IsPacked = false, DataFormat = DataFormat.ZigZag)]
        public List<long> NonPackedSInt64 { get; set; }

        [ProtoMember(7, IsPacked = false)]
        public List<bool> NonPackedBool { get; set; }

        [ProtoMember(8, IsPacked = false)]
        public List<PackableEnum> NonPackedEnum { get; set; }

        [ProtoMember(9, IsPacked = false, DataFormat = DataFormat.FixedSize)]
        public List<int> NonPackedFixed32 { get; set; }

        [ProtoMember(10, IsPacked = false, DataFormat = DataFormat.FixedSize)]
        public List<long> NonPackedFixed64 { get; set; }

        [ProtoMember(11, IsPacked = false)]
        public List<float> NonPackedFloat { get; set; }

        [ProtoMember(12, IsPacked = false)]
        public List<double> NonPackedDouble { get; set; }
    }

    public enum PackableEnum
    {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3,
        Negative = -1
    }

    /// <summary>
    /// Model where GProtobuf expects packed but we'll send non-packed to test compatibility.
    /// </summary>
    [ProtoContract]
    public class MixedPackingModel
    {
        [ProtoMember(1)]
        public List<int> IntValues { get; set; }

        [ProtoMember(2)]
        public List<double> DoubleValues { get; set; }

        [ProtoMember(3)]
        public List<bool> BoolValues { get; set; }
    }

    #endregion

    #region Map with Collection Values Tests

    /// <summary>
    /// Model for testing maps with collection values (HashSet, List).
    /// Tests pre-initialization pattern.
    /// </summary>
    [ProtoContract]
    public class MapWithCollectionValuesModel
    {
        [ProtoMember(1)]
        public Dictionary<string, HashSet<int>> StringToIntSet { get; set; }

        [ProtoMember(2)]
        public Dictionary<int, List<string>> IntToStringList { get; set; }

        [ProtoMember(3)]
        public Dictionary<string, HashSet<string>> StringToStringSet { get; set; }
    }

    /// <summary>
    /// Model for testing maps with empty collection values.
    /// </summary>
    [ProtoContract]
    public class MapWithEmptyCollectionsModel
    {
        [ProtoMember(1)]
        public Dictionary<string, HashSet<int>> MapWithEmptySet { get; set; }

        [ProtoMember(2)]
        public Dictionary<string, List<int>> MapWithEmptyList { get; set; }
    }

    /// <summary>
    /// Model for testing maps with single-element collection values.
    /// </summary>
    [ProtoContract]
    public class MapWithSingleElementCollectionsModel
    {
        [ProtoMember(1)]
        public Dictionary<string, HashSet<int>> MapWithSingleSet { get; set; }

        [ProtoMember(2)]
        public Dictionary<string, List<int>> MapWithSingleList { get; set; }
    }

    /// <summary>
    /// Model for testing nested maps with collection values.
    /// </summary>
    [ProtoContract]
    public class NestedMapWithCollectionsModel
    {
        [ProtoMember(1)]
        public Dictionary<string, Dictionary<int, List<string>>> NestedMapWithList { get; set; }
    }

    /// <summary>
    /// Complex model combining multiple maps with various collection values.
    /// </summary>
    [ProtoContract]
    public class ComplexMapCollectionsModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        [ProtoMember(3)]
        public Dictionary<string, HashSet<int>> TagsMap { get; set; }

        [ProtoMember(4)]
        public Dictionary<int, List<double>> MetricsMap { get; set; }

        [ProtoMember(5)]
        public Dictionary<Guid, HashSet<string>> GuidToStringsMap { get; set; }
    }

    #endregion

    #region Duplicate Repeated Field Append Tests

    /// <summary>
    /// Model for testing duplicate repeated field handling.
    /// Per spec, duplicate repeated fields should APPEND, not replace.
    /// </summary>
    [ProtoContract]
    public class DuplicateRepeatedFieldModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public List<int> Values { get; set; }

        [ProtoMember(3)]
        public List<string> Names { get; set; }
    }

    #endregion
}
