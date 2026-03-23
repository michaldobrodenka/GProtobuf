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
    public class ByteMapTests
    {
        private ByteMapTestModel CreateTestModel()
        {
            return new ByteMapTestModel
            {
                // Byte as key tests
                ByteStringMap = new Dictionary<byte, string>
                {
                    { 0, "zero" },
                    { 1, "one" },
                    { 127, "max signed" },
                    { 255, "max unsigned" },
                    { 128, "min negative as signed" }
                },
                
                ByteIntMap = new Dictionary<byte, int>
                {
                    { 10, 100 },
                    { 20, 200 },
                    { 30, -300 }
                },
                
                ByteDoubleMap = new Dictionary<byte, double>
                {
                    { 1, 1.5 },
                    { 2, 3.14159 },
                    { 3, -2.71828 },
                    { 255, double.MaxValue },
                    { 0, 0.0 }
                },
                
                ByteBoolMap = new Dictionary<byte, bool>
                {
                    { 0, false },
                    { 1, true },
                    { 2, false },
                    { 255, true }
                },
                
                // Byte as value tests
                StringByteMap = new Dictionary<string, byte>
                {
                    { "min", 0 },
                    { "max", 255 },
                    { "mid", 128 },
                    { "one", 1 }
                },
                
                IntByteMap = new Dictionary<int, byte>
                {
                    { -1, 255 },
                    { 0, 0 },
                    { 100, 100 },
                    { 1000, 200 }
                },
                
                LongByteMap = new Dictionary<long, byte>
                {
                    { long.MaxValue, 255 },
                    { long.MinValue, 0 },
                    { 0L, 128 }
                },
                
                // Byte as both key and value
                ByteByteMap = new Dictionary<byte, byte>
                {
                    { 0, 255 },
                    { 1, 254 },
                    { 128, 127 },
                    { 255, 0 }
                },
                
                // With custom types
                ByteNestedItemMap = new Dictionary<byte, NestedItem>
                {
                    { 1, new NestedItem { Id = 10, Name = "First", Value = 1.5 } },
                    { 2, new NestedItem { Id = 20, Name = "Second", Value = 2.5 } }
                },
                
                NestedItemByteMap = new Dictionary<NestedItem, byte>
                {
                    { new NestedItem { Id = 100, Name = "Test1", Value = 100.5 }, 1 },
                    { new NestedItem { Id = 200, Name = "Test2", Value = 200.5 }, 2 }
                },
                
                // With collections
                ByteIntListMap = new Dictionary<byte, List<int>>
                {
                    { 1, new List<int> { 1, 2, 3 } },
                    { 2, new List<int> { 10, 20, 30 } }
                },
                
                ByteIntArrayMap = new Dictionary<byte, int[]>
                {
                    { 10, new int[] { 100, 200, 300 } },
                    { 20, new int[] { 1000, 2000 } }
                },
                
                // Empty dictionary
                EmptyByteStringMap = new Dictionary<byte, string>()
            };
        }
        
        [Fact]
        public void SimpleByteKeyTest()
        {
            // Arrange - test just byte key map
            var model = new ByteMapTestModel
            {
                ByteStringMap = new Dictionary<byte, string>
                {
                    { 0, "zero" },
                    { 127, "max signed" },
                    { 255, "max unsigned" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, model);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeByteMapTestModel(bytes);
            
            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.ByteStringMap);
            Assert.Equal(3, deserialized.ByteStringMap.Count);
            Assert.Equal("zero", deserialized.ByteStringMap[0]);
            Assert.Equal("max signed", deserialized.ByteStringMap[127]);
            Assert.Equal("max unsigned", deserialized.ByteStringMap[255]);
        }
        
        [Fact]
        public void SimpleByteValueTest()
        {
            // Arrange - test just byte value map
            var model = new ByteMapTestModel
            {
                StringByteMap = new Dictionary<string, byte>
                {
                    { "min", 0 },
                    { "max", 255 },
                    { "mid", 128 }
                }
            };
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, model);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeByteMapTestModel(bytes);
            
            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.StringByteMap);
            Assert.Equal(3, deserialized.StringByteMap.Count);
            Assert.Equal(0, deserialized.StringByteMap["min"]);
            Assert.Equal(255, deserialized.StringByteMap["max"]);
            Assert.Equal(128, deserialized.StringByteMap["mid"]);
        }
        
        [Fact]
        public void SerializeDeserialize_ByteMaps_GProtobuf()
        {
            // Arrange
            var original = CreateTestModel();
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, original);
            var bytes = ms.ToArray();
            
            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeByteMapTestModel(bytes);
            
            // Assert - check each property individually
            Assert.NotNull(deserialized);
            
            // Check ByteStringMap
            Assert.NotNull(deserialized.ByteStringMap);
            Assert.Equal(original.ByteStringMap.Count, deserialized.ByteStringMap.Count);
            foreach (var kvp in original.ByteStringMap)
            {
                Assert.True(deserialized.ByteStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.ByteStringMap[kvp.Key]);
            }
        }
        
        [Fact]
        public void CrossCompatibility_GProtobufToProtobufNet()
        {
            // Arrange
            var original = CreateTestModel();
            
            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, original);
            var gprotobufBytes = ms.ToArray();
            
            // Deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<ByteMapTestModel>(
                new MemoryStream(gprotobufBytes));
            
            // Assert - check properties individually since empty dictionaries become null
            Assert.NotNull(protobufNetDeserialized);
            
            // ByteStringMap
            Assert.NotNull(protobufNetDeserialized.ByteStringMap);
            Assert.Equal(original.ByteStringMap.Count, protobufNetDeserialized.ByteStringMap.Count);
            foreach (var kvp in original.ByteStringMap)
            {
                Assert.True(protobufNetDeserialized.ByteStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteStringMap[kvp.Key]);
            }
            
            // ByteIntMap
            Assert.NotNull(protobufNetDeserialized.ByteIntMap);
            Assert.Equal(original.ByteIntMap.Count, protobufNetDeserialized.ByteIntMap.Count);
            foreach (var kvp in original.ByteIntMap)
            {
                Assert.True(protobufNetDeserialized.ByteIntMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteIntMap[kvp.Key]);
            }
            
            // ByteDoubleMap
            Assert.NotNull(protobufNetDeserialized.ByteDoubleMap);
            Assert.Equal(original.ByteDoubleMap.Count, protobufNetDeserialized.ByteDoubleMap.Count);
            foreach (var kvp in original.ByteDoubleMap)
            {
                Assert.True(protobufNetDeserialized.ByteDoubleMap.ContainsKey(kvp.Key));
                Assert.InRange(protobufNetDeserialized.ByteDoubleMap[kvp.Key], kvp.Value - 0.0001, kvp.Value + 0.0001);
            }
            
            // ByteBoolMap
            Assert.NotNull(protobufNetDeserialized.ByteBoolMap);
            Assert.Equal(original.ByteBoolMap.Count, protobufNetDeserialized.ByteBoolMap.Count);
            foreach (var kvp in original.ByteBoolMap)
            {
                Assert.True(protobufNetDeserialized.ByteBoolMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteBoolMap[kvp.Key]);
            }
            
            // StringByteMap
            Assert.NotNull(protobufNetDeserialized.StringByteMap);
            Assert.Equal(original.StringByteMap.Count, protobufNetDeserialized.StringByteMap.Count);
            foreach (var kvp in original.StringByteMap)
            {
                Assert.True(protobufNetDeserialized.StringByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.StringByteMap[kvp.Key]);
            }
            
            // IntByteMap
            Assert.NotNull(protobufNetDeserialized.IntByteMap);
            Assert.Equal(original.IntByteMap.Count, protobufNetDeserialized.IntByteMap.Count);
            foreach (var kvp in original.IntByteMap)
            {
                Assert.True(protobufNetDeserialized.IntByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.IntByteMap[kvp.Key]);
            }
            
            // LongByteMap
            Assert.NotNull(protobufNetDeserialized.LongByteMap);
            Assert.Equal(original.LongByteMap.Count, protobufNetDeserialized.LongByteMap.Count);
            foreach (var kvp in original.LongByteMap)
            {
                Assert.True(protobufNetDeserialized.LongByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.LongByteMap[kvp.Key]);
            }
            
            // ByteByteMap
            Assert.NotNull(protobufNetDeserialized.ByteByteMap);
            Assert.Equal(original.ByteByteMap.Count, protobufNetDeserialized.ByteByteMap.Count);
            foreach (var kvp in original.ByteByteMap)
            {
                Assert.True(protobufNetDeserialized.ByteByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteByteMap[kvp.Key]);
            }
            
            // ByteNestedItemMap
            Assert.NotNull(protobufNetDeserialized.ByteNestedItemMap);
            Assert.Equal(original.ByteNestedItemMap.Count, protobufNetDeserialized.ByteNestedItemMap.Count);
            foreach (var kvp in original.ByteNestedItemMap)
            {
                Assert.True(protobufNetDeserialized.ByteNestedItemMap.ContainsKey(kvp.Key));
                var item = protobufNetDeserialized.ByteNestedItemMap[kvp.Key];
                Assert.Equal(kvp.Value.Id, item.Id);
                Assert.Equal(kvp.Value.Name, item.Name);
                Assert.Equal(kvp.Value.Value, item.Value);
            }
            
            // NestedItemByteMap - this uses custom class as key
            Assert.NotNull(protobufNetDeserialized.NestedItemByteMap);
            Assert.Equal(original.NestedItemByteMap.Count, protobufNetDeserialized.NestedItemByteMap.Count);
            // For custom keys, we need to check values by matching properties
            foreach (var kvp in original.NestedItemByteMap)
            {
                var matchingKvp = protobufNetDeserialized.NestedItemByteMap
                    .FirstOrDefault(x => x.Key.Id == kvp.Key.Id && x.Key.Name == kvp.Key.Name);
                Assert.NotEqual(default, matchingKvp);
                Assert.Equal(kvp.Value, matchingKvp.Value);
            }
            
            // ByteIntListMap
            Assert.NotNull(protobufNetDeserialized.ByteIntListMap);
            Assert.Equal(original.ByteIntListMap.Count, protobufNetDeserialized.ByteIntListMap.Count);
            foreach (var kvp in original.ByteIntListMap)
            {
                Assert.True(protobufNetDeserialized.ByteIntListMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteIntListMap[kvp.Key]);
            }
            
            // ByteIntArrayMap
            Assert.NotNull(protobufNetDeserialized.ByteIntArrayMap);
            Assert.Equal(original.ByteIntArrayMap.Count, protobufNetDeserialized.ByteIntArrayMap.Count);
            foreach (var kvp in original.ByteIntArrayMap)
            {
                Assert.True(protobufNetDeserialized.ByteIntArrayMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, protobufNetDeserialized.ByteIntArrayMap[kvp.Key]);
            }
            
            // EmptyByteStringMap - empty dictionaries become null in protobuf
            Assert.Null(protobufNetDeserialized.EmptyByteStringMap);
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
            var gprotobufDeserialized = Deserializers.DeserializeByteMapTestModel(protobufNetBytes);
            
            // Assert - check properties individually since empty dictionaries become null
            Assert.NotNull(gprotobufDeserialized);
            
            // ByteStringMap
            Assert.NotNull(gprotobufDeserialized.ByteStringMap);
            Assert.Equal(original.ByteStringMap.Count, gprotobufDeserialized.ByteStringMap.Count);
            foreach (var kvp in original.ByteStringMap)
            {
                Assert.True(gprotobufDeserialized.ByteStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteStringMap[kvp.Key]);
            }
            
            // ByteIntMap
            Assert.NotNull(gprotobufDeserialized.ByteIntMap);
            Assert.Equal(original.ByteIntMap.Count, gprotobufDeserialized.ByteIntMap.Count);
            foreach (var kvp in original.ByteIntMap)
            {
                Assert.True(gprotobufDeserialized.ByteIntMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteIntMap[kvp.Key]);
            }
            
            // ByteDoubleMap
            Assert.NotNull(gprotobufDeserialized.ByteDoubleMap);
            Assert.Equal(original.ByteDoubleMap.Count, gprotobufDeserialized.ByteDoubleMap.Count);
            foreach (var kvp in original.ByteDoubleMap)
            {
                Assert.True(gprotobufDeserialized.ByteDoubleMap.ContainsKey(kvp.Key));
                Assert.InRange(gprotobufDeserialized.ByteDoubleMap[kvp.Key], kvp.Value - 0.0001, kvp.Value + 0.0001);
            }
            
            // ByteBoolMap
            Assert.NotNull(gprotobufDeserialized.ByteBoolMap);
            Assert.Equal(original.ByteBoolMap.Count, gprotobufDeserialized.ByteBoolMap.Count);
            foreach (var kvp in original.ByteBoolMap)
            {
                Assert.True(gprotobufDeserialized.ByteBoolMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteBoolMap[kvp.Key]);
            }
            
            // StringByteMap
            Assert.NotNull(gprotobufDeserialized.StringByteMap);
            Assert.Equal(original.StringByteMap.Count, gprotobufDeserialized.StringByteMap.Count);
            foreach (var kvp in original.StringByteMap)
            {
                Assert.True(gprotobufDeserialized.StringByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.StringByteMap[kvp.Key]);
            }
            
            // IntByteMap
            Assert.NotNull(gprotobufDeserialized.IntByteMap);
            Assert.Equal(original.IntByteMap.Count, gprotobufDeserialized.IntByteMap.Count);
            foreach (var kvp in original.IntByteMap)
            {
                Assert.True(gprotobufDeserialized.IntByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.IntByteMap[kvp.Key]);
            }
            
            // LongByteMap
            Assert.NotNull(gprotobufDeserialized.LongByteMap);
            Assert.Equal(original.LongByteMap.Count, gprotobufDeserialized.LongByteMap.Count);
            foreach (var kvp in original.LongByteMap)
            {
                Assert.True(gprotobufDeserialized.LongByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.LongByteMap[kvp.Key]);
            }
            
            // ByteByteMap
            Assert.NotNull(gprotobufDeserialized.ByteByteMap);
            Assert.Equal(original.ByteByteMap.Count, gprotobufDeserialized.ByteByteMap.Count);
            foreach (var kvp in original.ByteByteMap)
            {
                Assert.True(gprotobufDeserialized.ByteByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteByteMap[kvp.Key]);
            }
            
            // ByteNestedItemMap
            Assert.NotNull(gprotobufDeserialized.ByteNestedItemMap);
            Assert.Equal(original.ByteNestedItemMap.Count, gprotobufDeserialized.ByteNestedItemMap.Count);
            foreach (var kvp in original.ByteNestedItemMap)
            {
                Assert.True(gprotobufDeserialized.ByteNestedItemMap.ContainsKey(kvp.Key));
                var item = gprotobufDeserialized.ByteNestedItemMap[kvp.Key];
                Assert.Equal(kvp.Value.Id, item.Id);
                Assert.Equal(kvp.Value.Name, item.Name);
                Assert.Equal(kvp.Value.Value, item.Value);
            }
            
            // NestedItemByteMap - this uses custom class as key
            Assert.NotNull(gprotobufDeserialized.NestedItemByteMap);
            Assert.Equal(original.NestedItemByteMap.Count, gprotobufDeserialized.NestedItemByteMap.Count);
            // For custom keys, we need to check values by matching properties
            foreach (var kvp in original.NestedItemByteMap)
            {
                var matchingKvp = gprotobufDeserialized.NestedItemByteMap
                    .FirstOrDefault(x => x.Key.Id == kvp.Key.Id && x.Key.Name == kvp.Key.Name);
                Assert.NotEqual(default, matchingKvp);
                Assert.Equal(kvp.Value, matchingKvp.Value);
            }
            
            // ByteIntListMap
            Assert.NotNull(gprotobufDeserialized.ByteIntListMap);
            Assert.Equal(original.ByteIntListMap.Count, gprotobufDeserialized.ByteIntListMap.Count);
            foreach (var kvp in original.ByteIntListMap)
            {
                Assert.True(gprotobufDeserialized.ByteIntListMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteIntListMap[kvp.Key]);
            }
            
            // ByteIntArrayMap
            Assert.NotNull(gprotobufDeserialized.ByteIntArrayMap);
            Assert.Equal(original.ByteIntArrayMap.Count, gprotobufDeserialized.ByteIntArrayMap.Count);
            foreach (var kvp in original.ByteIntArrayMap)
            {
                Assert.True(gprotobufDeserialized.ByteIntArrayMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ByteIntArrayMap[kvp.Key]);
            }
            
            // EmptyByteStringMap - empty dictionaries become null in protobuf
            Assert.Null(gprotobufDeserialized.EmptyByteStringMap);
        }
        
        [Fact]
        public void ByteMapEdgeCases()
        {
            // Test with null maps
            var modelWithNulls = new ByteMapTestModel
            {
                ByteStringMap = null,
                ByteIntMap = new Dictionary<byte, int> { { 1, 100 } },
                EmptyByteStringMap = new Dictionary<byte, string>()
            };
            
            // Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, modelWithNulls);
            var bytes = ms.ToArray();
            
            // Deserialize and verify
            var deserialized = Deserializers.DeserializeByteMapTestModel(bytes);
            Assert.Null(deserialized.ByteStringMap);
            Assert.NotNull(deserialized.ByteIntMap);
            Assert.Single(deserialized.ByteIntMap);
            Assert.Equal(100, deserialized.ByteIntMap[1]);
            // Empty dictionaries are deserialized as null in protobuf
            Assert.Null(deserialized.EmptyByteStringMap);
        }
        
        [Fact]
        public void ByteValues_AllPossibleValues()
        {
            // Test all possible byte values (0-255)
            var model = new ByteMapTestModel
            {
                ByteByteMap = new Dictionary<byte, byte>()
            };
            
            for (int i = 0; i <= 255; i++)
            {
                model.ByteByteMap[(byte)i] = (byte)(255 - i);
            }
            
            // Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, model);
            var gprotobufBytes = ms.ToArray();
            
            // Deserialize with both libraries
            var gprotobufDeserialized = Deserializers.DeserializeByteMapTestModel(gprotobufBytes);
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<ByteMapTestModel>(
                new MemoryStream(gprotobufBytes));
            
            // Verify all values
            Assert.Equal(256, gprotobufDeserialized.ByteByteMap.Count);
            Assert.Equal(256, protobufNetDeserialized.ByteByteMap.Count);
            
            for (int i = 0; i <= 255; i++)
            {
                var key = (byte)i;
                var expectedValue = (byte)(255 - i);
                Assert.Equal(expectedValue, gprotobufDeserialized.ByteByteMap[key]);
                Assert.Equal(expectedValue, protobufNetDeserialized.ByteByteMap[key]);
            }
        }
        
        [Fact]
        public void Performance_LargeByteMap()
        {
            // Create a large map
            var model = new ByteMapTestModel
            {
                ByteIntListMap = new Dictionary<byte, List<int>>()
            };
            
            for (byte i = 0; i < 100; i++)
            {
                var list = new List<int>();
                for (int j = 0; j < 10; j++)
                {
                    list.Add(i * 10 + j);
                }
                model.ByteIntListMap[i] = list;
            }
            
            // Serialize and deserialize
            var ms = new MemoryStream();
            Serializers.SerializeByteMapTestModel(ms, model);
            var bytes = ms.ToArray();
            
            var deserialized = Deserializers.DeserializeByteMapTestModel(bytes);
            
            // Verify
            Assert.Equal(100, deserialized.ByteIntListMap.Count);
            foreach (var kvp in model.ByteIntListMap)
            {
                Assert.True(deserialized.ByteIntListMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.ByteIntListMap[kvp.Key]);
            }
        }
    }
}