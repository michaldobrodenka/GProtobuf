using System;
using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.Utilities;

namespace GProtobuf.Generator.Analysis
{
    /// <summary>
    /// Centralized registry for managing type definitions, inheritance hierarchies, and namespace resolution.
    /// Populated during SerializerGenerator analysis phase, queried during code generation.
    /// </summary>
    public class TypeRegistry
    {
        // Fast lookups by type identity
        private readonly Dictionary<string, TypeDefinition> _byFullName = new Dictionary<string, TypeDefinition>();
        private readonly Dictionary<string, List<TypeDefinition>> _byNamespace = new Dictionary<string, List<TypeDefinition>>();

        // ProtoInclude-based inheritance (polymorphism)
        private readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>();
        private readonly Dictionary<string, HashSet<string>> _childrenOf = new Dictionary<string, HashSet<string>>();

        // Enum detection
        private readonly HashSet<string> _enumTypes = new HashSet<string>();

        // Namespace resolution cache
        private readonly Dictionary<string, string> _typeNamespaceCache = new Dictionary<string, string>();

        // Flat inheritance (field aggregation without ProtoInclude)
        private readonly Dictionary<string, string> _baseClassOf = new Dictionary<string, string>();

        private static readonly string[] EmptyStringArray = Array.Empty<string>();
        private static readonly TypeDefinition[] EmptyTypeArray = Array.Empty<TypeDefinition>();
        private static readonly ProtoMemberAttribute[] EmptyProtoMemberArray = Array.Empty<ProtoMemberAttribute>();

        #region Registration

        public void Register(string @namespace, TypeDefinition type)
        {
            _byFullName[type.FullName] = type;
            _typeNamespaceCache[type.FullName] = @namespace;

            if (!_byNamespace.TryGetValue(@namespace, out var list))
            {
                list = new List<TypeDefinition>();
                _byNamespace[@namespace] = list;
            }
            list.Add(type);

            if (type.ProtoIncludes != null)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    if (!_parentOf.ContainsKey(include.Type))
                    {
                        _parentOf[include.Type] = type.FullName;
                    }

                    if (!_childrenOf.TryGetValue(type.FullName, out var children))
                    {
                        children = new HashSet<string>();
                        _childrenOf[type.FullName] = children;
                    }
                    children.Add(include.Type);
                }
            }

