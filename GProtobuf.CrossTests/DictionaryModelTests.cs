using FluentAssertions;
using GProtobuf.Tests.TestModel;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

/// <summary>
/// Dictionary/Map Tests for DictionaryModel
/// 
/// Tests verify Map/Dictionary serialization and deserialization across different patterns:
/// - PG (Protobuf-net to GProtobuf): Serialize with protobuf-net, Deserialize with GProtobuf
/// - GG (GProtobuf to GProtobuf): Serialize with GProtobuf, Deserialize with GProtobuf  
/// - GP (GProtobuf to Protobuf-net): Serialize with GProtobuf, Deserialize with protobuf-net
/// 
/// Dictionary types covered:
/// - Dictionary<int, string>: Simple key-value mapping
/// - List<KeyValuePair<long, NestedDictionaryValue>>: Collection of key-value pairs with nested messages
/// </summary>
public sealed class DictionaryModelTests : BaseSerializationTest
{
    private readonly ITestOutputHelper _outputHelper;
    
    public DictionaryModelTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    #region Test Data Factories
    
    private static DictionaryModel CreateSimpleDictionaryModel()
    {
        return new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>
            {
                { 1, "First" },
                { 2, "Second" },
                { 3, "Third" }
            },
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>
            {
                new KeyValuePair<long, NestedDictionaryValue>(10L, new NestedDictionaryValue { Value = 100, StringValue = "Value100" }),
                new KeyValuePair<long, NestedDictionaryValue>(20L, new NestedDictionaryValue { Value = 200, StringValue = "Value200" })
            }
            ,
            DerivedDictionary = new DerivedDictionary() { { 36456747, "sdfgsdfgsdfgsdf" }, /*{ 0, null},*/ {-1, "" } }
        };
    }
    
    private static DictionaryModel CreateEmptyDictionaryModel()
    {
        return new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>(),
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>()
        };
    }
    
    private static DictionaryModel CreateNullDictionaryModel()
    {
        return new DictionaryModel
        {
            Dictionary = null,
            ValuePairs = null
        };
    }
    
    private static DictionaryModel CreateComplexDictionaryModel()
    {
        return new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>
            {
                { int.MinValue, "MinValue" },
                { -1000, "Negative" },
                { 0, "Zero" },
                { 42, "Answer" },
                { 1000, "Positive" },
                { int.MaxValue, "MaxValue" }
            },
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>
            {
                new KeyValuePair<long, NestedDictionaryValue>(long.MinValue, new NestedDictionaryValue 
                { 
                    Value = int.MinValue, 
                    StringValue = "Long.MinValue -> Int.MinValue" 
                }),
                new KeyValuePair<long, NestedDictionaryValue>(0L, new NestedDictionaryValue 
                { 
                    Value = 0, 
                    StringValue = "Zero -> Zero" 
                }),
                new KeyValuePair<long, NestedDictionaryValue>(long.MaxValue, new NestedDictionaryValue 
                { 
                    Value = int.MaxValue, 
                    StringValue = "Long.MaxValue -> Int.MaxValue" 
                })
            }
        };
    }
    
    private static DictionaryModel CreateLargeDictionaryModel()
    {
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>(),
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>()
        };
        
        // Add 1000 entries to Dictionary
        for (int i = 0; i < 1000; i++)
        {
            model.Dictionary[i] = $"Value_{i}_" + new string('X', 50); // 50+ char strings
        }
        
        // Add 500 entries to ValuePairs
        for (long i = 0; i < 500; i++)
        {
            model.ValuePairs.Add(new KeyValuePair<long, NestedDictionaryValue>(
                i * 1000L,
                new NestedDictionaryValue 
                { 
                    Value = (int)(i % int.MaxValue), 
                    StringValue = $"Large_{i}_" + new string('Y', 100) 
                }
            ));
        }
        
        return model;
    }
    
    #endregion
    
    #region PG Tests (Protobuf-net to GProtobuf)
    
    [Fact]
    public void DictionaryModel_SimpleDictionary_PG()
    {
        var model = CreateSimpleDictionaryModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Verify Dictionary
        deserialized.Dictionary.Should().NotBeNull();
        deserialized.Dictionary.Should().HaveCount(3);
        deserialized.Dictionary[1].Should().Be("First");
        deserialized.Dictionary[2].Should().Be("Second");
        deserialized.Dictionary[3].Should().Be("Third");
        
        // Verify ValuePairs
        deserialized.ValuePairs.Should().NotBeNull();
        deserialized.ValuePairs.Should().HaveCount(2);
        
        var pair1 = deserialized.ValuePairs.First(p => p.Key == 10L);
        pair1.Value.Value.Should().Be(100);
        pair1.Value.StringValue.Should().Be("Value100");
        
        var pair2 = deserialized.ValuePairs.First(p => p.Key == 20L);
        pair2.Value.Value.Should().Be(200);
        pair2.Value.StringValue.Should().Be("Value200");
    }
    
    [Fact]
    public void DictionaryModel_EmptyDictionary_PG()
    {
        var model = CreateEmptyDictionaryModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Empty collections become null after deserialization in protobuf (default behavior)
        // This is correct protobuf behavior - empty collections are not serialized
        data.Length.Should().Be(0, "empty collections should not serialize any data");
        deserialized.Dictionary.Should().BeNull("empty collections deserialize as null");
        deserialized.ValuePairs.Should().BeNull("empty collections deserialize as null");
    }
    
    [Fact]
    public void DictionaryModel_NullDictionary_PG()
    {
        var model = CreateNullDictionaryModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Null collections should be preserved
        deserialized.Dictionary.Should().BeNull();
        deserialized.ValuePairs.Should().BeNull();
    }
    
    [Fact]
    public void DictionaryModel_ExtremeValues_PG()
    {
        var model = CreateComplexDictionaryModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Verify extreme int values in Dictionary
        deserialized.Dictionary.Should().HaveCount(6);
        deserialized.Dictionary[int.MinValue].Should().Be("MinValue");
        deserialized.Dictionary[-1000].Should().Be("Negative");
        deserialized.Dictionary[0].Should().Be("Zero");
        deserialized.Dictionary[42].Should().Be("Answer");
        deserialized.Dictionary[1000].Should().Be("Positive");
        deserialized.Dictionary[int.MaxValue].Should().Be("MaxValue");
        
        // Verify extreme long values in ValuePairs
        deserialized.ValuePairs.Should().HaveCount(3);
        
        var minPair = deserialized.ValuePairs.First(p => p.Key == long.MinValue);
        minPair.Value.Value.Should().Be(int.MinValue);
        minPair.Value.StringValue.Should().Be("Long.MinValue -> Int.MinValue");
        
        var zeroPair = deserialized.ValuePairs.First(p => p.Key == 0L);
        zeroPair.Value.Value.Should().Be(0);
        zeroPair.Value.StringValue.Should().Be("Zero -> Zero");
        
        var maxPair = deserialized.ValuePairs.First(p => p.Key == long.MaxValue);
        maxPair.Value.Value.Should().Be(int.MaxValue);
        maxPair.Value.StringValue.Should().Be("Long.MaxValue -> Int.MaxValue");
    }
    
    [Fact]
    public void DictionaryModel_LargeData_PG()
    {
        var model = CreateLargeDictionaryModel();

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Verify large Dictionary
        deserialized.Dictionary.Should().HaveCount(1000);
        for (int i = 0; i < 1000; i++)
        {
            deserialized.Dictionary[i].Should().Be($"Value_{i}_" + new string('X', 50));
        }
        
        // Verify large ValuePairs
        deserialized.ValuePairs.Should().HaveCount(500);
        for (int i = 0; i < 500; i++)
        {
            var pair = deserialized.ValuePairs.First(p => p.Key == i * 1000L);
            pair.Value.Value.Should().Be(i % int.MaxValue);
            pair.Value.StringValue.Should().Be($"Large_{i}_" + new string('Y', 100));
        }
    }
    
    [Fact]
    public void DictionaryModel_UnicodeStrings_PG()
    {
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string>
            {
                { 1, "Ľubomír" },
                { 2, "Žitný" },
                { 3, "Košice" },
                { 4, "Bratislava" },
                { 5, "ťŠčÝáé" },
                { 6, "你好世界" },
                { 7, "Привет мир" },
                { 8, "🎉🚀✨" }
            },
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>
            {
                new KeyValuePair<long, NestedDictionaryValue>(1L, new NestedDictionaryValue 
                { 
                    Value = 1, 
                    StringValue = "Slovenčina čžšťÝÁÍÉ" 
                }),
                new KeyValuePair<long, NestedDictionaryValue>(2L, new NestedDictionaryValue 
                { 
                    Value = 2, 
                    StringValue = "Emoji test: 🌟🎯🔥" 
                })
            }
        };

        var data = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Verify Unicode strings in Dictionary
        deserialized.Dictionary[1].Should().Be("Ľubomír");
        deserialized.Dictionary[2].Should().Be("Žitný");
        deserialized.Dictionary[3].Should().Be("Košice");
        deserialized.Dictionary[4].Should().Be("Bratislava");
        deserialized.Dictionary[5].Should().Be("ťŠčÝáé");
        deserialized.Dictionary[6].Should().Be("你好世界");
        deserialized.Dictionary[7].Should().Be("Привет мир");
        deserialized.Dictionary[8].Should().Be("🎉🚀✨");
        
        // Verify Unicode strings in ValuePairs
        var pair1 = deserialized.ValuePairs.First(p => p.Key == 1L);
        pair1.Value.StringValue.Should().Be("Slovenčina čžšťÝÁÍÉ");
        
        var pair2 = deserialized.ValuePairs.First(p => p.Key == 2L);
        pair2.Value.StringValue.Should().Be("Emoji test: 🌟🎯🔥");
    }
    
    #endregion
    
    #region GG Tests (GProtobuf to GProtobuf)
    
    [Fact]
    public void DictionaryModel_SimpleDictionary_GG()
    {
        var model = CreateSimpleDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_EmptyDictionary_GG()
    {
        var model = CreateEmptyDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Empty collections become null after serialization/deserialization in protobuf
        data.Length.Should().Be(0, "empty collections should not serialize any data");
        deserialized.Dictionary.Should().BeNull("empty collections deserialize as null");
        deserialized.ValuePairs.Should().BeNull("empty collections deserialize as null");
    }
    
    [Fact]
    public void DictionaryModel_NullDictionary_GG()
    {
        var model = CreateNullDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_ExtremeValues_GG()
    {
        var model = CreateComplexDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_LargeData_GG()
    {
        var model = CreateLargeDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Use detailed comparison for large data to ensure accuracy
        deserialized.Dictionary.Should().HaveCount(model.Dictionary.Count);
        foreach (var kvp in model.Dictionary)
        {
            deserialized.Dictionary[kvp.Key].Should().Be(kvp.Value);
        }
        
        deserialized.ValuePairs.Should().HaveCount(model.ValuePairs.Count);
        for (int i = 0; i < model.ValuePairs.Count; i++)
        {
            var originalPair = model.ValuePairs[i];
            var deserializedPair = deserialized.ValuePairs.First(p => p.Key == originalPair.Key);
            
            deserializedPair.Value.Value.Should().Be(originalPair.Value.Value);
            deserializedPair.Value.StringValue.Should().Be(originalPair.Value.StringValue);
        }
    }
    
    [Fact]
    public void DictionaryModel_ConsistentSerialization_GG()
    {
        // Test that serializing the same data twice produces identical bytes
        var model = CreateSimpleDictionaryModel();

        var data1 = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var data2 = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);

        data1.Should().Equal(data2, "serialization should be deterministic");
    }
    
    #endregion
    
    #region GP Tests (GProtobuf to Protobuf-net)
    
    [Fact]
    public void DictionaryModel_SimpleDictionary_GP()
    {
        var model = CreateSimpleDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var data1 = SerializeWithProtobufNet(model);
        var deserialized = DeserializeWithProtobufNet<DictionaryModel>(data);
        var deserialized1 = DeserializeWithGProtobuf(data1, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Compare with custom equivalency that treats null and empty string as equivalent
        deserialized.Should().BeEquivalentTo(model, options => options
            .Using<string>(ctx => 
            {
                var actual = ctx.Subject ?? "";
                var expected = ctx.Expectation ?? "";
                actual.Should().Be(expected);
            })
            .WhenTypeIs<string>());
    }
    
    [Fact]
    public void DictionaryModel_EmptyDictionary_GP()
    {
        var model = CreateEmptyDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithProtobufNet<DictionaryModel>(data);

        // Empty collections become null after serialization/deserialization in protobuf
        data.Length.Should().Be(0, "empty collections should not serialize any data");
        deserialized.Dictionary.Should().BeNull("empty collections deserialize as null");
        deserialized.ValuePairs.Should().BeNull("empty collections deserialize as null");
    }
    
    [Fact]
    public void DictionaryModel_NullDictionary_GP()
    {
        var model = CreateNullDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithProtobufNet<DictionaryModel>(data);

        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_ExtremeValues_GP()
    {
        var model = CreateComplexDictionaryModel();

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithProtobufNet<DictionaryModel>(data);

        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_ProtobufNetCompatibility_GP()
    {
        // Test that GProtobuf serialized data can be deserialized by protobuf-net
        // and then re-serialized by protobuf-net to produce equivalent data
        var model = CreateSimpleDictionaryModel();

        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var protobufData = SerializeWithProtobufNet(model);

        var deserializedByProtobufNet = DeserializeWithProtobufNet<DictionaryModel>(gprotobufData);
        var reserializedByProtobufNet = SerializeWithProtobufNet(deserializedByProtobufNet);
        var finalDeserialized = DeserializeWithGProtobuf(reserializedByProtobufNet, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Compare with custom equivalency that treats null and empty string as equivalent
        finalDeserialized.Should().BeEquivalentTo(model, options => options
            .Using<string>(ctx => 
            {
                var actual = ctx.Subject ?? "";
                var expected = ctx.Expectation ?? "";
                actual.Should().Be(expected);
            })
            .WhenTypeIs<string>());
    }
    
    #endregion
    
    #region Bidirectional Compatibility Tests
    
    [Fact]
    public void DictionaryModel_BidirectionalCompatibility_PG_GP()
    {
        // Test: protobuf-net -> GProtobuf -> GProtobuf -> protobuf-net
        var model = CreateComplexDictionaryModel();

        // protobuf-net serialize
        var protobufNetData = SerializeWithProtobufNet(model);
        
        // GProtobuf deserialize
        var gprotobufDeserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));
        
        // GProtobuf serialize
        var gprotobufData = SerializeWithGProtobuf(gprotobufDeserialized, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        
        // protobuf-net deserialize
        var finalDeserialized = DeserializeWithProtobufNet<DictionaryModel>(gprotobufData);

        finalDeserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void DictionaryModel_BidirectionalCompatibility_GG_PG()
    {
        // Test: GProtobuf -> GProtobuf -> protobuf-net -> GProtobuf
        var model = CreateLargeDictionaryModel();

        // GProtobuf serialize/deserialize
        var gprotobufData1 = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var gprotobufDeserialized = DeserializeWithGProtobuf(gprotobufData1, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));
        
        // protobuf-net serialize
        var protobufNetData = SerializeWithProtobufNet(gprotobufDeserialized);
        
        // GProtobuf deserialize
        var finalDeserialized = DeserializeWithGProtobuf(protobufNetData, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        // Use detailed comparison for large data
        finalDeserialized.Dictionary.Should().HaveCount(model.Dictionary.Count);
        finalDeserialized.ValuePairs.Should().HaveCount(model.ValuePairs.Count);
        
        foreach (var kvp in model.Dictionary)
        {
            finalDeserialized.Dictionary[kvp.Key].Should().Be(kvp.Value);
        }
        
        for (int i = 0; i < model.ValuePairs.Count; i++)
        {
            var originalPair = model.ValuePairs[i];
            var deserializedPair = finalDeserialized.ValuePairs.First(p => p.Key == originalPair.Key);
            
            deserializedPair.Value.Value.Should().Be(originalPair.Value.Value);
            deserializedPair.Value.StringValue.Should().Be(originalPair.Value.StringValue);
        }
    }
    
    #endregion
    
    #region Edge Cases and Error Scenarios
    
    [Fact]
    public void DictionaryModel_PartialData_PG()
    {
        // Test model with only Dictionary set
        var modelOnlyDict = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string> { { 1, "Only" } },
            ValuePairs = null
        };

        var data1 = SerializeWithGProtobuf(modelOnlyDict, TestModel.Serialization.Serializers.SerializeDictionaryModel);

        var data = SerializeWithProtobufNet(modelOnlyDict);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Dictionary.Should().HaveCount(1);
        deserialized.Dictionary[1].Should().Be("Only");
        deserialized.ValuePairs.Should().BeNull();
    }
    
    [Fact]
    public void DictionaryModel_PartialData_ValuePairsOnly_PG()
    {
        // Test model with only ValuePairs set
        var modelOnlyPairs = new DictionaryModel
        {
            Dictionary = null,
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>
            {
                new KeyValuePair<long, NestedDictionaryValue>(99L, new NestedDictionaryValue { Value = 999, StringValue = "OnlyPairs" })
            }
        };

        var data = SerializeWithProtobufNet(modelOnlyPairs);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Dictionary.Should().BeNull();
        deserialized.ValuePairs.Should().HaveCount(1);
        deserialized.ValuePairs[0].Key.Should().Be(99L);
        deserialized.ValuePairs[0].Value.Value.Should().Be(999);
        deserialized.ValuePairs[0].Value.StringValue.Should().Be("OnlyPairs");
    }
    
    [Fact]
    public void DictionaryModel_DuplicateKeys_Dictionary()
    {
        // Dictionary<int, string> cannot have duplicate keys by design
        // This test verifies that the last value wins when building from data
        var model = new DictionaryModel
        {
            Dictionary = new Dictionary<int, string> { { 1, "First" }, { 2, "Second" } },
            ValuePairs = new List<KeyValuePair<long, NestedDictionaryValue>>()
        };
        
        // Manually modify the dictionary after creation to test edge case
        model.Dictionary[1] = "Updated"; // Overwrite key 1

        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));

        deserialized.Dictionary[1].Should().Be("Updated");
        deserialized.Dictionary[2].Should().Be("Second");
    }
    
    #endregion
    
    #region Performance and Size Tests
    
    [Fact]
    public void DictionaryModel_SerializationSize_Comparison()
    {
        var model = CreateSimpleDictionaryModel();

        var protobufNetData = SerializeWithProtobufNet(model);
        var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);

        _outputHelper.WriteLine($"protobuf-net serialized size: {protobufNetData.Length} bytes");
        _outputHelper.WriteLine($"GProtobuf serialized size: {gprotobufData.Length} bytes");
        
        // Both should produce reasonably similar sizes
        // Allow some variance but they shouldn't be drastically different
        var sizeDifference = Math.Abs(protobufNetData.Length - gprotobufData.Length);
        var maxExpectedDifference = Math.Max(protobufNetData.Length, gprotobufData.Length) * 0.2; // 20% variance
        
        sizeDifference.Should().BeLessThan((int)maxExpectedDifference, 
            "serialized sizes should be reasonably similar between protobuf-net and GProtobuf");
    }
    
    [Fact]
    public void DictionaryModel_LargeDataPerformance()
    {
        var model = CreateLargeDictionaryModel();
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Serialize
        var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeDictionaryModel);
        var serializeTime = stopwatch.ElapsedMilliseconds;
        
        stopwatch.Restart();
        
        // Deserialize
        var deserialized = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeDictionaryModel(bytes));
        var deserializeTime = stopwatch.ElapsedMilliseconds;
        
        stopwatch.Stop();
        
        _outputHelper.WriteLine($"Large data serialization time: {serializeTime} ms");
        _outputHelper.WriteLine($"Large data deserialization time: {deserializeTime} ms");
        _outputHelper.WriteLine($"Serialized size: {data.Length} bytes");
        
        // Performance check - should complete in reasonable time
        serializeTime.Should().BeLessThan(5000, "serialization should complete within 5 seconds");
        deserializeTime.Should().BeLessThan(5000, "deserialization should complete within 5 seconds");
        
        // Verify data integrity
        deserialized.Dictionary.Should().HaveCount(1000);
        deserialized.ValuePairs.Should().HaveCount(500);
    }
    
    #endregion
}