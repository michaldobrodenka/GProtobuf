using Microsoft.CodeAnalysis;

namespace GProtobuf.Generator.Analysis
{
    /// <summary>
    /// Represents a standalone type for serialization (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;, primitives).
    /// These types are registered via [assembly: GenerateSerializer(typeof(...))] attribute.
    /// </summary>
    public sealed record StandaloneTypeInfo(
        StandaloneTypeKind Kind,
        string FullTypeName,
        string TargetNamespace,
        string MethodNameSuffix,
        string? ElementType,
        bool ElementIsPrimitive,
        string? KeyType,
        string? ValueType,
        ITypeSymbol TypeSymbol,
        bool KeyIsPrimitive = false,
        bool ValueIsPrimitive = false,
        StandaloneTypeKind? ElementKind = null,
        StandaloneTypeKind? KeyKind = null,
        StandaloneTypeKind? ValueKind = null,
        StandaloneTypeInfo? NestedValueInfo = null,
        StandaloneTypeInfo? NestedElementInfo = null,
        bool IsCustomDictionaryType = false,
        string? CustomDictionaryTypeName = null,
        string? InnerElementType = null,
        bool InnerElementIsPrimitive = false,
        bool ElementIsEnum = false,
        string? ElementEnumUnderlyingType = null,
        bool KeyIsEnum = false,
        string? KeyEnumUnderlyingType = null,
        bool ValueIsEnum = false,
        string? ValueEnumUnderlyingType = null);

    /// <summary>
    /// Kind of standalone type for serialization.
    /// </summary>
    public enum StandaloneTypeKind
    {
        List,
        Array,
        Dictionary,
        Primitive
    }
}
