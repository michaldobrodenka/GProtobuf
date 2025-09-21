namespace GProtobuf.Generator;

// Local WireType enum matching GProtobuf.Core.WireType
internal enum WireType
{
    VarInt = 0,
    Fixed64b = 1,
    Len = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32b = 5
}