using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;
using Microsoft.CodeAnalysis;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Orchestrates all code generation for protobuf serialization/deserialization.
    /// Coordinates TypeRegistry, virtual type generators, and modular code generators.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture (Modular V2 Design):</b></para>
    /// - TypeRegistry: Central type metadata store
    /// - Virtual Type Registries: Track compiler-generated types (Map entries, Tuples)
    /// - Code Generators: SpanReader, BufferWriter, StreamWriter, SizeCalculator, Tags, KeyValue
    /// - Output: One {Namespace}.Serialization.cs file per namespace + shared GProtobuf.Generated.Serialization.cs
    ///
    /// <para><b>Generated Code Structure:</b></para>
    /// Each .cs file contains:
    /// - Tags: Static readonly byte[] precomputed tags (optimization)
    /// - Deserializers: Public entry point methods (Deserialize{Type}(ReadOnlySpan&lt;byte&gt;))
    /// - Serializers: Public entry point methods (Serialize{Type}(T obj, IBufferWriter&lt;byte&gt;))
    /// - SpanReaders: Internal Read{Type} methods (ref SpanReader -> T)
    /// - StreamWriters: Internal Write{Type} methods (T -> Stream)
    /// - BufferWriters: Internal Write{Type} methods (T -> IBufferWriter)
    /// - SizeCalculators: Internal Calculate{Type}Size methods (T -> int)
    /// - KeyValue{K}{V}: Generated structs for Dictionary&lt;K,V&gt; serialization
    ///
    /// <para><b>Virtual Types:</b></para>
    /// - Map entries: KeyValue{K}{V} structs for Dictionary serialization (generated ONCE in GProtobuf.Generated)
    /// - Tuples: Generated for nested tuples in collections (e.g., List&lt;(int, string)&gt;)
    ///
    /// <para><b>Code Generation Order:</b></para>
    /// 1. Register enums in constructor
    /// 2. AddType() called by SerializerGenerator for each [ProtoContract] type
    /// 3. GenerateCode() first collects all virtual types, then generates shared file, then per-namespace files
    /// 4. Tags collected from all types (base + virtual)
    /// 5. Code generators execute (Span, Stream, Buffer, Size, KeyValue)
    /// 6. Tags class inserted at beginning of file
    /// </remarks>
    public class ObjectTreeV2
    {
        private readonly TypeRegistry _registry = new();
        private readonly Microsoft.CodeAnalysis.Compilation _compilation;
        private readonly Dictionary<string, List<StandaloneTypeInfo>> _standaloneTypesByNamespace = new();
        private readonly GeneratorOptions _options;


        #region Type Registration

        /// <summary>
        /// Initializes a new ObjectTreeV2 with enum types pre-registered.
        /// Called by SerializerGenerator after Roslyn analysis phase.
        /// </summary>
        /// <param name="enumTypes">Set of fully qualified enum type names discovered during analysis.</param>
        /// <param name="compilation">
        /// Roslyn Compilation context (optional).
        /// Used for advanced type resolution in VirtualMapTypeRegistry (e.g., nested generic constraints).
        /// </param>
        /// <param name="standaloneTypes">
        /// Types specified via [assembly: GenerateSerializer(typeof(...))] for standalone serialization.
        /// </param>
        /// <param name="options">
        /// Generator options from [assembly: GProtobufOptions(...)]. Controls which generators are enabled.
        /// </param>
        public ObjectTreeV2(HashSet<string> enumTypes, Microsoft.CodeAnalysis.Compilation compilation = null, ImmutableArray<ITypeSymbol> standaloneTypes = default, GeneratorOptions options = null)
        {
            _compilation = compilation;
            _options = options ?? GeneratorOptions.Default;

            // Register all enum types in the TypeRegistry
            if (enumTypes != null)
            {
                foreach (var enumType in enumTypes)
                {
                    _registry.RegisterEnum(enumType);
                }
            }

            // Analyze and register standalone types
            if (!standaloneTypes.IsDefault)
            {
                foreach (var typeSymbol in standaloneTypes)
                {
                    var info = StandaloneTypeAnalyzer.Analyze(typeSymbol);
                    if (info != null)
                    {
                        if (!_standaloneTypesByNamespace.TryGetValue(info.TargetNamespace, out var list))
                        {
                            list = new List<StandaloneTypeInfo>();
                            _standaloneTypesByNamespace[info.TargetNamespace] = list;
                        }
                        list.Add(info);
                    }
                }
            }
        }

        /// <summary>
        /// Registers a type definition for code generation.
        /// Called by SerializerGenerator for each [ProtoContract] or [ProtoInclude] type discovered.
        /// </summary>
        /// <param name="namespace">Namespace where type is declared (determines output .cs file).</param>
        /// <param name="type">Type metadata captured from Roslyn analysis.</param>
        public void AddType(string @namespace, TypeDefinition type)
        {
            _registry.Register(@namespace, type);
        }

        #endregion

        #region Code Generation

        /// <summary>
        /// Generates serialization code for all registered types.
        /// Produces one {Namespace}.Serialization.cs file per namespace.
        /// Uses global virtual type registries to avoid duplicate method generation.
        /// </summary>
        /// <returns>
        /// Collection of (FileName, FileCode) tuples.
        /// Each tuple represents one generated .cs file.
        /// </returns>
        /// <remarks>
        /// <para><b>Output Files:</b></para>
        /// - MyApp.Models.Serialization.cs (for MyApp.Models namespace)
        /// - MyApp.DTOs.Serialization.cs (for MyApp.DTOs namespace)
        ///
        /// <para><b>Error Handling:</b></para>
        /// Exceptions during generation include namespace context in message for debugging.
        /// </remarks>
        public IEnumerable<(string FileName, string FileCode)> GenerateCode()
        {
            // Collect all namespaces (from registry + standalone types)
            var allNamespaces = new HashSet<string>(_registry.GetAllNamespaces());
            foreach (var ns in _standaloneTypesByNamespace.Keys)
            {
                allNamespaces.Add(ns);
            }

            var globalTupleRegistry = new VirtualTupleTypeRegistry();
            var globalMapRegistry = new VirtualMapTypeRegistry(globalTupleRegistry, _registry, _compilation);

            
            foreach (var ns in allNamespaces)
            {
                yield return GenerateCodeForNamespace(ns, globalMapRegistry, globalTupleRegistry);
            }
        }

        private (string FileName, string FileCode) GenerateCodeForNamespace(
            string ns, VirtualMapTypeRegistry globalMapRegistry, VirtualTupleTypeRegistry globalTupleRegistry)
        {
            try
            {
                var sb = new StringBuilderWithIndent();
                var types = _registry.GetByNamespace(ns).ToList();

                WriteHeader(sb, ns);

                // Get standalone types for this namespace
                var standaloneTypes = _standaloneTypesByNamespace.TryGetValue(ns, out var list) ? list : new List<StandaloneTypeInfo>();

                try
                {
                    // Generate Deserializers class (entry point methods)
                    GenerateDeserializers(sb, types, standaloneTypes);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateDeserializers for namespace '{ns}'", ex);
                }

                try
                {
                    // Generate Serializers class (entry point methods)
                    GenerateSerializers(sb, types, standaloneTypes);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateSerializers for namespace '{ns}'", ex);
                }

                // Generate SpanReaders class (uses global registries for deduplication)
                if (_options.GenerateSpanReader)
                {
                    try
                    {
                        var spanReaderGenerator = new SpanReaderGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry);
                        spanReaderGenerator.GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in SpanReaderGenerator for namespace '{ns}'. Inner: {ex.Message}. Stack: {ex.StackTrace}", ex);
                    }
                }

                // Generate StreamReaders class (uses global registries for deduplication)
                if (_options.GenerateStreamReader)
                {
                    try
                    {
                        new StreamReaderGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry).GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in StreamReaderGenerator for namespace '{ns}'. Inner: {ex.Message}. Stack: {ex.StackTrace}", ex);
                    }
                }

                // Generate StreamWriters class (uses global registries for deduplication)
                if (_options.GenerateStreamWriter)
                {
                    try
                    {
                        new StreamWriterGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry).GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in StreamWriterGenerator for namespace '{ns}'", ex);
                    }
                }

                // Generate BufferWriters class (uses global registries for deduplication)
                if (_options.GenerateBufferWriter)
                {
                    try
                    {
                        new BufferWriterGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry).GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in BufferWriterGenerator for namespace '{ns}'", ex);
                    }
                }

                // Generate OnePassStreamWriters class (uses global registries for deduplication)
                if (_options.GenerateOnePassStreamWriter)
                {
                    try
                    {
                        new OnePassStreamWriterGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry).GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in OnePassStreamWriterGenerator for namespace '{ns}'", ex);
                    }
                }

                // Generate SizeCalculators class - needed when any writer is enabled (except OnePass which doesn't need size calculation)
                if (_options.GenerateStreamWriter || _options.GenerateBufferWriter)
                {
                    try
                    {
                        new SizeCalculatorGenerator(sb, _registry, globalMapRegistry, globalTupleRegistry).GenerateAll(types, ns);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in SizeCalculatorGenerator for namespace '{ns}'", ex);
                    }
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

        private void GenerateDeserializers(StringBuilderWithIndent sb, List<TypeDefinition> types, List<StandaloneTypeInfo> standaloneTypes)
        {
            // Only generate if at least one reader is enabled
            if (!_options.GenerateSpanReader && !_options.GenerateStreamReader)
            {
                return;
            }

            sb.AppendIndentedLine("public static class Deserializers");
            sb.StartNewBlock();

            foreach (var type in types)
            {
                var className = TypeNameHelper.GetClassName(type.FullName);
                // Reference types can have optional null parameter; value types (structs, enums) cannot
                bool isReferenceType = !type.IsStruct && !type.IsEnum;
                bool canPopulate = !type.IsAbstract;

                if (isReferenceType && canPopulate)
                {
                    // Reference types with optional instance parameter - unified API
                    // Deserialize(data) - creates new instance (for method group compatibility)
                    // Deserialize(data, existingInstance) - populates existing instance

                    if (_options.GenerateSpanReader)
                    {
                        // Simple overload for method group compatibility (Func<ReadOnlySpan<byte>, T>)
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(ReadOnlySpan<byte> data)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new SpanReader(data);");
                        sb.AppendIndentedLine($"return SpanReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Overload with existingInstance for populating existing objects
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(ReadOnlySpan<byte> data, global::{type.FullName} existingInstance)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new SpanReader(data);");
                        sb.AppendIndentedLine("if (!(existingInstance is null))");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine($"SpanReaders.Populate{className}(ref reader, existingInstance);");
                        sb.AppendIndentedLine("return existingInstance;");
                        sb.EndBlock();
                        sb.AppendIndentedLine($"return SpanReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }

                    if (_options.GenerateStreamReader)
                    {
                        // Stream overload (allocates default buffer)
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("Span<byte> buffer = stackalloc byte[global::GProtobuf.Core.StreamReader.DefaultBufferSize];");
                        sb.AppendIndentedLine($"return Deserialize{className}(stream, buffer);");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Stream overload with existingInstance
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream, global::{type.FullName} existingInstance)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("Span<byte> buffer = stackalloc byte[global::GProtobuf.Core.StreamReader.DefaultBufferSize];");
                        sb.AppendIndentedLine($"return Deserialize{className}(stream, buffer, existingInstance);");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Stream overload with buffer - true streaming using PushLimit/PopLimit
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream, Span<byte> buffer)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new global::GProtobuf.Core.StreamReader(stream, buffer);");
                        sb.AppendIndentedLine($"return StreamReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Stream overload with buffer and existingInstance - true streaming using PushLimit/PopLimit
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream, Span<byte> buffer, global::{type.FullName} existingInstance)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new global::GProtobuf.Core.StreamReader(stream, buffer);");
                        sb.AppendIndentedLine("if (!(existingInstance is null))");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine($"StreamReaders.Populate{className}(ref reader, existingInstance);");
                        sb.AppendIndentedLine("return existingInstance;");
                        sb.EndBlock();
                        sb.AppendIndentedLine($"return StreamReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }
                }
                else
                {
                    if (_options.GenerateSpanReader)
                    {
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(ReadOnlySpan<byte> data)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new SpanReader(data);");
                        sb.AppendIndentedLine($"return SpanReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }

                    if (_options.GenerateStreamReader)
                    {
                        // Deserialize method - Stream overload (allocates default buffer)
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("Span<byte> buffer = stackalloc byte[global::GProtobuf.Core.StreamReader.DefaultBufferSize];");
                        sb.AppendIndentedLine($"return Deserialize{className}(stream, buffer);");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Deserialize method - Stream overload with buffer - true streaming using PushLimit/PopLimit
                        sb.AppendIndentedLine($"public static global::{type.FullName} Deserialize{className}(Stream stream, Span<byte> buffer)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new global::GProtobuf.Core.StreamReader(stream, buffer);");
                        sb.AppendIndentedLine($"return StreamReaders.Read{className}(ref reader);");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }
                }

                // =================================================================
                // LEGACY POPULATE API (kept for explicit populate scenarios):
                // void Populate(data, instance) - for when you explicitly want populate
                // =================================================================
                if (canPopulate)
                {
                    if (_options.GenerateSpanReader)
                    {
                        sb.AppendIndentedLine($"public static void Populate{className}(ReadOnlySpan<byte> data, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new SpanReader(data);");
                        sb.AppendIndentedLine($"SpanReaders.Populate{className}(ref reader, {GeneratorHelpers.GetPopulateInstanceArgument(type, "instance")});");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }

                    if (_options.GenerateStreamReader)
                    {
                        // Populate method - Stream overload (allocates default buffer)
                        sb.AppendIndentedLine($"public static void Populate{className}(Stream stream, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("Span<byte> buffer = stackalloc byte[global::GProtobuf.Core.StreamReader.DefaultBufferSize];");
                        sb.AppendIndentedLine($"Populate{className}(stream, buffer, {GeneratorHelpers.GetPopulateInstanceArgument(type, "instance")});");
                        sb.EndBlock();
                        sb.AppendNewLine();

                        // Populate method - Stream overload with custom buffer - true streaming using PushLimit/PopLimit
                        sb.AppendIndentedLine($"public static void Populate{className}(Stream stream, Span<byte> buffer, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine("var reader = new global::GProtobuf.Core.StreamReader(stream, buffer);");
                        sb.AppendIndentedLine($"StreamReaders.Populate{className}(ref reader, {GeneratorHelpers.GetPopulateInstanceArgument(type, "instance")});");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }
                }
            }

            // Generate deserializers for standalone types (List<T>, T[], Dictionary<K,V>)
            if (standaloneTypes.Count > 0)
            {
                var standaloneGenerator = new StandaloneTypeGenerator(sb, _registry, _options);
                standaloneGenerator.GenerateDeserializers(standaloneTypes);
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }

        #endregion

        #region Serializers Class

        private void GenerateSerializers(StringBuilderWithIndent sb, List<TypeDefinition> types, List<StandaloneTypeInfo> standaloneTypes)
        {
            // Only generate if at least one writer is enabled
            if (!_options.GenerateStreamWriter && !_options.GenerateBufferWriter && !_options.GenerateOnePassStreamWriter)
            {
                return;
            }

            sb.AppendIndentedLine("public static class Serializers");
            sb.StartNewBlock();

            foreach (var type in types)
            {
                var className = TypeNameHelper.GetClassName(type.FullName);

                if (_options.GenerateStreamWriter)
                {
                    // Stream serializer
                    sb.AppendIndentedLine($"public static void Serialize{className}(Stream stream, global::{type.FullName} obj)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
                    sb.AppendIndentedLine($"StreamWriters.Write{className}(ref writer, obj);");
                    sb.AppendIndentedLine("writer.Flush();");
                    sb.EndBlock();
                    sb.AppendNewLine();
                }

                if (_options.GenerateBufferWriter)
                {
                    // IBufferWriter serializer
                    sb.AppendIndentedLine($"public static void Serialize{className}(IBufferWriter<byte> buffer, global::{type.FullName} obj)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
                    sb.AppendIndentedLine($"BufferWriters.Write{className}(ref writer, obj);");
                    sb.AppendIndentedLine("writer.Flush();");
                    sb.EndBlock();
                    sb.AppendNewLine();
                }

                if (_options.GenerateOnePassStreamWriter)
                {
                    // OnePassStreamWriter serializer (one-pass approach using memory pooling)
                    sb.AppendIndentedLine($"public static void Serialize{className}OnePass(Stream stream, global::{type.FullName} obj)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.OnePassStreamWriter(stream, stackalloc byte[256]);");
                    sb.AppendIndentedLine($"OnePassStreamWriters.Write{className}(ref writer, obj);");
                    sb.AppendIndentedLine("writer.Flush();");
                    sb.EndBlock();
                    sb.AppendNewLine();
                }
            }

            // Generate serializers for standalone types (List<T>, T[], Dictionary<K,V>)
            if (standaloneTypes.Count > 0)
            {
                var standaloneGenerator = new StandaloneTypeGenerator(sb, _registry, _options);
                standaloneGenerator.GenerateSerializers(standaloneTypes);
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
            sb.AppendIndentedLine("using System.Linq;");
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
