
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
                UniqueNumbers = new HashSet<int> { 1, 2, 3, 4, 5 }
            };

            var ms = new MemoryStream();
            Todos.Model.Serialization.Serializers.SerializeBasic(ms, basic);

            var basic2 = Todos.Model.Serialization.Deserializers.DeserializeBasic(ms.ToArray());

            if (basic2.First != FirstEnum.Second) Console.WriteLine("Enum support not working");
            if (basic2.TimeSpan != TimeSpan.FromHours(1)) Console.WriteLine("TimeSpan support not working");
            if (!basic2.Tags.SetEquals(new HashSet<string> { "tag1", "tag2", "tag3" })) Console.WriteLine("HashSet<string> support not working");
            if (!basic2.UniqueNumbers.SetEquals(new HashSet<int> { 1, 2, 3, 4, 5 })) Console.WriteLine("HashSet<int> support not working");
        }
    }
}
