using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Tests.TestModel
{
    public class DerivedDictionary : Dictionary<long, string>
    { 
    
    }

    [ProtoContract]
    public class NestedDictionaryValue
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
        public string StringValue { get; set; }
    }


    [ProtoContract]
    public class DictionaryModel
    {
        [ProtoMember(1)]
        public Dictionary<int, string> Dictionary { get; set; }

        [ProtoMember(2)]
        public List<KeyValuePair<long, NestedDictionaryValue>> ValuePairs { get; set; }

        [ProtoMember(3)]
        public DerivedDictionary DerivedDictionary { get; set; }
    }
}
