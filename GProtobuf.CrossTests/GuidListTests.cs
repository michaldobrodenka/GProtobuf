using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GProtobuf.CrossTests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class GuidListTests
    {
        [Fact]
        public void SerializeDeserialize_ListOfGuids()
        {
            // Arrange
            var original = new GuidMapTestModel
            {
                GuidList = new List<Guid>
                {
                    Guid.Empty,
                    new Guid("12345678-1234-1234-1234-123456789abc"),
                    new Guid("87654321-4321-4321-4321-cba987654321"),
                    new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    new Guid("12345678-1234-1234-1234-123456789abc") // Duplicate to test List allows duplicates
                }
            };

            // Act - serialize
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeGuidMapTestModel(stream, original);
            var bytes = stream.ToArray();

            // Act - deserialize
            var deserialized = TestModel.Serialization.Deserializers.DeserializeGuidMapTestModel(bytes);

            // Assert
            Assert.NotNull(deserialized.GuidList);
            Assert.Equal(5, deserialized.GuidList.Count); // List allows duplicates
            Assert.Equal(original.GuidList, deserialized.GuidList);
            
            // Verify order is preserved (List maintains order)
            for (int i = 0; i < original.GuidList.Count; i++)
            {
                Assert.Equal(original.GuidList[i], deserialized.GuidList[i]);
            }
        }

        [Fact]
        public void CrossCompatibility_GProtobufToProtobufNet_ListOfGuids()
        {
            // Arrange
            var original = new GuidMapTestModel
            {
                GuidList = new List<Guid>
                {
                    new Guid("11111111-2222-3333-4444-555555555555"),
                    new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    Guid.Empty,
                    new Guid("11111111-2222-3333-4444-555555555555") // Duplicate
                }
            };

            // Act - serialize with GProtobuf
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeGuidMapTestModel(stream, original);
            var gprotobufBytes = stream.ToArray();

            // Act - deserialize with protobuf-net
            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<GuidMapTestModel>(new MemoryStream(gprotobufBytes));

            // Assert
            Assert.NotNull(protobufNetDeserialized.GuidList);
            Assert.Equal(original.GuidList.Count, protobufNetDeserialized.GuidList.Count);
            
            // Verify all items and order
            for (int i = 0; i < original.GuidList.Count; i++)
            {
                Assert.Equal(original.GuidList[i], protobufNetDeserialized.GuidList[i]);
            }
        }

        [Fact]
        public void CrossCompatibility_ProtobufNetToGProtobuf_ListOfGuids()
        {
            // Arrange
            var original = new GuidMapTestModel
            {
                GuidList = new List<Guid>
                {
                    new Guid("87654321-4321-4321-4321-cba987654321"),
                    Guid.Empty,
                    new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    Guid.Empty // Can have multiple Empty Guids in List
                }
            };

            // Act - serialize with protobuf-net
            var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, original);
            var protobufNetBytes = stream.ToArray();

            // Act - deserialize with GProtobuf
            var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeGuidMapTestModel(protobufNetBytes);

            // Assert
            Assert.NotNull(gprotobufDeserialized.GuidList);
            Assert.Equal(original.GuidList.Count, gprotobufDeserialized.GuidList.Count);
            
            // Verify all items and order
            for (int i = 0; i < original.GuidList.Count; i++)
            {
                Assert.Equal(original.GuidList[i], gprotobufDeserialized.GuidList[i]);
            }
        }

        [Fact]
        public void EmptyList_SerializesCorrectly()
        {
            // Arrange
            var original = new GuidMapTestModel
            {
                GuidList = new List<Guid>() // Empty list
            };

            // Act - serialize
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeGuidMapTestModel(stream, original);
            var bytes = stream.ToArray();

            // Act - deserialize
            var deserialized = TestModel.Serialization.Deserializers.DeserializeGuidMapTestModel(bytes);

            // Assert - protobuf doesn't distinguish between null and empty list
            // Empty list can deserialize as null
            Assert.True(deserialized.GuidList == null || deserialized.GuidList.Count == 0);
        }

        [Fact]
        public void NullList_HandledCorrectly()
        {
            // Arrange
            var original = new GuidMapTestModel
            {
                GuidList = null
            };

            // Act - serialize
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeGuidMapTestModel(stream, original);
            var bytes = stream.ToArray();

            // Act - deserialize
            var deserialized = TestModel.Serialization.Deserializers.DeserializeGuidMapTestModel(bytes);

            // Assert - null or empty list after deserialization is acceptable
            // (protobuf doesn't distinguish between null and empty)
            Assert.True(deserialized.GuidList == null || deserialized.GuidList.Count == 0);
        }
    }
}