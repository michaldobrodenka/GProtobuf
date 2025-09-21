using System.Collections.Generic;

namespace GProtobuf.Generator;

public sealed record TypeDefinition(
    bool IsStruct,
    bool IsAbstract,
    string FullName,
    List<ProtoIncludeAttribute> ProtoIncludes, // List of ProtoInclude derived classes
    List<ProtoMemberAttribute> ProtoMembers);