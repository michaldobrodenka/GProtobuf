using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    /// <summary>
    /// Test model for recursion depth limits.
    /// Used to verify Level200 requirement: max recursion depth = 100.
    /// </summary>
    [ProtoContract]
    public class RecursiveNode
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public RecursiveNode Child { get; set; }
    }

    /// <summary>
    /// Test model with multiple recursive fields.
    /// </summary>
    [ProtoContract]
    public class TreeNode
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public TreeNode Left { get; set; }

        [ProtoMember(3)]
        public TreeNode Right { get; set; }
    }
}
