using ProtoBuf;

namespace GProtobuf.CrossTests.Refactored;

[ProtoContract]
[ProtoInclude(5, typeof(B))]
public class A
{
    [ProtoMember(1)]
    public string StringA { get; set; } = string.Empty;
}

[ProtoContract]
[ProtoInclude(10, typeof(C))]
public class B : A
{
    [ProtoMember(1)]
    public string StringB { get; set; } = string.Empty;
}

[ProtoContract]
[ProtoInclude(14, typeof(D))]
[ProtoInclude(15, typeof(E))] // TODO it seems there is an error when tag > 15
public class C : B
{
    [ProtoMember(1)]
    public string StringC { get; set; } = string.Empty;
}

[ProtoContract]
public class D : C
{
    [ProtoMember(1)]
    public string StringD { get; set; } = string.Empty;
}

[ProtoContract]
public class E : C
{
    [ProtoMember(1)]
    public string StringE { get; set; } = string.Empty;
}