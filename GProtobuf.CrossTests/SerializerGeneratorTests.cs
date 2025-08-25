//using System.Reflection;
//using GProtobuf.Generator;
//using Microsoft.CodeAnalysis;
//using Microsoft.CodeAnalysis.CSharp;
//using ProtoBuf;
//using Xunit.Abstractions;

//namespace GProtobuf.Tests;

//public sealed class SerializerGeneratorTests(ITestOutputHelper outputHelper)
//{
//    [Fact]
//    public void SerializerGenerator_SimpleContract_ShouldGenerateSerializer()
//    {
//        const string namespaceName = "TestNamespace";
//        const string className = "SimpleClass";

//        // language=C#
//        var code =
//        $$"""
//        using ProtoBuf;

//        namespace {{namespaceName}};

//        [ProtoContract]
//        public class {{className}}
//        {
//            [ProtoMember(1)]
//            public int X { get; set; }

//            [ProtoMember(2)]
//            public string Y { get; set; }

//            [ProtoMember(3)]
//            public float Z { get; set; }
//        }
//        """;

//        var runResult = RunGenerator(code);
//        var generatedFileSyntaxTree = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith($"{namespaceName}.Serialization.cs"));
//        var generatedText = generatedFileSyntaxTree.GetText().ToString();

//        // think about better assertions
//        outputHelper.WriteLine(generatedText);
//        AssertGeneratedCode(generatedText, className, namespaceName);
//    }
    
//    [Fact]
//    public void SerializerGenerator_ContractWithInheritance_ShouldGenerateSerializer()
//    {
//        const string namespaceName = "TestNamespace";
//        const string abstractClassName = "AbstractClass";
//        const string derivedClass1Name = "DerivedClass1";
//        const string derivedClass2Name = "DerivedClass2";

//        // language=C#
//        var code =
//        $$"""
//          using ProtoBuf;

//          namespace {{namespaceName}};

//          [ProtoContract]
//          [ProtoInclude(1, typeof(DerivedClass1))]
//          [ProtoInclude(2, typeof(DerivedClass2))]
//          public class {{abstractClassName}}
//          {
//            [ProtoMember(1)] public int X { get; set; }
//          }

//          [ProtoContract]
//          public class {{derivedClass1Name}} : {{abstractClassName}}
//          {
//            [ProtoMember(2)] public string Y { get; set; }
//          }
          
//          [ProtoContract]
//          public class {{derivedClass2Name}} : {{abstractClassName}}
//          {
//            [ProtoMember(2)] public double Z { get; set; }
//          }
//          """;

//        var runResult = RunGenerator(code);
//        var generatedFileSyntaxTree = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith($"{namespaceName}.Serialization.cs"));
//        var generatedText = generatedFileSyntaxTree.GetText().ToString();
        
//        outputHelper.WriteLine(generatedText);
//        AssertGeneratedCode(generatedText, abstractClassName, namespaceName);
//        AssertGeneratedCode(generatedText, derivedClass1Name, namespaceName);
//        AssertGeneratedCode(generatedText, derivedClass2Name, namespaceName);
//    }

//    private static GeneratorDriverRunResult RunGenerator(string code)
//    {
//        // Create an instance of the source generator.
//        var generator = new SerializerGenerator();

//        // Source generators should be tested using 'GeneratorDriver'.
//        var driver = CSharpGeneratorDriver.Create(generator);

//        // We need to add all the required references for the compilation.
//        var protobufAssembly = Assembly.GetAssembly(typeof(ProtoBuf.ProtoContractAttribute));
//        var references = AppDomain.CurrentDomain.GetAssemblies()
//            .Where(assembly => !assembly.IsDynamic)
//            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
//            .Cast<MetadataReference>()
//            .Append(MetadataReference.CreateFromFile(protobufAssembly!.Location))
//            .ToArray();

//        // We need to create a compilation with the required source code.
//        var compilation = CSharpCompilation.Create(nameof(Tests),
//            [CSharpSyntaxTree.ParseText(code)],
//            references);

//        // Run generators and retrieve all results.
//        var runResult = driver
//            .RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics)
//            .GetRunResult();

//        Assert.Empty(diagnostics.Where(x => x.Severity is DiagnosticSeverity.Error));
//        return runResult;
//    }

