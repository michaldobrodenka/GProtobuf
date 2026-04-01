using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
{
    /// <summary>
    /// Tracks virtual map entry types that have been registered for generation.
    /// Prevents duplicate generation and provides type information for code generation.
    /// </summary>
    internal class VirtualMapTypeRegistry
    {
        private readonly Dictionary<string, VirtualMapEntryInfo> _registeredTypes = new();
        private readonly List<VirtualMapEntryInfo> _orderedTypes = new();
        private readonly Dictionary<string, VirtualCollectionInfo> _collectionTypes = new();
        private readonly List<VirtualCollectionInfo> _orderedCollectionTypes = new();
        private readonly VirtualTupleTypeRegistry _tupleRegistry;
        private readonly TypeRegistry _typeRegistry;
        private readonly Microsoft.CodeAnalysis.Compilation _compilation;

        public VirtualMapTypeRegistry(VirtualTupleTypeRegistry tupleRegistry = null, TypeRegistry typeRegistry = null, Microsoft.CodeAnalysis.Compilation compilation = null)
        {
            _tupleRegistry = tupleRegistry;
            _typeRegistry = typeRegistry;
            _compilation = compilation;
        }

        /// <summary>
        /// Registers a map entry type for generation if not already registered.
        /// Returns the info for the registered type.
        /// </summary>
        public VirtualMapEntryInfo RegisterMapEntry(string keyType, string valueType,
            bool keyIsEnum = false, bool valueIsEnum = false,
            string keyEnumUnderlyingType = null, string valueEnumUnderlyingType = null)
        {
            var typeName = VirtualTypeNameGenerator.GetMapEntryTypeName(keyType, valueType);

            // Checks if a type has been registered.
            if (_registeredTypes.TryGetValue(typeName, out var existing))
            {
                return existing;
            }

            // VALIDATE NESTING DEPTH: Ensure dictionary nesting doesn't exceed safety limits
            // This check applies to the VALUE type (key can't be a dictionary in protobuf maps)
            // Example: Dictionary<string, Dictionary<int, Dictionary<long, double>>>
            //          Level 0: outer dict, Level 1: first inner dict, Level 2: second inner dict
            int nestingDepth = NestingLimits.AnalyzeAndValidateDictionaryNestingDepth(valueType);

            // Log depth for diagnostic purposes (optional, can be removed in production)
            // System.Diagnostics.Debug.WriteLine($"Registering map entry: {typeName}, nesting depth: {nestingDepth}");

            var info = new VirtualMapEntryInfo
            {
                TypeName = typeName,
                KeyType = keyType,
                ValueType = valueType,
                KeyIsEnum = keyIsEnum,
                ValueIsEnum = valueIsEnum,
                KeyEnumUnderlyingType = keyEnumUnderlyingType,
                ValueEnumUnderlyingType = valueEnumUnderlyingType,
                KeyTypeInfo = AnalyzeType(keyType),
                ValueTypeInfo = AnalyzeType(valueType)
            };

            _registeredTypes[typeName] = info;
            _orderedTypes.Add(info);

            // Recursively register nested map types
            RegisterNestedTypes(info);

            return info;
        }

        /// <summary>
        /// Gets all registered virtual types in order of registration.
        /// </summary>
        public IReadOnlyList<VirtualMapEntryInfo> GetAllTypes() => _orderedTypes;

        /// <summary>
        /// Gets info for a registered type.
        /// </summary>
        public VirtualMapEntryInfo GetTypeInfo(string typeName)
        {
            return _registeredTypes.TryGetValue(typeName, out var info) ? info : null;
        }

        /// <summary>
        /// Clears all registered types.
        /// </summary>
        public void Clear()
        {
            _registeredTypes.Clear();
            _orderedTypes.Clear();
            _collectionTypes.Clear();
            _orderedCollectionTypes.Clear();
        }

        /// <summary>
        /// Gets all registered collection types in order of registration.
        /// </summary>
        public IReadOnlyList<VirtualCollectionInfo> GetAllCollectionTypes() => _orderedCollectionTypes;

        /// <summary>
        /// Registers a collection type (List, HashSet, Dictionary) for virtual reader generation.
        /// </summary>
        public VirtualCollectionInfo RegisterCollectionType(string fullTypeName, TypeAnalysisInfo typeInfo)
        {
            var safeName = VirtualTypeNameGenerator.GetSafeTypeName(fullTypeName);

            if (_collectionTypes.TryGetValue(safeName, out var existing))
            {
                return existing;
            }

            var info = new VirtualCollectionInfo
            {
                FullTypeName = fullTypeName,
                SafeName = safeName
            };

            if (typeInfo.IsList)
            {
                info.Kind = VirtualCollectionKind.List;
                info.ElementType = typeInfo.CollectionElementType;
                info.ElementTypeInfo = typeInfo.CollectionElementTypeInfo ?? AnalyzeType(typeInfo.CollectionElementType);
            }
            else if (typeInfo.IsHashSet)
            {
                info.Kind = VirtualCollectionKind.HashSet;
                info.ElementType = typeInfo.CollectionElementType;
                info.ElementTypeInfo = typeInfo.CollectionElementTypeInfo ?? AnalyzeType(typeInfo.CollectionElementType);
            }
            else if (typeInfo.IsDictionary)
            {
                info.Kind = VirtualCollectionKind.Dictionary;
                info.DictionaryKeyType = typeInfo.DictionaryKeyType;
                info.DictionaryValueType = typeInfo.DictionaryValueType;
                info.DictionaryKeyTypeInfo = AnalyzeType(typeInfo.DictionaryKeyType);
                info.DictionaryValueTypeInfo = AnalyzeType(typeInfo.DictionaryValueType);
            }
            else if (typeInfo.IsArray)
            {
                info.Kind = VirtualCollectionKind.Array;
                info.ElementType = typeInfo.CollectionElementType;
                info.ElementTypeInfo = typeInfo.CollectionElementTypeInfo ?? AnalyzeType(typeInfo.CollectionElementType);
            }
            else if (typeInfo.IsCollection)
            {
                // Generic collection - treat as List
                info.Kind = VirtualCollectionKind.List;
                info.ElementType = typeInfo.CollectionElementType;
                info.ElementTypeInfo = typeInfo.CollectionElementTypeInfo ?? AnalyzeType(typeInfo.CollectionElementType);
            }
            else
            {
                // Not a collection type
                return null;
            }

            _collectionTypes[safeName] = info;
            _orderedCollectionTypes.Add(info);
            return info;
        }

        /// <summary>
        /// Registers any nested dictionary and collection types found in the value type.
        /// </summary>
        private void RegisterNestedTypes(VirtualMapEntryInfo info)
        {
            // Register nested Tuples in key and value types
            if (_tupleRegistry != null)
            {
                _tupleRegistry.RegisterNestedTuples(info.KeyType);
                _tupleRegistry.RegisterNestedTuples(info.ValueType);
            }

            // If value is a dictionary, register it as both a map entry and a collection type
            if (info.ValueTypeInfo.IsDictionary)
            {
                // Analyze nested key/value types to detect enums
                var nestedKeyTypeInfo = AnalyzeType(info.ValueTypeInfo.DictionaryKeyType);
                var nestedValueTypeInfo = AnalyzeType(info.ValueTypeInfo.DictionaryValueType);

                RegisterMapEntry(
                    info.ValueTypeInfo.DictionaryKeyType,
                    info.ValueTypeInfo.DictionaryValueType,
                    nestedKeyTypeInfo?.IsEnum ?? false,
                    nestedValueTypeInfo?.IsEnum ?? false);
                // Also register as a collection type for virtual reader generation
                RegisterCollectionType(info.ValueType, info.ValueTypeInfo);
            }

            // If value is a collection (List, HashSet), register it as a collection type
            if (info.ValueTypeInfo.IsCollection || info.ValueTypeInfo.IsList || info.ValueTypeInfo.IsHashSet)
            {
                RegisterCollectionType(info.ValueType, info.ValueTypeInfo);

                // If collection element is a dictionary, register it
                if (info.ValueTypeInfo.CollectionElementTypeInfo?.IsDictionary == true)
                {
                    var elemInfo = info.ValueTypeInfo.CollectionElementTypeInfo;
                    // Analyze nested key/value types to detect enums
                    var elemKeyTypeInfo = AnalyzeType(elemInfo.DictionaryKeyType);
                    var elemValueTypeInfo = AnalyzeType(elemInfo.DictionaryValueType);

                    RegisterMapEntry(
                        elemInfo.DictionaryKeyType,
                        elemInfo.DictionaryValueType,
                        elemKeyTypeInfo?.IsEnum ?? false,
                        elemValueTypeInfo?.IsEnum ?? false);
                }
            }
        }

        /// <summary>
        /// Analyzes a type and returns detailed information about it.
        /// </summary>
        internal TypeAnalysisInfo AnalyzeType(string typeName)
        {
            // Handle null/empty type names
            if (string.IsNullOrEmpty(typeName))
            {
                return new TypeAnalysisInfo
                {
                    FullTypeName = typeName ?? string.Empty,
                    SafeName = "Unknown",
                    ShortTypeName = "object"
                };
            }

            var info = new TypeAnalysisInfo
            {
                FullTypeName = typeName,
                SafeName = VirtualTypeNameGenerator.GetSafeTypeName(typeName)
            };

            // Normalize type name first for all comparisons
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            // Check for string (must be before IsSimpleType because string is "simple" but needs IsString flag)
            if (normalized == "System.String")
            {
                info.IsPrimitive = true;
                info.IsString = true;
                info.ShortTypeName = "string";
                return info;
            }

            // Check for primitive
            if (TypeMapping.IsSimpleType(normalized))
            {
                info.IsPrimitive = true;
                info.ShortTypeName = TypeMapping.GetShortTypeName(normalized);
                return info;
            }

            // Check for Guid
            if (normalized == "System.Guid")
            {
                info.IsPrimitive = true;
                info.IsGuid = true;
                info.ShortTypeName = "global::System.Guid";
                return info;
            }

            // Check for array
            if (typeName.EndsWith("[]"))
            {
                info.IsArray = true;
                var elementType = typeName.Substring(0, typeName.Length - 2);
                // Normalize element type recursively
                info.CollectionElementType = TypeMapping.NormalizeGenericTypeName(elementType);
                info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                return info;
            }

            // Get outer generic type name
            var outerTypeName = GetOuterTypeName(typeName);

            // Check for List/Collection
            if (outerTypeName.EndsWith("List") || outerTypeName.EndsWith("IList") ||
                outerTypeName.EndsWith("ICollection") || outerTypeName.EndsWith("IEnumerable"))
            {
                info.IsCollection = true;
                info.IsList = true;
                var elementType = ParseSingleGenericArg(typeName);
                if (elementType != null)
                {
                    // Normalize element type recursively (e.g., "KeyValuePair<int, string>" -> "System.Collections.Generic.KeyValuePair<System.Int32, System.String>")
                    info.CollectionElementType = TypeMapping.NormalizeGenericTypeName(elementType);
                    info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                }
                else
                {
                    // Custom List type without generic parameter in name
                    // Try to extract element type from ICollection<T> interface using TypeSymbol
                    var extractedElementType = ExtractCollectionElementTypeFromInterfaces(typeName);
                    if (extractedElementType != null)
                    {
                        info.CollectionElementType = TypeMapping.NormalizeGenericTypeName(extractedElementType);
                        info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                    }
                }
                return info;
            }

            // Check for HashSet
            if (outerTypeName.EndsWith("HashSet") || outerTypeName.EndsWith("ISet"))
            {
                info.IsCollection = true;
                info.IsHashSet = true;
                var elementType = ParseSingleGenericArg(typeName);
                if (elementType != null)
                {
                    // Normalize element type recursively
                    info.CollectionElementType = TypeMapping.NormalizeGenericTypeName(elementType);
                    info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                }
                else
                {
                    // Custom HashSet type (e.g., ValueLogTypeHashSet) without generic parameter in name
                    // Try to extract element type from ICollection<T> interface using TypeSymbol
                    var extractedElementType = ExtractCollectionElementTypeFromInterfaces(typeName);
                    if (extractedElementType != null)
                    {
                        info.CollectionElementType = TypeMapping.NormalizeGenericTypeName(extractedElementType);
                        info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                    }
                }
                return info;
            }

            // Check for Dictionary
            if (outerTypeName.EndsWith("Dictionary") || outerTypeName.EndsWith("IDictionary"))
            {
                info.IsDictionary = true;
                var (keyType, valueType) = ParseTwoGenericArgs(typeName);
                info.DictionaryKeyType = keyType;
                info.DictionaryValueType = valueType;
                info.MapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(info.DictionaryKeyType, info.DictionaryValueType);
                return info;
            }

            // Check for enum type (before IsCustomType check)
            if (_typeRegistry != null && _typeRegistry.IsEnum(normalized))
            {
                info.IsEnum = true;
                info.ShortTypeName = TypeNameHelper.GetClassName(typeName);
                return info;
            }

            // Check for ProtoVarint type (before IsCustomType check)
            // ProtoVarint types are treated as primitives (varints), not as custom message types
            // First try to find in TypeRegistry (types with [ProtoContract])
            if (_typeRegistry != null && _typeRegistry.IsProtoVarint(normalized))
            {
                var varintType = _typeRegistry.GetProtoVarintType(normalized);
                var valueMember = _typeRegistry.GetProtoVarintValueMember(normalized);
                info.IsProtoVarint = true;
                info.ProtoVarintType = varintType ?? ProtoVarintType.UInt32;
                info.ProtoVarintValueMember = valueMember;
                info.ShortTypeName = TypeNameHelper.GetClassName(typeName);
                info.IsStruct = true; // ProtoVarint types are typically readonly structs
                return info;
            }

            // If not in TypeRegistry, try to check via Compilation (types without [ProtoContract])
            if (_compilation != null)
            {
                var protoVarintInfo = TryGetProtoVarintInfoFromCompilation(normalized);
                if (protoVarintInfo != null)
                {
                    info.IsProtoVarint = true;
                    info.ProtoVarintType = protoVarintInfo.Value.VarintType;
                    info.ProtoVarintValueMember = protoVarintInfo.Value.ValueMember;
                    info.ShortTypeName = TypeNameHelper.GetClassName(typeName);
                    info.IsStruct = true;
                    return info;
                }
            }

            // Custom class/message type
            info.IsCustomType = true;
            info.ShortTypeName = TypeNameHelper.GetClassName(typeName);

            // Check if it's a struct (value type) - structs can't be null
            if (_typeRegistry != null)
            {
                var typeDef = _typeRegistry.GetByFullName(normalized);
                if (typeDef != null && typeDef.IsStruct)
                {
                    info.IsStruct = true;
                }
            }

            return info;
        }

        #region Parsing Helpers

        private static (string first, string second) ParseTwoGenericArgs(string typeName)
        {
            int startIndex = typeName.IndexOf('<');
            if (startIndex < 0) return (null, null);

            int endIndex = typeName.LastIndexOf('>');
            if (endIndex <= startIndex) return (null, null);

            string innerContent = typeName.Substring(startIndex + 1, endIndex - startIndex - 1);

            int depth = 0;
            int commaIndex = -1;

            for (int i = 0; i < innerContent.Length; i++)
            {
                char c = innerContent[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    commaIndex = i;
                    break;
                }
            }

            if (commaIndex < 0) return (null, null);

            string first = innerContent.Substring(0, commaIndex).Trim();
            string second = innerContent.Substring(commaIndex + 1).Trim();

            return (first, second);
        }

        private static string ParseSingleGenericArg(string typeName)
        {
            int startIndex = typeName.IndexOf('<');
            if (startIndex < 0) return null;

            int endIndex = typeName.LastIndexOf('>');
            if (endIndex <= startIndex) return null;

            return typeName.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
        }

        /// <summary>
        /// Extracts the outer generic type name (the part before the first '&lt;').
        /// For example: "List&lt;Dictionary&lt;int, string&gt;&gt;" returns "List".
        /// For "System.Collections.Generic.List&lt;...&gt;" returns "System.Collections.Generic.List".
        /// For non-generic types, returns the type name as-is.
        /// </summary>
        private static string GetOuterTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return string.Empty;

            int genericIndex = typeName.IndexOf('<');
            if (genericIndex < 0)
                return typeName;

            return typeName.Substring(0, genericIndex);
        }

        /// <summary>
        /// Extracts the element type from ICollection&lt;T&gt; or IEnumerable&lt;T&gt; interface implementation.
        /// Used for custom collection types like ValueLogTypeHashSet that don't have generic parameters in their name.
        /// </summary>
        /// <param name="typeName">Full type name to analyze</param>
        /// <returns>Element type T from ICollection&lt;T&gt;, or null if not found</returns>
        private string ExtractCollectionElementTypeFromInterfaces(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            Microsoft.CodeAnalysis.INamedTypeSymbol typeSymbol = null;

            // Try to get TypeSymbol from TypeRegistry first (for types with ProtoContract)
            if (_typeRegistry != null)
            {
                var typeDefinition = _typeRegistry.GetByFullName(typeName);
                if (typeDefinition?.TypeSymbol != null)
                {
                    typeSymbol = typeDefinition.TypeSymbol;
                }
            }

            // If not found in TypeRegistry, try to get it from Compilation (for all types)
            if (typeSymbol == null && _compilation != null)
            {
                typeSymbol = _compilation.GetTypeByMetadataName(typeName);
            }

            // No TypeSymbol available - cannot extract element type
            if (typeSymbol == null)
                return null;

            // Search for ICollection<T> or IEnumerable<T> in implemented interfaces
            foreach (var interfaceType in typeSymbol.AllInterfaces)
            {
                var interfaceFullName = interfaceType.ToDisplayString();

                // Check for ICollection<T>
                if (interfaceFullName.StartsWith("System.Collections.Generic.ICollection<"))
                {
                    var elementType = ParseSingleGenericArg(interfaceFullName);
                    if (elementType != null)
                        return elementType;
                }

                // Fallback to IEnumerable<T> if ICollection<T> not found
                if (interfaceFullName.StartsWith("System.Collections.Generic.IEnumerable<"))
                {
                    var elementType = ParseSingleGenericArg(interfaceFullName);
                    if (elementType != null)
                        return elementType;
                }
            }

            return null;
        }

        /// <summary>
        /// Result of ProtoVarint info extraction from compilation.
        /// </summary>
        private struct ProtoVarintCompilationInfo
        {
            public ProtoVarintType VarintType;
            public string ValueMember;
        }

        /// <summary>
        /// Tries to get ProtoVarint info by checking the type's attributes via Compilation.
        /// This is used for types that don't have [ProtoContract] but have [ProtoVarint].
        /// </summary>
        private ProtoVarintCompilationInfo? TryGetProtoVarintInfoFromCompilation(string typeName)
        {
            if (_compilation == null || string.IsNullOrEmpty(typeName))
                return null;

            var typeSymbol = _compilation.GetTypeByMetadataName(typeName);
            if (typeSymbol == null)
                return null;

            // Check for [ProtoVarint] attribute
            var protoVarintAttr = typeSymbol.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == ProtoVarintConstants.AttributeName);

            if (protoVarintAttr == null)
                return null;

            // Get the varint type from attribute (default is UInt32 = 0)
            var varintType = ProtoVarintType.UInt32;
            if (protoVarintAttr.ConstructorArguments.Length > 0 &&
                protoVarintAttr.ConstructorArguments[0].Value is int typeValue)
            {
                varintType = (ProtoVarintType)typeValue;
            }

            // Find [ProtoVarintValue] member
            string valueMemberName = null;
            foreach (var member in typeSymbol.GetMembers())
            {
                var hasValueAttr = member.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == ProtoVarintConstants.ValueAttributeName);

                if (hasValueAttr)
                {
                    if (member is Microsoft.CodeAnalysis.IPropertySymbol prop)
                    {
                        valueMemberName = prop.Name;
                        break;
                    }
                    else if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        valueMemberName = field.Name;
                        break;
                    }
                    else if (member is Microsoft.CodeAnalysis.IMethodSymbol method && method.Parameters.Length == 0)
                    {
                        // For methods, we need to add parentheses when calling
                        valueMemberName = method.Name + "()";
                        break;
                    }
                }
            }

            if (valueMemberName == null)
                return null;

            return new ProtoVarintCompilationInfo
            {
                VarintType = varintType,
                ValueMember = valueMemberName
            };
        }

        #endregion
    }

    /// <summary>
    /// Information about a virtual map entry type.
    /// </summary>
    internal class VirtualMapEntryInfo
    {
        public string TypeName { get; set; }
        public string KeyType { get; set; }
        public string ValueType { get; set; }
        public bool KeyIsEnum { get; set; }
        public bool ValueIsEnum { get; set; }
        public string KeyEnumUnderlyingType { get; set; }
        public string ValueEnumUnderlyingType { get; set; }
        public TypeAnalysisInfo KeyTypeInfo { get; set; }
        public TypeAnalysisInfo ValueTypeInfo { get; set; }
        public bool IsGenerated { get; set; }
        public string GeneratedInNamespace { get; set; }
    }

    /// <summary>
    /// Detailed analysis of a type for code generation.
    /// </summary>
    internal class TypeAnalysisInfo
    {
        public string FullTypeName { get; set; }
        public string SafeName { get; set; }
        public string ShortTypeName { get; set; }

        // Type categories
        public bool IsPrimitive { get; set; }
        public bool IsString { get; set; }
        public bool IsGuid { get; set; }
        public bool IsEnum { get; set; }
        public bool IsArray { get; set; }
        public bool IsCollection { get; set; }
        public bool IsList { get; set; }
        public bool IsHashSet { get; set; }
        public bool IsDictionary { get; set; }
        public bool IsCustomType { get; set; }
        public bool IsStruct { get; set; }

        // For collections
        public string CollectionElementType { get; set; }
        public TypeAnalysisInfo CollectionElementTypeInfo { get; set; }

        // For dictionaries
        public string DictionaryKeyType { get; set; }
        public string DictionaryValueType { get; set; }
        public string MapEntryTypeName { get; set; }

        // For ProtoVarint types (structs marked with [ProtoVarint])
        public bool IsProtoVarint { get; set; }
        public ProtoVarintType ProtoVarintType { get; set; }
        public string ProtoVarintValueMember { get; set; }
    }

    /// <summary>
    /// Information about a virtual collection type (List, HashSet, Dictionary as nested types).
    /// </summary>
    internal class VirtualCollectionInfo
    {
        public string FullTypeName { get; set; }
        public string SafeName { get; set; }
        public VirtualCollectionKind Kind { get; set; }
        public string ElementType { get; set; }
        public TypeAnalysisInfo ElementTypeInfo { get; set; }
        // For nested dictionaries
        public string DictionaryKeyType { get; set; }
        public string DictionaryValueType { get; set; }
        public TypeAnalysisInfo DictionaryKeyTypeInfo { get; set; }
        public TypeAnalysisInfo DictionaryValueTypeInfo { get; set; }
    }

    internal enum VirtualCollectionKind
    {
        List,
        HashSet,
        Dictionary,
        Array
    }
}
