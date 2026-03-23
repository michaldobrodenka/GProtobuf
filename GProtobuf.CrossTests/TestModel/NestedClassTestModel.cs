using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model demonstrating nested classes with ProtoContract.
    /// Outer class contains a nested inner class, both serializable.
    /// </summary>
    [ProtoContract]
    public class OuterClass
    {
        [ProtoMember(1)]
        public int OuterId { get; set; }

        [ProtoMember(2)]
        public string OuterName { get; set; }

        [ProtoMember(3)]
        public InnerClass Inner { get; set; }

        /// <summary>
        /// Nested class inside OuterClass with its own ProtoContract.
        /// </summary>
        [ProtoContract]
        public class InnerClass
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public byte ByteValue { get; set; }
        }
    }

    /// <summary>
    /// Test model with multiple levels of nesting.
    /// </summary>
    [ProtoContract]
    public class Level1Class
    {
        [ProtoMember(1)]
        public int Level1Id { get; set; }

        [ProtoMember(2)]
        public Level2Class Level2 { get; set; }

        [ProtoContract]
        public class Level2Class
        {
            [ProtoMember(1)]
            public int Level2Id { get; set; }

            [ProtoMember(2)]
            public Level3Class Level3 { get; set; }

            [ProtoContract]
            public class Level3Class
            {
                [ProtoMember(1)]
                public int Level3Id { get; set; }

                [ProtoMember(2)]
                public string Value { get; set; }
            }
        }
    }

    /// <summary>
    /// Test model with multiple nested classes at the same level.
    /// </summary>
    [ProtoContract]
    public class ContainerClass
    {
        [ProtoMember(1)]
        public int ContainerId { get; set; }

        [ProtoMember(2)]
        public NestedA A { get; set; }

        [ProtoMember(3)]
        public NestedB B { get; set; }

        [ProtoContract]
        public class NestedA
        {
            [ProtoMember(1)]
            public int ValueA { get; set; }
        }

        [ProtoContract]
        public class NestedB
        {
            [ProtoMember(1)]
            public string ValueB { get; set; }

            [ProtoMember(2)]
            public double NumberB { get; set; }
        }
    }
}
