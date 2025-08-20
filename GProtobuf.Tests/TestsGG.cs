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
/// GG (GProtobuf to GProtobuf) Tests
/// 
/// These tests verify serialization and deserialization using only GProtobuf:
/// - Data is serialized using GProtobuf-generated serializers
/// - Data is deserialized using GProtobuf-generated deserializers
/// - Ensures internal consistency of GProtobuf implementation
/// - Tests various inheritance scenarios with ProtoInclude attributes
/// 
/// Test scenarios covered:
/// - Basic hierarchy serialization (A -> B -> C inheritance)
/// - Partial property serialization (only specific fields set)
/// - Cross-type serialization (serialize as derived, deserialize as base)
/// - Large data handling (long strings)
/// </summary>
public sealed class GProtobufToGProtobufTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _outputHelper;
    
    public GProtobufToGProtobufTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void ABCBasicHierarchy_GG()
    {
        var model = new C()
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyC_GG()
    {
        var model = new C()
        {
            StringC = "StringC"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyA_GG()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }


    [Fact]
    public void ABCSerializeAsC_GG()
    {
        // C is the most derived type, should be able to be deserialized as A
        var model = new C()
        {
            StringA = "StringA"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeC);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCSerializeLongString_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesAllValues_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesDefaultValues_GG()
    {
        var model = new BasicTypesModel(); // All default values

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesMinMaxValues_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesNullValues_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagAllValues_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagNegativeValues_GG()
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypesZigZagMinMaxValues_GG()
    {
        // Test extreme values that might be problematic with ZigZag encoding
        var model = new BasicTypesZigZagModel
        {
            SByteValue = sbyte.MinValue,
            ShortValue = short.MinValue,
            IntValue = int.MinValue,
            LongValue = long.MinValue,
            StringValue = "Min/Max values test"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void LongMinValueZigZag_GG()
    {
        // Test the specific case of long.MinValue with ZigZag encoding
        var model = new SimpleTypesZigZag
        {
            LongValue = long.MinValue // -9223372036854775808
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeSimpleTypesZigZag);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeSimpleTypesZigZag(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
        
        // Verify the serialized data matches protobuf-net output
        var expectedBytes = new byte[] { 8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 1 };
        data.Should().Equal(expectedBytes, "serialized data should match protobuf-net output");
    }

    [Fact] 
    public void LongMinValueZigZag_DeserializeProtobufNetData_GG()
    {
        // Test deserializing the exact bytes that protobuf-net generates for long.MinValue with ZigZag
        var protobufNetData = new byte[] { 8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 1 };
        
        var deserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeSimpleTypesZigZag(bytes));
        
        deserialized.Should().NotBeNull();
        deserialized.LongValue.Should().Be(long.MinValue, "should correctly deserialize long.MinValue from protobuf-net data");
    }
}
