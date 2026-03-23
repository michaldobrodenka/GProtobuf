using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Readonly struct used as Dictionary key.
    /// This tests the fix for readonly struct deserialization in Map entries.
    /// Bug: VirtualMapEntryGenerator used Populate() for readonly structs, but Populate() is a no-op for readonly structs.
    /// Fix: Use ReadContent() instead of Populate() for readonly structs.
    /// </summary>
    [ProtoContract]
    public readonly struct ValueTypeKey : System.IEquatable<ValueTypeKey>
    {
        [ProtoMember(1)]
        public readonly uint Value;

        public ValueTypeKey(uint value)
        {
            Value = value;
        }

        public bool Equals(ValueTypeKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ValueTypeKey other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public override string ToString() => $"ValueTypeKey({Value})";
    }

    /// <summary>
    /// Description for a value type.
    /// </summary>
    [ProtoContract]
    public class ValueTypeDescription
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public int MinPeriod { get; set; }

        [ProtoMember(3)]
        public bool IsEnabled { get; set; }
    }

    /// <summary>
    /// Model with Dictionary where key is readonly struct.
    /// </summary>
    [ProtoContract]
    public class ReadonlyStructKeyModel
    {
        /// <summary>
        /// Dictionary with readonly struct as key.
        /// Tests: VirtualMapEntryGenerator fix for readonly struct keys.
        /// </summary>
        [ProtoMember(1)]
        public Dictionary<ValueTypeKey, ValueTypeDescription> Descriptions { get; set; }

        /// <summary>
        /// Dictionary with readonly struct as value.
        /// Tests: VirtualMapEntryGenerator fix for readonly struct values.
        /// </summary>
        [ProtoMember(2)]
        public Dictionary<string, ValueTypeKey> ValuesByName { get; set; }

        /// <summary>
        /// List of readonly struct.
        /// Tests: readonly struct in collection deserialization.
        /// </summary>
        [ProtoMember(3)]
        public List<ValueTypeKey> ValueTypeKeys { get; set; }

        /// <summary>
        /// HashSet of readonly struct.
        /// Tests: readonly struct in HashSet.
        /// </summary>
        [ProtoMember(4)]
        public HashSet<ValueTypeKey> ValueTypeKeyHashSet { get; set; }

        /// <summary>
        /// Nested Dictionary with readonly struct keys.
        /// Tests: complex nested structure with readonly struct.
        /// </summary>
        [ProtoMember(5)]
        public Dictionary<ValueTypeKey, List<int>> CommandsByValueType { get; set; }
    }
}
