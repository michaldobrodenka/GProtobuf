using System;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Registry for managing type definitions and their inheritance relationships.
    /// </summary>
    public class TypeRegistry
    {
        private readonly Dictionary<string, TypeDefinition> _byFullName = new Dictionary<string, TypeDefinition>();
        private readonly Dictionary<string, List<TypeDefinition>> _byNamespace = new Dictionary<string, List<TypeDefinition>>();
        private readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>(); // ProtoInclude-based inheritance
        private readonly Dictionary<string, HashSet<string>> _childrenOf = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _enumTypes = new HashSet<string>();
        private readonly Dictionary<string, string> _typeNamespaceCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _baseClassOf = new Dictionary<string, string>(); // Flat inheritance (без ProtoInclude)

        private static readonly string[] EmptyStringArray = Array.Empty<string>();
        private static readonly TypeDefinition[] EmptyTypeArray = Array.Empty<TypeDefinition>();

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

            // Track ProtoInclude-based inheritance
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

            // Track flat inheritance (without ProtoInclude)
            if (!string.IsNullOrEmpty(type.BaseClass))
            {
                // Only track if NOT already tracked via ProtoInclude
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

        public IReadOnlyCollection<string> GetAllEnums() => _enumTypes;

        /// <summary>
        /// Gets the namespace where a type was registered.
        /// For nested types (e.g., Parent.Nested), returns the namespace of the parent type.
        /// </summary>
        public string GetNamespaceForType(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return string.Empty;

            // Try direct lookup first (fast path for registered types)
            if (_typeNamespaceCache.TryGetValue(fullTypeName, out var cachedNs))
                return cachedNs;

            // For nested types, strip parts until we find a registered type
            // Example: A.B.C.Parent.NestedType -> try A.B.C.Parent, then A.B.C, etc.
            string candidate = fullTypeName;
            while (true)
            {
                int lastDot = candidate.LastIndexOf('.');
                if (lastDot < 0)
                    break;

                candidate = candidate.Substring(0, lastDot);

                if (_typeNamespaceCache.TryGetValue(candidate, out var ns))
                {
                    // Cache result for future lookups
                    _typeNamespaceCache[fullTypeName] = ns;
                    return ns;
                }

                // Check if this candidate is a known namespace (has registered types in it)
                // This handles nested types in non-ProtoContract classes
                // Example: GProtobuf.Tests.CustomCollectionTests.TestStatus
                //   -> candidate becomes GProtobuf.Tests.CustomCollectionTests
                //   -> not in cache, but we check if GProtobuf.Tests namespace exists
                //   -> next iteration candidate becomes GProtobuf.Tests
                //   -> we find types in this namespace, return it
                if (_byNamespace.ContainsKey(candidate))
                {
                    // Found a registered namespace - this is the namespace for the type
                    _typeNamespaceCache[fullTypeName] = candidate;
                    return candidate;
                }
            }

            // Fallback: use TypeNameHelper (for types not in registry)
            var fallbackNs = Helpers.TypeNameHelper.GetNamespace(fullTypeName);
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

        /// <summary>
        /// Gets ALL transitive derived types (direct and indirect children).
        /// Example: For A with B:A and C:B, returns [B, C] (not just [B]).
        /// </summary>
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
                // Recursively collect children of this child
                CollectAllDerivedTypes(child, result);
            }
        }

        /// <summary>
        /// Gets ProtoMembers defined ONLY at this type level (not inherited from base).
        /// For derived types, filters out fields that belong to the parent type.
        /// For root types, returns all ProtoMembers.
        ///
        /// IMPORTANT: Filters by Property NAME, not FieldId, because protobuf-net allows
        /// field shadowing (derived type can have same FieldId as base type for different property).
        /// </summary>
        /// <param name="fullTypeName">The full type name to get own members for</param>
        /// <returns>List of ProtoMembers defined at this type level only</returns>
        public IReadOnlyList<ProtoMemberAttribute> GetOwnProtoMembers(string fullTypeName)
        {
            var type = GetByFullName(fullTypeName);
            if (type?.ProtoMembers == null || type.ProtoMembers.Count == 0)
                return EmptyProtoMemberArray;

            // If type has no parent, all members are "own"
            var parentTypeName = GetParent(fullTypeName);
            if (parentTypeName == null)
                return type.ProtoMembers;

            // Get parent type's member names (not FieldIds!)
            var parentType = GetByFullName(parentTypeName);
            if (parentType?.ProtoMembers == null || parentType.ProtoMembers.Count == 0)
                return type.ProtoMembers;

            // Build set of parent member names for fast lookup
            var parentMemberNames = new HashSet<string>();
            foreach (var parentMember in parentType.ProtoMembers)
            {
                parentMemberNames.Add(parentMember.Name);
            }

            // Filter out inherited fields (by property name, not FieldId)
            var ownMembers = new List<ProtoMemberAttribute>(type.ProtoMembers.Count);
            foreach (var member in type.ProtoMembers)
            {
                // If parent doesn't have a member with this NAME, it's an own field
                if (!parentMemberNames.Contains(member.Name))
                {
                    ownMembers.Add(member);
                }
            }

            return ownMembers;
        }

        private static readonly ProtoMemberAttribute[] EmptyProtoMemberArray = Array.Empty<ProtoMemberAttribute>();

        #endregion

        #region Nested Derived Type Detection (for ProtoInclude wrapper generation in nested fields)

        /// <summary>
        /// Checks if a ProtoMember field is declared as a concrete derived type.
        /// Example: ModbusManualTransaction Transaction (where ModbusManualTransaction : ModbusTransaction)
        /// Returns true if the field type is a derived type (has a parent in inheritance chain).
        /// Used to determine if ProtoInclude wrapper is needed when serializing nested fields.
        /// </summary>
        public bool IsConcreteNestedDerivedType(ProtoMemberAttribute member)
        {
            if (member == null || string.IsNullOrEmpty(member.Type))
                return false;

            // Skip enums - they cannot be derived types
            if (IsEnum(member.Type))
                return false;

            // Get the field's type definition
            var fieldType = GetByFullName(member.Type);
            if (fieldType == null)
                return false;

            // Skip if type is an enum (double check)
            if (fieldType.IsEnum)
                return false;

            // Check if this type is a derived type (has a parent with ProtoInclude)
            return IsDerivedType(member.Type);
        }

        /// <summary>
        /// Checks if a ProtoMember field is declared as a polymorphic base type.
        /// Example: IntegromatResponse Response (where IntegromatResponse has ProtoIncludes)
        /// Returns true if the field type is a base type with known derived types.
        /// Used to determine if runtime type dispatch is needed when serializing nested fields.
        /// </summary>
        public bool IsPolymorphicField(ProtoMemberAttribute member)
        {
            if (member == null || string.IsNullOrEmpty(member.Type))
                return false;

            // Get the field's type definition
            var fieldType = GetByFullName(member.Type);
            if (fieldType == null)
                return false;

            // Check if this type has ProtoIncludes (is a polymorphic base)
            return fieldType.ProtoIncludes != null && fieldType.ProtoIncludes.Count > 0;
        }

        /// <summary>
        /// Gets ProtoInclude field ID for a derived type in its parent.
        /// Returns null if type is not derived or parent has no ProtoInclude for it.
        /// </summary>
        public int? GetProtoIncludeFieldIdForType(string derivedTypeName)
        {
            return GetProtoIncludeFieldId(derivedTypeName);
        }

        /// <summary>
        /// Gets the full inheritance chain with ProtoInclude field IDs.
        /// Returns list of (TypeName, ProtoIncludeFieldId) tuples from root to derived.
        /// ProtoIncludeFieldId is null for root type.
        /// </summary>
        public List<(string TypeName, int? ProtoIncludeFieldId)> GetInheritanceChainWithFieldIds(string typeName)
        {
            var chain = GetInheritanceChain(typeName);
            var result = new List<(string, int?)>(chain.Count);

            for (int i = 0; i < chain.Count; i++)
            {
                var currentTypeName = chain[i];
                int? fieldId = null;

                // Get ProtoInclude field ID from parent (if not root)
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

        #region Flat Inheritance (без ProtoInclude)

        /// <summary>
        /// Gets the base class for a type (flat inheritance, not ProtoInclude-based).
        /// Returns null if no base class or base is System.Object.
        /// </summary>
        public string GetFlatBaseClass(string typeName)
        {
            return _baseClassOf.TryGetValue(typeName, out var baseClass) ? baseClass : null;
        }

        /// <summary>
        /// Checks if a type has flat inheritance (has BaseClass without ProtoInclude).
        /// </summary>
        public bool HasFlatInheritance(string typeName)
        {
            return _baseClassOf.ContainsKey(typeName);
        }

        /// <summary>
        /// Gets the flat inheritance chain from root to type. Example: [BaseClass, DerivedClass]
        /// This includes ONLY flat inheritance (без ProtoInclude).
        /// </summary>
        public IReadOnlyList<string> GetFlatInheritanceChain(string typeName)
        {
            var chain = new List<string>();
            var current = typeName;

            // Build chain from derived to root
            while (current != null)
            {
                chain.Insert(0, current);
                current = GetFlatBaseClass(current);
            }

            return chain;
        }

        /// <summary>
        /// Gets merged ProtoMembers from base + derived classes with field shadowing logic.
        /// If derived field has same FieldId as base field, derived field WINS (shadows base).
        /// Returns list ordered by FieldId.
        /// </summary>
        public IReadOnlyList<MergedFieldInfo> GetMergedFields(string typeName)
        {
            var chain = GetFlatInheritanceChain(typeName);
            var fieldMap = new Dictionary<int, MergedFieldInfo>();

            // Process from ROOT to DERIVED (so derived fields override base fields)
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

                    // If field with same FieldId already exists, mark OLD one as shadowed
                    if (fieldMap.TryGetValue(member.FieldId, out var existing))
                    {
                        existing.IsShadowed = true;
                    }

                    // Override with new field (derived wins)
                    fieldMap[member.FieldId] = fieldInfo;
                }
            }

            // Return only non-shadowed fields, sorted by FieldId
            return fieldMap.Values
                .Where(f => !f.IsShadowed)
                .OrderBy(f => f.FieldId)
                .ToList();
        }

        #endregion
    }

    /// <summary>
    /// Information about a merged field from inheritance chain.
    /// </summary>
    public class MergedFieldInfo
    {
        public int FieldId { get; set; }
        public ProtoMemberAttribute Field { get; set; }
        public string DeclaringType { get; set; }  // Type that declares this field
        public bool IsShadowed { get; set; }  // True if hidden by derived type field
    }
}
