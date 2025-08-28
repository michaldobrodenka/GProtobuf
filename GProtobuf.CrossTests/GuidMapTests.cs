using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GProtobuf.Core;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.CrossTests.TestModel.Serialization;
using Xunit;

namespace GProtobuf.Tests
{
    public class GuidMapTests
    {
        private GuidMapTestModel CreateTestModel()
        {
            var guid1 = new Guid("12345678-1234-1234-1234-123456789abc");
            var guid2 = new Guid("87654321-4321-4321-4321-cba987654321");
            var guid3 = Guid.Empty;
            var guid4 = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            
            return new GuidMapTestModel
            {
                // Guid as key tests
                GuidStringMap = new Dictionary<Guid, string>
                {
                    { guid1, "first" },
                    { guid2, "second" },
                    { guid3, "empty" },
                    { guid4, "fourth" }
                },
                
                GuidIntMap = new Dictionary<Guid, int>
                {
                    { guid1, 100 },
                    { guid2, 200 },
                    { guid3, 0 },
                    { guid4, -300 }
                },
                
                GuidDoubleMap = new Dictionary<Guid, double>
                {
                    { guid1, 1.5 },
                    { guid2, 3.14159 },
                    { guid3, 0.0 },
                    { guid4, -2.71828 }
                },
                
                GuidBoolMap = new Dictionary<Guid, bool>
                {
                    { guid1, true },
                    { guid2, false },
                    { guid3, true }
                },
                
                GuidLongMap = new Dictionary<Guid, long>
                {
                    { guid1, long.MaxValue },
                    { guid2, long.MinValue },
                    { guid3, 0L }
                },
                
                // Guid as value tests
                StringGuidMap = new Dictionary<string, Guid>
                {
                    { "first", guid1 },
                    { "second", guid2 },
                    { "empty", guid3 },
                    { "fourth", guid4 }
                },
                
                IntGuidMap = new Dictionary<int, Guid>
                {
                    { 1, guid1 },
                    { 2, guid2 },
                    { 0, guid3 },
                    { -1, guid4 }
                },
                
                LongGuidMap = new Dictionary<long, Guid>
                {
                    { long.MaxValue, guid1 },
                    { long.MinValue, guid2 },
                    { 0L, guid3 }
                },
                
                // Guid as both key and value
                GuidGuidMap = new Dictionary<Guid, Guid>
                {
                    { guid1, guid2 },
                    { guid2, guid3 },
                    { guid3, guid4 },
                    { guid4, guid1 }
                },
                
                // With custom types
                GuidNestedItemMap = new Dictionary<Guid, NestedItem>
                {
                    { guid1, new NestedItem { Id = 10, Name = "First", Value = 1.5 } },
                    { guid2, new NestedItem { Id = 20, Name = "Second", Value = 2.5 } }
                },
                
                NestedItemGuidMap = new Dictionary<NestedItem, Guid>
                {
                    { new NestedItem { Id = 100, Name = "Test1", Value = 100.5 }, guid1 },
                    { new NestedItem { Id = 200, Name = "Test2", Value = 200.5 }, guid2 }
                },
                
                // With collections
                GuidIntListMap = new Dictionary<Guid, List<int>>
                {
                    { guid1, new List<int> { 1, 2, 3 } },
                    { guid2, new List<int> { 10, 20, 30 } }
                },
                
                GuidByteArrayMap = new Dictionary<Guid, byte[]>
                {
                    { guid1, new byte[] { 1, 2, 3, 4, 5 } },
                    { guid2, new byte[] { 10, 20, 30, 40 } }
                },
                
                // Empty dictionary
                EmptyGuidStringMap = new Dictionary<Guid, string>()
            };
        }
        
        [Fact]
        public void SimpleGuidKeyTest()
        {
            // Arrange - test just Guid key map
            var guid1 = new Guid("12345678-1234-1234-1234-123456789abc");
            var guid2 = Guid.Empty;
            
            var model = new GuidMapTestModel
            {
                GuidStringMap = new Dictionary<Guid, string>
                {
                    { guid1, "test value" },
                    { guid2, "empty guid" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, model);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeGuidMapTestModel(bytes);
            
            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.GuidStringMap);
            Assert.Equal(2, deserialized.GuidStringMap.Count);
            Assert.Equal("test value", deserialized.GuidStringMap[guid1]);
            Assert.Equal("empty guid", deserialized.GuidStringMap[guid2]);
        }
        
        [Fact]
        public void SimpleGuidValueTest()
        {
            // Arrange - test just Guid value map
            var guid1 = new Guid("12345678-1234-1234-1234-123456789abc");
            var guid2 = Guid.Empty;
            
            var model = new GuidMapTestModel
            {
                StringGuidMap = new Dictionary<string, Guid>
                {
                    { "first", guid1 },
                    { "empty", guid2 }
                }
            };
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, model);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeGuidMapTestModel(bytes);
            
            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.StringGuidMap);
            Assert.Equal(2, deserialized.StringGuidMap.Count);
            Assert.Equal(guid1, deserialized.StringGuidMap["first"]);
            Assert.Equal(guid2, deserialized.StringGuidMap["empty"]);
        }
        
