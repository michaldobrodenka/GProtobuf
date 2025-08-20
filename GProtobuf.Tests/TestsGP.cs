using FluentAssertions;
using GProtobuf.Tests.TestModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System.Reflection;
using System.Text;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// GP (GProtobuf to Protobuf-net) Tests
/// 
/// These tests verify cross-compatibility between GProtobuf and protobuf-net:
/// - Data is serialized using GProtobuf-generated serializers
/// - Data is deserialized using protobuf-net (reference implementation)
/// - Ensures protobuf-net can correctly read GProtobuf serialized data
/// - Critical for forward compatibility and interoperability
/// 
/// Test scenarios covered:
/// - Basic hierarchy serialization (A -> B -> C inheritance)
/// - Partial property serialization (only specific fields set)
/// - Cross-type serialization (serialize as derived, deserialize as base)
/// - Large data handling (long strings)
/// </summary>
public sealed class GProtobufToProtobufNetTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _outputHelper;
    
    public GProtobufToProtobufNetTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void ABCBasicHierarchy_GP()
    {
        var model = new C()
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithProtobufNet<C>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyC_GP()
    {
        var model = new C()
        {
            StringC = "StringC"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithProtobufNet<C>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyA_GP()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithProtobufNet<C>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }


    [Fact]
    public void ABCSerializeAsC_GP()
    {
        // C is the most derived type, should be able to be deserialized as A
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeC);
        var deserialized = DeserializeWithProtobufNet<C>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCSerializeLongString_GP()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"abc{i}");
        }
        model.StringC = sb.ToString();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeC);
        var deserialized = DeserializeWithProtobufNet<C>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesAllValues_GP()
    {
        var model = new BasicTypesModel
        {
            ByteValue = 255,
            SByteValue = -128,
            ShortValue = -32768,
            UShortValue = 65535,
            IntValue = -123456,
            UIntValue = 4294967295,
            LongValue = -987654321012345,
            ULongValue = 18446744073709551615,
            FloatValue = 3.14159f,
            DoubleValue = 2.718281828459045,
            BoolValue = true,
            StringValue = "Hello, Protocol Buffers!",
            BytesValue = new byte[] { 1, 2, 3, 4, 5, 255, 0, 127 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesDefaultValues_GP()
    {
        var model = new BasicTypesModel(); // All default values

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesModel>(data);

        data.Should().NotBeNull();
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesMinMaxValues_GP()
    {
        var model = new BasicTypesModel
        {
            ByteValue = byte.MaxValue,
            SByteValue = sbyte.MinValue,
            ShortValue = short.MinValue,
            UShortValue = ushort.MaxValue,
            IntValue = int.MinValue,
            UIntValue = uint.MaxValue,
            LongValue = long.MinValue,
            ULongValue = ulong.MaxValue,
            FloatValue = float.MinValue,
            DoubleValue = double.MaxValue,
            BoolValue = false,
            StringValue = "",
            BytesValue = new byte[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesNullValues_GP()
    {
        var model = new BasicTypesModel
        {
            ByteValue = 42,
            IntValue = 12345,
            DoubleValue = 123.456,
            BoolValue = true,
            StringValue = null, // Null string
            BytesValue = null   // Null byte array
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagAllValues_GP()
    {
        var model = new BasicTypesZigZagModel
        {
            ByteValue = 255,
            SByteValue = -128,
            ShortValue = -32768,
            UShortValue = 65535,
            IntValue = -123456,
            UIntValue = 4294967295,
            LongValue = -987654321012345,
            ULongValue = 18446744073709551615,
            FloatValue = 3.14159f,
            DoubleValue = 2.718281828459045,
            BoolValue = true,
            StringValue = "Hello, ZigZag Protocol Buffers!",
            BytesValue = new byte[] { 1, 2, 3, 4, 5, 255, 0, 127 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesZigZagModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagNegativeValues_GP()
    {
        var model = new BasicTypesZigZagModel
        {
            SByteValue = -1,
            ShortValue = -1000,
            IntValue = -123456,
            LongValue = -987654321012345,
            FloatValue = -123.456f,
            DoubleValue = -789.012345,
            BoolValue = false,
            StringValue = "Negative ZigZag test",
            BytesValue = new byte[] { 255, 128, 0 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesZigZagModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void GuidValue_SerializeDeserialize_GP()
    {
        // Test basic Guid serialization/deserialization from GProtobuf to protobuf-net
        var testGuid = new Guid("12345678-1234-5678-9abc-123456789abc");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.GuidTypesModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.GuidValue.Should().Be(testGuid, "specific Guid should roundtrip correctly");
        deserialized.EmptyGuidValue.Should().Be(Guid.Empty, "Guid.Empty should roundtrip correctly");
        deserialized.AnotherGuidValue.Should().Be(model.AnotherGuidValue, "generated Guid should roundtrip correctly");
    }

    [Fact]
    public void GuidEmpty_ShouldNotSerialize_GP()
    {
        // Test that Guid.Empty values are not serialized (same as null for reference types)
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = Guid.Empty,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        
        // All fields are Guid.Empty, so the serialized data should be minimal (just empty message)
        data.Should().NotBeNull();
        data.Length.Should().Be(0, "Guid.Empty values should not be serialized, resulting in empty data");

        // Verify protobuf-net can still deserialize the empty data correctly
        var deserialized = DeserializeWithProtobufNet<TestModel.GuidTypesModel>(data);
        deserialized.Should().NotBeNull();
        deserialized.GuidValue.Should().Be(Guid.Empty);
        deserialized.EmptyGuidValue.Should().Be(Guid.Empty);
        deserialized.AnotherGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void GuidValue_CrossCompatibility_GP()
    {
        // Test cross-compatibility: GProtobuf serialization -> protobuf-net deserialization
        var testGuid = Guid.Parse("12030201-0000-0000-1100-000000000001");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<TestModel.GuidTypesModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.GuidValue.Should().Be(testGuid, "protobuf-net should correctly deserialize GProtobuf-serialized Guid");
        protobufNetDeserialized.EmptyGuidValue.Should().Be(Guid.Empty, "empty Guid should remain empty");
        protobufNetDeserialized.AnotherGuidValue.Should().Be(Guid.Empty, "empty Guid should remain empty");
    }

    [Fact]
    public void NullableTypes_CompatibleWithProtobufNet_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = 123456789L,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = true
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().Be(123456789L);
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().Be(true);
    }

    [Fact]
    public void NullableTypes_GuidCompatibleWithProtobufNet_GP()
    {
        var testGuid = Guid.NewGuid();
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableGuidValue.Should().Be(testGuid);
    }
}
