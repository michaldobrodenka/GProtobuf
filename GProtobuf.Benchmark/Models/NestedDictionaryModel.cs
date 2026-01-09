using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.Benchmark.Models
{
    /// <summary>
    /// Model for benchmarking nested dictionary serialization/deserialization.
    /// This tests the performance of Dictionary<K, Dictionary<K2, V2>> which is a complex scenario.
    /// </summary>
    [ProtoContract]
    public class NestedDictionaryModel
    {
        /// <summary>
        /// Nested dictionary: string -> (int -> string)
        /// </summary>
        [ProtoMember(1)]
        public Dictionary<string, Dictionary<int, string>> StringToIntStringMap { get; set; }

        /// <summary>
        /// Nested dictionary: int -> (string -> double)
        /// </summary>
        [ProtoMember(2)]
        public Dictionary<int, Dictionary<string, double>> IntToStringDoubleMap { get; set; }

        /// <summary>
        /// Nested dictionary: string -> (Guid -> TimeSpan)
        /// Custom value types to test complex serialization
        /// </summary>
        [ProtoMember(3)]
        public Dictionary<string, Dictionary<Guid, TimeSpan>> StringToGuidTimeSpanMap { get; set; }
    }
}
