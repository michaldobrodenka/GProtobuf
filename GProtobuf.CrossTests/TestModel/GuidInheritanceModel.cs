using ProtoBuf;
using System;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model to verify Guid serialization in inheritance hierarchy.
    /// This tests the scenario where a base class has a Guid field and
    /// derived classes are serialized with ProtoInclude.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(101, typeof(DerivedWithGuid))]
    public class BaseWithGuid
    {
        [ProtoMember(1)]
        public Guid Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }
    }

    [ProtoContract]
    public class DerivedWithGuid : BaseWithGuid
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public string Description { get; set; }
    }

    /// <summary>
    /// Container model that holds a list of BaseWithGuid items
    /// (similar to DashboardModel with SectionModels).
    /// </summary>
    [ProtoContract]
    public class ContainerWithGuidItems
    {
        [ProtoMember(1)]
        public BaseWithGuid[] Items { get; set; }
    }
}
