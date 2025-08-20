// See https://aka.ms/new-console-template for more information
using GProtobuf.Core;
using Model;
using ProtoBuf;
using ProtoBuf.Serializers;
//using Serializer;
using System;
using System.Net.Http.Headers;
using System.Text;

ModelClass model = new ModelClass
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
        NonPackedFixedSizeInts = [1,2,3,4] //{ -5, 16, -7, 18 },
    }
};

byte[] data, data2, modelData;

SimpleTypes simpleTypes = new SimpleTypes
{
     //SByteValue = SByte.MinValue,
     LongValue = long.MinValue,
};
using (var ms = new MemoryStream())
{
    Serializer.Serialize(ms, simpleTypes);
    data = ms.ToArray();

    Console.WriteLine("protobuf-net: " + String.Join(',', data));
}



//{
//    var x = new C()
//    {
//        StringA = "abc",
//        //StringB = "abc1",
//        //StringC = "abc2",
//    };

//    StringBuilder sb = new StringBuilder();
//    for (int i = 0; i < 1000; i++)
//    {
//        sb.Append($"abc{i}");
//    }

//    x.StringC = sb.ToString();

//    using (var ms = new MemoryStream())
//    {
//        Serializer.Serialize<A>(ms, x);
//        data = ms.ToArray();

//        Console.WriteLine("protobuf-net: " + String.Join(',', data));
//    }

//    using (var ms = new MemoryStream())
//    {
//        //Serializer.Serialize(ms, b);
//        Model.Serialization.Serializers.SerializeC(ms, x);
//        data2 = ms.ToArray();
//        Console.WriteLine("GProtobuf: " + String.Join(',', data2));
//    }

//    Console.WriteLine("Output equals: " + data.SequenceEqual(data2));

//    {
//        var result3 = Model.Serialization.Deserializers.DeserializeC(data2);
//        //Console.WriteLine(result3.StringA);
//        //Console.WriteLine(result3.StringB);
//    }

//    using (var ms = new MemoryStream(data2))
//    {
//        //Serializer.Serialize(ms, b);
//        var result = Serializer.Deserialize<C>(ms);
//        //Console.WriteLine(result.StringA);
//        //Console.WriteLine(result.StringB);
//    }
//}
//var c = new C()
//{
//     StringA = "abc",
//     StringB = "abc1",
//     StringC = "abc2",
//};

//using (var ms = new MemoryStream())
//{
//    Serializer.Serialize(ms, c);
//    data = ms.ToArray();
//}
//{
//    var result3 = Model.Serialization.Deserializers.DeserializeC(data);
//}

//using (var ms = new MemoryStream(data))
//{
//    //Model.Serialization.Serializers.SerializeClassWithCollections(ms, model.Model2);
//    //var result3 = Serializer.Deserialize<A1>(ms);

//}


//using (var ms = new MemoryStream())
//{
//    Serializer.Serialize(ms, model.Model2);
//    data = ms.ToArray();
//}

//using (var ms = new MemoryStream())
//{
//    Serializer.Serialize(ms, model);
//    modelData = ms.ToArray();

//    Console.WriteLine("protobuf-net: " + String.Join(',', modelData));
//}

//using (var ms = new MemoryStream())
//{
//    //Model.Serialization.Serializers.SerializeClassWithCollections(ms, model.Model2);
//    Model.Serialization.Serializers.SerializeModelClass(ms, model);

//    data2 = ms.ToArray();

//    Console.WriteLine("GProtobuf: " + String.Join(',', data2));
//}

//Console.WriteLine("Output equals: " + modelData.SequenceEqual(data2));

//var modelResult = Model.Serialization.Deserializers.DeserializeModelClassBase(modelData);
////var result = Model.Serialization.Deserializers.DeserializeClassWithCollections(data);
//var result2 = Model.Serialization.Deserializers.DeserializeModelClassBase(data2);

Console.ReadLine();

//using (var ms = new MemoryStream(data2))
//{
//    //Model.Serialization.Serializers.SerializeClassWithCollections(ms, model.Model2);
//    var result3 = Serializer.Deserialize<ClassWithCollections>(ms);
//}

//Console.WriteLine(result.SomeInt);
//Console.WriteLine(modelResult.Str);

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


