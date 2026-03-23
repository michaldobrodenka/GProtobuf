using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using System.Collections.Generic;
using System.IO;
using Xunit;
using System;

namespace GProtobuf.CrossTests
{
    public class HashSetModelTests : BaseSerializationTest
    {
        private HashSetTestModel RoundTripTestGG(HashSetTestModel model)
        {
            // GProtobuf -> GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeHashSetTestModel);
            return DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeHashSetTestModel(bytes));
        }
        
        private void TestCrossCompatibility(HashSetTestModel model, Action<HashSetTestModel> assertions)
        {
            // Test PG: protobuf-net -> GProtobuf
            var protobufData = SerializeWithProtobufNet(model);
            var gprotobufResult = DeserializeWithGProtobuf(protobufData, 
                bytes => TestModel.Serialization.Deserializers.DeserializeHashSetTestModel(bytes));
            assertions(gprotobufResult);
            
            // Test GP: GProtobuf -> protobuf-net
            var gprotobufData = SerializeWithGProtobuf(model, 
                TestModel.Serialization.Serializers.SerializeHashSetTestModel);
            var protobufResult = DeserializeWithProtobufNet<HashSetTestModel>(gprotobufData);
            assertions(protobufResult);
        }
        [Fact]
        public void Should_serialize_and_deserialize_int_HashSet()
        {
            var model = new HashSetTestModel
            {
                UniqueNumbers = new HashSet<int> { 1, 2, 3, 4, 5, 1, 2 } // Should contain only unique values
            };

            var result = RoundTripTestGG(model);

            result.UniqueNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
            result.UniqueNumbers.Should().BeOfType<HashSet<int>>();
        }

        [Fact]
        public void Should_serialize_and_deserialize_string_HashSet()
        {
            var model = new HashSetTestModel
            {
                UniqueTags = new HashSet<string> { "tag1", "tag2", "tag3", "tag1" }
            };

            var result = RoundTripTestGG(model);

            result.UniqueTags.Should().BeEquivalentTo(new[] { "tag1", "tag2", "tag3" });
            result.UniqueTags.Should().BeOfType<HashSet<string>>();
        }

        [Fact]
        public void Should_serialize_and_deserialize_complex_type_HashSet()
        {
            var model = new HashSetTestModel
            {
                UniqueMessages = new HashSet<SimpleMessage>
                {
                    new SimpleMessage { Id = 1, Name = "First" },
                    new SimpleMessage { Id = 2, Name = "Second" },
                    new SimpleMessage { Id = 1, Name = "First" } // Duplicate should be ignored
                }
            };

            var result = RoundTripTestGG(model);

            result.UniqueMessages.Should().HaveCount(2);
            result.UniqueMessages.Should().BeOfType<HashSet<SimpleMessage>>();
            result.UniqueMessages.Should().Contain(m => m.Id == 1 && m.Name == "First");
            result.UniqueMessages.Should().Contain(m => m.Id == 2 && m.Name == "Second");
        }

        [Fact]
        public void Should_serialize_and_deserialize_packed_float_HashSet()
        {
            var model = new HashSetTestModel
            {
                PackedFloats = new HashSet<float> { 1.5f, 2.5f, 3.5f, 1.5f }
            };

            var result = RoundTripTestGG(model);

            result.PackedFloats.Should().BeEquivalentTo(new[] { 1.5f, 2.5f, 3.5f });
            result.PackedFloats.Should().BeOfType<HashSet<float>>();
        }

        [Fact]
        public void Should_serialize_and_deserialize_byte_HashSet()
        {
            var model = new HashSetTestModel
            {
                ByteSet = new HashSet<byte> { 1, 2, 3, 4, 255, 0, 1 }
            };

            var result = RoundTripTestGG(model);

            // HashSet doesn't maintain order, just check contents
            result.ByteSet.Should().BeOfType<HashSet<byte>>();
            result.ByteSet.Count.Should().Be(6);
            result.ByteSet.Should().Contain(new byte[] { 0, 1, 2, 3, 4, 255 });
        }

        [Fact]
        public void Should_handle_empty_HashSet()
        {
            var model = new HashSetTestModel
            {
                UniqueNumbers = new HashSet<int>(),
                UniqueTags = new HashSet<string>()
            };

            var result = RoundTripTestGG(model);

            // Empty collections might be deserialized as null in protobuf
            result.UniqueNumbers.Should().BeNullOrEmpty();
            result.UniqueTags.Should().BeNullOrEmpty();
        }

        [Fact]
        public void Should_handle_null_HashSet()
        {
            var model = new HashSetTestModel
            {
                UniqueNumbers = null,
                UniqueTags = null
            };

            var result = RoundTripTestGG(model);

            result.UniqueNumbers.Should().BeNull();
            result.UniqueTags.Should().BeNull();
        }

        [Fact]
        public void Should_serialize_ZigZag_int_HashSet()
        {
            var model = new HashSetTestModel
            {
                ZigZagIntSet = new HashSet<int> { -1, -2, 1, 2, 0 }
            };

            var result = RoundTripTestGG(model);

            result.ZigZagIntSet.Should().BeEquivalentTo(new[] { -2, -1, 0, 1, 2 });
            result.ZigZagIntSet.Should().BeOfType<HashSet<int>>();
        }

        [Fact]
        public void Should_serialize_FixedSize_int_HashSet()
        {
            var model = new HashSetTestModel
            {
                FixedSizeIntSet = new HashSet<int> { 100, 200, 300 }
            };

            var result = RoundTripTestGG(model);

            result.FixedSizeIntSet.Should().BeEquivalentTo(new[] { 100, 200, 300 });
            result.FixedSizeIntSet.Should().BeOfType<HashSet<int>>();
        }

        [Fact]
        public void Should_maintain_compatibility_with_protobuf_net()
        {
            var model = new HashSetTestModel
            {
                UniqueNumbers = new HashSet<int> { 1, 2, 3 },
                UniqueTags = new HashSet<string> { "a", "b", "c" }
            };

            // Test cross-compatibility
            TestCrossCompatibility(model, m =>
            {
                m.UniqueNumbers.Should().BeEquivalentTo(new[] { 1, 2, 3 });
                m.UniqueTags.Should().BeEquivalentTo(new[] { "a", "b", "c" });
            });
        }
    }
}