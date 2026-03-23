using System.IO;
using FluentAssertions;
using ProtoBuf;
using Xunit;
using GProtobuf.CrossTests.TestModel;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for nested class serialization - classes with [ProtoContract] inside other classes.
    /// </summary>
    public class NestedClassSerializationTests
    {
        #region OuterClass with InnerClass tests

        [Fact]
        public void Test_GG_NestedClass_BasicRoundTrip()
        {
            // Arrange
            var original = new OuterClass
            {
                OuterId = 42,
                OuterName = "Outer",
                Inner = new OuterClass.InnerClass
                {
                    Id = 100,
                    ByteValue = 255
                }
            };

            // Act - serialize with GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOuterClass(ms, original);
            var bytes = ms.ToArray();

            // Deserialize with GProtobuf
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOuterClass(bytes);

            // Assert
            deserialized.OuterId.Should().Be(42);
            deserialized.OuterName.Should().Be("Outer");
            deserialized.Inner.Should().NotBeNull();
            deserialized.Inner.Id.Should().Be(100);
            deserialized.Inner.ByteValue.Should().Be(255);
        }

        [Fact]
        public void Test_GG_NestedClass_NullInner()
        {
            // Arrange
            var original = new OuterClass
            {
                OuterId = 1,
                OuterName = "Test",
                Inner = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOuterClass(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOuterClass(bytes);

            // Assert
            deserialized.OuterId.Should().Be(1);
            deserialized.OuterName.Should().Be("Test");
            deserialized.Inner.Should().BeNull();
        }

        [Fact]
        public void Test_GP_NestedClass_ProtobufNetCanRead()
        {
            // Arrange - serialize with GProtobuf
            var original = new OuterClass
            {
                OuterId = 77,
                OuterName = "GProtobuf",
                Inner = new OuterClass.InnerClass
                {
                    Id = 123,
                    ByteValue = 200
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeOuterClass(ms, original);
            var bytes = ms.ToArray();

            // Act - deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<OuterClass>(ms);

            // Assert
            deserialized.OuterId.Should().Be(77);
            deserialized.OuterName.Should().Be("GProtobuf");
            deserialized.Inner.Should().NotBeNull();
            deserialized.Inner.Id.Should().Be(123);
            deserialized.Inner.ByteValue.Should().Be(200);
        }

        [Fact]
        public void Test_PG_NestedClass_GProtobufCanRead()
        {
            // Arrange - serialize with protobuf-net
            var original = new OuterClass
            {
                OuterId = 88,
                OuterName = "ProtobufNet",
                Inner = new OuterClass.InnerClass
                {
                    Id = 456,
                    ByteValue = 128
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act - deserialize with GProtobuf
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeOuterClass(bytes);

            // Assert
            deserialized.OuterId.Should().Be(88);
            deserialized.OuterName.Should().Be("ProtobufNet");
            deserialized.Inner.Should().NotBeNull();
            deserialized.Inner.Id.Should().Be(456);
            deserialized.Inner.ByteValue.Should().Be(128);
        }

        #endregion

        #region Multi-level nesting tests

        [Fact]
        public void Test_GG_ThreeLevelNesting_RoundTrip()
        {
            // Arrange
            var original = new Level1Class
            {
                Level1Id = 1,
                Level2 = new Level1Class.Level2Class
                {
                    Level2Id = 2,
                    Level3 = new Level1Class.Level2Class.Level3Class
                    {
                        Level3Id = 3,
                        Value = "DeepValue"
                    }
                }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLevel1Class(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeLevel1Class(bytes);

            // Assert
            deserialized.Level1Id.Should().Be(1);
            deserialized.Level2.Should().NotBeNull();
            deserialized.Level2.Level2Id.Should().Be(2);
            deserialized.Level2.Level3.Should().NotBeNull();
            deserialized.Level2.Level3.Level3Id.Should().Be(3);
            deserialized.Level2.Level3.Value.Should().Be("DeepValue");
        }

        [Fact]
        public void Test_GP_ThreeLevelNesting_ProtobufNetCanRead()
        {
            // Arrange
            var original = new Level1Class
            {
                Level1Id = 10,
                Level2 = new Level1Class.Level2Class
                {
                    Level2Id = 20,
                    Level3 = new Level1Class.Level2Class.Level3Class
                    {
                        Level3Id = 30,
                        Value = "CrossTest"
                    }
                }
            };

            // Act - serialize with GProtobuf
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeLevel1Class(ms, original);

            // Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = Serializer.Deserialize<Level1Class>(ms);

            // Assert
            deserialized.Level1Id.Should().Be(10);
            deserialized.Level2.Level2Id.Should().Be(20);
            deserialized.Level2.Level3.Level3Id.Should().Be(30);
            deserialized.Level2.Level3.Value.Should().Be("CrossTest");
        }

        #endregion

        #region Multiple nested classes tests

        [Fact]
        public void Test_GG_MultipleNestedClasses_RoundTrip()
        {
            // Arrange
            var original = new ContainerClass
            {
                ContainerId = 999,
                A = new ContainerClass.NestedA { ValueA = 111 },
                B = new ContainerClass.NestedB { ValueB = "Hello", NumberB = 3.14 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeContainerClass(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeContainerClass(bytes);

            // Assert
            deserialized.ContainerId.Should().Be(999);
            deserialized.A.Should().NotBeNull();
            deserialized.A.ValueA.Should().Be(111);
            deserialized.B.Should().NotBeNull();
            deserialized.B.ValueB.Should().Be("Hello");
            deserialized.B.NumberB.Should().Be(3.14);
        }

        [Fact]
        public void Test_GG_MultipleNestedClasses_PartiallyNull()
        {
            // Arrange
            var original = new ContainerClass
            {
                ContainerId = 500,
                A = new ContainerClass.NestedA { ValueA = 50 },
                B = null
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeContainerClass(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeContainerClass(bytes);

            // Assert
            deserialized.ContainerId.Should().Be(500);
            deserialized.A.ValueA.Should().Be(50);
            deserialized.B.Should().BeNull();
        }

        [Fact]
        public void Test_GP_MultipleNestedClasses_ProtobufNetCanRead()
        {
            // Arrange
            var original = new ContainerClass
            {
                ContainerId = 777,
                A = new ContainerClass.NestedA { ValueA = 7 },
                B = new ContainerClass.NestedB { ValueB = "Seven", NumberB = 7.77 }
            };

            // Act
            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeContainerClass(ms, original);

            ms.Position = 0;
            var deserialized = Serializer.Deserialize<ContainerClass>(ms);

            // Assert
            deserialized.ContainerId.Should().Be(777);
            deserialized.A.ValueA.Should().Be(7);
            deserialized.B.ValueB.Should().Be("Seven");
            deserialized.B.NumberB.Should().Be(7.77);
        }

        #endregion
    }
}
