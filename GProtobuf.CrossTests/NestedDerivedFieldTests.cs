using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.CrossTests.TestModel.Serialization;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// Tests for nested derived type field deserialization.
///
/// Bug scenario:
/// - Container has a field of type DerivedActionParams (a derived type)
/// - protobuf-net serializes DerivedActionParams with ProtoInclude wrapper
/// - Wire format: [field tag][total length] [ProtoInclude tag=100][wrapper length][derived fields][base fields]
/// - GProtobuf Populate method was calling ReadXxxContent (expects direct fields)
/// - Fix: Should call ReadXxx (detects and handles ProtoInclude wrapper)
/// </summary>
public sealed class NestedDerivedFieldTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _outputHelper;

    public NestedDerivedFieldTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void TriggerContainer_WithDerivedActionParams_PG()
    {
        // Arrange
        var model = new TriggerContainer
        {
            TriggerName = "TestTrigger",
            ActionParameters = new DerivedActionParams
            {
                BaseName = "Base",
                BaseValue = 10,
                DerivedName = "Derived",
                DerivedValue = 20
            },
            Priority = 5
        };

        // Act - Serialize with protobuf-net
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"Serialized data length: {data.Length} bytes");
        _outputHelper.WriteLine($"Data: {BitConverter.ToString(data)}");

        // Act - Deserialize with GProtobuf
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeTriggerContainer(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.TriggerName.Should().Be("TestTrigger");
        deserialized.Priority.Should().Be(5);
        deserialized.ActionParameters.Should().NotBeNull();
        deserialized.ActionParameters.BaseName.Should().Be("Base");
        deserialized.ActionParameters.BaseValue.Should().Be(10);
        deserialized.ActionParameters.DerivedName.Should().Be("Derived");
        deserialized.ActionParameters.DerivedValue.Should().Be(20);
    }

    [Fact]
    public void TriggerContainer_OnlyBaseName_PG()
    {
        // Arrange - Only base fields set
        var model = new TriggerContainer
        {
            TriggerName = "TestTrigger",
            ActionParameters = new DerivedActionParams
            {
                BaseName = "BaseOnly",
                BaseValue = 42
            },
            Priority = 1
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeTriggerContainer(bytes));

        // Assert
        deserialized.ActionParameters.BaseName.Should().Be("BaseOnly");
        deserialized.ActionParameters.BaseValue.Should().Be(42);
        deserialized.ActionParameters.DerivedName.Should().BeNull();
        deserialized.ActionParameters.DerivedValue.Should().Be(0);
    }

    [Fact]
    public void TriggerContainer_OnlyDerivedFields_PG()
    {
        // Arrange - Only derived fields set
        var model = new TriggerContainer
        {
            TriggerName = "TestTrigger",
            ActionParameters = new DerivedActionParams
            {
                DerivedName = "DerivedOnly",
                DerivedValue = 99
            },
            Priority = 2
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeTriggerContainer(bytes));

        // Assert
        deserialized.ActionParameters.DerivedName.Should().Be("DerivedOnly");
        deserialized.ActionParameters.DerivedValue.Should().Be(99);
        deserialized.ActionParameters.BaseName.Should().BeNull();
        deserialized.ActionParameters.BaseValue.Should().Be(0);
    }

    [Fact]
    public void MultiDerivedContainer_TwoFields_PG()
    {
        // Arrange
        var model = new MultiDerivedContainer
        {
            Name = "Multi",
            Primary = new DerivedActionParams
            {
                BaseName = "Primary Base",
                DerivedName = "Primary Derived"
            },
            Secondary = new DerivedActionParams
            {
                BaseName = "Secondary Base",
                DerivedName = "Secondary Derived"
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeMultiDerivedContainer(bytes));

        // Assert
        deserialized.Primary.BaseName.Should().Be("Primary Base");
        deserialized.Primary.DerivedName.Should().Be("Primary Derived");
        deserialized.Secondary.BaseName.Should().Be("Secondary Base");
        deserialized.Secondary.DerivedName.Should().Be("Secondary Derived");
    }

    [Fact]
    public void DeepNestingContainer_DerivedInsideDerived_PG()
    {
        // Arrange - Derived type inside another derived type
        var model = new DeepNestingContainer
        {
            Trigger = new DerivedTriggerContainer
            {
                Name = "Outer",
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "Inner Base",
                    DerivedName = "Inner Derived",
                    BaseValue = 100,
                    DerivedValue = 200
                }
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"Deep nesting data length: {data.Length} bytes");
        _outputHelper.WriteLine($"Data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeDeepNestingContainer(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Trigger.Should().NotBeNull();
        deserialized.Trigger.Name.Should().Be("Outer");
        deserialized.Trigger.ActionParameters.Should().NotBeNull();
        deserialized.Trigger.ActionParameters.BaseName.Should().Be("Inner Base");
        deserialized.Trigger.ActionParameters.DerivedName.Should().Be("Inner Derived");
        deserialized.Trigger.ActionParameters.BaseValue.Should().Be(100);
        deserialized.Trigger.ActionParameters.DerivedValue.Should().Be(200);
    }

    [Fact]
    public void TriggerContainer_RoundTrip_GG()
    {
        // Arrange
        var model = new TriggerContainer
        {
            TriggerName = "RoundTrip",
            ActionParameters = new DerivedActionParams
            {
                BaseName = "Base",
                BaseValue = 10,
                DerivedName = "Derived",
                DerivedValue = 20
            },
            Priority = 5
        };

        // Act - Serialize with GProtobuf
        var data = SerializeWithGProtobuf(model,
            (stream, obj) => Serializers.SerializeTriggerContainer(stream, obj));

        // Deserialize with GProtobuf
        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeTriggerContainer(bytes));

        // Assert
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void TriggerContainer_GP()
    {
        // Arrange
        var model = new TriggerContainer
        {
            TriggerName = "GPTest",
            ActionParameters = new DerivedActionParams
            {
                BaseName = "Base",
                DerivedName = "Derived"
            },
            Priority = 3
        };

        // Act - Serialize with GProtobuf
        var data = SerializeWithGProtobuf(model,
            (stream, obj) => Serializers.SerializeTriggerContainer(stream, obj));

        // Deserialize with protobuf-net
        var deserialized = DeserializeWithProtobufNet<TriggerContainer>(data);

        // Assert
        deserialized.Should().BeEquivalentTo(model);
    }
}
