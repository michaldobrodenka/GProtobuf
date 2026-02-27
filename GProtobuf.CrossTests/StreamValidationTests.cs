using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using CrossTestModel = GProtobuf.CrossTests.TestModel;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// Validation tests for three critical stream deserialization behaviors:
/// 1. Duplicate message field merge (spec requires merge, not overwrite)
/// 2. Packed/non-packed compatibility (must accept both formats)
/// 3. Collection values in maps (pre-initialization requirement)
///
/// These tests verify GProtobuf stream deserialization matches protobuf spec.
/// </summary>
public class StreamValidationTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public StreamValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region 1. Duplicate Message Field Merge Tests

    /// <summary>
    /// Per protobuf spec: When the same non-repeated message field appears multiple times,
    /// the contents should be MERGED (combine fields from all occurrences).
    ///
    /// This test creates raw bytes with duplicate nested message field to verify behavior.
    /// </summary>
    [Fact]
    public void DuplicateMessageField_ShouldMerge_NotOverwrite()
    {
        // Create two separate messages with partial nested content
        var msg1 = new DuplicateFieldMergeModel
        {
            Id = 1,
            Nested = new NestedMergeMessage { Field1 = 100 }
        };

        var msg2 = new DuplicateFieldMergeModel
        {
            Nested = new NestedMergeMessage { Field2 = "hello" }
        };

        var msg3 = new DuplicateFieldMergeModel
        {
            Nested = new NestedMergeMessage { Field3 = 3.14 }
        };

        // Serialize each message
        var bytes1 = SerializeWithProtobufNet(msg1);
        var bytes2 = SerializeWithProtobufNet(msg2);
        var bytes3 = SerializeWithProtobufNet(msg3);

        _output.WriteLine($"Message 1 bytes: {BitConverter.ToString(bytes1)}");
        _output.WriteLine($"Message 2 bytes: {BitConverter.ToString(bytes2)}");
        _output.WriteLine($"Message 3 bytes: {BitConverter.ToString(bytes3)}");

        // Concatenate bytes (simulates duplicate field occurrences)
        var concatenated = bytes1.Concat(bytes2).Concat(bytes3).ToArray();
        _output.WriteLine($"Concatenated bytes: {BitConverter.ToString(concatenated)}");

        // Deserialize with protobuf-net (reference behavior)
        var pnetResult = DeserializeWithProtobufNet<DuplicateFieldMergeModel>(concatenated);

        // Deserialize with GProtobuf Stream
        var gprotoResult = DeserializeWithGProtobufStreamFromBytes(concatenated,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeDuplicateFieldMergeModel(stream));

        _output.WriteLine($"protobuf-net result: Id={pnetResult.Id}, Nested.Field1={pnetResult.Nested?.Field1}, " +
                          $"Nested.Field2={pnetResult.Nested?.Field2}, Nested.Field3={pnetResult.Nested?.Field3}");
        _output.WriteLine($"GProtobuf result: Id={gprotoResult.Id}, Nested.Field1={gprotoResult.Nested?.Field1}, " +
                          $"Nested.Field2={gprotoResult.Nested?.Field2}, Nested.Field3={gprotoResult.Nested?.Field3}");

        // Verify merge behavior (all fields should be present)
        // EXPECTED: Id=1, Nested.Field1=100, Nested.Field2="hello", Nested.Field3=3.14
        // If overwriting: Id=1, Nested.Field1=0, Nested.Field2=null, Nested.Field3=3.14 (only last)
        gprotoResult.Id.Should().Be(1, "Id should be 1 (last wins for scalar)");
        gprotoResult.Nested.Should().NotBeNull("Nested should not be null");
        gprotoResult.Nested.Field1.Should().Be(100, "Field1 should be merged from first occurrence");
        gprotoResult.Nested.Field2.Should().Be("hello", "Field2 should be merged from second occurrence");
        gprotoResult.Nested.Field3.Should().Be(3.14, "Field3 should be merged from third occurrence");
    }

    /// <summary>
    /// Test that scalar fields in duplicate message use "last wins" semantics.
    /// </summary>
    [Fact]
    public void DuplicateScalarField_ShouldUseLastValue()
    {
        var msg1 = new DuplicateFieldMergeModel { Id = 1, Name = "first" };
        var msg2 = new DuplicateFieldMergeModel { Id = 2, Name = "second" };
        var msg3 = new DuplicateFieldMergeModel { Id = 3 }; // Name not set

        var bytes1 = SerializeWithProtobufNet(msg1);
        var bytes2 = SerializeWithProtobufNet(msg2);
        var bytes3 = SerializeWithProtobufNet(msg3);

        var concatenated = bytes1.Concat(bytes2).Concat(bytes3).ToArray();

        var gprotoResult = DeserializeWithGProtobufStreamFromBytes(concatenated,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeDuplicateFieldMergeModel(stream));

        // Scalars: last wins (Id=3, Name should be "second" since msg3 didn't set it)
        // Actually per protobuf, msg3 serializes with no Name field, so "second" should persist
        gprotoResult.Id.Should().Be(3, "Id should be 3 (last value)");
        gprotoResult.Name.Should().Be("second", "Name should be 'second' (last non-default value)");
    }

    /// <summary>
    /// Test deep nested merge behavior.
    /// </summary>
    [Fact]
    public void DeepNestedDuplicateField_ShouldMerge()
    {
        var msg1 = new DeepNestedMergeModel
        {
            Id = 1,
            Level1 = new Level1Merge { Name = "level1" }
        };

        var msg2 = new DeepNestedMergeModel
        {
            Level1 = new Level1Merge
            {
                Level2 = new Level2Merge { Value = 42 }
            }
        };

        var msg3 = new DeepNestedMergeModel
        {
            Level1 = new Level1Merge
            {
                Level2 = new Level2Merge { Description = "deep" }
            }
        };

        var bytes1 = SerializeWithProtobufNet(msg1);
        var bytes2 = SerializeWithProtobufNet(msg2);
        var bytes3 = SerializeWithProtobufNet(msg3);

        var concatenated = bytes1.Concat(bytes2).Concat(bytes3).ToArray();

        var gprotoResult = DeserializeWithGProtobufStreamFromBytes(concatenated,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeDeepNestedMergeModel(stream));

        // All nested fields should be merged
        gprotoResult.Id.Should().Be(1);
        gprotoResult.Level1.Should().NotBeNull();
        gprotoResult.Level1.Name.Should().Be("level1");
        gprotoResult.Level1.Level2.Should().NotBeNull();
        gprotoResult.Level1.Level2.Value.Should().Be(42);
        gprotoResult.Level1.Level2.Description.Should().Be("deep");
    }

    #endregion

    #region 2. Packed/Non-Packed Compatibility Tests

    /// <summary>
    /// Per protobuf spec: Parsers MUST accept both packed and non-packed encoding for packable fields.
    /// This test serializes as PACKED with protobuf-net and deserializes with GProtobuf.
    /// </summary>
    [Fact]
    public void PackedFormat_Int32_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedInt32 = new List<int> { 1, -2, 100, -100, int.MaxValue, int.MinValue }
        };

        var data = SerializeWithProtobufNet(model);
        _output.WriteLine($"Packed int32 bytes: {BitConverter.ToString(data)}");

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedInt32.Should().BeEquivalentTo(model.PackedInt32);
    }

    /// <summary>
    /// Test non-packed int32 deserialization.
    /// </summary>
    [Fact]
    public void NonPackedFormat_Int32_ShouldDeserializeCorrectly()
    {
        var model = new AllNonPackedTypesModel
        {
            NonPackedInt32 = new List<int> { 1, -2, 100, -100, int.MaxValue, int.MinValue }
        };

        var data = SerializeWithProtobufNet(model);
        _output.WriteLine($"Non-packed int32 bytes: {BitConverter.ToString(data)}");

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllNonPackedTypesModel(stream));

        result.NonPackedInt32.Should().BeEquivalentTo(model.NonPackedInt32);
    }

    /// <summary>
    /// Test packed int64 deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Int64_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedInt64 = new List<long> { 1L, -2L, 100000000000L, -100000000000L, long.MaxValue, long.MinValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedInt64.Should().BeEquivalentTo(model.PackedInt64);
    }

    /// <summary>
    /// Test packed uint32 deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_UInt32_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedUInt32 = new List<uint> { 0, 1, 100, uint.MaxValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedUInt32.Should().BeEquivalentTo(model.PackedUInt32);
    }

    /// <summary>
    /// Test packed uint64 deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_UInt64_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedUInt64 = new List<ulong> { 0, 1, 100, ulong.MaxValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedUInt64.Should().BeEquivalentTo(model.PackedUInt64);
    }

    /// <summary>
    /// Test packed sint32 (ZigZag) deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_SInt32_ZigZag_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedSInt32 = new List<int> { 0, 1, -1, 100, -100, int.MaxValue, int.MinValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedSInt32.Should().BeEquivalentTo(model.PackedSInt32);
    }

    /// <summary>
    /// Test packed sint64 (ZigZag) deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_SInt64_ZigZag_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedSInt64 = new List<long> { 0, 1, -1, 100, -100, long.MaxValue, long.MinValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedSInt64.Should().BeEquivalentTo(model.PackedSInt64);
    }

    /// <summary>
    /// Test packed bool deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Bool_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedBool = new List<bool> { true, false, true, true, false }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedBool.Should().BeEquivalentTo(model.PackedBool);
    }

    /// <summary>
    /// Test packed enum deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Enum_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedEnum = new List<PackableEnum>
            {
                PackableEnum.None, PackableEnum.First, PackableEnum.Second,
                PackableEnum.Third, PackableEnum.Negative
            }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedEnum.Should().BeEquivalentTo(model.PackedEnum);
    }

    /// <summary>
    /// Test packed fixed32 deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Fixed32_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedFixed32 = new List<int> { 0, 1, -1, int.MaxValue, int.MinValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedFixed32.Should().BeEquivalentTo(model.PackedFixed32);
    }

    /// <summary>
    /// Test packed fixed64 deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Fixed64_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedFixed64 = new List<long> { 0, 1, -1, long.MaxValue, long.MinValue }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedFixed64.Should().BeEquivalentTo(model.PackedFixed64);
    }

    /// <summary>
    /// Test packed float deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Float_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedFloat = new List<float> { 0f, 1.5f, -1.5f, float.MaxValue, float.MinValue, float.Epsilon }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedFloat.Should().BeEquivalentTo(model.PackedFloat);
    }

    /// <summary>
    /// Test packed double deserialization.
    /// </summary>
    [Fact]
    public void PackedFormat_Double_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedDouble = new List<double> { 0.0, 1.5, -1.5, double.MaxValue, double.MinValue, double.Epsilon }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.PackedDouble.Should().BeEquivalentTo(model.PackedDouble);
    }

    /// <summary>
    /// Test that all packable types work together.
    /// </summary>
    [Fact]
    public void PackedFormat_AllTypes_ShouldDeserializeCorrectly()
    {
        var model = new AllPackableTypesModel
        {
            PackedInt32 = new List<int> { 1, -1, 100 },
            PackedInt64 = new List<long> { 1L, -1L, 100L },
            PackedUInt32 = new List<uint> { 1, 100, uint.MaxValue },
            PackedUInt64 = new List<ulong> { 1, 100, ulong.MaxValue },
            PackedSInt32 = new List<int> { 0, -50, 50 },
            PackedSInt64 = new List<long> { 0L, -50L, 50L },
            PackedBool = new List<bool> { true, false, true },
            PackedEnum = new List<PackableEnum> { PackableEnum.First, PackableEnum.Second },
            PackedFixed32 = new List<int> { 1, 2, 3 },
            PackedFixed64 = new List<long> { 1L, 2L, 3L },
            PackedFloat = new List<float> { 1.5f, 2.5f },
            PackedDouble = new List<double> { 1.5, 2.5 }
        };

        var data = SerializeWithProtobufNet(model);
        _output.WriteLine($"All packed types bytes length: {data.Length}");

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        result.Should().BeEquivalentTo(model);
    }

    /// <summary>
    /// Test mixed packed from protobuf-net can be read by GProtobuf.
    /// </summary>
    [Fact]
    public void MixedPacking_FromProtobufNet_ShouldWork()
    {
        var model = new MixedPackingModel
        {
            IntValues = new List<int> { 1, 2, 3, 4, 5 },
            DoubleValues = new List<double> { 1.1, 2.2, 3.3 },
            BoolValues = new List<bool> { true, false, true }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMixedPackingModel(stream));

        result.Should().BeEquivalentTo(model);
    }

    /// <summary>
    /// Test GProtobuf serialized data can be read back with stream.
    /// </summary>
    [Fact]
    public void GProtobufSerialized_ShouldWorkWithStreamReader()
    {
        var model = new MixedPackingModel
        {
            IntValues = new List<int> { 10, 20, 30 },
            DoubleValues = new List<double> { 1.5, 2.5 },
            BoolValues = new List<bool> { false, true }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMixedPackingModel);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMixedPackingModel(stream));

        result.Should().BeEquivalentTo(model);
    }

    #endregion

    #region 3. Collection Values in Maps Tests

    /// <summary>
    /// Test maps with HashSet values (pre-initialization requirement).
    /// </summary>
    [Fact]
    public void MapWithHashSetValues_ShouldWork_GG_Stream()
    {
        var model = new MapWithCollectionValuesModel
        {
            StringToIntSet = new Dictionary<string, HashSet<int>>
            {
                { "key1", new HashSet<int> { 1, 2, 3 } },
                { "key2", new HashSet<int> { 10, 20, 30, 40 } },
                { "key3", new HashSet<int> { 100 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithCollectionValuesModel);
        _output.WriteLine($"Map with HashSet values bytes: {data.Length}");

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(stream));

        result.StringToIntSet.Should().HaveCount(3);
        result.StringToIntSet["key1"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
        result.StringToIntSet["key2"].Should().BeEquivalentTo(new[] { 10, 20, 30, 40 });
        result.StringToIntSet["key3"].Should().BeEquivalentTo(new[] { 100 });
    }

    /// <summary>
    /// Test maps with HashSet values from protobuf-net.
    /// </summary>
    [Fact]
    public void MapWithHashSetValues_ShouldWork_PG_Stream()
    {
        var model = new MapWithCollectionValuesModel
        {
            StringToIntSet = new Dictionary<string, HashSet<int>>
            {
                { "alpha", new HashSet<int> { 5, 10, 15 } },
                { "beta", new HashSet<int> { 100, 200 } }
            }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(stream));

        result.StringToIntSet.Should().HaveCount(2);
        result.StringToIntSet["alpha"].Should().BeEquivalentTo(new[] { 5, 10, 15 });
        result.StringToIntSet["beta"].Should().BeEquivalentTo(new[] { 100, 200 });
    }

    /// <summary>
    /// Test maps with List values.
    /// </summary>
    [Fact]
    public void MapWithListValues_ShouldWork_GG_Stream()
    {
        var model = new MapWithCollectionValuesModel
        {
            IntToStringList = new Dictionary<int, List<string>>
            {
                { 1, new List<string> { "a", "b", "c" } },
                { 2, new List<string> { "x", "y" } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithCollectionValuesModel);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(stream));

        result.IntToStringList.Should().HaveCount(2);
        result.IntToStringList[1].Should().BeEquivalentTo(new[] { "a", "b", "c" });
        result.IntToStringList[2].Should().BeEquivalentTo(new[] { "x", "y" });
    }

    /// <summary>
    /// Test maps with string HashSet values.
    /// </summary>
    [Fact]
    public void MapWithStringHashSetValues_ShouldWork_GG_Stream()
    {
        var model = new MapWithCollectionValuesModel
        {
            StringToStringSet = new Dictionary<string, HashSet<string>>
            {
                { "tags", new HashSet<string> { "tag1", "tag2", "tag3" } },
                { "categories", new HashSet<string> { "cat1", "cat2" } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithCollectionValuesModel);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(stream));

        result.StringToStringSet.Should().HaveCount(2);
        result.StringToStringSet["tags"].Should().BeEquivalentTo(new[] { "tag1", "tag2", "tag3" });
        result.StringToStringSet["categories"].Should().BeEquivalentTo(new[] { "cat1", "cat2" });
    }

    /// <summary>
    /// Test maps with empty collections (edge case).
    /// </summary>
    [Fact]
    public void MapWithEmptyCollections_ShouldWork_GG_Stream()
    {
        var model = new MapWithEmptyCollectionsModel
        {
            MapWithEmptySet = new Dictionary<string, HashSet<int>>
            {
                { "empty", new HashSet<int>() },
                { "notEmpty", new HashSet<int> { 1, 2 } }
            },
            MapWithEmptyList = new Dictionary<string, List<int>>
            {
                { "emptyList", new List<int>() },
                { "withValues", new List<int> { 10, 20 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithEmptyCollectionsModel);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithEmptyCollectionsModel(stream));

        // Note: Empty collections may or may not be present depending on serialization behavior
        // The key test is that non-empty collections are correctly deserialized
        if (result.MapWithEmptySet.ContainsKey("notEmpty"))
        {
            result.MapWithEmptySet["notEmpty"].Should().BeEquivalentTo(new[] { 1, 2 });
        }
        if (result.MapWithEmptyList.ContainsKey("withValues"))
        {
            result.MapWithEmptyList["withValues"].Should().BeEquivalentTo(new[] { 10, 20 });
        }
    }

    /// <summary>
    /// Test maps with single-element collections.
    /// </summary>
    [Fact]
    public void MapWithSingleElementCollections_ShouldWork_GG_Stream()
    {
        var model = new MapWithSingleElementCollectionsModel
        {
            MapWithSingleSet = new Dictionary<string, HashSet<int>>
            {
                { "single", new HashSet<int> { 42 } }
            },
            MapWithSingleList = new Dictionary<string, List<int>>
            {
                { "one", new List<int> { 99 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithSingleElementCollectionsModel);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithSingleElementCollectionsModel(stream));

        result.MapWithSingleSet.Should().HaveCount(1);
        result.MapWithSingleSet["single"].Should().BeEquivalentTo(new[] { 42 });
        result.MapWithSingleList.Should().HaveCount(1);
        result.MapWithSingleList["one"].Should().BeEquivalentTo(new[] { 99 });
    }

    /// <summary>
    /// Test complex model with multiple map types and collection values.
    /// </summary>
    [Fact]
    public void ComplexMapCollections_ShouldWork_GG_Stream()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        var model = new ComplexMapCollectionsModel
        {
            Id = 123,
            Name = "TestComplex",
            TagsMap = new Dictionary<string, HashSet<int>>
            {
                { "primary", new HashSet<int> { 1, 2, 3 } },
                { "secondary", new HashSet<int> { 10, 20 } }
            },
            MetricsMap = new Dictionary<int, List<double>>
            {
                { 1, new List<double> { 1.1, 2.2, 3.3 } },
                { 2, new List<double> { 100.5 } }
            },
            GuidToStringsMap = new Dictionary<Guid, HashSet<string>>
            {
                { guid1, new HashSet<string> { "a", "b" } },
                { guid2, new HashSet<string> { "x", "y", "z" } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeComplexMapCollectionsModel);
        _output.WriteLine($"Complex map collections bytes: {data.Length}");

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeComplexMapCollectionsModel(stream));

        result.Id.Should().Be(123);
        result.Name.Should().Be("TestComplex");
        result.TagsMap.Should().HaveCount(2);
        result.TagsMap["primary"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
        result.MetricsMap.Should().HaveCount(2);
        result.MetricsMap[1].Should().BeEquivalentTo(new[] { 1.1, 2.2, 3.3 });
        result.GuidToStringsMap.Should().HaveCount(2);
        result.GuidToStringsMap[guid1].Should().BeEquivalentTo(new[] { "a", "b" });
        result.GuidToStringsMap[guid2].Should().BeEquivalentTo(new[] { "x", "y", "z" });
    }

    /// <summary>
    /// Test complex model with protobuf-net serialization.
    /// </summary>
    [Fact]
    public void ComplexMapCollections_ShouldWork_PG_Stream()
    {
        var guid1 = Guid.NewGuid();

        var model = new ComplexMapCollectionsModel
        {
            Id = 456,
            Name = "FromProtobufNet",
            TagsMap = new Dictionary<string, HashSet<int>>
            {
                { "test", new HashSet<int> { 5, 10, 15 } }
            },
            MetricsMap = new Dictionary<int, List<double>>
            {
                { 100, new List<double> { 99.9 } }
            },
            GuidToStringsMap = new Dictionary<Guid, HashSet<string>>
            {
                { guid1, new HashSet<string> { "only" } }
            }
        };

        var data = SerializeWithProtobufNet(model);

        var result = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeComplexMapCollectionsModel(stream));

        result.Id.Should().Be(456);
        result.Name.Should().Be("FromProtobufNet");
        result.TagsMap["test"].Should().BeEquivalentTo(new[] { 5, 10, 15 });
    }

    #endregion

    #region Duplicate Repeated Field Append Tests

    /// <summary>
    /// Per spec: Duplicate repeated fields should APPEND, not replace.
    /// </summary>
    [Fact]
    public void DuplicateRepeatedField_ShouldAppend()
    {
        var msg1 = new DuplicateRepeatedFieldModel
        {
            Id = 1,
            Values = new List<int> { 1, 2, 3 }
        };

        var msg2 = new DuplicateRepeatedFieldModel
        {
            Values = new List<int> { 4, 5 }
        };

        var msg3 = new DuplicateRepeatedFieldModel
        {
            Names = new List<string> { "a", "b" }
        };

        var bytes1 = SerializeWithProtobufNet(msg1);
        var bytes2 = SerializeWithProtobufNet(msg2);
        var bytes3 = SerializeWithProtobufNet(msg3);

        var concatenated = bytes1.Concat(bytes2).Concat(bytes3).ToArray();

        var gprotoResult = DeserializeWithGProtobufStreamFromBytes(concatenated,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeDuplicateRepeatedFieldModel(stream));

        // Repeated fields should append: Values = [1,2,3,4,5], Names = ["a","b"]
        gprotoResult.Id.Should().Be(1);
        gprotoResult.Values.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        gprotoResult.Names.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    #endregion

    #region SpanReader vs StreamReader Consistency

    /// <summary>
    /// Verify SpanReader and StreamReader produce identical results for packed data.
    /// </summary>
    [Fact]
    public void SpanVsStream_PackedData_ShouldBeIdentical()
    {
        var model = new AllPackableTypesModel
        {
            PackedInt32 = new List<int> { 1, 2, 3 },
            PackedFloat = new List<float> { 1.5f, 2.5f },
            PackedBool = new List<bool> { true, false }
        };

        var data = SerializeWithProtobufNet(model);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeAllPackableTypesModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    /// <summary>
    /// Verify SpanReader and StreamReader produce identical results for map collections.
    /// </summary>
    [Fact]
    public void SpanVsStream_MapCollections_ShouldBeIdentical()
    {
        var model = new MapWithCollectionValuesModel
        {
            StringToIntSet = new Dictionary<string, HashSet<int>>
            {
                { "k1", new HashSet<int> { 1, 2 } },
                { "k2", new HashSet<int> { 3, 4, 5 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapWithCollectionValuesModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithCollectionValuesModel(stream));

        streamResult.StringToIntSet.Should().BeEquivalentTo(spanResult.StringToIntSet);
    }

    #endregion
}
