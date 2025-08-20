using FluentAssertions;
using GProtobuf.Tests.TestModel;
//using GProtobuf.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using System.Reflection;
using System.Text;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

// GG tests - serialization and deserialization using GProtobuf
public sealed class TestsGG(ITestOutputHelper outputHelper)
{
    [Fact]
    public void ABCBasicHierachyGG()
    {
        var model = new C()
        {
            StringA = "StringA",
            StringB = "StringB",
            StringC = "StringC"
        };

        using var ms = new MemoryStream();
        TestModel.Serialization.Serializers.SerializeA(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyCGG()
    {
        var model = new C()
        {
            StringC = "StringC"
        };

        using var ms = new MemoryStream();
        TestModel.Serialization.Serializers.SerializeA(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCOnlyAGG()
    {
        var model = new C()
        {
            StringA = "StringA"
        };

        using var ms = new MemoryStream();
        TestModel.Serialization.Serializers.SerializeA(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }


    [Fact]
    public void ABCSerializeAsCGG()
    {
        // C is the most derived type, should be able to be deserialized as A

        var model = new C()
        {
            StringA = "StringA"
        };

        using var ms = new MemoryStream();
        TestModel.Serialization.Serializers.SerializeC(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }

    [Fact]
    public void ABCSerializeLongStringGG()
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
        TestModel.Serialization.Serializers.SerializeC(ms, model);

        var data = ms.ToArray();

        data.Should().NotBeNull();
        data.Length.Should().BeGreaterThan(0);

        var deserialized = TestModel.Serialization.Deserializers.DeserializeA(data);

        deserialized.Should().BeEquivalentTo(model);
    }
}
