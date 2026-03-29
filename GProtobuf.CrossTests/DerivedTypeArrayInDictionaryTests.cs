using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using TestModelSerializers = GProtobuf.CrossTests.TestModel.Serialization.Serializers;
using TestModelDeserializers = GProtobuf.CrossTests.TestModel.Serialization.Deserializers;

namespace GProtobuf.Tests;

/// <summary>
/// Tests for Dictionary with derived type array as value.
///
/// Bug: When declared element type IS the derived type (e.g., DerivedClass[]),
/// protobuf-net serializes elements WITHOUT ProtoInclude wrapper.
/// But GProtobuf incorrectly checks IsDerivedType() and uses wrapper format.
///
/// Example:
/// - Dictionary&lt;int, ConversionBase[]&gt; - elements need ProtoInclude wrapper (base type declared)
/// - Dictionary&lt;int, LinearInterpolationConversion[]&gt; - elements should NOT use wrapper (derived type declared)
/// </summary>
public sealed class DerivedTypeArrayInDictionaryTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public DerivedTypeArrayInDictionaryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Data Factory

    private static DerivedTypeArrayModel CreateTestModel()
    {
        return new DerivedTypeArrayModel
        {
            Id = 42,
            // This dictionary has DERIVED type array as value
            // protobuf-net should serialize WITHOUT ProtoInclude wrapper
            ConversionsByKey = new Dictionary<int, LinearInterpolationConversion[]>
            {
                [1] = new[]
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
                [2] = new[]
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
            }
        };
    }

    #endregion

    #region Cross-serialization Tests

    [Fact]
    public void DerivedTypeArray_PG_ShouldDeserializeAllFields()
    {
        // Serialize with protobuf-net, deserialize with GProtobuf
        var model = CreateTestModel();

        var data = SerializeWithProtobufNet(model);
        _output.WriteLine($"protobuf-net serialized size: {data.Length} bytes");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeDerivedTypeArrayModel(bytes));

        deserialized.Id.Should().Be(42);
        deserialized.ConversionsByKey.Should().HaveCount(2);

        // Verify key 1 has 2 conversions with all fields
        deserialized.ConversionsByKey.Should().ContainKey(1);
        var conversions1 = deserialized.ConversionsByKey[1];
        conversions1.Should().HaveCount(2);

        // First conversion
        conversions1[0].SourceTypes.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        conversions1[0].TargetType.Should().Be(10);
        conversions1[0].Value1OnInput.Should().Be(0.0);
        conversions1[0].Value1OnOutput.Should().Be(0.0);
        conversions1[0].Value2OnInput.Should().Be(100.0);
        conversions1[0].Value2OnOutput.Should().Be(255.0);

        // Second conversion
        conversions1[1].SourceTypes.Should().BeEquivalentTo(new[] { 4, 5 });
        conversions1[1].TargetType.Should().Be(20);
        conversions1[1].Value1OnInput.Should().Be(10.5);
        conversions1[1].Value1OnOutput.Should().Be(20.5);
        conversions1[1].Value2OnInput.Should().Be(50.5);
        conversions1[1].Value2OnOutput.Should().Be(100.5);

        // Verify key 2 has 1 conversion
        deserialized.ConversionsByKey.Should().ContainKey(2);
        var conversions2 = deserialized.ConversionsByKey[2];
        conversions2.Should().HaveCount(1);
        conversions2[0].Value2OnOutput.Should().Be(4.0);
    }

    [Fact]
    public void DerivedTypeArray_GP_ShouldBeReadableByProtobufNet()
    {
        // Serialize with GProtobuf, deserialize with protobuf-net
        var model = CreateTestModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeDerivedTypeArrayModel);
        _output.WriteLine($"GProtobuf serialized size: {data.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(data)}");

        var deserialized = DeserializeWithProtobufNet<DerivedTypeArrayModel>(data);

        deserialized.Id.Should().Be(42);
        deserialized.ConversionsByKey.Should().HaveCount(2);
        deserialized.ConversionsByKey[1].Should().HaveCount(2);
        deserialized.ConversionsByKey[1][0].Value2OnOutput.Should().Be(255.0);
    }

    [Fact]
    public void DerivedTypeArray_GG_ShouldRoundtrip()
    {
        // Serialize and deserialize with GProtobuf
        var model = CreateTestModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeDerivedTypeArrayModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeDerivedTypeArrayModel(bytes));

        deserialized.Id.Should().Be(42);
        deserialized.ConversionsByKey.Should().HaveCount(2);
        deserialized.ConversionsByKey[1][0].Value2OnOutput.Should().Be(255.0);
    }

    #endregion

    #region Binary Compatibility Tests

    [Fact]
    public void DerivedTypeArray_BinaryCompatibility_GProtobufEqualsProtobufNet()
    {
        var model = CreateTestModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeDerivedTypeArrayModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        _output.WriteLine($"GProtobuf size: {gprotobufData.Length} bytes");
        _output.WriteLine($"protobuf-net size: {protobufNetData.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(gprotobufData)}");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(protobufNetData)}");

        // This is the key test - sizes should match
        // If GProtobuf uses ProtoInclude wrapper incorrectly, sizes will differ
        gprotobufData.Length.Should().Be(protobufNetData.Length,
            "GProtobuf and protobuf-net should produce identical byte sizes. " +
            "If different, GProtobuf may be incorrectly using ProtoInclude wrapper for declared derived types.");

        gprotobufData.Should().Equal(protobufNetData,
            "GProtobuf and protobuf-net should produce identical bytes for Dictionary<int, DerivedType[]>");
    }

    #endregion
}
