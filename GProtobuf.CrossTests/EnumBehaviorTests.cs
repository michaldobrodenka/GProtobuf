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
    /// Tests to verify enum behavior in GProtobuf and protobuf-net 2.3.7
    ///
    /// Key questions to answer:
    /// 1. Does enum with value=0 serialize on wire? (Proto2 spec: NO)
    /// 2. How are unknown enum values handled during deserialization?
    /// 3. How does protobuf-net 2.3.7 handle unknown enum values?
    /// 4. Are enum arrays packed or unpacked by default?
    /// </summary>
    public class EnumBehaviorTests : BaseSerializationTest
    {
        private readonly ITestOutputHelper _output;

        public EnumBehaviorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Question 1: Default value (0) serialization

        [Fact]
        public void EnumDefaultValue_ShouldNotSerialize_ProtobufNetBehavior()
        {
            // Arrange: Model with default enum value (Status.Unknown = 0)
            var model = new EnumTypesModel
            {
                Status = Status.Unknown // = 0, should NOT serialize
            };

            // Act: Serialize with protobuf-net
            byte[] protobufNetData;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, model);
                protobufNetData = ms.ToArray();
            }

            _output.WriteLine($"ProtobufNet serialized bytes (Status.Unknown=0): {BitConverter.ToString(protobufNetData)}");

            // Assert: Empty data (proto2 default values are NOT serialized)
            protobufNetData.Should().BeEmpty("proto2 spec: default enum value (0) should NOT serialize");
        }

        [Fact]
        public void EnumDefaultValue_ShouldNotSerialize_GProtobufBehavior()
        {
            // Arrange: Model with default enum value
            var model = new EnumTypesModel
            {
                Status = Status.Unknown // = 0
            };

            // Act: Serialize with GProtobuf
            byte[] gprotobufData;
            using (var ms = new MemoryStream())
            {
                try
                {
                    global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeEnumTypesModel(ms, model);
                    gprotobufData = ms.ToArray();

                    _output.WriteLine($"GProtobuf serialized bytes (Status.Unknown=0): {BitConverter.ToString(gprotobufData)}");

                    // Assert: Should match protobuf-net behavior (empty)
                    gprotobufData.Should().BeEmpty("GProtobuf should match proto2 behavior: default=0 not serialized");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"❌ GProtobuf serialization failed: {ex.Message}");
                    throw new Exception($"ENUM NOT IMPLEMENTED YET in GProtobuf? {ex.Message}", ex);
                }
            }
        }

        [Fact]
        public void EnumNonDefaultValue_ShouldSerialize()
        {
            // Arrange
            var model = new EnumTypesModel
            {
                Status = Status.Active // = 1, should serialize
            };

            // Act: protobuf-net
            byte[] protobufNetData;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, model);
                protobufNetData = ms.ToArray();
            }

            _output.WriteLine($"ProtobufNet bytes (Status.Active=1): {BitConverter.ToString(protobufNetData)}");

            // Assert: Should have data
            protobufNetData.Should().NotBeEmpty("non-default enum value should serialize");

            // Expected wire format:
            // Field 1, WireType.Varint = 0x08
            // Value 1 (Active) = 0x01
            protobufNetData.Should().Equal(new byte[] { 0x08, 0x01 });
        }

        #endregion

        #region Question 2: Unknown enum values (Evolution)

        [Fact]
        public void UnknownEnumValue_ProtobufNetBehavior()
        {
            // Arrange: Manually craft message with enum value=99 (not in Status enum)
            var malformed = new byte[]
            {
                0x08, 0x63  // Field 1 (Status), WireType.Varint, value=99 (unknown!)
            };

            // Act: Deserialize with protobuf-net
            EnumTypesModel deserialized;
            using (var ms = new MemoryStream(malformed))
            {
                deserialized = ProtoBuf.Serializer.Deserialize<EnumTypesModel>(ms);
            }

            _output.WriteLine($"ProtobufNet deserialized unknown enum value: {deserialized.Status} = {(int)deserialized.Status}");

            // Assert: protobuf-net PRESERVES unknown enum values as raw int
            ((int)deserialized.Status).Should().Be(99, "protobuf-net 2.3.7 preserves unknown enum values");
        }

        [Fact]
        public void UnknownEnumValue_GProtobufBehavior()
        {
            // Arrange: Manually craft message with enum value=99
            var malformed = new byte[]
            {
                0x08, 0x63  // Field 1 (Status), value=99
            };

            // Act: Deserialize with GProtobuf
            try
            {
                var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(malformed);

                _output.WriteLine($"GProtobuf deserialized unknown enum: {deserialized.Status} = {(int)deserialized.Status}");

                // Assert: Should match protobuf-net behavior (preserve as int)
                ((int)deserialized.Status).Should().Be(99, "GProtobuf should preserve unknown enum values like protobuf-net");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ GProtobuf failed to deserialize unknown enum: {ex.Message}");
                throw new Exception($"ENUM HANDLING NOT IMPLEMENTED? {ex.Message}", ex);
            }
        }

        #endregion

        #region Question 3: Enum arrays - NOT YET SUPPORTED

        // TODO: Enum arrays not supported yet - generator doesn't create ReadEnumContent() methods
        // Need to implement enum array handling in generator before enabling these tests

        // [Fact]
        // public void EnumArray_DefaultEncoding_ProtobufNetBehavior()
        // {
        //     // Arrange
        //     var model = new EnumTypesModel
        //     {
        //         StatusArray = new[] { Status.Active, Status.Deleted, Status.Archived }
        //     };
        //
        //     // Act: Serialize with protobuf-net
        //     byte[] protobufNetData;
        //     using (var ms = new MemoryStream())
        //     {
        //         ProtoBuf.Serializer.Serialize(ms, model);
        //         protobufNetData = ms.ToArray();
        //     }
        //
        //     _output.WriteLine($"ProtobufNet enum array bytes: {BitConverter.ToString(protobufNetData)}");
        //     _output.WriteLine($"Length: {protobufNetData.Length} bytes");
        //
        //     // Analyze: Is it packed or unpacked?
        //     // protobuf-net 2.3.7 Level200 behavior: PACKED by default for repeated primitives
        //     if (protobufNetData.Length > 0 && (protobufNetData[0] & 0x07) == 2)
        //     {
        //         _output.WriteLine("✅ ProtobufNet uses PACKED encoding for enum arrays (Level200)");
        //     }
        //     else
        //     {
        //         _output.WriteLine("⚠️ ProtobufNet uses UNPACKED encoding for enum arrays");
        //     }
        // }

        // [Fact]
        // public void EnumArray_PackedEncoding_CrossCompatibility()
        // {
        //     // Arrange
        //     var model = new EnumTypesModel
        //     {
        //         PriorityArrayPacked = new[] { Priority.Low, Priority.High, Priority.Critical }
        //     };
        //
        //     // Act: Serialize with protobuf-net
        //     byte[] protobufNetData;
        //     using (var ms = new MemoryStream())
        //     {
        //         ProtoBuf.Serializer.Serialize(ms, model);
        //         protobufNetData = ms.ToArray();
        //     }
        //
        //     _output.WriteLine($"ProtobufNet packed enum array: {BitConverter.ToString(protobufNetData)}");
        //
        //     // Try to deserialize with GProtobuf
        //     try
        //     {
        //         var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(protobufNetData);
        //
        //         deserialized.PriorityArrayPacked.Should().Equal(Priority.Low, Priority.High, Priority.Critical);
        //         _output.WriteLine("✅ GProtobuf correctly deserialized packed enum array from protobuf-net");
        //     }
        //     catch (Exception ex)
        //     {
        //         _output.WriteLine($"❌ GProtobuf failed: {ex.Message}");
        //         throw;
        //     }
        // }

        #endregion

        #region Question 4: Negative enum values

        [Fact]
        public void NegativeEnumValue_ProtobufNetBehavior()
        {
            // Arrange
            var model = new EnumTypesModel
            {
                SignedValue = SignedEnum.NegativeTwo // = -2
            };

            // Act: Serialize with protobuf-net
            byte[] protobufNetData;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, model);
                protobufNetData = ms.ToArray();
            }

            _output.WriteLine($"ProtobufNet negative enum bytes: {BitConverter.ToString(protobufNetData)}");

            // Deserialize back
            using (var ms = new MemoryStream(protobufNetData))
            {
                var deserialized = ProtoBuf.Serializer.Deserialize<EnumTypesModel>(ms);
                deserialized.SignedValue.Should().Be(SignedEnum.NegativeTwo);
            }
        }

        #endregion

        #region Cross-compatibility tests

        [Fact]
        public void EnumCrossCompatibility_GProtobufToProtobufNet()
        {
            // Arrange
            var model = new EnumTypesModel
            {
                Status = Status.Active,
                Priority = Priority.High
                // StatusArray = new[] { Status.Active, Status.Deleted } // TODO: Not supported yet
            };

            // Act: Serialize with GProtobuf
            byte[] gprotobufData;
            using (var ms = new MemoryStream())
            {
                try
                {
                    global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeEnumTypesModel(ms, model);
                    gprotobufData = ms.ToArray();
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"❌ ENUM NOT IMPLEMENTED in GProtobuf: {ex.Message}");
                    throw;
                }
            }

            // Deserialize with protobuf-net
            using (var ms = new MemoryStream(gprotobufData))
            {
                var deserialized = ProtoBuf.Serializer.Deserialize<EnumTypesModel>(ms);

                deserialized.Status.Should().Be(Status.Active);
                deserialized.Priority.Should().Be(Priority.High);
                // deserialized.StatusArray.Should().Equal(Status.Active, Status.Deleted); // TODO: Not supported yet
            }
        }

        [Fact]
        public void EnumCrossCompatibility_ProtobufNetToGProtobuf()
        {
            // Arrange
            var model = new EnumTypesModel
            {
                Status = Status.Archived,
                Priority = Priority.Low,
                NullableStatus = Status.Active
            };

            // Act: Serialize with protobuf-net
            byte[] protobufNetData;
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, model);
                protobufNetData = ms.ToArray();
            }

            _output.WriteLine($"ProtobufNet enum data: {BitConverter.ToString(protobufNetData)}");

            // Deserialize with GProtobuf
            try
            {
                var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(protobufNetData);

                deserialized.Status.Should().Be(Status.Archived);
                deserialized.Priority.Should().Be(Priority.Low);
                deserialized.NullableStatus.Should().Be(Status.Active);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ GProtobuf deserialization failed: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}
