using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.Benchmark.Model
{
    [ProtoContract]
    public class BenchmarkModel
    {
        [ProtoMember(1)]
        public int IntValue { get; set; }

        [ProtoMember(2)]
        public long LongValue { get; set; }

        [ProtoMember(3)]
        public double DoubleValue { get; set; }

        [ProtoMember(4)]
        public bool BoolValue { get; set; }

        [ProtoMember(5)]
        public string StringValue { get; set; }

        [ProtoMember(6)]
        public byte[] ByteArrayValue { get; set; }
         
        [ProtoMember(7)]
        //public float[] FloatArrayValue { get; set; }
        public List<float> FloatArrayValue { get; set; }

        [ProtoMember(8)]
        //public float[] FloatArrayValue { get; set; }
        public List<float> DoubleArrayValue { get; set; }


        [ProtoMember(9)]
        public List<NestedModel> NestedModels { get; set; }
    }


    [ProtoContract]
    public class NestedModel
    {
        [ProtoMember(1)]
        public string Name { get; set; }
        
        [ProtoMember(2)]
        public string Description { get; set; }
    }
}