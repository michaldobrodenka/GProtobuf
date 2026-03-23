using GProtobuf.Core;
using GProtobuf.CrossTests.TestModel;
using TestModelSerializers = GProtobuf.CrossTests.TestModel.Serialization.Deserializers;
using GeneratedSerializers = GProtobuf.Generated.Serialization.Deserializers;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

// Register standalone serializers for nested generic types
[assembly: GenerateSerializer(typeof(GenericCcu[]))]
[assembly: GenerateSerializer(typeof(Dictionary<int, List<DeviceActionType>>))]
[assembly: GenerateSerializer(typeof(Dictionary<string, DeviceAggregation[]>))]
[assembly: GenerateSerializer(typeof(Dictionary<DeviceValueType, List<DeviceActionType>>))]

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for nested generic type serialization.
    /// </summary>
    public class NestedGenericSerializerTests
    {
        /// <summary>
        /// Test array of custom types: GenericCcu[]
        /// </summary>
        [Fact]
        public void GenericCcuArray_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var original = new GenericCcu[]
            {
                new GenericCcu { CcuId = 1, CcuName = "CCU-Alpha" },
                new GenericCcu { CcuId = 2, CcuName = "CCU-Beta" },
                new GenericCcu { CcuId = 3, CcuName = "CCU-Gamma" }
            };

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = TestModelSerializers.DeserializeArrayOfGenericCcu(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Length, deserialized.Length);
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i].CcuId, deserialized[i].CcuId);
                Assert.Equal(original[i].CcuName, deserialized[i].CcuName);
            }
        }

        /// <summary>
        /// Test Dictionary<int, List<CustomType>>: primitive key, list of custom types value
        /// </summary>
        [Fact]
        public void DictionaryIntListCustomType_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var original = new Dictionary<int, List<DeviceActionType>>
            {
                { 1, new List<DeviceActionType>
                    {
                        new DeviceActionType { ActionId = 10, ActionName = "Action-A", ActionValue = 1.5 },
                        new DeviceActionType { ActionId = 11, ActionName = "Action-B", ActionValue = 2.5 }
                    }
                },
                { 2, new List<DeviceActionType>
                    {
                        new DeviceActionType { ActionId = 20, ActionName = "Action-C", ActionValue = 3.5 }
                    }
                }
            };

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = GeneratedSerializers.DeserializeDictionaryOfInt32AndListOfDeviceActionType(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value.Count, deserialized[kvp.Key].Count);
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    Assert.Equal(kvp.Value[i].ActionId, deserialized[kvp.Key][i].ActionId);
                    Assert.Equal(kvp.Value[i].ActionName, deserialized[kvp.Key][i].ActionName);
                    Assert.Equal(kvp.Value[i].ActionValue, deserialized[kvp.Key][i].ActionValue, 4);
                }
            }
        }

        /// <summary>
        /// Test Dictionary<string, CustomType[]>: string key, array of custom types value
        /// </summary>
        [Fact]
        public void DictionaryStringArrayCustomType_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var original = new Dictionary<string, DeviceAggregation[]>
            {
                { "group-1", new DeviceAggregation[]
                    {
                        new DeviceAggregation { AggId = 100, AggName = "Agg-X" },
                        new DeviceAggregation { AggId = 101, AggName = "Agg-Y" }
                    }
                },
                { "group-2", new DeviceAggregation[]
                    {
                        new DeviceAggregation { AggId = 200, AggName = "Agg-Z" }
                    }
                }
            };

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = GeneratedSerializers.DeserializeDictionaryOfStringAndArrayOfDeviceAggregation(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value.Length, deserialized[kvp.Key].Length);
                for (int i = 0; i < kvp.Value.Length; i++)
                {
                    Assert.Equal(kvp.Value[i].AggId, deserialized[kvp.Key][i].AggId);
                    Assert.Equal(kvp.Value[i].AggName, deserialized[kvp.Key][i].AggName);
                }
            }
        }

        /// <summary>
        /// Test Dictionary<CustomType, List<CustomType>>: custom key, list of custom types value
        /// </summary>
        [Fact]
        public void DictionaryCustomKeyListCustomValue_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var key1 = new DeviceValueType { TypeId = 1, TypeName = "Type-A" };
            var key2 = new DeviceValueType { TypeId = 2, TypeName = "Type-B" };

            var original = new Dictionary<DeviceValueType, List<DeviceActionType>>
            {
                { key1, new List<DeviceActionType>
                    {
                        new DeviceActionType { ActionId = 10, ActionName = "Action-1", ActionValue = 1.1 },
                        new DeviceActionType { ActionId = 11, ActionName = "Action-2", ActionValue = 2.2 }
                    }
                },
                { key2, new List<DeviceActionType>
                    {
                        new DeviceActionType { ActionId = 20, ActionName = "Action-3", ActionValue = 3.3 }
                    }
                }
            };

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = GeneratedSerializers.DeserializeDictionaryOfDeviceValueTypeAndListOfDeviceActionType(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
        }

        /// <summary>
        /// Test empty array of custom types
        /// </summary>
        [Fact]
        public void EmptyCustomTypeArray_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var original = new GenericCcu[0];

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = TestModelSerializers.DeserializeArrayOfGenericCcu(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Empty(deserialized);
        }

        /// <summary>
        /// Test empty dictionary with list value
        /// </summary>
        [Fact]
        public void EmptyDictionaryWithListValue_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var original = new Dictionary<int, List<DeviceActionType>>();

            // Act - Serialize with protobuf-net
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, original);
                bytes = ms.ToArray();
            }

            // Deserialize with GProtobuf
            var deserialized = GeneratedSerializers.DeserializeDictionaryOfInt32AndListOfDeviceActionType(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Empty(deserialized);
        }
    }
}
