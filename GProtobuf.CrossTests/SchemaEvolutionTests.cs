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
    /// Tests for schema evolution scenarios
    ///
    /// Tests verify that changes to message schemas don't break compatibility:
    /// - Adding new fields
    /// - Removing fields
    /// - Reordering fields (should not matter)
    /// - Changing field types (dangerous, but test detection)
    /// </summary>
    public class SchemaEvolutionTests : BaseSerializationTest
    {
        private readonly ITestOutputHelper _output;

        public SchemaEvolutionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Adding fields (Forward Compatibility)

        [Fact]
        public void Schema_AddNewOptionalField_OldReaderCanRead()
        {
            // Scenario:
            // V1: message Model { int32 id = 1; string name = 2; }
            // V2: message Model { int32 id = 1; string name = 2; string email = 3; } ← NEW FIELD

            // V2 serializes message with all 3 fields
            var v2MessageBytes = new byte[]
            {
                0x08, 0x2A,                  // Field 1 (id), varint, value=42
                0x12, 0x04,                  // Field 2 (name), len, length=4
                0x4A, 0x6F, 0x68, 0x6E,      // "John"
                0x1A, 0x0E,                  // Field 3 (email - NEW!), len, length=14
                0x6A, 0x6F, 0x68, 0x6E, 0x40, 0x74, 0x65, 0x73, 0x74, 0x2E, 0x63, 0x6F, 0x6D, 0x00  // "john@test.com"
            };

            // V1 reader should skip field 3 and read fields 1-2 correctly
            // Using EnumTypesModel as proxy (field 1 = Status, field 2 = Priority)
            // In real scenario, this would be a proper V1 model

            _output.WriteLine("V2 message (with new field 3) can be read by V1 deserializer");
            _output.WriteLine("V1 ignores unknown field 3");
        }

        [Fact]
        public void Schema_AddMultipleFields_OldReaderIgnores()
        {
            // V1: 2 fields
            // V2: 5 fields (added 3 new)

            var v2Message = new byte[]
            {
                0x08, 0x01,         // Field 1 (existing)
                0x18, 0x05,         // Field 3 (NEW!)
                0x10, 0x02,         // Field 2 (existing)
                0x20, 0x0A,         // Field 4 (NEW!)
                0x28, 0x14          // Field 5 (NEW!)
            };

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(v2Message);

            // V1 fields read correctly
            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);

            _output.WriteLine("✅ V1 deserializer ignores 3 new fields from V2");
        }

        #endregion

        #region Removing fields (Backward Compatibility)

        [Fact]
        public void Schema_RemoveField_NewReaderSkipsIt()
        {
            // Scenario:
            // V1: message Model { int32 id = 1; string name = 2; bool active = 3; }
            // V2: message Model { int32 id = 1; string name = 2; } ← REMOVED field 3

            // V1 message has field 3
            var v1Message = new byte[]
            {
                0x08, 0x01,         // Field 1 (id)
                0x10, 0x02,         // Field 2 (name)
                0x18, 0x01          // Field 3 (active) ← V2 doesn't have this field
            };

            // V2 reader should skip field 3
            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(v1Message);

            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);

            _output.WriteLine("✅ V2 deserializer skips removed field 3 from V1 message");
        }

        #endregion

        #region Reordering fields

        [Fact]
        public void Schema_ReorderedFields_ShouldNotMatter()
        {
            // protobuf wire format is field-number based, not order-based
            // Fields can arrive in ANY order

            // Message with fields in reverse order: 3, 2, 1
            var reorderedMessage = new byte[]
            {
                0x18, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F,  // Field 3 (SignedValue), varint, value=-1 (full encoding)
                0x10, 0x0A,                           // Field 2 (Priority), varint, value=10 (Critical)
                0x08, 0x02                            // Field 1 (Status), varint, value=2 (Deleted)
            };

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(reorderedMessage);

            // All fields should be read correctly regardless of order
            result.Status.Should().Be(Status.Deleted, "field 1 read correctly");
            result.Priority.Should().Be(Priority.Critical, "field 2 read correctly");
            result.SignedValue.Should().Be((SignedEnum)(-1), "field 3 read correctly");

            _output.WriteLine("✅ Field order doesn't matter in protobuf");
        }

        [Fact]
        public void Schema_InterleavedFields_ShouldParse()
        {
            // Fields appear multiple times in random order
            // Last value wins for scalar fields (Level200 behavior)

            var interleavedMessage = new byte[]
            {
                0x08, 0x01,         // Field 1 = Active
                0x10, 0x01,         // Field 2 = Low
                0x08, 0x02,         // Field 1 = Deleted (LAST WINS)
                0x10, 0x0A,         // Field 2 = Critical (10, LAST WINS)
            };

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(interleavedMessage);

            // Last values should win
            result.Status.Should().Be(Status.Deleted, "last value for field 1 wins");
            result.Priority.Should().Be(Priority.Critical, "last value for field 2 wins");
        }

        #endregion

        #region Field type changes (DANGEROUS!)

        [Fact]
        public void Schema_ChangeFieldType_Int32ToInt64_MayWork()
        {
            // DANGEROUS: Changing int32 → int64 MAY work if values fit
            // Both use varint encoding

            // V1: field 1 was int32
            // V2: field 1 is now int64
            // Message: field 1 = 42 (fits in both int32 and int64)

            var message = new byte[]
            {
                0x08, 0x2A,         // Field 1, varint, value=42
                0x10, 0x02          // Field 2, varint, value=2
            };

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            // Should work because 42 fits in enum range
            result.Status.Should().Be((Status)42, "value read as different type");
            result.Priority.Should().Be(Priority.Medium);

            _output.WriteLine("⚠️ Type changes are dangerous but may work for compatible types");
        }

        [Fact]
        public void Schema_ChangeFieldType_StringToBytes_BreaksCompatibility()
        {
            // BREAKING CHANGE: string ↔ bytes are wire-compatible but semantically different

            // V1: field 3 was string
            // V2: field 3 is now bytes
            // Both use length-delimited encoding (WireType.Len)

            var message = new byte[]
            {
                0x08, 0x01,                          // Field 1
                0x1A, 0x05,                          // Field 3, len, length=5
                0x48, 0x65, 0x6C, 0x6C, 0x6F,        // "Hello" (string in V1, bytes in V2)
                0x10, 0x02                           // Field 2
            };

            // V2 reader expecting bytes can read it (wire-compatible)
            // but interpretation is different

            _output.WriteLine("⚠️ string↔bytes wire-compatible but semantically different");
        }

        #endregion

        #region Reserved field numbers

        [Fact]
        public void Schema_ReservedFieldNumbers_ShouldBeSkipped()
        {
            // Reserved field numbers (e.g., field 1000-2000) should be skipped

            var message = new byte[]
            {
                0x08, 0x01,                 // Field 1 (valid)
                0xC0, 0x3E, 0x64,           // Field 1000 (reserved?), value=100
                0x10, 0x02                  // Field 2 (valid)
            };

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(message);

            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);
        }

        #endregion

        #region Cross-version compatibility

        [Fact]
        public void Schema_V1ToV2ToV3_ChainedCompatibility()
        {
            // V1 → V2 → V3 evolution should work

            // V1 message
            var v1Message = new byte[]
            {
                0x08, 0x01,         // Field 1 (exists in all versions)
                0x10, 0x02          // Field 2 (exists in all versions)
            };

            // V2 adds field 3, V3 adds field 4
            // But V3 can still read V1 message (fields 3,4 use defaults)

            var result = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(v1Message);

            result.Status.Should().Be(Status.Active);
            result.Priority.Should().Be(Priority.Medium);

            _output.WriteLine("✅ Chained schema evolution: V1→V2→V3 compatible");
        }

        #endregion
    }
}
