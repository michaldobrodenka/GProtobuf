using FluentAssertions;
using System.Collections.Generic;
using System.IO;
using Xunit;
using System;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;

namespace GProtobuf.CrossTests
{
    public class MapWithCollectionValuesTests : BaseSerializationTest
    {
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_HashSet_values()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                StringIntHashsetMap = new Dictionary<string, HashSet<int>>
                {
                    ["group1"] = new HashSet<int> { 10, 20, 30, 40 },
                    ["group2"] = new HashSet<int> { 100, 200, 300 },
                    ["empty"] = new HashSet<int>()
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert
            result.StringIntHashsetMap.Should().NotBeNull();
            result.StringIntHashsetMap.Should().HaveCount(3);
            result.StringIntHashsetMap["group1"].Should().BeEquivalentTo(new[] { 10, 20, 30, 40 });
            result.StringIntHashsetMap["group2"].Should().BeEquivalentTo(new[] { 100, 200, 300 });
            result.StringIntHashsetMap["empty"].Should().BeEmpty();
        }

        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_List_values()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                IntStringListMap = new Dictionary<int, List<string>>
                {
                    [1] = new List<string> { "first", "second", "third" },
                    [2] = new List<string> { "alpha", "beta", "gamma", "delta" },
                    [99] = new List<string>(),
                    [100] = new List<string> { "single" }
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert
            result.IntStringListMap.Should().NotBeNull();
            result.IntStringListMap.Should().HaveCount(4);
            result.IntStringListMap[1].Should().Equal("first", "second", "third");
            result.IntStringListMap[2].Should().Equal("alpha", "beta", "gamma", "delta");
            result.IntStringListMap[99].Should().BeEmpty();
            result.IntStringListMap[100].Should().Equal("single");
        }

        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_array_values()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                StringIntArrayMap = new Dictionary<string, int[]>
                {
                    ["array1"] = new int[] { 5, 10, 15, 20, 25 },
                    ["array2"] = new int[] { 50, 100, 150 },
                    ["single"] = new int[] { 42 },
                    ["empty"] = new int[0]
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert
            result.StringIntArrayMap.Should().NotBeNull();
            result.StringIntArrayMap.Should().HaveCount(4);
            result.StringIntArrayMap["array1"].Should().Equal(5, 10, 15, 20, 25);
            result.StringIntArrayMap["array2"].Should().Equal(50, 100, 150);
            result.StringIntArrayMap["single"].Should().Equal(42);
            result.StringIntArrayMap["empty"].Should().BeEmpty();
        }

        [Fact]
        public void Should_handle_null_maps_with_collection_values()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                StringIntHashsetMap = null,
                IntStringListMap = null,
                StringIntArrayMap = null
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert
            result.StringIntHashsetMap.Should().BeNull();
            result.IntStringListMap.Should().BeNull();
            result.StringIntArrayMap.Should().BeNull();
        }

        [Fact]
        public void Should_serialize_all_collection_value_types_together()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                StringIntHashsetMap = new Dictionary<string, HashSet<int>>
                {
                    ["set1"] = new HashSet<int> { 10, 20 },
                    ["set2"] = new HashSet<int> { 30, 40 }
                },
                IntStringListMap = new Dictionary<int, List<string>>
                {
                    [1] = new List<string> { "a", "b" },
                    [2] = new List<string> { "c", "d" }
                },
                StringIntArrayMap = new Dictionary<string, int[]>
                {
                    ["arr1"] = new int[] { 100, 200 },
                    ["arr2"] = new int[] { 300, 400 }
                },
                StringStringHashsetMap = new Dictionary<string, HashSet<string>>
                {
                    ["key1"] = new HashSet<string> { "val1", "val2" },
                    ["key2"] = new HashSet<string> { "val3", "val4" }
                },
                IntDoubleArrayMap = new Dictionary<int, double[]>
                {
                    [1] = new double[] { 1.5, 2.5 },
                    [2] = new double[] { 3.5, 4.5 }
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert - maps with collection values
            result.StringIntHashsetMap.Should().HaveCount(2);
            result.StringIntHashsetMap["set1"].Should().BeEquivalentTo(new[] { 10, 20 });
            result.StringIntHashsetMap["set2"].Should().BeEquivalentTo(new[] { 30, 40 });
            
            result.IntStringListMap.Should().HaveCount(2);
            result.IntStringListMap[1].Should().Equal("a", "b");
            result.IntStringListMap[2].Should().Equal("c", "d");
            
            result.StringIntArrayMap.Should().HaveCount(2);
            result.StringIntArrayMap["arr1"].Should().Equal(100, 200);
            result.StringIntArrayMap["arr2"].Should().Equal(300, 400);
            
            result.StringStringHashsetMap.Should().HaveCount(2);
            result.StringStringHashsetMap["key1"].Should().BeEquivalentTo(new[] { "val1", "val2" });
            result.StringStringHashsetMap["key2"].Should().BeEquivalentTo(new[] { "val3", "val4" });
            
            result.IntDoubleArrayMap.Should().HaveCount(2);
            result.IntDoubleArrayMap[1].Should().Equal(1.5, 2.5);
            result.IntDoubleArrayMap[2].Should().Equal(3.5, 4.5);
        }

