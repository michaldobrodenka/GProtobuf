using FluentAssertions;
using GProtobuf.Tests.TestModel;
//using GProtobuf.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System;
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

    [Fact]
    public void NullableTypes_CompatibleWithProtobufNet_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = 123456789L,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = true
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().Be(123456789L);
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().Be(true);
    }

    [Fact]
    public void NullableTypes_GuidCompatibleWithProtobufNet_PG()
    {
        var testGuid = Guid.NewGuid();
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void ArrayTypes_CompatibleWithProtobufNet_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000 },
            IntArrayNonPacked = new[] { -1, -2, 0, 50 },
            ByteArray = new byte[] { 0, 1, 255, 128, 42 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000);
        deserialized.IntArrayNonPacked.Should().Equal(-1, -2, 0, 50);
        deserialized.ByteArray.Should().Equal(new byte[] { 0, 1, 255, 128, 42 });
    }

    [Fact]
    public void BasicTypesZigZagMinMaxValues_PG()
    {
        var model = new BasicTypesZigZagModel
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
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void LongMinValueZigZag_PG()
    {
        // Test the edge case of long.MinValue with ZigZag encoding
        var model = new BasicTypesZigZagModel
        {
            LongValue = long.MinValue, // -9223372036854775808
            StringValue = "LongMinValue test"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().NotBeNull();
        deserialized.LongValue.Should().Be(long.MinValue, "long.MinValue should be preserved through ZigZag encoding");
        deserialized.StringValue.Should().Be("LongMinValue test");
    }

    [Fact]
    public void LongMinValueZigZag_DeserializeProtobufNetData_PG()
    {
        // Create expected protobuf-net serialized data for long.MinValue ZigZag encoded
        var protobufNetModel = new BasicTypesZigZagModel { LongValue = long.MinValue };
        var protobufNetData = SerializeWithProtobufNet(protobufNetModel);
        
        var deserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(bytes));
        deserialized.LongValue.Should().Be(long.MinValue);
    }

    [Fact]
    public void NullableTypes_AllNull_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = null,
            NullableLongValue = null,
            NullableDoubleValue = null,
            NullableBoolValue = null,
            NullableGuidValue = null
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableIntValue.Should().BeNull();
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().BeNull();
        deserialized.NullableBoolValue.Should().BeNull();
        deserialized.NullableGuidValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_WithZeroValues_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 0,
            NullableLongValue = 0L,
            NullableDoubleValue = 0.0,
            NullableBoolValue = false,
            NullableGuidValue = Guid.Empty
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        // Nullable types with 0 values should serialize and deserialize correctly
        deserialized.NullableIntValue.Should().Be(0);
        deserialized.NullableLongValue.Should().Be(0L);
        deserialized.NullableDoubleValue.Should().Be(0.0);
        deserialized.NullableBoolValue.Should().Be(false);
        deserialized.NullableGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void NullableTypes_WithNonZeroValues_PG()
    {
        var testGuid = Guid.NewGuid();
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = 123456789L,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = true,
            NullableGuidValue = testGuid
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().Be(123456789L);
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().Be(true);
        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void NullableTypes_MixedNullAndValues_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = null,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = null,
            NullableGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().BeNull();
        deserialized.NullableGuidValue.Should().Be(model.NullableGuidValue);
    }

    [Fact]
    public void NullableTypes_ZigZagEncoding_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableZigZagIntValue = -12345,
            NullableZigZagLongValue = -987654321L
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableZigZagIntValue.Should().Be(-12345);
        deserialized.NullableZigZagLongValue.Should().Be(-987654321L);
    }

    [Fact]
    public void NullableTypes_FixedSizeEncoding_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableFixedSizeIntValue = 12345
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableFixedSizeIntValue.Should().Be(12345);
    }

    [Fact]
    public void NullableTypes_GuidNull_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = null
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableGuidValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_GuidWithValue_PG()
    {
        var testGuid = new Guid("12345678-9abc-def0-1234-56789abcdef0");
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void NullableTypes_GuidEmpty_PG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = Guid.Empty
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void IntArray_NonPacked_Default_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = new[] { 1, 2, 3, 100, 1000, -5, 0 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayNonPacked.Should().Equal(1, 2, 3, 100, 1000, -5, 0);
    }

    [Fact]
    public void IntArray_NonPacked_ZigZag_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedZigZag = new[] { -1, -100, 50, 0, 1000, -5000 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayNonPackedZigZag.Should().Equal(-1, -100, 50, 0, 1000, -5000);
    }

    [Fact]
    public void IntArray_NonPacked_FixedSize_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedFixed = new[] { 12345, 67890, -11111, 0, 99999 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayNonPackedFixed.Should().Equal(12345, 67890, -11111, 0, 99999);
    }

    [Fact]
    public void IntArray_Packed_Default_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000, -5, 0 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000, -5, 0);
    }

    [Fact]
    public void IntArray_Packed_ZigZag_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedZigZag = new[] { -1, -100, 50, 0, 1000, -5000 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPackedZigZag.Should().Equal(-1, -100, 50, 0, 1000, -5000);
    }

    [Fact]
    public void IntArray_Packed_FixedSize_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedFixed = new[] { 12345, 67890, -11111, 0, 99999 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPackedFixed.Should().Equal(12345, 67890, -11111, 0, 99999);
    }

    [Fact]
    public void ByteArray_Basic_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[] { 0, 1, 255, 128, 64, 32, 16, 8, 4, 2 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.ByteArray.Should().Equal(new byte[] { 0, 1, 255, 128, 64, 32, 16, 8, 4, 2 });
    }

    [Fact]
    public void ByteArray_Empty_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[0]
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.ByteArray.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ByteArray_Large_PG()
    {
        var largeArray = new byte[10000];
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = (byte)(i % 256);
        }

        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = largeArray
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.ByteArray.Should().Equal(largeArray);
    }

    [Fact]
    public void IntArray_Null_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = null
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0);
        deserialized.Should().NotBeNull();
        deserialized.IntArrayNonPacked.Should().BeNull();
    }

    [Fact]
    public void IntArray_Empty_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = new int[0]
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.Should().NotBeNull();
        deserialized.IntArrayNonPacked.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ByteArray_Null_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = null
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0);
        deserialized.Should().NotBeNull();
        deserialized.ByteArray.Should().BeNull();
    }

    [Fact]
    public void IntArray_Large_Packed_vs_NonPacked_PG()
    {
        var largeArray = new int[1000];
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = i * 2 + 1; // Generate some variety of numbers
        }

        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = largeArray,
            IntArrayNonPacked = largeArray
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(largeArray);
        deserialized.IntArrayNonPacked.Should().Equal(largeArray);
    }

    [Fact]
    public void IntArray_ExtremeValues_PG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { int.MinValue, int.MaxValue, 0, -1, 1 },
            IntArrayNonPacked = new[] { int.MinValue, int.MaxValue, 0, -1, 1 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
        deserialized.IntArrayNonPacked.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
    }

    #region ByteArray Tests (Protobuf-net to GProtobuf)

    [Fact]
    public void ByteArray_CrossCompatibility_ProtobufNetToGProtobuf_PG()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 13, 37, 42, 128, 255, 0 }
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.BasicByteArray.Should().Equal(13, 37, 42, 128, 255, 0);
    }

    [Fact]
    public void ByteArray_EmptyAndNull_ProtobufNetToGProtobuf_PG()
    {
        var model = new ByteArrayTestModel
        {
            EmptyByteArray = new byte[0],
            NullByteArray = null
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.EmptyByteArray.Should().BeEmpty();
        gprotobufDeserialized.NullByteArray.Should().BeNull();
    }

    [Fact]
    public void ByteArray_LargeData_ProtobufNetToGProtobuf_PG()
    {
        var largeData = new byte[3000];
        var random = new Random(123); // Seed for deterministic test
        random.NextBytes(largeData);

        var model = new ByteArrayTestModel
        {
            LargeByteArray = largeData
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.LargeByteArray.Should().Equal(largeData);
    }

    [Fact]
    public void ByteArray_AllPossibleValues_ProtobufNetToGProtobuf_PG()
    {
        var allBytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            allBytes[i] = (byte)i;
        }

        var model = new ByteArrayTestModel
        {
            AllPossibleBytes = allBytes
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.AllPossibleBytes.Should().Equal(allBytes);
        gprotobufDeserialized.AllPossibleBytes[0].Should().Be(0);
        gprotobufDeserialized.AllPossibleBytes[255].Should().Be(255);
    }

    [Fact]
    public void ByteArray_BinaryDataHeaders_ProtobufNetToGProtobuf_PG()
    {
        var binaryData = new byte[]
        {
            // JPEG header
            0xFF, 0xD8, 0xFF, 0xE0,
            // PDF header
            0x25, 0x50, 0x44, 0x46,
            // ZIP header
            0x50, 0x4B, 0x03, 0x04,
            // Edge values
            0x00, 0x7F, 0x80, 0xFF
        };

        var model = new ByteArrayTestModel
        {
            BinaryData = binaryData
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.BinaryData.Should().Equal(binaryData);
    }

    [Fact]
    public void ByteArray_MultipleFields_ProtobufNetToGProtobuf_PG()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 1, 2, 3, 4, 5 },
            EmptyByteArray = new byte[0],
            EdgeCaseBytes = new byte[] { byte.MinValue, byte.MaxValue, 127, 128 }
        };

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        gprotobufDeserialized.Should().NotBeNull();
        gprotobufDeserialized.BasicByteArray.Should().Equal(1, 2, 3, 4, 5);
        gprotobufDeserialized.EmptyByteArray.Should().BeEmpty();
        gprotobufDeserialized.EdgeCaseBytes.Should().Equal(byte.MinValue, byte.MaxValue, 127, 128);
    }

    #endregion

    #region PrimitiveArrays Tests (Protobuf-net to GProtobuf)

    [Fact]
    public void PrimitiveArrays_FloatArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { 1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue },
            FloatArrayPacked = new float[] { 3.14f, 2.71f, -1.0f, 100.5f }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Should().Equal(1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue);
        deserialized.FloatArrayPacked.Should().Equal(3.14f, 2.71f, -1.0f, 100.5f);
    }

    [Fact]
    public void PrimitiveArrays_DoubleArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            DoubleArray = new double[] { 1.5, -2.3, 0.0, double.MaxValue, double.MinValue },
            DoubleArrayPacked = new double[] { Math.PI, Math.E, -1.0, 1000.123456789 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.DoubleArray.Should().Equal(1.5, -2.3, 0.0, double.MaxValue, double.MinValue);
        deserialized.DoubleArrayPacked.Should().Equal(Math.PI, Math.E, -1.0, 1000.123456789);
    }

    [Fact]
    public void PrimitiveArrays_LongArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[] { 1L, -2L, 0L, long.MaxValue, long.MinValue },
            LongArrayPacked = new long[] { 100L, 200L, 300L },
            LongArrayPackedZigZag = new long[] { -1L, -100L, 50L, 0L, 1000L, -5000L },
            LongArrayPackedFixed = new long[] { 12345L, 67890L, -11111L, 0L, 99999L }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArray.Should().Equal(1L, -2L, 0L, long.MaxValue, long.MinValue);
        deserialized.LongArrayPacked.Should().Equal(100L, 200L, 300L);
        deserialized.LongArrayPackedZigZag.Should().Equal(-1L, -100L, 50L, 0L, 1000L, -5000L);
        deserialized.LongArrayPackedFixed.Should().Equal(12345L, 67890L, -11111L, 0L, 99999L);
    }

    [Fact]
    public void PrimitiveArrays_BoolArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            BoolArray = new bool[] { true, false, true, true, false },
            BoolArrayPacked = new bool[] { false, true, false, true }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BoolArray.Should().Equal(true, false, true, true, false);
        deserialized.BoolArrayPacked.Should().Equal(false, true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_EmptyAndNullArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[0], // Empty array
            DoubleArray = null, // Null array
            LongArrayPacked = new long[0], // Empty packed array
            BoolArrayPacked = null // Null packed array
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Should().BeNullOrEmpty();
        deserialized.DoubleArray.Should().BeNull();
        deserialized.LongArrayPacked.Should().BeNullOrEmpty();
        deserialized.BoolArrayPacked.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_SpecialFloatingPointValues_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon },
            DoubleArray = new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Length.Should().Be(4);
        deserialized.FloatArray[0].Should().Be(float.NaN);
        deserialized.FloatArray[1].Should().Be(float.PositiveInfinity);
        deserialized.FloatArray[2].Should().Be(float.NegativeInfinity);
        deserialized.FloatArray[3].Should().Be(float.Epsilon);
        
        deserialized.DoubleArray.Length.Should().Be(4);
        deserialized.DoubleArray[0].Should().Be(double.NaN);
        deserialized.DoubleArray[1].Should().Be(double.PositiveInfinity);
        deserialized.DoubleArray[2].Should().Be(double.NegativeInfinity);
        deserialized.DoubleArray[3].Should().Be(double.Epsilon);
    }

    [Fact]
    public void PrimitiveArrays_ExtremeValues_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[] { long.MaxValue, long.MinValue, 0L, 1L, -1L },
            LongArrayPackedZigZag = new long[] { long.MaxValue, long.MinValue } // ZigZag handles negatives efficiently
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArray.Should().Equal(long.MaxValue, long.MinValue, 0L, 1L, -1L);
        deserialized.LongArrayPackedZigZag.Should().Equal(long.MaxValue, long.MinValue);
    }

    [Fact]
    public void PrimitiveArrays_SByteArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, -1, 0, 1, 127 },
            SByteArrayPacked = new sbyte[] { -50, -25, 0, 25, 50 },
            SByteArrayPackedZigZag = new sbyte[] { -100, -1, 0, 1, 100 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArray.Should().Equal(-128, -1, 0, 1, 127);
        deserialized.SByteArrayPacked.Should().Equal(-50, -25, 0, 25, 50);
        deserialized.SByteArrayPackedZigZag.Should().Equal(-100, -1, 0, 1, 100);
    }

    [Fact]
    public void PrimitiveArrays_ShortArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            ShortArray = new short[] { short.MinValue, -1000, 0, 1000, short.MaxValue },
            ShortArrayPacked = new short[] { 100, 200, 300, 400 },
            ShortArrayPackedZigZag = new short[] { -500, -100, 0, 100, 500 },
            ShortArrayPackedFixed = new short[] { 1000, 2000, 3000, -1000 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.ShortArray.Should().Equal(short.MinValue, -1000, 0, 1000, short.MaxValue);
        deserialized.ShortArrayPacked.Should().Equal(100, 200, 300, 400);
        deserialized.ShortArrayPackedZigZag.Should().Equal(-500, -100, 0, 100, 500);
        deserialized.ShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, -1000);
    }

    [Fact]
    public void PrimitiveArrays_UShortArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            UShortArray = new ushort[] { 0, 1000, 30000, ushort.MaxValue },
            UShortArrayPacked = new ushort[] { 100, 200, 300, 400, 500 },
            UShortArrayPackedFixed = new ushort[] { 1000, 2000, 3000, 4000 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.UShortArray.Should().Equal(0, 1000, 30000, ushort.MaxValue);
        deserialized.UShortArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, 4000);
    }

    [Fact]
    public void PrimitiveArrays_UIntArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            UIntArray = new uint[] { 0, 1000000, 2000000000, uint.MaxValue },
            UIntArrayPacked = new uint[] { 100, 200, 300, 400, 500 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000, 3000000 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.UIntArray.Should().Equal(0, 1000000, 2000000000, uint.MaxValue);
        deserialized.UIntArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000, 3000000);
    }

    [Fact]
    public void PrimitiveArrays_ULongArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            ULongArray = new ulong[] { 0, 1000000000000UL, ulong.MaxValue },
            ULongArrayPacked = new ulong[] { 100, 200, 300, 400, 500 },
            ULongArrayPackedFixed = new ulong[] { 1000000000UL, 2000000000UL, 3000000000UL }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.ULongArray.Should().Equal(0, 1000000000000UL, ulong.MaxValue);
        deserialized.ULongArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.ULongArrayPackedFixed.Should().Equal(1000000000UL, 2000000000UL, 3000000000UL);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesEmptyAndNullArrays_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayEmpty = new sbyte[0],
            ShortArrayNull = null,
            UShortArrayPackedEmpty = new ushort[0],
            UIntArrayPackedNull = null
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayEmpty.Should().BeNullOrEmpty();
        deserialized.ShortArrayNull.Should().BeNull();
        deserialized.UShortArrayPackedEmpty.Should().BeNullOrEmpty();
        deserialized.UIntArrayPackedNull.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_NewTypesExtremeValues_PG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayExtremes = new sbyte[] { sbyte.MinValue, sbyte.MaxValue, 0, -1, 1 },
            UShortArrayExtremes = new ushort[] { ushort.MinValue, ushort.MaxValue, 0, 1 },
            UIntArrayExtremes = new uint[] { uint.MinValue, uint.MaxValue, 0, 1 },
            ULongArrayExtremes = new ulong[] { ulong.MinValue, ulong.MaxValue, 0, 1 }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayExtremes.Should().Equal(sbyte.MinValue, sbyte.MaxValue, 0, -1, 1);
        deserialized.UShortArrayExtremes.Should().Equal(ushort.MinValue, ushort.MaxValue, 0, 1);
        deserialized.UIntArrayExtremes.Should().Equal(uint.MinValue, uint.MaxValue, 0, 1);
        deserialized.ULongArrayExtremes.Should().Equal(ulong.MinValue, ulong.MaxValue, 0, 1);
    }

    #endregion

    #region Additional Bidirectional Compatibility Tests

    [Fact]
    public void ByteArray_CrossCompatibilityBidirectional_PG()
    {
        // Test that data can roundtrip: protobuf-net -> GProtobuf -> protobuf-net
        var originalData = new byte[1000];
        for (int i = 0; i < originalData.Length; i++)
        {
            originalData[i] = (byte)(i % 256);
        }

        var model = new ByteArrayTestModel
        {
            LargeByteArray = originalData
        };

        // protobuf-net serialize -> GProtobuf deserialize -> GProtobuf serialize -> protobuf-net deserialize
        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);
        var gprotobufData = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var finalDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.LargeByteArray.Should().Equal(originalData);
    }

    [Fact]
    public void ByteArray_PackedEncodingNotApplicable_PG()
    {
        // This test documents that byte[] does NOT support packed encoding
        // Unlike int[] arrays, byte[] is always length-delimited (wire type 2)
        // This is consistent across both protobuf-net and GProtobuf
        
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 1, 2, 3, 4, 5 }
        };

        // Serialize with protobuf-net
        var protobufNetData = SerializeWithProtobufNet(model);
        
        // Verify the wire format starts with field 1, wire type 2 (length-delimited)
        // Field 1 with wire type 2 = (1 << 3) | 2 = 10 (0x0A)
        protobufNetData[0].Should().Be(0x0A);
        
        // Deserialize with GProtobuf to ensure compatibility
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);
        deserialized.BasicByteArray.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void PrimitiveArrays_CrossCompatibilityBidirectional_PG()
    {
        // Test bidirectional compatibility: protobuf-net -> GProtobuf -> protobuf-net
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { 1.5f, -2.3f, 0.0f },
            DoubleArrayPacked = new double[] { Math.PI, Math.E },
            LongArrayPackedZigZag = new long[] { -1000L, 1000L, 0L },
            BoolArrayPacked = new bool[] { true, false, true }
        };

        // protobuf-net serialize -> GProtobuf deserialize -> GProtobuf serialize -> protobuf-net deserialize
        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(protobufNetData);
        var gprotobufData = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var finalDeserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotobufData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.FloatArray.Should().Equal(1.5f, -2.3f, 0.0f);
        finalDeserialized.DoubleArrayPacked.Should().Equal(Math.PI, Math.E);
        finalDeserialized.LongArrayPackedZigZag.Should().Equal(-1000L, 1000L, 0L);
        finalDeserialized.BoolArrayPacked.Should().Equal(true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesCrossCompatibilityBidirectional_PG()
    {
        // Test bidirectional compatibility for new array types: protobuf-net -> GProtobuf -> protobuf-net
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, 0, 127 },
            ShortArrayPackedZigZag = new short[] { -1000, 0, 1000 },
            UShortArrayPacked = new ushort[] { 100, 200, 300 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000 },
            ULongArrayPacked = new ulong[] { 100UL, 200UL, 300UL }
        };

        // protobuf-net serialize -> GProtobuf deserialize -> GProtobuf serialize -> protobuf-net deserialize
        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufDeserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(protobufNetData);
        var gprotobufData = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var finalDeserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotobufData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.SByteArray.Should().Equal(-128, 0, 127);
        finalDeserialized.ShortArrayPackedZigZag.Should().Equal(-1000, 0, 1000);
        finalDeserialized.UShortArrayPacked.Should().Equal(100, 200, 300);
        finalDeserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000);
        finalDeserialized.ULongArrayPacked.Should().Equal(100UL, 200UL, 300UL);
    }

    #endregion
}
