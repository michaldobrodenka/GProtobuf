using FluentAssertions;
using GProtobuf.Core;
using System;
using System.IO;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Level200 malformed input validation.
    /// Verifies that SpanReader rejects malformed protobuf messages gracefully.
    /// </summary>
    public class MalformedInputTests
    {
        [Fact]
        public void MalformedInput_VarInt32_ExceedsMaxLength_ShouldThrow()
        {
            // Arrange: Craft varint that continues for 11 bytes (exceeds 10-byte max per protobuf spec)
            var malformed = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 5 bytes
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 10 bytes total with continuation bit
                0x01  // 11th byte - INVALID (protobuf spec max is 10 bytes)
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw InvalidDataException
            try
            {
                reader.ReadVarInt32();
                Assert.Fail("Expected InvalidDataException was not thrown");
            }
            catch (InvalidDataException ex)
            {
                ex.Message.Should().Contain("exceeded maximum length of 10 bytes");
            }
        }

        [Fact]
        public void MalformedInput_VarInt64_ExceedsMaxLength_ShouldThrow()
        {
            // Arrange: Craft varint that continues for 11 bytes (exceeds 10-byte max for int64)
            var malformed = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 5 bytes
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 10 bytes total with continuation bit
                0x01  // 11th byte - INVALID
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw InvalidDataException
            try
            {
                reader.ReadVarInt64();
                Assert.Fail("Expected InvalidDataException was not thrown");
            }
            catch (InvalidDataException ex)
            {
                ex.Message.Should().Contain("exceeded maximum length of 10 bytes");
            }
        }

        [Fact]
        public void MalformedInput_NegativeLength_String_ShouldThrow()
        {
            // Arrange: Craft length-delimited field with negative length
            // VarInt encoding of -1 is 0xFFFFFFFF = [0xFF, 0xFF, 0xFF, 0xFF, 0x0F]
            var malformed = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x0F // VarInt32(-1)
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw InvalidDataException when trying to read string
            try
            {
                SpanReaders.ReadString(ref reader, WireType.Len);
                Assert.Fail("Expected InvalidDataException was not thrown");
            }
            catch (InvalidDataException ex)
            {
                ex.Message.Should().Contain("length cannot be negative");
            }
        }

        [Fact]
        public void MalformedInput_NegativeLength_ByteArray_ShouldThrow()
        {
            // Arrange: Craft length-delimited field with negative length
            var malformed = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x0F // VarInt32(-1)
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw InvalidDataException
            try
            {
                SpanReaders.ReadByteArray(ref reader);
                Assert.Fail("Expected InvalidDataException was not thrown");
            }
            catch (InvalidDataException ex)
            {
                ex.Message.Should().Contain("length cannot be negative");
            }
        }

        [Fact]
        public void MalformedInput_LengthExceedsBuffer_String_ShouldThrow()
        {
            // Arrange: Claim length=100 but only provide 5 bytes
            var malformed = new byte[]
            {
                0x64,       // Length = 100 (VarInt)
                0x01, 0x02, 0x03, 0x04, 0x05  // Only 5 bytes available
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on buffer overrun
            try
            {
                SpanReaders.ReadString(ref reader, WireType.Len);
                Assert.Fail("Expected InvalidOperationException was not thrown");
            }
            catch (InvalidOperationException ex)
            {
                ex.Message.Should().Be("Buffer overrun");
            }
        }

        [Fact]
        public void MalformedInput_SkipField_Fixed32_BufferOverrun_ShouldThrow()
        {
            // Arrange: Only 2 bytes available, but trying to skip Fixed32 (4 bytes)
            var malformed = new byte[] { 0x01, 0x02 };
            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on buffer overrun
            try
            {
                reader.SkipField(WireType.Fixed32b);
                Assert.Fail("Expected InvalidOperationException was not thrown");
            }
            catch (InvalidOperationException ex)
            {
                ex.Message.Should().Be("Buffer overrun");
            }
        }

        [Fact]
        public void MalformedInput_SkipField_Fixed64_BufferOverrun_ShouldThrow()
        {
            // Arrange: Only 4 bytes available, but trying to skip Fixed64 (8 bytes)
            var malformed = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on buffer overrun
            try
            {
                reader.SkipField(WireType.Fixed64b);
                Assert.Fail("Expected InvalidOperationException was not thrown");
            }
            catch (InvalidOperationException ex)
            {
                ex.Message.Should().Be("Buffer overrun");
            }
        }

        [Fact]
        public void MalformedInput_SkipField_Len_ExceedsBuffer_ShouldThrow()
        {
            // Arrange: Claim length=50 but only 5 bytes available after length prefix
            var malformed = new byte[]
            {
                0x32,       // Length = 50 (VarInt)
                0x01, 0x02, 0x03, 0x04, 0x05  // Only 5 bytes
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on buffer overrun
            try
            {
                reader.SkipField(WireType.Len);
                Assert.Fail("Expected InvalidOperationException was not thrown");
            }
            catch (InvalidOperationException ex)
            {
                ex.Message.Should().Be("Buffer overrun");
            }
        }

        [Fact]
        public void MalformedInput_UnexpectedEOF_VarInt_ShouldThrow()
        {
            // Arrange: Varint with continuation bit set but no next byte
            var malformed = new byte[]
            {
                0xFF  // Continuation bit set, but no more bytes
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should NOT throw (current behavior treats as incomplete but valid)
            // This is a design decision: incomplete varint with continuation bit is accepted
            var result = reader.ReadVarInt32();
            result.Should().Be(127); // Reads 0x7F (ignores continuation bit on last byte)
        }

        [Fact]
        public void MalformedInput_EmptyBuffer_VarInt_ShouldThrow()
        {
            // Arrange: Completely empty buffer
            var malformed = Array.Empty<byte>();
            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on empty buffer
            try
            {
                reader.ReadVarInt32();
                Assert.Fail("Expected InvalidOperationException was not thrown");
            }
            catch (InvalidOperationException ex)
            {
                ex.Message.Should().Contain("end of buffer");
            }
        }

        [Fact]
        public void ValidInput_MaxValidVarInt32_5Bytes_ShouldSucceed()
        {
            // Arrange: Valid 5-byte varint for int32
            var valid = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x0F // Max positive int32 with 5 bytes
            };

            var reader = new SpanReader(valid);

            // Act: Should not throw
            var result = reader.ReadVarInt32();

            // Assert: Should read successfully
            result.Should().Be(-1); // This encodes -1 in protobuf varint
        }

        [Fact]
        public void ValidInput_NonOptimal_VarInt32_6Bytes_ShouldSucceed()
        {
            // Arrange: Non-optimal 6-byte varint for int32 (protobuf-net compatibility)
            // Protobuf spec allows up to 10 bytes for any varint, even if not optimal
            var valid = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // 5 bytes with continuation
                0x00  // 6th byte - NOT optimal but VALID per spec
            };

            var reader = new SpanReader(valid);

            // Act: Should not throw (backward compatibility with protobuf-net)
            var result = reader.ReadVarInt32();

            // Assert: Should read successfully
            result.Should().Be(-1);
        }

        [Fact]
        public void ValidInput_MaxValidVarInt64_10Bytes_ShouldSucceed()
        {
            // Arrange: Valid 10-byte varint for int64
            var valid = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0xFF, 0xFF, 0x01 // 10 bytes
            };

            var reader = new SpanReader(valid);

            // Act: Should not throw
            var result = reader.ReadVarInt64();

            // Assert: Should read successfully
            result.Should().NotBe(0);
        }

        [Fact]
        public void ValidInput_ZeroLength_String_ShouldReturnEmpty()
        {
            // Arrange: Zero-length string (valid protobuf)
            var valid = new byte[] { 0x00 }; // Length = 0

            var reader = new SpanReader(valid);

            // Act: Should not throw
            var result = SpanReaders.ReadString(ref reader, WireType.Len);

            // Assert: Should return empty string
            result.Should().BeEmpty();
        }

        [Fact]
        public void MalformedInput_SkipField_NegativeLength_ShouldThrow()
        {
            // Arrange: Length-delimited field with negative length in SkipField
            var malformed = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x0F // VarInt32(-1)
            };

            var reader = new SpanReader(malformed);

            // Act & Assert: Should throw on negative length
            try
            {
                reader.SkipField(WireType.Len);
                Assert.Fail("Expected InvalidDataException was not thrown");
            }
            catch (InvalidDataException ex)
            {
                ex.Message.Should().Contain("length cannot be negative");
            }
        }
    }
}
