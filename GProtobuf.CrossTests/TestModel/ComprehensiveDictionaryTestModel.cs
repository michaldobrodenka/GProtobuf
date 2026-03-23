using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    #region Supporting Types

    public enum DictTestEnum
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Three = 3,
        Ten = 10,
        Hundred = 100
    }

    [ProtoContract]
    public class DictTestClass
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        [ProtoMember(3)]
        public double Value { get; set; }

        [ProtoMember(4)]
        public Guid Guid { get; set; }

        [ProtoMember(5)]
        public TimeSpan Duration { get; set; }

        [ProtoMember(6)]
        public DictTestEnum Status { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is DictTestClass other)
            {
                return Id == other.Id &&
                       Name == other.Name &&
                       Value == other.Value &&
                       Guid == other.Guid &&
                       Duration == other.Duration &&
                       Status == other.Status;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, Value, Guid, Duration, Status);
        }
    }

    #endregion

    [ProtoContract]
    public class ComprehensiveDictionaryTestModel
    {
        #region Primitive Keys with String Values

        [ProtoMember(1)]
        public Dictionary<int, string> IntStringMap { get; set; }

        [ProtoMember(2)]
        public Dictionary<long, string> LongStringMap { get; set; }

        [ProtoMember(3)]
        public Dictionary<byte, string> ByteStringMap { get; set; }

        [ProtoMember(4)]
        public Dictionary<uint, string> UIntStringMap { get; set; }

        [ProtoMember(5)]
        public Dictionary<ulong, string> ULongStringMap { get; set; }

        [ProtoMember(6)]
        public Dictionary<bool, string> BoolStringMap { get; set; }

        [ProtoMember(7)]
        public Dictionary<float, string> FloatStringMap { get; set; }

        [ProtoMember(8)]
        public Dictionary<double, string> DoubleStringMap { get; set; }

        #endregion

        #region String Keys with Primitive Values

        [ProtoMember(13)]
        public Dictionary<string, int> StringIntMap { get; set; }

        [ProtoMember(14)]
        public Dictionary<string, long> StringLongMap { get; set; }

        [ProtoMember(15)]
        public Dictionary<string, byte> StringByteMap { get; set; }

        [ProtoMember(16)]
        public Dictionary<string, uint> StringUIntMap { get; set; }

        [ProtoMember(17)]
        public Dictionary<string, ulong> StringULongMap { get; set; }

        [ProtoMember(18)]
        public Dictionary<string, bool> StringBoolMap { get; set; }

        [ProtoMember(19)]
        public Dictionary<string, float> StringFloatMap { get; set; }

        [ProtoMember(20)]
        public Dictionary<string, double> StringDoubleMap { get; set; }

        #endregion

        #region Primitive to Primitive Combinations

        [ProtoMember(25)]
        public Dictionary<int, int> IntIntMap { get; set; }

        [ProtoMember(26)]
        public Dictionary<int, long> IntLongMap { get; set; }

        [ProtoMember(27)]
        public Dictionary<long, long> LongLongMap { get; set; }

        [ProtoMember(28)]
        public Dictionary<int, double> IntDoubleMap { get; set; }

        [ProtoMember(29)]
        public Dictionary<double, double> DoubleDoubleMap { get; set; }

        [ProtoMember(30)]
        public Dictionary<bool, bool> BoolBoolMap { get; set; }

        #endregion

        #region Guid Keys and Values

        [ProtoMember(31)]
        public Dictionary<Guid, string> GuidStringMap { get; set; }

        [ProtoMember(32)]
        public Dictionary<string, Guid> StringGuidMap { get; set; }

        [ProtoMember(33)]
        public Dictionary<Guid, Guid> GuidGuidMap { get; set; }

        [ProtoMember(34)]
        public Dictionary<Guid, int> GuidIntMap { get; set; }

        [ProtoMember(35)]
        public Dictionary<int, Guid> IntGuidMap { get; set; }

        #endregion

        #region TimeSpan Keys and Values

        [ProtoMember(36)]
        public Dictionary<TimeSpan, string> TimeSpanStringMap { get; set; }

        [ProtoMember(37)]
        public Dictionary<string, TimeSpan> StringTimeSpanMap { get; set; }

        [ProtoMember(38)]
        public Dictionary<TimeSpan, TimeSpan> TimeSpanTimeSpanMap { get; set; }

        [ProtoMember(39)]
        public Dictionary<TimeSpan, int> TimeSpanIntMap { get; set; }

        [ProtoMember(40)]
        public Dictionary<int, TimeSpan> IntTimeSpanMap { get; set; }

        #endregion

        #region Enum Keys and Values

        [ProtoMember(41)]
        public Dictionary<DictTestEnum, string> EnumStringMap { get; set; }

        [ProtoMember(42)]
        public Dictionary<string, DictTestEnum> StringEnumMap { get; set; }

        [ProtoMember(43)]
        public Dictionary<DictTestEnum, DictTestEnum> EnumEnumMap { get; set; }

        [ProtoMember(44)]
        public Dictionary<DictTestEnum, int> EnumIntMap { get; set; }

        [ProtoMember(45)]
        public Dictionary<int, DictTestEnum> IntEnumMap { get; set; }

        #endregion

        #region Nullable Type Values
        // NOTE: Nullable dictionary values are not yet fully supported in V2 generator
        // Commented out until generator support is added

        // [ProtoMember(46)]
        // public Dictionary<int, int?> IntNullableIntMap { get; set; }

        // [ProtoMember(47)]
        // public Dictionary<string, int?> StringNullableIntMap { get; set; }

        // [ProtoMember(48)]
        // public Dictionary<int, double?> IntNullableDoubleMap { get; set; }

        // [ProtoMember(49)]
        // public Dictionary<string, Guid?> StringNullableGuidMap { get; set; }

        // [ProtoMember(50)]
        // public Dictionary<int, TimeSpan?> IntNullableTimeSpanMap { get; set; }

        // [ProtoMember(51)]
        // public Dictionary<string, DictTestEnum?> StringNullableEnumMap { get; set; }

        // [ProtoMember(52)]
        // public Dictionary<int, bool?> IntNullableBoolMap { get; set; }

        // [ProtoMember(53)]
        // public Dictionary<string, long?> StringNullableLongMap { get; set; }

        #endregion

        #region Collection Values - List

        [ProtoMember(54)]
        public Dictionary<string, List<int>> StringIntListMap { get; set; }

        [ProtoMember(55)]
        public Dictionary<int, List<string>> IntStringListMap { get; set; }

        [ProtoMember(56)]
        public Dictionary<string, List<double>> StringDoubleListMap { get; set; }

        [ProtoMember(57)]
        public Dictionary<int, List<Guid>> IntGuidListMap { get; set; }

        [ProtoMember(58)]
        public Dictionary<string, List<TimeSpan>> StringTimeSpanListMap { get; set; }

        [ProtoMember(59)]
        public Dictionary<int, List<DictTestEnum>> IntEnumListMap { get; set; }

        #endregion

        #region Collection Values - HashSet

        [ProtoMember(60)]
        public Dictionary<string, HashSet<int>> StringIntHashSetMap { get; set; }

        [ProtoMember(61)]
        public Dictionary<int, HashSet<string>> IntStringHashSetMap { get; set; }

        [ProtoMember(62)]
        public Dictionary<string, HashSet<double>> StringDoubleHashSetMap { get; set; }

        [ProtoMember(63)]
        public Dictionary<int, HashSet<Guid>> IntGuidHashSetMap { get; set; }

        [ProtoMember(64)]
        public Dictionary<string, HashSet<TimeSpan>> StringTimeSpanHashSetMap { get; set; }

        [ProtoMember(65)]
        public Dictionary<int, HashSet<DictTestEnum>> IntEnumHashSetMap { get; set; }

        #endregion

        #region Collection Values - Array

        [ProtoMember(66)]
        public Dictionary<string, int[]> StringIntArrayMap { get; set; }

        [ProtoMember(67)]
        public Dictionary<int, string[]> IntStringArrayMap { get; set; }

        [ProtoMember(68)]
        public Dictionary<string, byte[]> StringByteArrayMap { get; set; }

        [ProtoMember(69)]
        public Dictionary<int, double[]> IntDoubleArrayMap { get; set; }

        [ProtoMember(70)]
        public Dictionary<string, Guid[]> StringGuidArrayMap { get; set; }

        [ProtoMember(71)]
        public Dictionary<int, TimeSpan[]> IntTimeSpanArrayMap { get; set; }

        #endregion

        #region Custom Class Keys and Values

        [ProtoMember(72)]
        public Dictionary<string, DictTestClass> StringCustomClassMap { get; set; }

        [ProtoMember(73)]
        public Dictionary<int, DictTestClass> IntCustomClassMap { get; set; }

        [ProtoMember(74)]
        public Dictionary<DictTestClass, string> CustomClassStringMap { get; set; }

        [ProtoMember(75)]
        public Dictionary<DictTestClass, int> CustomClassIntMap { get; set; }

        [ProtoMember(76)]
        public Dictionary<DictTestClass, DictTestClass> CustomClassCustomClassMap { get; set; }

        [ProtoMember(77)]
        public Dictionary<string, List<DictTestClass>> StringCustomClassListMap { get; set; }

        [ProtoMember(78)]
        public Dictionary<int, HashSet<DictTestClass>> IntCustomClassHashSetMap { get; set; }

        #endregion

        #region Tuple Keys and Values

        [ProtoMember(79)]
        public Dictionary<string, Tuple<int, string>> StringTupleMap { get; set; }

        [ProtoMember(80)]
        public Dictionary<int, Tuple<string, double>> IntTupleMap { get; set; }

        [ProtoMember(81)]
        public Dictionary<Tuple<int, string>, string> TupleStringMap { get; set; }

        [ProtoMember(82)]
        public Dictionary<Tuple<int, string>, int> TupleIntMap { get; set; }

        [ProtoMember(83)]
        public Dictionary<Tuple<int, string>, Tuple<double, bool>> TupleTupleMap { get; set; }

        [ProtoMember(84)]
        public Dictionary<string, Tuple<Guid, TimeSpan, DictTestEnum>> StringComplexTupleMap { get; set; }

        #endregion

        #region Nested Dictionaries
        // NOTE: Testing nested dictionaries (Dictionary as value) support in V2 generator

        [ProtoMember(85)]
        public Dictionary<string, Dictionary<int, string>> StringToIntStringDictMap { get; set; }

        [ProtoMember(86)]
        public Dictionary<int, Dictionary<string, double>> IntToStringDoubleDictMap { get; set; }

        [ProtoMember(87)]
        public Dictionary<string, Dictionary<Guid, TimeSpan>> StringToGuidTimeSpanDictMap { get; set; }

        [ProtoMember(88)]
        public Dictionary<DictTestEnum, Dictionary<int, DictTestClass>> EnumToIntCustomClassDictMap { get; set; }

        #endregion

        #region Mixed Complex Types

        [ProtoMember(89)]
        public Dictionary<Guid, List<DictTestClass>> GuidToCustomClassListMap { get; set; }

        [ProtoMember(90)]
        public Dictionary<TimeSpan, Dictionary<string, int>> TimeSpanToDictMap { get; set; }

        [ProtoMember(91)]
        public Dictionary<DictTestEnum, HashSet<Guid>> EnumToGuidHashSetMap { get; set; }

        [ProtoMember(92)]
        public Dictionary<Tuple<int, string>, List<DictTestClass>> TupleToCustomClassListMap { get; set; }

        [ProtoMember(93)]
        public Dictionary<DictTestClass, Tuple<Guid, TimeSpan>> CustomClassToTupleMap { get; set; }

        #endregion

        #region KeyValuePair Collections

        [ProtoMember(94)]
        public List<KeyValuePair<int, string>> IntStringKVPList { get; set; }

        [ProtoMember(95)]
        public List<KeyValuePair<string, Guid>> StringGuidKVPList { get; set; }

        [ProtoMember(96)]
        public List<KeyValuePair<DictTestEnum, DictTestClass>> EnumCustomClassKVPList { get; set; }

        // NOTE: HashSet of KeyValuePair causes generator to incorrectly use indexing
        // [ProtoMember(97)]
        // public HashSet<KeyValuePair<int, string>> IntStringKVPHashSet { get; set; }

        #endregion

        #region Edge Cases

        [ProtoMember(98)]
        public Dictionary<string, string> StringStringMap { get; set; }

        [ProtoMember(99)]
        public Dictionary<Tuple<int, string>, Dictionary<Guid, List<DictTestClass>>> ComplexNestedMap { get; set; }

        [ProtoMember(100)]
        public Dictionary<byte[], string> ByteArrayStringMap { get; set; }

        [ProtoMember(101)]
        public Dictionary<string, byte[]> StringByteArrayValueMap { get; set; }

        #endregion
    }
}
