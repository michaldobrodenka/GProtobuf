// See https://aka.ms/new-console-template for more information
using GProtobuf.Core;
using Model;
using ProtoBuf;
using ProtoBuf.Serializers;
//using Serializer;
using System;
using System.Net.Http.Headers;

Console.WriteLine("Hello, World!");

ModelClass model = new ModelClass()
{
    A = 3.4,//new byte[] { 1, 2, 3, 4 },//"long.MaxValue",
    B = -3,
    Str = "toto je string",
    D = 12345,
    Model2 = new ClassWithCollections
    {
        //SomeInt = -1,//12345,
        //Bytes = new byte[] { 1, 2, 3, 4 },
        //PackedInts = new[] { -1, 15 },
        //PackedFixedSizeInts = new[] { -2, 17 },
        //NonPackedInts = new[] { 5, 6, 7, 8 },
        NonPackedFixedSizeInts = new int[] {1,2,3,4 }//{ -5, 16, -7, 18 },
    }
};

byte[] data, data2;

using( var ms = new MemoryStream())
{
    global::ProtoBuf.Serializer.Serialize(ms, model.Model2);

    data = ms.ToArray();
}

using (var ms = new MemoryStream())
{
    Model.Serialization.Serializers.SerializeClassWithCollections(ms, model.Model2);

    data2 = ms.ToArray();
}

//var result = Model.Serialization.Deserializers.DeserializeModelClassBase(data);
var result = Model.Serialization.Deserializers.DeserializeClassWithCollections(data);
var result2 = Model.Serialization.Deserializers.DeserializeClassWithCollections(data2);

using (var ms = new MemoryStream(data2))
{
    //Model.Serialization.Serializers.SerializeClassWithCollections(ms, model.Model2);
    var result3 = global::ProtoBuf.Serializer.Deserialize<ClassWithCollections>(ms);
}

Console.WriteLine(result.SomeInt);

//SpanReader reader = new SpanReader(data.AsSpan());

//var result = new ModelClass();

//while (!reader.IsEnd)
//{
//    var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//    if (fieldId == 3122)
//    {
//        result.A = reader.ReadDouble(wireType);
//        continue;
//    }

//    if (fieldId == 201)
//    {
//        result.B = reader.ReadInt32(wireType, zigZag: false);
//        continue;
//    }

//    if (fieldId == 1234568)
//    {
//        result.Str = reader.ReadString(wireType);
//        continue;
//    }

//    // default
//    reader.SkipField(wireType);
//}

//Console.WriteLine(result.A);
//public double ReadDouble(SpanReader reader, wireType)
//{

//}

//var positon = 0;
//var (typeAndFieldId, bytesAdvanced) = Utils.GetIntFromVarInt(data);

//positon += bytesAdvanced;

//var type = typeAndFieldId & 0b111;
//var fieldId = typeAndFieldId >> 3;

//if (type == 2)
//{
//    var (length, bytesAdvanced1) = Utils.GetIntFromVarInt(data.AsSpan(positon));

//    positon += bytesAdvanced1;
//}


//Console.WriteLine(fieldId);


