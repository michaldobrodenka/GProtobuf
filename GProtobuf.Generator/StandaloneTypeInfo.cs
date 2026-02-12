using Microsoft.CodeAnalysis;

namespace GProtobuf.Generator;

/// <summary>
/// Represents a standalone type for serialization (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;, primitives).
/// These types are registered via [assembly: GenerateSerializer(typeof(...))] attribute.
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
    ITypeSymbol TypeSymbol);

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
