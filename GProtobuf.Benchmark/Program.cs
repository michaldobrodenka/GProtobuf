using BenchmarkDotNet.Running;
using ProtoBuf;
using ProtoBuf.Serializers;
using Model;
using BenchmarkDotNet.Attributes;
using System.Runtime.InteropServices;
using System.Buffers.Binary;
using System.Reflection;

namespace GProtobuf.Benchmark
{
    [MemoryDiagnoser]
    //[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 8, invocationCount: 8)]
    public unsafe class GProtbufBench
    {
        public byte[] SerializedData;
        public MemoryStream ms;

        ClassWithCollections Model2;

        public GProtbufBench()
        {
            Model2 = new ClassWithCollections
            {
                //SomeInt = 4567,
                //Bytes = new byte[] { 1, 2, 3, 4 },
                //PackedInts = new[] { 1, 2, 55 },
                PackedFixedSizeInts = new[] { -2, 17 },
                //NonPackedFixedSizeInts = new[] { -2, 17 },
                //NonPackedInts = new[] { -2, 17 },
            };

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
                    NonPackedFixedSizeInts = new[] { -2, 17 },
                    NonPackedInts = new[] { -2, 17 },
                }
            };

            ms = new MemoryStream();
            global::ProtoBuf.Serializer.Serialize(ms, Model2);

            SerializedData = ms.ToArray();


            using var ms1 = new MemoryStream(SerializedData);
            global::ProtoBuf.Serializer.Deserialize<ClassWithCollections> (ms1);
        }

        [Benchmark]
        public void DeserializeP()
        {
            ms.Position = 0;

            var result = global::ProtoBuf.Serializer.Deserialize<ClassWithCollections>(ms);
        }

        [Benchmark]
        public void DeserializeG()
        {
            var result = Model.Serialization.Deserializers.DeserializeClassWithCollections(this.SerializedData);
        }

        //[Benchmark]
        //public void SerializeP()
        //{
        //    ms.Position = 0;

        //    global::ProtoBuf.Serializer.Serialize(ms, this.Model2);
        //}

        //[Benchmark]
        //public void SerializeG()
        //{
        //    ms.Position = 0;

        //    Model.Serialization.Serializers.SerializeClassWithCollections(ms, this.Model2);
        //}


        //[Benchmark]
        //public void MemoryMarshalWrite()
        //{
        //    int x = 12345;
        //    ms.Position = 0;

        //    ms.Write(MemoryMarshal.Cast<int, byte>(MemoryMarshal.CreateReadOnlySpan(ref x, 1)));
        //}

        //[Benchmark]
        //public unsafe void UnsafeWrite()
        //{
        //    int x = 12345;
        //    ms.Position = 0;

        //    ms.Write(new Span<byte>(&x, 4));
        //}

        //[Benchmark]
        //public void MemoryStackAlloc()
        //{
        //    ms.Position = 0;
        //    int x = 12345;
        //    Span<byte> buffer = stackalloc byte[4];

        //    BinaryPrimitives.WriteInt32BigEndian(buffer, x);

        //    ms.Write(buffer);
        //}
    }


    internal class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run(typeof(Program).Assembly);
        }
    }
}
