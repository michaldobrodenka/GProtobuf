using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// V2 implementation of ObjectTree that uses modular code generators.
    /// Orchestrates TypeRegistry and all code generators.
    /// </summary>
    public class ObjectTreeV2
    {
        private readonly TypeRegistry _registry = new();
        private readonly Microsoft.CodeAnalysis.Compilation _compilation;

        #region Type Registration

        public ObjectTreeV2(HashSet<string> enumTypes, Microsoft.CodeAnalysis.Compilation compilation = null)
        {
            _compilation = compilation;

            // Register all enum types in the TypeRegistry
            if (enumTypes != null)
            {
                foreach (var enumType in enumTypes)
                {
                    _registry.RegisterEnum(enumType);
                }
            }
        }

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
                yield return GenerateCodeForNamespace(ns);
            }
        }

        private (string FileName, string FileCode) GenerateCodeForNamespace(string ns)
        {
            try
            {
                var sb = new StringBuilderWithIndent();
                var types = _registry.GetByNamespace(ns).ToList();

                // Create shared virtual registries
                var virtualTupleRegistry = new VirtualTupleTypeRegistry();
                var virtualMapRegistry = new VirtualMapTypeRegistry(virtualTupleRegistry, _registry, _compilation);

                WriteHeader(sb, ns);

                // Remember position after header to insert Tags class later
                var insertPosition = sb.Length;

                // Create TagsGenerator with separate StringBuilder (will be inserted at insertPosition)
                var tagsSb = new StringBuilderWithIndent();
                tagsSb.IndentLevel = sb.IndentLevel; // Match indentation
                var tagsGenerator = new TagsGenerator(tagsSb);

                try
                {
                    // Collect tags from base types first
                    tagsGenerator.CollectTags(types);

                    // Collect tags from ProtoInclude types that will have WriteContent methods generated
                    tagsGenerator.CollectTagsFromProtoIncludes(types, _registry);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error collecting tags from base types for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate Deserializers class (entry point methods)
                    GenerateDeserializers(sb, types);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateDeserializers for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate Serializers class (entry point methods)
                    GenerateSerializers(sb, types);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateSerializers for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate SpanReaders class (uses shared virtual registries, registers Dictionary and Tuple types)
                    var spanReaderGenerator = new SpanReaderGenerator(sb, _registry, virtualMapRegistry, virtualTupleRegistry);
                    spanReaderGenerator.GenerateAll(types, ns);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in SpanReaderGenerator for namespace '{ns}'. Inner: {ex.Message}. Stack: {ex.StackTrace}", ex);
                }

                try
                {
                    // Generate StreamWriters class (uses shared virtual registries)
                    new StreamWriterGenerator(sb, _registry, virtualMapRegistry, virtualTupleRegistry).GenerateAll(types, ns);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in StreamWriterGenerator for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate BufferWriters class (uses shared virtual registries)
                    new BufferWriterGenerator(sb, _registry, virtualMapRegistry, virtualTupleRegistry).GenerateAll(types, ns);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in BufferWriterGenerator for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate SizeCalculators class (uses shared virtual registries)
                    new SizeCalculatorGenerator(sb, _registry, virtualMapRegistry, virtualTupleRegistry).GenerateAll(types, ns);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in SizeCalculatorGenerator for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate KeyValue structs AFTER all types are registered
                    var keyValueGenerator = new KeyValueClassGenerator(sb, virtualMapRegistry);
                    keyValueGenerator.GenerateAllKeyValueClasses();
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in KeyValueClassGenerator for namespace '{ns}'", ex);
                }

                try
                {
                    // Collect tags from virtual types (Map entries, Tuples) registered by generators
                    tagsGenerator.CollectTagsFromMapEntries(virtualMapRegistry.GetAllTypes());
                    tagsGenerator.CollectTagsFromTuples(virtualTupleRegistry.GetAllTypes());

                    // Generate Tags class AFTER collecting all tags (base + virtual)
                    tagsGenerator.Generate();

                    // Insert Tags class at the beginning (after header, before other classes)
                    if (tagsSb.Length > 0)
                    {
                        sb.Insert(insertPosition, tagsSb.ToString());
                    }
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error generating Tags class for namespace '{ns}'", ex);
                }

                WriteFooter(sb);

                return ($"{ns}.Serialization.cs", sb.ToString());
            }
            catch (System.NullReferenceException ex)
            {
                throw new System.Exception($"NullReferenceException while generating code for namespace '{ns}'. Message: {ex.Message}", ex);
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
                var className = TypeNameHelper.GetClassName(type.FullName);

                // Deserialize method - creates new instance (ReadOnlySpan<byte> overload)
                sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(ReadOnlySpan<byte> data)");
                sb.StartNewBlock();
                sb.AppendIndentedLine("var reader = new SpanReader(data);");
                sb.AppendIndentedLine($"return SpanReaders.Read{className}(ref reader);");
                sb.EndBlock();
                sb.AppendNewLine();

                // Deserialize method - byte[] overload for compatibility with Reflection
                sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(byte[] data)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"return Deserialize{className}(new ReadOnlySpan<byte>(data));");
                sb.EndBlock();
                sb.AppendNewLine();

                // Populate method - fills existing instance (zero object allocation)
                // Generate even for readonly structs (but Populate will be no-op for them)
                if (!type.IsAbstract)
                {
                    sb.AppendIndentedLine($"public static void Populate{className}(ReadOnlySpan<byte> data, global::{type.FullName} instance)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine("var reader = new SpanReader(data);");
                    sb.AppendIndentedLine($"SpanReaders.Populate{className}(ref reader, instance);");
                    sb.EndBlock();
                    sb.AppendNewLine();

                    // Populate method - byte[] overload for compatibility with Reflection
                    sb.AppendIndentedLine($"public static void Populate{className}(byte[] data, global::{type.FullName} instance)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"Populate{className}(new ReadOnlySpan<byte>(data), instance);");
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
                var className = TypeNameHelper.GetClassName(type.FullName);

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
                sb.AppendIndentedLine("writer.Flush();");
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
    }
}
