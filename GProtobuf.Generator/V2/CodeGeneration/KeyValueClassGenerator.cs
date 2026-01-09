using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates nested KeyValue structs for Dictionary serialization.
    /// These structs are used as zero-allocation intermediate objects during serialization/deserialization.
    /// Using struct ensures no heap allocations for the KeyValue wrapper itself.
    /// </summary>
    internal class KeyValueClassGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly VirtualMapTypeRegistry _mapRegistry;

        public KeyValueClassGenerator(
            StringBuilderWithIndent sb,
            VirtualMapTypeRegistry mapRegistry)
        {
            _sb = sb;
            _mapRegistry = mapRegistry;
        }

        /// <summary>
        /// Generates all KeyValue nested classes for registered map types.
        /// </summary>
        public void GenerateAllKeyValueClasses()
        {
            var allMapTypes = _mapRegistry.GetAllTypes();
            if (allMapTypes == null || allMapTypes.Count == 0)
                return;

            _sb.AppendLine("");
            _sb.AppendIndentedLine("// Generated KeyValue classes for Dictionary serialization");

            foreach (var mapInfo in allMapTypes)
            {
                GenerateKeyValueClass(mapInfo);
            }
        }

        /// <summary>
        /// Generates methods that work directly with KeyValue structs.
        /// These methods wrap the existing MapEntry methods.
        /// </summary>
        public void GenerateKeyValueMethods(string generatorType)
        {
            var allMapTypes = _mapRegistry.GetAllTypes();
            if (allMapTypes == null || allMapTypes.Count == 0)
                return;

            _sb.AppendLine("");
            _sb.AppendIndentedLine($"// KeyValue methods for {generatorType}");

            foreach (var mapInfo in allMapTypes)
            {
                var className = GetKeyValueClassName(mapInfo);
                var mapEntryName = mapInfo.TypeName;

                switch (generatorType)
                {
                    case "SpanReaders":
                        GenerateReadKeyValueMethod(className, mapEntryName);
                        break;
                    case "StreamWriters":
                        GenerateWriteKeyValueMethod(className, mapEntryName, "global::GProtobuf.Core.StreamWriter", "StreamWriters");
                        break;
                    case "BufferWriters":
                        GenerateWriteKeyValueMethod(className, mapEntryName, "global::GProtobuf.Core.BufferWriter", "BufferWriters");
                        break;
                    case "SizeCalculators":
                        GenerateSizeKeyValueMethod(className, mapEntryName);
                        break;
                }
            }
        }

        private void GenerateReadKeyValueMethod(string className, string mapEntryName)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static {className} Read{className}(ref SpanReader reader)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var entry = SpanReaders.Read{mapEntryName}(ref reader);");
            _sb.AppendIndentedLine($"return new {className} {{ Key = entry.key, Value = entry.value }};");
            _sb.EndBlock();
        }

        private void GenerateWriteKeyValueMethod(string className, string mapEntryName, string writerTypeName, string generatorClassName)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static void Write{className}(ref {writerTypeName} writer, {className} keyValue)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{generatorClassName}.Write{mapEntryName}(ref writer, keyValue.Key, keyValue.Value);");
            _sb.EndBlock();
        }

        private void GenerateSizeKeyValueMethod(string className, string mapEntryName)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static void Calculate{className}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {className} keyValue)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{mapEntryName}Size(ref calculator, keyValue.Key, keyValue.Value);");
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates a single KeyValue class for a specific map entry type.
        /// </summary>
        private void GenerateKeyValueClass(VirtualMapEntryInfo mapInfo)
        {
            var className = GetKeyValueClassName(mapInfo);

            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public struct {className}");
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Generate Key property
            GenerateKeyProperty(mapInfo);

            // Generate Value property
            GenerateValueProperty(mapInfo);

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates the Key property with ProtoMember(1).
        /// </summary>
        private void GenerateKeyProperty(VirtualMapEntryInfo mapInfo)
        {
            var keyType = GetPropertyType(mapInfo.KeyType, mapInfo.KeyTypeInfo);

            _sb.AppendIndentedLine($"public {keyType} Key {{ get; set; }}");
            _sb.AppendLine("");
        }

        /// <summary>
        /// Generates the Value property with ProtoMember(2).
        /// APPROACH A: Nested dictionaries keep their Dictionary&lt;K,V&gt; type (no conversion to List).
        /// </summary>
        private void GenerateValueProperty(VirtualMapEntryInfo mapInfo)
        {
            var valueType = GetPropertyType(mapInfo.ValueType, mapInfo.ValueTypeInfo);

            _sb.AppendIndentedLine($"public {valueType} Value {{ get; set; }}");
        }

        /// <summary>
        /// Determines the property type.
        /// APPROACH A: Nested dictionaries remain as Dictionary&lt;K,V&gt;, NOT converted to List&lt;KeyValue&gt;.
        ///
        /// RATIONALE:
        /// - Native Dictionary provides O(1) lookups after deserialization
        /// - No conversion overhead from List → Dictionary
        /// - Simpler API for users (direct access)
        /// - Matches protobuf spec semantics
        /// </summary>
        private string GetPropertyType(string originalType, TypeAnalysisInfo typeInfo)
        {
            // For dictionaries, keep the original Dictionary<K,V> type
            // Virtual map readers return (bool, K, Dictionary<K2,V2>) directly
            if (typeInfo.IsDictionary)
            {
                var keyType = EnsureGlobalPrefix(typeInfo.DictionaryKeyType);
                var valueType = EnsureGlobalPrefix(typeInfo.DictionaryValueType);
                return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            // For all other types, use as-is with global:: prefix
            return EnsureGlobalPrefix(originalType);
        }

        /// <summary>
        /// Gets the KeyValue class name from map info.
        /// Example: KeyValue_Int32_String
        /// Uses VirtualTypeNameGenerator for consistent naming.
        /// </summary>
        private string GetKeyValueClassName(VirtualMapEntryInfo mapInfo)
        {
            var keyName = VirtualTypeNameGenerator.GetSafeTypeName(mapInfo.KeyType);
            var valueName = VirtualTypeNameGenerator.GetSafeTypeName(mapInfo.ValueType);

            return $"KeyValue_{keyName}_{valueName}";
        }

        /// <summary>
        /// Ensures type has global:: prefix if needed.
        /// </summary>
        private string EnsureGlobalPrefix(string typeName)
        {
            if (typeName.StartsWith("global::"))
                return typeName;

            // Handle arrays
            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);

                // Primitive arrays (int[], string[], byte[]) don't need global::
                if (IsPrimitiveType(elementType))
                    return typeName;

                // System type arrays (System.Int32[], System.String[]) don't need global::
                if (elementType.StartsWith("System."))
                    return typeName;

                // Custom type arrays need global:: before the element type
                return $"global::{elementType}[]";
            }

            // Primitive types don't need global::
            if (IsPrimitiveType(typeName))
                return typeName;

            // System types don't need global::
            if (typeName.StartsWith("System."))
                return typeName;

            return $"global::{typeName}";
        }

        /// <summary>
        /// Checks if type is primitive.
        /// </summary>
        private bool IsPrimitiveType(string typeName)
        {
            var primitives = new[] { "int", "long", "short", "byte", "sbyte",
                "uint", "ulong", "ushort", "float", "double", "bool", "char", "string" };
            return primitives.Contains(typeName);
        }
    }
}
