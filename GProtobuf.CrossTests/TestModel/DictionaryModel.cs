using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Tests.TestModel
{
    public enum TestEnum
    {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3
    }

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

        [ProtoMember(4)]
        public Dictionary<char, string> CharKeyDictionary { get; set; }

        [ProtoMember(5)]
        public Dictionary<int, char> CharValueDictionary { get; set; }

        [ProtoMember(6)]
        public Dictionary<char, char> CharCharDictionary { get; set; }

        [ProtoMember(7)]
        public Dictionary<TestEnum, string> EnumKeyDictionary { get; set; }

        [ProtoMember(8)]
        public Dictionary<string, TestEnum> EnumValueDictionary { get; set; }

        [ProtoMember(9)]
        public Dictionary<TestEnum, TestEnum> EnumEnumDictionary { get; set; }
    }
}
