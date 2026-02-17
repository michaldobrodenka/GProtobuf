using System;
using System.IO;
using FluentAssertions;
using ProtoBuf;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests to verify wire format compatibility between GProtobuf custom buffer
    /// serialization and protobuf-net standard byte[] serialization.
    ///
    /// Key insight: Custom buffer fields use WireType.Len (length-prefixed bytes),
    /// which is the same wire format as standard byte[] fields in protobuf-net.
    /// This means protobuf-net can read custom buffer data if we define a compatible type.
    /// </summary>
    public class CustomBufferCompatibilityTests
    {
        #region Protobuf-net compatible models

        /// <summary>
        /// Protobuf-net model that mirrors BasicCustomBufferModel structure.
        /// Field 10 is defined as byte[] to match the custom buffer wire format.
        /// </summary>
        [ProtoContract]
        public class ProtobufNetCompatibleModel
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(10)]
            public byte[] CustomData { get; set; }
        }

        /// <summary>
        /// Protobuf-net model for multiple custom buffers.
        /// </summary>
        [ProtoContract]
        public class ProtobufNetMultiBufferModel
        {
            [ProtoMember(1)]
            public int Version { get; set; }

            [ProtoMember(5)]
            public byte[] HeaderData { get; set; }

            [ProtoMember(10)]
            public byte[] PayloadData { get; set; }

            [ProtoMember(15)]
            public byte[] FooterData { get; set; }
        }

        #endregion

        #region GProtobuf -> Protobuf-net (GP tests)

        [Fact]
        public void Test_GP_BasicCustomBuffer_ProtobufNetCanRead()
        {
            // Arrange - create and serialize with GProtobuf
            var gprotobufModel = new TestModel.BasicCustomBufferModel
            {
                Id = 42,
                Name = "Test",
                CustomData = new byte[] { 1, 2, 3, 4, 5 }
            };

            // Act - serialize with GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, gprotobufModel);
            var bytes = ms.ToArray();

            // Deserialize with protobuf-net
            ms.Position = 0;
            var protobufNetModel = Serializer.Deserialize<ProtobufNetCompatibleModel>(ms);

            // Assert - protobuf-net should read the same data
            protobufNetModel.Id.Should().Be(42);
            protobufNetModel.Name.Should().Be("Test");
            protobufNetModel.CustomData.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
        }

        [Fact]
        public void Test_GP_MultipleCustomBuffers_ProtobufNetCanRead()
        {
            // Arrange - create and serialize with GProtobuf
            var gprotobufModel = new TestModel.MultipleCustomBufferModel
            {
                Version = 1,
                HeaderData = new byte[] { 0xAA, 0xBB },
                PayloadData = new byte[] { 0x01, 0x02, 0x03 },
                FooterData = new byte[] { 0xFF }
            };

            // Act - serialize with GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleCustomBufferModel(ms, gprotobufModel);
            var bytes = ms.ToArray();

            // Deserialize with protobuf-net
            ms.Position = 0;
            var protobufNetModel = Serializer.Deserialize<ProtobufNetMultiBufferModel>(ms);

            // Assert
            protobufNetModel.Version.Should().Be(1);
            protobufNetModel.HeaderData.Should().BeEquivalentTo(new byte[] { 0xAA, 0xBB });
            protobufNetModel.PayloadData.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03 });
            protobufNetModel.FooterData.Should().BeEquivalentTo(new byte[] { 0xFF });
        }

        [Fact]
        public void Test_GP_EmptyCustomBuffer_ProtobufNetCanRead()
        {
            // Arrange
            var gprotobufModel = new TestModel.BasicCustomBufferModel
            {
                Id = 100,
                Name = "Empty",
                CustomData = Array.Empty<byte>()
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, gprotobufModel);

            ms.Position = 0;
            var protobufNetModel = Serializer.Deserialize<ProtobufNetCompatibleModel>(ms);

            // Assert
            protobufNetModel.Id.Should().Be(100);
            protobufNetModel.Name.Should().Be("Empty");
            // Empty byte[] serializes as length=0, protobuf-net reads as empty array
            protobufNetModel.CustomData.Should().BeEmpty();
        }

        [Fact]
        public void Test_GP_LargeCustomBuffer_ProtobufNetCanRead()
        {
            // Arrange - 10KB of data
            var largeData = new byte[10 * 1024];
            new Random(42).NextBytes(largeData);

            var gprotobufModel = new TestModel.LargeCustomBufferModel
            {
                Tag = "Large",
                LargeData = largeData
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLargeCustomBufferModel(ms, gprotobufModel);

            ms.Position = 0;
            // Use a compatible model for protobuf-net
            var protobufNetModel = Serializer.Deserialize<LargeDataProtobufNetModel>(ms);

            // Assert
            protobufNetModel.Tag.Should().Be("Large");
            protobufNetModel.LargeData.Should().BeEquivalentTo(largeData);
        }

        [ProtoContract]
        public class LargeDataProtobufNetModel
        {
            [ProtoMember(1)]
            public string Tag { get; set; }

            [ProtoMember(100)]
            public byte[] LargeData { get; set; }
        }

        #endregion

        #region Protobuf-net -> GProtobuf (PG tests)

        [Fact]
        public void Test_PG_ProtobufNetSerialize_GProtobufCanRead()
        {
            // Arrange - serialize with protobuf-net
            var protobufNetModel = new ProtobufNetCompatibleModel
            {
                Id = 99,
                Name = "FromProtobufNet",
                CustomData = new byte[] { 10, 20, 30, 40, 50 }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, protobufNetModel);
            var bytes = ms.ToArray();

            // Act - deserialize with GProtobuf
            var gprotobufModel = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            gprotobufModel.Id.Should().Be(99);
            gprotobufModel.Name.Should().Be("FromProtobufNet");
            gprotobufModel.CustomData.Should().BeEquivalentTo(new byte[] { 10, 20, 30, 40, 50 });
        }

        [Fact]
        public void Test_PG_MultipleBuffers_ProtobufNetSerialize_GProtobufCanRead()
        {
            // Arrange
            var protobufNetModel = new ProtobufNetMultiBufferModel
            {
                Version = 2,
                HeaderData = new byte[] { 0x11, 0x22 },
                PayloadData = new byte[] { 0x33, 0x44, 0x55 },
                FooterData = new byte[] { 0x66, 0x77, 0x88, 0x99 }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, protobufNetModel);
            var bytes = ms.ToArray();

            // Act
            var gprotobufModel = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleCustomBufferModel(bytes);

            // Assert
            gprotobufModel.Version.Should().Be(2);
            gprotobufModel.HeaderData.Should().BeEquivalentTo(new byte[] { 0x11, 0x22 });
            gprotobufModel.PayloadData.Should().BeEquivalentTo(new byte[] { 0x33, 0x44, 0x55 });
            gprotobufModel.FooterData.Should().BeEquivalentTo(new byte[] { 0x66, 0x77, 0x88, 0x99 });
        }

        #endregion

        #region Wire Format Analysis

        [Fact]
        public void Test_WireFormat_CustomBufferMatchesByteArray()
        {
            // This test verifies that custom buffer serialization produces
            // the exact same wire format as standard byte[] serialization

            // Arrange
            var customData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            // Serialize with GProtobuf (custom buffer)
            var gprotobufModel = new TestModel.BasicCustomBufferModel
            {
                Id = 1,
                Name = "A",
                CustomData = customData
            };

            using var msGProtobuf = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(msGProtobuf, gprotobufModel);
            var gprotobufBytes = msGProtobuf.ToArray();

            // Serialize with protobuf-net (standard byte[])
            var protobufNetModel = new ProtobufNetCompatibleModel
            {
                Id = 1,
                Name = "A",
                CustomData = customData
            };

            using var msProtobufNet = new MemoryStream();
            Serializer.Serialize(msProtobufNet, protobufNetModel);
            var protobufNetBytes = msProtobufNet.ToArray();

            // Assert - wire formats should be identical
            gprotobufBytes.Should().BeEquivalentTo(protobufNetBytes,
                "Custom buffer should produce identical wire format to standard byte[] serialization");
        }

        [Fact]
        public void Test_WireFormat_PrintComparison()
        {
            // Debug test to visualize wire format differences (if any)
            var customData = new byte[] { 0x01, 0x02, 0x03 };

            // GProtobuf
            var gprotobufModel = new TestModel.BasicCustomBufferModel
            {
                Id = 42,
                CustomData = customData
            };

            using var msG = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(msG, gprotobufModel);
            var gBytes = msG.ToArray();

            // Protobuf-net
            var pnModel = new ProtobufNetCompatibleModel
            {
                Id = 42,
                CustomData = customData
            };

            using var msP = new MemoryStream();
            Serializer.Serialize(msP, pnModel);
            var pBytes = msP.ToArray();

            // Output for debugging (visible in test output)
            var gHex = BitConverter.ToString(gBytes);
            var pHex = BitConverter.ToString(pBytes);

            // Both should serialize field 1 (Id=42) and field 10 (CustomData)
            // Field 1: tag=0x08 (field 1, varint), value=0x2A (42)
            // Field 10: tag=0x52 (field 10, length-delimited), length=3, data=01-02-03

            gBytes.Should().BeEquivalentTo(pBytes);
        }

        #endregion
    }
}
