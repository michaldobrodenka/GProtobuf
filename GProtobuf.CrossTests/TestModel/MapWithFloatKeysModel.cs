using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class MapWithFloatKeysModel
    {
        [ProtoMember(1)]
        public Dictionary<float, string> FloatStringMap { get; set; }
        
        [ProtoMember(2)]
        public Dictionary<double, int> DoubleIntMap { get; set; }
        
        [ProtoMember(3)]
        public Dictionary<string, float> StringFloatMap { get; set; }
        
        [ProtoMember(4)]
        public Dictionary<int, double> IntDoubleMap { get; set; }
    }
}