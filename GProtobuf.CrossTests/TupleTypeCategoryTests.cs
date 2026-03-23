using System;
using System.IO;
using Xunit;
using GProtobuf.CrossTests.TestModel;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Critical validation tests for Tuple type handling.
    ///
    /// CONTEXT:
    /// These tests verify that nested Tuples are properly handled as Complex types
    /// (length-prefixed messages) rather than incorrectly treated as primitives.
    ///
    /// VALIDATION APPROACH:
    /// Instead of directly testing Generator internals (which is not accessible),
    /// we validate the BEHAVIOR through serialization/deserialization tests.
    ///
    /// If Tuples were incorrectly treated as primitives, these tests would fail
    /// with wire format mismatches or deserialization errors.
    /// </summary>
    public class TupleTypeCategoryTests
    {
        [Fact]
        public void Nested_Tuple_Should_Serialize_As_Length_Prefixed_Message()
        {
            // Arrange: Create nested tuple
            var model = new NestedTupleModel
            {
                SimpleNested = new Tuple<int, Tuple<string, bool>>(
                    42,
                    new Tuple<string, bool>("test", true)
                )
            };

            // Act: Serialize
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, model);
            var bytes = ms.ToArray();

            // Assert: Verify wire format structure
            // The nested Tuple should be length-prefixed (field 2 of outer tuple)
            // If it were treated as primitive, wire format would be different

            // Parse wire format manually
            int pos = 0;
            // Skip to field 1 (SimpleNested field)
            while (pos < bytes.Length)
            {
                byte tag = bytes[pos++];
                int fieldId = tag >> 3;
                int wireType = tag & 0x07;

                if (fieldId == 1) // SimpleNested field
                {
                    Assert.Equal(2, wireType); // WireType.Len

                    // Read length
                    int tupleLength = bytes[pos++];
                    Assert.True(tupleLength > 0, "Outer tuple should have length > 0");

                    // Inside outer tuple, field 2 should be the nested tuple
                    int tupleStart = pos;
                    bool foundNestedTuple = false;

                    while (pos < tupleStart + tupleLength)
                    {
                        byte innerTag = bytes[pos++];
                        int innerFieldId = innerTag >> 3;
                        int innerWireType = innerTag & 0x07;

                        if (innerFieldId == 2) // Nested Tuple field
                        {
                            foundNestedTuple = true;
                            // CRITICAL: Nested tuple MUST be WireType.Len (2), NOT VarInt (0)
                            Assert.Equal(2, innerWireType); // WireType.Len
                            break;
                        }

                        // Skip field value
                        if (innerWireType == 0) // VarInt
                        {
                            while ((bytes[pos] & 0x80) != 0) pos++;
                            pos++;
                        }
                        else if (innerWireType == 2) // Len
                        {
                            int len = bytes[pos++];
                            pos += len;
                        }
                    }

                    Assert.True(foundNestedTuple, "Should find nested tuple field");
                    break;
                }

                // Skip field value
                if (wireType == 0) // VarInt
                {
                    while ((bytes[pos] & 0x80) != 0) pos++;
                    pos++;
                }
                else if (wireType == 2) // Len
                {
                    int len = bytes[pos++];
                    pos += len;
                }
            }
        }

        [Fact]
        public void Eight_Element_Tuple_Rest_Should_Be_Length_Prefixed()
        {
            // Arrange: 8-element tuple where Rest is a nested Tuple
            var model = new NestedTupleModel
            {
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int>(88, 99)
                )
            };

            // Act: Serialize
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, model);
            var bytes = ms.ToArray();

            // Assert: Verify Rest (field 8) is length-prefixed
            // If Rest were treated as primitive int, this would fail

            int pos = 0;
            while (pos < bytes.Length)
            {
                byte tag = bytes[pos++];
                int fieldId = tag >> 3;
                int wireType = tag & 0x07;

                if (fieldId == 5) // EightElementTuple field
                {
                    Assert.Equal(2, wireType); // WireType.Len

                    int tupleLength = bytes[pos++];
                    int tupleStart = pos;
                    bool foundRest = false;

                    while (pos < tupleStart + tupleLength)
                    {
                        byte innerTag = bytes[pos++];
                        int innerFieldId = innerTag >> 3;
                        int innerWireType = innerTag & 0x07;

                        if (innerFieldId == 8) // Rest field
                        {
                            foundRest = true;
                            // CRITICAL: Rest MUST be WireType.Len (nested Tuple), NOT VarInt
                            Assert.Equal(2, innerWireType);
                            break;
                        }

                        // Skip field value
                        if (innerWireType == 0) // VarInt
                        {
                            while ((bytes[pos] & 0x80) != 0) pos++;
                            pos++;
                        }
                        else if (innerWireType == 2) // Len
                        {
                            int len = bytes[pos++];
                            pos += len;
                        }
                    }

                    Assert.True(foundRest, "Should find Rest field");
                    break;
                }

                // Skip field value
                if (wireType == 0) // VarInt
                {
                    while ((bytes[pos] & 0x80) != 0) pos++;
                    pos++;
                }
                else if (wireType == 2) // Len
                {
                    int len = bytes[pos++];
                    pos += len;
                }
            }
        }

        [Fact]
        public void Deeply_Nested_Tuple_Should_Maintain_Wire_Format_Integrity()
        {
            // Arrange: 3-level nested tuple
            var model = new NestedTupleModel
            {
                DeeplyNested = new Tuple<int, Tuple<int, Tuple<int, int>>>(
                    1,
                    new Tuple<int, Tuple<int, int>>(
                        2,
                        new Tuple<int, int>(3, 4)
                    )
                )
            };

            // Act: Round-trip through both serializers
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, model);
            ms.Position = 0;

            // Deserialize with protobuf-net
            var deserialized = ProtoBuf.Serializer.Deserialize<NestedTupleModel>(ms);

            // Assert: If Tuples were incorrectly categorized, this would fail
            Assert.NotNull(deserialized.DeeplyNested);
            Assert.Equal(1, deserialized.DeeplyNested.Item1);
            Assert.Equal(2, deserialized.DeeplyNested.Item2.Item1);
            Assert.Equal(3, deserialized.DeeplyNested.Item2.Item2.Item1);
            Assert.Equal(4, deserialized.DeeplyNested.Item2.Item2.Item2);
        }
    }
}
