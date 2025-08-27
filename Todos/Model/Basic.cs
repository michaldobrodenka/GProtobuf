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

        //[ProtoMember(5)]
        //public DateTime DateTime { get; set; }
    }
}
