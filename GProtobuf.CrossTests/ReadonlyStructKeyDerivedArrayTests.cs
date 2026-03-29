using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using TestModelSerializers = GProtobuf.CrossTests.TestModel.Serialization.Serializers;
using TestModelDeserializers = GProtobuf.CrossTests.TestModel.Serialization.Deserializers;

namespace GProtobuf.Tests;

/// <summary>
/// Tests for Dictionary with readonly struct key and derived type array value.
/// Mirrors the original bug: Dictionary&lt;DeviceValueType, DeviceValueAggregationExtended[]&gt;
///
/// Bug symptoms:
/// 1. Deserialization skipped all fields from DeviceValueAggregationExtended
/// 2. Serialization produced bytes incompatible with protobuf-net
///
/// Fix: StandaloneTypeGenerator uses IsDerivedType() check to determine
/// whether to use Read/Write methods (for derived types) or ReadContent/WriteContent (for others).
/// </summary>
public sealed class ReadonlyStructKeyDerivedArrayTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public ReadonlyStructKeyDerivedArrayTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Data Factory

    private static ReadonlyStructKeyDerivedArrayModel CreateTestModel()
    {
        return new ReadonlyStructKeyDerivedArrayModel
        {
            Id = 42,
            Name = "Test Model",
            // Dictionary with readonly struct key and derived type array value
            ConversionsByValueType = new Dictionary<ValueTypeKey, LinearInterpolationConversion[]>
            {
                [new ValueTypeKey(100)] = new[]
                {
                    new LinearInterpolationConversion
                    {
                        SourceTypes = new List<int> { 1, 2, 3 },
                        TargetType = 10,
                        Value1OnInput = 0.0,
                        Value1OnOutput = 0.0,
                        Value2OnInput = 100.0,
                        Value2OnOutput = 255.0
                    },
                    new LinearInterpolationConversion
                    {
                        SourceTypes = new List<int> { 4, 5 },
                        TargetType = 20,
                        Value1OnInput = 10.5,
                        Value1OnOutput = 20.5,
                        Value2OnInput = 50.5,
                        Value2OnOutput = 100.5
                    }
                },
                [new ValueTypeKey(200)] = new[]
                {
                    new LinearInterpolationConversion
                    {
                        SourceTypes = new List<int> { 100 },
                        TargetType = 200,
                        Value1OnInput = 1.0,
                        Value1OnOutput = 2.0,
                        Value2OnInput = 3.0,
                        Value2OnOutput = 4.0
                    }
                }
            },
            SwitchConversionsByValueType = new Dictionary<ValueTypeKey, AnalogSwitchConversion[]>
            {
                [new ValueTypeKey(300)] = new[]
                {
                    new AnalogSwitchConversion
                    {
                        SourceTypes = new List<int> { 7, 8, 9 },
                        TargetType = 30,
                        Threshold = 50.5,
                        InvertLogic = true
                    }
                }
            }
        };
    }

    #endregion

    #region Cross-serialization Tests

    [Fact]
    public void ReadonlyStructKeyDerivedArray_PG_ShouldDeserializeAllFields()
    {
        // Serialize with protobuf-net, deserialize with GProtobuf
        var model = CreateTestModel();

        var data = SerializeWithProtobufNet(model);
        _output.WriteLine($"protobuf-net serialized size: {data.Length} bytes");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyDerivedArrayModel(bytes));

        // Verify basic fields
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test Model");

        // Verify LinearInterpolationConversion dictionary
        deserialized.ConversionsByValueType.Should().HaveCount(2);

        // Verify key 100 entry
        var key100 = new ValueTypeKey(100);
        deserialized.ConversionsByValueType.Should().ContainKey(key100);
        var conversions100 = deserialized.ConversionsByValueType[key100];
        conversions100.Should().HaveCount(2);

        // First conversion - verify ALL fields including derived fields
        conversions100[0].SourceTypes.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        conversions100[0].TargetType.Should().Be(10);
        conversions100[0].Value1OnInput.Should().Be(0.0);
        conversions100[0].Value1OnOutput.Should().Be(0.0);
        conversions100[0].Value2OnInput.Should().Be(100.0);
        conversions100[0].Value2OnOutput.Should().Be(255.0);

        // Second conversion
        conversions100[1].SourceTypes.Should().BeEquivalentTo(new[] { 4, 5 });
        conversions100[1].TargetType.Should().Be(20);
        conversions100[1].Value1OnInput.Should().Be(10.5);
        conversions100[1].Value1OnOutput.Should().Be(20.5);
        conversions100[1].Value2OnInput.Should().Be(50.5);
        conversions100[1].Value2OnOutput.Should().Be(100.5);

        // Verify key 200 entry
        var key200 = new ValueTypeKey(200);
        deserialized.ConversionsByValueType.Should().ContainKey(key200);
        var conversions200 = deserialized.ConversionsByValueType[key200];
        conversions200.Should().HaveCount(1);
        conversions200[0].Value2OnOutput.Should().Be(4.0);

        // Verify AnalogSwitchConversion dictionary
        deserialized.SwitchConversionsByValueType.Should().HaveCount(1);
        var key300 = new ValueTypeKey(300);
        deserialized.SwitchConversionsByValueType.Should().ContainKey(key300);
        var switchConversions = deserialized.SwitchConversionsByValueType[key300];
        switchConversions.Should().HaveCount(1);
        switchConversions[0].Threshold.Should().Be(50.5);
        switchConversions[0].InvertLogic.Should().BeTrue();
    }

    [Fact]
    public void ReadonlyStructKeyDerivedArray_GP_ShouldBeReadableByProtobufNet()
    {
        // Serialize with GProtobuf, deserialize with protobuf-net
        var model = CreateTestModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyDerivedArrayModel);
        _output.WriteLine($"GProtobuf serialized size: {data.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(data)}");

        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyDerivedArrayModel>(data);

        // Verify basic fields
        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test Model");

        // Verify dictionary structure
        deserialized.ConversionsByValueType.Should().HaveCount(2);

        // Verify key value (readonly struct)
        var key100 = new ValueTypeKey(100);
        deserialized.ConversionsByValueType.Should().ContainKey(key100);

        // Verify derived type fields are correctly serialized
        var conversions = deserialized.ConversionsByValueType[key100];
        conversions.Should().HaveCount(2);
        conversions[0].Value2OnOutput.Should().Be(255.0);

        // Verify switch conversions
        deserialized.SwitchConversionsByValueType.Should().HaveCount(1);
        var key300 = new ValueTypeKey(300);
        deserialized.SwitchConversionsByValueType[key300][0].Threshold.Should().Be(50.5);
    }

    [Fact]
    public void ReadonlyStructKeyDerivedArray_GG_ShouldRoundtrip()
    {
        // Serialize and deserialize with GProtobuf
        var model = CreateTestModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyDerivedArrayModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyDerivedArrayModel(bytes));

        deserialized.Id.Should().Be(42);
        deserialized.Name.Should().Be("Test Model");
        deserialized.ConversionsByValueType.Should().HaveCount(2);
        deserialized.ConversionsByValueType[new ValueTypeKey(100)][0].Value2OnOutput.Should().Be(255.0);
        deserialized.SwitchConversionsByValueType[new ValueTypeKey(300)][0].Threshold.Should().Be(50.5);
    }

    #endregion

    #region Binary Compatibility Tests

    [Fact]
    public void ReadonlyStructKeyDerivedArray_BinaryCompatibility_GProtobufEqualsProtobufNet()
    {
        var model = CreateTestModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyDerivedArrayModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        _output.WriteLine($"GProtobuf size: {gprotobufData.Length} bytes");
        _output.WriteLine($"protobuf-net size: {protobufNetData.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(gprotobufData)}");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(protobufNetData)}");

        // This is the key test - sizes should match
        // If GProtobuf uses ProtoInclude wrapper incorrectly, sizes will differ
        gprotobufData.Length.Should().Be(protobufNetData.Length,
            "GProtobuf and protobuf-net should produce identical byte sizes. " +
            "If different, GProtobuf may be incorrectly handling derived types or readonly struct keys.");

        gprotobufData.Should().Equal(protobufNetData,
            "GProtobuf and protobuf-net should produce identical bytes for Dictionary<ValueTypeKey, DerivedType[]>");
    }

    #endregion

    #region Specific Bug Regression Tests

    [Fact]
    public void ReadonlyStructKeyDerivedArray_DerivedFieldsShouldNotBeSkipped()
    {
        // This test specifically verifies the bug fix:
        // Bug: Deserialization skipped all fields from the derived class
        var model = CreateTestModel();

        // Serialize with protobuf-net (correct format)
        var data = SerializeWithProtobufNet(model);

        // Deserialize with GProtobuf
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyDerivedArrayModel(bytes));

        // The bug caused these fields to be 0.0 or default
        var conversion = deserialized.ConversionsByValueType[new ValueTypeKey(100)][0];

        conversion.Value1OnInput.Should().Be(0.0, "Value1OnInput should be read");
        conversion.Value1OnOutput.Should().Be(0.0, "Value1OnOutput should be read");
        conversion.Value2OnInput.Should().Be(100.0, "Value2OnInput should NOT be skipped (bug fix)");
        conversion.Value2OnOutput.Should().Be(255.0, "Value2OnOutput should NOT be skipped (bug fix)");

        // Also verify base class fields
        conversion.SourceTypes.Should().NotBeNullOrEmpty("SourceTypes (base class) should be read");
        conversion.TargetType.Should().Be(10, "TargetType (base class) should be read");
    }

    [Fact]
    public void ReadonlyStructKeyDerivedArray_SerializationShouldBeCompatible()
    {
        // This test specifically verifies the bug fix:
        // Bug: Serialization produced bytes that protobuf-net couldn't read
        var model = CreateTestModel();

        // Serialize with GProtobuf
        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyDerivedArrayModel);

        // The bug caused this to fail or produce wrong values
        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyDerivedArrayModel>(data);

        // Verify the data is correctly readable
        var conversion = deserialized.ConversionsByValueType[new ValueTypeKey(100)][0];

        conversion.Value2OnOutput.Should().Be(255.0,
            "protobuf-net should be able to read GProtobuf serialized data correctly (bug fix)");
        conversion.SourceTypes.Should().BeEquivalentTo(new[] { 1, 2, 3 },
            "Base class fields should also be readable");
    }

    #endregion
}
