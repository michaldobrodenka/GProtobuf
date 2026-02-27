using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using CrossTestModel = GProtobuf.CrossTests.TestModel;

namespace GProtobuf.Tests
{
    /// <summary>
    /// Comprehensive tests for StreamReader with inheritance and nesting scenarios.
    /// Tests ProtoInclude hierarchies, nested derived types, and complex type combinations.
    /// Uses StreamReaders directly (not through Deserializers) to properly test the StreamReader code path.
    /// </summary>
    public class InheritanceNestingStreamTests : BaseSerializationTest
    {
        #region Simple Inheritance Tests

        [Fact]
        public void Inheritance_BaseType_GG_Stream()
        {
            var original = new ModelBase { Id = 42 };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Id.Should().Be(42);
        }

        [Fact]
        public void Inheritance_DerivedType1_GG_Stream()
        {
            var original = new ModelInh1
            {
                Id = 1,
                Description = "Test Description",
                Guid = Guid.NewGuid()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<ModelInh1>();
            var derived = (ModelInh1)deserialized;
            derived.Id.Should().Be(1);
            derived.Description.Should().Be("Test Description");
            derived.Guid.Should().Be(original.Guid);
        }

        [Fact]
        public void Inheritance_DerivedType2_GG_Stream()
        {
            var original = new ModelInh2
            {
                Id = 2,
                Description1 = "Desc1",
                Guid1 = Guid.NewGuid()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<ModelInh2>();
            var derived = (ModelInh2)deserialized;
            derived.Id.Should().Be(2);
            derived.Description1.Should().Be("Desc1");
            derived.Guid1.Should().Be(original.Guid1);
        }

        [Fact]
        public void Inheritance_DerivedType3_GG_Stream()
        {
            var original = new ModelInh3
            {
                Id = 3,
                Description2 = "Desc2",
                Guid2 = Guid.NewGuid()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<ModelInh3>();
            var derived = (ModelInh3)deserialized;
            derived.Id.Should().Be(3);
            derived.Description2.Should().Be("Desc2");
            derived.Guid2.Should().Be(original.Guid2);
        }

        #endregion

        #region Deep Inheritance Tests (3 levels)

        [Fact]
        public void Inheritance_ThreeLevels_Model15_GG_Stream()
        {
            var original = new Model15
            {
                Id = 15,
                Description = "Level 2 Desc",
                Guid = Guid.NewGuid(),
                Description15 = "Level 3 Desc",
                Guid15 = Guid.NewGuid()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<Model15>();
            var derived = (Model15)deserialized;
            derived.Id.Should().Be(15);
            derived.Description.Should().Be("Level 2 Desc");
            derived.Guid.Should().Be(original.Guid);
            derived.Description15.Should().Be("Level 3 Desc");
            derived.Guid15.Should().Be(original.Guid15);
        }

        #endregion

        #region Nested Derived Type Tests

        [Fact]
        public void NestedDerived_TriggerContainer_GG_Stream()
        {
            var original = new TriggerContainer
            {
                TriggerName = "TestTrigger",
                Priority = 5,
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "Base",
                    BaseValue = 100,
                    DerivedName = "Derived",
                    DerivedValue = 200
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeTriggerContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(stream));

            deserialized.TriggerName.Should().Be("TestTrigger");
            deserialized.Priority.Should().Be(5);
            deserialized.ActionParameters.Should().NotBeNull();
            deserialized.ActionParameters.BaseName.Should().Be("Base");
            deserialized.ActionParameters.BaseValue.Should().Be(100);
            deserialized.ActionParameters.DerivedName.Should().Be("Derived");
            deserialized.ActionParameters.DerivedValue.Should().Be(200);
        }

        [Fact]
        public void NestedDerived_TriggerContainer_PG_Stream()
        {
            var original = new TriggerContainer
            {
                TriggerName = "TestTrigger",
                Priority = 5,
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "Base",
                    BaseValue = 100,
                    DerivedName = "Derived",
                    DerivedValue = 200
                }
            };
            var bytes = SerializeWithProtobufNet(original);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(stream));

            deserialized.TriggerName.Should().Be("TestTrigger");
            deserialized.Priority.Should().Be(5);
            deserialized.ActionParameters.Should().NotBeNull();
            deserialized.ActionParameters.BaseName.Should().Be("Base");
            deserialized.ActionParameters.BaseValue.Should().Be(100);
            deserialized.ActionParameters.DerivedName.Should().Be("Derived");
            deserialized.ActionParameters.DerivedValue.Should().Be(200);
        }

        [Fact]
        public void NestedDerived_MultiDerivedContainer_GG_Stream()
        {
            var original = new MultiDerivedContainer
            {
                Name = "MultiContainer",
                Primary = new DerivedActionParams
                {
                    BaseName = "Primary Base",
                    BaseValue = 10,
                    DerivedName = "Primary Derived",
                    DerivedValue = 20
                },
                Secondary = new DerivedActionParams
                {
                    BaseName = "Secondary Base",
                    BaseValue = 30,
                    DerivedName = "Secondary Derived",
                    DerivedValue = 40
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Should().Be("MultiContainer");
            deserialized.Primary.BaseName.Should().Be("Primary Base");
            deserialized.Primary.DerivedName.Should().Be("Primary Derived");
            deserialized.Secondary.BaseName.Should().Be("Secondary Base");
            deserialized.Secondary.DerivedName.Should().Be("Secondary Derived");
        }

        #endregion

        #region Deep Nesting Tests (Derived inside Derived)

        [Fact]
        public void DeepNesting_DerivedTriggerContainer_GG_Stream()
        {
            var original = new DerivedTriggerContainer
            {
                Name = "DerivedTrigger",
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "ActionBase",
                    BaseValue = 50,
                    DerivedName = "ActionDerived",
                    DerivedValue = 60
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeBaseTriggerContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeBaseTriggerContainer(stream));

            deserialized.Should().BeOfType<DerivedTriggerContainer>();
            var derived = (DerivedTriggerContainer)deserialized;
            derived.Name.Should().Be("DerivedTrigger");
            derived.ActionParameters.Should().NotBeNull();
            derived.ActionParameters.BaseName.Should().Be("ActionBase");
            derived.ActionParameters.DerivedValue.Should().Be(60);
        }

        [Fact]
        public void DeepNesting_DeepNestingContainer_GG_Stream()
        {
            var original = new DeepNestingContainer
            {
                Trigger = new DerivedTriggerContainer
                {
                    Name = "DeepTrigger",
                    ActionParameters = new DerivedActionParams
                    {
                        BaseName = "DeepBase",
                        BaseValue = 100,
                        DerivedName = "DeepDerived",
                        DerivedValue = 200
                    }
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeDeepNestingContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeDeepNestingContainer(stream));

            deserialized.Trigger.Should().NotBeNull();
            deserialized.Trigger.Name.Should().Be("DeepTrigger");
            deserialized.Trigger.ActionParameters.Should().NotBeNull();
            deserialized.Trigger.ActionParameters.BaseName.Should().Be("DeepBase");
            deserialized.Trigger.ActionParameters.DerivedValue.Should().Be(200);
        }

        #endregion

        #region Dictionary with Derived Types Tests

        [Fact]
        public void DictionaryDerived_MapWithDerivedValue_GG_Stream()
        {
            var original = new MapWithDerivedValue
            {
                Items = new Dictionary<int, DerivedActionParams>
                {
                    [1] = new DerivedActionParams { BaseName = "Item1Base", DerivedName = "Item1Derived", BaseValue = 10, DerivedValue = 100 },
                    [2] = new DerivedActionParams { BaseName = "Item2Base", DerivedName = "Item2Derived", BaseValue = 20, DerivedValue = 200 }
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMapWithDerivedValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithDerivedValue(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items[1].BaseName.Should().Be("Item1Base");
            deserialized.Items[1].DerivedName.Should().Be("Item1Derived");
            deserialized.Items[2].BaseValue.Should().Be(20);
            deserialized.Items[2].DerivedValue.Should().Be(200);
        }

        [Fact]
        public void DictionaryDerived_MapWithDerivedKey_GG_Stream()
        {
            var key1 = new AggregationKey { Function = 1, PeriodType = 2, Period = 3 };
            var key2 = new AggregationKey { Function = 4, PeriodType = 5, Period = 6 };

            var original = new MapWithDerivedKey
            {
                Items = new Dictionary<AggregationKey, int>
                {
                    [key1] = 100,
                    [key2] = 200
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMapWithDerivedKey);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithDerivedKey(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items.Should().ContainValue(100);
            deserialized.Items.Should().ContainValue(200);
        }

        #endregion

        #region ConcurrentDictionary with Derived Types Tests

        [Fact]
        public void ConcurrentDictDerived_WithDerivedValue_GG_Stream()
        {
            var original = new ConcurrentMapWithDerivedValue
            {
                Items = new ConcurrentDictionary<int, DerivedActionParams>()
            };
            original.Items[1] = new DerivedActionParams { BaseName = "Base1", DerivedName = "Derived1", BaseValue = 10, DerivedValue = 100 };
            original.Items[2] = new DerivedActionParams { BaseName = "Base2", DerivedName = "Derived2", BaseValue = 20, DerivedValue = 200 };

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithDerivedValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithDerivedValue(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items[1].BaseName.Should().Be("Base1");
            deserialized.Items[1].DerivedName.Should().Be("Derived1");
            deserialized.Items[2].DerivedValue.Should().Be(200);
        }

        [Fact]
        public void ConcurrentDictDerived_WithDerivedKey_GG_Stream()
        {
            var key1 = new AggregationKey { Function = 1, PeriodType = 2, Period = 3 };
            var key2 = new AggregationKey { Function = 4, PeriodType = 5, Period = 6 };

            var original = new ConcurrentMapWithDerivedKey
            {
                Items = new ConcurrentDictionary<AggregationKey, int>()
            };
            original.Items[key1] = 100;
            original.Items[key2] = 200;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithDerivedKey);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithDerivedKey(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items.Should().ContainValue(100);
            deserialized.Items.Should().ContainValue(200);
        }

        [Fact]
        public void ConcurrentDictDerived_WithListOfDerived_GG_Stream()
        {
            var original = new ConcurrentMapWithListValue
            {
                Items = new ConcurrentDictionary<int, List<DerivedActionParams>>()
            };
            original.Items[1] = new List<DerivedActionParams>
            {
                new DerivedActionParams { BaseName = "Item1A", DerivedName = "Derived1A" },
                new DerivedActionParams { BaseName = "Item1B", DerivedName = "Derived1B" }
            };
            original.Items[2] = new List<DerivedActionParams>
            {
                new DerivedActionParams { BaseName = "Item2A", DerivedName = "Derived2A" }
            };

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithListValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithListValue(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items[1].Should().HaveCount(2);
            deserialized.Items[1][0].BaseName.Should().Be("Item1A");
            deserialized.Items[1][1].DerivedName.Should().Be("Derived1B");
            deserialized.Items[2].Should().HaveCount(1);
        }

        #endregion

        #region Complex Aggregated Connection Tests

        [Fact]
        public void ComplexAggregated_DeepAggregatedConnection_GG_Stream()
        {
            var key1 = new AggregationKey { Function = 1, PeriodType = 1, Period = 30 };

            var original = new DeepAggregatedConnection
            {
                ConnectionType = 77,
                Connections = new ConcurrentDictionary<uint, ConcurrentDictionary<AggregationKey, ConcurrentDictionary<int, HashSet<int>>>>()
            };
            var level2 = new ConcurrentDictionary<AggregationKey, ConcurrentDictionary<int, HashSet<int>>>();
            var level3 = new ConcurrentDictionary<int, HashSet<int>>();
            level3[1] = new HashSet<int> { 100, 200 };
            level3[2] = new HashSet<int> { 300, 400 };
            level2[key1] = level3;
            original.Connections[1000] = level2;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeDeepAggregatedConnection);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeDeepAggregatedConnection(stream));

            deserialized.ConnectionType.Should().Be(77);
            deserialized.Connections.Should().HaveCount(1);
            deserialized.Connections[1000].Should().HaveCount(1);
        }

        #endregion

        #region Cross-compatibility Tests (Protobuf-net to GProtobuf Stream)

        [Fact]
        public void CrossCompat_TriggerContainer_PG_Stream()
        {
            var original = new TriggerContainer
            {
                TriggerName = "CrossTest",
                Priority = 99,
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "CrossBase",
                    BaseValue = 111,
                    DerivedName = "CrossDerived",
                    DerivedValue = 222
                }
            };
            var bytes = SerializeWithProtobufNet(original);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(stream));

            deserialized.TriggerName.Should().Be("CrossTest");
            deserialized.Priority.Should().Be(99);
            deserialized.ActionParameters.BaseName.Should().Be("CrossBase");
            deserialized.ActionParameters.DerivedValue.Should().Be(222);
        }

        [Fact]
        public void CrossCompat_DeepNestingContainer_PG_Stream()
        {
            var original = new DeepNestingContainer
            {
                Trigger = new DerivedTriggerContainer
                {
                    Name = "CrossDeep",
                    ActionParameters = new DerivedActionParams
                    {
                        BaseName = "CrossDeepBase",
                        BaseValue = 333,
                        DerivedName = "CrossDeepDerived",
                        DerivedValue = 444
                    }
                }
            };
            var bytes = SerializeWithProtobufNet(original);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeDeepNestingContainer(stream));

            deserialized.Trigger.Should().NotBeNull();
            deserialized.Trigger.Name.Should().Be("CrossDeep");
            deserialized.Trigger.ActionParameters.DerivedValue.Should().Be(444);
        }

        [Fact]
        public void CrossCompat_ModelInh1_PG_Stream()
        {
            var original = new ModelInh1
            {
                Id = 999,
                Description = "CrossInheritance",
                Guid = Guid.NewGuid()
            };
            var bytes = SerializeWithProtobufNet(original);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<ModelInh1>();
            var derived = (ModelInh1)deserialized;
            derived.Id.Should().Be(999);
            derived.Description.Should().Be("CrossInheritance");
            derived.Guid.Should().Be(original.Guid);
        }

        [Fact]
        public void CrossCompat_Model15_ThreeLevels_PG_Stream()
        {
            var original = new Model15
            {
                Id = 15,
                Description = "Level2",
                Guid = Guid.NewGuid(),
                Description15 = "Level3",
                Guid15 = Guid.NewGuid()
            };
            var bytes = SerializeWithProtobufNet(original);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            deserialized.Should().BeOfType<Model15>();
            var derived = (Model15)deserialized;
            derived.Id.Should().Be(15);
            derived.Description.Should().Be("Level2");
            derived.Description15.Should().Be("Level3");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void EdgeCase_NullNestedDerived_GG_Stream()
        {
            var original = new TriggerContainer
            {
                TriggerName = "NullTest",
                Priority = 1,
                ActionParameters = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeTriggerContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(stream));

            deserialized.TriggerName.Should().Be("NullTest");
            deserialized.Priority.Should().Be(1);
            deserialized.ActionParameters.Should().BeNull();
        }

        [Fact]
        public void EdgeCase_EmptyDictionaryWithDerived_GG_Stream()
        {
            var original = new MapWithDerivedValue
            {
                Items = new Dictionary<int, DerivedActionParams>()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMapWithDerivedValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMapWithDerivedValue(stream));

            // Empty collections serialize to no bytes, result is null
            (deserialized.Items == null || deserialized.Items.Count == 0).Should().BeTrue();
        }

        [Fact]
        public void EdgeCase_LargeInheritanceData_GG_Stream()
        {
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 4000),
                Primary = new DerivedActionParams
                {
                    BaseName = new string('A', 4000),
                    DerivedName = new string('B', 4000),
                    BaseValue = int.MaxValue,
                    DerivedValue = int.MinValue
                },
                Secondary = new DerivedActionParams
                {
                    BaseName = new string('C', 4000),
                    DerivedName = new string('D', 4000),
                    BaseValue = 999999,
                    DerivedValue = -999999
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Length.Should().Be(4000);
            deserialized.Primary.BaseName.Length.Should().Be(4000);
            deserialized.Primary.BaseValue.Should().Be(int.MaxValue);
            deserialized.Secondary.DerivedValue.Should().Be(-999999);
        }

        #region MultiDerivedContainer Buffer Investigation Tests

        [Fact]
        public void MultiDerivedContainer_SmallStrings_Stream()
        {
            // Test with small strings that fit in default buffer (4096 bytes)
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 100),
                Primary = new DerivedActionParams
                {
                    BaseName = "SmallBase",
                    DerivedName = "SmallDerived",
                    BaseValue = 42,
                    DerivedValue = 99
                },
                Secondary = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Should().Be(original.Name);
            deserialized.Primary.BaseName.Should().Be("SmallBase");
            deserialized.Primary.DerivedName.Should().Be("SmallDerived");
        }

        [Fact]
        public void MultiDerivedContainer_MediumStrings_Stream()
        {
            // Test with medium strings (2000 bytes - fits in buffer)
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 2000),
                Primary = new DerivedActionParams
                {
                    BaseName = new string('A', 1000),
                    DerivedName = new string('B', 1000),
                    BaseValue = 42,
                    DerivedValue = 99
                },
                Secondary = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Length.Should().Be(2000);
            deserialized.Primary.BaseName.Length.Should().Be(1000);
        }

        [Fact]
        public void MultiDerivedContainer_NearBufferLimit_Stream()
        {
            // Test with strings near buffer limit (4000 bytes - close to 4096 default)
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 4000),
                Primary = new DerivedActionParams
                {
                    BaseName = new string('A', 100),
                    DerivedName = new string('B', 100),
                    BaseValue = 42,
                    DerivedValue = 99
                },
                Secondary = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Length.Should().Be(4000);
        }

        [Fact]
        public void MultiDerivedContainer_ExceedsBufferLimit_Stream()
        {
            // Test with strings exceeding buffer limit (5000 bytes > 4096 default)
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 5000),
                Primary = new DerivedActionParams
                {
                    BaseName = new string('A', 100),
                    DerivedName = new string('B', 100),
                    BaseValue = 42,
                    DerivedValue = 99
                },
                Secondary = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream));

            deserialized.Name.Length.Should().Be(5000);
        }

        [Fact]
        public void MultiDerivedContainer_WithLargerBuffer_Stream()
        {
            // Test with explicit larger buffer (16KB)
            var original = new MultiDerivedContainer
            {
                Name = new string('X', 10000),
                Primary = new DerivedActionParams
                {
                    BaseName = new string('A', 5000),
                    DerivedName = new string('B', 5000),
                    BaseValue = int.MaxValue,
                    DerivedValue = int.MinValue
                },
                Secondary = null
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeMultiDerivedContainer);

            // Use larger buffer to test if that's the issue
            using var stream = new MemoryStream(bytes);
            Span<byte> largeBuffer = stackalloc byte[16384]; // 16KB buffer
            var deserialized = CrossTestModel.Serialization.Deserializers.DeserializeMultiDerivedContainer(stream, largeBuffer);

            deserialized.Name.Length.Should().Be(10000);
            deserialized.Primary.BaseName.Length.Should().Be(5000);
        }

        #endregion

        #endregion

        #region Nested Dictionary Tests

        [Fact]
        public void NestedDictionary_NestedMapContainer_GG_Stream()
        {
            var original = new NestedMapContainer
            {
                NestedMap = new Dictionary<uint, Dictionary<int, string>>
                {
                    [1] = new Dictionary<int, string> { [10] = "A", [20] = "B" },
                    [2] = new Dictionary<int, string> { [30] = "C" }
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeNestedMapContainer);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedMapContainer(stream));

            deserialized.NestedMap.Should().HaveCount(2);
            deserialized.NestedMap[1][10].Should().Be("A");
            deserialized.NestedMap[1][20].Should().Be("B");
            deserialized.NestedMap[2][30].Should().Be("C");
        }

        [Fact]
        public void NestedDictionary_NestedConcurrentMap_GG_Stream()
        {
            var original = new NestedConcurrentMap
            {
                NestedMap = new ConcurrentDictionary<uint, ConcurrentDictionary<int, string>>()
            };
            var inner1 = new ConcurrentDictionary<int, string>();
            inner1[100] = "Value100";
            inner1[200] = "Value200";
            original.NestedMap[1] = inner1;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeNestedConcurrentMap);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedConcurrentMap(stream));

            deserialized.NestedMap.Should().HaveCount(1);
            deserialized.NestedMap[1][100].Should().Be("Value100");
            deserialized.NestedMap[1][200].Should().Be("Value200");
        }

