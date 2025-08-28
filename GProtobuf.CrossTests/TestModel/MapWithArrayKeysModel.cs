using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class MapWithArrayKeysModel
    {
        [ProtoMember(1)]
        public Dictionary<int[], string> IntArrayStringMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<string[], int> StringArrayIntMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<CustomNested[], List<int>> CustomNestedArrayIntListMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<byte[], string> ByteArrayStringMap { get; set; }
        
        [ProtoMember(5)]
        public Dictionary<string, CustomNested> StringCustomNestedMap { get; set; }
        
        [ProtoMember(6)]
        public Dictionary<string, CustomNested[]> StringCustomNestedListMap { get; set; }
    }
    
    [ProtoContract]
    public class CustomNested
    {
        [ProtoMember(1)]
        public int Id { get; set; }
        
        [ProtoMember(2)]
        public string Name { get; set; }
        
        [ProtoMember(3)]
        public double Value { get; set; }
        
        public override bool Equals(object obj)
        {
            if (obj is CustomNested other)
            {
                return Id == other.Id && 
                       Name == other.Name && 
                       Value == other.Value;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, Value);
        }
    }
}