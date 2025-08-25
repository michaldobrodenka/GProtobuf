using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class ArrayTypesModel
    {
        // Int array tests - non-packed
        [ProtoMember(1)]
        public int[] IntArrayNonPacked { get; set; }

        [ProtoMember(2, DataFormat = DataFormat.ZigZag)]
        public int[] IntArrayNonPackedZigZag { get; set; }

        [ProtoMember(3, DataFormat = DataFormat.FixedSize)]
        public int[] IntArrayNonPackedFixed { get; set; }

        // Int array tests - packed
        [ProtoMember(4, IsPacked = true)]
        public int[] IntArrayPacked { get; set; }

        [ProtoMember(5, IsPacked = true, DataFormat = DataFormat.ZigZag)]
        public int[] IntArrayPackedZigZag { get; set; }

        [ProtoMember(6, IsPacked = true, DataFormat = DataFormat.FixedSize)]
        public int[] IntArrayPackedFixed { get; set; }

        // Byte array tests (byte[] is treated specially, not like other arrays)
        [ProtoMember(7)]
        public byte[] ByteArray { get; set; }

        // Additional int arrays for edge cases
        [ProtoMember(8)]
        public int[] IntArrayEmpty { get; set; }

        [ProtoMember(9)]
        public int[] IntArrayNull { get; set; }

        [ProtoMember(10, IsPacked = true)]
        public int[] IntArrayPackedEmpty { get; set; }

        [ProtoMember(11, IsPacked = true)]
        public int[] IntArrayPackedNull { get; set; }

        // Large arrays for performance testing
        [ProtoMember(12, IsPacked = true)]
        public int[] IntArrayPackedLarge { get; set; }

        [ProtoMember(13)]
        public int[] IntArrayNonPackedLarge { get; set; }
    }
}