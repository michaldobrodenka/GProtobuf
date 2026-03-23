using System;
using System.IO;
using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Guid serialization in inheritance hierarchies.
    /// These tests verify cross-compatibility between GProtobuf and protobuf-net
    /// when Guid fields are present in base classes with ProtoInclude.
    /// </summary>
    public class GuidInheritanceTests
    {
        [Fact]
        public void BaseWithGuid_GProtobufToProtobufNet_GP()
        {
            // Arrange - simple base class with Guid
            var testGuid = new Guid("12345678-1234-5678-9abc-123456789abc");
            var model = new BaseWithGuid
            {
                Id = testGuid,
                Name = "Test Base"
            };

            // Act - serialize with GProtobuf
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeBaseWithGuid(stream, model);
            var bytes = stream.ToArray();

            // Act - deserialize with protobuf-net
            stream.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<BaseWithGuid>(stream);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Id.Should().Be(testGuid);
            deserialized.Name.Should().Be("Test Base");
        }

        [Fact]
        public void DerivedWithGuid_GProtobufToProtobufNet_GP()
        {
            // Arrange - derived class with Guid in base
            var testGuid = new Guid("87654321-4321-8765-dcba-876543218765");
            var model = new DerivedWithGuid
            {
                Id = testGuid,
                Name = "Test Derived",
                Value = 42,
                Description = "Test Description"
            };

            // Act - serialize with GProtobuf (as base type to include ProtoInclude wrapper)
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeBaseWithGuid(stream, model);
            var bytes = stream.ToArray();

            // Act - deserialize with protobuf-net
            stream.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<BaseWithGuid>(stream);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Should().BeOfType<DerivedWithGuid>();
            var derived = (DerivedWithGuid)deserialized;
            derived.Id.Should().Be(testGuid, "Guid in base class should be preserved");
            derived.Name.Should().Be("Test Derived");
            derived.Value.Should().Be(42);
            derived.Description.Should().Be("Test Description");
        }

        [Fact]
        public void ContainerWithGuidItems_GProtobufToProtobufNet_GP()
        {
            // Arrange - container with array of items (similar to DashboardModel scenario)
            var guid1 = new Guid("11111111-1111-1111-1111-111111111111");
            var guid2 = new Guid("22222222-2222-2222-2222-222222222222");

            var model = new ContainerWithGuidItems
            {
                Items = new BaseWithGuid[]
                {
                    new DerivedWithGuid
                    {
                        Id = guid1,
                        Name = "First",
                        Value = 100,
                        Description = "First item"
                    },
                    new DerivedWithGuid
                    {
                        Id = guid2,
                        Name = "Second",
                        Value = 200,
                        Description = "Second item"
                    }
                }
            };

            // Act - serialize with GProtobuf
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeContainerWithGuidItems(stream, model);
            var bytes = stream.ToArray();

            // Act - deserialize with protobuf-net
            stream.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<ContainerWithGuidItems>(stream);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Items.Should().HaveCount(2);

            var first = deserialized.Items[0] as DerivedWithGuid;
            first.Should().NotBeNull();
            first.Id.Should().Be(guid1, "First item Guid should be preserved");
            first.Name.Should().Be("First");
            first.Value.Should().Be(100);

            var second = deserialized.Items[1] as DerivedWithGuid;
            second.Should().NotBeNull();
            second.Id.Should().Be(guid2, "Second item Guid should be preserved");
            second.Name.Should().Be("Second");
            second.Value.Should().Be(200);
        }

        [Fact]
        public void DerivedWithGuid_ProtobufNetToGProtobuf_PG()
        {
            // Arrange - verify protobuf-net can serialize and GProtobuf can deserialize
            var testGuid = new Guid("abcdef12-3456-7890-abcd-ef1234567890");
            var model = new DerivedWithGuid
            {
                Id = testGuid,
                Name = "Test Derived PG",
                Value = 99,
                Description = "PG Test"
            };

            // Act - serialize with protobuf-net
            var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, (BaseWithGuid)model);
            var bytes = stream.ToArray();

            // Act - deserialize with GProtobuf
            var deserialized = TestModel.Serialization.Deserializers.DeserializeBaseWithGuid(bytes);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Should().BeOfType<DerivedWithGuid>();
            var derived = (DerivedWithGuid)deserialized;
            derived.Id.Should().Be(testGuid);
            derived.Name.Should().Be("Test Derived PG");
            derived.Value.Should().Be(99);
            derived.Description.Should().Be("PG Test");
        }

        [Fact]
        public void DerivedWithGuid_Bidirectional_GPGP()
        {
            // Arrange - full bidirectional test
            var testGuid = new Guid("fedcba98-7654-3210-fedc-ba9876543210");
            var original = new DerivedWithGuid
            {
                Id = testGuid,
                Name = "Bidirectional Test",
                Value = 123,
                Description = "Full roundtrip"
            };

            // GProtobuf -> protobuf-net -> GProtobuf
            var stream1 = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeBaseWithGuid(stream1, original);
            stream1.Position = 0;

            var protobufNetDeserialized = ProtoBuf.Serializer.Deserialize<BaseWithGuid>(stream1);

            var stream2 = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream2, protobufNetDeserialized);
            var bytes2 = stream2.ToArray();

            var finalDeserialized = TestModel.Serialization.Deserializers.DeserializeBaseWithGuid(bytes2);

            // Assert
            finalDeserialized.Should().NotBeNull();
            finalDeserialized.Should().BeOfType<DerivedWithGuid>();
            var final = (DerivedWithGuid)finalDeserialized;
            final.Id.Should().Be(testGuid);
            final.Name.Should().Be("Bidirectional Test");
            final.Value.Should().Be(123);
            final.Description.Should().Be("Full roundtrip");
        }

        [Fact]
        public void DerivedWithGuid_EmptyGuid_GP()
        {
            // Arrange - test with Guid.Empty
            var model = new DerivedWithGuid
            {
                Id = Guid.Empty,
                Name = "Empty Guid Test",
                Value = 0,
                Description = "Testing Guid.Empty"
            };

            // Act - serialize with GProtobuf
            var stream = new MemoryStream();
            TestModel.Serialization.Serializers.SerializeBaseWithGuid(stream, model);
            var bytes = stream.ToArray();

            // Act - deserialize with protobuf-net
            stream.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<BaseWithGuid>(stream);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Should().BeOfType<DerivedWithGuid>();
            var derived = (DerivedWithGuid)deserialized;
            derived.Id.Should().Be(Guid.Empty);
            derived.Name.Should().Be("Empty Guid Test");
        }

    }
}
