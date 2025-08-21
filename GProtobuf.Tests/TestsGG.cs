using FluentAssertions;
using GProtobuf.Tests.TestModel;
//using GProtobuf.Generator;
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

    [Fact]
    public void NullableTypes_GuidNull_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0, "nullable Guid with null value should not be serialized");
        
        deserialized.NullableGuidValue.Should().BeNull();
    }

    [Fact]
    public void NullableTypes_GuidWithValue_GG()
    {
        var testGuid = Guid.NewGuid();
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0, "nullable Guid with value should be serialized");
        
        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void NullableTypes_GuidEmpty_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = Guid.Empty
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0, "nullable Guid with Guid.Empty should still be serialized");
        
        deserialized.NullableGuidValue.Should().Be(Guid.Empty);
    }

    #region Array Types Tests - Int Arrays

    [Fact]
    public void IntArray_NonPacked_Default_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPacked = new[] { 1, 2, 3, 100, 1000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.IntArrayNonPacked.Should().Equal(1, 2, 3, 100, 1000);
    }

    [Fact]
    public void IntArray_NonPacked_ZigZag_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedZigZag = new[] { -1, -100, 0, 50, -50 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayNonPackedZigZag.Should().Equal(-1, -100, 0, 50, -50);
    }

    [Fact]
    public void IntArray_NonPacked_FixedSize_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNonPackedFixed = new[] { int.MaxValue, int.MinValue, 0, 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayNonPackedFixed.Should().Equal(int.MaxValue, int.MinValue, 0, 42);
    }

    [Fact]
    public void IntArray_Packed_Default_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000);
    }

    [Fact]
    public void IntArray_Packed_ZigZag_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedZigZag = new[] { -1, -100, 0, 50, -50 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        Console.WriteLine(String.Join(',',data));
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayPackedZigZag.Should().Equal(-1, -100, 0, 50, -50);
    }

    [Fact]
    public void IntArray_Packed_FixedSize_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedFixed = new[] { int.MaxValue, int.MinValue, 0, 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayPackedFixed.Should().Equal(int.MaxValue, int.MinValue, 0, 42);
    }

    #endregion

    #region Array Types Tests - Byte Arrays

    [Fact]
    public void ByteArray_Basic_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[] { 0, 1, 255, 128, 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.ByteArray.Should().Equal(new byte[] { 0, 1, 255, 128, 42 });
    }

    [Fact]
    public void ByteArray_Empty_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = new byte[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.ByteArray.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ByteArray_Large_GG()
    {
        var largeByteArray = System.Linq.Enumerable.Range(0, 10000).Select(i => (byte)(i % 256)).ToArray();
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = largeByteArray
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.ByteArray.Should().Equal(largeByteArray);
    }

    #endregion

    #region Array Types Tests - Null and Empty

    [Fact]
    public void IntArray_Null_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayNull = null,
            IntArrayPackedNull = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0, "null arrays should not be serialized");
        deserialized.IntArrayNull.Should().BeNull();
        deserialized.IntArrayPackedNull.Should().BeNull();
    }

    [Fact]
    public void IntArray_Empty_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayEmpty = new int[0],
            IntArrayPackedEmpty = new int[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayEmpty.Should().BeNullOrEmpty();
        deserialized.IntArrayPackedEmpty.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ByteArray_Null_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            ByteArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        data.Length.Should().Be(0, "null byte array should not be serialized");
        deserialized.ByteArray.Should().BeNull();
    }

    #endregion

    #region Array Types Tests - Performance and Edge Cases

    [Fact]
    public void IntArray_Large_Packed_vs_NonPacked_GG()
    {
        var largeArray = System.Linq.Enumerable.Range(1, 1000).ToArray();
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPackedLarge = largeArray,
            IntArrayNonPackedLarge = largeArray
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        data.Should().NotBeNull();
        deserialized.IntArrayPackedLarge.Should().Equal(largeArray);
        deserialized.IntArrayNonPackedLarge.Should().Equal(largeArray);
    }

    [Fact]
    public void IntArray_ExtremeValues_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { int.MinValue, int.MaxValue, 0, -1, 1 },
            IntArrayPackedZigZag = new[] { int.MinValue, int.MaxValue, 0, -1, 1 },
            IntArrayPackedFixed = new[] { int.MinValue, int.MaxValue, 0, -1, 1 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
        deserialized.IntArrayPackedZigZag.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
        deserialized.IntArrayPackedFixed.Should().Equal(int.MinValue, int.MaxValue, 0, -1, 1);
    }

    #endregion

    #region ByteArray Tests (GProtobuf to GProtobuf)

    [Fact]
    public void ByteArray_BasicValues_GG()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 0, 1, 2, 127, 128, 254, 255 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BasicByteArray.Should().Equal(0, 1, 2, 127, 128, 254, 255);
    }

    [Fact]
    public void ByteArray_EmptyArray_GG()
    {
        var model = new ByteArrayTestModel
        {
            EmptyByteArray = new byte[0]
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.EmptyByteArray.Should().NotBeNull();
        deserialized.EmptyByteArray.Should().BeEmpty();
    }

    [Fact]
    public void ByteArray_NullArray_GG()
    {
        var model = new ByteArrayTestModel
        {
            NullByteArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.NullByteArray.Should().BeNull();
    }

    [Fact]
    public void ByteArray_LargeArray_GG()
    {
        // Create a large byte array with pattern
        var largeArray = new byte[5000];
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = (byte)(i % 256);
        }

        var model = new ByteArrayTestModel
        {
            LargeByteArray = largeArray
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LargeByteArray.Should().Equal(largeArray);
        deserialized.LargeByteArray.Length.Should().Be(5000);
    }

    [Fact]
    public void ByteArray_AllPossibleByteValues_GG()
    {
        // Test all possible byte values (0-255)
        var allBytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            allBytes[i] = (byte)i;
        }

        var model = new ByteArrayTestModel
        {
            AllPossibleBytes = allBytes
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.AllPossibleBytes.Should().Equal(allBytes);
        deserialized.AllPossibleBytes.Length.Should().Be(256);
        
        // Verify specific values are preserved
        deserialized.AllPossibleBytes[0].Should().Be(0);
        deserialized.AllPossibleBytes[127].Should().Be(127);
        deserialized.AllPossibleBytes[128].Should().Be(128);
        deserialized.AllPossibleBytes[255].Should().Be(255);
    }

    [Fact]
    public void ByteArray_BinaryDataSimulation_GG()
    {
        // Simulate typical binary data patterns
        var binaryData = new byte[]
        {
            // Binary file header simulation
            0x50, 0x4B, 0x03, 0x04, // ZIP file signature
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            // Random binary data
            0xDE, 0xAD, 0xBE, 0xEF,
            0xCA, 0xFE, 0xBA, 0xBE,
            // Edge cases
            0x00, 0xFF, 0x7F, 0x80
        };

        var model = new ByteArrayTestModel
        {
            BinaryData = binaryData
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BinaryData.Should().Equal(binaryData);
    }

    [Fact]
    public void ByteArray_MultipleFieldsWithDifferentSizes_GG()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 1, 2, 3 },
            EmptyByteArray = new byte[0],
            LargeByteArray = new byte[500], // Smaller for GG tests
            AllPossibleBytes = new byte[] { 0, 127, 128, 255 }
        };

        // Fill large array with pattern
        for (int i = 0; i < model.LargeByteArray.Length; i++)
        {
            model.LargeByteArray[i] = (byte)(i % 100);
        }

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BasicByteArray.Should().Equal(1, 2, 3);
        deserialized.EmptyByteArray.Should().BeEmpty();
        deserialized.LargeByteArray.Should().Equal(model.LargeByteArray);
        deserialized.AllPossibleBytes.Should().Equal(0, 127, 128, 255);
    }

    #endregion

    #region PrimitiveArrays Tests (GProtobuf to GProtobuf)

    [Fact]
    public void PrimitiveArrays_FloatArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new float[] { 1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue },
            FloatArrayPacked = new float[] { 3.14f, 2.71f, -1.0f, 100.5f }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArray.Should().Equal(1.5f, -2.3f, 0.0f, float.MaxValue, float.MinValue);
        deserialized.FloatArrayPacked.Should().Equal(3.14f, 2.71f, -1.0f, 100.5f);
    }

    [Fact]
    public void PrimitiveArrays_DoubleArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            DoubleArray = new double[] { 1.5, -2.3, 0.0, double.MaxValue, double.MinValue },
            DoubleArrayPacked = new double[] { Math.PI, Math.E, -1.0, 1000.123456789 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.DoubleArray.Should().Equal(1.5, -2.3, 0.0, double.MaxValue, double.MinValue);
        deserialized.DoubleArrayPacked.Should().Equal(Math.PI, Math.E, -1.0, 1000.123456789);
    }

    [Fact]
    public void PrimitiveArrays_LongArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[] { 1L, -2L, 0L, long.MaxValue, long.MinValue },
            LongArrayPacked = new long[] { 100L, 200L, 300L },
            LongArrayPackedZigZag = new long[] { -1L, -100L, 50L, 0L, 1000L, -5000L },
            LongArrayPackedFixed = new long[] { 12345L, 67890L, -11111L, 0L, 99999L }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArray.Should().Equal(1L, -2L, 0L, long.MaxValue, long.MinValue);
        deserialized.LongArrayPacked.Should().Equal(100L, 200L, 300L);
        deserialized.LongArrayPackedZigZag.Should().Equal(-1L, -100L, 50L, 0L, 1000L, -5000L);
        deserialized.LongArrayPackedFixed.Should().Equal(12345L, 67890L, -11111L, 0L, 99999L);
    }

    [Fact]
    public void PrimitiveArrays_BoolArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            BoolArray = new bool[] { true, false, true, true, false },
            BoolArrayPacked = new bool[] { false, true, false, true }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BoolArray.Should().Equal(true, false, true, true, false);
        deserialized.BoolArrayPacked.Should().Equal(false, true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_EmptyAndNullArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArrayEmpty = new float[0],
            FloatArrayNull = null,
            DoubleArrayPackedEmpty = new double[0],
            DoubleArrayPackedNull = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.FloatArrayEmpty.Should().BeNull();
        deserialized.FloatArrayNull.Should().BeNull();
        deserialized.DoubleArrayPackedEmpty.Should().BeEmpty();
        deserialized.DoubleArrayPackedNull.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_SpecialFloatingPointValues_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArrayWithSpecialValues = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.0f, -0.0f },
            DoubleArrayWithSpecialValues = new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        
        // Special handling for NaN comparison
        deserialized.FloatArrayWithSpecialValues[0].Should().Be(float.NaN);
        deserialized.FloatArrayWithSpecialValues[1].Should().Be(float.PositiveInfinity);
        deserialized.FloatArrayWithSpecialValues[2].Should().Be(float.NegativeInfinity);
        deserialized.FloatArrayWithSpecialValues[3].Should().Be(0.0f);
        deserialized.FloatArrayWithSpecialValues[4].Should().Be(-0.0f);
        
        deserialized.DoubleArrayWithSpecialValues[0].Should().Be(double.NaN);
        deserialized.DoubleArrayWithSpecialValues[1].Should().Be(double.PositiveInfinity);
        deserialized.DoubleArrayWithSpecialValues[2].Should().Be(double.NegativeInfinity);
        deserialized.DoubleArrayWithSpecialValues[3].Should().Be(0.0);
        deserialized.DoubleArrayWithSpecialValues[4].Should().Be(-0.0);
    }

    [Fact]
    public void PrimitiveArrays_ExtremeValues_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArrayWithExtremeValues = new long[] { long.MinValue, long.MaxValue, 0L, -1L, 1L },
            BoolArrayMixed = new bool[] { true, false, false, true, true, false, true }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LongArrayWithExtremeValues.Should().Equal(long.MinValue, long.MaxValue, 0L, -1L, 1L);
        deserialized.BoolArrayMixed.Should().Equal(true, false, false, true, true, false, true);
    }

    [Fact]
    public void PrimitiveArrays_SByteArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, -1, 0, 1, 127 },
            SByteArrayPacked = new sbyte[] { -50, -25, 0, 25, 50 },
            SByteArrayPackedZigZag = new sbyte[] { -100, -1, 0, 1, 100 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArray.Should().Equal(-128, -1, 0, 1, 127);
        deserialized.SByteArrayPacked.Should().Equal(-50, -25, 0, 25, 50);
        deserialized.SByteArrayPackedZigZag.Should().Equal(-100, -1, 0, 1, 100);
    }

    [Fact]
    public void PrimitiveArrays_ShortArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            ShortArray = new short[] { short.MinValue, -1000, 0, 1000, short.MaxValue },
            ShortArrayPacked = new short[] { 100, 200, 300, 400 },
            ShortArrayPackedZigZag = new short[] { -500, -100, 0, 100, 500 },
            ShortArrayPackedFixed = new short[] { 1000, 2000, 3000, -1000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.ShortArray.Should().Equal(short.MinValue, -1000, 0, 1000, short.MaxValue);
        deserialized.ShortArrayPacked.Should().Equal(100, 200, 300, 400);
        deserialized.ShortArrayPackedZigZag.Should().Equal(-500, -100, 0, 100, 500);
        deserialized.ShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, -1000);
    }

    [Fact]
    public void PrimitiveArrays_UShortArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            UShortArray = new ushort[] { 0, 1000, 30000, ushort.MaxValue },
            UShortArrayPacked = new ushort[] { 100, 200, 300, 400, 500 },
            UShortArrayPackedFixed = new ushort[] { 1000, 2000, 3000, 4000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.UShortArray.Should().Equal(0, 1000, 30000, ushort.MaxValue);
        deserialized.UShortArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UShortArrayPackedFixed.Should().Equal(1000, 2000, 3000, 4000);
    }

    [Fact]
    public void PrimitiveArrays_UIntArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            UIntArray = new uint[] { 0, 1000000, 2000000000, uint.MaxValue },
            UIntArrayPacked = new uint[] { 100, 200, 300, 400, 500 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000, 3000000 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.UIntArray.Should().Equal(0, 1000000, 2000000000, uint.MaxValue);
        deserialized.UIntArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000, 3000000);
    }

    [Fact]
    public void PrimitiveArrays_ULongArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            ULongArray = new ulong[] { 0, 1000000000000UL, ulong.MaxValue },
            ULongArrayPacked = new ulong[] { 100, 200, 300, 400, 500 },
            ULongArrayPackedFixed = new ulong[] { 1000000000UL, 2000000000UL, 3000000000UL }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.ULongArray.Should().Equal(0, 1000000000000UL, ulong.MaxValue);
        deserialized.ULongArrayPacked.Should().Equal(100, 200, 300, 400, 500);
        deserialized.ULongArrayPackedFixed.Should().Equal(1000000000UL, 2000000000UL, 3000000000UL);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesEmptyAndNullArrays_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayEmpty = new sbyte[0],
            ShortArrayNull = null,
            UShortArrayPackedEmpty = new ushort[0],
            UIntArrayPackedNull = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayEmpty.Should().BeNullOrEmpty();
        deserialized.ShortArrayNull.Should().BeNull();
        deserialized.UShortArrayPackedEmpty.Should().BeNullOrEmpty();
        deserialized.UIntArrayPackedNull.Should().BeNull();
    }

    [Fact]
    public void PrimitiveArrays_NewTypesExtremeValues_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArrayExtremes = new sbyte[] { sbyte.MinValue, sbyte.MaxValue, 0, -1, 1 },
            UShortArrayExtremes = new ushort[] { ushort.MinValue, ushort.MaxValue, 0, 1 },
            UIntArrayExtremes = new uint[] { uint.MinValue, uint.MaxValue, 0, 1 },
            ULongArrayExtremes = new ulong[] { ulong.MinValue, ulong.MaxValue, 0, 1 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SByteArrayExtremes.Should().Equal(sbyte.MinValue, sbyte.MaxValue, 0, -1, 1);
        deserialized.UShortArrayExtremes.Should().Equal(ushort.MinValue, ushort.MaxValue, 0, 1);
        deserialized.UIntArrayExtremes.Should().Equal(uint.MinValue, uint.MaxValue, 0, 1);
        deserialized.ULongArrayExtremes.Should().Equal(ulong.MinValue, ulong.MaxValue, 0, 1);
    }

    #endregion

    #region String Arrays Tests (GProtobuf to GProtobuf)

    [Fact]
    public void StringArrays_BasicValues_GG()
    {
        var model = new StringArraysTestModel
        {
            BasicStringArray = new string[] { "Hello", "World", "Test", "Array" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.BasicStringArray.Should().Equal("Hello", "World", "Test", "Array");
    }

    [Fact]
    public void StringArrays_EmptyAndNullArrays_GG()
    {
        var model = new StringArraysTestModel
        {
            EmptyStringArray = new string[0],
            NullStringArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.EmptyStringArray.Should().BeNullOrEmpty();
        deserialized.NullStringArray.Should().BeNull();
    }

    [Fact]
    public void StringArrays_WithNullStrings_GG()
    {
        var model = new StringArraysTestModel
        {
            StringArrayWithNulls = new string[] { "First", null, "Third", null, "Fifth" }
        };

        // GProtobuf should throw exception when trying to serialize null elements
        var act = () => SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*element of type string was null*");
    }

    [Fact]
    public void StringArrays_SpecialCharacters_GG()
    {
        var model = new StringArraysTestModel
        {
            SpecialCharStringArray = new string[] { 
                "", 
                " ", 
                "\t", 
                "\n", 
                "\r\n", 
                "\"quotes\"", 
                "'apostrophe'",
                "special!@#$%^&*()",
                "line1\nline2"
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SpecialCharStringArray.Should().NotBeNull();
    }

    [Fact]
    public void StringArrays_UnicodeCharacters_GG()
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
        var deserialized = TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.UnicodeStringArray.Should().NotBeNull();
    }

    [Fact]
    public void StringArrays_LongStrings_GG()
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
        var deserialized = TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.LongStringArray.Should().Equal(longString1, longString2, longString3);
    }

    #endregion

    #region Message Arrays Tests (GProtobuf to GProtobuf)

    [Fact]
    public void MessageArrays_SimpleMessages_GG()
    {
        var model = new MessageArraysTestModel
        {
            SimpleMessages = new SimpleMessage[]
            {
                new SimpleMessage { Name = "First", Value = 1 },
                new SimpleMessage { Name = "Second", Value = 2 },
                new SimpleMessage { Name = "Third", Value = 3 }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.SimpleMessages.Should().NotBeNull();
        deserialized.SimpleMessages.Should().HaveCount(3);
        deserialized.SimpleMessages[0].Name.Should().Be("First");
        deserialized.SimpleMessages[0].Value.Should().Be(1);
        deserialized.SimpleMessages[1].Name.Should().Be("Second");
        deserialized.SimpleMessages[1].Value.Should().Be(2);
        deserialized.SimpleMessages[2].Name.Should().Be("Third");
        deserialized.SimpleMessages[2].Value.Should().Be(3);
    }

    [Fact]
    public void MessageArrays_NestedMessages_GG()
    {
        var model = new MessageArraysTestModel
        {
            NestedMessages = new NestedMessage[]
            {
                new NestedMessage 
                { 
                    Title = "First Nested",
                    Inner = new SimpleMessage { Name = "Inner1", Value = 10 },
                    Score = 95.5
                },
                new NestedMessage 
                { 
                    Title = "Second Nested",
                    Inner = new SimpleMessage { Name = "Inner2", Value = 20 },
                    Score = 87.3
                }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.NestedMessages.Should().NotBeNull();
        deserialized.NestedMessages.Should().HaveCount(2);
        
        deserialized.NestedMessages[0].Title.Should().Be("First Nested");
        deserialized.NestedMessages[0].Inner.Should().NotBeNull();
        deserialized.NestedMessages[0].Inner.Name.Should().Be("Inner1");
        deserialized.NestedMessages[0].Inner.Value.Should().Be(10);
        deserialized.NestedMessages[0].Score.Should().Be(95.5);
        
        deserialized.NestedMessages[1].Title.Should().Be("Second Nested");
        deserialized.NestedMessages[1].Inner.Should().NotBeNull();
        deserialized.NestedMessages[1].Inner.Name.Should().Be("Inner2");
        deserialized.NestedMessages[1].Inner.Value.Should().Be(20);
        deserialized.NestedMessages[1].Score.Should().Be(87.3);
    }

    [Fact]
    public void MessageArrays_EmptyAndNullArrays_GG()
    {
        var model = new MessageArraysTestModel
        {
            EmptyMessageArray = new SimpleMessage[0],
            NullMessageArray = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.EmptyMessageArray.Should().BeNullOrEmpty();
        deserialized.NullMessageArray.Should().BeNull();
    }

    [Fact]
    public void MessageArrays_WithNullMessages_GG()
    {
        var model = new MessageArraysTestModel
        {
            MessageArrayWithNulls = new SimpleMessage[]
            {
                new SimpleMessage { Name = "First", Value = 1 },
                null,
                new SimpleMessage { Name = "Third", Value = 3 },
                null
            }
        };

        // GProtobuf should throw exception when trying to serialize null elements
        var act = () => SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*element of type SimpleMessage was null*");
    }

    [Fact]
    public void MessageArrays_ComplexScenario_GG()
    {
        var model = new MessageArraysTestModel
        {
            ComplexMessageArray = new NestedMessage[]
            {
                new NestedMessage 
                { 
                    Title = "Complex1",
                    Inner = null, // null inner message
                    Score = 0.0
                },
                new NestedMessage 
                { 
                    Title = "",  // empty string
                    Inner = new SimpleMessage { Name = null, Value = -100 }, // null name, negative value
                    Score = double.MaxValue
                },
                new NestedMessage 
                { 
                    Title = "Normal",
                    Inner = new SimpleMessage { Name = "Normal", Value = 42 },
                    Score = 3.14159
                }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(data);

        deserialized.Should().NotBeNull();
        deserialized.ComplexMessageArray.Should().NotBeNull();
        deserialized.ComplexMessageArray.Should().HaveCount(3);
    }

    #endregion

    #region Additional Self-Compatibility Tests

    [Fact]
    public void NullableTypes_SelfCompatibility_GG()
    {
        var model = new TestModel.NullableTypesModel
        {
            NullableIntValue = 42,
            NullableLongValue = 123456789L,
            NullableDoubleValue = 3.14159,
            NullableBoolValue = true
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableIntValue.Should().Be(42);
        deserialized.NullableLongValue.Should().Be(123456789L);
        deserialized.NullableDoubleValue.Should().Be(3.14159);
        deserialized.NullableBoolValue.Should().Be(true);
    }

    [Fact]
    public void NullableTypes_GuidSelfCompatibility_GG()
    {
        var testGuid = Guid.NewGuid();
        var model = new TestModel.NullableTypesModel
        {
            NullableGuidValue = testGuid
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeNullableTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeNullableTypesModel(bytes));

        deserialized.NullableGuidValue.Should().Be(testGuid);
    }

    [Fact]
    public void ArrayTypes_SelfCompatibility_GG()
    {
        var model = new TestModel.ArrayTypesModel
        {
            IntArrayPacked = new[] { 1, 2, 3, 100, 1000 },
            IntArrayNonPacked = new[] { -1, -2, 0, 50 },
            ByteArray = new byte[] { 0, 1, 255, 128, 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeArrayTypesModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeArrayTypesModel(bytes));

        deserialized.IntArrayPacked.Should().Equal(1, 2, 3, 100, 1000);
        deserialized.IntArrayNonPacked.Should().Equal(-1, -2, 0, 50);
        deserialized.ByteArray.Should().Equal(0, 1, 255, 128, 42);
    }

    [Fact]  
    public void ByteArray_CrossCompatibilityBidirectional_GG()
    {
        // Test that data can roundtrip: GProtobuf -> GProtobuf -> GProtobuf
        var originalData = new byte[1000];
        for (int i = 0; i < originalData.Length; i++)
        {
            originalData[i] = (byte)(i % 256);
        }

        var model = new ByteArrayTestModel
        {
            LargeByteArray = originalData
        };

        // GProtobuf serialize -> GProtobuf deserialize -> GProtobuf serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var gprotobufDeserialized = DeserializeWithGProtobuf(gprotobufData, bytes => TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(bytes));
        var gprotobufData2 = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var finalDeserialized = DeserializeWithGProtobuf(gprotobufData2, bytes => TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(bytes));

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.LargeByteArray.Should().NotBeNull();
        finalDeserialized.LargeByteArray.Should().Equal(originalData);
    }

    [Fact]
    public void ByteArray_PackedEncodingNotApplicable_GG()
    {
        // Byte arrays should not use packed encoding - they are length-delimited
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[] { 1, 2, 3, 4, 5 },
            EdgeCaseBytes = new byte[] { 10, 20, 30 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(bytes));

        deserialized.Should().NotBeNull();
        deserialized.BasicByteArray.Should().Equal(1, 2, 3, 4, 5);
        deserialized.EdgeCaseBytes.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void PrimitiveArrays_CrossCompatibilityBidirectional_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -128, 0, 127 },
            ShortArrayPackedZigZag = new short[] { -1000, 0, 1000 },
            UShortArrayPacked = new ushort[] { 100, 200, 300 },
            UIntArrayPackedFixed = new uint[] { 1000000, 2000000 },
            ULongArrayPacked = new ulong[] { 100UL, 200UL, 300UL }
        };

        // GProtobuf serialize -> GProtobuf deserialize -> GProtobuf serialize -> GProtobuf deserialize
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var gprotobufDeserialized = DeserializeWithGProtobuf(gprotobufData, bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(bytes));
        var gprotobufData2 = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var finalDeserialized = DeserializeWithGProtobuf(gprotobufData2, bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(bytes));

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.SByteArray.Should().Equal(-128, 0, 127);
        finalDeserialized.ShortArrayPackedZigZag.Should().Equal(-1000, 0, 1000);
        finalDeserialized.UShortArrayPacked.Should().Equal(100, 200, 300);
        finalDeserialized.UIntArrayPackedFixed.Should().Equal(1000000, 2000000);
        finalDeserialized.ULongArrayPacked.Should().Equal(100UL, 200UL, 300UL);
    }

    [Fact]
    public void PrimitiveArrays_NewTypesCrossCompatibilityBidirectional_GG()
    {
        var model = new PrimitiveArraysTestModel
        {
            SByteArray = new sbyte[] { -50, 0, 50 },
            ShortArrayPackedZigZag = new short[] { -500, 0, 500 },
            UShortArrayPacked = new ushort[] { 10, 20, 30 },
            UIntArrayPackedFixed = new uint[] { 100000, 200000 },
            ULongArrayPacked = new ulong[] { 10UL, 20UL, 30UL }
        };

        // GProtobuf bidirectional consistency test
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var gprotobufDeserialized = DeserializeWithGProtobuf(gprotobufData, bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(bytes));
        var gprotobufData2 = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var finalDeserialized = DeserializeWithGProtobuf(gprotobufData2, bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(bytes));

        finalDeserialized.Should().NotBeNull();
        finalDeserialized.SByteArray.Should().Equal(-50, 0, 50);
        finalDeserialized.ShortArrayPackedZigZag.Should().Equal(-500, 0, 500);
        finalDeserialized.UShortArrayPacked.Should().Equal(10, 20, 30);
        finalDeserialized.UIntArrayPackedFixed.Should().Equal(100000, 200000);
        finalDeserialized.ULongArrayPacked.Should().Equal(10UL, 20UL, 30UL);
    }

    [Fact]
    public void StringArrays_SpecialCharactersAndUnicode_GG()
    {
        var model = new StringArraysTestModel
        {
            SpecialCharStringArray = new string[] { "hello\nworld", "tab\there", "quote\"test" },
            UnicodeStringArray = new string[] { "Ľubomír", "Žitný", "Košice", "Bratislava", "ťŠčÝáé" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(bytes));

        deserialized.Should().NotBeNull();
        deserialized.SpecialCharStringArray.Should().Equal("hello\nworld", "tab\there", "quote\"test");
        deserialized.UnicodeStringArray.Should().Equal("Ľubomír", "Žitný", "Košice", "Bratislava", "ťŠčÝáé");
    }

    [Fact]
    public void ListMessageTypes_SelfCompatibility_GG()
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

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeCollectionTypesTestModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeCollectionTypesTestModel(bytes));

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
    public void PrimitiveCollections_ByteCollections_GG()
    {
        var model = new PrimitiveCollectionsTestModel
        {
            ByteList = new List<byte> { 1, 2, 3, 255, 0, 128 },
            ByteICollection = new List<byte> { 10, 20, 30 },
            ByteIList = new List<byte> { 100, 200 },
            ByteIEnumerable = new List<byte> { 42 }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveCollectionsTestModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveCollectionsTestModel(bytes));

        deserialized.Should().NotBeNull();
        deserialized.ByteList.Should().Equal(1, 2, 3, 255, 0, 128);
        deserialized.ByteICollection.Should().Equal(10, 20, 30);
        deserialized.ByteIList.Should().Equal(100, 200);
        deserialized.ByteIEnumerable.Should().Equal(42);
    }

    #endregion
}
