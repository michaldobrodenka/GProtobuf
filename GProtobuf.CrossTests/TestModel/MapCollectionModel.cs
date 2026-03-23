using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class MapCollectionModel
    {
        [ProtoMember(1)]
        public Dictionary<string, HashSet<int>> StringIntHashsetMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<int, List<string>> IntStringListMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<string, int[]> StringIntArrayMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<string, HashSet<string>> StringStringHashsetMap { get; set; }
        
        [ProtoMember(5)]
        public Dictionary<int, double[]> IntDoubleArrayMap { get; set; }
    }
}