using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace GProtobuf.Generator;

/// <summary>
/// Analyzes ITypeSymbol from [GenerateSerializer] attribute and creates StandaloneTypeInfo.
/// </summary>
public static class StandaloneTypeAnalyzer
{
    private static readonly HashSet<string> PrimitiveTypes = new()
    {
        "int", "System.Int32",
        "uint", "System.UInt32",
        "long", "System.Int64",
        "ulong", "System.UInt64",
        "short", "System.Int16",
        "ushort", "System.UInt16",
        "byte", "System.Byte",
        "sbyte", "System.SByte",
        "bool", "System.Boolean",
        "float", "System.Single",
        "double", "System.Double",
        "string", "System.String",
        "byte[]", "System.Byte[]",
        "System.Guid",
        "System.DateTime",
        "System.TimeSpan",
        "System.Decimal"
    };

    /// <summary>
    /// Analyzes an ITypeSymbol and creates StandaloneTypeInfo.
    /// Returns null if the type is not supported.
    /// </summary>
    public static StandaloneTypeInfo? Analyze(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return null;

        // Check for array type
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return AnalyzeArray(arrayType);
        }

        // Check for generic types (List<T>, Dictionary<K,V>)
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            return AnalyzeGenericType(namedType);
        }

        // Check for primitive type
        if (IsPrimitiveType(typeSymbol.ToDisplayString()))
        {
            return AnalyzePrimitive(typeSymbol);
        }

        // Unsupported type
        return null;
    }

    private static StandaloneTypeInfo? AnalyzeArray(IArrayTypeSymbol arrayType)
    {
        var elementType = arrayType.ElementType;
        var elementTypeName = elementType.ToDisplayString();
        var isEnum = IsEnumType(elementType);
        var isPrimitive = IsPrimitiveType(elementTypeName) || isEnum;

        // byte[] is handled specially - not as a collection
        if (elementType.SpecialType == SpecialType.System_Byte)
        {
            return new StandaloneTypeInfo(
                Kind: StandaloneTypeKind.Primitive,
                FullTypeName: "byte[]",
                TargetNamespace: "GProtobuf.Core",
                MethodNameSuffix: "ByteArray",
                ElementType: null,
                ElementIsPrimitive: true,
                KeyType: null,
                ValueType: null,
                TypeSymbol: arrayType);
        }

        var targetNamespace = GetTargetNamespace(elementType);
        var methodNameSuffix = "ArrayOf" + GetSafeTypeName(elementTypeName);

        // Recursively analyze element type for nested generics
        StandaloneTypeKind? elementKind = null;
        StandaloneTypeInfo? nestedElementInfo = null;

        if (!isPrimitive)
        {
            // Check if element is a generic type (List, Dictionary, etc.)
            if (elementType is INamedTypeSymbol namedElement && namedElement.IsGenericType)
            {
                nestedElementInfo = AnalyzeGenericType(namedElement);
                elementKind = nestedElementInfo?.Kind;
            }
            else if (elementType is IArrayTypeSymbol nestedArray)
            {
                nestedElementInfo = AnalyzeArray(nestedArray);
                elementKind = StandaloneTypeKind.Array;
            }
        }

        return new StandaloneTypeInfo(
            Kind: StandaloneTypeKind.Array,
            FullTypeName: arrayType.ToDisplayString(),
            TargetNamespace: targetNamespace,
            MethodNameSuffix: methodNameSuffix,
            ElementType: elementTypeName,
            ElementIsPrimitive: isPrimitive,
            KeyType: null,
            ValueType: null,
            TypeSymbol: arrayType,
            ElementKind: elementKind,
            NestedElementInfo: nestedElementInfo,
            ElementIsEnum: isEnum);
    }

    private static StandaloneTypeInfo? AnalyzeGenericType(INamedTypeSymbol namedType)
    {
        var originalDef = namedType.OriginalDefinition.ToDisplayString();

        // Check for List<T>, IList<T>, ICollection<T>
        if (IsListType(originalDef) && namedType.TypeArguments.Length == 1)
        {
            return AnalyzeListType(namedType);
        }

        // Check for standard Dictionary<K,V>, IDictionary<K,V>
        if (IsDictionaryType(originalDef) && namedType.TypeArguments.Length == 2)
        {
            return AnalyzeDictionaryType(namedType, isCustomDictionary: false);
        }

        // Check for custom dictionary types (implement IDictionary<K,V>)
        var dictionaryInterface = FindDictionaryInterface(namedType);
        if (dictionaryInterface != null)
        {
            return AnalyzeDictionaryType(namedType, isCustomDictionary: true, dictionaryInterface);
        }

        return null;
    }

    private static StandaloneTypeInfo AnalyzeListType(INamedTypeSymbol namedType)
    {
        var elementType = namedType.TypeArguments[0];
        var elementTypeName = elementType.ToDisplayString();
        var isEnum = IsEnumType(elementType);
        var isPrimitive = IsPrimitiveType(elementTypeName) || isEnum;
        var targetNamespace = GetTargetNamespace(elementType);
        var methodNameSuffix = "ListOf" + GetSafeTypeName(elementTypeName);

        // Recursively analyze element type for nested generics
        StandaloneTypeKind? elementKind = null;
        StandaloneTypeInfo? nestedElementInfo = null;

        if (!isPrimitive)
        {
            if (elementType is INamedTypeSymbol namedElement && namedElement.IsGenericType)
            {
                nestedElementInfo = AnalyzeGenericType(namedElement);
                elementKind = nestedElementInfo?.Kind;
            }
            else if (elementType is IArrayTypeSymbol arrayElement)
            {
                nestedElementInfo = AnalyzeArray(arrayElement);
                elementKind = StandaloneTypeKind.Array;
            }
        }

        return new StandaloneTypeInfo(
            Kind: StandaloneTypeKind.List,
            FullTypeName: namedType.ToDisplayString(),
            TargetNamespace: targetNamespace,
            MethodNameSuffix: methodNameSuffix,
            ElementType: elementTypeName,
            ElementIsPrimitive: isPrimitive,
            KeyType: null,
            ValueType: null,
            TypeSymbol: namedType,
            ElementKind: elementKind,
            NestedElementInfo: nestedElementInfo,
            ElementIsEnum: isEnum);
    }

    private static StandaloneTypeInfo AnalyzeDictionaryType(INamedTypeSymbol namedType, bool isCustomDictionary, INamedTypeSymbol? dictionaryInterface = null)
    {
        // Get key/value types from the interface or directly from type arguments
        ITypeSymbol keyType, valueType;
        if (dictionaryInterface != null)
        {
            keyType = dictionaryInterface.TypeArguments[0];
            valueType = dictionaryInterface.TypeArguments[1];
        }
        else
        {
            keyType = namedType.TypeArguments[0];
            valueType = namedType.TypeArguments[1];
        }

        var keyTypeName = keyType.ToDisplayString();
        var valueTypeName = valueType.ToDisplayString();
        var keyIsEnum = IsEnumType(keyType);
        var valueIsEnum = IsEnumType(valueType);
        var keyIsPrimitive = IsPrimitiveType(keyTypeName) || keyIsEnum;
        var valueIsPrimitive = IsPrimitiveType(valueTypeName) || valueIsEnum;

        // Target namespace from value type (more likely to be user type)
        var targetNamespace = GetTargetNamespace(valueType);
        if (targetNamespace == "System" || targetNamespace.StartsWith("System."))
        {
            targetNamespace = GetTargetNamespace(keyType);
        }
        if (targetNamespace == "System" || targetNamespace.StartsWith("System."))
        {
            targetNamespace = "GProtobuf.Generated";
        }

        var methodNameSuffix = (isCustomDictionary ? GetSimpleName(namedType.ToDisplayString().Split('<')[0]) : "Dictionary")
            + "Of" + GetSafeTypeName(keyTypeName) + "And" + GetSafeTypeName(valueTypeName);

        // Recursively analyze value type for nested generics
        StandaloneTypeKind? valueKind = null;
        StandaloneTypeInfo? nestedValueInfo = null;
        string? innerElementType = null;
        bool innerElementIsPrimitive = false;

        if (!valueIsPrimitive)
        {
            if (valueType is INamedTypeSymbol namedValue && namedValue.IsGenericType)
            {
                nestedValueInfo = AnalyzeGenericType(namedValue);
                valueKind = nestedValueInfo?.Kind;

                // Extract inner element type for List<T> or Dictionary<K,V> values
                if (nestedValueInfo != null)
                {
                    innerElementType = nestedValueInfo.ElementType ?? nestedValueInfo.ValueType;
                    innerElementIsPrimitive = nestedValueInfo.ElementIsPrimitive || nestedValueInfo.ValueIsPrimitive;
                }
            }
            else if (valueType is IArrayTypeSymbol arrayValue)
            {
                nestedValueInfo = AnalyzeArray(arrayValue);
                valueKind = StandaloneTypeKind.Array;
                innerElementType = nestedValueInfo?.ElementType;
                innerElementIsPrimitive = nestedValueInfo?.ElementIsPrimitive ?? false;
            }
        }

        // Analyze key type kind (usually primitive or custom type)
        StandaloneTypeKind? keyKind = keyIsPrimitive ? StandaloneTypeKind.Primitive : null;
        if (!keyIsPrimitive && keyType is INamedTypeSymbol namedKey && namedKey.IsGenericType)
        {
            var keyInfo = AnalyzeGenericType(namedKey);
            keyKind = keyInfo?.Kind;
        }

        return new StandaloneTypeInfo(
            Kind: StandaloneTypeKind.Dictionary,
            FullTypeName: namedType.ToDisplayString(),
            TargetNamespace: targetNamespace,
            MethodNameSuffix: methodNameSuffix,
            ElementType: null,
            ElementIsPrimitive: false,
            KeyType: keyTypeName,
            ValueType: valueTypeName,
            TypeSymbol: namedType,
            KeyIsPrimitive: keyIsPrimitive,
            ValueIsPrimitive: valueIsPrimitive,
            KeyKind: keyKind,
            ValueKind: valueKind,
            NestedValueInfo: nestedValueInfo,
            IsCustomDictionaryType: isCustomDictionary,
            CustomDictionaryTypeName: isCustomDictionary ? namedType.ToDisplayString().Split('<')[0] : null,
            InnerElementType: innerElementType,
            InnerElementIsPrimitive: innerElementIsPrimitive,
            KeyIsEnum: keyIsEnum,
            ValueIsEnum: valueIsEnum);
    }

    /// <summary>
    /// Finds IDictionary&lt;K,V&gt; interface if the type implements it.
    /// </summary>
    private static INamedTypeSymbol? FindDictionaryInterface(INamedTypeSymbol namedType)
    {
        foreach (var iface in namedType.AllInterfaces)
        {
            var ifaceDef = iface.OriginalDefinition.ToDisplayString();
            if (ifaceDef == "System.Collections.Generic.IDictionary<TKey, TValue>" && iface.TypeArguments.Length == 2)
            {
                return iface;
            }
        }
        return null;
    }

    private static StandaloneTypeInfo? AnalyzePrimitive(ITypeSymbol typeSymbol)
    {
        var typeName = typeSymbol.ToDisplayString();
        var methodNameSuffix = GetSafeTypeName(typeName);

        return new StandaloneTypeInfo(
            Kind: StandaloneTypeKind.Primitive,
            FullTypeName: typeName,
            TargetNamespace: "GProtobuf.Core",
            MethodNameSuffix: methodNameSuffix,
            ElementType: null,
            ElementIsPrimitive: true,
            KeyType: null,
            ValueType: null,
            TypeSymbol: typeSymbol);
    }

    private static bool IsListType(string originalDef)
    {
        return originalDef == "System.Collections.Generic.List<T>" ||
               originalDef == "System.Collections.Generic.IList<T>" ||
               originalDef == "System.Collections.Generic.ICollection<T>" ||
               originalDef == "System.Collections.Generic.IEnumerable<T>";
    }

    private static bool IsDictionaryType(string originalDef)
    {
        return originalDef == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
               originalDef == "System.Collections.Generic.IDictionary<TKey, TValue>" ||
               originalDef == "System.Collections.Generic.SortedDictionary<TKey, TValue>";
    }

    private static bool IsPrimitiveType(string typeName)
    {
        return PrimitiveTypes.Contains(typeName);
    }

    private static bool IsEnumType(ITypeSymbol typeSymbol)
    {
        return typeSymbol.TypeKind == TypeKind.Enum;
    }

    private static string GetTargetNamespace(ITypeSymbol typeSymbol)
    {
        var ns = typeSymbol.ContainingNamespace;
        if (ns == null || ns.IsGlobalNamespace)
            return "GProtobuf.Generated";

        var nsName = ns.ToDisplayString();

        // For primitive types (System namespace), use GProtobuf.Generated instead
        if (nsName == "System" || nsName.StartsWith("System."))
            return "GProtobuf.Generated";

        return nsName;
    }

    /// <summary>
    /// Converts a type name to a safe method name suffix.
    /// E.g., "System.Collections.Generic.List&lt;int&gt;" -> "ListOfInt32"
    /// E.g., "MyNamespace.MyClass" -> "MyClass"
    /// </summary>
    private static string GetSafeTypeName(string typeName)
    {
        // Handle arrays
        if (typeName.EndsWith("[]"))
        {
            var elementType = typeName.Substring(0, typeName.Length - 2);
            return "ArrayOf" + GetSafeTypeName(elementType);
        }

        // Handle generics
        var genericIndex = typeName.IndexOf('<');
        if (genericIndex > 0)
        {
            var baseName = GetSimpleName(typeName.Substring(0, genericIndex));
            var argsStart = genericIndex + 1;
            var argsEnd = typeName.LastIndexOf('>');
            if (argsEnd > argsStart)
            {
                var args = typeName.Substring(argsStart, argsEnd - argsStart);
                var argNames = SplitGenericArgs(args);
                return baseName + "Of" + string.Join("And", argNames.Select(GetSafeTypeName));
            }
        }

        // Simple type - just get class name
        return GetSimpleName(typeName);
    }

    private static string GetSimpleName(string typeName)
    {
        // Map common primitive types
        switch (typeName)
        {
            case "int":
            case "System.Int32": return "Int32";
            case "uint":
            case "System.UInt32": return "UInt32";
            case "long":
            case "System.Int64": return "Int64";
            case "ulong":
            case "System.UInt64": return "UInt64";
            case "short":
            case "System.Int16": return "Int16";
            case "ushort":
            case "System.UInt16": return "UInt16";
            case "byte":
            case "System.Byte": return "Byte";
            case "sbyte":
            case "System.SByte": return "SByte";
            case "bool":
            case "System.Boolean": return "Boolean";
            case "float":
            case "System.Single": return "Single";
            case "double":
            case "System.Double": return "Double";
            case "string":
            case "System.String": return "String";
            case "System.Guid": return "Guid";
            case "System.DateTime": return "DateTime";
            case "System.TimeSpan": return "TimeSpan";
            case "System.Decimal": return "Decimal";
        }

        // Get last part of namespace-qualified name
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot >= 0)
        {
            return typeName.Substring(lastDot + 1);
        }

        return typeName;
    }

    private static List<string> SplitGenericArgs(string args)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',':
                    if (depth == 0)
                    {
                        result.Add(args.Substring(start, i - start).Trim());
                        start = i + 1;
                    }
                    break;
            }
        }

        result.Add(args.Substring(start).Trim());
        return result;
    }
}
