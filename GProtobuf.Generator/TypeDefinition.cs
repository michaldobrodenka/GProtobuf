using System.Collections.Generic;

namespace GProtobuf.Generator;

/// <summary>
/// Immutable metadata record for types marked with [ProtoContract] or [ProtoInclude].
/// Captured during source generator analysis phase and passed to code generators.
/// </summary>
/// <remarks>
/// <para><b>Design Rationale:</b></para>
/// - Sealed record: Structural equality, immutability, thread-safe
/// - Positional syntax: Compact declaration for 9 properties
/// - INamedTypeSymbol: Preserved for advanced constructor analysis (readonly structs)
/// - Separation of concerns: Metadata collection (SerializerGenerator) vs. code generation (ObjectTreeV2)
///
/// <para><b>Inheritance Modeling:</b></para>
/// - ProtoIncludes: Explicit polymorphism via [ProtoInclude(tag, typeof(Derived))]
/// - BaseClass: Flat inheritance tracking for types without ProtoInclude
/// - IsAbstract: Prevents direct instantiation in generated deserializers
///
/// <para><b>Constructor Strategy Selection:</b></para>
/// - HasParameterlessConstructor = true: Use 'new T()' in generated code
/// - HasParameterlessConstructor = false + IsStruct = false: Use FormatterServices.GetUninitializedObject
/// - HasParameterlessConstructor = false + IsStruct = true: Requires TypeSymbol for constructor matching
/// </remarks>
/// <param name="IsStruct">True if type is a struct (value type), false for classes (reference types).</param>
/// <param name="IsAbstract">True if type is abstract (cannot be instantiated directly).</param>
/// <param name="IsEnum">True if type is an enum (special handling in serialization).</param>
/// <param name="FullName">Fully qualified type name (e.g., "MyNamespace.MyClass").</param>
/// <param name="ProtoIncludes">
/// List of derived types declared via [ProtoInclude(tag, typeof(Derived))].
/// Empty list if type has no polymorphic descendants.
/// </param>
/// <param name="ProtoMembers">
/// List of serializable fields/properties marked with [ProtoMember(tag)].
/// Ordered by field ID for deterministic serialization order.
/// </param>
/// <param name="HasParameterlessConstructor">
/// True if type has a parameterless constructor (explicit or implicit).
/// False for types requiring FormatterServices or constructor parameter matching.
/// </param>
/// <param name="TypeSymbol">
/// Roslyn INamedTypeSymbol for advanced constructor analysis.
/// Required for readonly struct deserialization (constructor parameter matching).
/// Null for simple types with parameterless constructors.
/// </param>
/// <param name="BaseClass">
/// Fully qualified name of direct base class for flat inheritance tracking.
/// Null if type inherits from System.Object, System.ValueType, or has no base class.
/// Used to detect non-ProtoInclude inheritance (flat serialization without polymorphism).
/// </param>
/// <param name="CustomBufferMembers">
/// List of custom buffer members defined via [ProtoMemberBufferSize], [ProtoMemberBufferFill], [ProtoMemberBufferRead].
/// Allows user-defined serialization logic for fields not marked with [ProtoMember].
/// Empty list if type has no custom buffer members.
/// </param>
public sealed record TypeDefinition(
    bool IsStruct,
    bool IsAbstract,
    bool IsEnum,
    string FullName,
    List<ProtoIncludeAttribute> ProtoIncludes,
    List<ProtoMemberAttribute> ProtoMembers,
    bool HasParameterlessConstructor,
    Microsoft.CodeAnalysis.INamedTypeSymbol? TypeSymbol = null,
    string? BaseClass = null,
    List<CustomBufferMember>? CustomBufferMembers = null);