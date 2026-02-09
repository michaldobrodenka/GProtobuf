using System.Collections.Generic;

namespace GProtobuf.Generator;

public sealed record TypeDefinition(
    bool IsStruct,
    bool IsAbstract,
    bool IsEnum,
    string FullName,
    List<ProtoIncludeAttribute> ProtoIncludes, // List of ProtoInclude derived classes
    List<ProtoMemberAttribute> ProtoMembers,
    bool HasParameterlessConstructor, // True if type has a parameterless constructor (explicit or implicit)
    Microsoft.CodeAnalysis.INamedTypeSymbol? TypeSymbol = null, // Type symbol for constructor matching (readonly struct support)
    string? BaseClass = null); // Base class full name for flat inheritance tracking (null if no base or base is System.Object)