using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Helper methods for working with namespaces and generating fully qualified type references
    /// for cross-namespace serialization code generation.
    /// </summary>
    internal static class NamespaceHelper
    {
        /// <summary>
        /// Gets the full namespace path for serialization classes from a type name.
        /// Uses TypeRegistry for accurate namespace resolution, especially for nested types.
        /// </summary>
        /// <param name="fullTypeName">The full type name (e.g., "GProtobuf.Tests.TestModel.NestedDictionaryValue")</param>
        /// <param name="registry">Optional TypeRegistry for accurate nested type namespace resolution</param>
        /// <returns>The serialization namespace (e.g., "global::GProtobuf.Tests.TestModel.Serialization")</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization"
        /// </example>
        public static string GetSerializationNamespace(string fullTypeName, TypeRegistry registry = null)
        {
            // Remove global:: prefix if present
            if (fullTypeName.StartsWith("global::"))
            {
                fullTypeName = fullTypeName.Substring(8);
            }

            string typeNamespace;

            // Try to use TypeRegistry for accurate namespace resolution (handles nested types correctly)
            if (registry != null)
            {
                typeNamespace = registry.GetNamespaceForType(fullTypeName);
            }
            else
            {
                // Fallback to TypeNameHelper (may be incorrect for nested types)
                typeNamespace = TypeNameHelper.GetNamespace(fullTypeName);
            }

            if (string.IsNullOrEmpty(typeNamespace))
            {
                return "global::Serialization";
            }

            return $"global::{typeNamespace}.Serialization";
        }

        /// <summary>
        /// Gets the fully qualified SpanReaders class reference for a custom type.
        /// </summary>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="registry">Optional TypeRegistry for accurate nested type namespace resolution</param>
        /// <returns>The fully qualified SpanReaders class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.SpanReaders"
        /// </example>
        public static string GetSpanReadersClass(string fullTypeName, TypeRegistry registry = null)
        {
            return $"{GetSerializationNamespace(fullTypeName, registry)}.SpanReaders";
        }

        /// <summary>
        /// Gets the fully qualified SizeCalculators class reference for a custom type.
        /// </summary>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="registry">Optional TypeRegistry for accurate nested type namespace resolution</param>
        /// <returns>The fully qualified SizeCalculators class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.SizeCalculators"
        /// </example>
        public static string GetSizeCalculatorsClass(string fullTypeName, TypeRegistry registry = null)
        {
            return $"{GetSerializationNamespace(fullTypeName, registry)}.SizeCalculators";
        }

        /// <summary>
        /// Gets the fully qualified Writers class reference for a custom type.
        /// </summary>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="writerClassName">The writer class name (e.g., "StreamWriters", "BufferWriters")</param>
        /// <param name="registry">Optional TypeRegistry for accurate nested type namespace resolution</param>
        /// <returns>The fully qualified Writers class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue", "StreamWriters"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.StreamWriters"
        /// </example>
        public static string GetWritersClass(string fullTypeName, string writerClassName, TypeRegistry registry = null)
        {
            return $"{GetSerializationNamespace(fullTypeName, registry)}.{writerClassName}";
        }
    }
}
