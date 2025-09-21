using FluentAssertions;
using GProtobuf.CrossTests.Refactored.Serialization;

namespace GProtobuf.CrossTests.Refactored;

public sealed class InheritanceTests : BaseSerializationTest
{
    [Fact]
    public void SerializeAndDeserializeA()
    {
        var model = new A
        {
            StringA = "StringA",
        };

        var data = SerializeWithGProtobuf(model, Serializers.SerializeA);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void SerializeAndDeserializeB()
    {
        var model = new B
        {
            StringA = "StringA",
            StringB = "StringB"
        };

        var data = SerializeWithGProtobuf(model, Serializers.SerializeB);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeB(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void SerializeAndDeserializeC()
    {
        var model = new C
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        var data = SerializeWithGProtobuf(model, Serializers.SerializeC);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeC(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void SerializeAndDeserializeD()
    {
        var model = new D
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC",
            StringD = "StringD"
        };

        var data = SerializeWithGProtobuf(model, Serializers.SerializeD);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeD(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }
    
    [Fact]
    public void SerializeDAndDeserializeAsBaseClass()
    {
        var model = new D
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC",
            StringD = "StringD"
        };

        var data = SerializeWithGProtobuf(model, Serializers.SerializeD);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeA(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should()
            .BeOfType<D>()
            .And
            .BeEquivalentTo(model);
    }

    [Fact]
    public void SerializeAndDeserializeEmptyD()
    {
        var model = new D();
        
        var data = SerializeWithGProtobuf(model, Serializers.SerializeD);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeD(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void SerializeAndDeserializeE()
    {
        var model = new E
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC",
            StringE = "StringE"
        };
        
        var data = SerializeWithGProtobuf(model, Serializers.SerializeE);
        var deserialized = DeserializeWithGProtobuf(data, bytes => Deserializers.DeserializeE(bytes));

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);
        deserialized.Should().BeEquivalentTo(model);
    }
}