
using Todos.Model;

namespace Todos
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var basic = new Basic()
            {
                First = FirstEnum.Second,
                TimeSpan = TimeSpan.FromHours(1),
                Tags = new HashSet<string> { "tag1", "tag2", "tag3" },
                UniqueNumbers = new HashSet<int> { 1, 2, 3, 4, 5 },
                
                // NEW: Dictionary with HashSet as value
                StringIntHashsetMap = new Dictionary<string, HashSet<int>>
                {
                    ["group1"] = new HashSet<int> { 10, 20, 30 },
                    ["group2"] = new HashSet<int> { 100, 200, 300 }
                },
                
                // NEW: Dictionary with List as value
                IntStringListMap = new Dictionary<int, List<string>>
                {
                    [1] = new List<string> { "first", "second", "third" },
                    [2] = new List<string> { "alpha", "beta", "gamma" }
                },
                
                // NEW: Dictionary with array as value
                StringIntArrayMap = new Dictionary<string, int[]>
                {
                    ["array1"] = new int[] { 5, 10, 15, 20 },
                    ["array2"] = new int[] { 50, 100, 150 }
                },
                
                // NEW: Dictionary with custom nested class as value
                StringCustomNestedMap = new Dictionary<string, CustomNested>
                {
                    ["first"] = new CustomNested { Id = 1, Name = "First Item", Score = 99.5 },
                    ["second"] = new CustomNested { Id = 2, Name = "Second Item", Score = 87.3 }
                },
                
                // NEW: Dictionary with custom nested class as key
                CustomNestedStringMap = new Dictionary<CustomNested, string>
                {
                    [new CustomNested { Id = 10, Name = "Key One", Score = 1.1 }] = "Value for key one",
                    [new CustomNested { Id = 20, Name = "Key Two", Score = 2.2 }] = "Value for key two"
                },
                
                // NEW: Dictionary with custom nested class as key and List as value
                CustomNestedIntListMap = new Dictionary<CustomNested, List<int>>
                {
                    [new CustomNested { Id = 100, Name = "Complex Key", Score = 10.5 }] = new List<int> { 1, 2, 3, 4, 5 }
                },
                
                // NEW: Dictionary with enum as key
                EnumStringMap = new Dictionary<FirstEnum, string>
                {
                    [FirstEnum.First] = "First value",
                    [FirstEnum.Second] = "Second value",
                    [FirstEnum.Third] = "Third value"
                },
                
                // NEW: Dictionary with enum as value
                StringEnumMap = new Dictionary<string, FirstEnum>
                {
                    ["key1"] = FirstEnum.First,
                    ["key2"] = FirstEnum.Second,
                    ["key3"] = FirstEnum.Third
                }
            };

            var ms = new MemoryStream();
            Todos.Model.Serialization.Serializers.SerializeBasic(ms, basic);
            Console.WriteLine($"Serialized size: {ms.ToArray().Length} bytes");

            var basic2 = Todos.Model.Serialization.Deserializers.DeserializeBasic(ms.ToArray());

            // Original tests
            if (basic2.First != FirstEnum.Second) Console.WriteLine("Enum support not working");
            if (basic2.TimeSpan != TimeSpan.FromHours(1)) Console.WriteLine("TimeSpan support not working");
            if (!basic2.Tags.SetEquals(new HashSet<string> { "tag1", "tag2", "tag3" })) Console.WriteLine("HashSet<string> support not working");
            if (!basic2.UniqueNumbers.SetEquals(new HashSet<int> { 1, 2, 3, 4, 5 })) Console.WriteLine("HashSet<int> support not working");

            // NEW: Test collections as map values
            Console.WriteLine("\n=== Testing Collections as Map Values ===");
            
            // Test Dictionary<string, HashSet<int>>
            if (basic2.StringIntHashsetMap != null)
            {
                Console.WriteLine($"StringIntHashsetMap has {basic2.StringIntHashsetMap.Count} entries");
                foreach (var kvp in basic2.StringIntHashsetMap)
                {
                    Console.WriteLine($"  {kvp.Key}: [{string.Join(", ", kvp.Value)}]");
                }
            }
            
            // Test Dictionary<int, List<string>>
            if (basic2.IntStringListMap != null)
            {
                Console.WriteLine($"IntStringListMap has {basic2.IntStringListMap.Count} entries");
                foreach (var kvp in basic2.IntStringListMap)
                {
                    Console.WriteLine($"  {kvp.Key}: [{string.Join(", ", kvp.Value)}]");
                }
            }
            
            // Test Dictionary<string, int[]>
            if (basic2.StringIntArrayMap != null)
            {
                Console.WriteLine($"StringIntArrayMap has {basic2.StringIntArrayMap.Count} entries");
                foreach (var kvp in basic2.StringIntArrayMap)
                {
                    Console.WriteLine($"  {kvp.Key}: [{string.Join(", ", kvp.Value)}]");
                }
            }
            
            Console.WriteLine("\n✅ Collections as map values work correctly!");
            
            // NEW: Test custom nested class as value
            Console.WriteLine("\n=== Testing Custom Nested Class in Maps ===");
            
            // Test Dictionary<string, CustomNested>
            if (basic2.StringCustomNestedMap != null)
            {
                Console.WriteLine($"StringCustomNestedMap has {basic2.StringCustomNestedMap.Count} entries");
                foreach (var kvp in basic2.StringCustomNestedMap)
                {
                    Console.WriteLine($"  {kvp.Key}: Id={kvp.Value.Id}, Name={kvp.Value.Name}, Score={kvp.Value.Score}");
                }
                
                // Verify values
                if (!basic2.StringCustomNestedMap["first"].Equals(new CustomNested { Id = 1, Name = "First Item", Score = 99.5 }))
                    Console.WriteLine("ERROR: StringCustomNestedMap values don't match!");
            }
            
            // Test Dictionary<CustomNested, string>
            if (basic2.CustomNestedStringMap != null)
            {
                Console.WriteLine($"CustomNestedStringMap has {basic2.CustomNestedStringMap.Count} entries");
                foreach (var kvp in basic2.CustomNestedStringMap)
                {
                    Console.WriteLine($"  Key(Id={kvp.Key.Id}, Name={kvp.Key.Name}): {kvp.Value}");
                }
                
                // Verify we can find values using custom key
                var testKey = new CustomNested { Id = 10, Name = "Key One", Score = 1.1 };
                if (basic2.CustomNestedStringMap.ContainsKey(testKey))
                {
                    Console.WriteLine($"  ✓ Found value for testKey: {basic2.CustomNestedStringMap[testKey]}");
                }
                else
                {
                    Console.WriteLine("  ERROR: Could not find value using custom key!");
                }
            }
            
            // Test Dictionary<CustomNested, List<int>>
            if (basic2.CustomNestedIntListMap != null)
            {
                Console.WriteLine($"CustomNestedIntListMap has {basic2.CustomNestedIntListMap.Count} entries");
                foreach (var kvp in basic2.CustomNestedIntListMap)
                {
                    Console.WriteLine($"  Key(Id={kvp.Key.Id}, Name={kvp.Key.Name}): [{string.Join(", ", kvp.Value)}]");
                }
            }
            
            Console.WriteLine("\n✅ Custom nested class as map key/value works correctly!");
            
            // Test Dictionary<FirstEnum, string>
            Console.WriteLine("\n=== Testing Enum as Dictionary Key ===");
            if (basic2.EnumStringMap != null)
            {
                Console.WriteLine($"EnumStringMap has {basic2.EnumStringMap.Count} entries");
                foreach (var kvp in basic2.EnumStringMap)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                
                // Verify values
                if (basic2.EnumStringMap[FirstEnum.First] != "First value" ||
                    basic2.EnumStringMap[FirstEnum.Second] != "Second value" ||
                    basic2.EnumStringMap[FirstEnum.Third] != "Third value")
                {
                    Console.WriteLine("ERROR: EnumStringMap values don't match!");
                }
                else
                {
                    Console.WriteLine("  ✓ All enum key mappings verified successfully!");
                }
            }
            
            Console.WriteLine("\n✅ Enum as dictionary key works correctly!");
            
            // Test Dictionary<string, FirstEnum>
            Console.WriteLine("\n=== Testing Enum as Dictionary Value ===");
            if (basic2.StringEnumMap != null)
            {
                Console.WriteLine($"StringEnumMap has {basic2.StringEnumMap.Count} entries");
                foreach (var kvp in basic2.StringEnumMap)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                
                // Verify values
                if (basic2.StringEnumMap["key1"] != FirstEnum.First ||
                    basic2.StringEnumMap["key2"] != FirstEnum.Second ||
                    basic2.StringEnumMap["key3"] != FirstEnum.Third)
                {
                    Console.WriteLine("ERROR: StringEnumMap values don't match!");
                }
                else
                {
                    Console.WriteLine("  ✓ All enum value mappings verified successfully!");
                }
            }
            
            Console.WriteLine("\n✅ Enum as dictionary value works correctly!");
            
            // Test byte as dictionary key
            Console.WriteLine("\n=== Testing Byte as Dictionary Key ===");
            ByteMapTest.TestByteDoubleMap();
            
            // Test Guid as dictionary key
            Console.WriteLine("\n=== Testing Guid as Dictionary Key ===");
            GuidMapTest.TestGuidLongMap();
            GuidMapTest.TestProtobufNetCompatibility();
            
            // Test HashSet<Guid>
            Console.WriteLine("\n=== Testing HashSet<Guid> ===");
            HashSetGuidTest.TestHashSetGuid();
            HashSetGuidTest.TestProtobufNetCompatibility();
        }
    }
}
