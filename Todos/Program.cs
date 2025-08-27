
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
                DateTime = new DateTime(2024, 1, 1)
            };

            var ms = new MemoryStream();
            Todos.Model.Serialization.Serializers.SerializeBasic(ms, basic);

            var basic2 = Todos.Model.Serialization.Deserializers.DeserializeBasic(ms.ToArray());

            if (basic2.First != FirstEnum.Second) Console.WriteLine("Enum support not working");
            if (basic2.TimeSpan != TimeSpan.FromHours(1)) Console.WriteLine("TimeSpan support not working");
            if (basic2.DateTime != new DateTime(2024, 1, 1)) Console.WriteLine("DateTime support not working");
        }
    }
}
