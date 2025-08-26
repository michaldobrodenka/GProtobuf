using BenchmarkDotNet.Attributes;
using GProtobuf.Benchmark.Models;
using Microsoft.IO;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Benchmark.Benchmarks
{
    [MemoryDiagnoser(displayGenColumns: false)]
    [CategoriesColumn]
    public class CollectionsBenchmark
    {
        private CollectionsModel _testModel;
        private byte[] _gprotobufSerializedData;
        private byte[] _protobufNetSerializedData;
        private RecyclableMemoryStream _memoryStream;
        private static readonly RecyclableMemoryStreamManager _streamManager = new RecyclableMemoryStreamManager();

        [GlobalSetup]
        public void Setup()
        {
            _memoryStream = _streamManager.GetStream();

            var intData = Enumerable.Range(1, 100).ToList();
            var longData = Enumerable.Range(1, 100).Select(i => (long)i * 1000000).ToList();
            var floatData = Enumerable.Range(1, 100).Select(i => i * 1.5f).ToList();
            var doubleData = Enumerable.Range(1, 100).Select(i => i * 2.5).ToList();
            var stringData = Enumerable.Range(1, 50).Select(i => $"String item number {i} with some content").ToList();

            _testModel = new CollectionsModel
            {
                //IntList = intData,
                //LongList = longData,
                //FloatList = floatData,
                //DoubleList = doubleData,
                //StringList = stringData,
                //IntArray = intData.ToArray(),
                //FloatArray = floatData.ToArray(),
                //DoubleArray = doubleData.ToArray(),
                //StringArray = stringData.ToArray(),
                PackedFixedIntList = intData.Take(50).ToList(),
                //PackedZigZagIntList = intData.Select(i => i % 2 == 0 ? i : -i).ToList()
            };

            // Pre-serialize for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(_memoryStream, _testModel);
            _protobufNetSerializedData = _memoryStream.ToArray();
            _memoryStream.Position = 0;

            Models.Serialization.Serializers.SerializeCollectionsModel((Stream)_memoryStream, _testModel);
            _gprotobufSerializedData = _memoryStream.ToArray();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Serialization")]
        public void ProtobufNet_Serialize()
        {
            _memoryStream.Position = 0;
            _memoryStream.SetLength(0);
            global::ProtoBuf.Serializer.Serialize(_memoryStream, _testModel);
        }

        [Benchmark]
        [BenchmarkCategory("Serialization")]
        public void GProtobuf_Serialize()
        {
            _memoryStream.Position = 0;
            _memoryStream.SetLength(0);
            Models.Serialization.Serializers.SerializeCollectionsModel((Stream)_memoryStream, _testModel);
        }

        [Benchmark]
        [BenchmarkCategory("Deserialization")]
        public CollectionsModel ProtobufNet_Deserialize()
        {
            return global::ProtoBuf.Serializer.Deserialize<CollectionsModel>((ReadOnlySpan<byte>)_protobufNetSerializedData);
        }

        [Benchmark]
        [BenchmarkCategory("Deserialization")]
        public CollectionsModel GProtobuf_Deserialize()
        {
            return Models.Serialization.Deserializers.DeserializeCollectionsModel(_gprotobufSerializedData);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _memoryStream?.Dispose();
        }
    }
}