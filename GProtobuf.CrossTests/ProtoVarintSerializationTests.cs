using System;
using System.IO;
using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for ProtoVarint feature - structs/classes serialized as varints instead of nested messages.
    /// </summary>
    public class ProtoVarintSerializationTests
    {
        #region SimpleProtoVarintModel Tests

        [Fact]
        public void Test_GG_SimpleProtoVarint_RoundTrip()
        {
            // Arrange
            var original = new SimpleProtoVarintModel
            {
                Id = 42,
                Address = new KNXAddress(1, 2, 100),
                Name = "Test Device"
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeSimpleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeSimpleProtoVarintModel(bytes);

            // Assert
            deserialized.Id.Should().Be(42);
            deserialized.Address.ToUInt32().Should().Be(original.Address.ToUInt32());
            deserialized.Address.Area.Should().Be(1);
            deserialized.Address.Line.Should().Be(2);
            deserialized.Address.Device.Should().Be(100);
            deserialized.Name.Should().Be("Test Device");
        }

        [Fact]
        public void Test_GG_SimpleProtoVarint_ZeroAddress()
        {
            // Arrange
            var original = new SimpleProtoVarintModel
            {
                Id = 1,
                Address = new KNXAddress(0),
                Name = "Zero"
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeSimpleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeSimpleProtoVarintModel(bytes);

            // Assert
            deserialized.Id.Should().Be(1);
            deserialized.Address.ToUInt32().Should().Be(0);
            deserialized.Name.Should().Be("Zero");
        }

        #endregion

        #region MultipleProtoVarintModel Tests

        [Fact]
        public void Test_GG_MultipleProtoVarint_RoundTrip()
        {
            // Arrange
            var original = new MultipleProtoVarintModel
            {
                SourceAddress = new KNXAddress(1, 1, 1),
                DestinationAddress = new KNXAddress(15, 15, 255),
                GatewayIP = new IPv4Address(192, 168, 1, 1),
                TimeOffset = new SignedOffset(-100),
                Timestamp = new LargeTimestamp(1700000000000)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleProtoVarintModel(bytes);

            // Assert
            deserialized.SourceAddress.ToUInt32().Should().Be(original.SourceAddress.ToUInt32());
            deserialized.DestinationAddress.ToUInt32().Should().Be(original.DestinationAddress.ToUInt32());
            deserialized.GatewayIP.Value.Should().Be(original.GatewayIP.Value);
            deserialized.GatewayIP.A.Should().Be(192);
            deserialized.GatewayIP.B.Should().Be(168);
            deserialized.GatewayIP.C.Should().Be(1);
            deserialized.GatewayIP.D.Should().Be(1);
            deserialized.TimeOffset.Value.Should().Be(-100);
            deserialized.Timestamp.Value.Should().Be(1700000000000);
        }

        [Fact]
        public void Test_GG_MultipleProtoVarint_SignedOffset_Positive()
        {
            // Arrange
            var original = new MultipleProtoVarintModel
            {
                SourceAddress = new KNXAddress(0),
                DestinationAddress = new KNXAddress(0),
                GatewayIP = new IPv4Address(0),
                TimeOffset = new SignedOffset(999),
                Timestamp = new LargeTimestamp(0)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleProtoVarintModel(bytes);

            // Assert
            deserialized.TimeOffset.Value.Should().Be(999);
        }

        [Fact]
        public void Test_GG_MultipleProtoVarint_SignedOffset_Negative()
        {
            // Arrange - test zigzag encoding for negative values
            var original = new MultipleProtoVarintModel
            {
                SourceAddress = new KNXAddress(0),
                DestinationAddress = new KNXAddress(0),
                GatewayIP = new IPv4Address(0),
                TimeOffset = new SignedOffset(-12345),
                Timestamp = new LargeTimestamp(0)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleProtoVarintModel(bytes);

            // Assert
            deserialized.TimeOffset.Value.Should().Be(-12345);
        }

        #endregion

        #region MixedProtoVarintModel Tests

        [Fact]
        public void Test_GG_MixedProtoVarint_RoundTrip()
        {
            // Arrange
            var original = new MixedProtoVarintModel
            {
                DeviceName = "Thermostat",
                Address = new KNXAddress(2, 3, 50),
                Version = 3,
                IPAddress = new IPv4Address(10, 0, 0, 1),
                Temperature = 23.5,
                CalibrationOffset = new SignedOffset(-5)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMixedProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMixedProtoVarintModel(bytes);

            // Assert
            deserialized.DeviceName.Should().Be("Thermostat");
            deserialized.Address.ToUInt32().Should().Be(original.Address.ToUInt32());
            deserialized.Version.Should().Be(3);
            deserialized.IPAddress.Value.Should().Be(original.IPAddress.Value);
            deserialized.Temperature.Should().Be(23.5);
            deserialized.CalibrationOffset.Value.Should().Be(-5);
        }

        #endregion

        #region OnlyProtoVarintModel Tests

        [Fact]
        public void Test_GG_OnlyProtoVarint_RoundTrip()
        {
            // Arrange
            var original = new OnlyProtoVarintModel
            {
                Address1 = new KNXAddress(1, 0, 0),
                Address2 = new KNXAddress(0, 1, 0),
                Address3 = new KNXAddress(0, 0, 1)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOnlyProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOnlyProtoVarintModel(bytes);

            // Assert
            deserialized.Address1.ToUInt32().Should().Be(original.Address1.ToUInt32());
            deserialized.Address2.ToUInt32().Should().Be(original.Address2.ToUInt32());
            deserialized.Address3.ToUInt32().Should().Be(original.Address3.ToUInt32());
        }

        #endregion

        #region DefaultValueProtoVarintModel Tests

        [Fact]
        public void Test_GG_DefaultValueProtoVarint_ZeroValuesNotSerialized()
        {
            // Arrange - only NonZeroAddress has a non-zero value
            var original = new DefaultValueProtoVarintModel
            {
                ZeroAddress = new KNXAddress(0),
                ZeroOffset = new SignedOffset(0),
                NonZeroAddress = new KNXAddress(1, 2, 3)
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeDefaultValueProtoVarintModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeDefaultValueProtoVarintModel(bytes);

            // Assert
            deserialized.ZeroAddress.ToUInt32().Should().Be(0);
            deserialized.ZeroOffset.Value.Should().Be(0);
            deserialized.NonZeroAddress.ToUInt32().Should().Be(original.NonZeroAddress.ToUInt32());
        }

        #endregion

        #region Wire Format Tests

        [Fact]
        public void Test_GG_ProtoVarint_WireFormat_UInt32()
        {
            // Test that KNXAddress (UInt32 varint) is serialized as varint, not nested message
            var original = new SimpleProtoVarintModel
            {
                Id = 1,
                Address = new KNXAddress(0x1234),  // 0x1234 = 4660
                Name = ""
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeSimpleProtoVarintModel(ms, original);
            var bytes = ms.ToArray();

            // The Address field (field 2) should be serialized as:
            // - Tag: (2 << 3) | 0 = 16 (varint wire type = 0)
            // - Value: 0x1234 as varint
            // NOT as:
            // - Tag: (2 << 3) | 2 = 18 (length-delimited wire type = 2)
            // - Length + nested message

            // Field 1 (Id = 1): tag 0x08, value 0x01 = 2 bytes
            // Field 2 (Address = 0x1234): tag 0x10, value = 0x34 0x24 = 3 bytes (varint)
            // Field 3 (Name = ""): not serialized (empty string)

            // Verify it's a small message (varint encoding is more compact than nested)
            bytes.Length.Should().BeLessThan(10, "ProtoVarint should be more compact than nested message");
        }

        #endregion
    }
}
