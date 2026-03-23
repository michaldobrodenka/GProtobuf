using System.Collections.Generic;
using ProtoBuf;

namespace GProtobuf.CrossTests.TestModel
{
    [ProtoContract]
    public class ShortMapTestModel
    {
        // Short as key
        [ProtoMember(1)]
        public Dictionary<short, string> ShortStringMap { get; set; }

        [ProtoMember(2)]
        public Dictionary<short, int> ShortIntMap { get; set; }

        // Short as value
        [ProtoMember(3)]
        public Dictionary<string, short> StringShortMap { get; set; }

        [ProtoMember(4)]
        public Dictionary<int, short> IntShortMap { get; set; }

        // Ushort as key
        [ProtoMember(5)]
        public Dictionary<ushort, string> UShortStringMap { get; set; }

        [ProtoMember(6)]
        public Dictionary<ushort, int> UShortIntMap { get; set; }

        // Ushort as value
        [ProtoMember(7)]
        public Dictionary<string, ushort> StringUShortMap { get; set; }

        [ProtoMember(8)]
        public Dictionary<int, ushort> IntUShortMap { get; set; }

        // Sbyte as key
        [ProtoMember(9)]
        public Dictionary<sbyte, string> SByteStringMap { get; set; }

        [ProtoMember(10)]
        public Dictionary<sbyte, int> SByteIntMap { get; set; }

        // Sbyte as value
        [ProtoMember(11)]
        public Dictionary<string, sbyte> StringSByteMap { get; set; }

        [ProtoMember(12)]
        public Dictionary<int, sbyte> IntSByteMap { get; set; }

        // Mixed combinations
        [ProtoMember(13)]
        public Dictionary<short, ushort> ShortUShortMap { get; set; }

        [ProtoMember(14)]
        public Dictionary<sbyte, short> SByteShortMap { get; set; }
    }
}
