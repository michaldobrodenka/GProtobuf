using System;
using System.Collections.Generic;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Registry for managing type definitions and their inheritance relationships.
    /// </summary>
    public class TypeRegistry
    {
        private readonly Dictionary<string, TypeDefinition> _byFullName = new Dictionary<string, TypeDefinition>();
        private readonly Dictionary<string, List<TypeDefinition>> _byNamespace = new Dictionary<string, List<TypeDefinition>>();
        private readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>();
        private readonly Dictionary<string, HashSet<string>> _childrenOf = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _enumTypes = new HashSet<string>();

        private static readonly string[] EmptyStringArray = Array.Empty<string>();
        private static readonly TypeDefinition[] EmptyTypeArray = Array.Empty<TypeDefinition>();

        #region Registration

        public void Register(string @namespace, TypeDefinition type)
        {
            _byFullName[type.FullName] = type;

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

        public IReadOnlyCollection<string> GetAllEnums() => _enumTypes;

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

        /// <summary>
        /// Gets the inheritance chain from root to type. Example: [GrandParent, Parent, Child]
        /// </summary>
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

        #endregion
    }
}
