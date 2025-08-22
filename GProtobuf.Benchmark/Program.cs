using BenchmarkDotNet.Running;
using BenchmarkDotNet.Attributes;
using GProtobuf.Benchmark.Model;

namespace GProtobuf.Benchmark
{
    [MemoryDiagnoser]
    public class GProtobufBench
    {
        public byte[] GProtobufSerializedData;
        public byte[] ProtobufNetSerializedData;
        public BenchmarkModel TestModel;

        [GlobalSetup]
        public void Setup()
        {
            var nestedModels = new List<NestedModel>();
            nestedModels.Add(new NestedModel() { Name = "Name1", Description = "Description1" });
            nestedModels.Add(new NestedModel() { Name = "Name2", Description = "Description2" });

            TestModel = new BenchmarkModel
            {
                IntValue = 12345,
                LongValue = 987654321L,
                DoubleValue = 3.14159,
                BoolValue = true,
                StringValue = "Test benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some content",
                //ByteArrayValue = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                FloatArrayValue = new float[] { 1.1f, 2.2f, 3.3f, 4.4f, 5.5f },
                //NestedModels = nestedModels
            };

            // Pre-serialize data for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(ms, TestModel);
            ProtobufNetSerializedData = ms.ToArray();
        }

        MemoryStream ms = new MemoryStream();

        [Benchmark(Baseline = true)]
        public void SerializeProtobufNet()
        {
            ms.Position = 0;
            global::ProtoBuf.Serializer.Serialize(ms, TestModel);
        }

        [Benchmark]
        public void SerializeGProtobuf()
        {
            ms.Position = 0;
            Model.Serialization.Serializers.SerializeBenchmarkModel(ms, TestModel);
        }

        [Benchmark]
        public BenchmarkModel DeserializeProtobufNet()
        {
            ms.Position = 0;
            return global::ProtoBuf.Serializer.Deserialize<BenchmarkModel>(ms);
        }

        [Benchmark]
        public BenchmarkModel DeserializeGProtobuf()
        {
            return Model.Serialization.Deserializers.DeserializeBenchmarkModel(ProtobufNetSerializedData);
        }
    }


    internal class Program
    {
        static void Main(string[] args)
        {
            //var benchmark = new GProtobufBench();
            //benchmark.Setup();
            //benchmark.DeserializeGProtobuf();

            var summary = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
