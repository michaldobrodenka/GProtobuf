using FluentAssertions;
using GProtobuf.Tests.TestModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void ArrayTypes_CompatibleWithProtobufNet_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000 },
            IntArrayNonPacked = new[] { -1, -2, 0, 50 },
            ByteArray = new byte[] { 0, 1, 255, 128, 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000);
        deserialized.IntArrayNonPacked.Should().Equal(-1, -2, 0, 50);
        deserialized.ByteArray.Should().Equal(new byte[] { 0, 1, 255, 128, 42 });
    }

    [Fact]
    public void BasicTypesZigZagMinMaxValues_GP()
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

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesZigZagModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void LongMinValueZigZag_GP()
    {
        // Test the edge case of long.MinValue with ZigZag encoding
        var model = new BasicTypesZigZagModel
        {
            LongValue = long.MinValue, // -9223372036854775808
            StringValue = "LongMinValue test"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithProtobufNet<BasicTypesZigZagModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().NotBeNull();
        deserialized.LongValue.Should().Be(long.MinValue, "long.MinValue should be preserved through ZigZag encoding");
        deserialized.StringValue.Should().Be("LongMinValue test");
    }

    [Fact]
    public void LongMinValueZigZag_DeserializeProtobufNetData_GP()
    {
        // Create expected GProtobuf serialized data for long.MinValue ZigZag encoded
        var gProtobufModel = new BasicTypesZigZagModel { LongValue = long.MinValue };
        var gProtobufData = SerializeWithGProtobuf(gProtobufModel, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        
        var deserialized = DeserializeWithProtobufNet<BasicTypesZigZagModel>(gProtobufData);
        deserialized.LongValue.Should().Be(long.MinValue);
    }

    [Fact]
    public void NullableTypes_AllNull_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = null,
            NullableLongValue = null,
            NullableDoubleValue = null,
            NullableBoolValue = null,
            NullableGuidValue = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableIntValue.Should().BeNull();
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().BeNull();
        deserialized.NullableBoolValue.Should().BeNull();
        deserialized.NullableGuidValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_WithZeroValues_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 0,
            NullableLongValue = 0L,
            NullableDoubleValue = 0.0,
            NullableBoolValue = false,
            NullableGuidValue = Guid.Empty
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        // Nullable types with 0 values should serialize and deserialize correctly
        deserialized.NullableIntValue.Should().Be(0);
        deserialized.NullableLongValue.Should().Be(0L);
        deserialized.NullableDoubleValue.Should().Be(0.0);
        deserialized.NullableBoolValue.Should().Be(false);
        deserialized.NullableGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void NullableTypes_WithNonZeroValues_GP()
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

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().Be(123456789L);
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().Be(true);
        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void NullableTypes_MixedNullAndValues_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = null,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = null,
            NullableGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().BeNull();
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().BeNull();
        deserialized.NullableGuidValue.Should().Be(model.NullableGuidValue);
    }

    [Fact]
    public void NullableTypes_ZigZagEncoding_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableZigZagIntValue = -12345,
            NullableZigZagLongValue = -987654321L
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableZigZagIntValue.Should().Be(-12345);
        deserialized.NullableZigZagLongValue.Should().Be(-987654321L);
    }

    [Fact]
    public void NullableTypes_FixedSizeEncoding_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableFixedSizeIntValue = 12345
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableFixedSizeIntValue.Should().Be(12345);
    }

    [Fact]
    public void NullableTypes_GuidNull_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableGuidValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_GuidWithValue_GP()
    {
        var testGuid = new Guid("12345678-9abc-def0-1234-56789abcdef0");
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void NullableTypes_GuidEmpty_GP()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = Guid.Empty
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.NullableTypesModel>(data);

        deserialized.NullableGuidValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void IntArray_NonPacked_Default_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = new[] { 1, 2, 3, 100, 1000, -5, 0 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayNonPacked.Should().Equal(1, 2, 3, 100, 1000, -5, 0);
    }

    [Fact]
    public void IntArray_NonPacked_ZigZag_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedZigZag = new[] { -1, -100, 50, 0, 1000, -5000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayNonPackedZigZag.Should().Equal(-1, -100, 50, 0, 1000, -5000);
    }

    [Fact]
    public void IntArray_NonPacked_FixedSize_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedFixed = new[] { 12345, 67890, -11111, 0, 99999 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayNonPackedFixed.Should().Equal(12345, 67890, -11111, 0, 99999);
    }

    [Fact]
    public void IntArray_Packed_Default_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000, -5, 0 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000, -5, 0);
    }

    [Fact]
    public void IntArray_Packed_ZigZag_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedZigZag = new[] { -1, -100, 50, 0, 1000, -5000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPackedZigZag.Should().Equal(-1, -100, 50, 0, 1000, -5000);
    }

    [Fact]
    public void IntArray_Packed_FixedSize_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedFixed = new[] { 12345, 67890, -11111, 0, 99999 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPackedFixed.Should().Equal(12345, 67890, -11111, 0, 99999);
    }

    [Fact]
    public void ByteArray_Basic_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[] { 0, 1, 255, 128, 64, 32, 16, 8, 4, 2 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.ByteArray.Should().Equal(new byte[] { 0, 1, 255, 128, 64, 32, 16, 8, 4, 2 });
    }

    [Fact]
    public void ByteArray_Empty_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.ByteArray.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ByteArray_Large_GP()
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

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.ByteArray.Should().Equal(largeArray);
    }

    [Fact]
    public void IntArray_Null_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().Be(0);
        deserialized.Should().NotBeNull();
        deserialized.IntArrayNonPacked.Should().BeNull();
    }

    [Fact]
    public void IntArray_Empty_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = new int[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        data.Should().NotBeNull();
        deserialized.Should().NotBeNull();
        deserialized.IntArrayNonPacked.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ByteArray_Null_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        data.Should().NotBeNull();
        data.Length.Should().Be(0);
        deserialized.Should().NotBeNull();
        deserialized.ByteArray.Should().BeNull();
    }

    [Fact]
    public void IntArray_Large_Packed_vs_NonPacked_GP()
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

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPacked.Should().Equal(largeArray);
        deserialized.IntArrayNonPacked.Should().Equal(largeArray);
    }

    [Fact]
    public void IntArray_ExtremeValues_GP()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { int.MinValue, int.MaxValue, 0, -1, 1 },
            IntArrayNonPacked = new[] { int.MinValue, int.MaxValue, 0, -1, 1 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithProtobufNet<TestModel.ArrayTypesModel>(data);

        deserialized.IntArrayPacked.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
        deserialized.IntArrayNonPacked.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
    }

    #region ByteArray Tests (GProtobuf to Protobuf-net)

    [Fact]
    public void ByteArray_CrossCompatibility_GProtobufToProtobufNet_GP()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 42, 100, 200, 0, 255, 1 }
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.BasicByteArray.Should().Equal(42, 100, 200, 0, 255, 1);
    }

    [Fact]
    public void ByteArray_EmptyAndNull_GProtobufToProtobufNet_GP()
    {
        var model = new ByteArrayTestModel
        {
            EmptyByteArray = new byte[0],
            NullByteArray = null
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.EmptyByteArray.Should().BeNullOrEmpty();
        protobufNetDeserialized.NullByteArray.Should().BeNull();
    }

    [Fact]
    public void ByteArray_LargeData_GProtobufToProtobufNet_GP()
    {
        var largeData = new byte[4000];
        var random = new Random(456); // Different seed for variety
        random.NextBytes(largeData);

        var model = new ByteArrayTestModel
        {
            LargeByteArray = largeData
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.LargeByteArray.Should().Equal(largeData);
    }

    [Fact]
    public void ByteArray_AllPossibleValues_GProtobufToProtobufNet_GP()
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

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.AllPossibleBytes.Should().Equal(allBytes);
        // Verify critical byte values are preserved
        protobufNetDeserialized.AllPossibleBytes[0].Should().Be(0);
        protobufNetDeserialized.AllPossibleBytes[127].Should().Be(127);
        protobufNetDeserialized.AllPossibleBytes[128].Should().Be(128);
        protobufNetDeserialized.AllPossibleBytes[255].Should().Be(255);
    }

    [Fact]
    public void ByteArray_BinaryDataHeaders_GProtobufToProtobufNet_GP()
    {
        var binaryData = new byte[]
        {
            // GIF header
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            // BMP header
            0x42, 0x4D,
            // ELF header
            0x7F, 0x45, 0x4C, 0x46,
            // Edge cases and control characters
            0x00, 0x01, 0x0A, 0x0D, 0x1B, 0x7F, 0x80, 0xFE, 0xFF
        };

        var model = new ByteArrayTestModel
        {
            BinaryData = binaryData
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.BinaryData.Should().Equal(binaryData);
    }

    [Fact]
    public void ByteArray_MultipleFields_GProtobufToProtobufNet_GP()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 10, 20, 30, 40, 50 },
            EmptyByteArray = new byte[0],
            EdgeCaseBytes = new byte[] { byte.MinValue, 1, 127, 128, 254, byte.MaxValue },
            BinaryData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.BasicByteArray.Should().Equal(10, 20, 30, 40, 50);
        protobufNetDeserialized.EmptyByteArray.Should().BeNullOrEmpty();
        protobufNetDeserialized.EdgeCaseBytes.Should().Equal(byte.MinValue, 1, 127, 128, 254, byte.MaxValue);
        protobufNetDeserialized.BinaryData.Should().Equal(0xDE, 0xAD, 0xBE, 0xEF);
    }

    [Fact]
    public void ByteArray_CrossCompatibilityBidirectional_GP()
    {
        // Test that data can roundtrip: GProtobuf -> protobuf-net -> GProtobuf
        var originalData = new byte[1000];
        for (int i = 0; i < originalData.Length; i++)
        {
            originalData[i] = (byte)(i % 256);
        }

        var model = new ByteArrayTestModel
        {
            LargeByteArray = originalData
        };

        // GProtobuf serialize -> protobuf-net deserialize -> protobuf-net serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);
        var protobufNetData = SerializeWithProtobufNet(protobufNetDeserialized);
        var finalDeserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(protobufNetData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.LargeByteArray.Should().Equal(originalData);
    }

    [Fact]
    public void ByteArray_PackedEncodingNotApplicable_GP()
    {
        // This test documents that byte[] does NOT support packed encoding
        // Unlike int[] arrays, byte[] is always length-delimited (wire type 2)
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            // Note: [ProtoMember(X, IsPacked = true)] would be incorrect for byte[]
            // byte[] is inherently a single length-delimited field, not repeated scalars
        };

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<ByteArrayTestModel>(gprotobufData);

        // The data should serialize/deserialize correctly regardless of any IsPacked considerations
        protobufNetDeserialized.Should().NotBeNull();
        protobufNetDeserialized.BasicByteArray.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
    }

    #endregion

    #region PrimitiveArrays Tests (GProtobuf to Protobuf-net)

    [Fact]
    public void PrimitiveArrays_FloatArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { 1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue },
            FloatArrayPacked = new float[] { 3.14f, 2.71f, -1.0f, 100.5f }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Should().Equal(1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue);
        deserialized.FloatArrayPacked.Should().Equal(3.14f, 2.71f, -1.0f, 100.5f);
    }

    [Fact]
    public void PrimitiveArrays_DoubleArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            DoubleArray = new double[] { 1.5, -2.3, 0.0, double.MaxValue, double.MinValue },
            DoubleArrayPacked = new double[] { Math.PI, Math.E, -1.0, 1000.123456789 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.DoubleArray.Should().Equal(1.5, -2.3, 0.0, double.MaxValue, double.MinValue);
        deserialized.DoubleArrayPacked.Should().Equal(Math.PI, Math.E, -1.0, 1000.123456789);
    }

    [Fact]
    public void PrimitiveArrays_LongArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[] { 1L, -2L, 0L, long.MaxValue, long.MinValue },
            LongArrayPacked = new long[] { 100L, 200L, 300L },
            LongArrayPackedZigZag = new long[] { -1L, -100L, 50L, 0L, 1000L, -5000L },
            LongArrayPackedFixed = new long[] { 12345L, 67890L, -11111L, 0L, 99999L }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArray.Should().Equal(1L, -2L, 0L, long.MaxValue, long.MinValue);
        deserialized.LongArrayPacked.Should().Equal(100L, 200L, 300L);
        deserialized.LongArrayPackedZigZag.Should().Equal(-1L, -100L, 50L, 0L, 1000L, -5000L);
        deserialized.LongArrayPackedFixed.Should().Equal(12345L, 67890L, -11111L, 0L, 99999L);
    }

    [Fact]
    public void PrimitiveArrays_BoolArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            BoolArray = new bool[] { true, false, true, true, false },
            BoolArrayPacked = new bool[] { false, true, false, true }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.BoolArray.Should().Equal(true, false, true, true, false);
        deserialized.BoolArrayPacked.Should().Equal(false, true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_EmptyAndNullArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[0], // Empty array
            DoubleArray = null, // Null array
            LongArrayPacked = new long[0], // Empty packed array
            BoolArrayPacked = null // Null packed array
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Should().BeNullOrEmpty();
        deserialized.DoubleArray.Should().BeNull();
        deserialized.LongArrayPacked.Should().BeNullOrEmpty();
        deserialized.BoolArrayPacked.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_SpecialFloatingPointValues_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon },
            DoubleArray = new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

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
    public void PrimitiveArrays_ExtremeValues_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[] { long.MaxValue, long.MinValue, 0L, 1L, -1L },
            LongArrayPackedZigZag = new long[] { long.MaxValue, long.MinValue } // ZigZag handles negatives efficiently
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArray.Should().Equal(long.MaxValue, long.MinValue, 0L, 1L, -1L);
        deserialized.LongArrayPackedZigZag.Should().Equal(long.MaxValue, long.MinValue);
    }

    [Fact]
    public void PrimitiveArrays_CrossCompatibilityBidirectional_GP()
    {
        // Test bidirectional compatibility: GProtobuf -> protobuf-net -> GProtobuf
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { 1.5f, -2.3f, 0.0f },
            DoubleArrayPacked = new double[] { Math.PI, Math.E },
            LongArrayPackedZigZag = new long[] { -1000L, 1000L, 0L },
            BoolArrayPacked = new bool[] { true, false, true }
        };

        // GProtobuf serialize -> protobuf-net deserialize -> protobuf-net serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotobufData);
        var protobufNetData = SerializeWithProtobufNet(protobufNetDeserialized);
        var finalDeserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(protobufNetData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.FloatArray.Should().Equal(1.5f, -2.3f, 0.0f);
        finalDeserialized.DoubleArrayPacked.Should().Equal(Math.PI, Math.E);
        finalDeserialized.LongArrayPackedZigZag.Should().Equal(-1000L, 1000L, 0L);
        finalDeserialized.BoolArrayPacked.Should().Equal(true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_SByteArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, -1, 0, 1, 127 },
            SByteArrayPacked = new sbyte[] { -50, -25, 0, 25, 50 },
            SByteArrayPackedZigZag = new sbyte[] { -100, -1, 0, 1, 100 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArray.Should().Equal(-128, -1, 0, 1, 127);
        deserialized.SByteArrayPacked.Should().Equal(-50, -25, 0, 25, 50);
        deserialized.SByteArrayPackedZigZag.Should().Equal(-100, -1, 0, 1, 100);
    }

    [Fact]
    public void PrimitiveArrays_ShortArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            ShortArray = new short[] { short.MinValue, -1000, 0, 1000, short.MaxValue },
            ShortArrayPacked = new short[] { 100, 200, 300, 400 },
            ShortArrayPackedZigZag = new short[] { -500, -100, 0, 100, 500 },
            ShortArrayPackedFixed = new short[] { 1000, 2000, 3000, -1000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.ShortArray.Should().Equal(short.MinValue, -1000, 0, 1000, short.MaxValue);
        deserialized.ShortArrayPacked.Should().Equal(100, 200, 300, 400);
        deserialized.ShortArrayPackedZigZag.Should().Equal(-500, -100, 0, 100, 500);
        deserialized.ShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, -1000);
    }

    [Fact]
    public void PrimitiveArrays_UShortArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            UShortArray = new ushort[] { 0, 1000, 30000, ushort.MaxValue },
            UShortArrayPacked = new ushort[] { 100, 200, 300, 400, 500 },
            UShortArrayPackedFixed = new ushort[] { 1000, 2000, 3000, 4000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.UShortArray.Should().Equal(0, 1000, 30000, ushort.MaxValue);
        deserialized.UShortArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, 4000);
    }

    [Fact]
    public void PrimitiveArrays_UIntArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            UIntArray = new uint[] { 0, 1000000, 2000000000, uint.MaxValue },
            UIntArrayPacked = new uint[] { 100, 200, 300, 400, 500 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000, 3000000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.UIntArray.Should().Equal(0, 1000000, 2000000000, uint.MaxValue);
        deserialized.UIntArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000, 3000000);
    }

    [Fact]
    public void PrimitiveArrays_ULongArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            ULongArray = new ulong[] { 0, 1000000000000UL, ulong.MaxValue },
            ULongArrayPacked = new ulong[] { 100, 200, 300, 400, 500 },
            ULongArrayPackedFixed = new ulong[] { 1000000000UL, 2000000000UL, 3000000000UL }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.ULongArray.Should().Equal(0, 1000000000000UL, ulong.MaxValue);
        deserialized.ULongArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.ULongArrayPackedFixed.Should().Equal(1000000000UL, 2000000000UL, 3000000000UL);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesEmptyAndNullArrays_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayEmpty = new sbyte[0],
            ShortArrayNull = null,
            UShortArrayPackedEmpty = new ushort[0],
            UIntArrayPackedNull = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayEmpty.Should().BeNullOrEmpty();
        deserialized.ShortArrayNull.Should().BeNull();
        deserialized.UShortArrayPackedEmpty.Should().BeNullOrEmpty();
        deserialized.UIntArrayPackedNull.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_NewTypesExtremeValues_GP()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayExtremes = new sbyte[] { sbyte.MinValue, sbyte.MaxValue, 0, -1, 1 },
            UShortArrayExtremes = new ushort[] { ushort.MinValue, ushort.MaxValue, 0, 1 },
            UIntArrayExtremes = new uint[] { uint.MinValue, uint.MaxValue, 0, 1 },
            ULongArrayExtremes = new ulong[] { ulong.MinValue, ulong.MaxValue, 0, 1 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayExtremes.Should().Equal(sbyte.MinValue, sbyte.MaxValue, 0, -1, 1);
        deserialized.UShortArrayExtremes.Should().Equal(ushort.MinValue, ushort.MaxValue, 0, 1);
        deserialized.UIntArrayExtremes.Should().Equal(uint.MinValue, uint.MaxValue, 0, 1);
        deserialized.ULongArrayExtremes.Should().Equal(ulong.MinValue, ulong.MaxValue, 0, 1);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesCrossCompatibilityBidirectional_GP()
    {
        // Test bidirectional compatibility for new array types: GProtobuf -> protobuf-net -> GProtobuf
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, 0, 127 },
            ShortArrayPackedZigZag = new short[] { -1000, 0, 1000 },
            UShortArrayPacked = new ushort[] { 100, 200, 300 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000 },
            ULongArrayPacked = new ulong[] { 100UL, 200UL, 300UL }
        };

        // GProtobuf serialize -> protobuf-net deserialize -> protobuf-net serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotobufData);
        var protobufNetData = SerializeWithProtobufNet(protobufNetDeserialized);
        var finalDeserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(protobufNetData);

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.SByteArray.Should().Equal(-128, 0, 127);
        finalDeserialized.ShortArrayPackedZigZag.Should().Equal(-1000, 0, 1000);
        finalDeserialized.UShortArrayPacked.Should().Equal(100, 200, 300);
        finalDeserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000);
        finalDeserialized.ULongArrayPacked.Should().Equal(100UL, 200UL, 300UL);
    }

    #endregion

    #region String Array Tests (GProtobuf to Protobuf-net)

    [Fact]
    public void StringArrays_BasicValues_GP()
    {
        var model = new StringArraysTestModel
        {
            BasicStringArray = new string[] { "hello", "world", "protobuf", "test" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<StringArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.BasicStringArray.Should().Equal("hello", "world", "protobuf", "test");
    }

    [Fact]
    public void StringArrays_EmptyAndNullArrays_GP()
    {
        var model = new StringArraysTestModel
        {
            EmptyStringArray = new string[0],
            NullStringArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<StringArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.EmptyStringArray.Should().BeNullOrEmpty();
        deserialized.NullStringArray.Should().BeNull();
    }

    [Fact]
    public void StringArrays_WithNullElements_GP()
    {
        var model = new StringArraysTestModel
        {
            StringArrayWithNulls = new string[] { "first", null, "third", null, "fifth" }
        };

        // GProtobuf should throw exception when trying to serialize null elements (same as protobuf-net)
        var act = () => SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*element of type string was null*");
    }

    [Fact]
    public void StringArrays_SpecialCharactersAndUnicode_GP()
    {
        var model = new StringArraysTestModel
        {
            SpecialCharStringArray = new string[] { "hello\nworld", "tab\there", "quote\"test" },
            UnicodeStringArray = new string[] { "Ľubomír", "Žitný", "Košice", "Bratislava", "ťŠčÝáé" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<StringArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.SpecialCharStringArray.Should().Equal("hello\nworld", "tab\there", "quote\"test");
        deserialized.UnicodeStringArray.Should().Equal("Ľubomír", "Žitný", "Košice", "Bratislava", "ťŠčÝáé");
    }

    [Fact]
    public void StringArrays_UnicodeCharacters_GP()
    {
        var model = new StringArraysTestModel
        {
            UnicodeStringArray = new string[] { 
                "Hello", 
                "Héllo", 
                "привет", 
                "你好", 
                "🎉", 
                "Åčéňť",
                "ľščťžýáíé"
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<StringArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.UnicodeStringArray.Should().Equal("Hello", "Héllo", "привет", "你好", "🎉", "Åčéňť", "ľščťžýáíé");
    }

    [Fact]
    public void StringArrays_LongStrings_GP()
    {
        // Create long strings
        var longString1 = new string('A', 1000);
        var longString2 = new string('B', 2000);
        var longString3 = string.Join("", Enumerable.Range(0, 100).Select(i => $"Item{i}_"));
        
        var model = new StringArraysTestModel
        {
            LongStringArray = new string[] { longString1, longString2, longString3 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<StringArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.LongStringArray.Should().Equal(longString1, longString2, longString3);
    }

    #endregion

    #region Message Array Tests (GProtobuf to Protobuf-net)

    [Fact]
    public void MessageArrays_SimpleMessages_GP()
    {
        var model = new MessageArraysTestModel
        {
            SimpleMessages = new SimpleMessage[]
            {
                new SimpleMessage { Name = "First", Value = 100 },
                new SimpleMessage { Name = "Second", Value = 200 },
                new SimpleMessage { Name = "Third", Value = 300 }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<MessageArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.SimpleMessages.Should().HaveCount(3);
        deserialized.SimpleMessages[0].Should().BeEquivalentTo(new SimpleMessage { Name = "First", Value = 100 });
        deserialized.SimpleMessages[1].Should().BeEquivalentTo(new SimpleMessage { Name = "Second", Value = 200 });
        deserialized.SimpleMessages[2].Should().BeEquivalentTo(new SimpleMessage { Name = "Third", Value = 300 });
    }

    [Fact]
    public void MessageArrays_EmptyAndNullArrays_GP()
    {
        var model = new MessageArraysTestModel
        {
            EmptyMessageArray = new SimpleMessage[0],
            NullMessageArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<MessageArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.EmptyMessageArray.Should().BeNullOrEmpty();
        deserialized.NullMessageArray.Should().BeNull();
    }

    [Fact]
    public void MessageArrays_WithNullElements_GP()
    {
        var model = new MessageArraysTestModel
        {
            MessageArrayWithNulls = new SimpleMessage[]
            {
                new SimpleMessage { Name = "First", Value = 100 },
                null,
                new SimpleMessage { Name = "Third", Value = 300 }
            }
        };

        // GProtobuf should throw exception when trying to serialize null elements (same as protobuf-net)
        var act = () => SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*element of type SimpleMessage was null*");
    }

    [Fact]
    public void MessageArrays_NestedMessages_GP()
    {
        var model = new MessageArraysTestModel
        {
            NestedMessages = new NestedMessage[]
            {
                new NestedMessage 
                { 
                    Title = "First Nested",
                    Inner = new SimpleMessage { Name = "Inner1", Value = 111 },
                    Score = 3.14
                },
                new NestedMessage 
                { 
                    Title = "Second Nested",
                    Inner = new SimpleMessage { Name = "Inner2", Value = 222 },
                    Score = 2.71
                }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<MessageArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.NestedMessages.Should().HaveCount(2);
        
        var expected1 = new NestedMessage 
        { 
            Title = "First Nested",
            Inner = new SimpleMessage { Name = "Inner1", Value = 111 },
            Score = 3.14
        };
        var expected2 = new NestedMessage 
        { 
            Title = "Second Nested",
            Inner = new SimpleMessage { Name = "Inner2", Value = 222 },
            Score = 2.71
        };
        
        deserialized.NestedMessages[0].Should().BeEquivalentTo(expected1);
        deserialized.NestedMessages[1].Should().BeEquivalentTo(expected2);
    }

    [Fact]
    public void MessageArrays_ComplexScenario_GP()
    {
        var model = new MessageArraysTestModel
        {
            ComplexMessageArray = new NestedMessage[]
            {
                new NestedMessage 
                { 
                    Title = "Complex 1",
                    Inner = new SimpleMessage { Name = "Inner 1", Value = 111 },
                    Score = 10.5
                },
                new NestedMessage 
                { 
                    Title = "Complex 2",
                    Inner = new SimpleMessage { Name = "Inner 2", Value = 222 },
                    Score = 20.5
                },
                new NestedMessage 
                { 
                    Title = "Complex 3", 
                    Inner = null, // This should be OK as Inner is optional field
                    Score = 30.5
                }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = DeserializeWithProtobufNet<MessageArraysTestModel>(data);

        deserialized.Should().NotBeNull();
        deserialized.ComplexMessageArray.Should().HaveCount(3);
        deserialized.ComplexMessageArray[0].Title.Should().Be("Complex 1");
        deserialized.ComplexMessageArray[0].Inner.Should().BeEquivalentTo(new SimpleMessage { Name = "Inner 1", Value = 111 });
        deserialized.ComplexMessageArray[2].Inner.Should().BeNull();
    }

    [Fact]
    public void ListMessageTypes_Compatibility_GP()
    {
        var model = new CollectionTypesTestModel
        {
            MessageList = new List<SimpleMessage>
            {
                new SimpleMessage { Name = "First", Value = 1 },
                new SimpleMessage { Name = "Second", Value = 2 }
            },
            MessageICollection = new List<SimpleMessage>
            {
                new SimpleMessage { Name = "ICol1", Value = 10 },
                new SimpleMessage { Name = "ICol2", Value = 20 }
            },
            MessageIList = new List<SimpleMessage>
            {
                new SimpleMessage { Name = "IList1", Value = 100 }
            },
            MessageIEnumerable = new List<SimpleMessage>
            {
                new SimpleMessage { Name = "IEnum1", Value = 1000 },
                new SimpleMessage { Name = "IEnum2", Value = 2000 }
            },
            StringList = new List<string> { "Hello", "World", "Test" },
            NestedICollection = new List<NestedMessage>
            {
                new NestedMessage 
                { 
                    Title = "NestedIC", 
                    Inner = new SimpleMessage { Name = "InnerIC", Value = 99 },
                    Score = 2.71 
                }
            },
            NestedMessageList = new List<NestedMessage>
            {
                new NestedMessage 
                { 
                    Title = "Nested1", 
                    Inner = new SimpleMessage { Name = "Inner1", Value = 42 },
                    Score = 3.14 
                }
            }
        };

        // GProtobuf serialize -> protobuf-net deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeCollectionTypesTestModel);
        var deserialized = DeserializeWithProtobufNet<CollectionTypesTestModel>(gprotobufData);

        // Test List<SimpleMessage>
        deserialized.MessageList.Should().HaveCount(2);
        deserialized.MessageList[0].Name.Should().Be("First");
        deserialized.MessageList[0].Value.Should().Be(1);
        deserialized.MessageList[1].Name.Should().Be("Second");
        deserialized.MessageList[1].Value.Should().Be(2);

        // Test ICollection<SimpleMessage>
        deserialized.MessageICollection.Should().HaveCount(2);
        var iColList = deserialized.MessageICollection.ToList();
        iColList[0].Name.Should().Be("ICol1");
        iColList[0].Value.Should().Be(10);
        iColList[1].Name.Should().Be("ICol2");
        iColList[1].Value.Should().Be(20);

        // Test IList<SimpleMessage>
        deserialized.MessageIList.Should().HaveCount(1);
        deserialized.MessageIList.First().Name.Should().Be("IList1");
        deserialized.MessageIList.First().Value.Should().Be(100);

        // Test IEnumerable<SimpleMessage>
        deserialized.MessageIEnumerable.Should().HaveCount(2);
        var enumList = deserialized.MessageIEnumerable.ToList();
        enumList[0].Name.Should().Be("IEnum1");
        enumList[0].Value.Should().Be(1000);
        enumList[1].Name.Should().Be("IEnum2");
        enumList[1].Value.Should().Be(2000);

        // Test List<string>
        deserialized.StringList.Should().Equal("Hello", "World", "Test");

        // Test ICollection<NestedMessage>
        deserialized.NestedICollection.Should().HaveCount(1);
        var nestedICol = deserialized.NestedICollection.First();
        nestedICol.Title.Should().Be("NestedIC");
        nestedICol.Inner.Name.Should().Be("InnerIC");
        nestedICol.Inner.Value.Should().Be(99);
        nestedICol.Score.Should().Be(2.71);

        // Test List<NestedMessage>
        deserialized.NestedMessageList.Should().HaveCount(1);
        deserialized.NestedMessageList[0].Title.Should().Be("Nested1");
        deserialized.NestedMessageList[0].Inner.Name.Should().Be("Inner1");
        deserialized.NestedMessageList[0].Inner.Value.Should().Be(42);
        deserialized.NestedMessageList[0].Score.Should().Be(3.14);
    }

    [Fact]
    public void StringArrays_WithNullStrings_GP()
    {
        var model = new StringArraysTestModel
        {
            StringArrayWithNulls = new string[] { "First", null, "Third", null, "Fifth" }
        };

        var act = () => SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        act.Should().Throw<Exception>()
           .WithMessage("*element*null*");
    }

    [Fact]
    public void MessageArrays_CrossCompatibilityBidirectional_GP()
    {
        var model = new MessageArraysTestModel
        {
            SimpleMessages = new SimpleMessage[]
            {
                new SimpleMessage { Name = "Test1", Value = 100 },
                new SimpleMessage { Name = "Test2", Value = 200 }
            }
        };

        // GProtobuf serialize -> protobuf-net deserialize -> protobuf-net serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var protobufNetDeserialized = DeserializeWithProtobufNet<MessageArraysTestModel>(gprotobufData);
        var protobufNetData = SerializeWithProtobufNet(protobufNetDeserialized);
        var finalDeserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(bytes));

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.SimpleMessages.Should().HaveCount(2);
        finalDeserialized.SimpleMessages[0].Name.Should().Be("Test1");
        finalDeserialized.SimpleMessages[0].Value.Should().Be(100);
        finalDeserialized.SimpleMessages[1].Name.Should().Be("Test2");
        finalDeserialized.SimpleMessages[1].Value.Should().Be(200);
    }

    #endregion
}
