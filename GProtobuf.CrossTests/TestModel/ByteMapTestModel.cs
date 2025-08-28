using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class ByteMapTestModel
    {
        // Test byte as key with various value types
        [ProtoMember(1)]
        public Dictionary<byte, string> ByteStringMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<byte, int> ByteIntMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<byte, double> ByteDoubleMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<byte, bool> ByteBoolMap { get; set; }
        
        // Test byte as value with various key types  
        [ProtoMember(5)]
        public Dictionary<string, byte> StringByteMap { get; set; }
        
        [ProtoMember(6)]
        public Dictionary<int, byte> IntByteMap { get; set; }
        
        [ProtoMember(7)]
        public Dictionary<long, byte> LongByteMap { get; set; }
        
        // Test byte as both key and value
        [ProtoMember(8)]
        public Dictionary<byte, byte> ByteByteMap { get; set; }
        
        // Test with custom class
        [ProtoMember(9)]
        public Dictionary<byte, NestedItem> ByteNestedItemMap { get; set; }
        
        [ProtoMember(10)]
        public Dictionary<NestedItem, byte> NestedItemByteMap { get; set; }
        
        // Test with collections as values
        [ProtoMember(11)]
        public Dictionary<byte, List<int>> ByteIntListMap { get; set; }
        
        [ProtoMember(12)]
        public Dictionary<byte, int[]> ByteIntArrayMap { get; set; }
        
        // Edge cases - empty and null values
        [ProtoMember(13)]
        public Dictionary<byte, string> EmptyByteStringMap { get; set; }
        
        public override bool Equals(object obj)
        {
            if (obj is not ByteMapTestModel other) return false;
            
            if (!CompareDictionary(ByteStringMap, other.ByteStringMap)) return false;
            if (!CompareDictionary(ByteIntMap, other.ByteIntMap)) return false;
            if (!CompareDictionary(ByteDoubleMap, other.ByteDoubleMap, (a, b) => Math.Abs(a - b) < 0.0001)) return false;
            if (!CompareDictionary(ByteBoolMap, other.ByteBoolMap)) return false;
            if (!CompareDictionary(StringByteMap, other.StringByteMap)) return false;
            if (!CompareDictionary(IntByteMap, other.IntByteMap)) return false;
            if (!CompareDictionary(LongByteMap, other.LongByteMap)) return false;
            if (!CompareDictionary(ByteByteMap, other.ByteByteMap)) return false;
            if (!CompareDictionary(ByteNestedItemMap, other.ByteNestedItemMap)) return false;
            if (!CompareDictionary(NestedItemByteMap, other.NestedItemByteMap)) return false;
            if (!CompareDictionaryWithCollection(ByteIntListMap, other.ByteIntListMap)) return false;
            if (!CompareDictionaryWithArray(ByteIntArrayMap, other.ByteIntArrayMap)) return false;
            if (!CompareDictionary(EmptyByteStringMap, other.EmptyByteStringMap)) return false;
            
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
        
        private static bool CompareDictionaryWithArray<TKey>(
            Dictionary<TKey, int[]> dict1,
            Dictionary<TKey, int[]> dict2)
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
        
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}