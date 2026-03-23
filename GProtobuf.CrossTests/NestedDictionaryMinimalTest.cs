using Xunit;
using System.Collections.Generic;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using GProtobuf.CrossTests.TestModel.Serialization;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Minimal test for debugging nested dictionary wire format issues.
    /// </summary>
    public class NestedDictionaryMinimalTest : BaseSerializationTest
    {
        [Fact]
        public void Test_SingleEntry_StringToIntString()
        {
            // MINIMAL: 1 outer entry, 1 inner entry
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
                {
                    { "key1", new Dictionary<int, string> { { 100, "value1" } } }
                }
            };

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print hex dump for debugging
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"Serialized bytes ({bytes.Length}): {hex}");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.Single(deserialized.StringToIntStringDictMap);
            Assert.True(deserialized.StringToIntStringDictMap.ContainsKey("key1"));
            Assert.Single(deserialized.StringToIntStringDictMap["key1"]);
            Assert.Equal("value1", deserialized.StringToIntStringDictMap["key1"][100]);
        }

        [Fact]
        public void Test_SingleEntry_IntToStringDouble()
        {
            // MINIMAL: 1 outer entry, 1 inner entry - THIS SHOULD FAIL IF THERE'S A BUG
            var model = new NestedDictionaryTestModel
            {
                IntToStringDoubleDictMap = new Dictionary<int, Dictionary<string, double>>
                {
                    { 1, new Dictionary<string, double> { { "pi", 3.14 } } }
                }
            };

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print hex dump for debugging
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"Serialized bytes ({bytes.Length}): {hex}");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.IntToStringDoubleDictMap);
            Assert.Single(deserialized.IntToStringDoubleDictMap);
            Assert.True(deserialized.IntToStringDoubleDictMap.ContainsKey(1));
            Assert.Single(deserialized.IntToStringDoubleDictMap[1]);
            Assert.Equal(3.14, deserialized.IntToStringDoubleDictMap[1]["pi"], 5);
        }

        [Fact]
        public void Test_BothFields()
        {
            // Test both fields together
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
                {
                    { "key1", new Dictionary<int, string> { { 100, "value1" } } }
                },
                IntToStringDoubleDictMap = new Dictionary<int, Dictionary<string, double>>
                {
                    { 1, new Dictionary<string, double> { { "pi", 3.14 } } }
                }
            };

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print hex dump for debugging
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"Serialized bytes ({bytes.Length}): {hex}");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.NotNull(deserialized.IntToStringDoubleDictMap);
            Assert.Single(deserialized.StringToIntStringDictMap);
            Assert.Single(deserialized.IntToStringDoubleDictMap);
        }
    }
}
