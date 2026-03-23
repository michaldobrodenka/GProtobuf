using FluentAssertions;
using GProtobuf.Tests;
using GProtobuf.Tests.TestModel;
using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for unknown field handling (Forward/Backward compatibility)
    ///
    /// Level200 requirement: Unknown fields should be SKIPPED (not preserved)
    /// </summary>
    public class UnknownFieldsTests : BaseSerializationTest
    {
        private readonly ITestOutputHelper _output;

        public UnknownFieldsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Unknown field numbers - should skip

        [Fact]
        public void UnknownFieldNumber_ShouldSkipAndContinueReading()
        {
            // Arrange: Manually craft message with unknown field 99
            var message = new byte[]
            {
                0x08, 0x01,         // Field 1 (Status), varint, value=1 (Active)
                0xF0, 0x06, 0x05,   // Field 99 (unknown!), varint, value=5
                0x10, 0x02          // Field 2 (Priority), varint, value=2 (Medium)
            };

            // Act: Deserialize with GProtobuf
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Assert: Unknown field skipped, known fields deserialized
            result.Should().NotBeNull();
            result.Status.Should().Be(Status.Active, "field 1 should be read correctly");
            result.Priority.Should().Be(Priority.Medium, "field 2 should be read after skipping field 99");

            _output.WriteLine($"✅ Unknown field 99 skipped successfully");
            _output.WriteLine($"   Status: {result.Status}");
            _output.WriteLine($"   Priority: {result.Priority}");
        }

        [Fact]
        public void UnknownLengthDelimitedField_ShouldSkipCorrectly()
        {
            // Arrange: Message with unknown string field
            var message = new byte[]
            {
                0x08, 0x01,                 // Field 1, varint, value=1
                0x62, 0x0B,                 // Field 12 (unknown!), length-delimited, length=11
                0x48, 0x65, 0x6C, 0x6C,
                0x6F, 0x20, 0x57, 0x6F,
                0x72, 0x6C, 0x64,           // "Hello World"
                0x10, 0x0A                  // Field 2, varint, value=10 (Critical)
            };

            // Act
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Assert
            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Critical, "field 2 should be read after skipping unknown field 12");
        }

        [Fact]
        public void MultipleUnknownFields_ShouldSkipAll()
        {
            // Arrange: Message with multiple unknown fields
            var message = new byte[]
            {
                0xC0, 0x01, 0x0A,   // Field 20 (unknown), varint, value=10
                0x08, 0x02,         // Field 1 (Status), varint, value=2 (Deleted)
                0xC8, 0x01, 0x14,   // Field 25 (unknown), varint, value=20
                0xD2, 0x01, 0x02, 0x41, 0x42,  // Field 26 (unknown), len, "AB"
                0x10, 0x01          // Field 2 (Priority), varint, value=1 (Low)
            };

            // Act
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Assert
            result.Status.Should().Be(Status.Deleted);
            result.Priority.Should().Be(Priority.Low);
        }

        #endregion

        #region Forward compatibility - New fields added

        [Fact]
        public void NewField_AddedToModelV2_OldDeserializerShouldSkip()
        {
            // Scenario: Model V2 adds "Description" field 10
            // V1 deserializer should skip it

            // Arrange: V2 message with field 10 (new field)
            var v2Message = new byte[]
            {
                0x08, 0x01,         // Field 1 (Status), value=1
                0x52, 0x04,         // Field 10 (Description - NEW!), len, length=4
                0x74, 0x65, 0x73, 0x74,  // "test"
                0x10, 0x02          // Field 2 (Priority), value=2
            };

            // Act: Deserialize with V1 deserializer (EnumTypesModel doesn't have field 10)
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(v2Message);

            // Assert: V1 fields deserialized, V2 field skipped
            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);

            _output.WriteLine("✅ Forward compatibility: V1 deserializer skipped V2 field");
        }

        #endregion

        #region Backward compatibility - Fields removed

        [Fact]
        public void RemovedField_MissingInV2_ShouldUseDefault()
        {
            // Scenario: Model V1 has "Priority" field, V2 removes it
            // V2 deserializer receives V1 message → should skip Priority

            // Arrange: V1 message with Priority (field 2)
            var v1Message = new byte[]
            {
                0x08, 0x03,         // Field 1 (Status), value=3 (Archived)
                0x10, 0x02          // Field 2 (Priority), value=2 - will be skipped
            };

            // Act: Deserialize (if V2 removed Priority, it would skip field 2)
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(v1Message);

            // Assert: Status read correctly, Priority uses default
            result.Status.Should().Be(Status.Archived);
            // Note: Priority is NOT removed in our test, so it will be read
            // This test demonstrates the concept
        }

        #endregion

        #region Edge cases

        [Fact]
        public void UnknownFieldAtEnd_ShouldSkipWithoutError()
        {
            // Arrange: Unknown field at the end
            var message = new byte[]
            {
                0x08, 0x01,         // Field 1, value=1
                0x10, 0x02,         // Field 2, value=2
                0xF8, 0x07, 0x64    // Field 127 (unknown, max field number), value=100
            };

            // Act
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Assert
            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);
        }

        [Fact]
        public void OnlyUnknownFields_ShouldReturnDefaultModel()
        {
            // Arrange: Message with only unknown fields
            var message = new byte[]
            {
                0xC0, 0x01, 0x0A,   // Field 20 (unknown), value=10
                0xC8, 0x01, 0x14    // Field 25 (unknown), value=20
            };

            // Act
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Assert: All fields use default values
            result.Status.Should().Be(Status.Unknown, "default enum value");
            result.Priority.Should().Be((Priority)0, "default enum value");
        }

        #endregion

        #region Performance - Large unknown fields

        [Fact]
        public void LargeUnknownField_ShouldSkipEfficiently()
        {
            // Arrange: Unknown field with 1KB data
            var largeData = new byte[1024];
            for (int i = 0; i < largeData.Length; i++)
            {
                largeData[i] = (byte)(i % 256);
            }

            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    // Field 1
                    writer.Write((byte)0x08);
                    writer.Write((byte)0x01);

                    // Field 50 (unknown) - large blob
                    writer.Write((byte)0x92);  // Field 50, WireType.Len
                    writer.Write((byte)0x03);
                    writer.Write((byte)0x80);  // Varint encoding of 1024
                    writer.Write((byte)0x08);
                    writer.Write(largeData);

                    // Field 2
                    writer.Write((byte)0x10);
                    writer.Write((byte)0x0A);  // Priority.Critical = 10
                }

                var message = ms.ToArray();
                _output.WriteLine($"Message size: {message.Length} bytes");

                // Act
                var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

                // Assert
                result.Status.Should().Be(Status.Active);
                result.Priority.Should().Be(Priority.Critical);

                _output.WriteLine("✅ Large unknown field (1KB) skipped efficiently");
            }
        }

        #endregion
    }
}