//    private static void AssertGeneratedCode(
//        string generatedCode,
//        string expectedClassName,
//        string expectedNamespaceName)
//    {
//        // TODO: think about more detailed assertions (e.g. full text assertion)
//        Assert.Contains(
//            $"public static global::{expectedNamespaceName}.{expectedClassName} Deserialize{expectedClassName}(ReadOnlySpan<byte> data)",
//            generatedCode);
//        Assert.Contains(
//            $"public static global::{expectedNamespaceName}.{expectedClassName} Read{expectedClassName}(ref SpanReader reader)",
//            generatedCode);
//        Assert.Contains(
//            $"public static void Serialize{expectedClassName}(Stream stream, global::{expectedNamespaceName}.{expectedClassName} obj)",
//            generatedCode);
//        Assert.Contains(
//            $"public static void Write{expectedClassName}(global::GProtobuf.Core.StreamWriter writer, global::{expectedNamespaceName}.{expectedClassName} obj)",
//            generatedCode);
//    }

//    [Fact]
//    public void EmptyArrayNullObject()
//    {
//        byte[] serializedData = [];
//        var result = Model.Serialization.Deserializers.DeserializeModelClassBase(serializedData);

//        Assert.True(result == null);
//    }

//    [Fact]
//    public void GeneratedSerializer_SerializeAndDeserializeClassWithCollections_SameObject()
//    {
//        var obj = new Model.ClassWithCollections
//        {
//            SomeInt = 7,
//            Bytes = [10, 11],
//            PackedInts = [1, 2],
//            PackedFixedSizeInts = [3, 4],
//            NonPackedInts = [5, 6],
//            NonPackedFixedSizeInts = [7, 8]
//        };

//        using var stream = new MemoryStream();
//        Model.Serialization.Serializers.SerializeClassWithCollections(stream, obj);
//        var deserialized = Model.Serialization.Deserializers.DeserializeClassWithCollections(stream.ToArray());
//        Assert.NotNull(deserialized);
//        Assert.IsType<Model.ClassWithCollections>(deserialized);
//        Assert.Equal(obj.SomeInt, deserialized.SomeInt);
//        Assert.True(obj.Bytes.SequenceEqual(deserialized.Bytes));
//        Assert.True(obj.PackedInts.SequenceEqual(deserialized.PackedInts));
//        Assert.True(obj.PackedFixedSizeInts.SequenceEqual(deserialized.PackedFixedSizeInts));
//        Assert.True(obj.NonPackedInts.SequenceEqual(deserialized.NonPackedInts));
//        Assert.True(obj.NonPackedFixedSizeInts.SequenceEqual(deserialized.NonPackedFixedSizeInts));
//    }
    
//    [Fact]
//    public void GeneratedSerializer_SerializeAndDeserializeModelClass_SameObject()
//    {
//        var obj = new Model.ModelClass
//        {
//            D = 42,
//            Model2 = new Model.ClassWithCollections
//            {
//                SomeInt = 7,
//                Bytes = [10, 11],
//                PackedInts = [1, 2],
//                PackedFixedSizeInts = [3, 4],
//                NonPackedInts = [5, 6],
//                NonPackedFixedSizeInts = [7, 8]
//            }
//        };

//        using var stream = new MemoryStream();
//        Model.Serialization.Serializers.SerializeModelClass(stream, obj);

//        var deserialized = Model.Serialization.Deserializers.DeserializeModelClass(stream.ToArray());

//        Assert.NotNull(deserialized);
//        Assert.IsType<Model.ModelClass>(deserialized);
//        Assert.Equal(obj.D, deserialized.D);
//        Assert.NotNull(deserialized.Model2);
//        Assert.Equal(obj.Model2.SomeInt, deserialized.Model2.SomeInt);
//        Assert.True(obj.Model2.Bytes.SequenceEqual(deserialized.Model2.Bytes));
//        Assert.True(obj.Model2.PackedInts.SequenceEqual(deserialized.Model2.PackedInts));
//        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(deserialized.Model2.PackedFixedSizeInts));
//        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(deserialized.Model2.NonPackedInts));
//        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(deserialized.Model2.NonPackedFixedSizeInts));
//    }
    
//    [Fact]
//    public void GeneratedSerializer_SerializeAndDeserializeModelClass_AsBaseClass_SameObject()
//    {
//        var obj = new Model.ModelClass
//        {
//            D = 42,
//            Model2 = new Model.ClassWithCollections
//            {
//                SomeInt = 7,
//                Bytes = [10, 11],
//                PackedInts = [1, 2],
//                PackedFixedSizeInts = [3, 4],
//                NonPackedInts = [5, 6],
//                NonPackedFixedSizeInts = [7, 8]
//            }
//        };

//        using var stream = new MemoryStream();
//        Model.Serialization.Serializers.SerializeModelClassBase(stream, obj);

