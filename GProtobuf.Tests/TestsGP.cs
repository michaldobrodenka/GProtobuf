using FluentAssertions;
using GProtobuf.Tests.TestModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System.Reflection;
using System.Text;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

// GP tests - serialization and deserialization using GProtobuf
public sealed class TestsGP(ITestOutputHelper outputHelper)
{
    [Fact]
    public void ABCBasicHierachyGP()
    {
        var model = new C()
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        using var ms2 = new MemoryStream(data);
        var deserialized = Serializer.Deserialize<C>(ms2);
        //var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyCGP()
    {
        var model = new C()
        {
            StringC = "StringC"
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        using var ms2 = new MemoryStream(data);
        var deserialized = Serializer.Deserialize<C>(ms2);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyAGP()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        using var ms2 = new MemoryStream(data);
        var deserialized = Serializer.Deserialize<C>(ms2);

        deserialized.Should().BeEquivalentTo(model);
    }


    [Fact]
    public void ABCSerializeAsCGP()
    {
        // C is the most derived type, should be able to be deserialized as A

        var model = new C()
        {
            StringA = "StringA"
        };

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        using var ms2 = new MemoryStream(data);
        var deserialized = Serializer.Deserialize<C>(ms2);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCSerializeLongStringGP()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append($"abc{i}");
        }

        model.StringC = sb.ToString();

        using var ms = new MemoryStream();
        Serializer.Serialize(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        using var ms2 = new MemoryStream(data);
        var deserialized = Serializer.Deserialize<C>(ms2);

        deserialized.Should().BeEquivalentTo(model);
    }
}
