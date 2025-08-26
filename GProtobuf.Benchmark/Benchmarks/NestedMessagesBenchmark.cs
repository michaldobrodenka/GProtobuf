using BenchmarkDotNet.Attributes;
using CommunityToolkit.HighPerformance.Buffers;
using GProtobuf.Benchmark.Models;
using Microsoft.IO;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Benchmark.Benchmarks
{
    [MemoryDiagnoser(displayGenColumns: true)]
    [CategoriesColumn]
    public class NestedMessagesBenchmark
    {
        private NestedMessagesModel _testModel;
        private byte[] _gprotobufSerializedData;
        private byte[] _protobufNetSerializedData;
        private RecyclableMemoryStream _memoryStream;
        private static readonly RecyclableMemoryStreamManager _streamManager = new RecyclableMemoryStreamManager();
        private static ArrayPoolBufferWriter<byte> arrayPoolBufferWriter = new ArrayPoolBufferWriter<byte>(1024);


        [GlobalSetup]
        public void Setup()
        {
            _memoryStream = _streamManager.GetStream();

            var addresses = Enumerable.Range(1, 5).Select(i => new AddressModel
            {
                Street = $"{100 + i} Main Street",
                City = $"City {i}",
                State = $"State {i}",
                ZipCode = $"1234{i}",
                Country = "USA"
            }).ToList();

            var people = Enumerable.Range(1, 10).Select(i => new PersonModel
            {
                FirstName = $"FirstName{i}",
                LastName = $"LastName{i}",
                Age = 20 + i,
                Email = $"person{i}@example.com",
                Address = addresses[i % addresses.Count],
                PhoneNumbers = new List<string> { $"555-000{i}", $"555-111{i}" }
            }).ToList();

            _testModel = new NestedMessagesModel
            {
                //Value = -1,
                StringField1 = "1",
                StringField2 = "Test String 2",
                StringField3 = "Test String 3",
                StringField4 = "Test String 4",
                StringField5 = "Test String 5",
                Person = people[0],
                People = people,
                Address = addresses[0],
                Addresses = addresses,
                Company = new CompanyModel
                {
                    Name = "Tech Corp Inc.",
                    HeadquartersAddress = addresses[0],
                    Employees = people,
                    Offices = addresses,
                    FoundedYear = 2020
                }
            };

            // Pre-serialize for deserialization benchmarks
            global::ProtoBuf.Serializer.Serialize(_memoryStream, _testModel);
            _protobufNetSerializedData = _memoryStream.ToArray();
            _memoryStream.Position = 0;

            Models.Serialization.Serializers.SerializeNestedMessagesModel((Stream)_memoryStream, _testModel);
            _gprotobufSerializedData = _memoryStream.ToArray();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Serialization")]
        public void ProtobufNet_Serialize()
        {
            _memoryStream.Position = 0;
            //_memoryStream.SetLength(0);
            global::ProtoBuf.Serializer.Serialize(_memoryStream, _testModel);
        }

        [Benchmark]
        [BenchmarkCategory("Serialization")]
        public void GProtobuf_Serialize_Stream()
        {
            _memoryStream.Position = 0;
            _memoryStream.SetLength(0);
            Models.Serialization.Serializers.SerializeNestedMessagesModel((Stream)_memoryStream, _testModel);
            //streamResult = _memoryStream.ToArray();
        }

        //private byte[] streamResult;
        //private byte[] bufferResult;


        [Benchmark]
        [BenchmarkCategory("Serialization")]
        public void GProtobuf_Serialize_IBufferWriter()
        {
            //_memoryStream.Position = 0;
            //_memoryStream.SetLength(0);
            arrayPoolBufferWriter.Clear();
            Models.Serialization.Serializers.SerializeNestedMessagesModel((IBufferWriter<byte>)arrayPoolBufferWriter, _testModel);
            var mem = arrayPoolBufferWriter.WrittenMemory;
            //bufferResult = arrayPoolBufferWriter.WrittenSpan.ToArray();
        }

        [Benchmark]
        [BenchmarkCategory("Serialization")]
        public void GProtobuf_SerializeCustom()
        {
            _memoryStream.Position = 0;
            //_memoryStream.SetLength(0);
            Models.Serialization.Serializers2.SerializeNestedMessagesModel(_memoryStream, _testModel);
        }


        //[Benchmark]
        //[BenchmarkCategory("Deserialization")]
        //public NestedMessagesModel ProtobufNet_Deserialize()
        //{
        //    return global::ProtoBuf.Serializer.Deserialize<NestedMessagesModel>((ReadOnlySpan<byte>)_protobufNetSerializedData);
        //}

        //[Benchmark]
        //[BenchmarkCategory("Deserialization")]
        //public NestedMessagesModel GProtobuf_Deserialize()
        //{
        //    return Models.Serialization.Deserializers.DeserializeNestedMessagesModel(_gprotobufSerializedData);
        //}

        [GlobalCleanup]
        public void Cleanup()
        {
            _memoryStream?.Dispose();
        }
    }
}