using Xunit;
using System.Collections.Generic;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using GProtobuf.CrossTests.TestModel.Serialization;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Boundary test to identify the exact difference between 18 (working) and 19 (failing) items.
    /// </summary>
    public class NestedDictionaryBoundaryTest : BaseSerializationTest
    {
        [Fact]
        public void Test_18_Items_Working()
        {
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
                {
                    { "outer_0", new Dictionary<int, string> { { 0, "value_0_0" }, { 100, "value_0_1" } } },
                    { "outer_1", new Dictionary<int, string> { { 0, "value_1_0" }, { 100, "value_1_1" } } }
                }
            };

            // Add large entry with 18 items
            model.StringToIntStringDictMap["large"] = new Dictionary<int, string>();
            for (int i = 0; i < 18; i++)
            {
                model.StringToIntStringDictMap["large"][i] = $"large_value_{i}";
            }

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print FULL hex dump
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"\n=== 18 ITEMS (WORKING) ===");
            System.Console.WriteLine($"Total bytes: {bytes.Length}");
            System.Console.WriteLine($"Hex: {hex}");
            System.Console.WriteLine($"=========================\n");

            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.Equal(3, deserialized.StringToIntStringDictMap.Count);
            Assert.Equal(18, deserialized.StringToIntStringDictMap["large"].Count);
        }

        [Fact]
        public void Test_19_Items_Failing()
        {
            var model = new NestedDictionaryTestModel
            {
                StringToIntStringDictMap = new Dictionary<string, Dictionary<int, string>>
                {
                    { "outer_0", new Dictionary<int, string> { { 0, "value_0_0" }, { 100, "value_0_1" } } },
                    { "outer_1", new Dictionary<int, string> { { 0, "value_1_0" }, { 100, "value_1_1" } } }
                }
            };

            // Add large entry with 19 items
            model.StringToIntStringDictMap["large"] = new Dictionary<int, string>();
            for (int i = 0; i < 19; i++)
            {
                model.StringToIntStringDictMap["large"][i] = $"large_value_{i}";
            }

            var bytes = SerializeWithGProtobuf(model, Serializers.SerializeNestedDictionaryTestModel);

            // Print FULL hex dump
            var hex = System.BitConverter.ToString(bytes).Replace("-", " ");
            System.Console.WriteLine($"\n=== 19 ITEMS (FAILING) ===");
            System.Console.WriteLine($"Total bytes: {bytes.Length}");
            System.Console.WriteLine($"Hex: {hex}");
            System.Console.WriteLine($"=========================\n");

            // This will fail with "Unknown WireType: 7"
            var deserialized = DeserializeWithGProtobuf(bytes, b => Deserializers.DeserializeNestedDictionaryTestModel(b));

            Assert.NotNull(deserialized.StringToIntStringDictMap);
            Assert.Equal(3, deserialized.StringToIntStringDictMap.Count);
            Assert.Equal(19, deserialized.StringToIntStringDictMap["large"].Count);
        }
    }
}
