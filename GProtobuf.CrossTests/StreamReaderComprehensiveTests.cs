using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using CrossTestModel = GProtobuf.CrossTests.TestModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// Comprehensive StreamReader Tests
///
/// These tests verify the independent StreamReader implementation that reads
/// directly from Stream without delegating to SpanReader.
///
/// Test categories:
/// - ConcurrentDictionary: Tests for thread-safe dictionary types
/// - NestedCollections: Tests for dictionaries with collection values
/// - CustomTypes: Tests for custom dictionary and HashSet types
/// - VirtualTypes: Tests for tuples and collections as nested values
/// - EdgeCases: Tests for boundary conditions and error scenarios
/// </summary>
public class StreamReaderComprehensiveTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public StreamReaderComprehensiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region ConcurrentDictionary Basic Tests

    [Fact]
    public void ConcurrentDictionary_IntString_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = new ConcurrentDictionary<int, string>(
                new Dictionary<int, string>
                {
                    { 1, "one" },
                    { 2, "two" },
                    { 3, "three" }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.IntStringConcurrentMap.Should().BeEquivalentTo(model.IntStringConcurrentMap);
    }

    [Fact]
    public void ConcurrentDictionary_StringInt_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            StringIntConcurrentMap = new ConcurrentDictionary<string, int>(
                new Dictionary<string, int>
                {
                    { "alpha", 100 },
                    { "beta", 200 },
                    { "gamma", 300 }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.StringIntConcurrentMap.Should().BeEquivalentTo(model.StringIntConcurrentMap);
    }

    [Fact]
    public void ConcurrentDictionary_IntDouble_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntDoubleConcurrentMap = new ConcurrentDictionary<int, double>(
                new Dictionary<int, double>
                {
                    { 1, 3.14159 },
                    { 2, 2.71828 },
                    { 3, 1.41421 }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.IntDoubleConcurrentMap.Should().BeEquivalentTo(model.IntDoubleConcurrentMap);
    }

    [Fact]
    public void ConcurrentDictionary_AllTypes_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = new ConcurrentDictionary<int, string>(
                new Dictionary<int, string> { { 1, "one" } }),
            StringIntConcurrentMap = new ConcurrentDictionary<string, int>(
                new Dictionary<string, int> { { "key", 42 } }),
            IntDoubleConcurrentMap = new ConcurrentDictionary<int, double>(
                new Dictionary<int, double> { { 1, 1.5 } }),
            LongStringConcurrentMap = new ConcurrentDictionary<long, string>(
                new Dictionary<long, string> { { 1000L, "thousand" } }),
            StringBoolConcurrentMap = new ConcurrentDictionary<string, bool>(
                new Dictionary<string, bool> { { "flag", true } })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ConcurrentDictionary_SpanVsStream_ShouldBeIdentical()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = new ConcurrentDictionary<int, string>(
                new Dictionary<int, string>
                {
                    { 1, "value1" },
                    { 2, "value2" }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    #endregion

    #region ConcurrentDictionary with Collections

    [Fact]
    public void ConcurrentDictionary_WithHashSetValue_GG_Stream()
    {
        var model = new ConcurrentDictionaryWithCollectionsModel
        {
            IntHashSetConcurrentMap = new ConcurrentDictionary<int, HashSet<int>>(
                new Dictionary<int, HashSet<int>>
                {
                    { 1, new HashSet<int> { 10, 20, 30 } },
                    { 2, new HashSet<int> { 100, 200 } }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryWithCollectionsModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryWithCollectionsModel(stream));

        deserialized.IntHashSetConcurrentMap.Should().HaveCount(2);
        deserialized.IntHashSetConcurrentMap[1].Should().BeEquivalentTo(new[] { 10, 20, 30 });
        deserialized.IntHashSetConcurrentMap[2].Should().BeEquivalentTo(new[] { 100, 200 });
    }

    [Fact]
    public void ConcurrentDictionary_WithListValue_GG_Stream()
    {
        var model = new ConcurrentDictionaryWithCollectionsModel
        {
            StringListConcurrentMap = new ConcurrentDictionary<string, List<string>>(
                new Dictionary<string, List<string>>
                {
                    { "fruits", new List<string> { "apple", "banana", "cherry" } },
                    { "colors", new List<string> { "red", "green", "blue" } }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryWithCollectionsModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryWithCollectionsModel(stream));

        deserialized.StringListConcurrentMap.Should().HaveCount(2);
        deserialized.StringListConcurrentMap["fruits"].Should().BeEquivalentTo(new[] { "apple", "banana", "cherry" });
        deserialized.StringListConcurrentMap["colors"].Should().BeEquivalentTo(new[] { "red", "green", "blue" });
    }

    [Fact]
    public void ConcurrentDictionary_WithArrayValue_GG_Stream()
    {
        var model = new ConcurrentDictionaryWithCollectionsModel
        {
            IntArrayConcurrentMap = new ConcurrentDictionary<int, int[]>(
                new Dictionary<int, int[]>
                {
                    { 1, new[] { 1, 2, 3, 4, 5 } },
                    { 2, new[] { 10, 20, 30 } }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryWithCollectionsModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryWithCollectionsModel(stream));

        deserialized.IntArrayConcurrentMap.Should().HaveCount(2);
        deserialized.IntArrayConcurrentMap[1].Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        deserialized.IntArrayConcurrentMap[2].Should().BeEquivalentTo(new[] { 10, 20, 30 });
    }

    #endregion

    #region Nested ConcurrentDictionary

    [Fact]
    public void NestedConcurrentDictionary_GG_Stream()
    {
        var innerDict = new ConcurrentDictionary<string, int>(
            new Dictionary<string, int> { { "inner", 42 } });
        var model = new NestedConcurrentDictionaryModel
        {
            NestedConcurrentMap = new ConcurrentDictionary<int, ConcurrentDictionary<string, int>>(
                new Dictionary<int, ConcurrentDictionary<string, int>>
                {
                    { 1, innerDict }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeNestedConcurrentDictionaryModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedConcurrentDictionaryModel(stream));

        deserialized.NestedConcurrentMap.Should().HaveCount(1);
        deserialized.NestedConcurrentMap[1]["inner"].Should().Be(42);
    }

    [Fact]
    public void MixedNestedDictionary_GG_Stream()
    {
        var innerConcurrent = new ConcurrentDictionary<int, string>(
            new Dictionary<int, string> { { 1, "value" } });
        var model = new NestedConcurrentDictionaryModel
        {
            MixedNestedMap = new Dictionary<string, ConcurrentDictionary<int, string>>
            {
                { "key", innerConcurrent }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeNestedConcurrentDictionaryModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedConcurrentDictionaryModel(stream));

        deserialized.MixedNestedMap.Should().HaveCount(1);
        deserialized.MixedNestedMap["key"][1].Should().Be("value");
    }

    #endregion

    #region ConcurrentDictionary with Enum Keys

    [Fact]
    public void ConcurrentDictionary_EnumKey_GG_Stream()
    {
        var model = new ConcurrentDictionaryEnumKeyModel
        {
            EnumKeyMap = new ConcurrentDictionary<ConnectionStatus, string>(
                new Dictionary<ConnectionStatus, string>
                {
                    { ConnectionStatus.Connected, "online" },
                    { ConnectionStatus.Disconnected, "offline" }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryEnumKeyModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryEnumKeyModel(stream));

        deserialized.EnumKeyMap.Should().HaveCount(2);
        deserialized.EnumKeyMap[ConnectionStatus.Connected].Should().Be("online");
        deserialized.EnumKeyMap[ConnectionStatus.Disconnected].Should().Be("offline");
    }

    [Fact]
    public void ConcurrentDictionary_EnumKeyWithCollectionValue_GG_Stream()
    {
        var model = new ConcurrentDictionaryEnumKeyModel
        {
            EnumKeyCollectionValueMap = new ConcurrentDictionary<ConnectionStatus, List<ConnectionStatus>>(
                new Dictionary<ConnectionStatus, List<ConnectionStatus>>
                {
                    { ConnectionStatus.Connected, new List<ConnectionStatus> { ConnectionStatus.Connected, ConnectionStatus.Pending } }
                })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryEnumKeyModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryEnumKeyModel(stream));

        deserialized.EnumKeyCollectionValueMap.Should().HaveCount(1);
        deserialized.EnumKeyCollectionValueMap[ConnectionStatus.Connected].Should()
            .BeEquivalentTo(new[] { ConnectionStatus.Connected, ConnectionStatus.Pending });
    }

    #endregion

    #region Mixed Dictionary Types

    [Fact]
    public void MixedDictionaryTypes_GG_Stream()
    {
        var model = new MixedDictionaryTypesModel
        {
            StandardDictionary = new Dictionary<int, string> { { 1, "standard" } },
            ConcurrentDictionary = new ConcurrentDictionary<int, string>(
                new Dictionary<int, string> { { 2, "concurrent" } })
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMixedDictionaryTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMixedDictionaryTypesModel(stream));

        deserialized.StandardDictionary[1].Should().Be("standard");
        deserialized.ConcurrentDictionary[2].Should().Be("concurrent");
    }

    [Fact]
    public void MixedDictionaryTypes_NestedMixed_GG_Stream()
    {
        var innerConcurrent = new ConcurrentDictionary<int, double>(
            new Dictionary<int, double> { { 1, 3.14 }, { 2, 2.71 } });
        var model = new MixedDictionaryTypesModel
        {
            NestedMixed = new Dictionary<string, ConcurrentDictionary<int, double>>
            {
                { "constants", innerConcurrent }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMixedDictionaryTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMixedDictionaryTypesModel(stream));

        deserialized.NestedMixed["constants"][1].Should().BeApproximately(3.14, 0.001);
        deserialized.NestedMixed["constants"][2].Should().BeApproximately(2.71, 0.001);
    }

    #endregion

    #region Map with Collection Values

    [Fact]
    public void MapCollection_StringIntHashset_GG_Stream()
    {
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>>
            {
                { "odds", new HashSet<int> { 1, 3, 5, 7, 9 } },
                { "evens", new HashSet<int> { 2, 4, 6, 8, 10 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.StringIntHashsetMap.Should().HaveCount(2);
        deserialized.StringIntHashsetMap["odds"].Should().BeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
        deserialized.StringIntHashsetMap["evens"].Should().BeEquivalentTo(new[] { 2, 4, 6, 8, 10 });
    }

    [Fact]
    public void MapCollection_IntStringList_GG_Stream()
    {
        var model = new MapCollectionModel
        {
            IntStringListMap = new Dictionary<int, List<string>>
            {
                { 1, new List<string> { "one", "uno", "ein" } },
                { 2, new List<string> { "two", "dos", "zwei" } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.IntStringListMap.Should().HaveCount(2);
        deserialized.IntStringListMap[1].Should().BeEquivalentTo(new[] { "one", "uno", "ein" });
        deserialized.IntStringListMap[2].Should().BeEquivalentTo(new[] { "two", "dos", "zwei" });
    }

    [Fact]
    public void MapCollection_StringIntArray_GG_Stream()
    {
        var model = new MapCollectionModel
        {
            StringIntArrayMap = new Dictionary<string, int[]>
            {
                { "primes", new[] { 2, 3, 5, 7, 11, 13 } },
                { "fibonacci", new[] { 1, 1, 2, 3, 5, 8, 13 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.StringIntArrayMap.Should().HaveCount(2);
        deserialized.StringIntArrayMap["primes"].Should().BeEquivalentTo(new[] { 2, 3, 5, 7, 11, 13 });
        deserialized.StringIntArrayMap["fibonacci"].Should().BeEquivalentTo(new[] { 1, 1, 2, 3, 5, 8, 13 });
    }

    [Fact]
    public void MapCollection_AllTypes_GG_Stream()
    {
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>> { { "a", new HashSet<int> { 1 } } },
            IntStringListMap = new Dictionary<int, List<string>> { { 1, new List<string> { "x" } } },
            StringIntArrayMap = new Dictionary<string, int[]> { { "b", new[] { 2 } } },
            StringStringHashsetMap = new Dictionary<string, HashSet<string>> { { "c", new HashSet<string> { "y" } } },
            IntDoubleArrayMap = new Dictionary<int, double[]> { { 3, new[] { 1.5 } } }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void MapCollection_SpanVsStream_ShouldBeIdentical()
    {
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>>
            {
                { "test", new HashSet<int> { 1, 2, 3 } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    #endregion

    #region Nested Dictionary Tests

    [Fact]
    public void NestedDictionary_StringToIntStringDict_GG_Stream()
    {
        var model = new NestedDictionaryTestModel
        {
            StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
            {
                { "outer1", new Dictionary<int, string> { { 1, "a" }, { 2, "b" } } },
                { "outer2", new Dictionary<int, string> { { 3, "c" }, { 4, "d" } } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeNestedDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedDictionaryTestModel(stream));

        deserialized.StringToIntStringDictMap.Should().HaveCount(2);
        deserialized.StringToIntStringDictMap["outer1"][1].Should().Be("a");
        deserialized.StringToIntStringDictMap["outer2"][3].Should().Be("c");
    }

    [Fact]
    public void NestedDictionary_SpanVsStream_ShouldBeIdentical()
    {
        var model = new NestedDictionaryTestModel
        {
            StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
            {
                { "key", new Dictionary<int, string> { { 100, "value" } } }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeNestedDictionaryTestModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeNestedDictionaryTestModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedDictionaryTestModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    #endregion

    #region Large Data Stress Tests

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void ConcurrentDictionary_LargeSize_GG_Stream(int size)
    {
        var dict = new ConcurrentDictionary<int, string>();
        for (int i = 0; i < size; i++)
        {
            dict[i] = $"value_{i}";
        }

        var model = new ConcurrentDictionaryTestModel { IntStringConcurrentMap = dict };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        _output.WriteLine($"Size {size}: Serialized to {data.Length} bytes");

        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.IntStringConcurrentMap.Should().HaveCount(size);
        deserialized.IntStringConcurrentMap[0].Should().Be("value_0");
        deserialized.IntStringConcurrentMap[size - 1].Should().Be($"value_{size - 1}");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void MapWithCollections_LargeCollectionValues_GG_Stream(int collectionSize)
    {
        var hashSet = new HashSet<int>(Enumerable.Range(0, collectionSize));
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>>
            {
                { "large", hashSet }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        _output.WriteLine($"Collection size {collectionSize}: Serialized to {data.Length} bytes");

        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.StringIntHashsetMap["large"].Should().HaveCount(collectionSize);
    }

    #endregion

    #region Empty Collection Tests

    [Fact]
    public void ConcurrentDictionary_Empty_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = new ConcurrentDictionary<int, string>()
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        // Empty collections serialize to no bytes, so deserialize returns null
        // This is expected protobuf behavior
        (deserialized.IntStringConcurrentMap == null || deserialized.IntStringConcurrentMap.IsEmpty).Should().BeTrue();
    }

    [Fact]
    public void MapCollection_EmptyCollections_GG_Stream()
    {
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>>
            {
                { "empty", new HashSet<int>() }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeMapCollectionModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.StringIntHashsetMap.Should().HaveCount(1);
        deserialized.StringIntHashsetMap["empty"].Should().BeEmpty();
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void ConcurrentDictionary_NullMap_GG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = null
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeConcurrentDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.IntStringConcurrentMap.Should().BeNull();
    }

    #endregion

    #region Protobuf-net Cross-Compatibility Tests

    [Fact]
    public void ConcurrentDictionary_PG_Stream()
    {
        var model = new ConcurrentDictionaryTestModel
        {
            IntStringConcurrentMap = new ConcurrentDictionary<int, string>(
                new Dictionary<int, string>
                {
                    { 1, "from_protobuf_net" },
                    { 2, "cross_compatible" }
                })
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentDictionaryTestModel(stream));

        deserialized.IntStringConcurrentMap.Should().HaveCount(2);
        deserialized.IntStringConcurrentMap[1].Should().Be("from_protobuf_net");
    }

    [Fact]
    public void MapCollection_PG_Stream()
    {
        var model = new MapCollectionModel
        {
            StringIntHashsetMap = new Dictionary<string, HashSet<int>>
            {
                { "cross", new HashSet<int> { 1, 2, 3 } }
            }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeMapCollectionModel(stream));

        deserialized.StringIntHashsetMap["cross"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    #endregion
}
