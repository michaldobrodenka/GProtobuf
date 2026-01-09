using System;
using System.IO;
using Xunit;
using GProtobuf.CrossTests.TestModel;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Critical test to verify 8-element Tuple compatibility with protobuf-net.
    ///
    /// ISSUE: Current implementation treats Rest as nested message (field 8 with length-delimited),
    /// but protobuf-net flattens Rest items directly (field 8 = Item8, field 9 = Item9).
    ///
    /// Wire format difference:
    ///
    /// GProtobuf (current - WRONG):
    ///   field 1-7: direct values
    ///   field 8: length-delimited { field 1: value, field 2: value }
    ///
    /// protobuf-net (correct):
    ///   field 1-7: direct values
    ///   field 8: direct value (Item8)
    ///   field 9: direct value (Item9)
    /// </summary>
    public class EightElementTupleCompatibilityTest
    {
        [Fact]
        public void Test_ProtobufNet_To_GProtobuf_EightElementTuple()
        {
            // Arrange: Create model with 8-element tuple
            var original = new NestedTupleModel
            {
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int>(88, 99)
                )
            };

            // Act: Serialize with protobuf-net
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Debug: Print wire format
            Console.WriteLine($"Wire format bytes ({bytes.Length}): {BitConverter.ToString(bytes)}");

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNestedTupleModel(bytes);

            // Assert: Verify all items
            Assert.NotNull(deserialized.EightElementTuple);
            Assert.Equal(1, deserialized.EightElementTuple.Item1);
            Assert.Equal(2, deserialized.EightElementTuple.Item2);
            Assert.Equal(3, deserialized.EightElementTuple.Item3);
            Assert.Equal(4, deserialized.EightElementTuple.Item4);
            Assert.Equal(5, deserialized.EightElementTuple.Item5);
            Assert.Equal(6, deserialized.EightElementTuple.Item6);
            Assert.Equal(7, deserialized.EightElementTuple.Item7);
            Assert.NotNull(deserialized.EightElementTuple.Rest);

            // CRITICAL: These assertions will likely FAIL with current implementation
            Assert.Equal(88, deserialized.EightElementTuple.Rest.Item1); // Expected Item8
            Assert.Equal(99, deserialized.EightElementTuple.Rest.Item2); // Expected Item9
        }

        [Fact]
        public void Test_GProtobuf_To_ProtobufNet_EightElementTuple()
        {
            // Arrange: Create model with 8-element tuple
            var original = new NestedTupleModel
            {
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    10, 20, 30, 40, 50, 60, 70,
                    new Tuple<int, int>(888, 999)
                )
            };

            // Act: Serialize with GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            var bytes = ms.ToArray();

            // Debug: Print wire format
            Console.WriteLine($"Wire format bytes ({bytes.Length}): {BitConverter.ToString(bytes)}");

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<NestedTupleModel>(ms);

            // Assert: Verify all items
            Assert.NotNull(deserialized.EightElementTuple);
            Assert.Equal(10, deserialized.EightElementTuple.Item1);
            Assert.Equal(20, deserialized.EightElementTuple.Item2);
            Assert.Equal(30, deserialized.EightElementTuple.Item3);
            Assert.Equal(40, deserialized.EightElementTuple.Item4);
            Assert.Equal(50, deserialized.EightElementTuple.Item5);
            Assert.Equal(60, deserialized.EightElementTuple.Item6);
            Assert.Equal(70, deserialized.EightElementTuple.Item7);
            Assert.NotNull(deserialized.EightElementTuple.Rest);

            // CRITICAL: These assertions will likely FAIL with current implementation
            Assert.Equal(888, deserialized.EightElementTuple.Rest.Item1); // Expected Item8
            Assert.Equal(999, deserialized.EightElementTuple.Rest.Item2); // Expected Item9
        }

        [Fact]
        public void Test_WireFormat_Analysis()
        {
            // This test analyzes actual wire format to understand the difference

            var tuple8 = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                1, 2, 3, 4, 5, 6, 7,
                new Tuple<int, int>(88, 99)
            );

            var model = new NestedTupleModel { EightElementTuple = tuple8 };

            // Serialize with protobuf-net
            using var msProto = new MemoryStream();
            ProtoBuf.Serializer.Serialize(msProto, model);
            var protoBytes = msProto.ToArray();

            // Serialize with GProtobuf
            using var msGProto = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(msGProto, model);
            var gprotoBytes = msGProto.ToArray();

            // Print comparison
            Console.WriteLine("=== Wire Format Comparison ===");
            Console.WriteLine($"protobuf-net ({protoBytes.Length} bytes): {BitConverter.ToString(protoBytes)}");
            Console.WriteLine($"GProtobuf    ({gprotoBytes.Length} bytes): {BitConverter.ToString(gprotoBytes)}");
            Console.WriteLine();
            Console.WriteLine($"Bytes match: {protoBytes.Length == gprotoBytes.Length}");

            // This assertion will fail, showing the wire format incompatibility
            Assert.Equal(protoBytes.Length, gprotoBytes.Length);
        }
    }
}
