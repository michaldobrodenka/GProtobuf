using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class HashSetTestModel
    {
        [ProtoMember(1)]
        public HashSet<int> UniqueNumbers { get; set; }

        [ProtoMember(2)]
        public HashSet<string> UniqueTags { get; set; }

        [ProtoMember(3)]
        public HashSet<SimpleMessage> UniqueMessages { get; set; }

        [ProtoMember(4, IsPacked = true)]
        public HashSet<float> PackedFloats { get; set; }

        [ProtoMember(5)]
        public HashSet<byte> ByteSet { get; set; }

        [ProtoMember(6, DataFormat = DataFormat.ZigZag)]
        public HashSet<int> ZigZagIntSet { get; set; }

        [ProtoMember(7, DataFormat = DataFormat.FixedSize)]
        public HashSet<int> FixedSizeIntSet { get; set; }
    }

    [ProtoContract]
    public class SimpleMessage
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is SimpleMessage other)
            {
                return Id == other.Id && Name == other.Name;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name);
        }
    }
}