using System.Collections.Generic;
using System.IO;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.CrossTests.TestModel.Serialization;
using Xunit;

namespace GProtobuf.Tests
{
    public class ShortMapTests
    {
        private ShortMapTestModel CreateTestModel()
        {
            return new ShortMapTestModel
            {
                // Short as key
                ShortStringMap = new Dictionary<short, string>
                {
                    { -32768, "min" },
                    { 0, "zero" },
                    { 32767, "max" },
                    { -1, "negative one" }
                },
                ShortIntMap = new Dictionary<short, int>
                {
                    { 100, 1000 },
                    { -100, -1000 },
                    { 0, 0 }
                },

                // Short as value
                StringShortMap = new Dictionary<string, short>
                {
                    { "min", -32768 },
                    { "max", 32767 },
                    { "zero", 0 }
                },
                IntShortMap = new Dictionary<int, short>
                {
                    { 1, 100 },
                    { 2, -100 },
                    { 3, 0 }
                },

                // Ushort as key
                UShortStringMap = new Dictionary<ushort, string>
                {
                    { 0, "zero" },
                    { 65535, "max" },
                    { 32768, "half" }
                },
                UShortIntMap = new Dictionary<ushort, int>
                {
                    { 1000, 10000 },
                    { 2000, 20000 }
                },

                // Ushort as value
                StringUShortMap = new Dictionary<string, ushort>
                {
                    { "min", 0 },
                    { "max", 65535 },
                    { "mid", 32768 }
                },
                IntUShortMap = new Dictionary<int, ushort>
                {
                    { 1, 1000 },
                    { 2, 2000 }
                },

                // Sbyte as key
                SByteStringMap = new Dictionary<sbyte, string>
                {
                    { -128, "min" },
                    { 127, "max" },
                    { 0, "zero" },
                    { -1, "negative one" }
                },
                SByteIntMap = new Dictionary<sbyte, int>
                {
                    { 10, 100 },
                    { -10, -100 }
                },

                // Sbyte as value
                StringSByteMap = new Dictionary<string, sbyte>
                {
                    { "min", -128 },
                    { "max", 127 },
                    { "zero", 0 }
                },
                IntSByteMap = new Dictionary<int, sbyte>
                {
                    { 1, 10 },
                    { 2, -10 }
                },

                // Mixed combinations
                ShortUShortMap = new Dictionary<short, ushort>
                {
                    { -1, 65535 },
                    { 0, 0 },
                    { 100, 200 }
                },
                SByteShortMap = new Dictionary<sbyte, short>
                {
                    { -1, -1000 },
                    { 0, 0 },
                    { 1, 1000 }
                }
            };
        }

        [Fact]
        public void SerializeDeserialize_ShortMaps_GProtobuf()
        {
            // Arrange
            var original = CreateTestModel();

            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeShortMapTestModel(ms, original);
            var bytes = ms.ToArray();

            // Deserialize with GProtobuf
            var deserialized = Deserializers.DeserializeShortMapTestModel(bytes);

            // Assert
            Assert.NotNull(deserialized);

            // Short as key
            Assert.NotNull(deserialized.ShortStringMap);
            Assert.Equal(original.ShortStringMap.Count, deserialized.ShortStringMap.Count);
            foreach (var kvp in original.ShortStringMap)
            {
                Assert.True(deserialized.ShortStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.ShortStringMap[kvp.Key]);
            }

            // Short as value
            Assert.NotNull(deserialized.StringShortMap);
            Assert.Equal(original.StringShortMap.Count, deserialized.StringShortMap.Count);
            foreach (var kvp in original.StringShortMap)
            {
                Assert.True(deserialized.StringShortMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.StringShortMap[kvp.Key]);
            }

            // UShort as key
            Assert.NotNull(deserialized.UShortStringMap);
            Assert.Equal(original.UShortStringMap.Count, deserialized.UShortStringMap.Count);
            foreach (var kvp in original.UShortStringMap)
            {
                Assert.True(deserialized.UShortStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.UShortStringMap[kvp.Key]);
            }

            // UShort as value
            Assert.NotNull(deserialized.StringUShortMap);
            Assert.Equal(original.StringUShortMap.Count, deserialized.StringUShortMap.Count);
            foreach (var kvp in original.StringUShortMap)
            {
                Assert.True(deserialized.StringUShortMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.StringUShortMap[kvp.Key]);
            }

            // SByte as key
            Assert.NotNull(deserialized.SByteStringMap);
            Assert.Equal(original.SByteStringMap.Count, deserialized.SByteStringMap.Count);
            foreach (var kvp in original.SByteStringMap)
            {
                Assert.True(deserialized.SByteStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.SByteStringMap[kvp.Key]);
            }

            // SByte as value
            Assert.NotNull(deserialized.StringSByteMap);
            Assert.Equal(original.StringSByteMap.Count, deserialized.StringSByteMap.Count);
            foreach (var kvp in original.StringSByteMap)
            {
                Assert.True(deserialized.StringSByteMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized.StringSByteMap[kvp.Key]);
            }
        }

