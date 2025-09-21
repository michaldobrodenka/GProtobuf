using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator;

internal sealed class RefactoredObjectTree : IEnumerable<KeyValuePair<string, List<TypeDefinition>>>
{
    // namespace -> type definitions
    private readonly Dictionary<string, List<TypeDefinition>> _types = new();

    // type name -> base class name
    private readonly Dictionary<string, string> _baseClassesForTypes = new();

    public void AddType(string @namespace, TypeDefinition typeDefinition)
    {
        if (!_types.TryGetValue(@namespace, out var typeDefinitions))
        {
            typeDefinitions = [];
            _types[@namespace] = typeDefinitions;
        }

        typeDefinitions.Add(typeDefinition);

        foreach (var protoInclude in typeDefinition.ProtoIncludes)
        {
            if (!_baseClassesForTypes.ContainsKey(protoInclude.Type))
            {
                _baseClassesForTypes[protoInclude.Type] = typeDefinition.FullName;
            }
        }
    }

    public bool HasBaseClass(string type) => _baseClassesForTypes.ContainsKey(type);

    public bool TryGetBaseClassName(string currentType, out string baseClassName)
    {
        return _baseClassesForTypes.TryGetValue(currentType, out baseClassName);
    }

    public IReadOnlyList<string> GetDerivedClasses(string type)
    {
        return _baseClassesForTypes
            .Where(x => x.Value == type)
            .Select(x => x.Key)
            .ToList();
    }

    public IEnumerator<KeyValuePair<string, List<TypeDefinition>>> GetEnumerator() => _types.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}