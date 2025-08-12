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
    public void GeneratedSerializer_SerializeAndDeserializeClassWithCollections_SameObject()
    {
        var obj = new Model.ClassWithCollections
        {
            SomeInt = 7,
            Bytes = [10, 11],
            PackedInts = [1, 2],
            PackedFixedSizeInts = [3, 4],
            NonPackedInts = [5, 6],
            NonPackedFixedSizeInts = [7, 8]
        };

        using var stream = new MemoryStream();
        Model.Serialization.Serializers.SerializeClassWithCollections(stream, obj);
        var deserialized = Model.Serialization.Deserializers.DeserializeClassWithCollections(stream.ToArray());
        Assert.NotNull(deserialized);
        Assert.IsType<Model.ClassWithCollections>(deserialized);
        Assert.Equal(obj.SomeInt, deserialized.SomeInt);
        Assert.True(obj.Bytes.SequenceEqual(deserialized.Bytes));
        Assert.True(obj.PackedInts.SequenceEqual(deserialized.PackedInts));
        Assert.True(obj.PackedFixedSizeInts.SequenceEqual(deserialized.PackedFixedSizeInts));
        Assert.True(obj.NonPackedInts.SequenceEqual(deserialized.NonPackedInts));
        Assert.True(obj.NonPackedFixedSizeInts.SequenceEqual(deserialized.NonPackedFixedSizeInts));
    }
    
    [Fact]
    public void GeneratedSerializer_SerializeAndDeserializeModelClass_SameObject()
    {
        var obj = new Model.ModelClass
        {
            D = 42,
            Model2 = new Model.ClassWithCollections
            {
                SomeInt = 7,
                Bytes = [10, 11],
                PackedInts = [1, 2],
                PackedFixedSizeInts = [3, 4],
                NonPackedInts = [5, 6],
                NonPackedFixedSizeInts = [7, 8]
            }
        };

        using var stream = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClass(stream, obj);

        var deserialized = Model.Serialization.Deserializers.DeserializeModelClass(stream.ToArray());

        Assert.NotNull(deserialized);
        Assert.IsType<Model.ModelClass>(deserialized);
        Assert.Equal(obj.D, deserialized.D);
        Assert.NotNull(deserialized.Model2);
        Assert.Equal(obj.Model2.SomeInt, deserialized.Model2.SomeInt);
        Assert.True(obj.Model2.Bytes.SequenceEqual(deserialized.Model2.Bytes));
        Assert.True(obj.Model2.PackedInts.SequenceEqual(deserialized.Model2.PackedInts));
        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(deserialized.Model2.PackedFixedSizeInts));
        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(deserialized.Model2.NonPackedInts));
        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(deserialized.Model2.NonPackedFixedSizeInts));
    }
    
    [Fact]
    public void GeneratedSerializer_SerializeAndDeserializeModelClass_AsBaseClass_OnlyDerivedFields()
    {
        var obj = new Model.ModelClass
        {
            D = 42,
            Model2 = new Model.ClassWithCollections
            {
                SomeInt = 7,
                Bytes = [10, 11],
                PackedInts = [1, 2],
                PackedFixedSizeInts = [3, 4],
                NonPackedInts = [5, 6],
                NonPackedFixedSizeInts = [7, 8]
            }
        };

        using var stream = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClassBase(stream, obj);
        var generatedBytes = stream.ToArray();

        var deserialized = Model.Serialization.Deserializers.DeserializeModelClassBase(generatedBytes);

        Assert.NotNull(deserialized);
        var modelClass = Assert.IsType<Model.ModelClass>(deserialized);
        Assert.Equal(0, modelClass.A);
        Assert.Equal(0, modelClass.B);
        Assert.Null(modelClass.Str);
        Assert.Equal(obj.D, modelClass.D);
        Assert.NotNull(modelClass.Model2);
        Assert.Equal(obj.Model2.SomeInt, modelClass.Model2.SomeInt);
        Assert.True(obj.Model2.Bytes.SequenceEqual(modelClass.Model2.Bytes));
        Assert.True(obj.Model2.PackedInts.SequenceEqual(modelClass.Model2.PackedInts));
        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(modelClass.Model2.PackedFixedSizeInts));
        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(modelClass.Model2.NonPackedInts));
        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(modelClass.Model2.NonPackedFixedSizeInts));

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        Assert.Equal(streamProtoBuf.ToArray(), generatedBytes);
    }

    [Fact]
    public void GeneratedSerializer_SerializeAndDeserializeModelClass_AsBaseClass_BaseAndDerivedFields()
    {
        var obj = new Model.ModelClass
        {
            A = 1.5,
            B = 11,
            Str = "base",
            D = 7
        };

        using var stream = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClassBase(stream, obj);
        var generatedBytes = stream.ToArray();

        var deserialized = Model.Serialization.Deserializers.DeserializeModelClassBase(generatedBytes);

        var modelClass = Assert.IsType<Model.ModelClass>(deserialized);
        Assert.Equal(obj.A, modelClass.A);
        Assert.Equal(obj.B, modelClass.B);
        Assert.Equal(obj.Str, modelClass.Str);
        Assert.Equal(obj.D, modelClass.D);

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        Assert.Equal(streamProtoBuf.ToArray(), generatedBytes);
    }

    [Fact]
    public void GeneratedSerializer_SerializeAndDeserializeSecondModelClass_OnlyBaseFields()
    {
        var obj = new Model.SecondModelClass
        {
            A = 2.25,
            B = 3,
            Str = "abc"
        };

        using var stream = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClassBase(stream, obj);
        var generatedBytes = stream.ToArray();

        var deserialized = Model.Serialization.Deserializers.DeserializeModelClassBase(generatedBytes);
        var second = Assert.IsType<Model.SecondModelClass>(deserialized);
        Assert.Equal(obj.A, second.A);
        Assert.Equal(obj.B, second.B);
        Assert.Equal(obj.Str, second.Str);

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        Assert.Equal(streamProtoBuf.ToArray(), generatedBytes);
    }

    [Fact]
    public void GeneratedSerializer_ClassWithCollections_ProducesSameBytesAsProtobufNet()
    {
        var clazzWithCollections = new Model.ClassWithCollections
        {
            SomeInt = 456,
            Bytes = [1, 2, 3],
            PackedInts = [1, 2, 3],
            PackedFixedSizeInts = [4, 5],
            NonPackedInts = [6, 7],
            NonPackedFixedSizeInts = [8, 9]
        };
        
        using var streamGenerated = new MemoryStream();
        Model.Serialization.Serializers.SerializeClassWithCollections(streamGenerated, clazzWithCollections);
        var generatedBytes = streamGenerated.ToArray();

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, clazzWithCollections);
        var protoBufBytes = streamProtoBuf.ToArray();

        Assert.Equal(protoBufBytes, generatedBytes);
    }

    [Fact]
    public void GeneratedSerializer_ModelClass_ProducesSameBytesAsProtobufNet()
    {
        var obj = new Model.ModelClass
        {
            D = 123,
            Model2 = new Model.ClassWithCollections
            {
                SomeInt = 456,
                Bytes = [1, 2, 3],
                PackedInts = [1, 2, 3],
                PackedFixedSizeInts = [4, 5],
                NonPackedInts = [6, 7],
                NonPackedFixedSizeInts = [8, 9]
            }
        };

        using var streamGenerated = new MemoryStream();
        Model.Serialization.Serializers.SerializeModelClassBase(streamGenerated, obj);
        var generatedBytes = streamGenerated.ToArray();

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        var protoBufBytes = streamProtoBuf.ToArray();

        Assert.Equal(protoBufBytes, generatedBytes);
    }

    [Fact]
    public void GeneratedSerializer_SingleProtoInclude_RoundTripsBaseAndDerivedFields()
    {
        const string ns = "SingleInclude";
        const string baseName = "Base";
        const string derivedName = "Derived";

        // language=C#
        var code = $$"""
        using ProtoBuf;

        namespace {{ns}};

        [ProtoContract]
        [ProtoInclude(1, typeof({{derivedName}}))]
        public class {{baseName}}
        {
            [ProtoMember(1)] public int X { get; set; }
        }

        [ProtoContract]
        public class {{derivedName}} : {{baseName}}
        {
            [ProtoMember(2)] public string Y { get; set; }
        }
        """;

        var generator = new SerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        var protobufAssembly = Assembly.GetAssembly(typeof(ProtoContractAttribute));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(protobufAssembly!.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create("dynamic", [CSharpSyntaxTree.ParseText(code)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var ms = new MemoryStream();
        var emit = newCompilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        ms.Seek(0, SeekOrigin.Begin);
        var asm = Assembly.Load(ms.ToArray());

        var baseType = asm.GetType($"{ns}.{baseName}")!;
        var derivedType = asm.GetType($"{ns}.{derivedName}")!;
        var serializers = asm.GetType($"{ns}.Serialization.Serializers")!;
        var deserializers = asm.GetType($"{ns}.Serialization.Deserializers")!;

        var obj = Activator.CreateInstance(derivedType)!;
        baseType.GetProperty("X")!.SetValue(obj, 5);
        derivedType.GetProperty("Y")!.SetValue(obj, "abc");

        using var streamGenerated = new MemoryStream();
        var serialize = serializers.GetMethod($"Serialize{baseName}", new[] { typeof(Stream), baseType })!;
        serialize.Invoke(null, new object[] { streamGenerated, obj });
        var generatedBytes = streamGenerated.ToArray();

        using var streamProtoBuf = new MemoryStream();
        Serializer.Serialize(streamProtoBuf, obj);
        Assert.Equal(streamProtoBuf.ToArray(), generatedBytes);

        var deserialize = deserializers.GetMethod($"Deserialize{baseName}", new[] { typeof(byte[]) })!;
        var clone = deserialize.Invoke(null, new object[] { generatedBytes });

        Assert.Equal(5, baseType.GetProperty("X")!.GetValue(clone));
        Assert.Equal("abc", derivedType.GetProperty("Y")!.GetValue(clone));
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
                Bytes = [10, 11],
                PackedInts = [1, 2],
                PackedFixedSizeInts = [3, 4],
                NonPackedInts = [5, 6],
                NonPackedFixedSizeInts = [7, 8]
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
    }
}