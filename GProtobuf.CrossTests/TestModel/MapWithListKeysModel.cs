using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class MapWithListKeysModel
    {
        [ProtoMember(1)]
        public Dictionary<List<int>, string> IntListStringMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<List<string>, int> StringListIntMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<List<CustomNested>, string> CustomNestedListStringMap { get; set; }
    }
}