        [Fact]
        public void CrossCompatibility_GProtobufToProtobufNet()
        {
            // Arrange
            var original = CreateTestModel();

            // Act - Serialize with GProtobuf
            var ms = new MemoryStream();
            Serializers.SerializeShortMapTestModel(ms, original);
            var gprotobufBytes = ms.ToArray();

            // Deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<ShortMapTestModel>(
                new MemoryStream(gprotobufBytes));

            // Assert
            Assert.NotNull(protobufNetDeserialized);

            // Short maps
            Assert.NotNull(protobufNetDeserialized.ShortStringMap);
            Assert.Equal(original.ShortStringMap.Count, protobufNetDeserialized.ShortStringMap.Count);

            Assert.NotNull(protobufNetDeserialized.StringShortMap);
            Assert.Equal(original.StringShortMap.Count, protobufNetDeserialized.StringShortMap.Count);

            // UShort maps
            Assert.NotNull(protobufNetDeserialized.UShortStringMap);
            Assert.Equal(original.UShortStringMap.Count, protobufNetDeserialized.UShortStringMap.Count);

            Assert.NotNull(protobufNetDeserialized.StringUShortMap);
            Assert.Equal(original.StringUShortMap.Count, protobufNetDeserialized.StringUShortMap.Count);

            // SByte maps
            Assert.NotNull(protobufNetDeserialized.SByteStringMap);
            Assert.Equal(original.SByteStringMap.Count, protobufNetDeserialized.SByteStringMap.Count);

            Assert.NotNull(protobufNetDeserialized.StringSByteMap);
            Assert.Equal(original.StringSByteMap.Count, protobufNetDeserialized.StringSByteMap.Count);
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
            var gprotobufDeserialized = Deserializers.DeserializeShortMapTestModel(protobufNetBytes);

            // Assert
            Assert.NotNull(gprotobufDeserialized);

            // Short maps
            Assert.NotNull(gprotobufDeserialized.ShortStringMap);
            Assert.Equal(original.ShortStringMap.Count, gprotobufDeserialized.ShortStringMap.Count);
            foreach (var kvp in original.ShortStringMap)
            {
                Assert.True(gprotobufDeserialized.ShortStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.ShortStringMap[kvp.Key]);
            }

            // UShort maps
            Assert.NotNull(gprotobufDeserialized.UShortStringMap);
            Assert.Equal(original.UShortStringMap.Count, gprotobufDeserialized.UShortStringMap.Count);
            foreach (var kvp in original.UShortStringMap)
            {
                Assert.True(gprotobufDeserialized.UShortStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.UShortStringMap[kvp.Key]);
            }

            // SByte maps
            Assert.NotNull(gprotobufDeserialized.SByteStringMap);
            Assert.Equal(original.SByteStringMap.Count, gprotobufDeserialized.SByteStringMap.Count);
            foreach (var kvp in original.SByteStringMap)
            {
                Assert.True(gprotobufDeserialized.SByteStringMap.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, gprotobufDeserialized.SByteStringMap[kvp.Key]);
            }
        }

        [Fact]
        public void EdgeCases_MinMaxValues()
        {
            // Test with min/max values for all types
            var model = new ShortMapTestModel
            {
                ShortStringMap = new Dictionary<short, string>
                {
                    { short.MinValue, "min" },
                    { short.MaxValue, "max" }
                },
                UShortStringMap = new Dictionary<ushort, string>
                {
                    { ushort.MinValue, "min" },
                    { ushort.MaxValue, "max" }
                },
                SByteStringMap = new Dictionary<sbyte, string>
                {
                    { sbyte.MinValue, "min" },
                    { sbyte.MaxValue, "max" }
                }
            };

            // Serialize and deserialize
            var ms = new MemoryStream();
            Serializers.SerializeShortMapTestModel(ms, model);
            var bytes = ms.ToArray();

            var deserialized = Deserializers.DeserializeShortMapTestModel(bytes);

            // Verify
            Assert.Equal("min", deserialized.ShortStringMap[short.MinValue]);
            Assert.Equal("max", deserialized.ShortStringMap[short.MaxValue]);
            Assert.Equal("min", deserialized.UShortStringMap[ushort.MinValue]);
            Assert.Equal("max", deserialized.UShortStringMap[ushort.MaxValue]);
            Assert.Equal("min", deserialized.SByteStringMap[sbyte.MinValue]);
            Assert.Equal("max", deserialized.SByteStringMap[sbyte.MaxValue]);
        }
    }
}
