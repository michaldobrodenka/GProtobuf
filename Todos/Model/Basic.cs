using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Todos.Model
{
    public enum FirstEnum
    {
        First = 0,
        Second = 1,
        Third = 2
    }

    [ProtoContract]
    public class CustomNested
    {
        [ProtoMember(1)]
        public int Id { get; set; }
        
        [ProtoMember(2)]
        public string Name { get; set; }
        
        [ProtoMember(3)]
        public double Score { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is CustomNested other)
            {
                return Id == other.Id && Name == other.Name && Score == other.Score;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, Score);
        }
    }

    [ProtoContract]
    public class Basic
    {
        [ProtoMember(1)]
        public FirstEnum First { get; set; }

        [ProtoMember(2)]
        public TimeSpan TimeSpan { get; set; }

        [ProtoMember(3)]
        public HashSet<string> Tags { get; set; }

        [ProtoMember(4)]
        public HashSet<int> UniqueNumbers { get; set; }

        [ProtoMember(5)]
        public Dictionary<string, HashSet<int>> StringIntHashsetMap { get; set; }
        
        [ProtoMember(6)]
        public Dictionary<int, List<string>> IntStringListMap { get; set; }
        
        [ProtoMember(7)]
        public Dictionary<string, int[]> StringIntArrayMap { get; set; }
        
        [ProtoMember(8)]
        public Dictionary<string, CustomNested> StringCustomNestedMap { get; set; }
        
        [ProtoMember(9)]
        public Dictionary<CustomNested, string> CustomNestedStringMap { get; set; }
        
        [ProtoMember(10)]
        public Dictionary<CustomNested, List<int>> CustomNestedIntListMap { get; set; }

        [ProtoMember(11)]
        public Dictionary<CustomNested[], List<int>> CustomNestedArrayIntListMap { get; set; }

        [ProtoMember(12)]
        public char MyChar { get; set; }

        //[ProtoMember(13)]
        //public Dictionary<int, Dictionary<int, int>> GroupPermissions { get; set; }

        [ProtoMember(14)]
        public Dictionary<FirstEnum, string> EnumStringMap { get; set; }

        [ProtoMember(15)]
        public Dictionary<string, FirstEnum> StringEnumMap { get; set; }

        [ProtoMember(16)]
        public Tuple<double, double> DoubleTuple { get; set; }

        [ProtoMember(17)]
        public Dictionary<int, Tuple<double, double>> ThermostatOutputMap { get; set; }

        [ProtoMember(18)]
        public Dictionary<byte, double> ByteDoubleMap { get; set; }

        [ProtoMember(19)]
        public Dictionary<float, long> FloatDoubleMap { get; set; }

        [ProtoMember(20)]
        public Dictionary<Guid, long> GuidLongMap { get; set; }

        [ProtoMember(21)]
        public HashSet<Guid> Guids { get; set; }
        // todo: List<Guid>, Dictionary<Guid, string>, HashSet<Guid> etc.
        // todo: Dictionary<int, Dictionary<int, int>>
        // Dict<byte, xxx>
    }
}
