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
/// Tests for readonly struct serialization/deserialization.
/// </summary>
public sealed class ReadonlyStructKeyTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public ReadonlyStructKeyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Data Factories

    private static ReadonlyStructKeyModel CreateSimpleModel()
    {
        return new ReadonlyStructKeyModel
        {
            Descriptions = new Dictionary<ValueTypeKey, ValueTypeDescription>
            {
                { new ValueTypeKey(1), new ValueTypeDescription { Name = "Type1", MinPeriod = 10, IsEnabled = true } },
                { new ValueTypeKey(2), new ValueTypeDescription { Name = "Type2", MinPeriod = 20, IsEnabled = false } },
                { new ValueTypeKey(3), new ValueTypeDescription { Name = "Type3", MinPeriod = 30, IsEnabled = true } }
            },
            ValuesByName = new Dictionary<string, ValueTypeKey>
            {
                { "First", new ValueTypeKey(100) },
                { "Second", new ValueTypeKey(200) }
            },
            ValueTypeKeys = new List<ValueTypeKey>
            {
                new ValueTypeKey(10),
                new ValueTypeKey(20),
                new ValueTypeKey(30)
            }
        };
    }

    private static ReadonlyStructKeyModel CreateComplexModel()
    {
        return new ReadonlyStructKeyModel
        {
            Descriptions = new Dictionary<ValueTypeKey, ValueTypeDescription>
            {
                { new ValueTypeKey(uint.MinValue), new ValueTypeDescription { Name = "Min", MinPeriod = 0, IsEnabled = false } },
                { new ValueTypeKey(1), new ValueTypeDescription { Name = "One", MinPeriod = 1, IsEnabled = true } },
                { new ValueTypeKey(uint.MaxValue), new ValueTypeDescription { Name = "Max", MinPeriod = int.MaxValue, IsEnabled = true } }
            },
            ValuesByName = new Dictionary<string, ValueTypeKey>
            {
                { "", new ValueTypeKey(0) },
                { "Normal", new ValueTypeKey(42) },
                { "Large", new ValueTypeKey(uint.MaxValue) }
            },
            ValueTypeKeys = new List<ValueTypeKey>
            {
                new ValueTypeKey(0),
                new ValueTypeKey(1),
                new ValueTypeKey(uint.MaxValue)
            },
            ValueTypeKeyHashSet = new HashSet<ValueTypeKey>
            {
                new ValueTypeKey(100),
                new ValueTypeKey(200),
                new ValueTypeKey(300)
            },
            CommandsByValueType = new Dictionary<ValueTypeKey, List<int>>
            {
                { new ValueTypeKey(1), new List<int> { 1, 2, 3 } },
                { new ValueTypeKey(2), new List<int> { 4, 5 } }
            }
        };
    }

    #endregion

    #region Dictionary with Readonly Struct Key Tests

    [Fact]
    public void ReadonlyStructKey_Dictionary_PG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        // Verify Dictionary with readonly struct key
        deserialized.Descriptions.Should().NotBeNull();
        deserialized.Descriptions.Should().HaveCount(3);

        deserialized.Descriptions[new ValueTypeKey(1)].Name.Should().Be("Type1");
        deserialized.Descriptions[new ValueTypeKey(1)].MinPeriod.Should().Be(10);
        deserialized.Descriptions[new ValueTypeKey(1)].IsEnabled.Should().BeTrue();

        deserialized.Descriptions[new ValueTypeKey(2)].Name.Should().Be("Type2");
        deserialized.Descriptions[new ValueTypeKey(2)].MinPeriod.Should().Be(20);
        deserialized.Descriptions[new ValueTypeKey(2)].IsEnabled.Should().BeFalse();

        deserialized.Descriptions[new ValueTypeKey(3)].Name.Should().Be("Type3");
        deserialized.Descriptions[new ValueTypeKey(3)].MinPeriod.Should().Be(30);
        deserialized.Descriptions[new ValueTypeKey(3)].IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ReadonlyStructKey_Dictionary_GG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        deserialized.Descriptions.Should().HaveCount(3);
        deserialized.Descriptions[new ValueTypeKey(1)].Name.Should().Be("Type1");
        deserialized.Descriptions[new ValueTypeKey(2)].Name.Should().Be("Type2");
        deserialized.Descriptions[new ValueTypeKey(3)].Name.Should().Be("Type3");
    }

    [Fact]
    public void ReadonlyStructKey_Dictionary_GP()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyModel>(data);

        deserialized.Descriptions.Should().HaveCount(3);
        deserialized.Descriptions[new ValueTypeKey(1)].Name.Should().Be("Type1");
        deserialized.Descriptions[new ValueTypeKey(2)].Name.Should().Be("Type2");
        deserialized.Descriptions[new ValueTypeKey(3)].Name.Should().Be("Type3");
    }

    #endregion

    #region Dictionary with Readonly Struct Value Tests

    [Fact]
    public void ReadonlyStructValue_Dictionary_PG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        deserialized.ValuesByName.Should().NotBeNull();
        deserialized.ValuesByName.Should().HaveCount(2);
        deserialized.ValuesByName["First"].Value.Should().Be(100);
        deserialized.ValuesByName["Second"].Value.Should().Be(200);
    }

    [Fact]
    public void ReadonlyStructValue_Dictionary_GG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        deserialized.ValuesByName.Should().HaveCount(2);
        deserialized.ValuesByName["First"].Value.Should().Be(100);
        deserialized.ValuesByName["Second"].Value.Should().Be(200);
    }

    [Fact]
    public void ReadonlyStructValue_Dictionary_GP()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyModel>(data);

        deserialized.ValuesByName.Should().HaveCount(2);
        deserialized.ValuesByName["First"].Value.Should().Be(100);
        deserialized.ValuesByName["Second"].Value.Should().Be(200);
    }

    #endregion

    #region List of Readonly Struct Tests

    [Fact]
    public void ReadonlyStruct_List_PG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        deserialized.ValueTypeKeys.Should().NotBeNull();
        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeys[0].Value.Should().Be(10);
        deserialized.ValueTypeKeys[1].Value.Should().Be(20);
        deserialized.ValueTypeKeys[2].Value.Should().Be(30);
    }

    [Fact]
    public void ReadonlyStruct_List_GG()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeys[0].Value.Should().Be(10);
        deserialized.ValueTypeKeys[1].Value.Should().Be(20);
        deserialized.ValueTypeKeys[2].Value.Should().Be(30);
    }

    [Fact]
    public void ReadonlyStruct_List_GP()
    {
        var model = CreateSimpleModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyModel>(data);

        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeys[0].Value.Should().Be(10);
        deserialized.ValueTypeKeys[1].Value.Should().Be(20);
        deserialized.ValueTypeKeys[2].Value.Should().Be(30);
    }

    #endregion

    #region Complex Model Tests (All Field Types)

    [Fact]
    public void ReadonlyStruct_ComplexModel_PG()
    {
        var model = CreateComplexModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        // Dictionary with readonly struct key
        deserialized.Descriptions.Should().HaveCount(3);
        deserialized.Descriptions[new ValueTypeKey(uint.MinValue)].Name.Should().Be("Min");
        deserialized.Descriptions[new ValueTypeKey(1)].Name.Should().Be("One");
        deserialized.Descriptions[new ValueTypeKey(uint.MaxValue)].Name.Should().Be("Max");

        // Dictionary with readonly struct value
        deserialized.ValuesByName.Should().HaveCount(3);
        deserialized.ValuesByName[""].Value.Should().Be(0);
        deserialized.ValuesByName["Normal"].Value.Should().Be(42);
        deserialized.ValuesByName["Large"].Value.Should().Be(uint.MaxValue);

        // List of readonly struct
        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeys.Select(k => k.Value).Should().BeEquivalentTo(new uint[] { 0, 1, uint.MaxValue });

        // HashSet of readonly struct
        deserialized.ValueTypeKeyHashSet.Should().HaveCount(3);
        deserialized.ValueTypeKeyHashSet.Should().Contain(new ValueTypeKey(100));
        deserialized.ValueTypeKeyHashSet.Should().Contain(new ValueTypeKey(200));
        deserialized.ValueTypeKeyHashSet.Should().Contain(new ValueTypeKey(300));

        // Nested dictionary with readonly struct key
        deserialized.CommandsByValueType.Should().HaveCount(2);
        deserialized.CommandsByValueType[new ValueTypeKey(1)].Should().BeEquivalentTo(new[] { 1, 2, 3 });
        deserialized.CommandsByValueType[new ValueTypeKey(2)].Should().BeEquivalentTo(new[] { 4, 5 });
    }

    [Fact]
    public void ReadonlyStruct_ComplexModel_GG()
    {
        var model = CreateComplexModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        // Verify all fields preserved
        deserialized.Descriptions.Should().HaveCount(3);
        deserialized.ValuesByName.Should().HaveCount(3);
        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeyHashSet.Should().HaveCount(3);
        deserialized.CommandsByValueType.Should().HaveCount(2);
    }

    [Fact]
    public void ReadonlyStruct_ComplexModel_GP()
    {
        var model = CreateComplexModel();

        var data = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var deserialized = DeserializeWithProtobufNet<ReadonlyStructKeyModel>(data);

        // Verify all fields preserved
        deserialized.Descriptions.Should().HaveCount(3);
        deserialized.ValuesByName.Should().HaveCount(3);
        deserialized.ValueTypeKeys.Should().HaveCount(3);
        deserialized.ValueTypeKeyHashSet.Should().HaveCount(3);
        deserialized.CommandsByValueType.Should().HaveCount(2);
    }

    #endregion

    #region Binary Compatibility Tests

    [Fact]
    public void ReadonlyStruct_BinaryCompatibility_GProtobufEqualsProtobufNet()
    {
        var model = CreateSimpleModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var protobufNetData = SerializeWithProtobufNet(model);

        _output.WriteLine($"GProtobuf size: {gprotobufData.Length} bytes");
        _output.WriteLine($"protobuf-net size: {protobufNetData.Length} bytes");
        _output.WriteLine($"GProtobuf hex: {System.BitConverter.ToString(gprotobufData)}");
        _output.WriteLine($"protobuf-net hex: {System.BitConverter.ToString(protobufNetData)}");

        // Sizes should be identical
        gprotobufData.Length.Should().Be(protobufNetData.Length,
            "GProtobuf and protobuf-net should produce identical byte sizes");

        // Bytes should be identical
        gprotobufData.Should().Equal(protobufNetData,
            "GProtobuf and protobuf-net should produce identical bytes for readonly struct keys");
    }

    [Fact]
    public void ReadonlyStruct_Roundtrip_GG_PG_GP()
    {
        var model = CreateComplexModel();

        // GG roundtrip
        var ggData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var ggResult = DeserializeWithGProtobuf(ggData,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        // PG roundtrip
        var pgData = SerializeWithProtobufNet(model);
        var pgResult = DeserializeWithGProtobuf(pgData,
            bytes => TestModelDeserializers.DeserializeReadonlyStructKeyModel(bytes));

        // GP roundtrip
        var gpData = SerializeWithGProtobuf(model, TestModelSerializers.SerializeReadonlyStructKeyModel);
        var gpResult = DeserializeWithProtobufNet<ReadonlyStructKeyModel>(gpData);

        // All results should be equivalent
        ggResult.Descriptions.Should().HaveCount(model.Descriptions.Count);
        pgResult.Descriptions.Should().HaveCount(model.Descriptions.Count);
        gpResult.Descriptions.Should().HaveCount(model.Descriptions.Count);

        foreach (var key in model.Descriptions.Keys)
        {
            ggResult.Descriptions[key].Name.Should().Be(model.Descriptions[key].Name);
            pgResult.Descriptions[key].Name.Should().Be(model.Descriptions[key].Name);
            gpResult.Descriptions[key].Name.Should().Be(model.Descriptions[key].Name);
        }
    }

    #endregion
}
