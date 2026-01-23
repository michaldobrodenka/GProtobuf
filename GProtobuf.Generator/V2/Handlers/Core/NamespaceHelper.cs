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
        /// Correctly handles nested classes by using TypeNameHelper to extract the actual namespace.
        /// </summary>
        /// <param name="fullTypeName">The full type name (e.g., "GProtobuf.Tests.TestModel.NestedDictionaryValue")</param>
        /// <returns>The serialization namespace (e.g., "global::GProtobuf.Tests.TestModel.Serialization")</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization"
        /// </example>
        public static string GetSerializationNamespace(string fullTypeName)
        {
            // Remove global:: prefix if present
            if (fullTypeName.StartsWith("global::"))
            {
                fullTypeName = fullTypeName.Substring(8);
            }

            // Use TypeNameHelper to properly extract namespace (handles nested classes)
            var typeNamespace = TypeNameHelper.GetNamespace(fullTypeName);
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
        /// <returns>The fully qualified SpanReaders class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.SpanReaders"
        /// </example>
        public static string GetSpanReadersClass(string fullTypeName)
        {
            return $"{GetSerializationNamespace(fullTypeName)}.SpanReaders";
        }

        /// <summary>
        /// Gets the fully qualified SizeCalculators class reference for a custom type.
        /// </summary>
        /// <param name="fullTypeName">The full type name</param>
        /// <returns>The fully qualified SizeCalculators class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.SizeCalculators"
        /// </example>
        public static string GetSizeCalculatorsClass(string fullTypeName)
        {
            return $"{GetSerializationNamespace(fullTypeName)}.SizeCalculators";
        }

        /// <summary>
        /// Gets the fully qualified Writers class reference for a custom type.
        /// </summary>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="writerClassName">The writer class name (e.g., "StreamWriters", "BufferWriters")</param>
        /// <returns>The fully qualified Writers class name</returns>
        /// <example>
        /// Input: "GProtobuf.Tests.TestModel.NestedDictionaryValue", "StreamWriters"
        /// Output: "global::GProtobuf.Tests.TestModel.Serialization.StreamWriters"
        /// </example>
        public static string GetWritersClass(string fullTypeName, string writerClassName)
        {
            return $"{GetSerializationNamespace(fullTypeName)}.{writerClassName}";
        }
    }
}