        [Fact]
        public void NestedDictionary_MixedConcurrentAndRegular_GG_Stream()
        {
            var original = new ConcurrentMapWithDictionaryValue
            {
                Items = new ConcurrentDictionary<uint, Dictionary<int, string>>()
            };
            original.Items[1] = new Dictionary<int, string> { [10] = "Mixed1" };
            original.Items[2] = new Dictionary<int, string> { [20] = "Mixed2", [30] = "Mixed3" };

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithDictionaryValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithDictionaryValue(stream));

            deserialized.Items.Should().HaveCount(2);
            deserialized.Items[1][10].Should().Be("Mixed1");
            deserialized.Items[2][20].Should().Be("Mixed2");
        }

        [Fact]
        public void NestedDictionary_DictionaryWithConcurrentMapValue_GG_Stream()
        {
            var original = new DictionaryWithConcurrentMapValue
            {
                Items = new Dictionary<uint, ConcurrentDictionary<int, string>>()
            };
            var inner = new ConcurrentDictionary<int, string>();
            inner[1] = "ConcurrentValue1";
            inner[2] = "ConcurrentValue2";
            original.Items[100] = inner;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeDictionaryWithConcurrentMapValue);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeDictionaryWithConcurrentMapValue(stream));

            deserialized.Items.Should().HaveCount(1);
            deserialized.Items[100][1].Should().Be("ConcurrentValue1");
            deserialized.Items[100][2].Should().Be("ConcurrentValue2");
        }

        #endregion

        #region Deep Nested ConcurrentDictionary Tests

        [Fact]
        public void DeepNested_ConcurrentMapWithEnumKey_GG_Stream()
        {
            var original = new ConcurrentMapWithEnumKey
            {
                Items = new ConcurrentDictionary<ConnectionStatus, ConcurrentDictionary<int, HashSet<int>>>()
            };
            var inner = new ConcurrentDictionary<int, HashSet<int>>();
            inner[1] = new HashSet<int> { 10, 20, 30 };
            original.Items[ConnectionStatus.Pending] = inner;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithEnumKey);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithEnumKey(stream));

            deserialized.Items.Should().HaveCount(1);
            deserialized.Items[ConnectionStatus.Pending][1].Should().Contain(10);
            deserialized.Items[ConnectionStatus.Pending][1].Should().Contain(20);
            deserialized.Items[ConnectionStatus.Pending][1].Should().Contain(30);
        }

        [Fact]
        public void DeepNested_ConcurrentMapWithEnumArray_GG_Stream()
        {
            var original = new ConcurrentMapWithEnumArray
            {
                Items = new ConcurrentDictionary<uint, ConcurrentDictionary<ConnectionStatus, List<ConnectionStatus>>>()
            };
            var inner = new ConcurrentDictionary<ConnectionStatus, List<ConnectionStatus>>();
            inner[ConnectionStatus.Connected] = new List<ConnectionStatus>
            {
                ConnectionStatus.Connected,
                ConnectionStatus.Disconnected,
                ConnectionStatus.Pending
            };
            original.Items[1] = inner;

            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeConcurrentMapWithEnumArray);

            var deserialized = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeConcurrentMapWithEnumArray(stream));

            deserialized.Items.Should().HaveCount(1);
            deserialized.Items[1][ConnectionStatus.Connected].Should().HaveCount(3);
            deserialized.Items[1][ConnectionStatus.Connected].Should().Contain(ConnectionStatus.Pending);
        }

        #endregion

        #region Stream vs Span Consistency Tests

        [Fact]
        public void Consistency_TriggerContainer_StreamVsSpan()
        {
            var original = new TriggerContainer
            {
                TriggerName = "ConsistencyTest",
                Priority = 42,
                ActionParameters = new DerivedActionParams
                {
                    BaseName = "ConsBase",
                    BaseValue = 100,
                    DerivedName = "ConsDerived",
                    DerivedValue = 200
                }
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeTriggerContainer);

            // Deserialize with SpanReader
            var spanResult = CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(bytes);

            // Deserialize with StreamReader
            var streamResult = DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeTriggerContainer(stream));

            // Compare results
            streamResult.TriggerName.Should().Be(spanResult.TriggerName);
            streamResult.Priority.Should().Be(spanResult.Priority);
            streamResult.ActionParameters.BaseName.Should().Be(spanResult.ActionParameters.BaseName);
            streamResult.ActionParameters.BaseValue.Should().Be(spanResult.ActionParameters.BaseValue);
            streamResult.ActionParameters.DerivedName.Should().Be(spanResult.ActionParameters.DerivedName);
            streamResult.ActionParameters.DerivedValue.Should().Be(spanResult.ActionParameters.DerivedValue);
        }

        [Fact]
        public void Consistency_Model15_StreamVsSpan()
        {
            var original = new Model15
            {
                Id = 15,
                Description = "Desc2",
                Guid = Guid.NewGuid(),
                Description15 = "Desc15",
                Guid15 = Guid.NewGuid()
            };
            var bytes = SerializeWithGProtobuf(original, CrossTestModel.Serialization.Serializers.SerializeModelBase);

            // Deserialize with SpanReader
            var spanResult = (Model15)CrossTestModel.Serialization.Deserializers.DeserializeModelBase(bytes);

            // Deserialize with StreamReader
            var streamResult = (Model15)DeserializeWithGProtobufStreamFromBytes(bytes,
                stream => CrossTestModel.Serialization.Deserializers.DeserializeModelBase(stream));

            // Compare results
            streamResult.Id.Should().Be(spanResult.Id);
            streamResult.Description.Should().Be(spanResult.Description);
            streamResult.Guid.Should().Be(spanResult.Guid);
            streamResult.Description15.Should().Be(spanResult.Description15);
            streamResult.Guid15.Should().Be(spanResult.Guid15);
        }

        #endregion
    }
}
