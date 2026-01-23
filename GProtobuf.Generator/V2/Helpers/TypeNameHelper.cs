using System.Collections.Generic;
using System.Text;

namespace GProtobuf.Generator.V2.Helpers
{
    /// <summary>
    /// Helper class for sanitizing type names for use in generated method names.
    /// </summary>
    internal static class TypeNameHelper
    {
        /// <summary>
        /// Gets a safe method name from a full type name, removing "System." prefixes.
        /// Use this for generating method names in virtual type generators (Tuple, Map, etc).
        /// </summary>
        public static string GetSafeMethodName(string fullName)
        {
            var className = GetClassName(fullName);
            // Remove "System." prefix that may appear in nested type names
            return className?.Replace("System.", "") ?? className;
        }

        /// <summary>
        /// Extracts the namespace from a full type name.
        /// Handles generic types by finding the last dot outside of angle brackets.
        /// </summary>
        public static string GetNamespace(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return string.Empty;

            // Find the last dot that's not inside angle brackets or parentheses
            int depth = 0;
            int lastValidDot = -1;
            for (int i = fullTypeName.Length - 1; i >= 0; i--)
            {
                char c = fullTypeName[i];
                if (c == '>' || c == ')') depth++;
                else if (c == '<' || c == '(') depth--;
                else if (c == '.' && depth == 0)
                {
                    lastValidDot = i;
                    break;
                }
            }

            return lastValidDot >= 0 ? fullTypeName.Substring(0, lastValidDot) : string.Empty;
        }

        /// <summary>
        /// Gets a valid C# method name from a full type name.
        /// Handles nullable types, value tuples, arrays, and generic types.
        /// </summary>
        public static string GetClassName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // Strip nullable suffix first
            if (fullName.EndsWith("?"))
                fullName = fullName.Substring(0, fullName.Length - 1);

            // Handle value tuples (T1, T2) -> TupleOfT1AndT2
            if (fullName.StartsWith("(") && fullName.EndsWith(")"))
            {
                return SanitizeValueTuple(fullName);
            }

            // Get simple name without namespace (careful with dots inside generics)
            var simpleName = GetSimpleTypeName(fullName);

            // Sanitize generic type names and arrays for method naming
            if (simpleName.Contains("<") || simpleName.Contains("["))
            {
                return SanitizeGenericTypeName(simpleName);
            }

            // Handle nullable marker that might remain
            if (simpleName.EndsWith("?"))
                simpleName = simpleName.Substring(0, simpleName.Length - 1);

            return simpleName;
        }

        /// <summary>
        /// Gets the simple type name, handling dots inside generic arguments correctly.
        /// </summary>
        private static string GetSimpleTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // Find the last dot that's not inside angle brackets or parentheses
            int depth = 0;
            int lastValidDot = -1;
            for (int i = fullName.Length - 1; i >= 0; i--)
            {
                char c = fullName[i];
                if (c == '>' || c == ')') depth++;
                else if (c == '<' || c == '(') depth--;
                else if (c == '.' && depth == 0)
                {
                    lastValidDot = i;
                    break;
                }
            }

            return lastValidDot >= 0 ? fullName.Substring(lastValidDot + 1) : fullName;
        }

        /// <summary>
        /// Converts value tuple (T1, T2) to TupleOfT1AndT2
        /// </summary>
        private static string SanitizeValueTuple(string tupleType)
        {
            // Remove outer parentheses
            var inner = tupleType.Substring(1, tupleType.Length - 2);

            // Parse arguments (handling nested types)
            var args = new List<string>();
            var currentArg = new StringBuilder();
            int depth = 0;

            foreach (char c in inner)
            {
                if (c == '<' || c == '(') { depth++; currentArg.Append(c); }
                else if (c == '>' || c == ')') { depth--; currentArg.Append(c); }
                else if (c == ',' && depth == 0)
                {
                    args.Add(GetClassName(currentArg.ToString().Trim()));
                    currentArg.Clear();
                }
                else { currentArg.Append(c); }
            }
            if (currentArg.Length > 0)
                args.Add(GetClassName(currentArg.ToString().Trim()));

            return "TupleOf" + string.Join("And", args);
        }

        /// <summary>
        /// Converts generic type names to valid C# method name parts.
        /// Example: Tuple&lt;double, double&gt; -> TupleOfDoubleAndDouble
        /// </summary>
        private static string SanitizeGenericTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            // Handle arrays
            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);
                return SanitizeGenericTypeName(elementType) + "Array";
            }

            // Handle generics
            var genericStart = typeName.IndexOf('<');
            if (genericStart < 0)
            {
                // No generics, just return sanitized primitive name
                return SanitizePrimitiveName(typeName);
            }

            var baseType = typeName.Substring(0, genericStart);
            var genericEnd = typeName.LastIndexOf('>');
            if (genericEnd <= genericStart)
                return typeName;

            var genericArgs = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);

            // Parse and sanitize each generic argument
            var sanitizedArgs = new List<string>();
            var currentArg = new StringBuilder();
            int depth = 0;

            foreach (char c in genericArgs)
            {
                if (c == '<')
                {
                    depth++;
                    currentArg.Append(c);
                }
                else if (c == '>')
                {
                    depth--;
                    currentArg.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    sanitizedArgs.Add(SanitizeGenericTypeName(currentArg.ToString().Trim()));
                    currentArg.Clear();
                }
                else
                {
                    currentArg.Append(c);
                }
            }

            if (currentArg.Length > 0)
            {
                sanitizedArgs.Add(SanitizeGenericTypeName(currentArg.ToString().Trim()));
            }

            // Build method-friendly name
            return baseType + "Of" + string.Join("And", sanitizedArgs);
        }

        private static string SanitizePrimitiveName(string typeName)
        {
            var result = typeName switch
            {
                "string" or "String" => "string",
                "int" or "Int32" => "int",
                "long" or "Int64" => "long",
                "float" or "Single" => "float",
                "double" or "Double" => "double",
                "bool" or "Boolean" => "bool",
                "byte" or "Byte" => "byte",
                "short" or "Int16" => "short",
                "uint" or "UInt32" => "uint",
                "ulong" or "UInt64" => "ulong",
                "ushort" or "UInt16" => "ushort",
                "sbyte" or "SByte" => "sbyte",
                "decimal" or "Decimal" => "decimal",
                "char" or "Char" => "char",
                "Guid" => "Guid",
                "DateTime" => "DateTime",
                "TimeSpan" => "TimeSpan",
                _ => typeName
            };

            // If not a primitive and contains namespace, extract simple name
            if (result == typeName && typeName.Contains("."))
            {
                return GetSimpleTypeName(typeName);
            }

            return result;
        }
    }
}
