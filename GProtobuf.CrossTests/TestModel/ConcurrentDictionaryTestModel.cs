using ProtoBuf;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for ConcurrentDictionary scenarios.
    /// Tests the StreamReader handling of ConcurrentDictionary types.
    /// </summary>
    [ProtoContract]
    public class ConcurrentDictionaryTestModel
    {
        [ProtoMember(1)]
        public ConcurrentDictionary<int, string> IntStringConcurrentMap { get; set; }

        [ProtoMember(2)]
        public ConcurrentDictionary<string, int> StringIntConcurrentMap { get; set; }

        [ProtoMember(3)]
        public ConcurrentDictionary<int, double> IntDoubleConcurrentMap { get; set; }

        [ProtoMember(4)]
        public ConcurrentDictionary<long, string> LongStringConcurrentMap { get; set; }

        [ProtoMember(5)]
        public ConcurrentDictionary<string, bool> StringBoolConcurrentMap { get; set; }
    }

    /// <summary>
    /// Test model for ConcurrentDictionary with collection values.
    /// </summary>
    [ProtoContract]
    public class ConcurrentDictionaryWithCollectionsModel
    {
        [ProtoMember(1)]
        public ConcurrentDictionary<int, HashSet<int>> IntHashSetConcurrentMap { get; set; }

        [ProtoMember(2)]
        public ConcurrentDictionary<string, List<string>> StringListConcurrentMap { get; set; }

        [ProtoMember(3)]
        public ConcurrentDictionary<int, int[]> IntArrayConcurrentMap { get; set; }
    }

    /// <summary>
    /// Test model for nested ConcurrentDictionary scenarios.
    /// </summary>
    [ProtoContract]
    public class NestedConcurrentDictionaryModel
    {
        [ProtoMember(1)]
        public ConcurrentDictionary<int, ConcurrentDictionary<string, int>> NestedConcurrentMap { get; set; }

        [ProtoMember(2)]
        public Dictionary<string, ConcurrentDictionary<int, string>> MixedNestedMap { get; set; }
    }

    /// <summary>
    /// Test model for ConcurrentDictionary with enum keys.
    /// </summary>
    [ProtoContract]
    public class ConcurrentDictionaryEnumKeyModel
    {
        [ProtoMember(1)]
        public ConcurrentDictionary<ConnectionStatus, string> EnumKeyMap { get; set; }

        [ProtoMember(2)]
        public ConcurrentDictionary<ConnectionStatus, List<ConnectionStatus>> EnumKeyCollectionValueMap { get; set; }
    }

    /// <summary>
    /// Test model combining various dictionary types.
    /// </summary>
    [ProtoContract]
    public class MixedDictionaryTypesModel
    {
        [ProtoMember(1)]
        public Dictionary<int, string> StandardDictionary { get; set; }

        [ProtoMember(2)]
        public ConcurrentDictionary<int, string> ConcurrentDictionary { get; set; }

        [ProtoMember(3)]
        public Dictionary<string, ConcurrentDictionary<int, double>> NestedMixed { get; set; }
    }
}
