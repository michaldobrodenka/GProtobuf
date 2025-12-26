using System;
using System.Text;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Generates unique, deterministic names for virtual map entry types.
    /// Handles nested generics like Dictionary&lt;CustomClass, List&lt;Dictionary&lt;int, string&gt;&gt;&gt;
    /// </summary>
    internal static class VirtualTypeNameGenerator
    {
        /// <summary>
        /// Generates a virtual type name for a map entry with given key and value types.
        /// Example: MapEntry_Int32_String, MapEntry_CustomClass_ListOfDictionaryOfInt32AndString
        /// </summary>
        public static string GetMapEntryTypeName(string keyType, string valueType)
        {
            var keyName = GetSafeTypeName(keyType);
            var valueName = GetSafeTypeName(valueType);
            return $"MapEntry_{keyName}_{valueName}";
        }

        /// <summary>
        /// Converts a type name to a safe identifier that can be used in generated code.
        /// Handles generics, nested types, and primitives.
        /// </summary>
        public static string GetSafeTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return "Unknown";

            typeName = typeName.Trim();

            // Handle nullable types
            if (typeName.EndsWith("?"))
            {
                var innerType = typeName.Substring(0, typeName.Length - 1);
                return $"Nullable{GetSafeTypeName(innerType)}";
            }

            // Handle arrays
            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);
                return $"ArrayOf{GetSafeTypeName(elementType)}";
            }

            // Handle Dictionary<K, V>
            if (IsDictionaryType(typeName))
            {
                var (keyType, valueType) = ParseDictionaryTypes(typeName);
                if (keyType != null && valueType != null)
                {
                    return $"DictionaryOf{GetSafeTypeName(keyType)}And{GetSafeTypeName(valueType)}";
                }
            }

            // Handle List<T>
            if (IsListType(typeName))
            {
                var elementType = ParseSingleGenericArg(typeName);
                if (elementType != null)
                {
                    return $"ListOf{GetSafeTypeName(elementType)}";
                }
            }

            // Handle HashSet<T>
            if (IsHashSetType(typeName))
            {
                var elementType = ParseSingleGenericArg(typeName);
                if (elementType != null)
                {
                    return $"HashSetOf{GetSafeTypeName(elementType)}";
                }
            }

            // Handle KeyValuePair<K, V>
            if (IsKeyValuePairType(typeName))
            {
                var (keyType, valueType) = ParseTwoGenericArgs(typeName);
                if (keyType != null && valueType != null)
                {
                    return $"KeyValuePairOf{GetSafeTypeName(keyType)}And{GetSafeTypeName(valueType)}";
                }
            }

            // Handle primitive types
            var primitiveMapping = GetPrimitiveTypeName(typeName);
            if (primitiveMapping != null)
            {
                return primitiveMapping;
            }

            // Handle custom types - extract class name from full namespace
            return ExtractClassName(typeName);
        }

        /// <summary>
        /// Gets a short primitive type name for common types.
        /// </summary>
        private static string GetPrimitiveTypeName(string typeName)
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            return normalized switch
            {
                "System.Int32" => "Int32",
                "System.Int64" => "Int64",
                "System.Int16" => "Int16",
                "System.UInt32" => "UInt32",
                "System.UInt64" => "UInt64",
                "System.UInt16" => "UInt16",
                "System.Byte" => "Byte",
                "System.SByte" => "SByte",
                "System.Single" => "Single",
                "System.Double" => "Double",
                "System.Boolean" => "Boolean",
                "System.String" => "String",
                "System.Guid" => "Guid",
                "System.DateTime" => "DateTime",
                "System.TimeSpan" => "TimeSpan",
                "System.Decimal" => "Decimal",
                _ => null
            };
        }

        /// <summary>
        /// Extracts the class name from a fully qualified type name.
        /// Example: "MyNamespace.SubNamespace.MyClass" -> "MyClass"
        /// </summary>
        private static string ExtractClassName(string typeName)
        {
            // Remove global:: prefix if present
            if (typeName.StartsWith("global::"))
            {
                typeName = typeName.Substring(8);
            }

            // Find the last dot that's not inside angle brackets
            int depth = 0;
            int lastDotIndex = -1;

            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == '.' && depth == 0) lastDotIndex = i;
            }

            if (lastDotIndex >= 0)
            {
                typeName = typeName.Substring(lastDotIndex + 1);
            }

            // Replace any remaining special characters
            return typeName
                .Replace("<", "Of")
                .Replace(">", "")
                .Replace(",", "And")
                .Replace(" ", "")
                .Replace(".", "_");
        }

        #region Type Detection

        private static bool IsDictionaryType(string typeName)
        {
            return typeName.Contains("Dictionary<") ||
                   typeName.Contains("IDictionary<");
        }

        private static bool IsListType(string typeName)
        {
            return (typeName.Contains("List<") || typeName.Contains("IList<") ||
                    typeName.Contains("ICollection<") || typeName.Contains("IEnumerable<"))
                   && !typeName.Contains("KeyValuePair");
        }

        private static bool IsHashSetType(string typeName)
        {
            return typeName.Contains("HashSet<") || typeName.Contains("ISet<");
        }

        private static bool IsKeyValuePairType(string typeName)
        {
            return typeName.Contains("KeyValuePair<");
        }

        #endregion

        #region Generic Parsing

        /// <summary>
        /// Parses Dictionary&lt;K, V&gt; and returns (keyType, valueType).
        /// Handles nested generics correctly.
        /// </summary>
        private static (string keyType, string valueType) ParseDictionaryTypes(string typeName)
        {
            return ParseTwoGenericArgs(typeName);
        }

        /// <summary>
        /// Parses a generic type with two type arguments (e.g., Dictionary&lt;K, V&gt;, KeyValuePair&lt;K, V&gt;).
        /// </summary>
        private static (string first, string second) ParseTwoGenericArgs(string typeName)
        {
            int startIndex = typeName.IndexOf('<');
            if (startIndex < 0) return (null, null);

            int endIndex = typeName.LastIndexOf('>');
            if (endIndex <= startIndex) return (null, null);

            string innerContent = typeName.Substring(startIndex + 1, endIndex - startIndex - 1);

            // Find the comma that separates the two type arguments
            // Need to handle nested generics like Dictionary<int, List<string>>
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

        /// <summary>
        /// Parses a generic type with a single type argument (e.g., List&lt;T&gt;, HashSet&lt;T&gt;).
        /// </summary>
        private static string ParseSingleGenericArg(string typeName)
        {
            int startIndex = typeName.IndexOf('<');
            if (startIndex < 0) return null;

            int endIndex = typeName.LastIndexOf('>');
            if (endIndex <= startIndex) return null;

            return typeName.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
        }

        #endregion
    }
}
