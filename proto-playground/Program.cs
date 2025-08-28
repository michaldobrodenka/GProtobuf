using ProtoBuf;

namespace proto_playground
{
    [ProtoContract]
    public class TestClass
    {
        [ProtoMember(1)]
        public Char MyChar { get; set; }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, new TestClass { MyChar = '\u03A9' });
            var data = ms.ToArray();

            Console.WriteLine("Hello, World!");
        }
    }
}
