using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class GuidMapTestModel
    {
        // Test Guid as key with various value types
        [ProtoMember(1)]
        public Dictionary<Guid, string> GuidStringMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<Guid, int> GuidIntMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<Guid, double> GuidDoubleMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<Guid, bool> GuidBoolMap { get; set; }
        
        [ProtoMember(5)]
        public Dictionary<Guid, long> GuidLongMap { get; set; }
        
        // Test Guid as value with various key types  
        [ProtoMember(6)]
        public Dictionary<string, Guid> StringGuidMap { get; set; }
        
        [ProtoMember(7)]
        public Dictionary<int, Guid> IntGuidMap { get; set; }
        
        [ProtoMember(8)]
        public Dictionary<long, Guid> LongGuidMap { get; set; }
        
        // Test Guid as both key and value
        [ProtoMember(9)]
        public Dictionary<Guid, Guid> GuidGuidMap { get; set; }
        
        // Test with custom class
        [ProtoMember(10)]
        public Dictionary<Guid, NestedItem> GuidNestedItemMap { get; set; }
        
        [ProtoMember(11)]
        public Dictionary<NestedItem, Guid> NestedItemGuidMap { get; set; }
        
        // Test with collections as values
        [ProtoMember(12)]
        public Dictionary<Guid, List<int>> GuidIntListMap { get; set; }
        
        [ProtoMember(13)]
        public Dictionary<Guid, byte[]> GuidByteArrayMap { get; set; }
        
        // Edge cases - empty and null values
        [ProtoMember(14)]
        public Dictionary<Guid, string> EmptyGuidStringMap { get; set; }
        
        // List<Guid> for testing collection of Guids
        [ProtoMember(15)]
        public List<Guid> GuidList { get; set; }
        
        public override bool Equals(object obj)
        {
            if (obj is not GuidMapTestModel other) return false;
            
            if (!CompareDictionary(GuidStringMap, other.GuidStringMap)) return false;
            if (!CompareDictionary(GuidIntMap, other.GuidIntMap)) return false;
            if (!CompareDictionary(GuidDoubleMap, other.GuidDoubleMap, (a, b) => Math.Abs(a - b) < 0.0001)) return false;
            if (!CompareDictionary(GuidBoolMap, other.GuidBoolMap)) return false;
            if (!CompareDictionary(GuidLongMap, other.GuidLongMap)) return false;
            if (!CompareDictionary(StringGuidMap, other.StringGuidMap)) return false;
            if (!CompareDictionary(IntGuidMap, other.IntGuidMap)) return false;
            if (!CompareDictionary(LongGuidMap, other.LongGuidMap)) return false;
            if (!CompareDictionary(GuidGuidMap, other.GuidGuidMap)) return false;
            if (!CompareDictionary(GuidNestedItemMap, other.GuidNestedItemMap)) return false;
            if (!CompareDictionary(NestedItemGuidMap, other.NestedItemGuidMap)) return false;
            if (!CompareDictionaryWithCollection(GuidIntListMap, other.GuidIntListMap)) return false;
            if (!CompareDictionaryWithByteArray(GuidByteArrayMap, other.GuidByteArrayMap)) return false;
            if (!CompareDictionary(EmptyGuidStringMap, other.EmptyGuidStringMap)) return false;
            if (!CompareList(GuidList, other.GuidList)) return false;
            
            return true;
        }
        
        private static bool CompareDictionary<TKey, TValue>(
            Dictionary<TKey, TValue> dict1, 
            Dictionary<TKey, TValue> dict2,
            Func<TValue, TValue, bool> valueComparer = null)
        {
            if (dict1 == null && dict2 == null) return true;
            if (dict1 == null || dict2 == null) return false;
            if (dict1.Count != dict2.Count) return false;
            
            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out var value2)) return false;
                
                if (valueComparer != null)
                {
                    if (!valueComparer(kvp.Value, value2)) return false;
                }
                else
                {
                    if (!EqualityComparer<TValue>.Default.Equals(kvp.Value, value2)) return false;
                }
            }
            
            return true;
        }
        
        private static bool CompareDictionaryWithCollection<TKey>(
            Dictionary<TKey, List<int>> dict1,
            Dictionary<TKey, List<int>> dict2)
        {
            if (dict1 == null && dict2 == null) return true;
            if (dict1 == null || dict2 == null) return false;
            if (dict1.Count != dict2.Count) return false;
            
            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out var list2)) return false;
                if (kvp.Value == null && list2 == null) continue;
                if (kvp.Value == null || list2 == null) return false;
                if (!kvp.Value.SequenceEqual(list2)) return false;
            }
            
            return true;
        }
        
        private static bool CompareDictionaryWithByteArray<TKey>(
            Dictionary<TKey, byte[]> dict1,
            Dictionary<TKey, byte[]> dict2)
        {
            if (dict1 == null && dict2 == null) return true;
            if (dict1 == null || dict2 == null) return false;
            if (dict1.Count != dict2.Count) return false;
            
            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out var array2)) return false;
                if (kvp.Value == null && array2 == null) continue;
                if (kvp.Value == null || array2 == null) return false;
                if (!kvp.Value.SequenceEqual(array2)) return false;
            }
            
            return true;
        }
        
        private static bool CompareList<T>(List<T> list1, List<T> list2)
        {
            if (list1 == null && list2 == null) return true;
            if (list1 == null || list2 == null) return false;
            if (list1.Count != list2.Count) return false;
            
            for (int i = 0; i < list1.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(list1[i], list2[i])) return false;
            }
            
            return true;
        }
        
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}