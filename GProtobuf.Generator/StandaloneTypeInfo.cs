using Microsoft.CodeAnalysis;

namespace GProtobuf.Generator;

/// <summary>
/// Represents a standalone type for serialization (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;, primitives).
/// These types are registered via [assembly: GenerateSerializer(typeof(...))] attribute.
/// Supports nested generics up to 2 levels (e.g., Dictionary&lt;K, List&lt;V&gt;&gt;).
/// </summary>
public sealed record StandaloneTypeInfo(
    /// <summary>
    /// The kind of standalone type (List, Array, Dictionary, Primitive)
    /// </summary>
    StandaloneTypeKind Kind,

    /// <summary>
    /// Full type name (e.g., "System.Collections.Generic.List&lt;MyNamespace.MyClass&gt;")
    /// </summary>
    string FullTypeName,

    /// <summary>
    /// Namespace where serializers should be generated
    /// For List&lt;MyNamespace.MyClass&gt; this would be "MyNamespace"
    /// </summary>
    string TargetNamespace,

    /// <summary>
    /// Safe method name suffix (e.g., "ListOfMyClass", "ArrayOfInt32", "DictionaryOfStringAndMyClass")
    /// </summary>
    string MethodNameSuffix,

    /// <summary>
    /// Element type for List&lt;T&gt; or T[] (null for Dictionary)
    /// </summary>
    string? ElementType,

    /// <summary>
    /// Whether element type is a primitive (int, string, etc.)
    /// </summary>
    bool ElementIsPrimitive,

    /// <summary>
    /// Key type for Dictionary&lt;K,V&gt; (null for List/Array)
    /// </summary>
    string? KeyType,

    /// <summary>
    /// Value type for Dictionary&lt;K,V&gt; (null for List/Array)
    /// </summary>
    string? ValueType,

    /// <summary>
    /// Original type symbol for additional analysis
    /// </summary>
    ITypeSymbol TypeSymbol,

    // ============ NEW FIELDS FOR NESTED GENERIC SUPPORT ============

    /// <summary>
    /// Whether the dictionary key type is a primitive (for Dictionary types)
    /// </summary>
    bool KeyIsPrimitive = false,

    /// <summary>
    /// Whether the dictionary value type is a primitive (for Dictionary types)
    /// </summary>
    bool ValueIsPrimitive = false,

    /// <summary>
    /// Kind of the element type for nested generics (e.g., List in List&lt;List&lt;T&gt;&gt;)
    /// </summary>
    StandaloneTypeKind? ElementKind = null,

    /// <summary>
    /// Kind of the dictionary key type (usually Primitive or custom type)
    /// </summary>
    StandaloneTypeKind? KeyKind = null,

    /// <summary>
    /// Kind of the dictionary value type (List, Array, Dictionary, or custom type)
    /// </summary>
    StandaloneTypeKind? ValueKind = null,

    /// <summary>
    /// Nested type info for recursive structures (e.g., the List&lt;T&gt; in Dictionary&lt;K, List&lt;T&gt;&gt;)
    /// </summary>
    StandaloneTypeInfo? NestedValueInfo = null,

    /// <summary>
    /// Nested type info for element type in collections (e.g., the T[] in List&lt;T[]&gt;)
    /// </summary>
    StandaloneTypeInfo? NestedElementInfo = null,

    /// <summary>
    /// Whether this is a custom dictionary type (e.g., ListDictionary&lt;K,V&gt;) rather than standard Dictionary
    /// </summary>
    bool IsCustomDictionaryType = false,

    /// <summary>
    /// Original custom dictionary type name (e.g., "MyNamespace.ListDictionary") for instantiation
    /// </summary>
    string? CustomDictionaryTypeName = null,

    /// <summary>
    /// Inner element type for nested collections (e.g., T in Dictionary&lt;K, List&lt;T&gt;&gt;)
    /// </summary>
    string? InnerElementType = null,

    /// <summary>
    /// Whether inner element type is primitive
    /// </summary>
    bool InnerElementIsPrimitive = false,

    /// <summary>
    /// Whether the element type is an enum (for array/list types)
    /// </summary>
    bool ElementIsEnum = false,

    /// <summary>
    /// Whether the key type is an enum (for dictionary types)
    /// </summary>
    bool KeyIsEnum = false,

    /// <summary>
    /// Whether the value type is an enum (for dictionary types)
    /// </summary>
    bool ValueIsEnum = false);

/// <summary>
/// Kind of standalone type for serialization
/// </summary>
public enum StandaloneTypeKind
{
    /// <summary>
    /// List&lt;T&gt; or IList&lt;T&gt; or ICollection&lt;T&gt;
    /// </summary>
    List,

    /// <summary>
    /// T[] array
    /// </summary>
    Array,

    /// <summary>
    /// Dictionary&lt;K,V&gt; or IDictionary&lt;K,V&gt;
    /// </summary>
    Dictionary,

    /// <summary>
    /// Primitive type (int, string, byte[], etc.)
    /// </summary>
    Primitive
}
