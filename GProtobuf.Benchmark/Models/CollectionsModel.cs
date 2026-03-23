using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.Benchmark.Models
{
    [ProtoContract]
    public class CollectionsModel
    {
        [ProtoMember(1)]
        public List<int> IntList { get; set; }

        [ProtoMember(2)]
        public List<long> LongList { get; set; }

        [ProtoMember(3)]
        public List<float> FloatList { get; set; }

        [ProtoMember(4)]
        public List<double> DoubleList { get; set; }

        [ProtoMember(5)]
        public List<string> StringList { get; set; }

        [ProtoMember(6)]
        public int[] IntArray { get; set; }

        [ProtoMember(7)]
        public float[] FloatArray { get; set; }

        [ProtoMember(8)]
        public double[] DoubleArray { get; set; }

        [ProtoMember(9)]
        public string[] StringArray { get; set; }

        [ProtoMember(10, DataFormat = DataFormat.FixedSize, IsPacked = true)]
        public List<int> PackedFixedIntList { get; set; }

        [ProtoMember(11, DataFormat = DataFormat.ZigZag)]
        public List<int> PackedZigZagIntList { get; set; }
    }
}