        [Fact]//(Skip = "protobuf-net doesn't support repeated field tags for string collections in maps")]
        public void Should_maintain_cross_compatibility_with_protobuf_net_for_collection_values()
        {
            // Arrange
            var model = new MapCollectionModel
            {
                StringIntHashsetMap = new Dictionary<string, HashSet<int>>
                {
                    ["test"] = new HashSet<int> { 1, 2, 3 }
                },
                IntStringListMap = new Dictionary<int, List<string>>
                {
                    [1] = new List<string> { "test1", "test2" }
                },
                StringIntArrayMap = new Dictionary<string, int[]>
                {
                    ["array"] = new int[] { 10, 20 }
                }
            };

            // Test GProtobuf -> protobuf-net
            var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            //var protobufNetData = SerializeWithProtobufNet(model);
            
            var fromGProtobuf = DeserializeWithProtobufNet<MapCollectionModel>(gprotobufData);
            //DeserializeWithGProtobuf(protobufNetData, 
            //    bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            fromGProtobuf.StringIntHashsetMap.Should().NotBeNull();
            fromGProtobuf.StringIntHashsetMap["test"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
            fromGProtobuf.IntStringListMap[1].Should().Equal("test1", "test2");
            fromGProtobuf.StringIntArrayMap["array"].Should().Equal(10, 20);

            // Test protobuf-net -> GProtobuf
            var protobufData = SerializeWithProtobufNet(model);
            var fromProtobuf = DeserializeWithGProtobuf(protobufData, 
                bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            fromProtobuf.StringIntHashsetMap.Should().NotBeNull();
            fromProtobuf.StringIntHashsetMap["test"].Should().BeEquivalentTo(new[] { 1, 2, 3 });
            fromProtobuf.IntStringListMap[1].Should().Equal("test1", "test2");
            fromProtobuf.StringIntArrayMap["array"].Should().Equal(10, 20);
        }

        [Fact]
        public void Should_handle_large_collections_in_map_values()
        {
            // Arrange
            var largeSet = new HashSet<int>();
            var largeList = new List<string>();
            var largeArray = new int[100];
            
            for (int i = 0; i < 100; i++)
            {
                largeSet.Add(i);
                largeList.Add($"item_{i}");
                largeArray[i] = i * 10;
            }

            var model = new MapCollectionModel
            {
                StringIntHashsetMap = new Dictionary<string, HashSet<int>>
                {
                    ["large"] = largeSet
                },
                IntStringListMap = new Dictionary<int, List<string>>
                {
                    [1] = largeList
                },
                StringIntArrayMap = new Dictionary<string, int[]>
                {
                    ["bigarray"] = largeArray
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapCollectionModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapCollectionModel(bytes));

            // Assert
            result.StringIntHashsetMap["large"].Should().HaveCount(100);
            result.StringIntHashsetMap["large"].Should().Contain(0);
            result.StringIntHashsetMap["large"].Should().Contain(99);
            
            result.IntStringListMap[1].Should().HaveCount(100);
            result.IntStringListMap[1][0].Should().Be("item_0");
            result.IntStringListMap[1][99].Should().Be("item_99");
            
            result.StringIntArrayMap["bigarray"].Should().HaveCount(100);
            result.StringIntArrayMap["bigarray"][0].Should().Be(0);
            result.StringIntArrayMap["bigarray"][99].Should().Be(990);
        }
    }
}