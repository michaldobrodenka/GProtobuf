using FluentAssertions;
using GProtobuf.Tests.TestModel;
//using GProtobuf.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System.Reflection;
using System.Text;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// PG (Protobuf-net to GProtobuf) Tests
/// 
/// These tests verify cross-compatibility between protobuf-net and GProtobuf:
/// - Data is serialized using protobuf-net (reference implementation)
/// - Data is deserialized using GProtobuf-generated deserializers
/// - Ensures GProtobuf can correctly read protobuf-net serialized data
/// - Critical for backward compatibility and interoperability
/// 
/// Test scenarios covered:
/// - Basic hierarchy serialization (A -> B -> C inheritance)
/// - Partial property serialization (only specific fields set)
/// - Cross-type serialization (serialize as derived, deserialize as base)
/// - Large data handling (long strings)
/// </summary>
public sealed class ProtobufNetToGProtobufTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _outputHelper;
    
    public ProtobufNetToGProtobufTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void ABCBasicHierarchy_PG()
    {
        var model = new C()
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyC_PG()
    {
        var model = new C()
        {
            StringC = "StringC"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyA_PG()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }


    [Fact]
    public void ABCSerializeAsC_PG()
    {
        // C is the most derived type, should be able to be deserialized as A
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCSerializeLongString_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesAllValues_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));
        //var deserialized = DeserializeWithProtobufNet<BasicTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesDefaultValues_PG()
    {
        var model = new BasicTypesModel(); // All default values

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesMinMaxValues_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesNullValues_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagAllValues_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagNegativeValues_PG()
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

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void GuidValue_SerializeDeserialize_PG()
    {
        // Test basic Guid serialization/deserialization from protobuf-net to GProtobuf
        var testGuid = new Guid("12345678-1234-5678-9abc-123456789abc");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(bytes));

        deserialized.Should().NotBeNull();
        deserialized.GuidValue.Should().Be(testGuid, "specific Guid should roundtrip correctly");
        deserialized.EmptyGuidValue.Should().Be(Guid.Empty, "Guid.Empty should roundtrip correctly");
        deserialized.AnotherGuidValue.Should().Be(model.AnotherGuidValue, "generated Guid should roundtrip correctly");
    }

    [Fact]
    public void GuidEmpty_ShouldNotSerialize_PG()
    {
        // Test that Guid.Empty values are not serialized by protobuf-net (same as null for reference types)
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = Guid.Empty,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var data = SerializeWithProtobufNet(model);
        
        // All fields are Guid.Empty, so the serialized data should be minimal (just empty message)
        data.Should().NotBeNull();
        data.Length.Should().Be(0, "protobuf-net should not serialize Guid.Empty values, resulting in empty data");

        // Verify GProtobuf can still deserialize the empty data correctly
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(bytes));
        deserialized.Should().NotBeNull();
        deserialized.GuidValue.Should().Be(Guid.Empty);
        deserialized.EmptyGuidValue.Should().Be(Guid.Empty);
        deserialized.AnotherGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void GuidValue_CrossCompatibility_PG()
    {
        // Test cross-compatibility: protobuf-net serialization -> GProtobuf deserialization
        var testGuid = Guid.Parse("12030201-0000-0000-1100-000000000001");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(bytes));

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.GuidValue.Should().Be(testGuid, "GProtobuf should correctly deserialize protobuf-net-serialized Guid");
        gprotobufDeserialized.EmptyGuidValue.Should().Be(Guid.Empty, "empty Guid should remain empty");
        gprotobufDeserialized.AnotherGuidValue.Should().Be(Guid.Empty, "empty Guid should remain empty");
    }
}
