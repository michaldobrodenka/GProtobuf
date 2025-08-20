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

    [Fact]
    public void GuidValue_SerializeDeserialize_GG()
    {
        // Test with a specific known Guid value
        var testGuid = new Guid("12345678-1234-5678-9abc-123456789abc");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithGProtobuf(model, GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(bytes));

        deserialized.Should().NotBeNull();
        deserialized.GuidValue.Should().Be(testGuid, "specific Guid should roundtrip correctly");
        deserialized.EmptyGuidValue.Should().Be(Guid.Empty, "Guid.Empty should roundtrip correctly");
        deserialized.AnotherGuidValue.Should().Be(model.AnotherGuidValue, "generated Guid should roundtrip correctly");
    }

    [Fact]
    public void GuidEmpty_ShouldNotSerialize_GG()
    {
        // Test that Guid.Empty values are not serialized (same as null for reference types)
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = Guid.Empty,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var data = SerializeWithGProtobuf(model, GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        
        // All fields are Guid.Empty, so the serialized data should be minimal (just empty message)
        data.Should().NotBeNull();
        data.Length.Should().Be(0, "Guid.Empty values should not be serialized, resulting in empty data");
    }

    [Fact]
    public void GuidValue_CompatibilityWithProtobufNet_GG()
    {
        // Test specific Guid with known protobuf-net serialization
        var testGuid = Guid.Parse("12030201-0000-0000-1100-000000000001");
        var model = new TestModel.GuidTypesModel
        {
            GuidValue = testGuid,
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.Empty
        };

        var gprotobufData = SerializeWithGProtobuf(model, GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        // The serialized data should match between GProtobuf and protobuf-net
        gprotobufData.Should().Equal(protobufNetData, "GProtobuf serialization should match protobuf-net");

        // Both should deserialize to the same object
        var gprotobufDeserialized = DeserializeWithGProtobuf(gprotobufData, bytes => GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(bytes));
        var protobufNetDeserialized = DeserializeWithProtobufNet<TestModel.GuidTypesModel>(protobufNetData);

        gprotobufDeserialized.Should().BeEquivalentTo(protobufNetDeserialized);
    }

    [Fact]
    public void NullableTypes_AllNull_GG()
    {
        var model = new TestModel.NullableTypesModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0, "no fields should be serialized when all nullable values are null");
        
        deserialized.Should().BeEquivalentTo(model);
        deserialized.NullableIntValue.Should().BeNull();
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().BeNull();
        deserialized.NullableBoolValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_WithZeroValues_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 0,
            NullableLongValue = 0L,
            NullableDoubleValue = 0.0,
            NullableBoolValue = false,
            NullableByteValue = 0,
            NullableFloatValue = 0.0f
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0, "nullable types with zero values should still be serialized");
        
        deserialized.Should().BeEquivalentTo(model);
        deserialized.NullableIntValue.Should().Be(0);
        deserialized.NullableLongValue.Should().Be(0L);
        deserialized.NullableDoubleValue.Should().Be(0.0);
        deserialized.NullableBoolValue.Should().Be(false);
        deserialized.NullableByteValue.Should().Be(0);
        deserialized.NullableFloatValue.Should().Be(0.0f);
    }

    [Fact]
    public void NullableTypes_WithNonZeroValues_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = 123456789L,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = true,
            NullableByteValue = 255,
            NullableFloatValue = 2.71828f,
            NullableShortValue = -1000,
            NullableUShortValue = 65535,
            NullableUIntValue = 4294967295,
            NullableULongValue = 18446744073709551615,
            NullableSByteValue = -128
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void NullableTypes_MixedNullAndValues_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 100,
            NullableLongValue = null,
            NullableDoubleValue = 99.99,
            NullableBoolValue = null,
            NullableByteValue = 50,
            NullableFloatValue = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        
        deserialized.NullableIntValue.Should().Be(100);
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().Be(99.99);
        deserialized.NullableBoolValue.Should().BeNull();
        deserialized.NullableByteValue.Should().Be(50);
        deserialized.NullableFloatValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_ZigZagEncoding_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableZigZagIntValue = -42,
            NullableZigZagLongValue = -123456789L
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        
        deserialized.NullableZigZagIntValue.Should().Be(-42);
        deserialized.NullableZigZagLongValue.Should().Be(-123456789L);
    }

    [Fact]
    public void NullableTypes_FixedSizeEncoding_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableFixedSizeIntValue = 987654321
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        
        deserialized.NullableFixedSizeIntValue.Should().Be(987654321);
    }
}
