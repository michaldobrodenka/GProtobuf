using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for nested derived type field bug.
    ///
    /// Bug scenario:
    /// - Container has a field of type DerivedActionParams (a derived type)
    /// - When protobuf-net serializes DerivedActionParams, it wraps it with ProtoInclude wrapper
    /// - Wire format: [field tag][length] [ProtoInclude tag][wrapper length][derived fields][base fields]
    /// - GProtobuf Populate method was calling ReadXxxContent (expects direct fields)
    /// - But it should call ReadXxx (detects and handles ProtoInclude wrapper)
    /// </summary>

    // Base class for action parameters
    [ProtoContract]
    [ProtoInclude(100, typeof(DerivedActionParams))]
    public class ActionParamsBase
    {
        [ProtoMember(1)]
        public string BaseName { get; set; }

        [ProtoMember(2)]
        public int BaseValue { get; set; }
    }

    // Derived class for action parameters
    [ProtoContract]
    public class DerivedActionParams : ActionParamsBase
    {
        [ProtoMember(1)]
        public string DerivedName { get; set; }

        [ProtoMember(2)]
        public int DerivedValue { get; set; }
    }

    // Container class with a field of derived type
    [ProtoContract]
    public class TriggerContainer
    {
        [ProtoMember(1)]
        public string TriggerName { get; set; }

        [ProtoMember(2)]
        public DerivedActionParams ActionParameters { get; set; }

        [ProtoMember(3)]
        public int Priority { get; set; }
    }

    // Container with multiple derived type fields
    [ProtoContract]
    public class MultiDerivedContainer
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public DerivedActionParams Primary { get; set; }

        [ProtoMember(3)]
        public DerivedActionParams Secondary { get; set; }
    }

    // Nested container (derived type inside derived type)
    [ProtoContract]
    [ProtoInclude(100, typeof(DerivedTriggerContainer))]
    public class BaseTriggerContainer
    {
        [ProtoMember(1)]
        public string Name { get; set; }
    }

    [ProtoContract]
    public class DerivedTriggerContainer : BaseTriggerContainer
    {
        [ProtoMember(1)]
        public DerivedActionParams ActionParameters { get; set; }
    }

    // Deep nesting test
    [ProtoContract]
    public class DeepNestingContainer
    {
        [ProtoMember(1)]
        public DerivedTriggerContainer Trigger { get; set; }
    }
}
