using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Comprehensive tests for Custom Buffer Serialization feature.
    /// Tests GProtobuf serialization and deserialization of custom buffer fields.
    /// Note: protobuf-net does not support custom buffer attributes, so only GG tests are applicable.
    /// </summary>
    public class CustomBufferSerializationTests
    {
        #region BasicCustomBufferModel Tests

        [Fact]
        public void Test_GG_BasicCustomBuffer_WithData()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 42,
                Name = "Test Object",
                CustomData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.Id.Should().Be(42);
            deserialized.Name.Should().Be("Test Object");
            deserialized.CustomData.Should().NotBeNull();
            deserialized.CustomData.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        }

        [Fact]
        public void Test_GG_BasicCustomBuffer_EmptyCustomData()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 100,
                Name = "Empty Custom",
                CustomData = Array.Empty<byte>()
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.Id.Should().Be(100);
            deserialized.Name.Should().Be("Empty Custom");
            // Empty array should deserialize to empty array (not null)
            deserialized.CustomData.Should().NotBeNull();
            deserialized.CustomData.Should().BeEmpty();
        }

        [Fact]
        public void Test_GG_BasicCustomBuffer_NullCustomData()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 200,
                Name = "Null Custom",
                CustomData = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.Id.Should().Be(200);
            deserialized.Name.Should().Be("Null Custom");
            // Null data should serialize as zero-length and deserialize to empty array
            deserialized.CustomData.Should().NotBeNull();
            deserialized.CustomData.Should().BeEmpty();
        }

        [Fact]
        public void Test_GG_BasicCustomBuffer_OnlyRegularFields()
        {
            // Arrange - only set regular ProtoMember fields
            var original = new BasicCustomBufferModel
            {
                Id = 999,
                Name = "Regular Only"
                // CustomData is null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.Id.Should().Be(999);
            deserialized.Name.Should().Be("Regular Only");
        }

        #endregion

        #region MultipleCustomBufferModel Tests

        [Fact]
        public void Test_GG_MultipleCustomBuffers_AllFilled()
        {
            // Arrange
            var original = new MultipleCustomBufferModel
            {
                Version = 1,
                HeaderData = new byte[] { 0xDE, 0xAD },
                PayloadData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 },
                FooterData = new byte[] { 0xBE, 0xEF }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleCustomBufferModel(bytes);

            // Assert
            deserialized.Version.Should().Be(1);
            deserialized.HeaderData.Should().BeEquivalentTo(new byte[] { 0xDE, 0xAD });
            deserialized.PayloadData.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            deserialized.FooterData.Should().BeEquivalentTo(new byte[] { 0xBE, 0xEF });
        }

        [Fact]
        public void Test_GG_MultipleCustomBuffers_PartiallyFilled()
        {
            // Arrange - only header and footer, no payload
            var original = new MultipleCustomBufferModel
            {
                Version = 2,
                HeaderData = new byte[] { 0xAA, 0xBB, 0xCC },
                PayloadData = null,
                FooterData = new byte[] { 0xFF }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleCustomBufferModel(bytes);

            // Assert
            deserialized.Version.Should().Be(2);
            deserialized.HeaderData.Should().BeEquivalentTo(new byte[] { 0xAA, 0xBB, 0xCC });
            deserialized.PayloadData.Should().NotBeNull();
            deserialized.PayloadData.Should().BeEmpty();
            deserialized.FooterData.Should().BeEquivalentTo(new byte[] { 0xFF });
        }

        [Fact]
        public void Test_GG_MultipleCustomBuffers_AllEmpty()
        {
            // Arrange
            var original = new MultipleCustomBufferModel
            {
                Version = 3,
                HeaderData = Array.Empty<byte>(),
                PayloadData = Array.Empty<byte>(),
                FooterData = Array.Empty<byte>()
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeMultipleCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeMultipleCustomBufferModel(bytes);

            // Assert
            deserialized.Version.Should().Be(3);
            deserialized.HeaderData.Should().BeEmpty();
            deserialized.PayloadData.Should().BeEmpty();
            deserialized.FooterData.Should().BeEmpty();
        }

        #endregion

        #region CompressedDataModel Tests

        [Fact]
        public void Test_GG_CompressedData_RoundTrip()
        {
            // Arrange
            var original = new CompressedDataModel
            {
                Description = "Test document",
                Timestamp = 1234567890L,
                OriginalText = "This is the original text that will be 'compressed' and serialized."
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeCompressedDataModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeCompressedDataModel(bytes);

            // Assert
            deserialized.Description.Should().Be("Test document");
            deserialized.Timestamp.Should().Be(1234567890L);
            deserialized.OriginalText.Should().Be("This is the original text that will be 'compressed' and serialized.");
        }

        [Fact]
        public void Test_GG_CompressedData_EmptyText()
        {
            // Arrange
            var original = new CompressedDataModel
            {
                Description = "Empty content",
                Timestamp = 999L,
                OriginalText = ""
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeCompressedDataModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeCompressedDataModel(bytes);

            // Assert
            deserialized.Description.Should().Be("Empty content");
            deserialized.OriginalText.Should().BeNullOrEmpty();
        }

        [Fact]
        public void Test_GG_CompressedData_UnicodeText()
        {
            // Arrange
            var original = new CompressedDataModel
            {
                Description = "Unicode test",
                Timestamp = 42L,
                OriginalText = "Привіт світ! 你好世界! مرحبا بالعالم"
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeCompressedDataModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeCompressedDataModel(bytes);

            // Assert
            deserialized.OriginalText.Should().Be("Привіт світ! 你好世界! مرحبا بالعالم");
        }

        #endregion

        #region OnlyCustomBufferModel Tests

        [Fact]
        public void Test_GG_OnlyCustomBuffer_BothFilled()
        {
            // Arrange
            var original = new OnlyCustomBufferModel
            {
                Data1 = new byte[] { 10, 20, 30 },
                Data2 = new byte[] { 40, 50, 60, 70, 80 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOnlyCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOnlyCustomBufferModel(bytes);

            // Assert
            deserialized.Data1.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
            deserialized.Data2.Should().BeEquivalentTo(new byte[] { 40, 50, 60, 70, 80 });
        }

        [Fact]
        public void Test_GG_OnlyCustomBuffer_BothNull()
        {
            // Arrange
            var original = new OnlyCustomBufferModel
            {
                Data1 = null,
                Data2 = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOnlyCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOnlyCustomBufferModel(bytes);

            // Assert
            deserialized.Data1.Should().BeEmpty();
            deserialized.Data2.Should().BeEmpty();
        }

        #endregion

        #region EmptyCustomBufferModel Tests

        [Fact]
        public void Test_GG_EmptyCustomBuffer_WithData()
        {
            // Arrange
            var original = new EmptyCustomBufferModel
            {
                Marker = 123,
                OptionalData = new byte[] { 0x11, 0x22, 0x33 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeEmptyCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeEmptyCustomBufferModel(bytes);

            // Assert
            deserialized.Marker.Should().Be(123);
            deserialized.OptionalData.Should().BeEquivalentTo(new byte[] { 0x11, 0x22, 0x33 });
        }

        [Fact]
        public void Test_GG_EmptyCustomBuffer_WithoutData()
        {
            // Arrange
            var original = new EmptyCustomBufferModel
            {
                Marker = 456,
                OptionalData = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeEmptyCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeEmptyCustomBufferModel(bytes);

            // Assert
            deserialized.Marker.Should().Be(456);
            deserialized.OptionalData.Should().BeNull();
        }

        #endregion

        #region LargeCustomBufferModel Tests

        [Fact]
        public void Test_GG_LargeCustomBuffer_1KB()
        {
            // Arrange
            var largeData = new byte[1024];
            new Random(42).NextBytes(largeData);

            var original = new LargeCustomBufferModel
            {
                Tag = "1KB Test",
                LargeData = largeData
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLargeCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeLargeCustomBufferModel(bytes);

            // Assert
            deserialized.Tag.Should().Be("1KB Test");
            deserialized.LargeData.Should().BeEquivalentTo(largeData);
        }

        [Fact]
        public void Test_GG_LargeCustomBuffer_64KB()
        {
            // Arrange
            var largeData = new byte[64 * 1024];
            new Random(123).NextBytes(largeData);

            var original = new LargeCustomBufferModel
            {
                Tag = "64KB Test",
                LargeData = largeData
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLargeCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeLargeCustomBufferModel(bytes);

            // Assert
            deserialized.Tag.Should().Be("64KB Test");
            deserialized.LargeData.Should().BeEquivalentTo(largeData);
        }

        [Fact]
        public void Test_GG_LargeCustomBuffer_1MB()
        {
            // Arrange
            var largeData = new byte[1024 * 1024];
            new Random(456).NextBytes(largeData);

            var original = new LargeCustomBufferModel
            {
                Tag = "1MB Test",
                LargeData = largeData
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLargeCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeLargeCustomBufferModel(bytes);

            // Assert
            deserialized.Tag.Should().Be("1MB Test");
            deserialized.LargeData.Length.Should().Be(1024 * 1024);
            deserialized.LargeData.Should().BeEquivalentTo(largeData);
        }

        #endregion

        #region TransformedBufferModel Tests

        [Fact]
        public void Test_GG_TransformedBuffer_XorEncryption()
        {
            // Arrange
            var original = new TransformedBufferModel
            {
                Key = 0xAB,
                PlainData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeTransformedBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeTransformedBufferModel(bytes);

            // Assert
            deserialized.Key.Should().Be(0xAB);
            deserialized.PlainData.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        }

        [Fact]
        public void Test_GG_TransformedBuffer_DifferentKeys()
        {
            // Test with different keys to ensure transformation is applied correctly
            for (int key = 0; key < 256; key += 17)
            {
                // Arrange
                var original = new TransformedBufferModel
                {
                    Key = key,
                    PlainData = Encoding.UTF8.GetBytes($"Secret message with key {key}")
                };

                // Act
                using var ms = new MemoryStream();
                global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeTransformedBufferModel(ms, original);
                var bytes = ms.ToArray();
                var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeTransformedBufferModel(bytes);

                // Assert
                deserialized.Key.Should().Be(key);
                Encoding.UTF8.GetString(deserialized.PlainData).Should().Be($"Secret message with key {key}");
            }
        }

        [Fact]
        public void Test_GG_TransformedBuffer_EmptyData()
        {
            // Arrange
            var original = new TransformedBufferModel
            {
                Key = 0xFF,
                PlainData = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeTransformedBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeTransformedBufferModel(bytes);

            // Assert
            deserialized.Key.Should().Be(0xFF);
            deserialized.PlainData.Should().BeNull();
        }

        #endregion

        #region InterleavedFieldsModel Tests

        [Fact]
        public void Test_GG_InterleavedFields_AllFilled()
        {
            // Arrange
            var original = new InterleavedFieldsModel
            {
                FirstField = 1,
                CustomBuffer2 = new byte[] { 0x22, 0x22 },
                MiddleField = "Middle",
                CustomBuffer4 = new byte[] { 0x44, 0x44, 0x44, 0x44 },
                LastField = 3.14159
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeInterleavedFieldsModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeInterleavedFieldsModel(bytes);

            // Assert
            deserialized.FirstField.Should().Be(1);
            deserialized.CustomBuffer2.Should().BeEquivalentTo(new byte[] { 0x22, 0x22 });
            deserialized.MiddleField.Should().Be("Middle");
            deserialized.CustomBuffer4.Should().BeEquivalentTo(new byte[] { 0x44, 0x44, 0x44, 0x44 });
            deserialized.LastField.Should().BeApproximately(3.14159, 0.00001);
        }

        [Fact]
        public void Test_GG_InterleavedFields_OnlyRegularFields()
        {
            // Arrange
            var original = new InterleavedFieldsModel
            {
                FirstField = 100,
                CustomBuffer2 = null,
                MiddleField = "Only regular",
                CustomBuffer4 = null,
                LastField = 2.71828
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeInterleavedFieldsModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeInterleavedFieldsModel(bytes);

            // Assert
            deserialized.FirstField.Should().Be(100);
            deserialized.MiddleField.Should().Be("Only regular");
            deserialized.LastField.Should().BeApproximately(2.71828, 0.00001);
        }

        [Fact]
        public void Test_GG_InterleavedFields_OnlyCustomBuffers()
        {
            // Arrange
            var original = new InterleavedFieldsModel
            {
                FirstField = 0,
                CustomBuffer2 = new byte[] { 0xAA },
                MiddleField = null,
                CustomBuffer4 = new byte[] { 0xBB },
                LastField = 0.0
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeInterleavedFieldsModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeInterleavedFieldsModel(bytes);

            // Assert
            deserialized.CustomBuffer2.Should().BeEquivalentTo(new byte[] { 0xAA });
            deserialized.CustomBuffer4.Should().BeEquivalentTo(new byte[] { 0xBB });
        }

        #endregion

        #region Serialization Size Tests

        [Fact]
        public void Test_GG_CustomBuffer_SerializedSizeIsCorrect()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 1,
                Name = "A",
                CustomData = new byte[] { 1, 2, 3 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();

            // Assert - verify data can be deserialized (size calculation was correct)
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);
            deserialized.Should().NotBeNull();
            deserialized.CustomData.Length.Should().Be(3);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void Test_GG_CustomBuffer_SingleByte()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 1,
                CustomData = new byte[] { 0xFF }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.CustomData.Should().BeEquivalentTo(new byte[] { 0xFF });
        }

        [Fact]
        public void Test_GG_CustomBuffer_AllZeros()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 1,
                CustomData = new byte[] { 0, 0, 0, 0, 0 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.CustomData.Should().BeEquivalentTo(new byte[] { 0, 0, 0, 0, 0 });
        }

        [Fact]
        public void Test_GG_CustomBuffer_AllOnes()
        {
            // Arrange
            var data = Enumerable.Repeat((byte)0xFF, 100).ToArray();
            var original = new BasicCustomBufferModel
            {
                Id = 1,
                CustomData = data
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

            // Assert
            deserialized.CustomData.Should().BeEquivalentTo(data);
        }

        [Fact]
        public void Test_GG_CustomBuffer_BinaryPatterns()
        {
            // Arrange - test with various binary patterns
            var patterns = new byte[][]
            {
                new byte[] { 0x00, 0xFF, 0x00, 0xFF },
                new byte[] { 0xAA, 0x55, 0xAA, 0x55 },
                new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }
            };

            foreach (var pattern in patterns)
            {
                var original = new BasicCustomBufferModel
                {
                    Id = 1,
                    CustomData = pattern
                };

                // Act
                using var ms = new MemoryStream();
                global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, original);
                var bytes = ms.ToArray();
                var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);

                // Assert
                deserialized.CustomData.Should().BeEquivalentTo(pattern);
            }
        }

        #endregion

        #region Multiple Round-Trips

        [Fact]
        public void Test_GG_CustomBuffer_MultipleRoundTrips()
        {
            // Arrange
            var original = new BasicCustomBufferModel
            {
                Id = 42,
                Name = "Round-trip test",
                CustomData = new byte[] { 1, 2, 3, 4, 5 }
            };

            // Act - serialize and deserialize multiple times
            var current = original;
            for (int i = 0; i < 10; i++)
            {
                using var ms = new MemoryStream();
                global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeBasicCustomBufferModel(ms, current);
                var bytes = ms.ToArray();
                current = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeBasicCustomBufferModel(bytes);
            }

            // Assert
            current.Id.Should().Be(42);
            current.Name.Should().Be("Round-trip test");
            current.CustomData.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
        }

        #endregion
    }
}
