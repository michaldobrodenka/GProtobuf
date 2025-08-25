using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Tests.TestModel
{
    [ProtoContract]
    [ProtoInclude(5, typeof(B))]
    public class A
    {
        [ProtoMember(1)]
        public string StringA { get; set; }
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(C))]
    public class B : A
    {
        [ProtoMember(1)]
        public string StringB { get; set; }
    }

    [ProtoContract]
    public class C : B
    {
        [ProtoMember(1)]
        public string StringC { get; set; }
    }

}
