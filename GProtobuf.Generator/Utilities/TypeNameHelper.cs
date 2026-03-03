using System.Collections.Generic;
using System.Text;

namespace GProtobuf.Generator.Utilities
{
    /// <summary>
    /// Helper class for sanitizing type names for use in generated method names.
    /// </summary>
    internal static class TypeNameHelper
    {
        /// <summary>
        /// Gets a safe method name from a full type name, removing "System." prefixes.
        /// </summary>
        public static string GetSafeMethodName(string fullName)
        {
            var className = GetClassName(fullName);
            return className?.Replace("System.", "") ?? className;
        }

        /// <summary>
        /// Extracts the namespace from a full type name.
        /// </summary>
        public static string GetNamespace(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return string.Empty;

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
        /// </summary>
        public static string GetClassName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            if (fullName.EndsWith("?"))
                fullName = fullName.Substring(0, fullName.Length - 1);

            if (fullName.StartsWith("(") && fullName.EndsWith(")"))
            {
                return SanitizeValueTuple(fullName);
            }

            var simpleName = GetSimpleTypeName(fullName);

            if (simpleName.Contains("<") || simpleName.Contains("["))
            {
                return SanitizeGenericTypeName(simpleName);
            }

            if (simpleName.EndsWith("?"))
                simpleName = simpleName.Substring(0, simpleName.Length - 1);

            return simpleName;
        }

        private static string GetSimpleTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

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

        private static string SanitizeValueTuple(string tupleType)
        {
            var inner = tupleType.Substring(1, tupleType.Length - 2);

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

        private static string SanitizeGenericTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            if (typeName.Contains(".") && !typeName.StartsWith("<"))
            {
                var genericIdx = typeName.IndexOf('<');
                if (genericIdx > 0)
                {
                    var beforeGeneric = typeName.Substring(0, genericIdx);
                    var lastDot = beforeGeneric.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        var simpleTypeName = beforeGeneric.Substring(lastDot + 1);
                        typeName = simpleTypeName + typeName.Substring(genericIdx);
                    }
                }
                else
                {
                    var lastDot = typeName.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        typeName = typeName.Substring(lastDot + 1);
                    }
                }
            }

            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);
                return SanitizeGenericTypeName(elementType) + "Array";
            }

            var genericStart = typeName.IndexOf('<');
            if (genericStart < 0)
            {
                return SanitizePrimitiveName(typeName);
            }

            var baseType = typeName.Substring(0, genericStart);
            var genericEnd = typeName.LastIndexOf('>');
            if (genericEnd <= genericStart)
                return typeName;

            var genericArgs = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);

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

            if (result == typeName && typeName.Contains("."))
            {
                return GetSimpleTypeName(typeName);
            }

            return result;
        }
    }
}
