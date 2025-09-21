using System.Collections.Generic;

namespace GProtobuf.Generator;

internal static partial class CodeGenerator
{
    public static IEnumerable<(string FileName, string Code)> GenerateFiles(RefactoredObjectTree tree)
    {
        foreach (var namespaceWithTypes in tree)
        {
            var namespaceName = namespaceWithTypes.Key;
            var types = namespaceWithTypes.Value;

            if (types.Count is 0)
            {
                continue;
            }

            yield return ($"{namespaceName}.Deserializers.g.cs", GenerateDeserializerClass(namespaceName, types));

            yield return ($"{namespaceName}.Serializers.g.cs", GenerateSerializerClass(namespaceName, types));

            yield return ($"{namespaceName}.SpanReaders.g.cs", GenerateSpanReaderClass(namespaceName, types, tree));

            yield return ($"{namespaceName}.StreamWriters.g.cs", GenerateStreamWriterClass(namespaceName, types, tree));

            yield return ($"{namespaceName}.BufferWriters.g.cs", GenerateBufferWriterClass(namespaceName, types, tree));

            yield return ($"{namespaceName}.SizeCalculators.g.cs", GenerateSizeCalculatorClass(namespaceName, types, tree));
        }
    }
    
    private static string GetClassNameFromFullName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return string.Empty;

        // remove '?' for nullable types
        if (fullTypeName.EndsWith("?"))
        {
            fullTypeName = fullTypeName.Substring(0, fullTypeName.Length - 1);
        }

        // Handle generic types - don't split inside angle brackets
        int genericStart = fullTypeName.IndexOf('<');
        if (genericStart > 0)
        {
            // Find the last dot before the generic parameters
            string beforeGeneric = fullTypeName.Substring(0, genericStart);
            int lastDot = beforeGeneric.LastIndexOf('.');
            if (lastDot >= 0)
            {
                // Return everything after the last dot (including generic parameters)
                return fullTypeName.Substring(lastDot + 1);
            }
        }

        // Split by dots and take the last part
        string[] parts = fullTypeName.Split('.');
        return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
    }
}