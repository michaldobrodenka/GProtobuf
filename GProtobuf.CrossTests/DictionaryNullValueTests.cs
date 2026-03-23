using FluentAssertions;
using GProtobuf.Tests.TestModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// Tests for Dictionary null value serialization behavior.
///
/// Verifies that when a Dictionary entry has a null value:
/// - The map entry is written WITH the key field
/// - The value field is OMITTED (not written)
/// - This matches protobuf-net behavior exactly
///
/// Test patterns:
/// - PG: protobuf-net serialization -> GProtobuf deserialization
/// - GG: GProtobuf serialization -> GProtobuf deserialization
/// - GP: GProtobuf serialization -> protobuf-net deserialization
/// - ByteMatch: GProtobuf bytes == protobuf-net bytes (exact wire format)
/// </summary>
public sealed class DictionaryNullValueTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public DictionaryNullValueTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void LogBytes(string label, byte[] data)
    {
        _output.WriteLine($"{label}: {BitConverter.ToString(data)}");
    }

    // Helper methods to wrap ReadOnlySpan deserializers as byte[] functions
    private static DictWithStringNullValues DeserializeDictWithStringNullValues(byte[] data)
        => TestModel.Serialization.Deserializers.DeserializeDictWithStringNullValues(data);

    private static DictWithObjectNullValues DeserializeDictWithObjectNullValues(byte[] data)
        => TestModel.Serialization.Deserializers.DeserializeDictWithObjectNullValues(data);

    private static DictStringKeyStringNullValue DeserializeDictStringKeyStringNullValue(byte[] data)
        => TestModel.Serialization.Deserializers.DeserializeDictStringKeyStringNullValue(data);

    private static DictLevel2NullValues_IntStringString DeserializeDictLevel2NullValues_IntStringString(byte[] data)
        => TestModel.Serialization.Deserializers.DeserializeDictLevel2NullValues_IntStringString(data);

    #region Dict<int, string> null value tests

    [Fact]
    public void DictIntString_NullValue_ByteMatch()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "val" },
                { 2, null }  // null value - should serialize entry WITH key, WITHOUT value
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes, "GProtobuf should produce identical bytes to protobuf-net for null string values");
    }

    [Fact]
    public void DictIntString_NullValue_GG()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "val" },
                { 2, null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var result = DeserializeWithGProtobuf(data, DeserializeDictWithStringNullValues);

        result.Items.Should().HaveCount(2);
        result.Items[1].Should().Be("val");
        // NOTE: In protobuf, missing string fields get default value (""), not null
        // The map entry has key but no value field, so deserializer returns ""
        result.Items[2].Should().Be("");
    }

    [Fact]
    public void DictIntString_NullValue_PG()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "val" },
                { 2, null }
            }
        };

        var data = SerializeWithProtobufNet(model);
        var result = DeserializeWithGProtobuf(data, DeserializeDictWithStringNullValues);

        result.Items.Should().HaveCount(2);
        result.Items[1].Should().Be("val");
        // NOTE: In protobuf, missing string fields get default value (""), not null
        result.Items[2].Should().Be("");
    }

    [Fact]
    public void DictIntString_NullValue_GP()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "val" },
                { 2, null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var result = DeserializeWithProtobufNet<DictWithStringNullValues>(data);

        result.Items.Should().HaveCount(2);
        result.Items[1].Should().Be("val");
        // NOTE: In protobuf, missing string fields get default value (""), not null
        result.Items[2].Should().Be("");
    }

    [Fact]
    public void DictIntString_AllNulls_ByteMatch()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, null },
                { 2, null },
                { 3, null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictIntString_EmptyVsNull_ByteMatch()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "" },    // empty string
                { 2, null }   // null
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region Dict<string, Object> null value tests

    [Fact]
    public void DictStringObject_NullValue_ByteMatch()
    {
        var model = new DictWithObjectNullValues
        {
            Items = new Dictionary<string, NullValueNestedObject>
            {
                { "a", new NullValueNestedObject { Name = "X", Value = 1 } },
                { "b", null }  // null object
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithObjectNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictStringObject_NullValue_GG()
    {
        var model = new DictWithObjectNullValues
        {
            Items = new Dictionary<string, NullValueNestedObject>
            {
                { "a", new NullValueNestedObject { Name = "X", Value = 1 } },
                { "b", null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithObjectNullValues);
        var result = DeserializeWithGProtobuf(data, DeserializeDictWithObjectNullValues);

        result.Items.Should().HaveCount(2);
        result.Items["a"].Name.Should().Be("X");
        result.Items["a"].Value.Should().Be(1);
        result.Items["b"].Should().BeNull();
    }

    [Fact]
    public void DictStringObject_NullValue_PG()
    {
        var model = new DictWithObjectNullValues
        {
            Items = new Dictionary<string, NullValueNestedObject>
            {
                { "a", new NullValueNestedObject { Name = "X", Value = 1 } },
                { "b", null }
            }
        };

        var data = SerializeWithProtobufNet(model);
        var result = DeserializeWithGProtobuf(data, DeserializeDictWithObjectNullValues);

        result.Items.Should().HaveCount(2);
        result.Items["a"].Name.Should().Be("X");
        result.Items["b"].Should().BeNull();
    }

    [Fact]
    public void DictStringObject_NullValue_GP()
    {
        var model = new DictWithObjectNullValues
        {
            Items = new Dictionary<string, NullValueNestedObject>
            {
                { "a", new NullValueNestedObject { Name = "X", Value = 1 } },
                { "b", null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithObjectNullValues);
        var result = DeserializeWithProtobufNet<DictWithObjectNullValues>(data);

        result.Items.Should().HaveCount(2);
        result.Items["a"].Name.Should().Be("X");
        // NOTE: protobuf-net creates default object for missing value field, not null
        result.Items["b"].Should().NotBeNull();
        result.Items["b"].Name.Should().BeNull();
        result.Items["b"].Value.Should().Be(0);
    }

    #endregion

    #region ConcurrentDictionary null value tests

    [Fact]
    public void ConcurrentDictIntString_NullValue_ByteMatch()
    {
        var model = new ConcurrentDictWithStringNullValues
        {
            Items = new ConcurrentDictionary<int, string>()
        };
        model.Items[1] = "val";
        model.Items[2] = null;

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeConcurrentDictWithStringNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void ConcurrentDictStringObject_NullValue_ByteMatch()
    {
        var model = new ConcurrentDictWithObjectNullValues
        {
            Items = new ConcurrentDictionary<string, NullValueNestedObject>()
        };
        model.Items["a"] = new NullValueNestedObject { Name = "X", Value = 1 };
        model.Items["b"] = null;

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeConcurrentDictWithObjectNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region Dict<string, string> null value tests

    [Fact]
    public void DictStringString_NullValue_ByteMatch()
    {
        var model = new DictStringKeyStringNullValue
        {
            Items = new Dictionary<string, string>
            {
                { "key1", "val1" },
                { "key2", null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictStringKeyStringNullValue);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictStringString_NullValue_GG()
    {
        var model = new DictStringKeyStringNullValue
        {
            Items = new Dictionary<string, string>
            {
                { "key1", "val1" },
                { "key2", null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictStringKeyStringNullValue);
        var result = DeserializeWithGProtobuf(data, DeserializeDictStringKeyStringNullValue);

        result.Items.Should().HaveCount(2);
        result.Items["key1"].Should().Be("val1");
        // NOTE: protobuf returns default value ("") for missing string fields
        result.Items["key2"].Should().Be("");
    }

    #endregion

    #region Nullable value type tests

    [Fact]
    public void DictIntNullableInt_NullValue_ByteMatch()
    {
        var model = new DictWithNullableIntNullValues
        {
            Items = new Dictionary<int, int?>
            {
                { 1, 100 },
                { 2, null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithNullableIntNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictIntNullableLong_NullValue_ByteMatch()
    {
        var model = new DictWithNullableLongNullValues
        {
            Items = new Dictionary<int, long?>
            {
                { 1, 100L },
                { 2, null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithNullableLongNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region Dict<int, byte[]> null value tests

    [Fact]
    public void DictIntByteArray_NullValue_ByteMatch()
    {
        var model = new DictWithByteArrayNullValues
        {
            Items = new Dictionary<int, byte[]>
            {
                { 1, new byte[] { 1, 2, 3 } },
                { 2, null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithByteArrayNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictIntByteArray_EmptyVsNull_ByteMatch()
    {
        var model = new DictWithByteArrayNullValues
        {
            Items = new Dictionary<int, byte[]>
            {
                { 1, new byte[0] },  // empty
                { 2, null }          // null
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithByteArrayNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region 2-Level Nested Dictionary null value tests

    [Fact]
    public void DictLevel2_InnerDictNull_ByteMatch()
    {
        var model = new DictLevel2NullValues_IntStringString
        {
            Items = new Dictionary<int, Dictionary<string, string>>
            {
                { 1, new Dictionary<string, string> { { "a", "val" } } },
                { 2, null }  // inner dict is null
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictLevel2NullValues_IntStringString);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictLevel2_InnerValueNull_ByteMatch()
    {
        var model = new DictLevel2NullValues_IntStringString
        {
            Items = new Dictionary<int, Dictionary<string, string>>
            {
                { 1, new Dictionary<string, string> { { "a", "val" }, { "b", null } } }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictLevel2NullValues_IntStringString);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictLevel2_InnerObjectNull_ByteMatch()
    {
        var model = new DictLevel2NullValues_StringIntObject
        {
            Items = new Dictionary<string, Dictionary<int, NullValueNestedObject>>
            {
                { "x", new Dictionary<int, NullValueNestedObject>
                    {
                        { 1, new NullValueNestedObject { Name = "N" } },
                        { 2, null }
                    }
                }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictLevel2NullValues_StringIntObject);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictLevel2_GG()
    {
        var model = new DictLevel2NullValues_IntStringString
        {
            Items = new Dictionary<int, Dictionary<string, string>>
            {
                { 1, new Dictionary<string, string> { { "a", "val" }, { "b", null } } },
                { 2, null }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictLevel2NullValues_IntStringString);
        var result = DeserializeWithGProtobuf(data, DeserializeDictLevel2NullValues_IntStringString);

        result.Items.Should().HaveCount(2);
        result.Items[1]["a"].Should().Be("val");
        // NOTE: protobuf returns default value ("") for missing string fields
        result.Items[1]["b"].Should().Be("");
        // NOTE: protobuf returns empty dictionary for missing dictionary fields
        result.Items[2].Should().NotBeNull();
        result.Items[2].Should().BeEmpty();
    }

    #endregion

    #region Deep Object Nesting tests

    [Fact]
    public void DeepNested_Level2Null_ByteMatch()
    {
        var model = new DeepNestedNullTestModel
        {
            Root = new Dictionary<string, NullTestLevel1Container>
            {
                { "root1", new NullTestLevel1Container
                    {
                        Name = "L1",
                        Children = new Dictionary<int, NullTestLevel2Container>
                        {
                            { 1, new NullTestLevel2Container { Description = "L2" } },
                            { 2, null }  // Level2 is null
                        }
                    }
                }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDeepNestedNullTestModel);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DeepNested_Level3Null_ByteMatch()
    {
        var model = new DeepNestedNullTestModel
        {
            Root = new Dictionary<string, NullTestLevel1Container>
            {
                { "root1", new NullTestLevel1Container
                    {
                        Name = "L1",
                        Children = new Dictionary<int, NullTestLevel2Container>
                        {
                            { 1, new NullTestLevel2Container
                                {
                                    Description = "L2",
                                    Items = new Dictionary<string, NullTestLevel3Container>
                                    {
                                        { "a", new NullTestLevel3Container { Value = 1 } },
                                        { "b", null }  // Level3 is null
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDeepNestedNullTestModel);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DeepNested_InnermostStringNull_ByteMatch()
    {
        var model = new DeepNestedNullTestModel
        {
            Root = new Dictionary<string, NullTestLevel1Container>
            {
                { "root1", new NullTestLevel1Container
                    {
                        Name = "L1",
                        Children = new Dictionary<int, NullTestLevel2Container>
                        {
                            { 1, new NullTestLevel2Container
                                {
                                    Description = "L2",
                                    Items = new Dictionary<string, NullTestLevel3Container>
                                    {
                                        { "a", new NullTestLevel3Container
                                            {
                                                Value = 1,
                                                Data = new Dictionary<int, string>
                                                {
                                                    { 1, "val" },
                                                    { 2, null }  // innermost string is null
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDeepNestedNullTestModel);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region Dict<K, List<V>> null list tests

    [Fact]
    public void DictWithList_NullList_ByteMatch()
    {
        var model = new DictWithListNullValues
        {
            Items = new Dictionary<int, List<string>>
            {
                { 1, new List<string> { "a", "b" } },
                { 2, null }  // list is null
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithListNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    [Fact]
    public void DictWithListOfObjects_NullList_ByteMatch()
    {
        var model = new DictWithListOfObjectsNullValues
        {
            Items = new Dictionary<string, List<NullValueNestedObject>>
            {
                { "a", new List<NullValueNestedObject> { new NullValueNestedObject { Name = "N" } } },
                { "b", null }
            }
        };

        var gpBytes = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictWithListOfObjectsNullValues);
        var pbBytes = SerializeWithProtobufNet(model);

        LogBytes("GProtobuf", gpBytes);
        LogBytes("protobuf-net", pbBytes);

        gpBytes.Should().Equal(pbBytes);
    }

    #endregion

    #region Bidirectional compatibility tests

    [Fact]
    public void DictIntString_Bidirectional_PG_GP()
    {
        var model = new DictWithStringNullValues
        {
            Items = new Dictionary<int, string>
            {
                { 1, "val" },
                { 2, null },
                { 3, "" }
            }
        };

        // protobuf-net -> GProtobuf
        var pbData = SerializeWithProtobufNet(model);
        var gpResult = DeserializeWithGProtobuf(pbData, DeserializeDictWithStringNullValues);

        // GProtobuf -> protobuf-net
        var gpData = SerializeWithGProtobuf(gpResult, TestModel.Serialization.Serializers.SerializeDictWithStringNullValues);
        var pbResult = DeserializeWithProtobufNet<DictWithStringNullValues>(gpData);

        pbResult.Items.Should().HaveCount(3);
        pbResult.Items[1].Should().Be("val");
        // NOTE: protobuf-net converts missing string value to "" (default), not null
        // This is expected protobuf semantics - missing fields get default values on read
        pbResult.Items[2].Should().Be("");
        pbResult.Items[3].Should().Be("");
    }

    #endregion
}
