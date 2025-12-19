using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// V2 implementation of ObjectTree that uses modular code generators.
    /// Orchestrates TypeRegistry and all code generators.
    /// </summary>
    public class ObjectTreeV2
    {
        private readonly TypeRegistry _registry = new TypeRegistry();

        #region Type Registration

        public void AddType(string @namespace, TypeDefinition type)
        {
            _registry.Register(@namespace, type);
        }

        #endregion

        #region Code Generation

        public IEnumerable<(string FileName, string FileCode)> GenerateCode()
        {
            foreach (var ns in _registry.GetAllNamespaces())
            {
                var sb = new StringBuilderWithIndent();
                var types = _registry.GetByNamespace(ns).ToList();

                WriteHeader(sb, ns);

                // Generate static Tags class for multi-byte tags (zero-allocation)
                var tagsGenerator = new TagsGenerator(sb);
                tagsGenerator.CollectTags(types);
                tagsGenerator.Generate();

                // Generate Deserializers class (entry point methods)
                GenerateDeserializers(sb, types);

                // Generate Serializers class (entry point methods)
                GenerateSerializers(sb, types);

                // Generate SpanReaders class
                new SpanReaderGenerator(sb, _registry).GenerateAll(types);

                // Generate StreamWriters class
                new StreamWriterGenerator(sb, _registry).GenerateAll(types);

                // Generate BufferWriters class (same as StreamWriter but for IBufferWriter)
                new BufferWriterGenerator(sb, _registry).GenerateAll(types);

                // Generate SizeCalculators class
                new SizeCalculatorGenerator(sb, _registry).GenerateAll(types);

                WriteFooter(sb);

                yield return ($"{ns}.Serialization.cs", sb.ToString());
            }
        }

        #endregion

        #region Deserializers Class

        private void GenerateDeserializers(StringBuilderWithIndent sb, List<TypeDefinition> types)
        {
            sb.AppendIndentedLine("public static class Deserializers");
            sb.StartNewBlock();

            foreach (var type in types)
            {
                var className = GetClassName(type.FullName);

                // Deserialize method - creates new instance
                sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(ReadOnlySpan<byte> data)");
                sb.StartNewBlock();
                sb.AppendIndentedLine("var reader = new SpanReader(data);");
                sb.AppendIndentedLine($"return SpanReaders.Read{className}(ref reader);");
                sb.EndBlock();
                sb.AppendNewLine();

                // Populate method - fills existing instance (zero object allocation)
                if (!type.IsAbstract)
                {
                    sb.AppendIndentedLine($"public static void Populate{className}(ReadOnlySpan<byte> data, global::{type.FullName} instance)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine("var reader = new SpanReader(data);");
                    sb.AppendIndentedLine($"SpanReaders.Populate{className}(ref reader, instance);");
                    sb.EndBlock();
                    sb.AppendNewLine();
                }
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }

        #endregion

        #region Serializers Class

        private void GenerateSerializers(StringBuilderWithIndent sb, List<TypeDefinition> types)
        {
            sb.AppendIndentedLine("public static class Serializers");
            sb.StartNewBlock();

            foreach (var type in types)
            {
                var className = GetClassName(type.FullName);

                // Stream serializer
                sb.AppendIndentedLine($"public static void Serialize{className}(Stream stream, global::{type.FullName} obj)");
                sb.StartNewBlock();
                sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
                sb.AppendIndentedLine($"StreamWriters.Write{className}(ref writer, obj);");
                sb.AppendIndentedLine("writer.Flush();");
                sb.EndBlock();
                sb.AppendNewLine();

                // IBufferWriter serializer
                sb.AppendIndentedLine($"public static void Serialize{className}(IBufferWriter<byte> buffer, global::{type.FullName} obj)");
                sb.StartNewBlock();
                sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
                sb.AppendIndentedLine($"BufferWriters.Write{className}(ref writer, obj);");
                sb.EndBlock();
                sb.AppendNewLine();
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }

        #endregion

        #region Header/Footer

        private void WriteHeader(StringBuilderWithIndent sb, string namespaceName)
        {
            sb.AppendIndentedLine("// <auto-generated/>");
            sb.AppendIndentedLine("using GProtobuf.Core;");
            sb.AppendIndentedLine("using System;");
            sb.AppendIndentedLine("using System.Collections.Generic;");
            sb.AppendIndentedLine("using System.IO;");
            sb.AppendIndentedLine("using System.Buffers;");
            sb.AppendIndentedLine("using System.Text;");
            sb.AppendNewLine();
            sb.AppendIndentedLine($"namespace {namespaceName}.Serialization");
            sb.StartNewBlock();
        }

        private void WriteFooter(StringBuilderWithIndent sb)
        {
            sb.EndBlock(); // Close namespace
        }

        #endregion

        #region Helpers

        private static string GetClassName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            var lastDot = fullName.LastIndexOf('.');
            return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
        }

        #endregion
    }
}
