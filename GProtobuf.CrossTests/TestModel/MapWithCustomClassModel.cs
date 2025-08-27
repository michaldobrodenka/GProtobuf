using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class NestedItem
    {
        [ProtoMember(1)]
        public int Id { get; set; }
        
        [ProtoMember(2)]
        public string Name { get; set; }
        
        [ProtoMember(3)]
        public double Value { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is NestedItem other)
            {
                return Id == other.Id && Name == other.Name && Value == other.Value;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, Value);
        }
    }

    [ProtoContract]
    public class MapWithCustomClassModel
    {
        [ProtoMember(1)]
        public Dictionary<string, NestedItem> StringNestedItemMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<NestedItem, string> NestedItemStringMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<NestedItem, List<int>> NestedItemIntListMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<int, NestedItem> IntNestedItemMap { get; set; }
        
        [ProtoMember(5)]
        public Dictionary<NestedItem, HashSet<string>> NestedItemStringHashSetMap { get; set; }
    }
}