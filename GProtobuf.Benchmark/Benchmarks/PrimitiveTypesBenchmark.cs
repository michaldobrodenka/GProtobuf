using BenchmarkDotNet.Attributes;
using GProtobuf.Benchmark.Models;
using Microsoft.IO;
using System;

namespace GProtobuf.Benchmark.Benchmarks
{
    [MemoryDiagnoser(displayGenColumns: false)]
    [CategoriesColumn]
    public class PrimitiveTypesBenchmark
    {
        private PrimitiveTypesModel _testModel;
        private byte[] _gprotobufSerializedData;
        private byte[] _protobufNetSerializedData;
        private RecyclableMemoryStream _memoryStream;
        private static readonly RecyclableMemoryStreamManager _streamManager = new RecyclableMemoryStreamManager();

        [GlobalSetup]
        public void Setup()
        {
            _memoryStream = _streamManager.GetStream();

            _testModel = new PrimitiveTypesModel
            {
                IntValue = 12345,
                LongValue = 9876543210L,
                FloatValue = 3.14159f,
                DoubleValue = 2.71828,
                BoolValue = true,
                StringValue = "Benchmark test string with some reasonable length for testing purposes",
                ByteArrayValue = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
                FixedIntValue = -12345,
                FixedLongValue = -9876543210L,
                ZigZagIntValue = -12345,
                ZigZagLongValue = -9876543210L
            };

            // Pre-serialize for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(_memoryStream, _testModel);
            _protobufNetSerializedData = _memoryStream.ToArray();
            _memoryStream.Position = 0;

            Models.Serialization.Serializers.SerializePrimitiveTypesModel(_memoryStream, _testModel);
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
            Models.Serialization.Serializers.SerializePrimitiveTypesModel(_memoryStream, _testModel);
        }

        [Benchmark]
        [BenchmarkCategory("Deserialization")]
        public PrimitiveTypesModel ProtobufNet_Deserialize()
        {
            return global::ProtoBuf.Serializer.Deserialize<PrimitiveTypesModel>((ReadOnlySpan<byte>)_protobufNetSerializedData);
        }

        [Benchmark]
        [BenchmarkCategory("Deserialization")]
        public PrimitiveTypesModel GProtobuf_Deserialize()
        {
            return Models.Serialization.Deserializers.DeserializePrimitiveTypesModel(_gprotobufSerializedData);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _memoryStream?.Dispose();
        }
    }
}