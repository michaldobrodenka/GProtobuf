using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates StreamReaders class with Read{ClassName} and Read{ClassName}Content methods.
    /// Handles deserialization from StreamReader to object instances.
    ///
    /// DESIGN: StreamReaders methods read directly from StreamReader without SpanReader delegation.
    /// For nested messages, PushLimit/PopLimit is used for zero-allocation nested message reading.
    ///
    /// Pattern:
    /// - ReadXXX(ref StreamReader reader) - reads entire message from stream
    /// - ReadXXXContent(ref StreamReader reader) - reads message fields from stream
    /// - PopulateXXX(ref StreamReader reader, T instance) - populates existing instance
    /// </summary>
    internal class StreamReaderGenerator : GeneratorBase
    {
        private const string ReaderType = "global::GProtobuf.Core.StreamReader";
        private const string ClassName = "StreamReaders";

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry)
        {
        }

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry)
        {
        }

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true)
        {
        }

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string virtualTypesNamespace)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options: null, virtualTypesNamespace: virtualTypesNamespace)
        {
        }

        #region Constructor Analysis

        /// <summary>
        /// Analyzes the best constructor strategy for a type.
        /// For readonly structs with readonly fields, finds matching constructor.
        /// </summary>
        private ConstructorMatcher.ConstructorMatchResult AnalyzeConstructorStrategy(TypeDefinition type)
        {
            // If TypeSymbol not available, fallback to old behavior
            if (type.TypeSymbol == null)
            {
                if (type.HasParameterlessConstructor)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseParameterlessConstructor = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else if (!type.IsStruct)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseFormatterServices = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        ErrorMessage = $"Struct '{type.FullName}' requires TypeSymbol for constructor analysis"
                    };
                }
            }

            // Collect ProtoMember field information
            var protoFields = new List<ConstructorMatcher.FieldInfo>();
            if (type.ProtoMembers != null)
            {
                foreach (var protoMember in type.ProtoMembers)
                {
                    // Find corresponding field or property in TypeSymbol
                    var member = type.TypeSymbol.GetMembers(protoMember.Name).FirstOrDefault();

                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = field.Type,
                            IsReadonly = field.IsReadOnly
                        });
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = property.Type,
                            IsReadonly = property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public
                        });
                    }
                }
            }

            // Use ConstructorMatcher to find best constructor
            return ConstructorMatcher.FindBestConstructor(type.TypeSymbol, protoFields);
        }

        #endregion

        /// <summary>
        /// Generates complete StreamReaders class for all types.
        /// </summary>
        /// <param name="types">Types to generate readers for.</param>
        /// <param name="currentNamespace">Current namespace being generated.</param>
        /// <param name="skipVirtualTypes">If true, skip generating virtual map/tuple methods (they're in shared file).</param>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null, bool skipVirtualTypes = false)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {ClassName}");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateReadMethod(type);
                GenerateReadContentMethod(type);
                GeneratePopulateMethod(type);

                // Generate OwnFieldsPopulate method for derived types (used in ProtoInclude wrapper reading)
                // Skip if type has fields that need temp lists (arrays/IEnumerable) - those can't work with per-field helper
                if (_registry.IsDerivedType(type.FullName) && !HasFieldsNeedingTempList(type))
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GeneratePopulateOwnFieldsMethod(type, className);
                }

                // Generate BaseFieldsOnlyPopulate method for base types with ProtoIncludes
                // Skip if type has fields that need temp lists (arrays/IEnumerable) - those can't work with per-field helper
                if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0 && !HasFieldsNeedingTempList(type))
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GeneratePopulateBaseFieldsOnlyMethod(type, className);
                }
            }

            // Generate ReadContent and OwnFields methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    GenerateReadContentMethod(protoIncludeType);

                    // Also generate OwnFieldsPopulate if it's a derived type without temp list fields
                    if (_registry.IsDerivedType(protoIncludeTypeName) && !HasFieldsNeedingTempList(protoIncludeType))
                    {
                        var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                        GeneratePopulateOwnFieldsMethod(protoIncludeType, className);
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            // Virtual map entry and tuple readers are NOT generated here - they are centralized
            // in GProtobuf.Generated.Serialization.cs via GenerateVirtualTypesOnly().
            // Types are registered during field processing above, then generated once in the shared file.

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates only the virtual map entry and tuple reader methods.
        /// Used for generating the GProtobuf.Generated file that contains all virtual types.
        /// </summary>
        public void GenerateVirtualTypesOnly(string currentNamespace)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {ClassName}");
            _sb.StartNewBlock();

            // Generate all virtual types (ignoring IsGenerated flag)
            GenerateVirtualMapReaders(ignoreIsGeneratedFlag: true);
            GenerateVirtualTupleReaders(ignoreIsGeneratedFlag: true);
            GenerateVirtualCollectionReaders(ignoreIsGeneratedFlag: true);

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates virtual map entry readers for StreamReader.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualMapReaders(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualMapRegistry?.GetAllTypes();
            if (allTypes == null || !allTypes.Any())
                return;

            // Filter types based on IsGenerated flag
            var virtualTypes = ignoreIsGeneratedFlag
                ? allTypes.ToList()
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (virtualTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry StreamReaders");
            _sb.AppendNewLine();

            var generator = new GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry, _virtualTypesNamespace);

            foreach (var virtualType in virtualTypes)
            {
                try
                {
                    generator.GenerateStreamReader(virtualType);
                }
                catch (System.Exception ex)
                {
                    _sb.AppendIndentedLine($"// ERROR generating StreamReader for virtual type {virtualType.TypeName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generates virtual tuple readers for StreamReader.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualTupleReaders(bool ignoreIsGeneratedFlag)
        {
            var allTuples = _virtualTupleRegistry?.GetAllTypes();
            if (allTuples == null || !allTuples.Any())
                return;

            // Filter types based on IsGenerated flag
            var virtualTuples = ignoreIsGeneratedFlag
                ? allTuples
                : allTuples.Where(t => !t.IsGenerated).ToList();

            if (virtualTuples.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple StreamReaders");
            _sb.AppendNewLine();

            var generator = new GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualTupleGenerator(_sb, "Stream", _registry, _virtualTypesNamespace);

            foreach (var tuple in virtualTuples)
            {
                try
                {
                    generator.GenerateStreamReader(tuple);
                }
                catch (System.Exception ex)
                {
                    _sb.AppendIndentedLine($"// ERROR generating StreamReader for tuple {tuple.SafeName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generates virtual collection readers for StreamReader (List, HashSet, Dictionary as nested types).
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">Parameter for consistency with other methods. Collections don't track IsGenerated.</param>
        private void GenerateVirtualCollectionReaders(bool ignoreIsGeneratedFlag)
        {
            // Note: Collections don't have IsGenerated flag - they use inline generation
            // This parameter is for consistency with other methods
            var collectionTypes = _virtualMapRegistry?.GetAllCollectionTypes();
            if (collectionTypes == null || !collectionTypes.Any())
                return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Collection StreamReaders");
            _sb.AppendNewLine();

            foreach (var collection in collectionTypes)
            {
                try
                {
                    GenerateCollectionReader(collection);
                }
                catch (System.Exception ex)
                {
                    _sb.AppendIndentedLine($"// ERROR generating StreamReader for collection {collection.SafeName}: {ex.Message}");
                }
            }
        }

        private void GenerateCollectionReader(GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionInfo collection)
        {
            switch (collection.Kind)
            {
                case GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionKind.List:
                    GenerateListReader(collection);
                    break;
                case GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionKind.HashSet:
                    GenerateHashSetReader(collection);
                    break;
                case GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionKind.Dictionary:
                    GenerateDictionaryReader(collection);
                    break;
                case GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionKind.Array:
                    GenerateArrayReader(collection);
                    break;
            }
        }

        private void GenerateListReader(GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionInfo collection)
        {
            // Use GetGlobalGenericTypeName to handle nested generic types with global:: prefix
            var elementType = TypeMapping.GetGlobalGenericTypeName(collection.ElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(collection.ElementType);

            _sb.AppendIndentedLine($"public static global::System.Collections.Generic.List<{elementType}> Read{collection.SafeName}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var result = new global::System.Collections.Generic.List<{elementType}>();");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            GenerateCollectionElementRead("result", collection.ElementType, collection.ElementTypeInfo, normalizedElementType);

            _sb.EndBlock();
            _sb.AppendIndentedLine("return result;");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateHashSetReader(GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionInfo collection)
        {
            // Use GetGlobalGenericTypeName to handle nested generic types with global:: prefix
            var elementType = TypeMapping.GetGlobalGenericTypeName(collection.ElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(collection.ElementType);

            // Determine the actual HashSet type to use based on FullTypeName
            var (hashSetType, instantiationType) = GetHashSetTypes(collection.FullTypeName, elementType);

            _sb.AppendIndentedLine($"public static {hashSetType} Read{collection.SafeName}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var result = new {instantiationType}();");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            GenerateCollectionElementRead("result", collection.ElementType, collection.ElementTypeInfo, normalizedElementType);

            _sb.EndBlock();
            _sb.AppendIndentedLine("return result;");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateArrayReader(GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionInfo collection)
        {
            // Use GetGlobalGenericTypeName to handle nested generic types with global:: prefix
            var elementType = TypeMapping.GetGlobalGenericTypeName(collection.ElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(collection.ElementType);

            _sb.AppendIndentedLine($"public static {elementType}[] Read{collection.SafeName}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var result = new global::System.Collections.Generic.List<{elementType}>();");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            GenerateCollectionElementRead("result", collection.ElementType, collection.ElementTypeInfo, normalizedElementType);

            _sb.EndBlock();
            _sb.AppendIndentedLine("return result.ToArray();");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateDictionaryReader(GProtobuf.Generator.V2.Handlers.VirtualTypes.VirtualCollectionInfo collection)
        {
            // Use GetGlobalGenericTypeName to handle nested generic types with global:: prefix
            var keyType = TypeMapping.GetGlobalGenericTypeName(collection.DictionaryKeyType);
            var valueType = TypeMapping.GetGlobalGenericTypeName(collection.DictionaryValueType);

            // Determine the actual dictionary type to use based on FullTypeName
            var (dictionaryType, instantiationType) = GetDictionaryTypes(collection.FullTypeName, keyType, valueType);

            _sb.AppendIndentedLine($"public static {dictionaryType} Read{collection.SafeName}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var result = new {instantiationType}();");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            // Read map entry using PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var entryWireType, out var entryFieldId);");
            _sb.AppendIndentedLine("var entryLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var entryOldLimit = reader.PushLimit(entryLength);");
            _sb.AppendNewLine();

            // Initialize key and value
            var keyDefault = GetDefaultValue(collection.DictionaryKeyType, collection.DictionaryKeyTypeInfo);
            var valueDefault = GetDefaultValue(collection.DictionaryValueType, collection.DictionaryValueTypeInfo);
            _sb.AppendIndentedLine($"{keyType} key = {keyDefault};");
            _sb.AppendIndentedLine($"{valueType} value = {valueDefault};");
            _sb.AppendNewLine();

            // Check if the value is a collection type (List, HashSet) which uses repeated field pattern
            // Note: Arrays use packed format (single field 2), but List/HashSet have one field 2 per element
            // IMPORTANT: Must exclude dictionary types first, since Contains-based checks would match nested types
            var valueTypeInfo = collection.DictionaryValueTypeInfo;
            bool isCollectionValue = valueTypeInfo != null &&
                                     (valueTypeInfo.IsList || valueTypeInfo.IsHashSet) &&
                                     !valueTypeInfo.IsDictionary;

            // Read key and value fields
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var fieldWireType, out var fieldId);");
            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            // Key field (fieldId = 1)
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GenerateDictionaryFieldRead("key", collection.DictionaryKeyType, collection.DictionaryKeyTypeInfo, "reader", "fieldWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Value field (fieldId = 2)
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            if (isCollectionValue)
            {
                // For collection values, each occurrence of case 2 adds ONE element
                GenerateDictionaryRepeatedCollectionValueRead("value", collection.DictionaryValueType, valueTypeInfo, "reader", "fieldWireType");
            }
            else
            {
                GenerateDictionaryFieldRead("value", collection.DictionaryValueType, collection.DictionaryValueTypeInfo, "reader", "fieldWireType");
            }
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(fieldWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while reader

            _sb.AppendIndentedLine("reader.PopLimit(entryOldLimit);");
            _sb.AppendIndentedLine("result[key] = value;");

            _sb.EndBlock(); // while reader
            _sb.AppendIndentedLine("return result;");
            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates code to read ONE element of a collection value in a dictionary entry.
        /// For List/HashSet dictionary values, each occurrence of field 2 contains one element (repeated field pattern).
        /// </summary>
        private void GenerateDictionaryRepeatedCollectionValueRead(string varName, string typeName, GProtobuf.Generator.V2.Handlers.VirtualTypes.TypeAnalysisInfo typeInfo, string readerVar, string wireTypeVar)
        {
            var elementType = typeInfo.CollectionElementType ?? "System.Object";
            var elementTypeInfo = typeInfo.CollectionElementTypeInfo;
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);
            // Use GetGlobalGenericTypeName to properly handle custom types with global:: prefix
            var shortElementType = TypeMapping.GetGlobalGenericTypeName(elementType);
            bool isHashSet = typeInfo.IsHashSet;

            // Determine collection creation type
            string collectionCreationType;
            if (isHashSet)
            {
                collectionCreationType = $"global::System.Collections.Generic.HashSet<{shortElementType}>";
            }
            else
            {
                collectionCreationType = $"global::System.Collections.Generic.List<{shortElementType}>";
            }

            // Initialize collection on first element
            _sb.AppendIndentedLine($"if ({varName} == null) {varName} = new {collectionCreationType}();");

            // Add single element based on element type
            if (elementTypeInfo?.IsPrimitive == true || TypeMapping.IsSimpleType(normalizedElementType))
            {
                GenerateDictionaryRepeatedPrimitiveAdd(varName, normalizedElementType, readerVar, wireTypeVar);
            }
            else if (elementTypeInfo?.IsEnum == true || _registry?.IsEnum(elementType) == true)
            {
                // Enums can be packed, need wire type checking
                GeneratePackableCollectionElementRead(varName, readerVar, wireTypeVar,
                    $"{varName}.Add((global::{elementType}){readerVar}.ReadVarInt32());");
            }
            else
            {
                // Complex type - read with PushLimit
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"var elementLen = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var elementOldLimit = {readerVar}.PushLimit(elementLen);");

                string className;
                string nsPrefix;
                // Check if it's a real ProtoContract type registered in the registry
                bool isRegisteredType = _registry?.GetByFullName(normalizedElementType) != null || _registry?.GetByFullName(elementType) != null;
                if (isRegisteredType)
                {
                    // Real ProtoContract type - use its own namespace
                    className = TypeNameHelper.GetClassName(elementType);
                    var typeNs = _registry?.GetNamespaceForType(elementType);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
                }
                else if (IsLocalVirtualType(normalizedElementType))
                {
                    // Virtual type (tuple, collection, etc.) - use GProtobuf.Generated
                    className = VirtualTypeNameGenerator.GetSafeTypeName(elementType);
                    nsPrefix = $"{VirtualTypesPrefix}.";
                }
                else
                {
                    className = TypeNameHelper.GetClassName(elementType);
                    var typeNs = _registry?.GetNamespaceForType(elementType);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
                }

                bool isDerivedType = _registry?.IsDerivedType(elementType) ?? false;
                var readMethodSuffix = isDerivedType ? "" : "Content";
                _sb.AppendIndentedLine($"{varName}.Add({nsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref {readerVar}));");
                _sb.AppendIndentedLine($"{readerVar}.PopLimit(elementOldLimit);");
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
        }

        /// <summary>
        /// Generates code to add primitive element(s) to a collection for repeated field reading.
        /// Handles both packed (wireType=Len) and non-packed formats for packable primitive types.
        /// </summary>
        private void GenerateDictionaryRepeatedPrimitiveAdd(string varName, string normalizedElementType, string readerVar, string wireTypeVar)
        {
            var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, readerVar, wireTypeVar);
            if (readExpr == null)
            {
                _sb.AppendIndentedLine($"// Unsupported primitive type for repeated field: {normalizedElementType}");
                return;
            }

            var addStatement = $"{varName}.Add({readExpr});";

            if (PrimitiveTypeCodeGenerator.IsPackable(normalizedElementType))
            {
                // Packable types need wire type checking for packed/non-packed format
                GeneratePackableCollectionElementRead(varName, readerVar, wireTypeVar, addStatement);
            }
            else
            {
                // Non-packable types (string, Guid, etc.) - just output add line directly
                _sb.AppendIndentedLine(addStatement);
            }
        }

        /// <summary>
        /// Gets the dictionary return type and instantiation type based on the original type name.
        /// Supports ConcurrentDictionary and custom dictionary types.
        /// </summary>
        private (string returnType, string instantiationType) GetDictionaryTypes(string fullTypeName, string keyType, string valueType)
        {
            // Check for ConcurrentDictionary
            if (fullTypeName.Contains("ConcurrentDictionary<"))
            {
                var type = $"global::System.Collections.Concurrent.ConcurrentDictionary<{keyType}, {valueType}>";
                return (type, type);
            }

            // Check for custom dictionary types (like ListDictionary)
            // These types usually have constructors compatible with Dictionary
            if (!fullTypeName.StartsWith("System.Collections.Generic.Dictionary<") &&
                !fullTypeName.StartsWith("global::System.Collections.Generic.Dictionary<"))
            {
                // Extract the custom dictionary type (e.g., "TapHome.Core.Lib.Model.ListDictionary<uint, DataType>")
                var customType = TypeMapping.GetShortTypeName(fullTypeName);
                return (customType, customType);
            }

            // Default to standard Dictionary
            var dictType = $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            return (dictType, dictType);
        }

        /// <summary>
        /// Gets the HashSet return type and instantiation type based on the original type name.
        /// Supports custom HashSet types like ValueLogTypeHashSet.
        /// </summary>
        private (string returnType, string instantiationType) GetHashSetTypes(string fullTypeName, string elementType)
        {
            // Check for custom HashSet types (not standard System.Collections.Generic.HashSet)
            if (!fullTypeName.StartsWith("System.Collections.Generic.HashSet<") &&
                !fullTypeName.StartsWith("global::System.Collections.Generic.HashSet<"))
            {
                // Extract the custom HashSet type (e.g., "TapHome.Core.Lib.Model.ValueLogTypeHashSet")
                var customType = TypeMapping.GetShortTypeName(fullTypeName);
                return (customType, customType);
            }

            // Default to standard HashSet
            var hashSetType = $"global::System.Collections.Generic.HashSet<{elementType}>";
            return (hashSetType, hashSetType);
        }

        private void GenerateCollectionElementRead(string targetCollection, string elementType, GProtobuf.Generator.V2.Handlers.VirtualTypes.TypeAnalysisInfo elementTypeInfo, string normalizedElementType)
        {
            // Read wire type and field id
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");

            // Generate read based on element type
            if (elementTypeInfo?.IsPrimitive == true || TypeMapping.IsSimpleType(normalizedElementType))
            {
                GeneratePrimitiveCollectionElementRead(targetCollection, normalizedElementType);
            }
            else if (elementTypeInfo?.IsEnum == true || _registry?.IsEnum(elementType) == true)
            {
                _sb.AppendIndentedLine($"{targetCollection}.Add((global::{elementType})reader.ReadVarInt32());");
            }
            else
            {
                // Complex type - read submessage using PushLimit for zero-allocation nested message reading
                _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var itemOldLimit = reader.PushLimit(itemLength);");

                // Check if it's a real ProtoContract type registered in the registry
                bool isRegisteredType = _registry?.GetByFullName(normalizedElementType) != null || _registry?.GetByFullName(elementType) != null;
                string className;
                string nsPrefix;
                if (isRegisteredType)
                {
                    // Real ProtoContract type - use its own namespace
                    className = TypeNameHelper.GetClassName(elementType);
                    var elementNs = _registry?.GetNamespaceForType(elementType);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(elementNs, _currentNamespace);
                }
                else if (IsLocalVirtualType(normalizedElementType))
                {
                    // Virtual type (tuple, collection, etc.) - use GProtobuf.Generated
                    className = VirtualTypeNameGenerator.GetSafeTypeName(elementType);
                    nsPrefix = $"{VirtualTypesPrefix}.";
                }
                else
                {
                    className = TypeNameHelper.GetClassName(elementType);
                    var elementNs = _registry?.GetNamespaceForType(elementType);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(elementNs, _currentNamespace);
                }

                bool isDerivedType = _registry?.IsDerivedType(elementType) ?? false;
                var readMethodSuffix = isDerivedType ? "" : "Content";
                _sb.AppendIndentedLine($"{targetCollection}.Add({nsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref reader));");
                _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
            }
        }

        private void GeneratePrimitiveCollectionElementRead(string targetCollection, string normalizedElementType)
        {
            var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, "reader", "wireType");
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"{targetCollection}.Add({readExpr});");
            }
            else
            {
                _sb.AppendIndentedLine($"// WARNING: Unsupported primitive collection element type: {normalizedElementType}");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
        }

        private void GenerateDictionaryFieldRead(string targetVar, string typeName, GProtobuf.Generator.V2.Handlers.VirtualTypes.TypeAnalysisInfo typeInfo, string readerVar, string wireTypeVar)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);

            if (typeInfo?.IsPrimitive == true || TypeMapping.IsSimpleType(normalizedType))
            {
                GenerateDictionaryPrimitiveRead(targetVar, normalizedType, readerVar, wireTypeVar);
            }
            else if (typeInfo?.IsEnum == true || _registry?.IsEnum(typeName) == true)
            {
                _sb.AppendIndentedLine($"{targetVar} = (global::{typeName}){readerVar}.ReadVarInt32();");
            }
            else
            {
                // Complex type - use PushLimit for zero-allocation nested message reading
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"var {targetVar}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var {targetVar}OldLimit = {readerVar}.PushLimit({targetVar}Len);");

                // Check if it's a real ProtoContract type registered in the registry
                bool isRegisteredType = _registry?.GetByFullName(normalizedType) != null || _registry?.GetByFullName(typeName) != null;
                string className;
                string nsPrefix;
                if (isRegisteredType)
                {
                    // Real ProtoContract type - use its own namespace
                    className = TypeNameHelper.GetClassName(typeName);
                    var typeNs = _registry?.GetNamespaceForType(typeName);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
                }
                else if (IsLocalVirtualType(normalizedType))
                {
                    // Virtual type (tuple, collection, etc.) - use GProtobuf.Generated
                    className = VirtualTypeNameGenerator.GetSafeTypeName(typeName);
                    nsPrefix = $"{VirtualTypesPrefix}.";
                }
                else
                {
                    className = TypeNameHelper.GetClassName(typeName);
                    var typeNs = _registry?.GetNamespaceForType(typeName);
                    nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
                }

                bool isDerivedType = _registry?.IsDerivedType(typeName) ?? false;
                var readMethodSuffix = isDerivedType ? "" : "Content";
                _sb.AppendIndentedLine($"{targetVar} = {nsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref {readerVar});");
                _sb.AppendIndentedLine($"{readerVar}.PopLimit({targetVar}OldLimit);");

                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
        }

        /// <summary>
        /// Checks if a type is a local virtual type (tuple or collection) that is generated
        /// in the local StreamReaders class rather than an external namespace.
        /// </summary>
        private static bool IsLocalVirtualType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return false;

            // Tuples are always local virtual types
            if (TupleHandler.IsTupleType(typeName))
                return true;

            // System collections (List, HashSet, Dictionary) used as nested types are local virtual types
            if (typeName.StartsWith("System.Collections.Generic.List<") ||
                typeName.StartsWith("System.Collections.Generic.HashSet<") ||
                typeName.StartsWith("System.Collections.Generic.Dictionary<") ||
                typeName.StartsWith("System.Collections.Concurrent.ConcurrentDictionary<"))
                return true;

            // Custom dictionary types (like ListDictionary) containing "Dictionary<" are also local virtual types
            if (typeName.Contains("Dictionary<"))
                return true;

            // Custom HashSet types (like ValueLogTypeHashSet) containing "HashSet" but not in System namespace
            // are also local virtual types if they're registered as collection types
            if (typeName.Contains("HashSet"))
                return true;

            // Arrays (except byte[]) are handled as virtual types
            if (typeName.EndsWith("[]") && typeName != "System.Byte[]")
                return true;

            return false;
        }

        private void GenerateDictionaryPrimitiveRead(string targetVar, string normalizedType, string readerVar, string wireTypeVar)
        {
            // Special case for byte[] - keep original inline pattern for consistency
            if (normalizedType == "System.Byte[]")
            {
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"var {targetVar}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"{targetVar} = {readerVar}.GetSlice({targetVar}Len).ToArray();");
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                return;
            }

            var assignment = PrimitiveTypeCodeGenerator.GetAssignmentStatement(normalizedType, targetVar, readerVar, wireTypeVar);
            if (assignment != null)
            {
                _sb.AppendIndentedLine(assignment);
            }
            else
            {
                _sb.AppendIndentedLine($"// WARNING: Unsupported dictionary field type: {normalizedType}");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
        }

        private string GetDefaultValue(string typeName, GProtobuf.Generator.V2.Handlers.VirtualTypes.TypeAnalysisInfo typeInfo)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);

            if (typeInfo?.IsString == true || normalizedType == "System.String")
                return "null";

            if (typeInfo?.IsPrimitive == true || TypeMapping.IsSimpleType(normalizedType))
                return "default";

            if (typeInfo?.IsEnum == true || _registry?.IsEnum(typeName) == true)
                return "default";

            // Complex type
            return "default";
        }

        #region Read Method

        /// <summary>
        /// Generates Read{ClassName}(ref StreamReader reader) method.
        /// Entry point for deserialization from Stream.
        /// </summary>
        private void GenerateReadMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref {ReaderType} reader)");
            _sb.StartNewBlock();

            // Simple delegation pattern:
            // For messages without known length prefix, we read field by field
            // For messages with length prefix, we can create a sub-reader

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                // Simple case - no inheritance
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else
            {
                // Has inheritance - handle via ReadContent which processes all field IDs
                GenerateReadMethodWithInheritance(type, className, nsPrefix);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateReadMethodWithInheritance(TypeDefinition type, string className, string nsPrefix)
        {
            // Check if this is ProtoInclude-based inheritance or flat inheritance
            bool hasProtoInclude = type.ProtoIncludes != null && type.ProtoIncludes.Count > 0;
            bool isProtoIncludeDerived = _registry.IsDerivedType(type.FullName);
            bool isFlatInheritance = _registry.HasFlatInheritance(type.FullName);

            if (isProtoIncludeDerived)
            {
                // For ProtoInclude derived types, generate code that handles the full inheritance chain
                // using PushLimit/PopLimit for zero-allocation nested message reading
                GenerateReadMethodForDerived(type, className);
            }
            else if (hasProtoInclude)
            {
                // Base type with ProtoIncludes - delegate to ReadContent which has ProtoInclude cases
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else if (isFlatInheritance)
            {
                // Flat inheritance (no ProtoInclude) - just read fields
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else
            {
                // Default - delegate to ReadContent
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
        }

        /// <summary>
        /// Generates Read method for derived type that handles the full inheritance chain.
        /// Reads all ancestor fields and navigates through nested ProtoInclude wrappers
        /// using PushLimit/PopLimit for zero-allocation streaming.
        /// </summary>
        private void GenerateReadMethodForDerived(TypeDefinition type, string className)
        {
            // Create instance of the derived type
            GenerateObjectCreation(type, "result");
            _sb.AppendNewLine();

            // Get inheritance chain: [Root, ..., Parent, This]
            var chain = _registry.GetInheritanceChain(type.FullName);

            // Collect all fields needing temp lists from the entire inheritance chain
            var fieldsNeedingTempList = new List<ProtoMemberAttribute>();
            foreach (var typeName in chain)
            {
                var typeInChain = _registry.GetByFullName(typeName);
                if (typeInChain?.ProtoMembers != null)
                {
                    var tempListFields = typeInChain.ProtoMembers
                        .Where(m => m.IsCollection && (
                            m.CollectionKind == CollectionKind.Array ||
                            (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                             TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                             !m.Type.Contains("ICollection") &&
                             !m.Type.Contains("IList"))
                        ))
                        .ToList();

                    fieldsNeedingTempList.AddRange(tempListFields);
                }
            }

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();
            var fieldsUsingTempList = fieldsNeedingTempList.Where(m => !ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for non-class collection fields from inheritance chain
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if (fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
            }

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Generate nested reading for each level using PushLimit/PopLimit
            GenerateNestedReading(chain, 0, "reader");

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "result");

            // Convert temp lists to arrays or assign to IEnumerable properties
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "result");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Recursively generates code to read fields at a specific level of the inheritance chain.
        /// Uses PushLimit/PopLimit for zero-allocation nested message reading.
        /// </summary>
        private void GenerateNestedReading(IReadOnlyList<string> chain, int levelIndex, string readerVar)
        {
            if (levelIndex >= chain.Count)
                return;

            var currentTypeName = chain[levelIndex];
            var currentType = _registry.GetByFullName(currentTypeName);
            if (currentType == null)
                return;

            var wireTypeVar = levelIndex == 0 ? "wireType" : $"wireType{levelIndex}";
            var fieldIdVar = levelIndex == 0 ? "fieldId" : $"fieldId{levelIndex}";

            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{readerVar}.ReadWireTypeAndFieldId(out var {wireTypeVar}, out var {fieldIdVar});");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine($"switch ({fieldIdVar})");
            _sb.StartNewBlock();

            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    GenerateNestedProtoIncludeCase(protoInclude, chain, levelIndex + 1, readerVar);
                }
            }

            if (currentType.ProtoMembers != null)
            {
                foreach (var member in currentType.ProtoMembers)
                {
                    GenerateFieldReadCaseForDerived(member, wireTypeVar, readerVar);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while
        }

        /// <summary>
        /// Generates switch case for ProtoInclude that enters nested level.
        /// Uses PushLimit/PopLimit for zero-allocation nested message reading.
        /// </summary>
        private void GenerateNestedProtoIncludeCase(ProtoIncludeAttribute include, IReadOnlyList<string> chain, int nextLevelIndex, string currentReaderVar)
        {
            _sb.AppendIndentedLine($"case {include.FieldId}: {{");
            _sb.IncreaseIndent();

            // Read length and push limit (zero-allocation pattern)
            _sb.AppendIndentedLine($"var length{nextLevelIndex} = {currentReaderVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var oldLimit{nextLevelIndex} = {currentReaderVar}.PushLimit(length{nextLevelIndex});");

            // Recursively generate reading for next level using same reader
            GenerateNestedReading(chain, nextLevelIndex, currentReaderVar);

            // Pop limit after reading nested content
            _sb.AppendIndentedLine($"{currentReaderVar}.PopLimit(oldLimit{nextLevelIndex});");

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates switch case for reading a field into 'result' for derived types.
        /// </summary>
        private void GenerateFieldReadCaseForDerived(ProtoMemberAttribute member, string wireTypeVar, string readerVar)
        {
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            // Wire type validation
            GenerateWireTypeValidation(member, wireTypeVar, readerVar);

            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "StreamReaders", _registry, _virtualTypesNamespace);
                mapHandler.GenerateRead(member, $"result.{member.Name}", readerVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBodyForDerived(member, wireTypeVar, readerVar);
            }
            else if (member.IsEnum)
            {
                // Use fully qualified type name for enums to avoid namespace issues
                _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type}){readerVar}.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type, readerVar);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, readerVar, wireTypeVar);
            }
            else if (member.IsProtoVarint)
            {
                // ProtoVarint type - read varint and construct using the constructor
                ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}", readerVar);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type - skip field with warning comment
                _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
            else
            {
                GenerateComplexTypeReadBodyForDerived(member, readerVar);
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        /// <summary>
        /// Generates collection field reading for derived types using PushLimit.
        /// </summary>
        private void GenerateCollectionFieldReadBodyForDerived(ProtoMemberAttribute member, string wireTypeVar, string readerVar)
        {
            if (member.CollectionElementType == null)
            {
                throw new System.Exception($"CollectionElementType is null for collection member '{member.Name}' of type '{member.Type}'");
            }

            // Check if element type is enum (enums use packed encoding like primitives)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
                // Reader must accept both PACKED (for IsPacked=true) and UNPACKED (default) for backward compatibility
                bool shouldBePacked = member.IsPacked;

                if (shouldBePacked)
                {
                    _primitiveHandler.GenerateDualModePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type,
                        wireTypeVar,
                        readerVar,
                        useStreamLimits: true);
                }
                else
                {
                    var fieldIdVar = wireTypeVar.Replace("wireType", "fieldId");
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        member.CollectionKind,
                        member.Type,
                        readerVar,
                        wireTypeVar,
                        fieldIdVar);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    member.FieldId,
                    member.CollectionKind,
                    member.Type,
                    readerVar);
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    elementClassName,
                    member.CollectionKind,
                    member.Type,
                    readerVar,
                    useObjectArrayBuilder: ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry));
            }
        }

        /// <summary>
        /// Generates complex type field reading for derived types using PushLimit.
        /// </summary>
        private void GenerateComplexTypeReadBodyForDerived(ProtoMemberAttribute member, string readerVar)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            // Read length and use PushLimit for zero-allocation nested reading
            _sb.AppendIndentedLine($"var length = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var oldLimit = {readerVar}.PushLimit(length);");

            // For derived types (with ProtoInclude parent), use Read{typeName} to handle ProtoInclude wrapper
            // For non-derived types, use Read{typeName}Content for direct field reading
            bool isDerivedType = _registry?.IsDerivedType(member.Type) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";
            _sb.AppendIndentedLine($"result.{member.Name} = {nsPrefix}StreamReaders.Read{typeName}{readMethodSuffix}(ref {readerVar});");

            _sb.AppendIndentedLine($"{readerVar}.PopLimit(oldLimit);");
        }

        #endregion

        #region ReadContent Method

        private void GenerateReadContentMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();

            if (type.IsEnum)
            {
                _sb.AppendIndentedLine($"return (global::{type.FullName})reader.ReadVarInt32();");
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            if (type.EnableRecursionGuard)
            {
                _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Enter();");
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimpleReadContent(type, className, nsPrefix);
            }
            else
            {
                GenerateReadContentWithInheritance(type, className, nsPrefix);
            }

            if (type.EnableRecursionGuard)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Exit();");
                _sb.EndBlock();
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimpleReadContent(TypeDefinition type, string className, string nsPrefix)
        {
            // Analyze constructor strategy for readonly struct support
            var constructorStrategy = AnalyzeConstructorStrategy(type);

            if (!constructorStrategy.IsSuccess)
            {
                _sb.AppendIndentedLine($"// ERROR: {constructorStrategy.ErrorMessage}");
                _sb.AppendIndentedLine($"throw new global::System.InvalidOperationException(\"Cannot deserialize type '{type.FullName}': {constructorStrategy.ErrorMessage?.Replace("\"", "\\\"")}\");");
                return;
            }

            bool useConstructor = constructorStrategy.Constructor != null &&
                                 constructorStrategy.ParameterMappings != null &&
                                 constructorStrategy.ParameterMappings.Count > 0;

            if (useConstructor)
            {
                // Generate deserialization with constructor call (for readonly structs)
                _sb.AppendIndentedLine($"// Using constructor with {constructorStrategy.ParameterMappings.Count} parameters");
                GenerateReadContentWithConstructor(type, className, constructorStrategy);
                return;
            }

            GenerateObjectCreation(type, "result");
            _sb.AppendIndentedLine($"Populate{className}(ref reader, {GeneratorHelpers.GetPopulateInstanceArgument(type, "result")});");
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates read content for types that use constructor with parameters (readonly struct support).
        /// Creates local variables for constructor parameters and calls constructor at the end.
        /// </summary>
        private void GenerateReadContentWithConstructor(
            TypeDefinition type,
            string className,
            ConstructorMatcher.ConstructorMatchResult constructorStrategy)
        {
            var fullTypeName = $"global::{type.FullName}";
            var mappings = constructorStrategy.ParameterMappings!;

            // Declare local variables for constructor parameters
            _sb.AppendIndentedLine("// Local variables for constructor parameters");
            foreach (var mapping in mappings.OrderBy(m => m.ParameterOrdinal))
            {
                // Use fully qualified type name
                var paramTypeName = mapping.FieldType.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
                _sb.AppendIndentedLine($"{paramTypeName} param_{mapping.ParameterName} = default;");
            }
            _sb.AppendNewLine();

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for fields (sorted by field ID for optimal branch prediction)
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                {
                    // Find mapping for this field
                    var mapping = mappings.FirstOrDefault(m => m.FieldName == member.Name);

                    if (mapping != null)
                    {
                        // This field corresponds to a constructor parameter
                        GenerateFieldReadCaseForParameter(member, $"param_{mapping.ParameterName}");
                    }
                }

                // Default - skip unknown fields
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
            }
            else
            {
                // No fields - just skip
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.EndBlock();

            // Call constructor with parameters
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"// Create instance using constructor");
            var constructorParams = mappings
                .OrderBy(m => m.ParameterOrdinal)
                .Select(m => $"param_{m.ParameterName}")
                .ToList();

            if (constructorParams.Count == 0)
            {
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}();");
            }
            else if (constructorParams.Count <= 3)
            {
                // Inline for short parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}({string.Join(", ", constructorParams)});");
            }
            else
            {
                // Multi-line for long parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}(");
                _sb.IncreaseIndent();
                for (int i = 0; i < constructorParams.Count; i++)
                {
                    var comma = i < constructorParams.Count - 1 ? "," : ");";
                    _sb.AppendIndentedLine($"{constructorParams[i]}{comma}");
                }
                _sb.DecreaseIndent();
            }

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates field read case that assigns to a local variable instead of object field.
        /// Used for constructor-based deserialization.
        /// </summary>
        private void GenerateFieldReadCaseForParameter(ProtoMemberAttribute member, string targetVariable)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");
            _sb.IncreaseIndent();

            var normalizedType = TypeMapping.NormalizeTypeName(member.Type);
            var isZigZag = member.DataFormat == DataFormat.ZigZag;

            // Generate read statement based on type
            switch (normalizedType)
            {
                case "System.Int32":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedInt32();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;

                case "System.UInt32":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedUInt32();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;

                case "System.Int64":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedInt64();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;

                case "System.UInt64":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedUInt64();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;

                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;

                case "System.Single":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;

                case "System.Double":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;

                case "System.String":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                    break;

                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                    break;

                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;

                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;

                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;

                default:
                    // Check if it's an enum
                    if (_registry != null && (_registry.IsEnum(member.Type) || _registry.IsEnum(normalizedType)))
                    {
                        _sb.AppendIndentedLine($"{targetVariable} = (global::{member.Type})reader.ReadVarInt32();");
                    }
                    else if (member.IsProtoVarint)
                    {
                        // ProtoVarint type
                        ProtoVarintTypeSupport.GenerateRead(_sb, member, targetVariable, "reader");
                    }
                    else
                    {
                        // Complex type - use PushLimit for zero-allocation nested message reading
                        _sb.AppendIndentedLine("var nestedLength = reader.ReadVarInt32();");
                        _sb.AppendIndentedLine("var nestedOldLimit = reader.PushLimit(nestedLength);");

                        var simpleName = GProtobuf.Generator.Utilities.TypeNameHelper.GetClassName(member.Type);
                        var typeNs = _registry?.GetNamespaceForType(member.Type) ?? string.Empty;
                        var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);

                        // For derived types (with ProtoInclude parent), use Read{typeName} to handle ProtoInclude wrapper
                        bool isDerivedType = _registry?.IsDerivedType(member.Type) ?? false;
                        var readMethodSuffix = isDerivedType ? "" : "Content";
                        _sb.AppendIndentedLine($"{targetVariable} = {typeNsPrefix}StreamReaders.Read{simpleName}{readMethodSuffix}(ref reader);");
                        _sb.AppendIndentedLine("reader.PopLimit(nestedOldLimit);");
                    }
                    break;
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
        }

        private void GenerateReadContentWithInheritance(TypeDefinition type, string className, string nsPrefix)
        {
            if (type.IsAbstract)
            {
                _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            }
            else
            {
                GenerateObjectCreation(type, "result");
            }
            _sb.AppendNewLine();

            // Track array fields that need temp list
            var fieldsNeedingTempList = type.ProtoMembers?
                .Where(m => m.IsCollection && (
                    m.CollectionKind == CollectionKind.Array ||
                    (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                     TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                     !m.Type.Contains("ICollection") &&
                     !m.Type.Contains("IList"))
                ))
                .ToList();

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList?.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();
            var fieldsUsingTempList = fieldsNeedingTempList?.Where(m => !ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for non-class collection fields
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if ((fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0))
            {
                _sb.AppendNewLine();
            }

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            int protoIncludeCount = type.ProtoIncludes?.Count ?? 0;
            bool useBinaryDispatch = BinaryDispatchAnalyzer.ShouldUseBinaryDispatch(protoIncludeCount);

            if (useBinaryDispatch && protoIncludeCount > 0)
            {
                // Use binary dispatch for ProtoIncludes (O(log n) comparisons)
                GenerateStreamReadContentWithBinaryDispatch(type, nsPrefix);
            }
            else
            {
                // Fall back to switch-based dispatch
                GenerateStreamReadContentWithSwitchDispatch(type, nsPrefix);
            }

            _sb.EndBlock(); // while

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "result");

            // Convert temp lists to arrays if needed
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "result");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates binary dispatch for ProtoIncludes in StreamReader ReadContent method.
        /// </summary>
        private void GenerateStreamReadContentWithBinaryDispatch(TypeDefinition type, string nsPrefix)
        {
            var protoIncludeTree = BinaryDispatchAnalyzer.BuildTree(type.ProtoIncludes);
            var binaryDispatch = new BinaryDispatchGenerator(_sb, "fieldId");

            bool hasProtoMembers = type.ProtoMembers != null && type.ProtoMembers.Count > 0;

            // Optimized range-separated dispatch:
            // - ProtoIncludes checked first (binary dispatch)
            // - Regular fields switch appears ONCE (not duplicated in every branch)
            binaryDispatch.GenerateRangeSeparatedDispatch(
                protoIncludeTree,
                generateProtoIncludeCase: (fieldId, typeName) =>
                {
                    GenerateStreamProtoIncludeReadCaseBody(type, fieldId, typeName, nsPrefix);
                },
                generateRegularFieldsSwitch: () =>
                {
                    // Generate switch for regular fields (sorted by field ID for optimal branch prediction)
                    _sb.AppendIndentedLine("// Regular fields switch");
                    _sb.AppendIndentedLine("switch (fieldId)");
                    _sb.StartNewBlock();

                    // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                    if (type.ProtoMembers != null)
                    {
                        foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                        {
                            GenerateFieldReadCase(member, nsPrefix);
                        }
                    }

                    // Default - skip unknown fields
                    _sb.AppendIndentedLine("default:");
                    _sb.IncreaseIndent();
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    _sb.AppendIndentedLine("break;");
                    _sb.DecreaseIndent();

                    _sb.EndBlock();
                },
                generateSkipField: () =>
                {
                    // Unknown ProtoInclude field ID - skip it
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                }
            );
        }

        /// <summary>
        /// Generates traditional switch dispatch for StreamReader ReadContent method.
        /// </summary>
        private void GenerateStreamReadContentWithSwitchDispatch(TypeDefinition type, string nsPrefix)
        {
            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeReadCase(type, include, nsPrefix);
                }
            }

            // Use sorted dispatch for optimal branch prediction (PGO heuristic)
            if (type.ProtoMembers != null)
            {
                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                {
                    GenerateFieldReadCase(member, nsPrefix);
                }
            }

            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates the body of a StreamReader ProtoInclude case (without case X: prefix).
        /// Used by binary dispatch generator.
        /// </summary>
        private void GenerateStreamProtoIncludeReadCaseBody(TypeDefinition parentType, int fieldId, string derivedTypeName, string nsPrefix)
        {
            var derivedClassName = TypeNameHelper.GetClassName(derivedTypeName);
            var derivedNs = _registry.GetNamespaceForType(derivedTypeName);
            var derivedNsPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNs, _currentNamespace);

            // Use PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var oldLimit = reader.PushLimit(length);");

            // Use StreamReaders for derived content reading
            _sb.AppendIndentedLine($"result = {derivedNsPrefix}StreamReaders.Read{derivedClassName}Content(ref reader);");

            // Pop limit after reading derived content
            _sb.AppendIndentedLine("reader.PopLimit(oldLimit);");

            // Continue to next iteration
            _sb.AppendIndentedLine("continue;");
        }

        #endregion

        #region Wire Type Validation

        private bool ShouldGenerateWireTypeValidation(ProtoMemberAttribute member)
        {
            // Collections with dual-mode (packed/unpacked) support handle wire type internally
            if (member.IsCollection)
            {
                // Only primitive collections with IsPacked=true use dual-mode
                // (default UNPACKED uses non-packed reader which validates wire type itself)
                bool isDualMode = member.IsPacked;
                return !isDualMode;
            }

            return true;
        }

        /// <summary>
        /// Generates wire type validation check.
        /// Wire type validation REMOVED for performance (fail-fast approach).
        /// Read methods now align with Populate methods behavior.
        /// Malformed data will throw exception in ReadXXX methods.
        /// </summary>
        private void GenerateWireTypeValidation(ProtoMemberAttribute member, string wireTypeVar = "wireType", string readerVar = "reader")
        {
            // Wire type validation removed for performance
            // Read methods now use fail-fast approach (same as Populate methods)
            // Malformed data will throw exception in ReadXXX methods instead of graceful skip

            // Skip validation for collections with dual-mode support
            if (!ShouldGenerateWireTypeValidation(member))
                return;

            // Wire type check and skip logic REMOVED
            // Aligns Read behavior with Populate behavior (consistency)
            // Trade-off: Malformed data throws exception instead of silent skip
        }

        #endregion

        #region Field Generation

        private void GenerateFieldReadCase(ProtoMemberAttribute member, string nsPrefix)
        {
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            bool needsBraces = category == FieldCategory.Map ||
                              category == FieldCategory.Collection ||
                              category == FieldCategory.ComplexType ||
                              category == FieldCategory.Tuple ||
                              category == FieldCategory.ProtoVarint;

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            switch (category)
            {
                case FieldCategory.Map:
                    GenerateMapFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Collection:
                    GenerateCollectionFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Enum:
                    _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
                    break;
                case FieldCategory.Tuple:
                    GenerateTupleFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Primitive:
                    GeneratePrimitiveFieldReadBody(member);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}", "reader");
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
                case FieldCategory.ComplexType:
                    GenerateComplexTypeReadBody(member, nsPrefix);
                    break;
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GeneratePrimitiveFieldReadBody(ProtoMemberAttribute member)
        {
            // Use StreamReaders extension methods for type-aware reading
            var typeName = TypeMapping.NormalizeTypeName(member.Type);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (typeName)
            {
                case "System.Int32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedInt32();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedUInt32();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedInt64();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedUInt64();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Int16":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt16(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt16":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt16(ref reader, wireType);");
                    break;
                case "System.Byte":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByte(ref reader, wireType);");
                    break;
                case "System.SByte":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadSByte(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"result.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    // Nullable types
                    if (typeName.StartsWith("System.Nullable<"))
                    {
                        var innerType = typeName.Substring("System.Nullable<".Length, typeName.Length - "System.Nullable<".Length - 1);
                        GeneratePrimitiveFieldReadBodyForType(member, innerType);
                    }
                    else
                    {
                        _sb.AppendIndentedLine($"// WARNING: Unknown primitive type '{typeName}'");
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    }
                    break;
            }
        }

        private void GeneratePrimitiveFieldReadBodyForType(ProtoMemberAttribute member, string innerType)
        {
            var normalizedInnerType = TypeMapping.NormalizeTypeName(innerType);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (normalizedInnerType)
            {
                case "System.Int32":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"result.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unknown nullable inner type '{normalizedInnerType}'");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
            }
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Use MapHandler to generate consistent helper method calls (matching Populate methods pattern)
            // This reuses the existing ReadMapEntry_* helper methods instead of inlining ~80 lines per field
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "StreamReaders", _registry, _virtualTypesNamespace);
            mapHandler.GenerateRead(member, $"result.{member.Name}", "reader");
        }

        private void GenerateCollectionFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));
            bool isPrimitiveCollection = _primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection;

            if (isPrimitiveCollection)
            {
                GeneratePrimitiveCollectionRead(member, isEnumCollection);
            }
            else
            {
                GenerateComplexCollectionRead(member, nsPrefix);
            }
        }

        private void GeneratePrimitiveCollectionRead(ProtoMemberAttribute member, bool isEnumCollection)
        {
            var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);

            // Check for custom collection types (ValueLogTypeHashSet, etc.)
            // Note: IList<T>, ICollection<T> are NOT custom collections - they should use List<T>
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);
            bool isInterfaceCollection = normalizedMemberType.Contains("IList<") ||
                                         normalizedMemberType.Contains("ICollection<");
            bool isCustomCollection = !isInterfaceCollection &&
                                      (TypeHelper.IsCustomHashSetType(normalizedMemberType) ||
                                       TypeHelper.IsCustomListType(normalizedMemberType));

            // Determine if we need temp list
            // Custom collections don't use temp list - they implement ICollection and have Add method
            bool needsTempList = !isCustomCollection && (
                member.CollectionKind == CollectionKind.Array ||
                (member.CollectionKind == CollectionKind.InterfaceCollection &&
                 normalizedMemberType.StartsWith("System.Collections.Generic.IEnumerable<")));

            // Determine collection type name for initialization
            string collectionTypeName;
            if (isCustomCollection)
            {
                // Use original custom type
                collectionTypeName = $"global::{member.Type}";
            }
            else
            {
                bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                collectionTypeName = isHashSet
                    ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                    : $"global::System.Collections.Generic.List<{elementType}>";
            }

            string targetCollection = needsTempList ? $"_tempList_{member.Name}" : $"result.{member.Name}";

            if (needsTempList)
            {
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if (result.{member.Name} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"result.{member.Name} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            // Check if element type can be packed (only numeric/bool types can be packed)
            bool isPackable = IsPackableElementType(normalizedElementType) || isEnumCollection;

            if (isPackable)
            {
                // Check if packed
                _sb.AppendIndentedLine("if (wireType == global::GProtobuf.Core.WireType.Len)");
                _sb.StartNewBlock();

                // Packed - read as sub-reader
                _sb.AppendIndentedLine("var packedLength = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var packedReader = reader.CreateSubReader(packedLength);");
                _sb.AppendIndentedLine("while (!packedReader.IsEnd)");
                _sb.StartNewBlock();

                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})packedReader.ReadVarInt32());");
                }
                else
                {
                    GeneratePackedElementRead(targetCollection, normalizedElementType, member.DataFormat, "packedReader");
                }

                _sb.EndBlock();
                _sb.EndBlock();

                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();

                // Non-packed - single element
                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})reader.ReadVarInt32());");
                }
                else
                {
                    GenerateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
                }

                _sb.EndBlock();
            }
            else
            {
                // Non-packable types (string, bytes) - just read directly
                GenerateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
            }
        }

        /// <summary>
        /// Determines if an element type can be packed in protobuf wire format.
        /// Only numeric types and booleans can be packed. Strings, bytes, and messages cannot.
        /// </summary>
        private static bool IsPackableElementType(string normalizedElementType)
        {
            return normalizedElementType switch
            {
                "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => true,
                "System.Int16" or "System.UInt16" or "System.Byte" or "System.SByte" => true,
                "System.Double" or "System.Single" => true,
                "System.Boolean" => true,
                "System.Char" => true,
                _ => false // strings, bytes, messages, etc. cannot be packed
            };
        }

        private void GeneratePackedElementRead(string targetCollection, string elementType, DataFormat dataFormat, string readerVar)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32() != 0);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported packed element type: {elementType}");
                    break;
            }
        }

        private void GenerateSingleElementRead(string targetCollection, string elementType, DataFormat dataFormat)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32() != 0);");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType));");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader));");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType));");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType));");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType));");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported single element type: {elementType}");
                    break;
            }
        }

        private void GenerateComplexCollectionRead(ProtoMemberAttribute member, string nsPrefix)
        {
            var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
            var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);

            // Check for custom collection types (ValueLogTypeHashSet, etc.)
            // Note: IList<T>, ICollection<T> are NOT custom collections - they should use List<T>
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);
            bool isInterfaceCollection = normalizedMemberType.Contains("IList<") ||
                                         normalizedMemberType.Contains("ICollection<");
            bool isCustomCollection = !isInterfaceCollection &&
                                      (TypeHelper.IsCustomHashSetType(normalizedMemberType) ||
                                       TypeHelper.IsCustomListType(normalizedMemberType));

            // Custom collections don't use temp list - they implement ICollection and have Add method
            bool needsTempList = !isCustomCollection && (
                member.CollectionKind == CollectionKind.Array ||
                (member.CollectionKind == CollectionKind.InterfaceCollection &&
                 normalizedMemberType.StartsWith("System.Collections.Generic.IEnumerable<")));

            // Check if we should use ObjectArrayBuilder for class elements
            bool useObjectArrayBuilder = needsTempList && ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry);

            // Determine collection type name for initialization
            string collectionTypeName;
            if (isCustomCollection)
            {
                // Use original custom type
                collectionTypeName = $"global::{member.Type}";
            }
            else
            {
                bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                collectionTypeName = isHashSet
                    ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                    : $"global::System.Collections.Generic.List<{elementType}>";
            }

            string targetCollection;
            if (useObjectArrayBuilder)
            {
                // ObjectArrayBuilder is always pre-initialized, no lazy init needed
                targetCollection = $"_builder_{member.Name}";
            }
            else if (needsTempList)
            {
                targetCollection = $"_tempList_{member.Name}";
            }
            else
            {
                targetCollection = $"result.{member.Name}";
            }

            // For ObjectArrayBuilder, skip lazy init - it's already initialized before the loop
            // For _tempList_, lazy init as before
            // For result.Property, lazy init the property
            if (useObjectArrayBuilder)
            {
                // No lazy init needed - ObjectArrayBuilder is pre-initialized
            }
            else if (needsTempList)
            {
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if (result.{member.Name} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"result.{member.Name} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            // Handle special types (DateTime, Guid, TimeSpan, byte[]) that have predefined readers in GProtobuf.Core
            // These don't have generated ReadXxxContent methods
            if (normalizedElementType == "System.DateTime" || normalizedElementType == "System.TimeSpan" ||
                normalizedElementType == "System.Byte[]" || normalizedElementType == "System.Guid")
            {
                var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, "reader", "global::GProtobuf.Core.WireType.Len");
                _sb.AppendIndentedLine($"{targetCollection}.Add({readExpr});");
                return;
            }

            // Read nested message using PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var itemOldLimit = reader.PushLimit(itemLength);");

            // Handle tuple types - use centralized virtual types in GProtobuf.Generated
            if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                var tupleSafeName = VirtualTypeNameGenerator.GetSafeTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"{targetCollection}.Add({VirtualTypesPrefix}.StreamReaders.Read{tupleSafeName}Content(ref reader));");
                _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
                return;
            }

            var elementNs = _registry.GetNamespaceForType(member.CollectionElementType);
            var elementNsPrefix = GeneratorHelpers.GetNamespacePrefix(elementNs, _currentNamespace);

            // For derived types (with ProtoInclude parent), use Read{typeName} to handle ProtoInclude wrapper
            bool isDerivedType = _registry?.IsDerivedType(member.CollectionElementType) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";
            _sb.AppendIndentedLine($"{targetCollection}.Add({elementNsPrefix}StreamReaders.Read{elementClassName}{readMethodSuffix}(ref reader));");
            _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
        }

        private void GenerateTupleFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // For tuples, use PushLimit for zero-allocation nested message reading
            var itemTypes = TupleHandler.ParseTupleTypes(member.Type);
            var tupleInfo = _virtualTupleRegistry.Register(member.Type, itemTypes);

            _sb.AppendIndentedLine("var tupleLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var tupleOldLimit = reader.PushLimit(tupleLength);");
            _sb.AppendIndentedLine($"result.{member.Name} = {VirtualTypesPrefix}.StreamReaders.Read{tupleInfo.SafeName}Content(ref reader);");
            _sb.AppendIndentedLine("reader.PopLimit(tupleOldLimit);");
        }

        private void GenerateComplexTypeReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Use PushLimit for zero-allocation nested message reading
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var oldLimit = reader.PushLimit(length);");

            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            // For derived types (with ProtoInclude parent), use Read{typeName} to handle ProtoInclude wrapper
            bool isDerivedType = _registry?.IsDerivedType(member.Type) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";

            // Use StreamReaders for nested content
            _sb.AppendIndentedLine($"result.{member.Name} = {typeNsPrefix}StreamReaders.Read{typeName}{readMethodSuffix}(ref reader);");
            _sb.AppendIndentedLine("reader.PopLimit(oldLimit);");
        }

        private void GenerateProtoIncludeReadCase(TypeDefinition parentType, ProtoIncludeAttribute include, string nsPrefix)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);
            var derivedNs = _registry.GetNamespaceForType(include.Type);
            var derivedNsPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNs, _currentNamespace);

            _sb.AppendIndentedLine($"case {include.FieldId}: {{");
            _sb.IncreaseIndent();

            // Use PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var oldLimit = reader.PushLimit(length);");

            // Use StreamReaders for derived content reading
            _sb.AppendIndentedLine($"result = {derivedNsPrefix}StreamReaders.Read{derivedClassName}Content(ref reader);");

            // Pop limit after reading derived content
            _sb.AppendIndentedLine("reader.PopLimit(oldLimit);");

            // Continue to read any remaining fields after the ProtoInclude wrapper
            _sb.AppendIndentedLine("continue;");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        #endregion

        #region Populate Method

        /// <summary>
        /// Generates Populate{ClassName}(ref StreamReader reader, T instance) method.
        /// Reads fields directly from StreamReader without delegation.
        /// </summary>
        private void GeneratePopulateMethod(TypeDefinition type)
        {
            // Skip abstract types - can't populate them directly
            if (type.IsAbstract)
                return;

            var className = TypeNameHelper.GetClassName(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            // Check if this is a readonly struct with readonly fields
            bool isReadonlyStruct = false;
            if (type.IsStruct && type.TypeSymbol != null)
            {
                bool hasReadonlyFields = type.ProtoMembers?.Any(m =>
                {
                    var member = type.TypeSymbol.GetMembers(m.Name).FirstOrDefault();
                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        return field.IsReadOnly;
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        return property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public;
                    }
                    return false;
                }) ?? false;

                isReadonlyStruct = hasReadonlyFields;
            }

            // Generate Populate method signature
            _sb.AppendIndentedLine($"public static void Populate{className}(ref {ReaderType} reader, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
            _sb.StartNewBlock();

            if (isReadonlyStruct)
            {
                // For readonly structs, Populate method is a no-op
                _sb.AppendIndentedLine("// Readonly struct - fields cannot be modified after construction");
                _sb.AppendIndentedLine("// This method consumes the reader but does not modify the instance");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var _);");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.EndBlock();
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            // Generate inline field reading
            GeneratePopulateMethodBody(type, nsPrefix);

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates the body of Populate method with inline field reading.
        /// </summary>
        private void GeneratePopulateMethodBody(TypeDefinition type, string nsPrefix)
        {
            // For array and IEnumerable fields, declare temp lists before the while loop
            var tempListMembers = type.ProtoMembers?.Where(m => NeedsTempList(m.Type)).ToList() ?? new List<ProtoMemberAttribute>();

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = tempListMembers.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();
            var fieldsUsingTempList = tempListMembers.Where(m => !ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType ?? GetArrayElementType(m.Type)));

            // Declare temp lists for non-class collection fields
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType ?? GetArrayElementType(member.Type));
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if (tempListMembers.Count > 0)
            {
                _sb.AppendNewLine();
            }

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Use 'instance' instead of 'result' for Populate methods
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Use sorted dispatch for optimal branch prediction (PGO heuristic)
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                {
                    GeneratePopulateFieldReadCase(member, nsPrefix);
                }

                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.EndBlock();

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "instance", m => IsArrayType(m.Type));

            // After the while loop, assign temp lists to fields
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "instance", m => IsArrayType(m.Type));

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }
        }

        private bool IsArrayType(string typeName)
        {
            return typeName != null && typeName.EndsWith("[]") && !typeName.Equals("System.Byte[]");
        }

        private bool IsIEnumerableType(string typeName)
        {
            if (typeName == null) return false;
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            return normalized.StartsWith("System.Collections.Generic.IEnumerable<") &&
                   !typeName.Contains("ICollection") &&
                   !typeName.Contains("IList");
        }

        private bool NeedsTempList(string typeName)
        {
            return IsArrayType(typeName) || IsIEnumerableType(typeName);
        }

        /// <summary>
        /// Checks if a type has any fields that need temp lists (arrays or IEnumerable).
        /// Types with such fields cannot use per-field PopulateOwnFields/BaseFieldsOnly helpers.
        /// </summary>
        private bool HasFieldsNeedingTempList(TypeDefinition type)
        {
            if (type.ProtoMembers == null)
                return false;

            return type.ProtoMembers.Any(m => NeedsTempList(m.Type));
        }

        private string GetArrayElementType(string arrayTypeName)
        {
            if (arrayTypeName == null || !arrayTypeName.EndsWith("[]"))
                return arrayTypeName;
            return arrayTypeName.Substring(0, arrayTypeName.Length - 2);
        }

        /// <summary>
        /// Generates field read case for Populate method (uses 'instance' instead of 'result').
        /// </summary>
        private void GeneratePopulateFieldReadCase(ProtoMemberAttribute member, string nsPrefix)
        {
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            bool needsBraces = category == FieldCategory.Map ||
                              category == FieldCategory.Collection ||
                              category == FieldCategory.ComplexType ||
                              category == FieldCategory.Tuple ||
                              category == FieldCategory.ProtoVarint;

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            switch (category)
            {
                case FieldCategory.Map:
                    GeneratePopulateMapFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Collection:
                    GeneratePopulateCollectionFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Enum:
                    _sb.AppendIndentedLine($"instance.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
                    break;
                case FieldCategory.Tuple:
                    GeneratePopulateTupleFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Primitive:
                    GeneratePopulatePrimitiveFieldReadBody(member);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateRead(_sb, member, $"instance.{member.Name}", "reader");
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
                case FieldCategory.ComplexType:
                    GeneratePopulateComplexTypeReadBody(member, nsPrefix);
                    break;
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GeneratePopulatePrimitiveFieldReadBody(ProtoMemberAttribute member)
        {
            var typeName = TypeMapping.NormalizeTypeName(member.Type);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (typeName)
            {
                case "System.Int32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"instance.{member.Name} = reader.ReadFixedInt32();");
                    else
                        _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"instance.{member.Name} = reader.ReadFixedUInt32();");
                    else
                        _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"instance.{member.Name} = reader.ReadFixedInt64();");
                    else
                        _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"instance.{member.Name} = reader.ReadFixedUInt64();");
                    else
                        _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Int16":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt16(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt16":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt16(ref reader, wireType);");
                    break;
                case "System.Byte":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByte(ref reader, wireType);");
                    break;
                case "System.SByte":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadSByte(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"instance.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    if (typeName.StartsWith("System.Nullable<"))
                    {
                        var innerType = typeName.Substring("System.Nullable<".Length, typeName.Length - "System.Nullable<".Length - 1);
                        GeneratePopulatePrimitiveFieldReadBodyForType(member, innerType);
                    }
                    else
                    {
                        _sb.AppendIndentedLine($"// WARNING: Unknown primitive type '{typeName}'");
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    }
                    break;
            }
        }

        private void GeneratePopulatePrimitiveFieldReadBodyForType(ProtoMemberAttribute member, string innerType)
        {
            var normalizedInnerType = TypeMapping.NormalizeTypeName(innerType);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (normalizedInnerType)
            {
                case "System.Int32":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"instance.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unknown nullable inner type '{normalizedInnerType}'");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
            }
        }

        private void GeneratePopulateComplexTypeReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Use PushLimit for zero-allocation nested message reading
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var oldLimit = reader.PushLimit(length);");

            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            // Check if type is part of any inheritance hierarchy (either as base with ProtoInclude or as derived)
            bool isPartOfHierarchy = _registry?.IsPartOfHierarchy(member.Type) ?? false;

            if (isPartOfHierarchy)
            {
                // For types with inheritance (ProtoInclude), we need to read the discriminator
                // to determine the actual type, so we must use Read method.
                // If the field appears multiple times, last value wins (as per protobuf spec for oneof-like semantics)
                _sb.AppendIndentedLine($"instance.{member.Name} = {typeNsPrefix}StreamReaders.Read{typeName}(ref reader);");
            }
            else
            {
                // Per protobuf spec: When the same embedded message field appears multiple times,
                // the contents should be MERGED (not overwritten).
                // Create instance only if null, then populate to merge fields.
                // Strip nullable marker (?) when creating instance - can't instantiate nullable types directly
                var instanceType = member.Type.TrimEnd('?');

                // Check if the type is a struct (value type) - structs cannot be compared to null
                var memberTypeInfo = _registry?.GetByFullName(instanceType);
                bool isStruct = memberTypeInfo?.IsStruct ?? false;

                if (!isStruct)
                {
                    // Only generate null check for reference types (classes)
                    _sb.AppendIndentedLine($"if (instance.{member.Name} == null)");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"instance.{member.Name} = new global::{instanceType}();");
                    _sb.EndBlock();
                    _sb.AppendIndentedLine($"{typeNsPrefix}StreamReaders.Populate{typeName}(ref reader, instance.{member.Name});");
                }
                else
                {
                    _sb.AppendIndentedLine($"var _temp_{member.Name} = new global::{instanceType}();");
                    _sb.AppendIndentedLine($"{typeNsPrefix}StreamReaders.Populate{typeName}(ref reader, ref _temp_{member.Name});");
                    _sb.AppendIndentedLine($"instance.{member.Name} = _temp_{member.Name};");
                }
            }

            _sb.AppendIndentedLine("reader.PopLimit(oldLimit);");
        }

        private void GeneratePopulateTupleFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Use PushLimit for zero-allocation nested message reading
            var itemTypes = TupleHandler.ParseTupleTypes(member.Type);
            var tupleInfo = _virtualTupleRegistry.Register(member.Type, itemTypes);

            _sb.AppendIndentedLine("var tupleLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var tupleOldLimit = reader.PushLimit(tupleLength);");
            _sb.AppendIndentedLine($"instance.{member.Name} = {VirtualTypesPrefix}.StreamReaders.Read{tupleInfo.SafeName}Content(ref reader);");
            _sb.AppendIndentedLine("reader.PopLimit(tupleOldLimit);");
        }

        private void GeneratePopulateMapFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Use PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("var mapLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var mapOldLimit = reader.PushLimit(mapLength);");

            var keyType = TypeMapping.GetShortTypeName(member.MapKeyType);
            var valueType = TypeMapping.GetShortTypeName(member.MapValueType);

            bool isKeyValuePairCollection = TypeHelper.IsKeyValuePairCollection(member.Type);
            var dictCreationType = TypeHelper.GetDictionaryCreationType(member.Type, member.MapKeyType, member.MapValueType);

            _sb.AppendIndentedLine($"if (instance.{member.Name} == null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"instance.{member.Name} = new {dictCreationType}();");
            _sb.EndBlock();

            var keyDefault = GetDefaultValueForType(keyType, member.MapKeyType);
            var valueDefault = GetDefaultValueForType(valueType, member.MapValueType);
            _sb.AppendIndentedLine($"{keyType} key = {keyDefault};");
            _sb.AppendIndentedLine($"{valueType} value = {valueDefault};");

            // Check if the value uses repeated field pattern
            // - List/HashSet: each case 2 adds ONE element
            // - Nested dictionaries: each case 2 reads ONE inner map entry
            // - Arrays: can be packed (single field 2) or non-packed (repeated field 2)
            // IMPORTANT: Must check dictionary first since type name checks use Contains
            bool isNestedDictionary = TypeHelper.IsDictionaryType(member.MapValueType);
            // Exclude ProtoContract types (they should be treated as messages, not repeated collections)
            bool isProtoContractType = _registry?.GetByFullName(member.MapValueType) != null;
            bool isCollectionValue = !isNestedDictionary &&
                                     !isProtoContractType &&
                                     (TypeHelper.IsListType(member.MapValueType) ||
                                      TypeHelper.IsHashSetType(member.MapValueType) ||
                                      TypeHelper.IsCustomHashSetType(member.MapValueType) ||
                                      TypeHelper.IsCustomListType(member.MapValueType));
            bool isArrayValue = member.MapValueType.EndsWith("[]");

            // For arrays, pre-declare temp list to accumulate elements (handles both packed and non-packed)
            string arrayElementType = null;
            string shortArrayElementType = null;
            bool useObjectArrayBuilderForMapValue = false;
            if (isArrayValue)
            {
                arrayElementType = member.MapValueType.Substring(0, member.MapValueType.Length - 2);
                shortArrayElementType = TypeMapping.GetShortTypeName(arrayElementType);

                // Check if element type is a class (should use ObjectArrayBuilder)
                var elementTypeDef = _registry?.GetByFullName(arrayElementType);
                useObjectArrayBuilderForMapValue = elementTypeDef != null && !elementTypeDef.IsStruct && !elementTypeDef.IsEnum;

                if (useObjectArrayBuilderForMapValue)
                {
                    _sb.AppendIndentedLine($"var _builder_value = new global::GProtobuf.Core.ObjectArrayBuilder<{shortArrayElementType}>({ObjectArrayBuilderHelper.InitialCapacity});");
                }
                else
                {
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{shortArrayElementType}> _tempList_value = null;");
                }
            }

            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var entryWireType, out var entryFieldId);");
            _sb.AppendIndentedLine("switch (entryFieldId)");
            _sb.StartNewBlock();

            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GeneratePopulateMapKeyOrValueRead("key", member.MapKeyType, member.MapKeyEnumUnderlyingType, "reader", "entryWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            if (isCollectionValue)
            {
                // For collection values, each occurrence of case 2 adds ONE element
                GeneratePopulateMapRepeatedCollectionValueRead("value", member.MapValueType, "reader", "entryWireType");
            }
            else if (isNestedDictionary)
            {
                // For nested dictionaries, each occurrence of case 2 reads ONE inner map entry
                GeneratePopulateMapNestedDictRepeatedValueRead("value", member.MapValueType, "reader", "entryWireType");
            }
            else if (isArrayValue)
            {
                // For arrays, handle both packed and non-packed formats
                var targetVar = useObjectArrayBuilderForMapValue ? "_builder_value" : "_tempList_value";
                GeneratePopulateMapArrayValueReadWithWireType(targetVar, arrayElementType, shortArrayElementType, "reader", "entryWireType", useObjectArrayBuilderForMapValue);
            }
            else
            {
                GeneratePopulateMapKeyOrValueRead("value", member.MapValueType, member.MapValueEnumUnderlyingType, "reader", "entryWireType", nsPrefix);
            }
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(entryWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
            _sb.EndBlock();

            _sb.AppendIndentedLine("reader.PopLimit(mapOldLimit);");

            // For arrays, convert temp list/builder to array after entry loop
            if (isArrayValue)
            {
                if (useObjectArrayBuilderForMapValue)
                {
                    _sb.AppendIndentedLine("if (_builder_value.Count > 0)");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine("value = _builder_value.ToArray();");
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("else");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"value = global::System.Array.Empty<{shortArrayElementType}>();");
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("_builder_value.Dispose();");
                }
                else
                {
                    _sb.AppendIndentedLine("if (_tempList_value != null)");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine("value = _tempList_value.ToArray();");
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("else");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"value = global::System.Array.Empty<{shortArrayElementType}>();");
                    _sb.EndBlock();
                }
            }

            if (isKeyValuePairCollection)
            {
                _sb.AppendIndentedLine($"instance.{member.Name}.Add(new global::System.Collections.Generic.KeyValuePair<{member.MapKeyType}, {member.MapValueType}>(key, value));");
            }
            else
            {
                _sb.AppendIndentedLine($"instance.{member.Name}[key] = value;");
            }
        }

        /// <summary>
        /// Generates code to read ONE element of a collection value in a map entry.
        /// For List/HashSet map values, each occurrence of field 2 contains one element (repeated field pattern).
        /// </summary>
        private void GeneratePopulateMapRepeatedCollectionValueRead(string varName, string typeName, string readerVar, string wireTypeVar)
        {
            var elementType = TypeHelper.GetCollectionElementType(typeName);
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);
            // Use GetGlobalGenericTypeName to properly handle custom types with global:: prefix
            var shortElementType = TypeMapping.GetGlobalGenericTypeName(elementType);
            bool isHashSet = TypeHelper.IsHashSetType(typeName);
            bool isCustomHashSet = TypeHelper.IsCustomHashSetType(typeName);

            // Determine collection creation type
            string collectionCreationType;
            if (isCustomHashSet)
            {
                collectionCreationType = $"global::{typeName}";
            }
            else if (isHashSet)
            {
                collectionCreationType = $"global::System.Collections.Generic.HashSet<{shortElementType}>";
            }
            else if (TypeHelper.IsCustomListType(typeName))
            {
                collectionCreationType = $"global::{typeName}";
            }
            else
            {
                collectionCreationType = $"global::System.Collections.Generic.List<{shortElementType}>";
            }

            // Initialize collection on first element
            _sb.AppendIndentedLine($"if ({varName} == null) {varName} = new {collectionCreationType}();");

            // Add element(s) based on element type
            // Try primitive type helper first
            var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, readerVar, wireTypeVar);
            if (readExpr != null)
            {
                var addStatement = $"{varName}.Add({readExpr});";
                if (PrimitiveTypeCodeGenerator.IsPackable(normalizedElementType))
                {
                    // Packable primitives need wire type checking for packed vs non-packed format
                    GeneratePackableCollectionElementRead(varName, readerVar, wireTypeVar, addStatement);
                }
                else
                {
                    // Non-packable types (string, Guid, etc.) - just output add line directly
                    _sb.AppendIndentedLine(addStatement);
                }
                return;
            }

            // Check for enum types - enums can be packed
            if (_registry != null && (_registry.IsEnum(elementType) || _registry.IsEnum(normalizedElementType)))
            {
                GeneratePackableCollectionElementRead(varName, readerVar, wireTypeVar,
                    $"{varName}.Add((global::{elementType}){readerVar}.ReadVarInt32());");
                return;
            }

            // Complex type - read with PushLimit (not packable)
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var elementLen = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var elementOldLimit = {readerVar}.PushLimit(elementLen);");
            var className = TypeNameHelper.GetClassName(elementType);
            var typeNs = _registry?.GetNamespaceForType(elementType);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
            bool isDerivedType = _registry?.IsDerivedType(elementType) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";
            _sb.AppendIndentedLine($"{varName}.Add({typeNsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref {readerVar}));");
            _sb.AppendIndentedLine($"{readerVar}.PopLimit(elementOldLimit);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates code to read a packable varint collection element with wire type checking.
        /// If wireType == Len (packed), reads all elements from a length-prefixed blob.
        /// Otherwise, reads a single element.
        /// </summary>
        private void GeneratePackableCollectionElementRead(string varName, string readerVar, string wireTypeVar, string singleElementReadCode)
        {
            _sb.AppendIndentedLine($"if ({wireTypeVar} == global::GProtobuf.Core.WireType.Len)");
            _sb.StartNewBlock();
            // Packed format - read all elements from length-prefixed blob
            _sb.AppendIndentedLine($"var packedLen = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var packedOldLimit = {readerVar}.PushLimit(packedLen);");
            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine(singleElementReadCode);
            _sb.EndBlock();
            _sb.AppendIndentedLine($"{readerVar}.PopLimit(packedOldLimit);");
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            // Non-packed format - single element
            _sb.AppendIndentedLine(singleElementReadCode);
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates code to read ONE entry of a nested dictionary value in a map entry.
        /// For nested dictionary map values, each occurrence of field 2 contains one inner map entry (repeated field pattern).
        /// </summary>
        private void GeneratePopulateMapNestedDictRepeatedValueRead(string varName, string typeName, string readerVar, string wireTypeVar)
        {
            // Extract inner key/value types from the nested dictionary
            var (innerKeyType, innerValueType) = TypeHelper.ParseDictionaryTypes(typeName);
            // Use GetGlobalGenericTypeName to properly handle custom types with global:: prefix
            var shortInnerKeyType = TypeMapping.GetGlobalGenericTypeName(innerKeyType);
            var shortInnerValueType = TypeMapping.GetGlobalGenericTypeName(innerValueType);
            var normalizedInnerKeyType = TypeMapping.NormalizeTypeName(innerKeyType);
            var normalizedInnerValueType = TypeMapping.NormalizeTypeName(innerValueType);

            // Check if inner value is a collection (uses repeated field pattern)
            // Note: Exclude dictionary types first, since they might contain collection type names in their generic args
            bool isInnerValueCollection = !TypeHelper.IsDictionaryType(innerValueType) &&
                (TypeHelper.IsListType(innerValueType) || TypeHelper.IsHashSetType(innerValueType));

            // Determine dictionary creation type
            string dictCreationType;
            if (typeName.Contains("ConcurrentDictionary<"))
            {
                dictCreationType = $"global::System.Collections.Concurrent.ConcurrentDictionary<{shortInnerKeyType}, {shortInnerValueType}>";
            }
            else if (TypeHelper.IsCustomDictionaryType(typeName))
            {
                // Custom dictionary types like ListDictionary - use the full type name
                dictCreationType = $"global::{typeName}";
            }
            else
            {
                dictCreationType = $"global::System.Collections.Generic.Dictionary<{shortInnerKeyType}, {shortInnerValueType}>";
            }

            // Initialize nested dictionary on first entry
            _sb.AppendIndentedLine($"if ({varName} == null) {varName} = new {dictCreationType}();");

            // Each case 2 occurrence reads ONE inner map entry: length + (key field, value field)
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine($"var innerEntryLen = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var innerEntryOldLimit = {readerVar}.PushLimit(innerEntryLen);");
            _sb.AppendNewLine();

            // Initialize inner key/value with defaults
            var innerKeyDefault = GetDefaultValueForType(shortInnerKeyType, innerKeyType);
            var innerValueDefault = GetDefaultValueForType(shortInnerValueType, innerValueType);
            _sb.AppendIndentedLine($"{shortInnerKeyType} innerKey = {innerKeyDefault};");
            _sb.AppendIndentedLine($"{shortInnerValueType} innerValue = {innerValueDefault};");
            _sb.AppendNewLine();

            // Read inner key and value fields
            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{readerVar}.ReadWireTypeAndFieldId(out var innerWireType, out var innerFieldId);");
            _sb.AppendIndentedLine("switch (innerFieldId)");
            _sb.StartNewBlock();

            // Inner key (field 1)
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GenerateNestedDictInnerFieldRead("innerKey", innerKeyType, normalizedInnerKeyType, readerVar, "innerWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Inner value (field 2)
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            if (isInnerValueCollection)
            {
                // For collection inner values, use the repeated field pattern
                GeneratePopulateMapRepeatedCollectionValueRead("innerValue", innerValueType, readerVar, "innerWireType");
            }
            else
            {
                GenerateNestedDictInnerFieldRead("innerValue", innerValueType, normalizedInnerValueType, readerVar, "innerWireType");
            }
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"{readerVar}.SkipField(innerWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            _sb.AppendIndentedLine($"{readerVar}.PopLimit(innerEntryOldLimit);");
            _sb.AppendIndentedLine($"{varName}[innerKey] = innerValue;");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates code to read an inner key or value field within a nested dictionary entry.
        /// </summary>
        private void GenerateNestedDictInnerFieldRead(string varName, string typeName, string normalizedType, string readerVar, string wireTypeVar)
        {
            // Check for enum types first
            if (_registry != null && (_registry.IsEnum(typeName) || _registry.IsEnum(normalizedType)))
            {
                _sb.AppendIndentedLine($"{varName} = (global::{typeName}){readerVar}.ReadVarInt32();");
                return;
            }

            // Try primitive type helper first
            var primitiveAssignment = PrimitiveTypeCodeGenerator.GetAssignmentStatement(normalizedType, varName, readerVar, wireTypeVar);
            if (primitiveAssignment != null)
            {
                _sb.AppendIndentedLine(primitiveAssignment);
                return;
            }

            // Complex type - read with PushLimit
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Generate unique variable names based on varName to avoid conflicts in nested scopes
            var uniquePrefix = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");
            var innerLenVar = $"{uniquePrefix}_innerLen";
            var innerOldLimitVar = $"{uniquePrefix}_innerOldLimit";

            _sb.AppendIndentedLine($"var {innerLenVar} = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {innerOldLimitVar} = {readerVar}.PushLimit({innerLenVar});");

            // Check if this is a dictionary type - needs special handling for nested dictionary values
            if (TypeHelper.IsDictionaryType(typeName))
            {
                // For nested dictionary values in map entries, this PushLimit contains ONE ENTRY only
                // We need to read key (field 1) and value (field 2) inline instead of calling ReadContent
                var (dictKeyType, dictValueType) = TypeHelper.ParseDictionaryTypes(typeName);
                var shortDictKeyType = TypeMapping.GetShortTypeName(dictKeyType);
                var shortDictValueType = TypeMapping.GetShortTypeName(dictValueType);
                var normalizedDictKeyType = TypeMapping.NormalizeTypeName(dictKeyType);
                var normalizedDictValueType = TypeMapping.NormalizeTypeName(dictValueType);

                // Initialize dictionary if null
                string dictCreationType;
                if (typeName.Contains("ConcurrentDictionary<"))
                {
                    dictCreationType = $"global::System.Collections.Concurrent.ConcurrentDictionary<{shortDictKeyType}, {shortDictValueType}>";
                }
                else if (TypeHelper.IsCustomDictionaryType(typeName))
                {
                    // Custom dictionary types like ListDictionary - use the full type name
                    dictCreationType = $"global::{typeName}";
                }
                else
                {
                    dictCreationType = $"global::System.Collections.Generic.Dictionary<{shortDictKeyType}, {shortDictValueType}>";
                }
                _sb.AppendIndentedLine($"{varName} ??= new {dictCreationType}();");

                // Read one entry's key and value fields
                var entryKeyDefault = GetDefaultValueForType(shortDictKeyType, dictKeyType);
                var entryValueDefault = GetDefaultValueForType(shortDictValueType, dictValueType);
                var entryKeyVar = $"{uniquePrefix}EntryKey";
                var entryValueVar = $"{uniquePrefix}EntryValue";
                var entryFieldWireTypeVar = $"{uniquePrefix}FieldWireType";
                var entryFieldIdVar = $"{uniquePrefix}FieldId";
                _sb.AppendIndentedLine($"{shortDictKeyType} {entryKeyVar} = {entryKeyDefault};");
                _sb.AppendIndentedLine($"{shortDictValueType} {entryValueVar} = {entryValueDefault};");

                _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{readerVar}.ReadWireTypeAndFieldId(out var {entryFieldWireTypeVar}, out var {entryFieldIdVar});");
                _sb.AppendIndentedLine($"switch ({entryFieldIdVar})");
                _sb.StartNewBlock();

                // Entry key (field 1)
                _sb.AppendIndentedLine("case 1:");
                _sb.IncreaseIndent();
                GenerateNestedDictInnerFieldRead(entryKeyVar, dictKeyType, normalizedDictKeyType, readerVar, entryFieldWireTypeVar);
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                // Entry value (field 2)
                _sb.AppendIndentedLine("case 2:");
                _sb.IncreaseIndent();
                // Check if value is a collection type for proper handling
                if (!TypeHelper.IsDictionaryType(dictValueType) &&
                    (TypeHelper.IsListType(dictValueType) || TypeHelper.IsHashSetType(dictValueType)))
                {
                    GeneratePopulateMapRepeatedCollectionValueRead(entryValueVar, dictValueType, readerVar, entryFieldWireTypeVar);
                }
                else
                {
                    GenerateNestedDictInnerFieldRead(entryValueVar, dictValueType, normalizedDictValueType, readerVar, entryFieldWireTypeVar);
                }
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                // Default
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"{readerVar}.SkipField({entryFieldWireTypeVar});");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock(); // switch
                _sb.EndBlock(); // while

                _sb.AppendIndentedLine($"{readerVar}.PopLimit({innerOldLimitVar});");
                _sb.AppendIndentedLine($"{varName}[{entryKeyVar}] = {entryValueVar};");
            }
            else
            {
                // Non-dictionary complex types - use ReadContent as before
                var className = TypeNameHelper.GetClassName(typeName);

                // For virtual types (collections, dictionaries), use the element/value type's namespace
                // because virtual type readers are generated in the element's namespace
                string typeNs;
                if (TypeHelper.IsListType(typeName) || TypeHelper.IsHashSetType(typeName))
                {
                    var elementType = TypeHelper.GetCollectionElementType(typeName);
                    typeNs = GetNonSystemNamespace(elementType);
                }
                else
                {
                    typeNs = _registry?.GetNamespaceForType(typeName);
                }
                var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);
                bool isDerivedType = _registry?.IsDerivedType(typeName) ?? false;
                var readMethodSuffix = isDerivedType ? "" : "Content";
                _sb.AppendIndentedLine($"{varName} = {typeNsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref {readerVar});");
                _sb.AppendIndentedLine($"{readerVar}.PopLimit({innerOldLimitVar});");
            }
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Gets the namespace for a type, but returns null if it's a system namespace (e.g., System.Collections.Generic).
        /// Virtual type readers for primitive-only types are generated in the current namespace, not system namespaces.
        /// </summary>
        private string GetNonSystemNamespace(string typeName)
        {
            var ns = _registry?.GetNamespaceForType(typeName);
            // If namespace starts with "System.", it's a system type - use current namespace instead
            if (!string.IsNullOrEmpty(ns) && ns.StartsWith("System."))
            {
                // Try to find a registered (non-system) type within the generic arguments
                if (TypeHelper.IsDictionaryType(typeName))
                {
                    var (keyType, valueType) = TypeHelper.ParseDictionaryTypes(typeName);
                    var keyNs = GetNonSystemNamespace(keyType);
                    if (!string.IsNullOrEmpty(keyNs) && !keyNs.StartsWith("System."))
                        return keyNs;
                    return GetNonSystemNamespace(valueType);
                }
                else if (TypeHelper.IsListType(typeName) || TypeHelper.IsHashSetType(typeName))
                {
                    var elementType = TypeHelper.GetCollectionElementType(typeName);
                    return GetNonSystemNamespace(elementType);
                }
                return null; // Fall back to current namespace
            }
            return ns;
        }

        private void GeneratePopulateMapKeyOrValueRead(string varName, string typeName, string enumUnderlyingType, string readerVar, string wireTypeVar, string nsPrefix = "")
        {
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);

            if (!string.IsNullOrEmpty(enumUnderlyingType) || _registry.IsEnum(typeName) || _registry.IsEnum(normalizedType))
            {
                _sb.AppendIndentedLine($"{varName} = (global::{typeName}){readerVar}.ReadVarInt32();");
                return;
            }

            // Special handling for byte[] - use inline GetSlice pattern
            if (normalizedType == "System.Byte[]")
            {
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"{varName} = {readerVar}.GetSlice({varName}Len).ToArray();");
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                return;
            }

            // Try primitive type helper
            var primitiveAssignment = PrimitiveTypeCodeGenerator.GetAssignmentStatement(normalizedType, varName, readerVar, wireTypeVar);
            if (primitiveAssignment != null)
            {
                _sb.AppendIndentedLine(primitiveAssignment);
                return;
            }

            // Complex type handling:
            // First check if it's a real ProtoContract type registered in the registry
            // This prevents types like ValueLogTypeHashSet (which is a registered ProtoContract but has "HashSet" in name)
            // from being incorrectly treated as collection virtual types
            bool isRegisteredType = _registry?.GetByFullName(normalizedType) != null || _registry?.GetByFullName(typeName) != null;

            // Check for array types first
            if (typeName.EndsWith("[]"))
            {
                GeneratePopulateMapArrayValueRead(varName, typeName, readerVar);
                return;
            }
            // Check for nested Dictionary types (but not if it's a registered ProtoContract type)
            if (!isRegisteredType && TypeHelper.IsDictionaryType(typeName))
            {
                GeneratePopulateMapNestedDictionaryValueRead(varName, typeName, readerVar);
                return;
            }
            // Check for List/Collection types (but not if it's a registered ProtoContract type)
            if (!isRegisteredType && (TypeHelper.IsListType(typeName) || TypeHelper.IsHashSetType(typeName) ||
                TypeHelper.IsCustomHashSetType(typeName) || TypeHelper.IsCustomListType(typeName)))
            {
                GeneratePopulateMapCollectionValueRead(varName, typeName, readerVar);
                return;
            }
            // Check for Tuple types (but not if it's a registered ProtoContract type)
            if (!isRegisteredType && (typeName.StartsWith("System.Tuple<") || typeName.StartsWith("System.ValueTuple<") ||
                typeName.StartsWith("(") || normalizedType.Contains("Tuple<")))
            {
                GeneratePopulateMapTupleValueRead(varName, typeName, readerVar);
                return;
            }

            // Complex type - use PushLimit for zero-allocation nested message reading
            var className = TypeNameHelper.GetClassName(typeName);
            var typeNs = _registry.GetNamespaceForType(typeName);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}OldLimit = {readerVar}.PushLimit({varName}Len);");

            bool isDerivedType = _registry?.IsDerivedType(typeName) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";
            _sb.AppendIndentedLine($"{varName} = {typeNsPrefix}StreamReaders.Read{className}{readMethodSuffix}(ref {readerVar});");
            _sb.AppendIndentedLine($"{readerVar}.PopLimit({varName}OldLimit);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GeneratePopulateMapArrayValueRead(string varName, string typeName, string readerVar)
        {
            // Use PushLimit for zero-allocation nested message reading
            var elementType = typeName.Substring(0, typeName.Length - 2);
            var shortElementType = TypeMapping.GetShortTypeName(elementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}OldLimit = {readerVar}.PushLimit({varName}Len);");
            _sb.AppendIndentedLine($"var {varName}List = new global::System.Collections.Generic.List<{shortElementType}>();");
            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();

            // Generate inline primitive read for StreamReader using helper
            var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, readerVar);
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"{varName}List.Add({readExpr});");
            }
            else
            {
                _sb.AppendIndentedLine($"// Unsupported array element type: {elementType}");
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine($"{readerVar}.PopLimit({varName}OldLimit);");
            _sb.AppendIndentedLine($"{varName} = {varName}List.ToArray();");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates code to read array elements in a map entry, handling both packed and non-packed formats.
        /// </summary>
        private void GeneratePopulateMapArrayValueReadWithWireType(string tempListVar, string elementType, string shortElementType, string readerVar, string wireTypeVar, bool useObjectArrayBuilder = false)
        {
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);

            // For ObjectArrayBuilder, skip lazy init - it's already initialized before the loop
            if (!useObjectArrayBuilder)
            {
                _sb.AppendIndentedLine($"{tempListVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
            }
            _sb.AppendIndentedLine($"if ({wireTypeVar} == global::GProtobuf.Core.WireType.Len)");
            _sb.StartNewBlock();
            // Packed format - read all elements from length-prefixed blob
            _sb.AppendIndentedLine($"var packedLen = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var packedOldLimit = {readerVar}.PushLimit(packedLen);");
            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            GenerateArrayElementRead(tempListVar, normalizedElementType, readerVar);
            _sb.EndBlock();
            _sb.AppendIndentedLine($"{readerVar}.PopLimit(packedOldLimit);");
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            // Non-packed format - single element per case 2
            GenerateArrayElementRead(tempListVar, normalizedElementType, readerVar);
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates code to read and add a single array element to the temp list.
        /// </summary>
        private void GenerateArrayElementRead(string tempListVar, string normalizedElementType, string readerVar)
        {
            var readExpr = PrimitiveTypeCodeGenerator.GetCollectionAddExpression(normalizedElementType, readerVar);
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"{tempListVar}.Add({readExpr});");
            }
            else
            {
                _sb.AppendIndentedLine($"// Unsupported array element type: {normalizedElementType}");
            }
        }

        private void GeneratePopulateMapNestedDictionaryValueRead(string varName, string typeName, string readerVar)
        {
            // For nested dictionary values in map entries, we need to read ONE ENTRY at a time
            // Each field 2 occurrence at the map entry level contains a single entry of the nested dictionary
            // Wire format: [entryLength][field1:key][field2:value]

            var typeInfo = _virtualMapRegistry?.AnalyzeType(typeName);
            if (typeInfo == null || !typeInfo.IsDictionary)
            {
                // Fallback to old behavior if we can't analyze the type
                // For tuple types, use virtual types in shared namespace
                var safeName = VirtualTypeNameGenerator.GetSafeTypeName(typeName);
                var readersClass = TupleHandler.IsTupleType(typeName) ? GetVirtualTypesStreamReadersClass() : "StreamReaders";
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var {varName}OldLimit = {readerVar}.PushLimit({varName}Len);");
                _sb.AppendIndentedLine($"{varName} = {VirtualTypesPrefix}.StreamReaders.Read{safeName}Content(ref {readerVar});");
                _sb.AppendIndentedLine($"{readerVar}.PopLimit({varName}OldLimit);");
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                return;
            }

            var innerKeyType = typeInfo.DictionaryKeyType ?? "System.Object";
            var innerValueType = typeInfo.DictionaryValueType ?? "System.Object";

            var shortKeyType = TypeMapping.GetShortTypeName(innerKeyType);
            var shortValueType = TypeMapping.GetShortTypeName(innerValueType);

            // Initialize dictionary if null
            var fullTypeName = typeInfo.FullTypeName ?? "";
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            if (fullTypeName.Contains("ConcurrentDictionary"))
            {
                _sb.AppendIndentedLine($"{varName} ??= new global::System.Collections.Concurrent.ConcurrentDictionary<{shortKeyType}, {shortValueType}>();");
            }
            else if (TypeHelper.IsCustomDictionaryType(fullTypeName))
            {
                // Custom dictionary types like ListDictionary - use the full type name
                _sb.AppendIndentedLine($"{varName} ??= new global::{fullTypeName}();");
            }
            else
            {
                _sb.AppendIndentedLine($"{varName} ??= new global::System.Collections.Generic.Dictionary<{shortKeyType}, {shortValueType}>();");
            }

            // Read entry length and push limit
            var fieldPrefix = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");
            _sb.AppendIndentedLine($"var {fieldPrefix}Length = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {fieldPrefix}OldLimit = {readerVar}.PushLimit({fieldPrefix}Length);");

            // Declare key/value variables
            var keyVar = $"{fieldPrefix}Key";
            var valueVar = $"{fieldPrefix}Value";
            _sb.AppendIndentedLine($"{shortKeyType} {keyVar} = default;");
            _sb.AppendIndentedLine($"{shortValueType} {valueVar} = default;");

            // While loop to read entry fields
            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{readerVar}.ReadWireTypeAndFieldId(out var {fieldPrefix}InnerWireType, out var {fieldPrefix}InnerFieldId);");
            _sb.AppendIndentedLine($"switch ({fieldPrefix}InnerFieldId)");
            _sb.StartNewBlock();

            // Case 1: Key
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GeneratePopulateMapKeyOrValueRead(keyVar, innerKeyType, null, readerVar, $"{fieldPrefix}InnerWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Case 2: Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GeneratePopulateMapKeyOrValueRead(valueVar, innerValueType, null, readerVar, $"{fieldPrefix}InnerWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default: skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"{readerVar}.SkipField({fieldPrefix}InnerWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            // Pop limit and add to dictionary
            _sb.AppendIndentedLine($"{readerVar}.PopLimit({fieldPrefix}OldLimit);");
            _sb.AppendIndentedLine($"{varName}[{keyVar}] = {valueVar};");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GeneratePopulateMapCollectionValueRead(string varName, string typeName, string readerVar)
        {
            // Use PushLimit for zero-allocation nested message reading
            // Collection types (List, HashSet) use virtual types in shared namespace
            var safeName = VirtualTypeNameGenerator.GetSafeTypeName(typeName);
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}OldLimit = {readerVar}.PushLimit({varName}Len);");
            _sb.AppendIndentedLine($"{varName} = {VirtualTypesPrefix}.StreamReaders.Read{safeName}Content(ref {readerVar});");
            _sb.AppendIndentedLine($"{readerVar}.PopLimit({varName}OldLimit);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GeneratePopulateMapTupleValueRead(string varName, string typeName, string readerVar)
        {
            // Use PushLimit for zero-allocation nested message reading
            // Tuple types use virtual types in shared namespace
            var safeName = VirtualTypeNameGenerator.GetSafeTypeName(typeName);
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}OldLimit = {readerVar}.PushLimit({varName}Len);");
            _sb.AppendIndentedLine($"{varName} = {VirtualTypesPrefix}.StreamReaders.Read{safeName}Content(ref {readerVar});");
            _sb.AppendIndentedLine($"{readerVar}.PopLimit({varName}OldLimit);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GeneratePopulateCollectionFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));
            bool isPrimitiveCollection = _primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection;

            if (isPrimitiveCollection)
            {
                GeneratePopulatePrimitiveCollectionRead(member, isEnumCollection);
            }
            else
            {
                GeneratePopulateComplexCollectionRead(member, nsPrefix);
            }
        }

        private void GeneratePopulatePrimitiveCollectionRead(ProtoMemberAttribute member, bool isEnumCollection)
        {
            var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);

            // Check if this needs a temp list (array or IEnumerable)
            bool needsTempListForType = NeedsTempList(member.Type);

            // For arrays and IEnumerable, use temp list; for other collections, use instance directly
            string targetCollection;
            if (needsTempListForType)
            {
                targetCollection = $"_tempList_{member.Name}";
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                targetCollection = $"instance.{member.Name}";
                bool isInterfaceCollection = normalizedMemberType.Contains("IList<") || normalizedMemberType.Contains("ICollection<");
                bool isCustomCollection = !isInterfaceCollection && (TypeHelper.IsCustomHashSetType(normalizedMemberType) || TypeHelper.IsCustomListType(normalizedMemberType));

                string collectionTypeName;
                if (isCustomCollection)
                {
                    collectionTypeName = $"global::{member.Type}";
                }
                else
                {
                    bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                    collectionTypeName = isHashSet
                        ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                        : $"global::System.Collections.Generic.List<{elementType}>";
                }

                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            bool isPackable = IsPackableElementType(normalizedElementType) || isEnumCollection;

            if (isPackable)
            {
                _sb.AppendIndentedLine("if (wireType == global::GProtobuf.Core.WireType.Len)");
                _sb.StartNewBlock();

                // Use PushLimit for zero-allocation packed array reading
                _sb.AppendIndentedLine("var packedLength = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var packedOldLimit = reader.PushLimit(packedLength);");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();

                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})reader.ReadVarInt32());");
                }
                else
                {
                    GeneratePopulatePackedElementRead(targetCollection, normalizedElementType, member.DataFormat, "reader");
                }

                _sb.EndBlock();
                _sb.AppendIndentedLine("reader.PopLimit(packedOldLimit);");
                _sb.EndBlock();

                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();

                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})reader.ReadVarInt32());");
                }
                else
                {
                    GeneratePopulateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
                }

                _sb.EndBlock();
            }
            else
            {
                GeneratePopulateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
            }
        }

        private void GeneratePopulatePackedElementRead(string targetCollection, string elementType, DataFormat dataFormat, string readerVar)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32() != 0);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported packed element type: {elementType}");
                    break;
            }
        }

        private void GeneratePopulateSingleElementRead(string targetCollection, string elementType, DataFormat dataFormat)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32() != 0);");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType));");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader));");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType));");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType));");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType));");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported single element type: {elementType}");
                    break;
            }
        }

        private void GeneratePopulateComplexCollectionRead(ProtoMemberAttribute member, string nsPrefix)
        {
            var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
            var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);

            // Check if this needs a temp list (array or IEnumerable)
            bool needsTempListForType = NeedsTempList(member.Type);

            // Check if we should use ObjectArrayBuilder for class elements
            bool useObjectArrayBuilder = needsTempListForType && ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry);

            // For arrays and IEnumerable, use temp list; for other collections, use instance directly
            string targetCollection;
            if (useObjectArrayBuilder)
            {
                // ObjectArrayBuilder is always pre-initialized, no lazy init needed
                targetCollection = $"_builder_{member.Name}";
            }
            else if (needsTempListForType)
            {
                targetCollection = $"_tempList_{member.Name}";
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                targetCollection = $"instance.{member.Name}";
                bool isInterfaceCollection = normalizedMemberType.Contains("IList<") || normalizedMemberType.Contains("ICollection<");
                bool isCustomCollection = !isInterfaceCollection && (TypeHelper.IsCustomHashSetType(normalizedMemberType) || TypeHelper.IsCustomListType(normalizedMemberType));

                string collectionTypeName;
                if (isCustomCollection)
                {
                    collectionTypeName = $"global::{member.Type}";
                }
                else
                {
                    bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                    collectionTypeName = isHashSet
                        ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                        : $"global::System.Collections.Generic.List<{elementType}>";
                }

                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            switch (normalizedElementType)
            {
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, global::GProtobuf.Core.WireType.Len));");
                    return;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, global::GProtobuf.Core.WireType.Len));");
                    return;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader));");
                    return;
            }

            // Use PushLimit for zero-allocation nested message reading
            _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var itemOldLimit = reader.PushLimit(itemLength);");

            if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                var tupleSafeName = VirtualTypeNameGenerator.GetSafeTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"{targetCollection}.Add({VirtualTypesPrefix}.StreamReaders.Read{tupleSafeName}Content(ref reader));");
                _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
                return;
            }

            var elementNs = _registry.GetNamespaceForType(member.CollectionElementType);
            var elementNsPrefix = GeneratorHelpers.GetNamespacePrefix(elementNs, _currentNamespace);

            bool isDerivedType = _registry?.IsDerivedType(member.CollectionElementType) ?? false;
            var readMethodSuffix = isDerivedType ? "" : "Content";
            _sb.AppendIndentedLine($"{targetCollection}.Add({elementNsPrefix}StreamReaders.Read{elementClassName}{readMethodSuffix}(ref reader));");
            _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
        }

        #endregion

        #region OwnFields and BaseFieldsOnly Methods

        /// <summary>
        /// Generates Populate{ClassName}OwnFields method for derived types.
        /// Populates ONLY fields defined at this type level (not inherited from base).
        /// Used for ProtoInclude wrapper content reading and inheritance scenarios.
        /// </summary>
        private void GeneratePopulateOwnFieldsMethod(TypeDefinition type, string className)
        {
            var ownMembers = _registry.GetOwnProtoMembers(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Populates {className}'s OWN fields (not inherited from base).");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static bool Populate{className}OwnFields(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref {ReaderType} reader,");
            _sb.AppendIndentedLine($"global::{type.FullName} instance,");
            _sb.AppendIndentedLine($"global::GProtobuf.Core.WireType wireType,");
            _sb.AppendIndentedLine($"int fieldId)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            if (ownMembers.Count == 0)
            {
                _sb.AppendIndentedLine("// No own fields (all inherited from base)");
                _sb.AppendIndentedLine("return false;");
            }
            else
            {
                _sb.AppendIndentedLine($"// Read ONLY own fields (not inherited) - {ownMembers.Count} field(s)");
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in ownMembers)
                {
                    GeneratePopulateFieldReadCase(member, nsPrefix);
                }

                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("return false;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
                _sb.AppendIndentedLine("return true;");
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates Populate{ClassName}BaseFieldsOnly method for base types with ProtoIncludes.
        /// Populates ONLY base class fields without runtime type dispatch.
        /// Used for derived type reading after ProtoInclude wrapper.
        /// </summary>
        private void GeneratePopulateBaseFieldsOnlyMethod(TypeDefinition type, string className)
        {
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Populates ONLY base {className} fields without runtime type dispatch.");
            _sb.AppendIndentedLine($"/// Used for derived type reading after ProtoInclude wrapper.");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static bool Populate{className}BaseFieldsOnly(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref {ReaderType} reader,");
            _sb.AppendIndentedLine($"global::{type.FullName} instance,");
            _sb.AppendIndentedLine($"global::GProtobuf.Core.WireType wireType,");
            _sb.AppendIndentedLine($"int fieldId)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            if (type.ProtoMembers == null || type.ProtoMembers.Count == 0)
            {
                _sb.AppendIndentedLine("// No base fields");
                _sb.AppendIndentedLine("return false;");
            }
            else
            {
                // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                _sb.AppendIndentedLine($"// Read ONLY base fields (no type dispatch) - {type.ProtoMembers.Count} field(s)");
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                {
                    GeneratePopulateFieldReadCase(member, nsPrefix);
                }

                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("return false;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
                _sb.AppendIndentedLine("return true;");
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #endregion

        #region Helpers

        private void GenerateObjectCreation(TypeDefinition type, string variableName = "result")
        {
            var fullTypeName = $"global::{type.FullName}";

            if (type.HasParameterlessConstructor)
            {
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = new {fullTypeName}();");
            }
            else
            {
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = ({fullTypeName})");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"System.Runtime.Serialization.FormatterServices.GetUninitializedObject(");
                _sb.AppendIndentedLine($"    typeof({fullTypeName}));");
                _sb.DecreaseIndent();
            }
        }

        /// <summary>
        /// Returns the appropriate default value for a type.
        /// For collection types (List, HashSet, Dictionary), returns a proper initializer
        /// to ensure empty collections are not null.
        /// </summary>
        /// <param name="shortTypeName">The short type name (used for variable declaration)</param>
        /// <param name="originalTypeName">The original full type name (used for collection detection)</param>
        private static string GetDefaultValueForType(string shortTypeName, string originalTypeName = null)
        {
            var normalized = TypeMapping.NormalizeTypeName(shortTypeName);
            if (normalized == "System.String" || normalized == "string")
            {
                return "\"\"";
            }

            // Use original type name for collection detection if provided
            var typeToCheck = originalTypeName ?? shortTypeName;

            // Check for collection types - these should be initialized to empty, not null
            // IMPORTANT: Check dictionary first since other checks might match dictionary type names
            if (TypeHelper.IsDictionaryType(typeToCheck))
            {
                var (keyType, valueType) = TypeHelper.ParseDictionaryTypes(typeToCheck);
                var shortKeyType = TypeMapping.GetShortTypeName(keyType);
                var shortValueType = TypeMapping.GetShortTypeName(valueType);
                if (typeToCheck.Contains("ConcurrentDictionary<"))
                {
                    return $"new global::System.Collections.Concurrent.ConcurrentDictionary<{shortKeyType}, {shortValueType}>()";
                }
                if (TypeHelper.IsCustomDictionaryType(typeToCheck))
                {
                    // Custom dictionary types like ListDictionary - use the full type name
                    return $"new global::{typeToCheck}()";
                }
                return $"new global::System.Collections.Generic.Dictionary<{shortKeyType}, {shortValueType}>()";
            }

            if (TypeHelper.IsHashSetType(typeToCheck) || TypeHelper.IsCustomHashSetType(typeToCheck))
            {
                var elementType = TypeHelper.GetCollectionElementType(typeToCheck);
                var shortElementType = TypeMapping.GetShortTypeName(elementType);
                if (TypeHelper.IsCustomHashSetType(typeToCheck))
                {
                    return $"new global::{typeToCheck}()";
                }
                return $"new global::System.Collections.Generic.HashSet<{shortElementType}>()";
            }

            if (TypeHelper.IsListType(typeToCheck) || TypeHelper.IsCustomListType(typeToCheck))
            {
                var elementType = TypeHelper.GetCollectionElementType(typeToCheck);
                var shortElementType = TypeMapping.GetShortTypeName(elementType);
                if (TypeHelper.IsCustomListType(typeToCheck))
                {
                    return $"new global::{typeToCheck}()";
                }
                return $"new global::System.Collections.Generic.List<{shortElementType}>()";
            }

            return "default";
        }

        #endregion
    }
}