        [Fact]
        public void SerializeDeserialize_GuidMaps_GProtobuf()
        {
            // Arrange
            var original = CreateTestModel();
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, original);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeGuidMapTestModel(bytes);
            
            // Assert - check each property individually
            Assert.NotNull(deserialized);
            
            // Check GuidStringMap
            Assert.NotNull(deserialized.GuidStringMap);
            Assert.Equal(original.GuidStringMap.Count, deserialized.GuidStringMap.Count);
            foreach (var kvp in original.GuidStringMap)
            {
                Assert.True(deserialized.GuidStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.GuidStringMap[kvp.Key]);
            }
            
            // Check GuidIntMap
            Assert.NotNull(deserialized.GuidIntMap);
            Assert.Equal(original.GuidIntMap.Count, deserialized.GuidIntMap.Count);
            foreach (var kvp in original.GuidIntMap)
            {
                Assert.True(deserialized.GuidIntMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.GuidIntMap[kvp.Key]);
            }
            
            // Check StringGuidMap
            Assert.NotNull(deserialized.StringGuidMap);
            Assert.Equal(original.StringGuidMap.Count, deserialized.StringGuidMap.Count);
            foreach (var kvp in original.StringGuidMap)
            {
                Assert.True(deserialized.StringGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.StringGuidMap[kvp.Key]);
            }
            
