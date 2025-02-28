using BenchmarkDotNet.Running;
using ProtoBuf;
using ProtoBuf.Serializers;
using Model;
using BenchmarkDotNet.Attributes;

namespace GProtobuf.Benchmark
{
    [MemoryDiagnoser]
    //[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 8, invocationCount: 8)]
    public class GProtbufBench
    {
        public byte[] SerializedData;
        public MemoryStream ms;

        public GProtbufBench()
        {

            ModelClass model = new ModelClass()
            {
                //A = 3.4,//new byte[] { 1, 2, 3, 4 },//"long.MaxValue",
                //B = -3,
                //Str = "toto je string",
                //D = 12345
                A = 3.4,//new byte[] { 1, 2, 3, 4 },//"long.MaxValue",
                B = -3,
                Str = "toto je string",
                D = 12345,
                Model2 = new ClassWithCollections
                { 
                    SomeInt = 4567,
                    Bytes = new byte[] { 1, 2, 3, 4 },
                    PackedInts = new[] { 1, 2, 55 },
                    PackedFixedSizeInts = new[] { -2, 17 },
                }
            };

            ms = new MemoryStream();
            global::ProtoBuf.Serializer.Serialize(ms, model);

            SerializedData = ms.ToArray();


            using var ms1 = new MemoryStream(SerializedData);
            global::ProtoBuf.Serializer.Deserialize<ModelClass>(ms1);
        }

        [Benchmark]
        public void DeserializeP()
        {
            ms.Position = 0;

            var result = global::ProtoBuf.Serializer.Deserialize<ModelClass>(ms);
        }

        [Benchmark]
        public void DeserializeG()
        {
            var result = Model.Serialization.Deserializers.DeserializeModelClassBase(this.SerializedData);
        }
    }


    internal class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
