using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using GProtobuf.Benchmark.Model;
using Microsoft.IO;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GProtobuf.Benchmark
{
    [MemoryDiagnoser(displayGenColumns: true)]
    [SimpleJob(RuntimeMoniker.Net90, warmupCount: 2, iterationCount: 3, invocationCount: 8000)]
    [RankColumn, MinColumn, MaxColumn, MeanColumn, MedianColumn]
    public class GProtobufBench
    {
        public byte[] GProtobufSerializedData;
        public byte[] ProtobufNetSerializedData;
        public BenchmarkModel TestModel;

        [GlobalSetup]
        public void Setup()
        {
            ms = manager.GetStream();

            var nestedModels = new List<NestedModel>();
            for (int i = 0; i < 200; i++)
            {
                nestedModels.Add(new NestedModel() { Name = "Name1", Description = "Description1" });
                nestedModels.Add(new NestedModel() { Name = "Name2", Description = "Description2" });
            }

            TestModel = new BenchmarkModel
            {
                IntValue = 12345,
                LongValue = 987654321L,
                DoubleValue = 3.14159,
                BoolValue = true,
                StringValue = "Test benchmark string with some content",
                ByteArrayValue = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10 },                
                FloatArrayValue = new List<float> { 1.1f, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5 },
                DoubleArrayValue = new List<float> { 1.1f, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5 },
                NestedModels = nestedModels
            };

            // Pre-serialize data for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(ms, TestModel);
            ProtobufNetSerializedData = ms.ToArray();
        }

        private static readonly RecyclableMemoryStreamManager manager = new RecyclableMemoryStreamManager();

        RecyclableMemoryStream ms;

        [Benchmark(Baseline = true, Description = "Protobuf-net Serialize")]
        [BenchmarkCategory("Serialization")]
        public void SerializeProtobufNet()
        {
            ms.Position = 0;
            global::ProtoBuf.Serializer.Serialize(ms, TestModel);
        }

        [Benchmark(Description = "GProtobuf Serialize")]
        [BenchmarkCategory("Serialization")]
        public void SerializeGProtobuf()
        {
            ms.Position = 0;
            Model.Serialization.Serializers.SerializeBenchmarkModel(ms, TestModel);
        }
        }

        internal class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine("any key to start");
            //Console.ReadKey();

            //var benchmark = new GProtobufBench();
            //benchmark.Setup();
            //for (int i = 0; i < 1000; i++)
            //    benchmark.SerializeGProtobuf();

            //Console.WriteLine("done");
            //Console.ReadKey();

            var summary = BenchmarkRunner.Run<GProtobufBench>();
        }
    }
}