            // Check GuidGuidMap
            Assert.NotNull(deserialized.GuidGuidMap);
            Assert.Equal(original.GuidGuidMap.Count, deserialized.GuidGuidMap.Count);
            foreach (var kvp in original.GuidGuidMap)
            {
                Assert.True(deserialized.GuidGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.GuidGuidMap[kvp.Key]);
            }
        }
        
        [Fact]
        public void CrossCompatibility_GProtobufToProtobufNet()
        {
            // Arrange
            var original = CreateTestModel();
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, original);
            var gprotobufBytes = ms.ToArray();
            
            // Deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<GuidMapTestModel>(
                new MemoryStream(gprotobufBytes));
            
            // Assert - check properties individually since empty dictionaries become null
            Assert.NotNull(protobufNetDeserialized);
            
            // GuidStringMap
            Assert.NotNull(protobufNetDeserialized.GuidStringMap);
            Assert.Equal(original.GuidStringMap.Count, protobufNetDeserialized.GuidStringMap.Count);
            foreach (var kvp in original.GuidStringMap)
            {
                Assert.True(protobufNetDeserialized.GuidStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.GuidStringMap[kvp.Key]);
            }
            
            // GuidIntMap
            Assert.NotNull(protobufNetDeserialized.GuidIntMap);
            Assert.Equal(original.GuidIntMap.Count, protobufNetDeserialized.GuidIntMap.Count);
            foreach (var kvp in original.GuidIntMap)
            {
                Assert.True(protobufNetDeserialized.GuidIntMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.GuidIntMap[kvp.Key]);
            }
            
            // StringGuidMap
            Assert.NotNull(protobufNetDeserialized.StringGuidMap);
            Assert.Equal(original.StringGuidMap.Count, protobufNetDeserialized.StringGuidMap.Count);
            foreach (var kvp in original.StringGuidMap)
            {
                Assert.True(protobufNetDeserialized.StringGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.StringGuidMap[kvp.Key]);
            }
            
            // GuidGuidMap
            Assert.NotNull(protobufNetDeserialized.GuidGuidMap);
            Assert.Equal(original.GuidGuidMap.Count, protobufNetDeserialized.GuidGuidMap.Count);
            foreach (var kvp in original.GuidGuidMap)
            {
                Assert.True(protobufNetDeserialized.GuidGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.GuidGuidMap[kvp.Key]);
            }
            
            // GuidByteArrayMap - test byte[] as dictionary value
            Assert.NotNull(protobufNetDeserialized.GuidByteArrayMap);
            Assert.Equal(original.GuidByteArrayMap.Count, protobufNetDeserialized.GuidByteArrayMap.Count);
            foreach (var kvp in original.GuidByteArrayMap)
            {
                Assert.True(protobufNetDeserialized.GuidByteArrayMap.ContainsKey(kvp.Key));
                Assert.NotNull(protobufNetDeserialized.GuidByteArrayMap[kvp.Key]);
                Assert.Equal(kvp.Value, protobufNetDeserialized.GuidByteArrayMap[kvp.Key]);
            }
            
            // EmptyGuidStringMap - empty dictionaries become null in protobuf
            Assert.Null(protobufNetDeserialized.EmptyGuidStringMap);
        }
        
        [Fact]
        public void CrossCompatibility_ProtobufNetToGProtobuf()
        {
            // Arrange
            var original = CreateTestModel();
            
            // Act - Serialize with protobuf-net
            var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var protobufNetBytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var gprotobufDeserialized = Deserializers.DeserializeGuidMapTestModel(protobufNetBytes);
            
            // Assert - check properties individually
            Assert.NotNull(gprotobufDeserialized);
            
            // GuidStringMap
            Assert.NotNull(gprotobufDeserialized.GuidStringMap);
            Assert.Equal(original.GuidStringMap.Count, gprotobufDeserialized.GuidStringMap.Count);
            foreach (var kvp in original.GuidStringMap)
            {
                Assert.True(gprotobufDeserialized.GuidStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.GuidStringMap[kvp.Key]);
            }
            
            // GuidIntMap
            Assert.NotNull(gprotobufDeserialized.GuidIntMap);
            Assert.Equal(original.GuidIntMap.Count, gprotobufDeserialized.GuidIntMap.Count);
            foreach (var kvp in original.GuidIntMap)
            {
                Assert.True(gprotobufDeserialized.GuidIntMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.GuidIntMap[kvp.Key]);
            }
            
            // StringGuidMap
            Assert.NotNull(gprotobufDeserialized.StringGuidMap);
            Assert.Equal(original.StringGuidMap.Count, gprotobufDeserialized.StringGuidMap.Count);
            foreach (var kvp in original.StringGuidMap)
            {
                Assert.True(gprotobufDeserialized.StringGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.StringGuidMap[kvp.Key]);
            }
            
            // GuidGuidMap
            Assert.NotNull(gprotobufDeserialized.GuidGuidMap);
            Assert.Equal(original.GuidGuidMap.Count, gprotobufDeserialized.GuidGuidMap.Count);
            foreach (var kvp in original.GuidGuidMap)
            {
                Assert.True(gprotobufDeserialized.GuidGuidMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.GuidGuidMap[kvp.Key]);
            }
            
            // GuidByteArrayMap - test byte[] as dictionary value
            Assert.NotNull(gprotobufDeserialized.GuidByteArrayMap);
            Assert.Equal(original.GuidByteArrayMap.Count, gprotobufDeserialized.GuidByteArrayMap.Count);
            foreach (var kvp in original.GuidByteArrayMap)
            {
                Assert.True(gprotobufDeserialized.GuidByteArrayMap.ContainsKey(kvp.Key));
                Assert.NotNull(gprotobufDeserialized.GuidByteArrayMap[kvp.Key]);
                Assert.Equal(kvp.Value, gprotobufDeserialized.GuidByteArrayMap[kvp.Key]);
            }
            
            // EmptyGuidStringMap - empty dictionaries become null in protobuf
            Assert.Null(gprotobufDeserialized.EmptyGuidStringMap);
        }
        
        [Fact]
        public void GuidMapEdgeCases()
        {
            // Test with null maps
            var modelWithNulls = new GuidMapTestModel
            {
                GuidStringMap = null,
                GuidIntMap = new Dictionary<Guid, int> { { Guid.NewGuid(), 100 } },
                EmptyGuidStringMap = new Dictionary<Guid, string>()
            };
            
            // Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, modelWithNulls);
            var bytes = ms.ToArray();
            
            // Deserialize and verify
            var deserialized = Deserializers.DeserializeGuidMapTestModel(bytes);
            Assert.Null(deserialized.GuidStringMap);
            Assert.NotNull(deserialized.GuidIntMap);
            Assert.Single(deserialized.GuidIntMap);
            // Empty dictionaries are deserialized as null in protobuf
            Assert.Null(deserialized.EmptyGuidStringMap);
        }
        
        [Fact]
        public void GuidValues_SpecialCases()
        {
            // Test special Guid values
            var model = new GuidMapTestModel
            {
                GuidGuidMap = new Dictionary<Guid, Guid>
                {
                    { Guid.Empty, Guid.NewGuid() },
                    { Guid.NewGuid(), Guid.Empty },
                    { new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), new Guid("00000000-0000-0000-0000-000000000001") }
                }
            };
            
            // Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeGuidMapTestModel(ms, model);
            var gprotobufBytes = ms.ToArray();
            
            // Deserialize with both libraries
            var gprotobufDeserialized = Deserializers.DeserializeGuidMapTestModel(gprotobufBytes);
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<GuidMapTestModel>(
                new MemoryStream(gprotobufBytes));
            
            // Verify all values
            Assert.Equal(3, gprotobufDeserialized.GuidGuidMap.Count);
            Assert.Equal(3, protobufNetDeserialized.GuidGuidMap.Count);
            
            // Check that special Guids are preserved
            Assert.True(gprotobufDeserialized.GuidGuidMap.ContainsKey(Guid.Empty));
            Assert.True(gprotobufDeserialized.GuidGuidMap.ContainsKey(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")));
            Assert.True(protobufNetDeserialized.GuidGuidMap.ContainsKey(Guid.Empty));
            Assert.True(protobufNetDeserialized.GuidGuidMap.ContainsKey(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        }
    }
}