using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using TestModelSerializers = GProtobuf.CrossTests.TestModel.Serialization.Serializers;
using TestModelDeserializers = GProtobuf.CrossTests.TestModel.Serialization.Deserializers;

namespace GProtobuf.Tests;

/// <summary>
/// Tests for ProtoInclude hierarchy with derived class own fields.
///
/// These tests verify the fix for:
/// - Bug: SizeCalculatorGenerator used ContentSize (all fields) for ProtoInclude wrapper,
///        but StreamWriter used OwnFieldsSize (own fields only).
/// - Fix: Use OwnFieldsSize in SizeCalculatorGenerator for ProtoInclude wrapper calculation.
///
/// This bug caused "Buffer overrun" or "Sub-message not read correctly" errors
/// when derived classes had their own fields (not inherited from base).
/// </summary>
public sealed class ProtoIncludeDerivedFieldsTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public ProtoIncludeDerivedFieldsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Data Factories

    private static ProtoIncludeDerivedFieldsModel CreateLinearInterpolationModel()
    {
        return new ProtoIncludeDerivedFieldsModel
        {
            Id = 12345,
            Name = "Test Model",
            Conversions = new List<ConversionBase>
            {
                new LinearInterpolationConversion
                {
                    SourceTypes = new List<int> { 1, 2 },
                    TargetType = 3,
                    Value1OnInput = 0.0,
                    Value1OnOutput = 0.0,
                    Value2OnInput = 100.0,
                    Value2OnOutput = 255.0
                }
            },
            PrimaryConversion = new LinearInterpolationConversion
            {
                SourceTypes = new List<int> { 10 },
                TargetType = 20,
                Value1OnInput = 10.5,
                Value1OnOutput = 20.5,
                Value2OnInput = 30.5,
                Value2OnOutput = 40.5
            }
        };
    }

    private static ProtoIncludeDerivedFieldsModel CreateMixedConversionsModel()
    {
        return new ProtoIncludeDerivedFieldsModel
        {
            Id = 99999,
            Name = "Mixed Conversions",
            Conversions = new List<ConversionBase>
            {
                new LinearInterpolationConversion
                {
                    SourceTypes = new List<int> { 1 },
                    TargetType = 2,
                    Value1OnInput = 0.0,
                    Value1OnOutput = 0.0,
                    Value2OnInput = 100.0,
                    Value2OnOutput = 100.0
                },
                new AnalogSwitchConversion
                {
                    SourceTypes = new List<int> { 3, 4 },
                    TargetType = 5,
                    Threshold = 50.5,
                    InvertLogic = true
                },
                new MultiValueConversion
                {
                    SourceTypes = new List<int> { 6 },
                    TargetType = 7,
                    Multipliers = new List<double> { 1.0, 2.0, 3.0 },
                    Formula = "x * 2 + 1"
                }
            }
        };
    }

    private static ProtoIncludeDerivedFieldsModel CreateDefaultValuesModel()
    {
        // Tests with default values (0.0 for doubles) - these should not be serialized
        return new ProtoIncludeDerivedFieldsModel
        {
            Id = 1,
            Name = "Default Values Test",
            Conversions = new List<ConversionBase>
            {
                new LinearInterpolationConversion
                {
                    SourceTypes = new List<int> { 1 },
                    TargetType = 2,
                    Value1OnInput = 0.0,  // default
                    Value1OnOutput = 0.0, // default
                    Value2OnInput = 100.0,
                    Value2OnOutput = 255.0
                }
            }
        };
    }

    #endregion

    #region LinearInterpolationConversion Tests

    [Fact]
    public void LinearInterpolation_PG()
    {
        var model = CreateLinearInterpolationModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        deserialized.Id.Should().Be(12345);
        deserialized.Name.Should().Be("Test Model");
        deserialized.Conversions.Should().HaveCount(1);

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.SourceTypes.Should().BeEquivalentTo(new[] { 1, 2 });
        conversion.TargetType.Should().Be(3);
        conversion.Value1OnInput.Should().Be(0.0);
        conversion.Value1OnOutput.Should().Be(0.0);
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);

        var primary = deserialized.PrimaryConversion.Should().BeOfType<LinearInterpolationConversion>().Subject;
        primary.Value1OnInput.Should().Be(10.5);
        primary.Value1OnOutput.Should().Be(20.5);
        primary.Value2OnInput.Should().Be(30.5);
        primary.Value2OnOutput.Should().Be(40.5);
    }

    [Fact]
    public void LinearInterpolation_GG()
    {
        var model = CreateLinearInterpolationModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        deserialized.Id.Should().Be(12345);
        deserialized.Conversions.Should().HaveCount(1);

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);
    }

    [Fact]
    public void LinearInterpolation_GP()
    {
        var model = CreateLinearInterpolationModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithProtobufNet<ProtoIncludeDerivedFieldsModel>(data);

        deserialized.Id.Should().Be(12345);
        deserialized.Conversions.Should().HaveCount(1);

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);
    }

    #endregion

    #region Mixed Conversions Tests

    [Fact]
    public void MixedConversions_PG()
    {
        var model = CreateMixedConversionsModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        deserialized.Conversions.Should().HaveCount(3);

        deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>();
        deserialized.Conversions[1].Should().BeOfType<AnalogSwitchConversion>();
        deserialized.Conversions[2].Should().BeOfType<MultiValueConversion>();

        var analog = (AnalogSwitchConversion)deserialized.Conversions[1];
        analog.Threshold.Should().Be(50.5);
        analog.InvertLogic.Should().BeTrue();

        var multi = (MultiValueConversion)deserialized.Conversions[2];
        multi.Multipliers.Should().BeEquivalentTo(new[] { 1.0, 2.0, 3.0 });
        multi.Formula.Should().Be("x * 2 + 1");
    }

    [Fact]
    public void MixedConversions_GG()
    {
        var model = CreateMixedConversionsModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        deserialized.Conversions.Should().HaveCount(3);
        deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>();
        deserialized.Conversions[1].Should().BeOfType<AnalogSwitchConversion>();
        deserialized.Conversions[2].Should().BeOfType<MultiValueConversion>();
    }

    [Fact]
    public void MixedConversions_GP()
    {
        var model = CreateMixedConversionsModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithProtobufNet<ProtoIncludeDerivedFieldsModel>(data);

        deserialized.Conversions.Should().HaveCount(3);
        deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>();
        deserialized.Conversions[1].Should().BeOfType<AnalogSwitchConversion>();
        deserialized.Conversions[2].Should().BeOfType<MultiValueConversion>();
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultValues_PG()
    {
        var model = CreateDefaultValuesModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.Value1OnInput.Should().Be(0.0);
        conversion.Value1OnOutput.Should().Be(0.0);
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);
    }

    [Fact]
    public void DefaultValues_GG()
    {
        var model = CreateDefaultValuesModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeProtoIncludeDerivedFieldsModel(bytes));

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.Value1OnInput.Should().Be(0.0);
        conversion.Value1OnOutput.Should().Be(0.0);
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);
    }

    [Fact]
    public void DefaultValues_GP()
    {
        var model = CreateDefaultValuesModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var deserialized = DeserializeWithProtobufNet<ProtoIncludeDerivedFieldsModel>(data);

        var conversion = deserialized.Conversions[0].Should().BeOfType<LinearInterpolationConversion>().Subject;
        conversion.Value1OnInput.Should().Be(0.0);
        conversion.Value1OnOutput.Should().Be(0.0);
        conversion.Value2OnInput.Should().Be(100.0);
        conversion.Value2OnOutput.Should().Be(255.0);
    }

    #endregion

    #region Binary Compatibility Tests

    [Fact]
    public void ProtoInclude_BinaryCompatibility_GProtobufEqualsProtobufNet()
    {
        var model = CreateLinearInterpolationModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        _output.WriteLine($"GProtobuf size: {gprotobufData.Length} bytes");
        _output.WriteLine($"protobuf-net size: {protobufNetData.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(gprotobufData)}");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(protobufNetData)}");

        // Sizes should be identical - this was the bug!
        // Before fix: GProtobuf produced 8 bytes more (one extra double in size calculation)
        gprotobufData.Length.Should().Be(protobufNetData.Length,
            "GProtobuf and protobuf-net should produce identical byte sizes for ProtoInclude wrappers");

        // Bytes should be identical
        gprotobufData.Should().Equal(protobufNetData,
            "GProtobuf and protobuf-net should produce identical bytes for ProtoInclude hierarchies");
    }

    [Fact]
    public void ProtoInclude_MixedTypes_BinaryCompatibility()
    {
        var model = CreateMixedConversionsModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeProtoIncludeDerivedFieldsModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        _output.WriteLine($"GProtobuf size: {gprotobufData.Length} bytes");
        _output.WriteLine($"protobuf-net size: {protobufNetData.Length} bytes");

        gprotobufData.Length.Should().Be(protobufNetData.Length);
        gprotobufData.Should().Equal(protobufNetData);
    }

    #endregion

    #region Combined Complex Model Tests

    #endregion
}
