using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class MapWithListKeysTests : BaseSerializationTest
    {
        // Helper class to compare lists as dictionary keys
        private class ListKeyComparer<T> : IEqualityComparer<List<T>>
        {
            public bool Equals(List<T> x, List<T> y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return x.SequenceEqual(y);
            }
            
            public int GetHashCode(List<T> obj)
            {
                if (obj == null) return 0;
                int hash = 17;
                foreach (var item in obj)
                {
                    hash = hash * 31 + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }
        
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_int_list_keys()
        {
            // Arrange
            var model = new MapWithListKeysModel
            {
                IntListStringMap = new Dictionary<List<int>, string>(new ListKeyComparer<int>())
                {
                    { new List<int> { 1, 2, 3 }, "first" },
                    { new List<int> { 4, 5 }, "second" },
                    { new List<int> { }, "empty" },
                    { new List<int> { 100 }, "single" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var gprotobufBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithListKeysModel);
            
            // Act - Serialize with protobuf-net for comparison
            var protobufNetBytes = SerializeWithProtobufNet(model);
            
            // Debug output
            PrintHexDump("GProtobuf", gprotobufBytes);
            PrintHexDump("protobuf-net", protobufNetBytes);
            
            // Act - Deserialize with both
            var deserializedGp = DeserializeWithGProtobuf(gprotobufBytes, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithListKeysModel(bytes));
            var deserializedPbn = DeserializeWithProtobufNet<MapWithListKeysModel>(protobufNetBytes);
            
            // Act - Cross deserialize
            var crossDeserializedGp = DeserializeWithGProtobuf(protobufNetBytes, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithListKeysModel(bytes));
            var crossDeserializedPbn = DeserializeWithProtobufNet<MapWithListKeysModel>(gprotobufBytes);
            
            // Assert - Check original model
            deserializedGp.IntListStringMap.Should().NotBeNull();
            deserializedGp.IntListStringMap.Count.Should().Be(4);
            
            // Verify each entry by iterating (can't use indexer with new List instances)
            var foundFirst = false;
            var foundSecond = false;
            var foundEmpty = false;
            var foundSingle = false;
            
            foreach (var kvp in deserializedGp.IntListStringMap)
            {
                if (kvp.Key.SequenceEqual(new List<int> { 1, 2, 3 }) && kvp.Value == "first")
                    foundFirst = true;
                else if (kvp.Key.SequenceEqual(new List<int> { 4, 5 }) && kvp.Value == "second")
                    foundSecond = true;
                else if (kvp.Key.Count == 0 && kvp.Value == "empty")
                    foundEmpty = true;
                else if (kvp.Key.SequenceEqual(new List<int> { 100 }) && kvp.Value == "single")
                    foundSingle = true;
            }
            
            foundFirst.Should().BeTrue("Should find entry with key [1,2,3] and value 'first'");
            foundSecond.Should().BeTrue("Should find entry with key [4,5] and value 'second'");
            foundEmpty.Should().BeTrue("Should find entry with empty key and value 'empty'");
            foundSingle.Should().BeTrue("Should find entry with key [100] and value 'single'");
            
            // Check protobuf-net deserialized correctly
            deserializedPbn.IntListStringMap.Should().NotBeNull();
            deserializedPbn.IntListStringMap.Count.Should().Be(4);
            
            // Check cross-compatibility
            crossDeserializedGp.IntListStringMap.Should().NotBeNull();
            crossDeserializedGp.IntListStringMap.Count.Should().Be(4);
            
            // Verify cross-deserialized GProtobuf data
            foundFirst = foundSecond = foundEmpty = foundSingle = false;
            foreach (var kvp in crossDeserializedGp.IntListStringMap)
            {
                if (kvp.Key.SequenceEqual(new List<int> { 1, 2, 3 }) && kvp.Value == "first")
                    foundFirst = true;
                else if (kvp.Key.SequenceEqual(new List<int> { 4, 5 }) && kvp.Value == "second")
                    foundSecond = true;
                else if (kvp.Key.Count == 0 && kvp.Value == "empty")
                    foundEmpty = true;
                else if (kvp.Key.SequenceEqual(new List<int> { 100 }) && kvp.Value == "single")
                    foundSingle = true;
            }
            foundFirst.Should().BeTrue("Cross-deserialized should find entry with key [1,2,3] and value 'first'");
            foundSecond.Should().BeTrue("Cross-deserialized should find entry with key [4,5] and value 'second'");
            foundEmpty.Should().BeTrue("Cross-deserialized should find entry with empty key and value 'empty'");
            foundSingle.Should().BeTrue("Cross-deserialized should find entry with key [100] and value 'single'");
            
            crossDeserializedPbn.IntListStringMap.Should().NotBeNull();
            crossDeserializedPbn.IntListStringMap.Count.Should().Be(4);
        }
        
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_custom_list_keys()
        {
            // Arrange
            var model = new MapWithListKeysModel
            {
                CustomNestedListStringMap = new Dictionary<List<CustomNested>, string>(new ListKeyComparer<CustomNested>())
                {
                    { 
                        new List<CustomNested> 
                        { 
                            new CustomNested { Id = 1, Name = "Item1", Value = 10.5 },
                            new CustomNested { Id = 2, Name = "Item2", Value = 20.5 }
                        }, 
                        "first" 
                    },
                    { 
                        new List<CustomNested> 
                        { 
                            new CustomNested { Id = 3, Name = "Item3", Value = 30.5 }
                        }, 
                        "second" 
                    },
                    { new List<CustomNested>(), "empty" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var gprotobufBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithListKeysModel);
            
            // Act - Serialize with protobuf-net for comparison
            var protobufNetBytes = SerializeWithProtobufNet(model);
            
            // Debug output
            PrintHexDump("GProtobuf", gprotobufBytes);
            PrintHexDump("protobuf-net", protobufNetBytes);
            
            // Act - Deserialize with both
            var deserializedGp = DeserializeWithGProtobuf(gprotobufBytes, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithListKeysModel(bytes));
            var deserializedPbn = DeserializeWithProtobufNet<MapWithListKeysModel>(protobufNetBytes);
            
            // Act - Cross deserialize
            var crossDeserializedGp = DeserializeWithGProtobuf(protobufNetBytes, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithListKeysModel(bytes));
            var crossDeserializedPbn = DeserializeWithProtobufNet<MapWithListKeysModel>(gprotobufBytes);
            
            // Assert
            deserializedGp.CustomNestedListStringMap.Should().NotBeNull();
            deserializedGp.CustomNestedListStringMap.Count.Should().Be(3);
            
            // Check protobuf-net deserialized correctly
            deserializedPbn.CustomNestedListStringMap.Should().NotBeNull();
            deserializedPbn.CustomNestedListStringMap.Count.Should().Be(3);
            
            // Check cross-compatibility
            crossDeserializedGp.CustomNestedListStringMap.Should().NotBeNull();
            crossDeserializedGp.CustomNestedListStringMap.Count.Should().Be(3);
            crossDeserializedPbn.CustomNestedListStringMap.Should().NotBeNull();
            crossDeserializedPbn.CustomNestedListStringMap.Count.Should().Be(3);
        }
        
        private void PrintHexDump(string label, byte[] bytes)
        {
            System.Console.WriteLine($"{label} bytes: {string.Join(",", bytes.Select(b => b.ToString("X2")))}");
        }
    }
}