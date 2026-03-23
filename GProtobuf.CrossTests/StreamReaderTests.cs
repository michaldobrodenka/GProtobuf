using FluentAssertions;
using GProtobuf.Tests.TestModel;
using CrossTestModel = GProtobuf.CrossTests.TestModel;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// StreamReader Deserialization Tests
///
/// These tests verify that StreamReader produces identical results to SpanReader
/// and maintains full compatibility with protobuf-net.
///
/// Test naming convention:
/// - GG_Stream: GProtobuf serialize -> GProtobuf StreamReader deserialize
/// - PG_Stream: protobuf-net serialize -> GProtobuf StreamReader deserialize
/// - SpanVsStream: Compares SpanReader and StreamReader produce identical results
/// </summary>
public class StreamReaderTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _output;

    public StreamReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Basic Types Tests

    [Fact]
    public void BasicTypes_AllValues_GG_Stream()
    {
        var model = new BasicTypesModel
        {
            ByteValue = 255,
            SByteValue = -128,
            ShortValue = -32768,
            UShortValue = 65535,
            IntValue = int.MinValue,
            UIntValue = uint.MaxValue,
            LongValue = long.MinValue,
            ULongValue = ulong.MaxValue,
            FloatValue = 3.14159f,
            DoubleValue = 2.71828,
            BoolValue = true,
            StringValue = "Hello, World!",
            BytesValue = new byte[] { 1, 2, 3, 4, 5 },
            CharValue = 'X'
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void BasicTypes_AllValues_PG_Stream()
    {
        // Note: CharValue is excluded because protobuf-net encodes char as string,
        // while GProtobuf encodes as uint16 (protobuf default for char type)
        var model = new BasicTypesModel
        {
            ByteValue = 255,
            SByteValue = -128,
            ShortValue = -32768,
            UShortValue = 65535,
            IntValue = int.MinValue,
            UIntValue = uint.MaxValue,
            LongValue = long.MinValue,
            ULongValue = ulong.MaxValue,
            FloatValue = 3.14159f,
            DoubleValue = 2.71828,
            BoolValue = true,
            StringValue = "Hello, protobuf-net!",
            BytesValue = new byte[] { 10, 20, 30 },
            CharValue = 'Z'
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(stream));

        // Exclude CharValue - protobuf-net encodes char as string, GProtobuf as uint16
        deserialized.Should().BeEquivalentTo(model, options => options.Excluding(m => m.CharValue));
    }

    [Fact]
    public void BasicTypes_SpanVsStream_ShouldBeIdentical()
    {
        var model = new BasicTypesModel
        {
            IntValue = 42,
            StringValue = "Test",
            DoubleValue = 123.456
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    #endregion

    #region ZigZag Encoding Tests

    [Fact]
    public void ZigZag_NegativeValues_GG_Stream()
    {
        var model = new BasicTypesZigZagModel
        {
            IntValue = -1000000,
            LongValue = -9999999999L
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesZigZagModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ZigZag_NegativeValues_PG_Stream()
    {
        var model = new BasicTypesZigZagModel
        {
            IntValue = int.MinValue,
            LongValue = long.MinValue
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeBasicTypesZigZagModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Array Tests

    [Fact]
    public void PrimitiveArrays_GG_Stream()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new[] { 1.5f, 2.5f, 3.5f },
            LongArray = new[] { 100L, 200L, 300L },
            DoubleArray = new[] { 1.1, 2.2, 3.3 },
            BoolArray = new[] { true, false, true }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void PrimitiveArrays_PG_Stream()
    {
        var model = new PrimitiveArraysTestModel
        {
            FloatArray = new[] { float.MinValue, 0, float.MaxValue },
            LongArray = new[] { long.MaxValue, long.MinValue },
            DoubleArray = new[] { double.MinValue, 0, double.MaxValue },
            BoolArray = new[] { true, true, false, false, true }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void StringArrays_GG_Stream()
    {
        var model = new StringArraysTestModel
        {
            BasicStringArray = new[] { "Hello", "World", "Test", "Array" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeStringArraysTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeStringArraysTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ByteArray_LargeData_GG_Stream()
    {
        var model = new ByteArrayTestModel
        {
            BasicByteArray = new byte[10000]
        };
        new Random(42).NextBytes(model.BasicByteArray);

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeByteArrayTestModel);

        // Use custom buffer larger than data size (default 4KB is too small for 10KB data)
        using var ms = new MemoryStream(data);
        Span<byte> largeBuffer = stackalloc byte[16384]; // 16KB buffer
        var deserialized = TestModel.Serialization.Deserializers.DeserializeByteArrayTestModel(ms, largeBuffer);

        deserialized.BasicByteArray.Should().BeEquivalentTo(model.BasicByteArray);
    }

    #endregion

    #region Collection Tests

    [Fact]
    public void Collections_List_GG_Stream()
    {
        var model = new CollectionTypesTestModel
        {
            StringList = new List<string> { "a", "b", "c" }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeCollectionTypesTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeCollectionTypesTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void HashSet_GG_Stream()
    {
        var model = new CrossTestModel.HashSetTestModel
        {
            UniqueNumbers = new HashSet<int> { 1, 2, 3, 4, 5 },
            UniqueTags = new HashSet<string> { "tag1", "tag2", "tag3" }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeHashSetTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeHashSetTestModel(stream));

        deserialized.UniqueNumbers.Should().BeEquivalentTo(model.UniqueNumbers);
        deserialized.UniqueTags.Should().BeEquivalentTo(model.UniqueTags);
    }

    [Fact]
    public void HashSet_PG_Stream()
    {
        var model = new CrossTestModel.HashSetTestModel
        {
            UniqueNumbers = new HashSet<int> { 10, 20, 30 },
            UniqueTags = new HashSet<string> { "x", "y", "z" }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeHashSetTestModel(stream));

        deserialized.UniqueNumbers.Should().BeEquivalentTo(model.UniqueNumbers);
        deserialized.UniqueTags.Should().BeEquivalentTo(model.UniqueTags);
    }

    #endregion

    #region Dictionary/Map Tests

    [Fact]
    public void Dictionary_IntString_GG_Stream()
    {
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>
            {
                { 1, "one" },
                { 2, "two" },
                { 3, "three" }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void Dictionary_IntString_PG_Stream()
    {
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>
            {
                { 100, "alpha" },
                { 200, "beta" },
                { 300, "gamma" }
            }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void GuidMap_GG_Stream()
    {
        var model = new CrossTestModel.GuidMapTestModel
        {
            GuidStringMap = new Dictionary<Guid, string>
            {
                { Guid.NewGuid(), "value1" },
                { Guid.NewGuid(), "value2" }
            }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeGuidMapTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeGuidMapTestModel(stream));

        deserialized.GuidStringMap.Should().BeEquivalentTo(model.GuidStringMap);
    }

    [Fact]
    public void ComprehensiveDictionary_GG_Stream()
    {
        var model = new CrossTestModel.ComprehensiveDictionaryTestModel
        {
            IntStringMap = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } },
            StringIntMap = new Dictionary<string, int> { { "a", 10 }, { "b", 20 } },
            IntIntMap = new Dictionary<int, int> { { 1, 100 }, { 2, 200 } },
            LongStringMap = new Dictionary<long, string> { { 1000L, "thousand" } },
            StringDoubleMap = new Dictionary<string, double> { { "pi", 3.14159 } }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeComprehensiveDictionaryTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeComprehensiveDictionaryTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Tuple Tests

    [Fact]
    public void Tuple_Basic_GG_Stream()
    {
        var model = new TupleModel
        {
            IntStringTuple = Tuple.Create(42, "answer"),
            DoubleTuple = Tuple.Create(3.14, 2.71)
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeTupleModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeTupleModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void Tuple_Basic_PG_Stream()
    {
        var model = new TupleModel
        {
            IntStringTuple = Tuple.Create(100, "hundred"),
            DoubleTuple = Tuple.Create(2.71828, 1.41421)
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeTupleModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void NestedTuple_GG_Stream()
    {
        var model = new CrossTestModel.NestedTupleModel
        {
            SimpleNested = Tuple.Create(1, Tuple.Create("nested", true))
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeNestedTupleModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeNestedTupleModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ExtendedTuple_SixElements_GG_Stream()
    {
        var model = new CrossTestModel.ExtendedTupleModel
        {
            SixElementTuple = Tuple.Create("one", 2, 3.0, true, 5.0f, 6L)
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeExtendedTupleModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region DateTime/Guid/TimeSpan Tests

    [Fact]
    public void DateTime_GG_Stream()
    {
        var model = new CrossTestModel.DateTimeTypesModel
        {
            LocalTimestamp = DateTime.Now,
            UtcTimestamp = DateTime.UtcNow,
            UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeDateTimeTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeDateTimeTypesModel(stream));

        // DateTime comparison with tolerance for serialization precision
        deserialized.LocalTimestamp.Should().BeCloseTo(model.LocalTimestamp, TimeSpan.FromMilliseconds(1));
        deserialized.UtcTimestamp.Should().BeCloseTo(model.UtcTimestamp, TimeSpan.FromMilliseconds(1));
        deserialized.UnixEpoch.Should().BeCloseTo(model.UnixEpoch, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Guid_GG_Stream()
    {
        var model = new GuidTypesModel
        {
            GuidValue = Guid.NewGuid(),
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeGuidTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void Guid_PG_Stream()
    {
        var model = new GuidTypesModel
        {
            GuidValue = Guid.NewGuid(),
            EmptyGuidValue = Guid.Empty,
            AnotherGuidValue = Guid.NewGuid()
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeGuidTypesModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Enum Tests

    [Fact]
    public void Enum_GG_Stream()
    {
        var model = new EnumTypesModel
        {
            Status = Status.Active,
            Priority = Priority.High,
            SignedValue = SignedEnum.NegativeOne
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeEnumTypesModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void Enum_PG_Stream()
    {
        var model = new EnumTypesModel
        {
            Status = Status.Deleted,
            Priority = Priority.Critical,
            SignedValue = SignedEnum.Two
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeEnumTypesModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Inheritance Tests (ProtoInclude)

    [Fact]
    public void Inheritance_DerivedType_GG_Stream()
    {
        var model = new C
        {
            StringA = "BaseValue",
            StringB = "DerivedB",
            StringC = "MostDerived"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeA(stream));

        deserialized.Should().BeEquivalentTo(model);
        deserialized.Should().BeOfType<C>();
    }

    [Fact]
    public void Inheritance_DerivedType_PG_Stream()
    {
        var model = new C
        {
            StringA = "FromProtobufNet",
            StringB = "MiddleClass",
            StringC = "LeafClass"
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeA(stream));

        deserialized.Should().BeEquivalentTo(model);
        deserialized.Should().BeOfType<C>();
    }

    [Fact]
    public void Inheritance_PartialFields_GG_Stream()
    {
        var model = new B
        {
            StringA = "OnlyA",
            StringB = null
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeA(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Nested Types Tests

    [Fact]
    public void NestedMessage_GG_Stream()
    {
        var model = new MessageArraysTestModel
        {
            SimpleMessages = new SimpleMessage[]
            {
                new SimpleMessage { Name = "First", Value = 1 },
                new SimpleMessage { Name = "Second", Value = 2 }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMessageArraysTestModel);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeMessageArraysTestModel(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void RecursiveType_GG_Stream()
    {
        var model = new RecursiveNode
        {
            Value = 1,
            Child = new RecursiveNode
            {
                Value = 2,
                Child = new RecursiveNode
                {
                    Value = 3,
                    Child = null
                }
            }
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeRecursiveNode);
        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeRecursiveNode(stream));

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region Large Data Tests (Buffer Management)

    [Fact]
    public void LargeData_ExceedsBufferSize_GG_Stream()
    {
        // Create data larger than default buffer (4KB)
        var model = new BasicTypesModel
        {
            StringValue = new string('X', 10000), // 10KB string
            BytesValue = new byte[5000]
        };
        new Random(42).NextBytes(model.BytesValue);

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);
        _output.WriteLine($"Serialized size: {data.Length} bytes");

        // Use custom buffer larger than data size (default 4KB is too small)
        using var ms = new MemoryStream(data);
        Span<byte> largeBuffer = stackalloc byte[16384]; // 16KB buffer
        var deserialized = TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(ms, largeBuffer);

        deserialized.StringValue.Should().Be(model.StringValue);
        deserialized.BytesValue.Should().BeEquivalentTo(model.BytesValue);
    }

    [Fact]
    public void LargeArray_ExceedsBufferSize_GG_Stream()
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[5000],
            FloatArray = new float[3000]
        };
        for (int i = 0; i < model.LongArray.Length; i++)
            model.LongArray[i] = i * 1000L;
        for (int i = 0; i < model.FloatArray.Length; i++)
            model.FloatArray[i] = i * 1.5f;

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);
        _output.WriteLine($"Serialized size: {data.Length} bytes");

        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(stream));

        deserialized.LongArray.Should().BeEquivalentTo(model.LongArray);
        deserialized.FloatArray.Should().BeEquivalentTo(model.FloatArray);
    }

    [Fact]
    public void LargeDictionary_GG_Stream()
    {
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>()
        };
        for (int i = 0; i < 1000; i++)
        {
            model.Dictionary[i] = $"value_{i}";
        }

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        _output.WriteLine($"Serialized size: {data.Length} bytes");

        var deserialized = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(stream));

        deserialized.Dictionary.Should().HaveCount(1000);
        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion

    #region SpanReader vs StreamReader Consistency Tests

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void SpanVsStream_ArraySizes_ShouldBeIdentical(int arraySize)
    {
        var model = new PrimitiveArraysTestModel
        {
            LongArray = new long[arraySize]
        };
        for (int i = 0; i < arraySize; i++)
            model.LongArray[i] = i;

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(stream));

        streamResult.LongArray.Should().BeEquivalentTo(spanResult.LongArray);
    }

    [Fact]
    public void SpanVsStream_ComplexModel_ShouldBeIdentical()
    {
        var model = new CrossTestModel.ComprehensiveDictionaryTestModel
        {
            IntStringMap = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } },
            StringIntMap = new Dictionary<string, int> { { "a", 10 }, { "b", 20 } },
            IntIntMap = new Dictionary<int, int> { { 100, 1000 } },
            LongStringMap = new Dictionary<long, string> { { 999L, "big" } },
            StringDoubleMap = new Dictionary<string, double> { { "val", 123.456 } }
        };

        var data = SerializeWithGProtobuf(model, CrossTestModel.Serialization.Serializers.SerializeComprehensiveDictionaryTestModel);

        var spanResult = DeserializeWithGProtobuf(data,
            bytes => CrossTestModel.Serialization.Deserializers.DeserializeComprehensiveDictionaryTestModel(bytes));
        var streamResult = DeserializeWithGProtobufStreamFromBytes(data,
            stream => CrossTestModel.Serialization.Deserializers.DeserializeComprehensiveDictionaryTestModel(stream));

        streamResult.Should().BeEquivalentTo(spanResult);
    }

    #endregion

    #region Custom Buffer Tests

    [Fact]
    public void StreamReader_WithCustomBuffer_GG()
    {
        var model = new BasicTypesModel
        {
            IntValue = 42,
            StringValue = "Custom buffer test"
        };

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeBasicTypesModel);

        // Test with custom buffer size
        using var ms = new MemoryStream(data);
        Span<byte> customBuffer = stackalloc byte[8192]; // 8KB buffer
        var deserialized = TestModel.Serialization.Deserializers.DeserializeBasicTypesModel(ms, customBuffer);

        deserialized.Should().BeEquivalentTo(model);
    }

    #endregion
}
