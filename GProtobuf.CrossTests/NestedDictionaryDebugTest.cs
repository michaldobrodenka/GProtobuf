using Xunit;
using System.Collections.Generic;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using GProtobuf.CrossTests.TestModel.Serialization;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Ultra-minimal test for debugging exact wire format issue.
    /// </summary>
    public class NestedDictionaryDebugTest : BaseSerializationTest
    {
        [Fact]
        public void Test_OnlyTinyEntry()
        {
            // ONLY the "tiny" entry that should work
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
                {
                    { "tiny", new Dictionary<int, string> { { 1, "single" } } }
                }
            };

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print hex dump for debugging
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"TINY ONLY - Serialized bytes ({bytes.Length}): {hex}");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.Single(deserialized.StringToIntStringDictMap);
            Assert.Equal("single", deserialized.StringToIntStringDictMap["tiny"][1]);
        }

        [Fact]
        public void Test_OnlyLargeEntry_10Items()
        {
            // "large" entry with 10 items instead of 100
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>()
            };

            model.StringToIntStringDictMap["large"] = new Dictionary<int, string>();
            for (int i = 0; i < 10; i++)
            {
                model.StringToIntStringDictMap["large"][i] = $"large_value_{i}";
            }

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print hex dump for debugging
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"LARGE 10 - Serialized bytes ({bytes.Length}): {hex}");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.Single(deserialized.StringToIntStringDictMap);
            Assert.Equal(10, deserialized.StringToIntStringDictMap["large"].Count);
        }
    }
}
