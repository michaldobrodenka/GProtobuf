using GProtobuf.Generator.V2.Helpers;
using System.Collections.Generic;

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
        }

        /// <summary>
        /// Registers any nested dictionary types found in the value type.
        /// </summary>
        private void RegisterNestedTypes(VirtualMapEntryInfo info)
        {
            // If value is a dictionary, register it
            if (info.ValueTypeInfo.IsDictionary)
            {
                RegisterMapEntry(
                    info.ValueTypeInfo.DictionaryKeyType,
                    info.ValueTypeInfo.DictionaryValueType);
            }

            // If value is a collection of dictionaries, register the dictionary type
            if (info.ValueTypeInfo.IsCollection && info.ValueTypeInfo.CollectionElementTypeInfo?.IsDictionary == true)
            {
                var elemInfo = info.ValueTypeInfo.CollectionElementTypeInfo;
                RegisterMapEntry(elemInfo.DictionaryKeyType, elemInfo.DictionaryValueType);
            }
        }

        /// <summary>
        /// Analyzes a type and returns detailed information about it.
        /// </summary>
        private TypeAnalysisInfo AnalyzeType(string typeName)
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
            if (TypeMapping.IsSimpleType(typeName))
            {
                info.IsPrimitive = true;
                info.ShortTypeName = TypeMapping.GetShortTypeName(typeName);
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
                info.CollectionElementType = typeName.Substring(0, typeName.Length - 2);
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
                info.CollectionElementType = ParseSingleGenericArg(typeName);
                if (info.CollectionElementType != null)
                {
                    info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
                }
                return info;
            }

            // Check for HashSet
            if (outerTypeName.EndsWith("HashSet") || outerTypeName.EndsWith("ISet"))
            {
                info.IsCollection = true;
                info.IsHashSet = true;
                info.CollectionElementType = ParseSingleGenericArg(typeName);
                if (info.CollectionElementType != null)
                {
                    info.CollectionElementTypeInfo = AnalyzeType(info.CollectionElementType);
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
                info.MapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(keyType, valueType);
                return info;
            }

            // Custom class/message type
            info.IsCustomType = true;
            info.ShortTypeName = TypeNameHelper.GetClassName(typeName);
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
        public bool IsArray { get; set; }
        public bool IsCollection { get; set; }
        public bool IsList { get; set; }
        public bool IsHashSet { get; set; }
        public bool IsDictionary { get; set; }
        public bool IsCustomType { get; set; }

        // For collections
        public string CollectionElementType { get; set; }
        public TypeAnalysisInfo CollectionElementTypeInfo { get; set; }

        // For dictionaries
        public string DictionaryKeyType { get; set; }
        public string DictionaryValueType { get; set; }
        public string MapEntryTypeName { get; set; }
    }
}
