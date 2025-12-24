namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Centralized type checking and classification helpers.
    /// </summary>
    internal static class TypeHelper
    {
        #region Type Classification

        /// <summary>
        /// Checks if a type is a primitive type (including Guid).
        /// </summary>
        public static bool IsPrimitiveType(string typeName)
        {
            return TypeMapping.IsSimpleType(typeName) ||
                   TypeMapping.NormalizeTypeName(typeName) == "System.Guid";
        }

        /// <summary>
        /// Checks if a type is a string.
        /// </summary>
        public static bool IsString(string typeName)
        {
            return TypeMapping.NormalizeTypeName(typeName) == "System.String";
        }

        /// <summary>
        /// Checks if a type is a Guid.
        /// </summary>
        public static bool IsGuid(string typeName)
        {
            return TypeMapping.NormalizeTypeName(typeName) == "System.Guid";
        }

        /// <summary>
        /// Checks if a type is a List (but not KeyValuePair collection).
        /// </summary>
        public static bool IsListType(string typeName)
        {
            return typeName.Contains("List<") && !typeName.Contains("KeyValuePair");
        }

        /// <summary>
        /// Checks if a type is a HashSet.
        /// </summary>
        public static bool IsHashSetType(string typeName)
        {
            return typeName.Contains("HashSet<");
        }

        /// <summary>
        /// Checks if a type is a Dictionary.
        /// </summary>
        public static bool IsDictionaryType(string typeName)
        {
            return typeName.Contains("Dictionary<") || typeName.Contains("IDictionary<");
        }

        /// <summary>
        /// Checks if a type is an array.
        /// </summary>
        public static bool IsArrayType(string typeName)
        {
            return typeName.EndsWith("[]");
        }

        /// <summary>
        /// Checks if a type is a collection (List, HashSet, or array).
        /// </summary>
        public static bool IsCollectionType(string typeName)
        {
            return IsArrayType(typeName) || IsListType(typeName) || IsHashSetType(typeName);
        }

        #endregion

        #region Type Extraction

        /// <summary>
        /// Extracts element type from a collection (List, HashSet, or array).
        /// </summary>
        public static string GetCollectionElementType(string collectionType)
        {
            if (collectionType.EndsWith("[]"))
            {
                return collectionType.Substring(0, collectionType.Length - 2);
            }

            var startIndex = collectionType.IndexOf('<') + 1;
            var endIndex = collectionType.LastIndexOf('>');
            if (startIndex > 0 && endIndex > startIndex)
            {
                return collectionType.Substring(startIndex, endIndex - startIndex);
            }
            return collectionType;
        }

        /// <summary>
        /// Gets the outer generic type name (part before first '&lt;').
        /// </summary>
        public static string GetOuterTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return string.Empty;

            var genericIndex = typeName.IndexOf('<');
            if (genericIndex < 0)
                return typeName;

            return typeName.Substring(0, genericIndex);
        }

        #endregion

        #region Size Helpers

        /// <summary>
        /// Returns fixed size in bytes for types with constant wire size, or 0 for variable-size types.
        /// </summary>
        public static int GetFixedElementSize(string elementType)
        {
            var normalized = TypeMapping.NormalizeTypeName(elementType);
            return normalized switch
            {
                "System.Single" => 4,
                "System.Double" => 8,
                "System.Boolean" => 1,
                "System.Byte" => 1,
                "System.SByte" => 1,
                // VarInt types have variable size
                _ => 0
            };
        }

        /// <summary>
        /// Checks if value type needs a nested calculator for size computation.
        /// </summary>
        public static bool NeedsNestedCalculator(string valueType)
        {
            // Primitive types don't need nested calculator
            if (IsPrimitiveType(valueType))
                return false;

            // Check for special types
            var normalized = TypeMapping.NormalizeTypeName(valueType);
            if (normalized == "System.String" || normalized == "System.Guid")
                return false;

            // Arrays with fixed-size elements don't need nested calculator
            if (valueType.EndsWith("[]"))
            {
                var elementType = valueType.Substring(0, valueType.Length - 2);
                return GetFixedElementSize(elementType) == 0;
            }

            // List/HashSet with fixed-size elements don't need nested calculator
            if (IsListType(valueType) || IsHashSetType(valueType))
            {
                var elementType = GetCollectionElementType(valueType);
                return GetFixedElementSize(elementType) == 0;
            }

            // Nested message types need nested calculator
            return true;
        }

        #endregion

        #region Dictionary Helpers

        /// <summary>
        /// Gets the concrete type to use when creating a dictionary instance.
        /// </summary>
        public static string GetDictionaryCreationType(string mapType, string keyType, string valueType)
        {
            // For List<KeyValuePair<K,V>> we need to use List for intermediate storage
            if (mapType.Contains("List<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>>";
            }

            // For ICollection<KeyValuePair<K,V>> use List
            if (mapType.Contains("ICollection<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>>";
            }

            // For interface types (IDictionary<K,V>), use Dictionary<K,V>
            if (mapType.Contains("IDictionary<"))
            {
                return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            // For concrete Dictionary<K,V> or custom types, use the full type
            if (mapType.Contains("Dictionary<") && !IsCustomDictionaryType(mapType))
            {
                return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            // For custom derived dictionary types, use the full qualified type name
            return $"global::{mapType}";
        }

        /// <summary>
        /// Checks if the map type is a custom dictionary type (not standard Dictionary).
        /// </summary>
        public static bool IsCustomDictionaryType(string mapType)
        {
            return !mapType.StartsWith("System.Collections.Generic.Dictionary<") &&
                   !mapType.StartsWith("Dictionary<") &&
                   mapType.Contains("Dictionary");
        }

        /// <summary>
        /// Checks if the type is a KeyValuePair collection (List or ICollection of KeyValuePair).
        /// </summary>
        public static bool IsKeyValuePairCollection(string mapType)
        {
            return (mapType.Contains("List<") || mapType.Contains("ICollection<")) &&
                   mapType.Contains("KeyValuePair<");
        }

        #endregion
    }
}
