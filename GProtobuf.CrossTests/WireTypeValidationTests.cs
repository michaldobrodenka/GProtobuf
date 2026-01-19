using FluentAssertions;
using GProtobuf.Core;
using GProtobuf.Tests.TestModel;
using System;
using System.IO;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Level200 wire type validation.
    /// Verifies that malformed input with wrong wire types is handled gracefully.
    /// </summary>
    public class WireTypeValidationTests
    {
        [Fact]
        public void WireTypeValidation_IntField_WrongWireType_ShouldSkipField()
        {
            // Arrange: Craft malformed input
            // Field 5 (IntValue) should be Varint, but we send it as LengthDelimited
            var malformed = new byte[]
            {
                0x2A,       // Field 5, WireType.Len (wrong! should be Varint)
                0x02,       // Length = 2
                0x01, 0x02  // Some bytes
            };

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Field should be skipped, value should remain default
            deserialized.Should().NotBeNull();
            deserialized.IntValue.Should().Be(0); // Default value, field was skipped
        }

        [Fact]
        public void WireTypeValidation_StringField_WrongWireType_ShouldSkipField()
        {
            // Arrange: Craft malformed input
            // Field 12 (StringValue) should be LengthDelimited, but we send it as Varint
            var malformed = new byte[]
            {
                0x60,       // Field 12, WireType.Varint (wrong! should be Len)
                0x7F        // Some varint value
            };

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Field should be skipped, value should remain default
            deserialized.Should().NotBeNull();
            deserialized.StringValue.Should().BeNull(); // Default value, field was skipped
        }

        [Fact]
        public void WireTypeValidation_FloatField_WrongWireType_ShouldSkipField()
        {
            // Arrange: Craft malformed input
            // Field 9 (FloatValue) should be Fixed32, but we send it as Varint
            var malformed = new byte[]
            {
                0x48,       // Field 9, WireType.Varint (wrong! should be Fixed32)
                0x64        // Some varint value
            };

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Field should be skipped, value should remain default
            deserialized.Should().NotBeNull();
            deserialized.FloatValue.Should().Be(0.0f); // Default value, field was skipped
        }

        [Fact]
        public void WireTypeValidation_DoubleField_WrongWireType_ShouldSkipField()
        {
            // Arrange: Craft malformed input
            // Field 10 (DoubleValue) should be Fixed64, but we send it as Varint
            var malformed = new byte[]
            {
                0x50,       // Field 10, WireType.Varint (wrong! should be Fixed64)
                0x64        // Some varint value
            };

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Field should be skipped, value should remain default
            deserialized.Should().NotBeNull();
            deserialized.DoubleValue.Should().Be(0.0); // Default value, field was skipped
        }

        [Fact]
        public void WireTypeValidation_MultipleWrongFields_ShouldSkipAll()
        {
            // Arrange: Craft input with multiple wrong wire types
            var malformed = new byte[]
            {
                0x2A, 0x02, 0x01, 0x02,  // Field 5, wrong type
                0x60, 0x7F,               // Field 12, wrong type
                0x28, 0x0A                // Field 5, correct type (IntValue = 10)
            };

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Wrong fields skipped, correct field read
            deserialized.Should().NotBeNull();
            deserialized.IntValue.Should().Be(10); // Last correct value wins
            deserialized.StringValue.Should().BeNull(); // Skipped
        }

        [Fact]
        public void WireTypeValidation_ValidMessage_ShouldDeserializeNormally()
        {
            // Arrange: Create valid message
            var original = new BasicTypesModel
            {
                IntValue = 42,
                StringValue = "test",
                FloatValue = 3.14f,
                DoubleValue = 2.71
            };

            byte[] serialized;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeBasicTypesModel(ms, original);
                serialized = ms.ToArray();
            }

            // Act: Deserialize
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(serialized);

            // Assert: Should deserialize correctly
            deserialized.Should().NotBeNull();
            deserialized.IntValue.Should().Be(42);
            deserialized.StringValue.Should().Be("test");
            deserialized.FloatValue.Should().BeApproximately(3.14f, 0.001f);
            deserialized.DoubleValue.Should().BeApproximately(2.71, 0.001);
        }

        [Fact]
        public void WireTypeValidation_ComplexType_WrongWireType_ShouldSkipField()
        {
            // Arrange: Nested message should use LengthDelimited, but we send Varint
            var malformed = new byte[]
            {
                0x08, 0x2A,  // Field 1 (IntValue) = 42, correct
                // Field 2 would be nested message, we send wrong wire type
                0x10, 0x64   // Field 2, WireType.Varint (wrong! nested should be Len)
            };

            // Act: This should not crash, just skip the malformed field
            Action act = () => global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(malformed);

            // Assert: Should not throw
            act.Should().NotThrow();
        }
    }
}
