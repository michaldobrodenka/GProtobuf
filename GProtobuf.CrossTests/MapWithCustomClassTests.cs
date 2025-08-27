using FluentAssertions;
using System.Collections.Generic;
using System.IO;
using Xunit;
using System;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;

namespace GProtobuf.CrossTests
{
    public class MapWithCustomClassTests : BaseSerializationTest
    {
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_custom_class_as_value()
        {
            // Arrange
            var model = new MapWithCustomClassModel
            {
                StringNestedItemMap = new Dictionary<string, NestedItem>
                {
                    ["first"] = new NestedItem { Id = 1, Name = "First Item", Value = 10.5 },
                    ["second"] = new NestedItem { Id = 2, Name = "Second Item", Value = 20.75 },
                    ["third"] = new NestedItem { Id = 3, Name = "Third Item", Value = 30.25 }
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            // Assert
            result.StringNestedItemMap.Should().NotBeNull();
            result.StringNestedItemMap.Should().HaveCount(3);
            result.StringNestedItemMap["first"].Should().BeEquivalentTo(new NestedItem { Id = 1, Name = "First Item", Value = 10.5 });
            result.StringNestedItemMap["second"].Should().BeEquivalentTo(new NestedItem { Id = 2, Name = "Second Item", Value = 20.75 });
            result.StringNestedItemMap["third"].Should().BeEquivalentTo(new NestedItem { Id = 3, Name = "Third Item", Value = 30.25 });
        }

        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_custom_class_as_key()
        {
            // Arrange
            var key1 = new NestedItem { Id = 10, Name = "Key One", Value = 1.1 };
            var key2 = new NestedItem { Id = 20, Name = "Key Two", Value = 2.2 };
            var key3 = new NestedItem { Id = 30, Name = "Key Three", Value = 3.3 };
            
            var model = new MapWithCustomClassModel
            {
                NestedItemStringMap = new Dictionary<NestedItem, string>
                {
                    [key1] = "Value for key one",
                    [key2] = "Value for key two",
                    [key3] = "Value for key three"
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            // Assert
            result.NestedItemStringMap.Should().NotBeNull();
            result.NestedItemStringMap.Should().HaveCount(3);
            
            // Verify we can find values using custom key
            result.NestedItemStringMap[key1].Should().Be("Value for key one");
            result.NestedItemStringMap[key2].Should().Be("Value for key two");
            result.NestedItemStringMap[key3].Should().Be("Value for key three");
        }

        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_custom_class_as_key_and_collection_as_value()
        {
            // Arrange
            var key1 = new NestedItem { Id = 100, Name = "Complex Key 1", Value = 10.5 };
            var key2 = new NestedItem { Id = 200, Name = "Complex Key 2", Value = 20.5 };
            
            var model = new MapWithCustomClassModel
            {
                NestedItemIntListMap = new Dictionary<NestedItem, List<int>>
                {
                    [key1] = new List<int> { 1, 2, 3, 4, 5 },
                    [key2] = new List<int> { 10, 20, 30 }
                },
                NestedItemStringHashSetMap = new Dictionary<NestedItem, HashSet<string>>
                {
                    [key1] = new HashSet<string> { "alpha", "beta", "gamma" },
                    [key2] = new HashSet<string> { "delta", "epsilon" }
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            // Assert
            result.NestedItemIntListMap.Should().NotBeNull();
            result.NestedItemIntListMap.Should().HaveCount(2);
            result.NestedItemIntListMap[key1].Should().Equal(1, 2, 3, 4, 5);
            result.NestedItemIntListMap[key2].Should().Equal(10, 20, 30);
            
            result.NestedItemStringHashSetMap.Should().NotBeNull();
            result.NestedItemStringHashSetMap.Should().HaveCount(2);
            result.NestedItemStringHashSetMap[key1].Should().BeEquivalentTo(new[] { "alpha", "beta", "gamma" });
            result.NestedItemStringHashSetMap[key2].Should().BeEquivalentTo(new[] { "delta", "epsilon" });
        }

        [Fact]
        public void Should_handle_null_maps_with_custom_class()
        {
            // Arrange
            var model = new MapWithCustomClassModel
            {
                StringNestedItemMap = null,
                NestedItemStringMap = null,
                NestedItemIntListMap = null,
                IntNestedItemMap = null,
                NestedItemStringHashSetMap = null
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            // Assert
            result.StringNestedItemMap.Should().BeNull();
            result.NestedItemStringMap.Should().BeNull();
            result.NestedItemIntListMap.Should().BeNull();
            result.IntNestedItemMap.Should().BeNull();
            result.NestedItemStringHashSetMap.Should().BeNull();
        }

        [Fact]
        public void Should_serialize_all_map_types_with_custom_class()
        {
            // Arrange
            var key1 = new NestedItem { Id = 1000, Name = "Universal Key", Value = 99.9 };
            var item1 = new NestedItem { Id = 1, Name = "Item 1", Value = 1.5 };
            var item2 = new NestedItem { Id = 2, Name = "Item 2", Value = 2.5 };
            
            var model = new MapWithCustomClassModel
            {
                StringNestedItemMap = new Dictionary<string, NestedItem>
                {
                    ["test"] = item1
                },
                NestedItemStringMap = new Dictionary<NestedItem, string>
                {
                    [key1] = "Universal value"
                },
                NestedItemIntListMap = new Dictionary<NestedItem, List<int>>
                {
                    [key1] = new List<int> { 999 }
                },
                IntNestedItemMap = new Dictionary<int, NestedItem>
                {
                    [42] = item2
                },
                NestedItemStringHashSetMap = new Dictionary<NestedItem, HashSet<string>>
                {
                    [key1] = new HashSet<string> { "unique" }
                }
            };

            // Act - Serialize
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            
            // Act - Deserialize
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            // Assert - all fields populated
            result.StringNestedItemMap.Should().HaveCount(1);
            result.StringNestedItemMap["test"].Should().BeEquivalentTo(item1);
            
            result.NestedItemStringMap.Should().HaveCount(1);
            result.NestedItemStringMap[key1].Should().Be("Universal value");
            
            result.NestedItemIntListMap.Should().HaveCount(1);
            result.NestedItemIntListMap[key1].Should().Equal(999);
            
            result.IntNestedItemMap.Should().HaveCount(1);
            result.IntNestedItemMap[42].Should().BeEquivalentTo(item2);
            
            result.NestedItemStringHashSetMap.Should().HaveCount(1);
            result.NestedItemStringHashSetMap[key1].Should().BeEquivalentTo(new[] { "unique" });
        }

        [Fact]
        public void Should_maintain_cross_compatibility_with_protobuf_net_for_custom_class()
        {
            // Arrange
            var key1 = new NestedItem { Id = 555, Name = "Compat Key", Value = 55.5 };
            var model = new MapWithCustomClassModel
            {
                StringNestedItemMap = new Dictionary<string, NestedItem>
                {
                    ["compat"] = new NestedItem { Id = 777, Name = "Compat Item", Value = 77.7 }
                },
                NestedItemStringMap = new Dictionary<NestedItem, string>
                {
                    [key1] = "Compatible value"
                }
            };

            // Test GProtobuf -> protobuf-net
            var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithCustomClassModel);
            var fromGProtobuf = DeserializeWithProtobufNet<MapWithCustomClassModel>(gprotobufData);

            fromGProtobuf.StringNestedItemMap.Should().NotBeNull();
            fromGProtobuf.StringNestedItemMap["compat"].Should().BeEquivalentTo(
                new NestedItem { Id = 777, Name = "Compat Item", Value = 77.7 });
            fromGProtobuf.NestedItemStringMap[key1].Should().Be("Compatible value");

            // Test protobuf-net -> GProtobuf
            var protobufData = SerializeWithProtobufNet(model);
            var fromProtobuf = DeserializeWithGProtobuf(protobufData, 
                bytes => TestModel.Serialization.Deserializers.DeserializeMapWithCustomClassModel(bytes));

            fromProtobuf.StringNestedItemMap.Should().NotBeNull();
            fromProtobuf.StringNestedItemMap["compat"].Should().BeEquivalentTo(
                new NestedItem { Id = 777, Name = "Compat Item", Value = 77.7 });
            fromProtobuf.NestedItemStringMap[key1].Should().Be("Compatible value");
        }
    }
}