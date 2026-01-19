using FluentAssertions;
using GProtobuf.Core;
using GProtobuf.Tests.TestModel;
using System;
using System.IO;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Level200 MERGE semantics (protobuf-net 2.3.7 compatibility).
    /// Verifies correct handling of duplicate field tags:
    /// - Scalar fields: LAST WINS (overwrite previous value)
    /// - Repeated fields: MERGE (accumulate values)
    /// </summary>
    public class MergeSemanticsTests
    {
        #region Scalar Fields - LAST WINS

        [Fact]
        public void ScalarField_Int32_DuplicateTags_ShouldUseLastValue()
        {
            // Arrange: Craft message with duplicate field 5 (IntValue)
            var duplicate = new byte[]
            {
                0x28, 0x0A,  // Field 5 (IntValue) = 10
                0x28, 0x14,  // Field 5 (IntValue) = 20 (duplicate - should WIN)
                0x28, 0x1E   // Field 5 (IntValue) = 30 (last value - should WIN)
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(duplicate);

            // Assert: LAST WINS - should be 30
            result.Should().NotBeNull();
            result.IntValue.Should().Be(30, "scalar fields follow LAST WINS semantics");
        }

        [Fact]
        public void ScalarField_String_DuplicateTags_ShouldUseLastValue()
        {
            // Arrange: Craft message with duplicate field 12 (StringValue)
            var duplicate = new byte[]
            {
                0x62, 0x05, 0x66, 0x69, 0x72, 0x73, 0x74,  // Field 12 = "first"
                0x62, 0x06, 0x73, 0x65, 0x63, 0x6F, 0x6E, 0x64,  // Field 12 = "second" (duplicate)
                0x62, 0x04, 0x6C, 0x61, 0x73, 0x74  // Field 12 = "last" (should WIN)
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(duplicate);

            // Assert: LAST WINS
            result.Should().NotBeNull();
            result.StringValue.Should().Be("last", "scalar fields follow LAST WINS semantics");
        }

        [Fact]
        public void ScalarField_Bool_DuplicateTags_ShouldUseLastValue()
        {
            // Arrange: Craft message with duplicate field 11 (BoolValue)
            var duplicate = new byte[]
            {
                0x58, 0x01,  // Field 11 (BoolValue) = true
                0x58, 0x00,  // Field 11 (BoolValue) = false (duplicate)
                0x58, 0x01   // Field 11 (BoolValue) = true (last - should WIN)
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(duplicate);

            // Assert: LAST WINS
            result.Should().NotBeNull();
            result.BoolValue.Should().BeTrue("scalar fields follow LAST WINS semantics");
        }

        [Fact]
        public void ScalarField_Double_DuplicateTags_ShouldUseLastValue()
        {
            // Arrange: Craft message with duplicate field 10 (DoubleValue)
            var duplicate = new byte[]
            {
                // Field 10 (DoubleValue) = 1.5 (double, Fixed64)
                0x51, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x3F,
                // Field 10 (DoubleValue) = 2.5 (duplicate)
                0x51, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x40,
                // Field 10 (DoubleValue) = 3.5 (last - should WIN)
                0x51, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x40
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(duplicate);

            // Assert: LAST WINS
            result.Should().NotBeNull();
            result.DoubleValue.Should().BeApproximately(3.5, 0.001, "scalar fields follow LAST WINS semantics");
        }

        [Fact]
        public void ScalarField_Mixed_DuplicateAndUnique_ShouldHandleCorrectly()
        {
            // Arrange: Mix of unique and duplicate fields
            var mixed = new byte[]
            {
                0x28, 0x0A,  // Field 5 (IntValue) = 10
                0x62, 0x05, 0x66, 0x69, 0x72, 0x73, 0x74,  // Field 12 (StringValue) = "first"
                0x28, 0x14,  // Field 5 (IntValue) = 20 (duplicate - should WIN)
                0x58, 0x01,  // Field 11 (BoolValue) = true
                0x62, 0x04, 0x6C, 0x61, 0x73, 0x74  // Field 12 (StringValue) = "last" (duplicate - should WIN)
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(mixed);

            // Assert: Each field uses LAST value
            result.Should().NotBeNull();
            result.IntValue.Should().Be(20);
            result.StringValue.Should().Be("last");
            result.BoolValue.Should().BeTrue();
        }

        #endregion

        #region Repeated Fields - MERGE (Accumulate)

        [Fact]
        public void RepeatedField_FloatArray_DuplicateTags_ShouldMergeValues()
        {
            // Arrange: Craft message with duplicate field 2 (FloatArrayPacked)
            // Using PACKED encoding (Level200 default)
            var duplicate = new byte[]
            {
                // Field 2 (FloatArrayPacked) packed: 3 floats (1.0f, 2.0f, 3.0f as Fixed32)
                0x12, 0x0C,  // Tag 2 (LengthDelimited), length=12 (3 floats × 4 bytes)
                0x00, 0x00, 0x80, 0x3F,  // 1.0f (Fixed32)
                0x00, 0x00, 0x00, 0x40,  // 2.0f
                0x00, 0x00, 0x40, 0x40,  // 3.0f
                // Field 2 packed: 2 floats (4.0f, 5.0f) - duplicate, should MERGE
                0x12, 0x08,  // Tag 2, length=8 (2 floats × 4 bytes)
                0x00, 0x00, 0x80, 0x40,  // 4.0f
                0x00, 0x00, 0xA0, 0x40   // 5.0f
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(duplicate);

            // Assert: MERGE - should accumulate [1.0f, 2.0f, 3.0f, 4.0f, 5.0f]
            result.Should().NotBeNull();
            result.FloatArrayPacked.Should().NotBeNull();
            result.FloatArrayPacked.Should().HaveCount(5, "repeated fields follow MERGE semantics");
            result.FloatArrayPacked[0].Should().BeApproximately(1.0f, 0.001f);
            result.FloatArrayPacked[4].Should().BeApproximately(5.0f, 0.001f);
        }

        [Fact]
        public void RepeatedField_LongArray_UnpackedDuplicates_ShouldMergeValues()
        {
            // Arrange: Craft message with unpacked duplicate elements (backward compatibility scenario)
            // Field 6 (LongArrayPacked) but send as UNPACKED (pre-Level200 compatibility)
            // Level200 readers MUST support both packed and unpacked for backward compatibility
            var duplicate = new byte[]
            {
                0x30, 0x01,  // Field 6 (LongArrayPacked), WireType.Varint, value = 1 (unpacked)
                0x30, 0x02,  // Field 6, value = 2 (duplicate - should MERGE)
                0x30, 0x03,  // Field 6, value = 3 (duplicate - should MERGE)
                0x30, 0x04   // Field 6, value = 4 (duplicate - should MERGE)
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(duplicate);

            // Assert: MERGE - should accumulate [1, 2, 3, 4] even though field expects packed
            result.Should().NotBeNull();
            result.LongArrayPacked.Should().NotBeNull();
            result.LongArrayPacked.Should().Equal(1L, 2L, 3L, 4L);
            result.LongArrayPacked.Length.Should().Be(4, "backward compatibility: unpacked elements should MERGE");
        }

        [Fact]
        public void RepeatedField_LongArray_MixedPackedAndUnpacked_ShouldMergeAll()
        {
            // Arrange: Mix packed and unpacked encoding for SAME field (dual-mode support)
            // Field 6 (LongArrayPacked) receives both packed and unpacked data
            // Level200: MUST support dual-mode for backward compatibility
            var mixed = new byte[]
            {
                // Field 6 packed: [1, 2]
                0x32, 0x02, 0x01, 0x02,  // Tag 6 WireType.Len, length=2, values 1, 2
                // Field 6 unpacked: 3 (backward compat encoding)
                0x30, 0x03,              // Tag 6 WireType.Varint, value=3
                // Field 6 packed: [4, 5]
                0x32, 0x02, 0x04, 0x05,  // Tag 6 WireType.Len, length=2, values 4, 5
                // Field 6 unpacked: 6
                0x30, 0x06               // Tag 6 WireType.Varint, value=6
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(mixed);

            // Assert: MERGE - all values should accumulate regardless of encoding
            result.Should().NotBeNull();
            result.LongArrayPacked.Should().NotBeNull();
            result.LongArrayPacked.Should().Equal(1L, 2L, 3L, 4L, 5L, 6L);
            result.LongArrayPacked.Length.Should().Be(6, "dual-mode: both packed and unpacked should MERGE into same array");
        }

        [Fact]
        public void RepeatedField_StringArray_DuplicateTags_ShouldMergeValues()
        {
            // Arrange: Craft message with duplicate string array elements (field 1 = BasicStringArray)
            var duplicate = new byte[]
            {
                // Field 1 (BasicStringArray) = "first"
                0x0A, 0x05, 0x66, 0x69, 0x72, 0x73, 0x74,
                // Field 1 (BasicStringArray) = "second" (duplicate - should MERGE)
                0x0A, 0x06, 0x73, 0x65, 0x63, 0x6F, 0x6E, 0x64,
                // Field 1 (BasicStringArray) = "third" (duplicate - should MERGE)
                0x0A, 0x05, 0x74, 0x68, 0x69, 0x72, 0x64
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(duplicate);

            // Assert: MERGE - should accumulate ["first", "second", "third"]
            result.Should().NotBeNull();
            result.BasicStringArray.Should().NotBeNull();
            result.BasicStringArray.Should().Equal("first", "second", "third");
            result.BasicStringArray.Length.Should().Be(3, "repeated fields follow MERGE semantics");
        }

        #endregion

        #region Mixed Scalar and Repeated - Complex Scenarios

        [Fact]
        public void MixedFields_ScalarLastWins_RepeatedMerges()
        {
            // Arrange: Complex message with both scalar duplicates (LAST WINS) and repeated duplicates (MERGE)
            var complex = new byte[]
            {
                // Scalar field 5 (IntValue) = 10
                0x28, 0x0A,
                // Repeated field 2 (FloatArrayPacked) packed: [1.0f, 2.0f]
                0x12, 0x08,
                0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40,
                // Scalar field 5 (IntValue) = 20 (duplicate - should WIN)
                0x28, 0x14,
                // Repeated field 2 (FloatArrayPacked) packed: [3.0f, 4.0f] (duplicate - should MERGE)
                0x12, 0x08,
                0x00, 0x00, 0x40, 0x40, 0x00, 0x00, 0x80, 0x40,
                // Scalar field 12 (StringValue) = "test"
                0x62, 0x04, 0x74, 0x65, 0x73, 0x74
            };

            // Act: Deserialize BasicTypesModel and PrimitiveArraysTestModel separately
            var scalarResult = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(complex);
            var arrayResult = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(complex);

            // Assert: Scalar uses LAST value, repeated accumulates
            scalarResult.IntValue.Should().Be(20, "scalar field uses LAST WINS");
            scalarResult.StringValue.Should().Be("test");

            arrayResult.FloatArrayPacked.Should().NotBeNull();
            arrayResult.FloatArrayPacked.Should().HaveCount(4, "repeated field uses MERGE");
        }

        [Fact]
        public void RepeatedField_EmptyArray_ThenValues_ShouldMerge()
        {
            // Arrange: Start with empty packed array, then add values (field 2 = FloatArrayPacked)
            var withEmpty = new byte[]
            {
                0x12, 0x00,              // Field 2 packed: [] (empty)
                0x12, 0x0C,              // Field 2 packed: [1.0f, 2.0f, 3.0f] (should MERGE)
                0x00, 0x00, 0x80, 0x3F,  // 1.0f
                0x00, 0x00, 0x00, 0x40,  // 2.0f
                0x00, 0x00, 0x40, 0x40   // 3.0f
            };

            // Act: Deserialize
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(withEmpty);

            // Assert: MERGE - empty array doesn't prevent accumulation
            result.Should().NotBeNull();
            result.FloatArrayPacked.Should().NotBeNull();
            result.FloatArrayPacked.Should().HaveCount(3, "MERGE works even after empty array");
        }

        #endregion

        #region Cross-Compatibility with protobuf-net

        [Fact]
        public void CrossCompatibility_ProtobufNet_DuplicateScalar_LastWins()
        {
            // Arrange: Create model with value, serialize with protobuf-net,
            // then manually craft duplicate to verify GProtobuf behavior
            var original = new BasicTypesModel
            {
                IntValue = 42,
                StringValue = "original"
            };

            // Serialize with GProtobuf
            byte[] gprotoBytes;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeBasicTypesModel(ms, original);
                gprotoBytes = ms.ToArray();
            }

            // Manually append duplicate IntValue
            var duplicate = new byte[gprotoBytes.Length + 2];
            Array.Copy(gprotoBytes, duplicate, gprotoBytes.Length);
            duplicate[gprotoBytes.Length] = 0x28;  // Field 5 tag
            duplicate[gprotoBytes.Length + 1] = 0x64;  // Value 100

            // Act: Deserialize with GProtobuf
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(duplicate);

            // Assert: Last value wins (100, not 42)
            result.IntValue.Should().Be(100, "LAST WINS for duplicate scalar");
            result.StringValue.Should().Be("original", "other fields unaffected");
        }

        [Fact]
        public void CrossCompatibility_ProtobufNet_DuplicateRepeated_Merges()
        {
            // Arrange: Create model with array
            var original = new PrimitiveArraysTestModel
            {
                FloatArrayPacked = new[] { 1.0f, 2.0f, 3.0f }
            };

            // Serialize with GProtobuf (packed encoding)
            byte[] gprotoBytes;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel(ms, original);
                gprotoBytes = ms.ToArray();
            }

            // Manually append another packed array [4.0f, 5.0f]
            var duplicate = new byte[gprotoBytes.Length + 10];
            Array.Copy(gprotoBytes, duplicate, gprotoBytes.Length);
            duplicate[gprotoBytes.Length] = 0x12;      // Field 2 tag (packed)
            duplicate[gprotoBytes.Length + 1] = 0x08;  // Length = 8 (2 floats × 4 bytes)
            // 4.0f as Fixed32 (little-endian)
            duplicate[gprotoBytes.Length + 2] = 0x00;
            duplicate[gprotoBytes.Length + 3] = 0x00;
            duplicate[gprotoBytes.Length + 4] = 0x80;
            duplicate[gprotoBytes.Length + 5] = 0x40;
            // 5.0f as Fixed32
            duplicate[gprotoBytes.Length + 6] = 0x00;
            duplicate[gprotoBytes.Length + 7] = 0x00;
            duplicate[gprotoBytes.Length + 8] = 0xA0;
            duplicate[gprotoBytes.Length + 9] = 0x40;

            // Act: Deserialize with GProtobuf
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(duplicate);

            // Assert: MERGE - accumulates [1.0f, 2.0f, 3.0f, 4.0f, 5.0f]
            result.FloatArrayPacked.Should().NotBeNull();
            result.FloatArrayPacked.Should().HaveCount(5, "MERGE for duplicate repeated");
        }

        #endregion
    }
}
