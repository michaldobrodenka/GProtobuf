using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public enum WireType
    {
        VarInt = 0,
        Fixed64b = 1,
        Len = 2,
        Fixed32b = 5,
    }
}
