// Temporarily disabled - ComplexMapTestModel not implemented in V2
#if false
using System.Collections.Generic;
using System.IO;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.CrossTests.TestModel.Serialization;
using Xunit;

namespace GProtobuf.Tests
{
    public class ComplexMapTests
    {
        private ComplexMapTestModel CreateTestModel()
        {
            return new ComplexMapTestModel
            {
                // Custom class as key
                CustomKeyMap = new Dictionary<MapKeyClass, string>
                {
                    { new MapKeyClass { Id = 1, Code = "A" }, "First" },
                    { new MapKeyClass { Id = 2, Code = "B" }, "Second" }
                },

                // Custom class as value
                CustomValueMap = new Dictionary<int, MapValueClass>
                {
                    { 1, new MapValueClass { Name = "Item1", Value = 1.5, Tags = new List<int> { 1, 2, 3 } } },
                    { 2, new MapValueClass { Name = "Item2", Value = 2.5, Tags = new List<int> { 4, 5 } } }
                },

                // Custom class as both key and value
                CustomKeyValueMap = new Dictionary<MapKeyClass, MapValueClass>
                {
                    {
                        new MapKeyClass { Id = 10, Code = "X" },
                        new MapValueClass { Name = "ValueX", Value = 10.0, Tags = new List<int> { 10 } }
                    }
                },

                // Simple maps for comparison
                SimpleMap = new Dictionary<int, string>
                {
                    { 1, "one" },
                    { 2, "two" },
                    { 3, "three" }
                },

                ReverseSimpleMap = new Dictionary<string, int>
                {
                    { "one", 1 },
                    { "two", 2 }
                }
            };
        }

        [Fact]
        public void SerializeDeserialize_SimpleMap_GProtobuf()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                SimpleMap = new Dictionary<int, string>
                {
                    { 1, "one" },
                    { 2, "two" },
                    { 3, "three" }
                }
            };

            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeComplexMapTestModel(ms, original);
            var bytes = ms.ToArray();

            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeComplexMapTestModel(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.SimpleMap);
            Assert.Equal(original.SimpleMap.Count, deserialized.SimpleMap.Count);
            foreach (var kvp in original.SimpleMap)
            {
                Assert.True(deserialized.SimpleMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.SimpleMap[kvp.Key]);
            }
        }

        [Fact]
        public void SerializeDeserialize_CustomValueMap_GProtobuf()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                CustomValueMap = new Dictionary<int, MapValueClass>
                {
                    { 1, new MapValueClass { Name = "Test", Value = 42.0, Tags = new List<int> { 1, 2, 3 } } }
                }
            };

            // Act
            var ms = new MemoryStream();
            Serializers.SerializeComplexMapTestModel(ms, original);
            var bytes = ms.ToArray();

            var deserialized = Deserializers.DeserializeComplexMapTestModel(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized.CustomValueMap);
            Assert.Single(deserialized.CustomValueMap);
            Assert.True(deserialized.CustomValueMap.ContainsKey(1));

            var value = deserialized.CustomValueMap[1];
            Assert.Equal("Test", value.Name);
            Assert.Equal(42.0, value.Value);
            Assert.NotNull(value.Tags);
            Assert.Equal(3, value.Tags.Count);
        }

        [Fact]
        public void CrossCompatibility_SimpleMap_GProtobufToProtobufNet()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                SimpleMap = new Dictionary<int, string>
                {
                    { 1, "one" },
                    { 2, "two" }
                }
            };

            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeComplexMapTestModel(ms, original);
            var gprotobufBytes = ms.ToArray();

            // Deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<ComplexMapTestModel>(
                new MemoryStream(gprotobufBytes));

            // Assert
            Assert.NotNull(protobufNetDeserialized);
            Assert.NotNull(protobufNetDeserialized.SimpleMap);
            Assert.Equal(original.SimpleMap.Count, protobufNetDeserialized.SimpleMap.Count);
        }

        [Fact]
        public void CrossCompatibility_SimpleMap_ProtobufNetToGProtobuf()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                SimpleMap = new Dictionary<int, string>
                {
                    { 1, "one" },
                    { 2, "two" }
                }
            };

            // Act - Serialize with protobuf-net
            var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var protobufNetBytes = ms.ToArray();

            // Deserialize with GProtobuf
            var gprotobufDeserialized = Deserializers.DeserializeComplexMapTestModel(protobufNetBytes);

            // Assert
            Assert.NotNull(gprotobufDeserialized);
            Assert.NotNull(gprotobufDeserialized.SimpleMap);
            Assert.Equal(original.SimpleMap.Count, gprotobufDeserialized.SimpleMap.Count);
            foreach (var kvp in original.SimpleMap)
            {
                Assert.True(gprotobufDeserialized.SimpleMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.SimpleMap[kvp.Key]);
            }
        }

        [Fact]
        public void CrossCompatibility_CustomValueMap_GProtobufToProtobufNet()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                CustomValueMap = new Dictionary<int, MapValueClass>
                {
                    { 1, new MapValueClass { Name = "Test", Value = 42.0 } }
                }
            };

            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeComplexMapTestModel(ms, original);
            var gprotobufBytes = ms.ToArray();

            // Deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<ComplexMapTestModel>(
                new MemoryStream(gprotobufBytes));

            // Assert
            Assert.NotNull(protobufNetDeserialized);
            Assert.NotNull(protobufNetDeserialized.CustomValueMap);
            Assert.Single(protobufNetDeserialized.CustomValueMap);
            Assert.Equal("Test", protobufNetDeserialized.CustomValueMap[1].Name);
            Assert.Equal(42.0, protobufNetDeserialized.CustomValueMap[1].Value);
        }

        [Fact]
        public void CrossCompatibility_CustomValueMap_ProtobufNetToGProtobuf()
        {
            // Arrange
            var original = new ComplexMapTestModel
            {
                CustomValueMap = new Dictionary<int, MapValueClass>
                {
                    { 1, new MapValueClass { Name = "Test", Value = 42.0, Tags = new List<int> { 1, 2 } } }
                }
            };

            // Act - Serialize with protobuf-net
            var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var protobufNetBytes = ms.ToArray();

            // Deserialize with GProtobuf
            var gprotobufDeserialized = Deserializers.DeserializeComplexMapTestModel(protobufNetBytes);

            // Assert
            Assert.NotNull(gprotobufDeserialized);
            Assert.NotNull(gprotobufDeserialized.CustomValueMap);
            Assert.Single(gprotobufDeserialized.CustomValueMap);

            var value = gprotobufDeserialized.CustomValueMap[1];
            Assert.Equal("Test", value.Name);
            Assert.Equal(42.0, value.Value);
        }
    }
}
#endif
