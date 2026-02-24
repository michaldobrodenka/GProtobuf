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

    // ============================================================
    // IftttConnection-like test models
    // Tests: Dictionary with ProtoInclude class as KEY
    // ============================================================

    /// <summary>
    /// Similar to DeviceValueAggregation - base class used as dictionary key
    /// </summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(AggregationKeyExtended))]
    public class AggregationKey
    {
        [ProtoMember(1)]
        public int Function { get; set; }

        [ProtoMember(2)]
        public int PeriodType { get; set; }

        [ProtoMember(3)]
        public int Period { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is AggregationKey other)
                return Function == other.Function && PeriodType == other.PeriodType && Period == other.Period;
            return false;
        }

        public override int GetHashCode()
        {
            return (Function << 16) | (PeriodType << 8) | Period;
        }
    }

    /// <summary>
    /// Extended version - derived class
    /// </summary>
    [ProtoContract]
    public class AggregationKeyExtended : AggregationKey
    {
        [ProtoMember(1)]
        public int Usage { get; set; }

        [ProtoMember(2)]
        public int EnabledOn { get; set; }
    }

    /// <summary>
    /// Similar to IftttAggregatedConnection
    /// Dictionary<uint, Dictionary<AggregationKey, HashSet<int>>>
    /// </summary>
    [ProtoContract]
    public class AggregatedConnection
    {
        [ProtoMember(1)]
        public System.Collections.Generic.Dictionary<uint, System.Collections.Generic.Dictionary<AggregationKey, System.Collections.Generic.HashSet<int>>> Connections { get; set; }

        [ProtoMember(2)]
        public int ConnectionType { get; set; }
    }

    /// <summary>
    /// Simpler version - Dictionary with derived class as VALUE (not key)
    /// </summary>
    [ProtoContract]
    public class MapWithDerivedValue
    {
        [ProtoMember(1)]
        public System.Collections.Generic.Dictionary<int, DerivedActionParams> Items { get; set; }
    }

    /// <summary>
    /// Dictionary with derived class as KEY
    /// </summary>
    [ProtoContract]
    public class MapWithDerivedKey
    {
        [ProtoMember(1)]
        public System.Collections.Generic.Dictionary<AggregationKey, int> Items { get; set; }
    }

    /// <summary>
    /// Nested dictionary: outer value is another dictionary
    /// </summary>
    [ProtoContract]
    public class NestedMapContainer
    {
        [ProtoMember(1)]
        public System.Collections.Generic.Dictionary<uint, System.Collections.Generic.Dictionary<int, string>> NestedMap { get; set; }
    }

    /// <summary>
    /// Full IftttConnection-like structure
    /// </summary>
    [ProtoContract]
    public class AgregateConnectionTestModel
    {
        [ProtoMember(1)]
        public AggregatedConnection AggregatedConnections { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }
    }

    // ============================================================
    // ConcurrentDictionary tests
    // ============================================================

    /// <summary>
    /// Simple ConcurrentDictionary
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapSimple
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<int, string> Items { get; set; }
    }

    /// <summary>
    /// ConcurrentDictionary with derived type as value
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithDerivedValue
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<int, DerivedActionParams> Items { get; set; }
    }

    /// <summary>
    /// ConcurrentDictionary with derived type as key
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithDerivedKey
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, int> Items { get; set; }
    }

    /// <summary>
    /// Nested ConcurrentDictionary - outer value is another ConcurrentDictionary
    /// </summary>
    [ProtoContract]
    public class NestedConcurrentMap
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, string>> NestedMap { get; set; }
    }

    /// <summary>
    /// Mixed: ConcurrentDictionary with Dictionary inside
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithDictionaryValue
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Generic.Dictionary<int, string>> Items { get; set; }
    }

    /// <summary>
    /// Mixed: Dictionary with ConcurrentDictionary inside
    /// </summary>
    [ProtoContract]
    public class DictionaryWithConcurrentMapValue
    {
        [ProtoMember(1)]
        public System.Collections.Generic.Dictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, string>> Items { get; set; }
    }

    /// <summary>
    /// Full complex: ConcurrentDictionary<uint, ConcurrentDictionary<AggregationKey, HashSet<int>>>
    /// Similar to IftttAggregatedConnection but with ConcurrentDictionary
    /// </summary>
    [ProtoContract]
    public class ConcurrentAggregatedConnection
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Generic.HashSet<int>>> Connections { get; set; }

        [ProtoMember(2)]
        public int ConnectionType { get; set; }
    }

    /// <summary>
    /// ConcurrentDictionary with List value
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithListValue
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.List<DerivedActionParams>> Items { get; set; }
    }

    // ============================================================
    // Deep nested ConcurrentDictionary tests (3+ levels)
    // ============================================================

    /// <summary>
    /// Test enum for nested dictionary keys
    /// </summary>
    public enum ConnectionStatus
    {
        Unknown = 0,
        Connected = 1,
        Disconnected = 2,
        Pending = 3
    }

    /// <summary>
    /// 3-level nested: ConcurrentDictionary<uint, ConcurrentDictionary<int, ConcurrentDictionary<ConnectionStatus, HashSet&lt;int&gt;>>>
    /// </summary>
    [ProtoContract]
    public class DeepNestedConcurrentMap
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.HashSet<int>>>> Data { get; set; }
    }

    /// <summary>
    /// 3-level with enum key: ConcurrentDictionary<ConnectionStatus, ConcurrentDictionary<int, HashSet&lt;int&gt;>>
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithEnumKey
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>> Items { get; set; }
    }

    /// <summary>
    /// Mixed: ConcurrentDictionary<uint, ConcurrentDictionary<ConnectionStatus, List&lt;ConnectionStatus&gt;>>
    /// </summary>
    [ProtoContract]
    public class ConcurrentMapWithEnumArray
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<ConnectionStatus, System.Collections.Generic.List<ConnectionStatus>>> Items { get; set; }
    }

    /// <summary>
    /// Similar to IftttAggregatedConnection but deeper:
    /// ConcurrentDictionary<uint, ConcurrentDictionary<AggregationKey, ConcurrentDictionary<int, HashSet&lt;int&gt;>>>
    /// </summary>
    [ProtoContract]
    public class DeepAggregatedConnection
    {
        [ProtoMember(1)]
        public System.Collections.Concurrent.ConcurrentDictionary<uint, System.Collections.Concurrent.ConcurrentDictionary<AggregationKey, System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Generic.HashSet<int>>>> Connections { get; set; }

        [ProtoMember(2)]
        public int ConnectionType { get; set; }
    }
}
