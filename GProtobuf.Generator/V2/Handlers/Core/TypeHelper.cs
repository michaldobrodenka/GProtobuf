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

        #region Nullable Helpers

        /// <summary>
        /// Checks if a type is Nullable (Nullable<T> or T?).
        /// </summary>
        public static bool IsNullableType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return false;

            return typeName.Contains("Nullable<") || typeName.EndsWith("?");
        }

        /// <summary>
        /// Gets the underlying type from a Nullable type.
        /// Example: "int?" → "int", "Nullable<int>" → "int"
        /// </summary>
        public static string GetNullableUnderlyingType(string nullableTypeName)
        {
            if (!IsNullableType(nullableTypeName))
                return nullableTypeName;

            // Handle T? syntax
            if (nullableTypeName.EndsWith("?"))
            {
                return nullableTypeName.Substring(0, nullableTypeName.Length - 1);
            }

            // Handle Nullable<T> syntax
            var startIndex = nullableTypeName.IndexOf('<') + 1;
            var endIndex = nullableTypeName.LastIndexOf('>');
            if (startIndex > 0 && endIndex > startIndex)
            {
                return nullableTypeName.Substring(startIndex, endIndex - startIndex);
            }

            return nullableTypeName;
        }

        #endregion

        #region Dictionary Helpers

        /// <summary>
        /// Adds global:: prefix to a type if it's not a primitive or system type.
        /// </summary>
        private static string EnsureGlobalPrefix(string typeName)
        {
            // Already has global:: prefix
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

            // Primitive types and system types don't need global:: prefix
            if (IsPrimitiveType(typeName) || typeName.StartsWith("System."))
                return typeName;

            // Add global:: prefix for custom types
            return $"global::{typeName}";
        }

        /// <summary>
        /// Gets the concrete type to use when creating a dictionary instance.
        /// </summary>
        public static string GetDictionaryCreationType(string mapType, string keyType, string valueType)
        {
            // Ensure key and value types have global:: prefix if needed
            var globalKeyType = EnsureGlobalPrefix(keyType);
            var globalValueType = EnsureGlobalPrefix(valueType);

            // For List<KeyValuePair<K,V>> we need to use List for intermediate storage
            if (mapType.Contains("List<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{globalKeyType}, {globalValueType}>>";
            }

            // For ICollection<KeyValuePair<K,V>> use List
            if (mapType.Contains("ICollection<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{globalKeyType}, {globalValueType}>>";
            }

            // For interface types (IDictionary<K,V>), use Dictionary<K,V>
            if (mapType.Contains("IDictionary<"))
            {
                return $"global::System.Collections.Generic.Dictionary<{globalKeyType}, {globalValueType}>";
            }

            // For concrete Dictionary<K,V> or custom types, use the full type
            if (mapType.Contains("Dictionary<") && !IsCustomDictionaryType(mapType))
            {
                return $"global::System.Collections.Generic.Dictionary<{globalKeyType}, {globalValueType}>";
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
        /// Checks if the type is a custom List type (not standard List).
        /// Examples: CustomList&lt;T&gt;, MyList&lt;T&gt;, but not List&lt;T&gt; or System.Collections.Generic.List&lt;T&gt;
        /// </summary>
        public static bool IsCustomListType(string listType)
        {
            return !listType.StartsWith("System.Collections.Generic.List<") &&
                   !listType.StartsWith("List<") &&
                   listType.Contains("List<");
        }

        /// <summary>
        /// Checks if the type is a custom HashSet type (not standard HashSet).
        /// Examples: CustomHashSet&lt;T&gt;, ValueLogTypeHashSet, but not HashSet&lt;T&gt; or System.Collections.Generic.HashSet&lt;T&gt;
        /// </summary>
        public static bool IsCustomHashSetType(string hashSetType)
        {
            // ValueLogTypeHashSet doesn't have generic parameter, so check Contains("HashSet") instead of Contains("HashSet<")
            return !hashSetType.StartsWith("System.Collections.Generic.HashSet<") &&
                   !hashSetType.StartsWith("HashSet<") &&
                   hashSetType.Contains("HashSet");
        }

        /// <summary>
        /// Checks if the type is a KeyValuePair collection (List or ICollection of KeyValuePair).
        /// </summary>
        public static bool IsKeyValuePairCollection(string mapType)
        {
            return (mapType.Contains("List<") || mapType.Contains("ICollection<")) &&
                   mapType.Contains("KeyValuePair<");
        }

        /// <summary>
        /// Parses a Dictionary type string to extract key and value types.
        /// Example: "Dictionary&lt;string, Dictionary&lt;int, double&gt;&gt;" -> ("string", "Dictionary&lt;int, double&gt;")
        /// </summary>
        /// <param name="dictionaryTypeName">Full dictionary type name</param>
        /// <returns>Tuple of (keyType, valueType)</returns>
        public static (string keyType, string valueType) ParseDictionaryTypes(string dictionaryTypeName)
        {
            if (!IsDictionaryType(dictionaryTypeName))
                throw new System.ArgumentException($"Type '{dictionaryTypeName}' is not a Dictionary type", nameof(dictionaryTypeName));

            // Find the opening angle bracket
            var startIndex = dictionaryTypeName.IndexOf('<');
            if (startIndex < 0)
                throw new System.ArgumentException($"Invalid dictionary type format: '{dictionaryTypeName}'", nameof(dictionaryTypeName));

            startIndex++; // Skip the '<'

            // Parse key and value types, handling nested generics
            int depth = 0;
            int commaIndex = -1;

            for (int i = startIndex; i < dictionaryTypeName.Length; i++)
            {
                char c = dictionaryTypeName[i];

                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    if (depth == 0)
                    {
                        // End of dictionary type arguments
                        break;
                    }
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    // Found the comma separating key and value at top level
                    commaIndex = i;
                    break;
                }
            }

            if (commaIndex < 0)
                throw new System.ArgumentException($"Could not find key-value separator in dictionary type: '{dictionaryTypeName}'", nameof(dictionaryTypeName));

            // Extract key type
            string keyType = dictionaryTypeName.Substring(startIndex, commaIndex - startIndex).Trim();

            // Find the end of the dictionary type (matching closing '>')
            depth = 0;
            int endIndex = -1;
            for (int i = commaIndex + 1; i < dictionaryTypeName.Length; i++)
            {
                char c = dictionaryTypeName[i];

                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    if (depth == 0)
                    {
                        endIndex = i;
                        break;
                    }
                    depth--;
                }
            }

            if (endIndex < 0)
                throw new System.ArgumentException($"Could not find closing '>' in dictionary type: '{dictionaryTypeName}'", nameof(dictionaryTypeName));

            // Extract value type
            string valueType = dictionaryTypeName.Substring(commaIndex + 1, endIndex - commaIndex - 1).Trim();

            return (keyType, valueType);
        }

        #endregion
    }
}
