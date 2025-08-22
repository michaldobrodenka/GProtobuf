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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GProtobuf.Benchmark
{
    [MemoryDiagnoser(displayGenColumns: true)]
    [SimpleJob(RuntimeMoniker.Net90, warmupCount: 2, iterationCount: 3, invocationCount: 80000)]
    [RankColumn, MinColumn, MaxColumn, MeanColumn, MedianColumn]
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
                //IntValue = 12345,
                //LongValue = 987654321L,
                //DoubleValue = 3.14159,
                //BoolValue = true,
                //StringValue = "Test benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some contentTest benchmark string with some content",
                //ByteArrayValue = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
                //loatArrayValue = new List<float> { 1.1f, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5 }//new float[] { 1.1f, 2.2f, 3.3f, 4.4f, 5.5f },
                FloatArrayValue = new List<float> { 1.1f, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5 },
                DoubleArrayValue = new List<float> { 1.1f, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5, 2.2f, 3.3f, 4.4f, 5, 6, 7, 2.2f, 3.3f, 4.4f, 5 },
                //NestedModels = nestedModels
            };

            // Pre-serialize data for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(ms, TestModel);
            ProtobufNetSerializedData = ms.ToArray();
        }

        MemoryStream ms = new MemoryStream();

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

        //[Benchmark(Description = "Protobuf-net Deserialize")]
        //[BenchmarkCategory("Deserialization")]
        //public BenchmarkModel DeserializeProtobufNet()
        //{
        //    ms.Position = 0;
        //    return global::ProtoBuf.Serializer.Deserialize<BenchmarkModel>(ms);
        //}

        [Benchmark(Description = "GProtobuf Deserialize")]
        [BenchmarkCategory("Deserialization")]
        public BenchmarkModel DeserializeGProtobuf()
        {
            return Model.Serialization.Deserializers.DeserializeBenchmarkModel(ProtobufNetSerializedData);
        }

        [Benchmark(Description = "GProtobuf Custom")]
        [BenchmarkCategory("Serialization")]
        public unsafe void SerializeCustom()
        {
            ms.Position = 0;
            //CustomSerializer.Serialize(ms, TestModel);

            // 1437 ns
            const byte tag = 61;
            Span<byte> item = stackalloc byte[5];
            item[0] = tag;

            unsafe
            {
                var arr = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(TestModel.FloatArrayValue);
                fixed (byte* pItem = item)
                {
                    uint* pFloat = (uint*)(pItem + 1); // pozícia pre 4B hodnotu
                    for (int i = 0; i < TestModel.FloatArrayValue.Count; i++)
                    {
                        var value = arr[i];
                        *pFloat = *(uint*)&value;
                        //*pFloat = BitConverter.SingleToUInt32Bits(arr[i]);
                        ms.Write(item);
                    }
                }
            }
        }

        [Benchmark(Description = "GProtobuf Custom Grouped")]
        [BenchmarkCategory("Serialization")]
        public unsafe void SerializeCustomGrouped()
        {
            ms.Position = 0;
            const byte tag = 61;

            // 1 položka = 5 bajtov (1 tag + 4B float)
            Span<byte> batch = stackalloc byte[256];
            int used = 0; // počet naplnených bajtov v batchi

            unsafe
            {
                var arr = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(TestModel.FloatArrayValue);
                int n = TestModel.FloatArrayValue.Count;

                fixed (byte* pBatch = batch)
                {
                    for (int i = 0; i < n; i++)
                    {
                        // Ak sa ďalších 5 bajtov nezmestí, flushni batch
                        if (256 - used < 5)
                        {
                            ms.Write(batch.Slice(0, used));
                            used = 0;
                        }

                        var value = arr[i];

                        byte* dst = pBatch + used;
                        dst[0] = tag;

                        // Bitcast float -> uint a zapíš 4B za tag
                        *(uint*)(dst + 1) = *(uint*)&value;

                        used += 5;
                    }
                }
            }

            // doflushuj zvyšok
            if (used > 0)
            {
                ms.Write(batch.Slice(0, used));
            }
        }

        [Benchmark(Description = "GProtobuf Custom2")]
        [BenchmarkCategory("Serialization")]
        public unsafe void SerializeCustom2()
        {
            ms.Position = 0;
            //CustomSerializer.Serialize(ms, TestModel);

            //global::GProtobuf.Core.StreamWriter writer = new global::GProtobuf.Core.StreamWriter(ms);

            // 1460 ns
            var precomputedTag7 = 61u;

            Span<byte> tagBuffer = stackalloc byte[9] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            int tagVarintLength = 0;
            while (precomputedTag7 > 0x7F)
            {
                tagBuffer[tagVarintLength++] = (byte)((precomputedTag7 & 0x7F) | 0x80);
                precomputedTag7 >>= 7;
            }
            tagBuffer[tagVarintLength++] = (byte)precomputedTag7;

            var itemBuffer = tagBuffer.Slice(0, tagVarintLength + 4);
            var numberSlice = itemBuffer.Slice(tagVarintLength);
            foreach (var item in TestModel.FloatArrayValue)
            {
                var number = item;
                MemoryMarshal.Write(numberSlice, in number);
                ms.Write(itemBuffer);

            }
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
            //for (int i = 0; i < 1000000; i++)
            //    benchmark.DeserializeGProtobuf();

            //Console.WriteLine("done");
            //Console.ReadKey();

            var summary = BenchmarkRunner.Run<GProtobufBench>();
        }
    }
}
