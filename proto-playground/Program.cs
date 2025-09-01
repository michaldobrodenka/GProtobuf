using ProtoBuf;

namespace proto_playground
{
    [ProtoContract]
    public class TestClass
    {
        [ProtoMember(1)]//, CompatibilityLevel(CompatibilityLevel.Level200)]
        public DateTime DateTime { get; set; }
    }

    [ProtoContract]
    public class DateTime2
    {
        [ProtoMember(1, DataFormat = DataFormat.ZigZag)]
        public long LongValue { get; set; }

        [ProtoMember(2)]
        public int Scale { get; set; }

        [ProtoMember(3)]
        public int Kind { get; set; }
    }

    [ProtoContract]
    public class TestClass2
    {
        [ProtoMember(1)]
        public DateTime2 DateTime { get; set; }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            using var ms = new MemoryStream();

            //Serializer.Serialize(ms, new TestClass { DateTime = new DateTime(2025, 1,10) });
            Serializer.Serialize(ms, new TestClass { DateTime = new DateTime(1) });

            var data = ms.ToArray();

            var deserialized = Serializer.Deserialize<TestClass2>(new MemoryStream(data));

            Console.WriteLine("Hello, World!");
        }
    }
}
