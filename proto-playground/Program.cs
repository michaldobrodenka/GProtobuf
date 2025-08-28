using ProtoBuf;

namespace proto_playground
{
    [ProtoContract]
    public class TestClass
    {
        //[ProtoMember(1)]
        ////public Char MyChar { get; set; }
        //public Guid Guid { get; set; }

        [ProtoMember(1)]
        public Dictionary<byte, byte> DataDict { get; set; }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            using var ms = new MemoryStream();
            //Serializer.Serialize(ms, new TestClass { MyChar = '\u03A9' });


            //Serializer.Serialize(ms, new TestClass { Guid = new Guid( new byte[]{ 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16 })});
            Serializer.Serialize(ms, new TestClass { DataDict = new (){ { 0, 1 } } });
            var data = ms.ToArray();

            Console.WriteLine("Hello, World!");
        }
    }
}
