using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    public class ByteArrayTestModel
    {
        // Basic byte array test
        [ProtoMember(1)]
        public byte[] BasicByteArray { get; set; }

        // Empty byte array
        [ProtoMember(2)]
        public byte[] EmptyByteArray { get; set; }

        // Null byte array
        [ProtoMember(3)]
        public byte[] NullByteArray { get; set; }

        // Large byte array
        [ProtoMember(4)]
        public byte[] LargeByteArray { get; set; }

        // Byte array with all possible byte values (0-255)
        [ProtoMember(5)]
        public byte[] AllPossibleBytes { get; set; }

        // Binary data simulation (common use case)
        [ProtoMember(6)]
        public byte[] BinaryData { get; set; }

        // Small byte array with edge case values
        [ProtoMember(7)]
        public byte[] EdgeCaseBytes { get; set; }

        // Note: IsPacked should NOT be used with byte[] as it doesn't apply
        // This would be incorrect: [ProtoMember(8, IsPacked = true)]
        // byte[] is always length-delimited (wire type 2)
    }
}