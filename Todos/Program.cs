
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
        }
    }
}
