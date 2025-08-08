using System.IO;
using System.Linq;
using System.Reflection;
using GProtobuf.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf;
using Xunit.Abstractions;

namespace GProtobuf.Tests;

public sealed class SerializerGeneratorTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public void SerializerGenerator_SimpleContract_ShouldGenerateSerializer()
    {
        const string namespaceName = "TestNamespace";
        const string className = "SimpleClass";

        // language=C#
        var code =
        $$"""
        using ProtoBuf;

        namespace {{namespaceName}};

        [ProtoContract]
        public class {{className}}
        {
            [ProtoMember(1)]
            public int X { get; set; }

            [ProtoMember(2)]
            public string Y { get; set; }

            [ProtoMember(3)]
            public float Z { get; set; }
        }
        """;

        var runResult = RunGenerator(code);
        var generatedFileSyntaxTree = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith($"{namespaceName}.Serialization.cs"));
        var generatedText = generatedFileSyntaxTree.GetText().ToString();

        // think about better assertions
        outputHelper.WriteLine(generatedText);
        AssertGeneratedCode(generatedText, className, namespaceName);
    }
    
    [Fact]
    public void SerializerGenerator_ContractWithInheritance_ShouldGenerateSerializer()
    {
        const string namespaceName = "TestNamespace";
        const string abstractClassName = "AbstractClass";
        const string derivedClass1Name = "DerivedClass1";
        const string derivedClass2Name = "DerivedClass2";

        // language=C#
        var code =
        $$"""
          using ProtoBuf;

          namespace {{namespaceName}};

          [ProtoContract]
          [ProtoInclude(1, typeof(DerivedClass1))]
          [ProtoInclude(2, typeof(DerivedClass2))]
          public class {{abstractClassName}}
          {
            [ProtoMember(1)] public int X { get; set; }
          }

          [ProtoContract]
          public class {{derivedClass1Name}} : {{abstractClassName}}
          {
            [ProtoMember(2)] public string Y { get; set; }
          }
          
          [ProtoContract]
          public class {{derivedClass2Name}} : {{abstractClassName}}
          {
            [ProtoMember(2)] public double Z { get; set; }
          }
          """;

        var runResult = RunGenerator(code);
        var generatedFileSyntaxTree = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith($"{namespaceName}.Serialization.cs"));
        var generatedText = generatedFileSyntaxTree.GetText().ToString();
        
        outputHelper.WriteLine(generatedText);
        AssertGeneratedCode(generatedText, abstractClassName, namespaceName);
        AssertGeneratedCode(generatedText, derivedClass1Name, namespaceName);
        AssertGeneratedCode(generatedText, derivedClass2Name, namespaceName);
    }

    private static GeneratorDriverRunResult RunGenerator(string code)
    {
        // Create an instance of the source generator.
        var generator = new SerializerGenerator();

        // Source generators should be tested using 'GeneratorDriver'.
        var driver = CSharpGeneratorDriver.Create(generator);

        // We need to add all the required references for the compilation.
        var protobufAssembly = Assembly.GetAssembly(typeof(ProtoBuf.ProtoContractAttribute));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(protobufAssembly!.Location))
            .ToArray();

        // We need to create a compilation with the required source code.
        var compilation = CSharpCompilation.Create(nameof(Tests),
            [CSharpSyntaxTree.ParseText(code)],
            references);

        // Run generators and retrieve all results.
        var runResult = driver
            .RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics)
            .GetRunResult();

        Assert.Empty(diagnostics.Where(x => x.Severity is DiagnosticSeverity.Error));
        return runResult;
    }

    private static void AssertGeneratedCode(
        string generatedCode,
        string expectedClassName,
        string expectedNamespaceName)
    {
        // TODO: think about more detailed assertions (e.g. full text assertion)
        Assert.Contains(
            $"public static global::{expectedNamespaceName}.{expectedClassName} Deserialize{expectedClassName}(ReadOnlySpan<byte> data)",
            generatedCode);
        Assert.Contains(
            $"public static global::{expectedNamespaceName}.{expectedClassName} Read{expectedClassName}(ref SpanReader reader)",
            generatedCode);
        Assert.Contains(
            $"public static void Serialize{expectedClassName}(Stream stream, global::{expectedNamespaceName}.{expectedClassName} obj)",
            generatedCode);
        Assert.Contains(
            $"public static void Write{expectedClassName}(global::GProtobuf.Core.StreamWriter writer, global::{expectedNamespaceName}.{expectedClassName} obj)",
            generatedCode);
    }

    [Fact]
    public void EmptyArrayNullObject()
    {
        byte[] serializedData = [];
        var result = Model.Serialization.Deserializers.DeserializeModelClassBase(serializedData);

        Assert.True(result == null);
    }

    [Fact]
    public void GeneratedSerializer_ProducesSameBytesAsProtobufNet()
    {
        var obj = new Model.ModelClass
        {
            D = 123,
            Model2 = new Model.ClassWithCollections
            {
                SomeInt = 456,
                Bytes = new byte[] { 1, 2, 3 },
                PackedInts = new[] { 1, 2, 3 },
                PackedFixedSizeInts = new[] { 4, 5 },
                NonPackedInts = new[] { 6, 7 },
                NonPackedFixedSizeInts = new[] { 8, 9 }
            }
        };

        using var streamGenerated = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClass(streamGenerated, obj);
        var generatedBytes = streamGenerated.ToArray();

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        var protoBufBytes = streamProtoBuf.ToArray();

        Assert.Equal(protoBufBytes, generatedBytes);
    }

    [Fact]
    public void GeneratedDeserializer_ReadsBytesFromProtobufNet()
    {
        var obj = new Model.ModelClass
        {
            D = 42,
            Model2 = new Model.ClassWithCollections
            {
                SomeInt = 7,
                Bytes = new byte[] { 10, 11 },
                PackedInts = new[] { 1, 2 },
                PackedFixedSizeInts = new[] { 3, 4 },
                NonPackedInts = new[] { 5, 6 },
                NonPackedFixedSizeInts = new[] { 7, 8 }
            }
        };

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        var protoBufBytes = streamProtoBuf.ToArray();

        var deserialized = Model.Serialization.Deserializers.DeserializeModelClass(protoBufBytes);

        Assert.NotNull(deserialized.Model2);
        Assert.Equal(obj.D, deserialized.D);
        Assert.Equal(obj.Model2.SomeInt, deserialized.Model2.SomeInt);
        Assert.True(obj.Model2.Bytes.SequenceEqual(deserialized.Model2.Bytes));
        Assert.True(obj.Model2.PackedInts.SequenceEqual(deserialized.Model2.PackedInts));
        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(deserialized.Model2.PackedFixedSizeInts));
        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(deserialized.Model2.NonPackedInts));
        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(deserialized.Model2.NonPackedFixedSizeInts));

        using var roundtripStream = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClass(roundtripStream, deserialized);
        var roundtripBytes = roundtripStream.ToArray();
        Assert.Equal(protoBufBytes, roundtripBytes);
    }
}