using System.IO;
using GProtobuf.CrossTests.TestModel;
using Xunit;

//namespace GProtobuf.CrossTests
//{
    //public class InheritanceModelTests
    //{
    //    [Fact]
    //    public void Test_GG_Model1_SerializeDeserialize()
    //    {
    //        var original = new Model1
    //        {
    //            Id = 42,
    //            Value1 = 100
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModel1(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModel1(bytes);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value1, deserialized.Value1);
    //    }

    //    [Fact]
    //    public void Test_GG_Model2_SerializeDeserialize()
    //    {
    //        var original = new ModelInh2
    //        {
    //            Id = 123,
    //            Value2 = 456
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModel2(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModel2(bytes);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value2, deserialized.Value2);
    //    }

    //    [Fact]
    //    public void Test_GG_ModelBase_WithModel1_SerializeDeserialize()
    //    {
    //        ModelBase original = new ModelInh1
    //        {
    //            Id = 10,
    //            Value1 = 20
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModelBase(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModelBase(bytes);

    //        Assert.IsType<ModelInh1>(deserialized);
    //        var model1 = (ModelInh1)deserialized;
    //        Assert.Equal(10, model1.Id);
    //        Assert.Equal(20, model1.Value1);
    //    }

    //    [Fact]
    //    public void Test_GG_ModelBase_WithModel2_SerializeDeserialize()
    //    {
    //        ModelBase original = new ModelInh2
    //        {
    //            Id = 30,
    //            Value2 = 40
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModelBase(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModelBase(bytes);

    //        Assert.IsType<ModelInh2>(deserialized);
    //        var model2 = (ModelInh2)deserialized;
    //        Assert.Equal(30, model2.Id);
    //        Assert.Equal(40, model2.Value2);
    //    }

    //    [Fact]
    //    public void Test_PG_Model1_ProtobufNetSerialize_GProtobufDeserialize()
    //    {
    //        var original = new ModelInh1
    //        {
    //            Id = 55,
    //            Value1 = 77
    //        };

    //        using var ms = new MemoryStream();
    //        ProtoBuf.Serializer.Serialize(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModel1(bytes);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value1, deserialized.Value1);
    //    }

    //    [Fact]
    //    public void Test_PG_Model2_ProtobufNetSerialize_GProtobufDeserialize()
    //    {
    //        var original = new ModelInh2
    //        {
    //            Id = 88,
    //            Value2 = 99
    //        };

    //        using var ms = new MemoryStream();
    //        ProtoBuf.Serializer.Serialize(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModel2(bytes);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value2, deserialized.Value2);
    //    }

    //    [Fact]
    //    public void Test_GP_Model1_GProtobufSerialize_ProtobufNetDeserialize()
    //    {
    //        var original = new ModelInh1
    //        {
    //            Id = 111,
    //            Value1 = 222
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModel1(ms, original);
    //        ms.Position = 0;

    //        var deserialized = ProtoBuf.Serializer.Deserialize<ModelInh1>(ms);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value1, deserialized.Value1);
    //    }

    //    [Fact]
    //    public void Test_GP_Model2_GProtobufSerialize_ProtobufNetDeserialize()
    //    {
    //        var original = new ModelInh2
    //        {
    //            Id = 333,
    //            Value2 = 444
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModel2(ms, original);
    //        ms.Position = 0;

    //        var deserialized = ProtoBuf.Serializer.Deserialize<ModelInh2>(ms);

    //        Assert.Equal(original.Id, deserialized.Id);
    //        Assert.Equal(original.Value2, deserialized.Value2);
    //    }

    //    [Fact]
    //    public void Test_PG_ModelBase_WithModel1_Polymorphic()
    //    {
    //        ModelBase original = new ModelInh1
    //        {
    //            Id = 500,
    //            Value1 = 600
    //        };

    //        using var ms = new MemoryStream();
    //        ProtoBuf.Serializer.Serialize(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModelBase(bytes);

    //        Assert.IsType<ModelInh1>(deserialized);
    //        var model1 = (ModelInh1)deserialized;
    //        Assert.Equal(500, model1.Id);
    //        Assert.Equal(600, model1.Value1);
    //    }

    //    [Fact]
    //    public void Test_PG_ModelBase_WithModel2_Polymorphic()
    //    {
    //        ModelBase original = new ModelInh2
    //        {
    //            Id = 700,
    //            Value2 = 800
    //        };

    //        using var ms = new MemoryStream();
    //        ProtoBuf.Serializer.Serialize(ms, original);
    //        var bytes = ms.ToArray();

    //        var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeModelBase(bytes);

    //        Assert.IsType<ModelInh2>(deserialized);
    //        var model2 = (ModelInh2)deserialized;
    //        Assert.Equal(700, model2.Id);
    //        Assert.Equal(800, model2.Value2);
    //    }

    //    [Fact]
    //    public void Test_GP_ModelBase_WithModel1_Polymorphic()
    //    {
    //        ModelBase original = new ModelInh1
    //        {
    //            Id = 900,
    //            Value1 = 1000
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModelBase(ms, original);
    //        ms.Position = 0;

    //        var deserialized = ProtoBuf.Serializer.Deserialize<ModelBase>(ms);

    //        Assert.IsType<ModelInh1>(deserialized);
    //        var model1 = (ModelInh1)deserialized;
    //        Assert.Equal(900, model1.Id);
    //        Assert.Equal(1000, model1.Value1);
    //    }

    //    [Fact]
    //    public void Test_GP_ModelBase_WithModel2_Polymorphic()
    //    {
    //        ModelBase original = new ModelInh2
    //        {
    //            Id = 1100,
    //            Value2 = 1200
    //        };

    //        using var ms = new MemoryStream();
    //        global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeModelBase(ms, original);
    //        ms.Position = 0;

    //        var deserialized = ProtoBuf.Serializer.Deserialize<ModelBase>(ms);

    //        Assert.IsType<ModelInh2>(deserialized);
    //        var model2 = (ModelInh2)deserialized;
    //        Assert.Equal(1100, model2.Id);
    //        Assert.Equal(1200, model2.Value2);
    //    }
    //}
//}
