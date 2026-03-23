using ProtoBuf;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test enum for map keys
    /// </summary>
    public enum ValueLogType
    {
        None = 0,
        Temperature = 1,
        Humidity = 2,
        Pressure = 3,
        Power = 4
    }

    /// <summary>
    /// Test model for nested dictionary with float values.
    /// Tests: Dictionary<enum, ConcurrentDictionary<int, float>>
    ///
    /// This reproduces the issue where entryWireType == 6 (invalid wireType)
    /// is encountered when reading nested map entries.
    /// </summary>
    [ProtoContract]
    public class NestedMapWithFloatModel
    {
        /// <summary>
        /// Simple map: int -> float
        /// Expected wire format for value: Fixed32 (wireType 5)
        /// </summary>
        [ProtoMember(1)]
        public Dictionary<int, float> SimpleIntFloatMap { get; set; }

        /// <summary>
        /// Nested map: enum -> Dictionary<int, float>
        /// This is a nested dictionary structure that should be:
        /// - Outer entry: [length][field1: enum as VarInt][field2: inner dict as Len]
        /// - Inner entry: [length][field1: int as VarInt][field2: float as Fixed32]
        /// </summary>
        [ProtoMember(2)]
        public Dictionary<ValueLogType, Dictionary<int, float>> EnumToIntFloatNestedMap { get; set; }

        /// <summary>
        /// Same as above but using ConcurrentDictionary
        /// protobuf-net may serialize this differently than regular Dictionary
        /// </summary>
        [ProtoMember(3)]
        public Dictionary<ValueLogType, ConcurrentDictionary<int, float>> EnumToConcurrentIntFloatNestedMap { get; set; }

        /// <summary>
        /// Simple map: string -> double for comparison
        /// Expected wire format for value: Fixed64 (wireType 1)
        /// </summary>
        [ProtoMember(4)]
        public Dictionary<string, double> SimpleStringDoubleMap { get; set; }
    }
}
