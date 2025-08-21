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
}