//        // todo failure because of buffer overrun
//        var deserialized = Model.Serialization.Deserializers.DeserializeModelClassBase(stream.ToArray());

//        Assert.NotNull(deserialized);
//        Assert.IsType<Model.ModelClass>(deserialized);
//        var modelClass = deserialized as Model.ModelClass;
        
//        Assert.Equal(obj.D, modelClass!.D);
//        Assert.NotNull(modelClass.Model2);
//        Assert.Equal(obj.Model2.SomeInt, modelClass.Model2.SomeInt);
//        Assert.True(obj.Model2.Bytes.SequenceEqual(modelClass.Model2.Bytes));
//        Assert.True(obj.Model2.PackedInts.SequenceEqual(modelClass.Model2.PackedInts));
//        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(modelClass.Model2.PackedFixedSizeInts));
//        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(modelClass.Model2.NonPackedInts));
//        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(modelClass.Model2.NonPackedFixedSizeInts));
//    }

//    [Fact]
//    public void GeneratedSerializer_ClassWithCollections_ProducesSameBytesAsProtobufNet()
//    {
//        var clazzWithCollections = new Model.ClassWithCollections
//        {
//            SomeInt = 456,
//            Bytes = [1, 2, 3],
//            PackedInts = [1, 2, 3],
//            PackedFixedSizeInts = [4, 5],
//            NonPackedInts = [6, 7],
//            NonPackedFixedSizeInts = [8, 9]
//        };
        
//        using var streamGenerated = new MemoryStream();
//        Model.Serialization.Serializers.SerializeClassWithCollections(streamGenerated, clazzWithCollections);
//        var generatedBytes = streamGenerated.ToArray();

//        using var streamProtoBuf = new MemoryStream();
//        Serializer.Serialize(streamProtoBuf, clazzWithCollections);
//        var protoBufBytes = streamProtoBuf.ToArray();

//        Assert.Equal(protoBufBytes, generatedBytes);
//    }

//    [Fact]
//    public void GeneratedSerializer_ModelClass_ProducesSameBytesAsProtobufNet()
//    {
//        var obj = new Model.ModelClass
//        {
//            D = 123,
//            Model2 = new Model.ClassWithCollections
//            {
//                SomeInt = 456,
//                Bytes = [1, 2, 3],
//                PackedInts = [1, 2, 3],
//                PackedFixedSizeInts = [4, 5],
//                NonPackedInts = [6, 7],
//                NonPackedFixedSizeInts = [8, 9]
//            }
//        };

//        using var streamGenerated = new MemoryStream();
//        Model.Serialization.Serializers.SerializeModelClassBase(streamGenerated, obj);
//        var generatedBytes = streamGenerated.ToArray();

//        using var streamProtoBuf = new MemoryStream();
//        Serializer.Serialize(streamProtoBuf, obj);
//        var protoBufBytes = streamProtoBuf.ToArray();

//        Assert.Equal(protoBufBytes, generatedBytes); // todo the result is not the same
//    }

//    [Fact]
//    public void GeneratedDeserializer_ReadsBytesFromProtobufNet()
//    {
//        var obj = new Model.ModelClass
//        {
//            D = 42,
//            Model2 = new Model.ClassWithCollections
//            {
//                SomeInt = 7,
//                Bytes = [10, 11],
//                PackedInts = [1, 2],
//                PackedFixedSizeInts = [3, 4],
//                NonPackedInts = [5, 6],
//                NonPackedFixedSizeInts = [7, 8]
//            }
//        };

//        using var streamProtoBuf = new MemoryStream();
//        Serializer.Serialize(streamProtoBuf, obj);
//        var protoBufBytes = streamProtoBuf.ToArray();

//        var deserialized = Model.Serialization.Deserializers.DeserializeModelClass(protoBufBytes);

//        Assert.NotNull(deserialized.Model2);
//        Assert.Equal(obj.D, deserialized.D);
//        Assert.Equal(obj.Model2.SomeInt, deserialized.Model2.SomeInt);
//        Assert.True(obj.Model2.Bytes.SequenceEqual(deserialized.Model2.Bytes));
//        Assert.True(obj.Model2.PackedInts.SequenceEqual(deserialized.Model2.PackedInts));
//        Assert.True(obj.Model2.PackedFixedSizeInts.SequenceEqual(deserialized.Model2.PackedFixedSizeInts));
//        Assert.True(obj.Model2.NonPackedInts.SequenceEqual(deserialized.Model2.NonPackedInts));
//        Assert.True(obj.Model2.NonPackedFixedSizeInts.SequenceEqual(deserialized.Model2.NonPackedFixedSizeInts));
//    }
//}