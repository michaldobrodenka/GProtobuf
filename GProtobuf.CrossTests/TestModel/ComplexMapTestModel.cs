using System.Collections.Generic;
using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for complex map types with custom classes as keys/values,
    /// nested dictionaries, and collections of dictionaries.
    /// </summary>
    // [ProtoContract] // Temporarily disabled - nested dictionary and custom class map support not fully implemented in V2
    public class ComplexMapTestModel
    {
        // Custom class as key
        [ProtoMember(1)]
        public Dictionary<MapKeyClass, string> CustomKeyMap { get; set; }

        // Custom class as value
        [ProtoMember(2)]
        public Dictionary<int, MapValueClass> CustomValueMap { get; set; }

        // Custom class as both key and value
        [ProtoMember(3)]
        public Dictionary<MapKeyClass, MapValueClass> CustomKeyValueMap { get; set; }

        // List of dictionaries as value
        [ProtoMember(4)]
        public Dictionary<string, List<Dictionary<int, string>>> NestedDictionaryMap { get; set; }

        // Nested dictionary (Dictionary as value)
        [ProtoMember(5)]
        public Dictionary<int, Dictionary<string, int>> DictionaryOfDictionary { get; set; }

        // List of custom classes as value
        [ProtoMember(6)]
        public Dictionary<int, List<MapValueClass>> ListOfCustomClassMap { get; set; }

        // Simple maps for comparison
        [ProtoMember(7)]
        public Dictionary<int, string> SimpleMap { get; set; }

        [ProtoMember(8)]
        public Dictionary<string, int> ReverseSimpleMap { get; set; }
    }

    /// <summary>
    /// Custom class used as map key.
    /// </summary>
    // [ProtoContract] // Temporarily disabled
    public class MapKeyClass
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Code { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is MapKeyClass other)
            {
                return Id == other.Id && Code == other.Code;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (Id, Code).GetHashCode();
        }
    }

    /// <summary>
    /// Custom class used as map value.
    /// </summary>
    // [ProtoContract] // Temporarily disabled
    public class MapValueClass
    {
        [ProtoMember(1)]
        public string Name { get; set; }

        [ProtoMember(2)]
        public double Value { get; set; }

        [ProtoMember(3)]
        public List<int> Tags { get; set; }
    }
}
