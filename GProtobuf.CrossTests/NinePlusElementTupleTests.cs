using System;
using System.IO;
using Xunit;
using GProtobuf.CrossTests.TestModel;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Tuples with 9+ elements to ensure proper Rest handling.
    /// </summary>
    public class NinePlusElementTupleTests
    {
        [Fact]
        public void Test_PG_NineElements()
        {
            // Arrange: 9-element tuple (Rest has 2 items)
            var original = new NinePlusElementTupleModel
            {
                NineElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int>(88, 99) // Item8=88, Item9=99
                )
            };

            // Act: Serialize with protobuf-net, deserialize with GProtobuf
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNinePlusElementTupleModel(bytes);

            // Assert
            Assert.NotNull(deserialized.NineElements);
            Assert.Equal(1, deserialized.NineElements.Item1);
            Assert.Equal(7, deserialized.NineElements.Item7);
            Assert.NotNull(deserialized.NineElements.Rest);
            Assert.Equal(88, deserialized.NineElements.Rest.Item1); // Item8
            Assert.Equal(99, deserialized.NineElements.Rest.Item2); // Item9
        }

        [Fact]
        public void Test_GP_NineElements()
        {
            // Arrange
            var original = new NinePlusElementTupleModel
            {
                NineElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    10, 20, 30, 40, 50, 60, 70,
                    new Tuple<int, int>(888, 999)
                )
            };

            // Act: Serialize with GProtobuf, deserialize with protobuf-net
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNinePlusElementTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<NinePlusElementTupleModel>(ms);

            // Assert
            Assert.NotNull(deserialized.NineElements);
            Assert.Equal(10, deserialized.NineElements.Item1);
            Assert.Equal(70, deserialized.NineElements.Item7);
            Assert.NotNull(deserialized.NineElements.Rest);
            Assert.Equal(888, deserialized.NineElements.Rest.Item1);
            Assert.Equal(999, deserialized.NineElements.Rest.Item2);
        }

        [Fact]
        public void Test_PG_TenElements()
        {
            // Arrange: 10-element tuple (Rest has 3 items)
            var original = new NinePlusElementTupleModel
            {
                TenElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int, int>(80, 90, 100) // Item8, Item9, Item10
                )
            };

            // Act
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNinePlusElementTupleModel(bytes);

            // Assert
            Assert.NotNull(deserialized.TenElements);
            Assert.Equal(1, deserialized.TenElements.Item1);
            Assert.NotNull(deserialized.TenElements.Rest);
            Assert.Equal(80, deserialized.TenElements.Rest.Item1);
            Assert.Equal(90, deserialized.TenElements.Rest.Item2);
            Assert.Equal(100, deserialized.TenElements.Rest.Item3);
        }

        [Fact]
        public void Test_PG_FourteenElements()
        {
            // Arrange: 14-element tuple (Rest has 7 items)
            var original = new NinePlusElementTupleModel
            {
                FourteenElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int, int, int, int, int, int>(8, 9, 10, 11, 12, 13, 14)
                )
            };

            // Act
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNinePlusElementTupleModel(bytes);

            // Assert
            Assert.NotNull(deserialized.FourteenElements);
            Assert.Equal(1, deserialized.FourteenElements.Item1);
            Assert.Equal(7, deserialized.FourteenElements.Item7);
            Assert.NotNull(deserialized.FourteenElements.Rest);
            Assert.Equal(8, deserialized.FourteenElements.Rest.Item1);
            Assert.Equal(14, deserialized.FourteenElements.Rest.Item7);
        }

        [Fact]
        public void Test_PG_FifteenElements_DoublyNested()
        {
            // Arrange: 15-element tuple (Rest has Tuple with Rest)
            // This is the most complex case!
            var original = new NinePlusElementTupleModel
            {
                FifteenElements = new Tuple<int, int, int, int, int, int, int,
                    Tuple<int, int, int, int, int, int, int, Tuple<int>>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int, int, int, int, int, int, Tuple<int>>(
                        8, 9, 10, 11, 12, 13, 14,
                        new Tuple<int>(15) // Item15
                    )
                )
            };

            // Act
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Debug: Print wire format
            Console.WriteLine($"15-element tuple wire format ({bytes.Length} bytes): {BitConverter.ToString(bytes)}");

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNinePlusElementTupleModel(bytes);

            // Assert: Navigate through doubly-nested structure
            Assert.NotNull(deserialized.FifteenElements);
            Assert.Equal(1, deserialized.FifteenElements.Item1);
            Assert.Equal(7, deserialized.FifteenElements.Item7);

            Assert.NotNull(deserialized.FifteenElements.Rest);
            Assert.Equal(8, deserialized.FifteenElements.Rest.Item1);
            Assert.Equal(14, deserialized.FifteenElements.Rest.Item7);

            // The inner Rest (Item15)
            Assert.NotNull(deserialized.FifteenElements.Rest.Rest);
            Assert.Equal(15, deserialized.FifteenElements.Rest.Rest.Item1);
        }

        [Fact]
        public void Test_GG_CompleteModel()
        {
            // Test all fields together
            var original = new NinePlusElementTupleModel
            {
                NineElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7, new Tuple<int, int>(8, 9)),
                TenElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>>(
                    10, 20, 30, 40, 50, 60, 70, new Tuple<int, int, int>(80, 90, 100)),
                FourteenElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int>>(
                    1, 2, 3, 4, 5, 6, 7, new Tuple<int, int, int, int, int, int, int>(8, 9, 10, 11, 12, 13, 14)),
                FifteenElements = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int, int, int, int, int, Tuple<int>>>(
                    1, 2, 3, 4, 5, 6, 7, new Tuple<int, int, int, int, int, int, int, Tuple<int>>(
                        8, 9, 10, 11, 12, 13, 14, new Tuple<int>(15)))
            };

            // Act: Round-trip through GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNinePlusElementTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers
                .DeserializeNinePlusElementTupleModel(bytes);

            // Assert: All fields preserved
            Assert.NotNull(deserialized.NineElements);
            Assert.Equal(9, deserialized.NineElements.Rest.Item2);

            Assert.NotNull(deserialized.TenElements);
            Assert.Equal(100, deserialized.TenElements.Rest.Item3);

            Assert.NotNull(deserialized.FourteenElements);
            Assert.Equal(14, deserialized.FourteenElements.Rest.Item7);

            Assert.NotNull(deserialized.FifteenElements);
            Assert.Equal(15, deserialized.FifteenElements.Rest.Rest.Item1);
        }
    }
}
