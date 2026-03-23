using ProtoBuf;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Minimal test model specifically for nested dictionary testing.
    /// Isolated from ComprehensiveDictionaryTestModel to avoid conflicts with other fields.
    /// </summary>
    [ProtoContract]
    public class NestedDictionaryTestModel
    {
        [ProtoMember(1)]
        public Dictionary<string, Dictionary<int, string>> StringToIntStringDictMap { get; set; }

        [ProtoMember(2)]
        public Dictionary<int, Dictionary<string, double>> IntToStringDoubleDictMap { get; set; }
    }
}
