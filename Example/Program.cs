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
        Bytes = new byte[] { 1,2,3,4},
        PackedInts = new[] { -1, 15},
        PackedFixedSizeInts = new[] { -2, 17 },
        NonPackedInts = new[] { 5,6,7,8 },
    }
};

byte[] data;

using( var ms = new MemoryStream())
{
    global::ProtoBuf.Serializer.Serialize(ms, model);

    data = ms.ToArray();
}

var result = Model.Serialization.Deserializers.DeserializeModelClassBase(data);


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

Console.WriteLine(result);
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