            if (!string.IsNullOrEmpty(type.BaseClass))
            {
                if (!_parentOf.ContainsKey(type.FullName))
                {
                    _baseClassOf[type.FullName] = type.BaseClass;
                }
            }
        }

        public void RegisterEnum(string enumTypeName)
        {
            _enumTypes.Add(enumTypeName);
        }

        #endregion

        #region Lookups

        public TypeDefinition GetByFullName(string fullName)
        {
            return _byFullName.TryGetValue(fullName, out var type) ? type : null;
        }

        public IEnumerable<TypeDefinition> GetByNamespace(string ns)
        {
            return _byNamespace.TryGetValue(ns, out var list) ? list : EmptyTypeArray;
        }

        public IEnumerable<string> GetAllNamespaces() => _byNamespace.Keys;

        public IEnumerable<TypeDefinition> GetAllTypes() => _byFullName.Values;

        public bool IsEnum(string typeName)
        {
            return _enumTypes.Contains(typeName);
        }

        public bool IsReadonlyStruct(string typeName)
        {
            var type = GetByFullName(typeName);
            if (type == null || !type.IsStruct)
                return false;

            if (type.TypeSymbol?.IsReadOnly == true)
                return true;

            if (type.TypeSymbol != null && type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    var memberSymbol = type.TypeSymbol.GetMembers()
                        .FirstOrDefault(s => s.Name == member.Name &&
                            (s is Microsoft.CodeAnalysis.IFieldSymbol || s is Microsoft.CodeAnalysis.IPropertySymbol));

                    if (memberSymbol is Microsoft.CodeAnalysis.IFieldSymbol fs && fs.IsReadOnly)
                        return true;
                    if (memberSymbol is Microsoft.CodeAnalysis.IPropertySymbol ps && ps.SetMethod == null)
                        return true;
                }
            }

            return false;
        }

        public IReadOnlyCollection<string> GetAllEnums() => _enumTypes;

        public string GetNamespaceForType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return string.Empty;

            if (_typeNamespaceCache.TryGetValue(fullTypeName, out var cachedNs))
                return cachedNs;

            string candidate = fullTypeName;
            while (true)
            {
                int lastDot = candidate.LastIndexOf('.');
                if (lastDot < 0)
                    break;

                candidate = candidate.Substring(0, lastDot);

                if (_typeNamespaceCache.TryGetValue(candidate, out var ns))
                {
                    _typeNamespaceCache[fullTypeName] = ns;
                    return ns;
                }

                if (_byNamespace.ContainsKey(candidate))
                {
                    _typeNamespaceCache[fullTypeName] = candidate;
                    return candidate;
                }
            }

            var fallbackNs = TypeNameHelper.GetNamespace(fullTypeName);
            _typeNamespaceCache[fullTypeName] = fallbackNs;
            return fallbackNs;
        }

        #endregion

        #region Inheritance

        public string GetParent(string typeName)
        {
            return _parentOf.TryGetValue(typeName, out var parent) ? parent : null;
        }

        public IReadOnlyCollection<string> GetChildren(string typeName)
        {
            return _childrenOf.TryGetValue(typeName, out var children)
                ? children
                : EmptyStringArray;
        }

        public IReadOnlyList<string> GetInheritanceChain(string typeName)
        {
            int depth = 0;
            var current = typeName;
            while (current != null)
            {
                depth++;
                current = GetParent(current);
            }

            if (depth == 0)
                return EmptyStringArray;

            var chain = new string[depth];
            current = typeName;
            int index = depth - 1;

            while (current != null)
            {
                chain[index--] = current;
                current = GetParent(current);
            }

            return chain;
        }

        public string GetRootType(string typeName)
        {
            var current = typeName;
            while (_parentOf.TryGetValue(current, out var parent))
            {
                current = parent;
            }
            return current;
        }

        public bool IsPartOfHierarchy(string typeName)
        {
            return IsDerivedType(typeName) || IsBaseType(typeName);
        }

        public bool IsDerivedType(string typeName)
        {
            return _parentOf.ContainsKey(typeName);
        }

        public bool IsBaseType(string typeName)
        {
            return _childrenOf.TryGetValue(typeName, out var children) && children.Count > 0;
        }

        public int? GetProtoIncludeFieldId(string derivedTypeName)
        {
            var parent = GetParent(derivedTypeName);
            if (parent == null)
                return null;

            var parentType = GetByFullName(parent);
            if (parentType?.ProtoIncludes == null)
                return null;

            foreach (var include in parentType.ProtoIncludes)
            {
                if (include.Type == derivedTypeName)
                    return include.FieldId;
            }

            return null;
        }

        public List<string> GetAllDerivedTypes(string typeName)
        {
            var result = new List<string>();
            CollectAllDerivedTypes(typeName, result);
            return result;
        }

        private void CollectAllDerivedTypes(string typeName, List<string> result)
        {
            if (!_childrenOf.TryGetValue(typeName, out var children))
                return;

            foreach (var child in children)
            {
                result.Add(child);
                CollectAllDerivedTypes(child, result);
            }
        }

        public IReadOnlyList<ProtoMemberAttribute> GetOwnProtoMembers(string fullTypeName)
        {
            var type = GetByFullName(fullTypeName);
            if (type?.ProtoMembers == null || type.ProtoMembers.Count == 0)
                return EmptyProtoMemberArray;

            var parentTypeName = GetParent(fullTypeName);
            if (parentTypeName == null)
                return type.ProtoMembers;

            var parentType = GetByFullName(parentTypeName);
            if (parentType?.ProtoMembers == null || parentType.ProtoMembers.Count == 0)
                return type.ProtoMembers;

            var parentMemberNames = new HashSet<string>();
            foreach (var parentMember in parentType.ProtoMembers)
            {
                parentMemberNames.Add(parentMember.Name);
            }

            var ownMembers = new List<ProtoMemberAttribute>(type.ProtoMembers.Count);
            foreach (var member in type.ProtoMembers)
            {
                if (!parentMemberNames.Contains(member.Name))
                {
                    ownMembers.Add(member);
                }
            }

            return ownMembers;
        }

        #endregion

        #region Nested Derived Type Detection

        public bool IsConcreteNestedDerivedType(ProtoMemberAttribute member)
        {
            if (member == null || string.IsNullOrEmpty(member.Type))
                return false;

            if (IsEnum(member.Type))
                return false;

            var fieldType = GetByFullName(member.Type);
            if (fieldType == null)
                return false;

            if (fieldType.IsEnum)
                return false;

            return IsDerivedType(member.Type);
        }

        public bool IsPolymorphicField(ProtoMemberAttribute member)
        {
            if (member == null || string.IsNullOrEmpty(member.Type))
                return false;

            var fieldType = GetByFullName(member.Type);
            if (fieldType == null)
                return false;

            return fieldType.ProtoIncludes != null && fieldType.ProtoIncludes.Count > 0;
        }

        public int? GetProtoIncludeFieldIdForType(string derivedTypeName)
        {
            return GetProtoIncludeFieldId(derivedTypeName);
        }

        public List<(string TypeName, int? ProtoIncludeFieldId)> GetInheritanceChainWithFieldIds(string typeName)
        {
            var chain = GetInheritanceChain(typeName);
            var result = new List<(string, int?)>(chain.Count);

            for (int i = 0; i < chain.Count; i++)
            {
                var currentTypeName = chain[i];
                int? fieldId = null;

                if (i > 0)
                {
                    var parentTypeName = chain[i - 1];
                    var parentType = GetByFullName(parentTypeName);
                    if (parentType?.ProtoIncludes != null)
                    {
                        var protoInclude = parentType.ProtoIncludes
                            .FirstOrDefault(p => p.Type == currentTypeName);
                        fieldId = protoInclude?.FieldId;
                    }
                }

                result.Add((currentTypeName, fieldId));
            }

            return result;
        }

        #endregion

        #region Flat Inheritance

        public string GetFlatBaseClass(string typeName)
        {
            return _baseClassOf.TryGetValue(typeName, out var baseClass) ? baseClass : null;
        }

        public bool HasFlatInheritance(string typeName)
        {
            return _baseClassOf.ContainsKey(typeName);
        }

        public IReadOnlyList<string> GetFlatInheritanceChain(string typeName)
        {
            var chain = new List<string>();
            var current = typeName;

            while (current != null)
            {
                chain.Insert(0, current);
                current = GetFlatBaseClass(current);
            }

            return chain;
        }

        public IReadOnlyList<MergedFieldInfo> GetMergedFields(string typeName)
        {
            var chain = GetFlatInheritanceChain(typeName);
            var fieldMap = new Dictionary<int, MergedFieldInfo>();

            foreach (var typeNameInChain in chain)
            {
                var type = GetByFullName(typeNameInChain);
                if (type?.ProtoMembers == null)
                    continue;

                foreach (var member in type.ProtoMembers)
                {
                    var fieldInfo = new MergedFieldInfo
                    {
                        FieldId = member.FieldId,
                        Field = member,
                        DeclaringType = typeNameInChain,
                        IsShadowed = false
                    };

                    if (fieldMap.TryGetValue(member.FieldId, out var existing))
                    {
                        existing.IsShadowed = true;
                    }

                    fieldMap[member.FieldId] = fieldInfo;
                }
            }

            return fieldMap.Values
                .Where(f => !f.IsShadowed)
                .OrderBy(f => f.FieldId)
                .ToList();
        }

        #endregion
    }

    /// <summary>
    /// Represents a ProtoMember field resolved through flat inheritance chain.
    /// </summary>
    public class MergedFieldInfo
    {
        public int FieldId { get; set; }
        public ProtoMemberAttribute Field { get; set; }
        public string DeclaringType { get; set; }
        public bool IsShadowed { get; set; }
    }
}
