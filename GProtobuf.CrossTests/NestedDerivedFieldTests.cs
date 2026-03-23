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

    [Fact]
    public void MapWithDerivedValue_PG()
    {
        // Arrange - Dictionary with derived type as VALUE
        var model = new MapWithDerivedValue
        {
            Items = new System.Collections.Generic.Dictionary<int, DerivedActionParams>
            {
                [1] = new DerivedActionParams { BaseName = "Base1", DerivedName = "Derived1", BaseValue = 10, DerivedValue = 100 },
                [2] = new DerivedActionParams { BaseName = "Base2", DerivedName = "Derived2", BaseValue = 20, DerivedValue = 200 }
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"MapWithDerivedValue data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeMapWithDerivedValue(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
        deserialized.Items[1].BaseName.Should().Be("Base1");
        deserialized.Items[1].DerivedName.Should().Be("Derived1");
        deserialized.Items[2].BaseName.Should().Be("Base2");
        deserialized.Items[2].DerivedName.Should().Be("Derived2");
    }

    [Fact]
    public void MapWithDerivedKey_PG()
    {
        // Arrange - Dictionary with ProtoInclude class as KEY
        var model = new MapWithDerivedKey
        {
            Items = new System.Collections.Generic.Dictionary<AggregationKey, int>
            {
                [new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = 100,
                [new AggregationKey { Function = 4, PeriodType = 5, Period = 6 }] = 200
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"MapWithDerivedKey data length: {data.Length}");
        _outputHelper.WriteLine($"MapWithDerivedKey data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeMapWithDerivedKey(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
    }

    [Fact]
    public void MapWithDerivedKey_ExtendedKey_PG()
    {
        // Arrange - Dictionary with EXTENDED (derived) class as KEY
        var model = new MapWithDerivedKey
        {
            Items = new System.Collections.Generic.Dictionary<AggregationKey, int>
            {
                [new AggregationKeyExtended { Function = 1, PeriodType = 2, Period = 3, Usage = 10, EnabledOn = 12345 }] = 100,
                [new AggregationKey { Function = 4, PeriodType = 5, Period = 6 }] = 200
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"MapWithDerivedKey_Extended data length: {data.Length}");
        _outputHelper.WriteLine($"MapWithDerivedKey_Extended data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeMapWithDerivedKey(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
    }

    [Fact]
    public void NestedMapContainer_PG()
    {
        // Arrange - Nested dictionary (map inside map)
        var model = new NestedMapContainer
        {
            NestedMap = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.Dictionary<int, string>>
            {
                [1] = new System.Collections.Generic.Dictionary<int, string>
                {
                    [10] = "Value10",
                    [20] = "Value20"
                },
                [2] = new System.Collections.Generic.Dictionary<int, string>
                {
                    [30] = "Value30"
                }
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"NestedMapContainer data length: {data.Length}");
        _outputHelper.WriteLine($"NestedMapContainer data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeNestedMapContainer(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.NestedMap.Should().HaveCount(2);
        deserialized.NestedMap[1].Should().HaveCount(2);
        deserialized.NestedMap[1][10].Should().Be("Value10");
        deserialized.NestedMap[2][30].Should().Be("Value30");
    }

    [Fact]
    public void AggregatedConnection_FullStructure_PG()
    {
        // Arrange - Full IftttConnection-like structure
        // Dictionary<uint, Dictionary<AggregationKey, HashSet<int>>>
        var model = new AggregatedConnection
        {
            ConnectionType = 1,
            Connections = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>>
            {
                [100] = new System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>
                {
                    [new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = new System.Collections.Generic.HashSet<int> { 10, 20, 30 }
                },
                [200] = new System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>
                {
                    [new AggregationKey { Function = 4, PeriodType = 5, Period = 6 }] = new System.Collections.Generic.HashSet<int> { 40, 50 }
                }
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"AggregatedConnection data length: {data.Length}");
        _outputHelper.WriteLine($"AggregatedConnection data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeAggregatedConnection(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.ConnectionType.Should().Be(1);
        deserialized.Connections.Should().HaveCount(2);
        deserialized.Connections[100].Should().HaveCount(1);
        deserialized.Connections[200].Should().HaveCount(1);
    }

    [Fact]
    public void AgregateConnection_Full_PG()
    {
        // Arrange
        var model = new AgregateConnectionTestModel
        {
            Name = "TestConnection",
            AggregatedConnections = new AggregatedConnection
            {
                ConnectionType = 1,
                Connections = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>>
                {
                    [100] = new System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>
                    {
                        [new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = new System.Collections.Generic.HashSet<int> { 10, 20 }
                    }
                }
            }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"AgregateConnectionTestModel data length: {data.Length}");
        _outputHelper.WriteLine($"AgregateConnectionTestModel data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeAgregateConnectionTestModel(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Name.Should().Be("TestConnection");
        deserialized.AggregatedConnections.Should().NotBeNull();
        deserialized.AggregatedConnections.Connections.Should().HaveCount(1);
    }

    // ============================================================
    // ConcurrentDictionary tests
    // ============================================================

    [Fact]
    public void ConcurrentMapSimple_PG()
    {
        // Arrange
        var model = new ConcurrentMapSimple
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<int, string>()
        };
        model.Items[1] = "Value1";
        model.Items[2] = "Value2";

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapSimple data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapSimple(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
        deserialized.Items[1].Should().Be("Value1");
        deserialized.Items[2].Should().Be("Value2");
    }

    [Fact]
    public void ConcurrentMapWithDerivedValue_PG()
    {
        // Arrange - ConcurrentDictionary with derived type as VALUE
        var model = new ConcurrentMapWithDerivedValue
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<int, DerivedActionParams>()
        };
        model.Items[1] = new DerivedActionParams { BaseName = "Base1", DerivedName = "Derived1", BaseValue = 10, DerivedValue = 100 };
        model.Items[2] = new DerivedActionParams { BaseName = "Base2", DerivedName = "Derived2", BaseValue = 20, DerivedValue = 200 };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithDerivedValue data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithDerivedValue(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
        deserialized.Items[1].BaseName.Should().Be("Base1");
        deserialized.Items[1].DerivedName.Should().Be("Derived1");
        deserialized.Items[2].BaseName.Should().Be("Base2");
        deserialized.Items[2].DerivedName.Should().Be("Derived2");
    }

    [Fact]
    public void ConcurrentMapWithDerivedKey_PG()
    {
        // Arrange - ConcurrentDictionary with ProtoInclude class as KEY
        var model = new ConcurrentMapWithDerivedKey
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, int>()
        };
        model.Items[new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = 100;
        model.Items[new AggregationKey { Function = 4, PeriodType = 5, Period = 6 }] = 200;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithDerivedKey data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithDerivedKey(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
    }

    [Fact]
    public void NestedConcurrentMap_PG()
    {
        // Arrange - Nested ConcurrentDictionary
        var model = new NestedConcurrentMap
        {
            NestedMap = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, string>>()
        };
        var inner1 = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        inner1[10] = "Value10";
        inner1[20] = "Value20";
        model.NestedMap[1] = inner1;

        var inner2 = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        inner2[30] = "Value30";
        model.NestedMap[2] = inner2;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"NestedConcurrentMap data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeNestedConcurrentMap(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.NestedMap.Should().HaveCount(2);
        deserialized.NestedMap[1].Should().HaveCount(2);
        deserialized.NestedMap[1][10].Should().Be("Value10");
        deserialized.NestedMap[2][30].Should().Be("Value30");
    }

    [Fact]
    public void ConcurrentMapWithDictionaryValue_PG()
    {
        // Arrange - Mixed: ConcurrentDictionary with Dictionary inside
        var model = new ConcurrentMapWithDictionaryValue
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Generic.Dictionary<int, string>>()
        };
        model.Items[1] = new System.Collections.Generic.Dictionary<int, string> { [10] = "A", [20] = "B" };
        model.Items[2] = new System.Collections.Generic.Dictionary<int, string> { [30] = "C" };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithDictionaryValue data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithDictionaryValue(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
        deserialized.Items[1][10].Should().Be("A");
        deserialized.Items[2][30].Should().Be("C");
    }

    [Fact]
    public void DictionaryWithConcurrentMapValue_PG()
    {
        // Arrange - Mixed: Dictionary with ConcurrentDictionary inside
        var model = new DictionaryWithConcurrentMapValue
        {
            Items = new System.Collections.Generic.Dictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, string>>()
        };
        var inner1 = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        inner1[10] = "X";
        inner1[20] = "Y";
        model.Items[1] = inner1;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"DictionaryWithConcurrentMapValue data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeDictionaryWithConcurrentMapValue(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(1);
        deserialized.Items[1][10].Should().Be("X");
        deserialized.Items[1][20].Should().Be("Y");
    }

    [Fact]
    public void ConcurrentAggregatedConnection_Full_PG()
    {
        // Arrange - Full complex: ConcurrentDictionary<uint, ConcurrentDictionary<AggregationKey, HashSet<int>>>
        var model = new ConcurrentAggregatedConnection
        {
            ConnectionType = 1,
            Connections = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Generic.HashSet<int>>>()
        };

        var inner1 = new System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Generic.HashSet<int>>();
        inner1[new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = new System.Collections.Generic.HashSet<int> { 10, 20, 30 };
        model.Connections[100] = inner1;

        var inner2 = new System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Generic.HashSet<int>>();
        inner2[new AggregationKey { Function = 4, PeriodType = 5, Period = 6 }] = new System.Collections.Generic.HashSet<int> { 40, 50 };
        model.Connections[200] = inner2;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentAggregatedConnection data length: {data.Length}");
        _outputHelper.WriteLine($"ConcurrentAggregatedConnection data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentAggregatedConnection(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.ConnectionType.Should().Be(1);
        deserialized.Connections.Should().HaveCount(2);
    }

    [Fact]
    public void ConcurrentMapWithListValue_PG()
    {
        // Arrange - ConcurrentDictionary with List of derived types
        var model = new ConcurrentMapWithListValue
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.List<DerivedActionParams>>()
        };
        model.Items[1] = new System.Collections.Generic.List<DerivedActionParams>
        {
            new DerivedActionParams { BaseName = "B1", DerivedName = "D1" },
            new DerivedActionParams { BaseName = "B2", DerivedName = "D2" }
        };

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithListValue data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithListValue(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(1);
        deserialized.Items[1].Should().HaveCount(2);
        deserialized.Items[1][0].BaseName.Should().Be("B1");
        deserialized.Items[1][0].DerivedName.Should().Be("D1");
    }

    // ============================================================
    // Deep nested ConcurrentDictionary tests (3+ levels)
    // ============================================================

    [Fact]
    public void DeepNestedConcurrentMap_PG()
    {
        // Arrange - 3-level: ConcurrentDictionary<uint, ConcurrentDictionary<int, ConcurrentDictionary<ConnectionStatus, HashSet<int>>>>
        var model = new DeepNestedConcurrentMap
        {
            Data = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.HashSet<int>>>>()
        };

        var level2 = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.HashSet<int>>>();
        var level3 = new System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.HashSet<int>>();
        level3[ConnectionStatus.Connected] = new System.Collections.Generic.HashSet<int> { 1, 2, 3 };
        level3[ConnectionStatus.Disconnected] = new System.Collections.Generic.HashSet<int> { 4, 5 };
        level2[10] = level3;
        model.Data[100] = level2;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"DeepNestedConcurrentMap data length: {data.Length}");
        _outputHelper.WriteLine($"DeepNestedConcurrentMap data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeDeepNestedConcurrentMap(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Data.Should().HaveCount(1);
        deserialized.Data[100].Should().HaveCount(1);
        deserialized.Data[100][10].Should().HaveCount(2);
        deserialized.Data[100][10][ConnectionStatus.Connected].Should().BeEquivalentTo(new[] { 1, 2, 3 });
        deserialized.Data[100][10][ConnectionStatus.Disconnected].Should().BeEquivalentTo(new[] { 4, 5 });
    }

    [Fact]
    public void ConcurrentMapWithEnumKey_PG()
    {
        // Arrange - ConcurrentDictionary<ConnectionStatus, ConcurrentDictionary<int, HashSet<int>>>
        var model = new ConcurrentMapWithEnumKey
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>>()
        };

        var inner = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>();
        inner[1] = new System.Collections.Generic.HashSet<int> { 10, 20, 30 };
        inner[2] = new System.Collections.Generic.HashSet<int> { 40, 50 };
        model.Items[ConnectionStatus.Connected] = inner;

        var inner2 = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>();
        inner2[3] = new System.Collections.Generic.HashSet<int> { 60 };
        model.Items[ConnectionStatus.Pending] = inner2;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithEnumKey data length: {data.Length}");
        _outputHelper.WriteLine($"ConcurrentMapWithEnumKey data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithEnumKey(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(2);
        deserialized.Items[ConnectionStatus.Connected][1].Should().BeEquivalentTo(new[] { 10, 20, 30 });
        deserialized.Items[ConnectionStatus.Pending][3].Should().BeEquivalentTo(new[] { 60 });
    }

    [Fact]
    public void ConcurrentMapWithEnumArray_PG()
    {
        // Arrange - ConcurrentDictionary<uint, ConcurrentDictionary<ConnectionStatus, List<ConnectionStatus>>>
        var model = new ConcurrentMapWithEnumArray
        {
            Items = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.List<ConnectionStatus>>>()
        };

        var inner = new System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.List<ConnectionStatus>>();
        inner[ConnectionStatus.Connected] = new System.Collections.Generic.List<ConnectionStatus> { ConnectionStatus.Connected, ConnectionStatus.Pending };
        inner[ConnectionStatus.Disconnected] = new System.Collections.Generic.List<ConnectionStatus> { ConnectionStatus.Unknown };
        model.Items[1] = inner;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"ConcurrentMapWithEnumArray data length: {data.Length}");
        _outputHelper.WriteLine($"ConcurrentMapWithEnumArray data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeConcurrentMapWithEnumArray(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Items.Should().HaveCount(1);
        deserialized.Items[1][ConnectionStatus.Connected].Should().BeEquivalentTo(new[] { ConnectionStatus.Connected, ConnectionStatus.Pending });
        deserialized.Items[1][ConnectionStatus.Disconnected].Should().BeEquivalentTo(new[] { ConnectionStatus.Unknown });
    }

    [Fact]
    public void DeepAggregatedConnection_PG()
    {
        // Arrange - ConcurrentDictionary<uint, ConcurrentDictionary<AggregationKey, ConcurrentDictionary<int, HashSet<int>>>>
        var model = new DeepAggregatedConnection
        {
            ConnectionType = 5,
            Connections = new System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>>>()
        };

        var level2 = new System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>>();
        var level3 = new System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>();
        level3[1] = new System.Collections.Generic.HashSet<int> { 100, 200 };
        level3[2] = new System.Collections.Generic.HashSet<int> { 300 };
        level2[new AggregationKey { Function = 1, PeriodType = 2, Period = 3 }] = level3;
        model.Connections[100] = level2;

        // Act
        var data = SerializeWithProtobufNet(model);
        _outputHelper.WriteLine($"DeepAggregatedConnection data length: {data.Length}");
        _outputHelper.WriteLine($"DeepAggregatedConnection data: {BitConverter.ToString(data)}");

        var deserialized = DeserializeWithGProtobuf(data,
            bytes => Deserializers.DeserializeDeepAggregatedConnection(bytes));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.ConnectionType.Should().Be(5);
        deserialized.Connections.Should().HaveCount(1);
        deserialized.Connections[100].Should().HaveCount(1);
    }
}
