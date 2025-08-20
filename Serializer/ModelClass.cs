﻿using ProtoBuf;

namespace Model;


[ProtoContract]
[ProtoInclude(5, typeof(B))]
public class A
{
    [ProtoMember(1)]
    public string StringA { get; set; }
}

[ProtoContract]
[ProtoInclude(10,typeof(C))]
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

[ProtoContract]
public class A1
{
    [ProtoMember(5)]
    public B1 B1 { get; set; }

    [ProtoMember(1)]
    public string StringA { get; set; }
}

[ProtoContract]
public class B1
{
    [ProtoMember(10)]
    public C1 C1 { get; set; }

    [ProtoMember(1)]
    public string StringB { get; set; }
}

[ProtoContract]
public class C1
{
    [ProtoMember(1)]
    public string StringC { get; set; }
}

[ProtoContract]
[ProtoInclude(1, typeof(ModelClass))]
[ProtoInclude(2, typeof(SecondModelClass))]
public abstract class ModelClassBase
{
    [ProtoMember(3122, DataFormat = DataFormat.FixedSize)]
    //public byte[] A { get; set; }
    public double A { get; set; }

    [ProtoMember(201)]
    public int B { get; set; }


    [ProtoMember(1234568)]
    public string Str { get; set; }
}

[ProtoContract]
public class ModelClass : ModelClassBase
{
    [ProtoMember(1)]
    public int D { get; set; }

    [ProtoMember(2)]
    public ClassWithCollections Model2 { get; set; }
}

[ProtoContract]
public class SecondModelClass : ModelClassBase;

[ProtoContract]
public class ClassWithCollections
{
    [ProtoMember(1)]
    public int SomeInt { get; set; }

    //[ProtoMember(1)]
    //public List<int>? List { get; set; }

    //[ProtoMember(2, DataFormat = DataFormat.FixedSize, IsPacked = true)]
    //public List<int>? PackedList { get; set; }

    //[ProtoMember(3)]
    //public List<double> Doubles { get; set; }

    //[ProtoMember(4)]
    //public List<string> Strings { get; set; }

    //[ProtoMember(5)]
    //public List<KeyValuePair<int,string>> KeyValuePairs { get; set; }

    [ProtoMember(6)]
    public byte[] Bytes { get; set; }

    [ProtoMember(7, IsPacked = true)]
    public int[] PackedInts { get; set; }

    [ProtoMember(8, IsPacked = true, DataFormat = DataFormat.FixedSize)]
    public int[] PackedFixedSizeInts { get; set; }

    [ProtoMember(9)]
    public int[] NonPackedInts { get; set; }

    [ProtoMember(10, DataFormat = DataFormat.FixedSize)]
    public int[] NonPackedFixedSizeInts { get; set; }

    // todo: zigzag
}