using System;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Centralized registry for managing type definitions, inheritance hierarchies, and namespace resolution.
    /// Popul ated during SerializerGenerator analysis phase, queried during code generation.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Rationale:</b></para>
    /// - Single source of truth for all type metadata during code generation
    /// - O(1) lookups by type name, namespace, or inheritance relationships
    /// - Separates two inheritance models: ProtoInclude (polymorphism) vs flat (field merging)
    ///
    /// <para><b>Inheritance Models:</b></para>
    /// 1. **ProtoInclude Inheritance** (polymorphism):
    ///    - Tracked via _parentOf/_childrenOf dictionaries
    ///    - Used for runtime type dispatch (deserializing abstract base types)
    ///    - Generates wrapper fields for derived types in wire format
    ///
    /// 2. **Flat Inheritance** (field aggregation):
    ///    - Tracked via _baseClassOf dictionary
    ///    - Used when class inherits from base WITHOUT [ProtoInclude]
    ///    - Merges ProtoMembers from base + derived (derived shadows base on same FieldId)
    ///
    /// <para><b>Thread Safety:</b></para>
    /// - NOT thread-safe during registration phase (SerializerGenerator is single-threaded)
    /// - Thread-safe during code generation (read-only after registration completes)
    ///
    /// <para><b>Namespace Resolution:</b></para>
    /// - Handles nested types (A.B.C.Parent.Nested)
    /// - Caches lookups for performance (O(1) after first query)
    /// - Falls back to TypeNameHelper for types not in registry
    /// </remarks>
    public class TypeRegistry
    {
        // Fast lookups by type identity
        private readonly Dictionary<string, TypeDefinition> _byFullName = new Dictionary<string, TypeDefinition>();
        private readonly Dictionary<string, List<TypeDefinition>> _byNamespace = new Dictionary<string, List<TypeDefinition>>();

        // ProtoInclude-based inheritance (polymorphism)
        private readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>(); // derivedType -> baseType
        private readonly Dictionary<string, HashSet<string>> _childrenOf = new Dictionary<string, HashSet<string>>(); // baseType -> derivedTypes

        // Enum detection
        private readonly HashSet<string> _enumTypes = new HashSet<string>();

        // Namespace resolution cache
        private readonly Dictionary<string, string> _typeNamespaceCache = new Dictionary<string, string>();

        // Flat inheritance (field aggregation without ProtoInclude)
        private readonly Dictionary<string, string> _baseClassOf = new Dictionary<string, string>(); // derivedType -> baseType

        private static readonly string[] EmptyStringArray = Array.Empty<string>();
        private static readonly TypeDefinition[] EmptyTypeArray = Array.Empty<TypeDefinition>();

        #region Registration

        /// <summary>
        /// Registers a type definition with its namespace.
        /// Called by SerializerGenerator for each [ProtoContract] or [ProtoInclude] type discovered during analysis.
        /// </summary>
        /// <param name="namespace">Namespace where the type is declared (used for code generation).</param>
        /// <param name="type">Type metadata captured from Roslyn analysis.</param>
        /// <remarks>
        /// <para><b>Side Effects:</b></para>
        /// - Indexes type by FullName for O(1) lookup
        /// - Groups type by namespace for per-namespace code generation
        /// - Tracks ProtoInclude relationships (parent/children dictionaries)
        /// - Tracks flat inheritance (BaseClass without ProtoInclude)
        ///
        /// <para><b>Inheritance Tracking:</b></para>
        /// - If type.ProtoIncludes exists: Populates _parentOf/_childrenOf (polymorphism model)
        /// - If type.BaseClass exists BUT no ProtoInclude: Populates _baseClassOf (flat model)
        /// - ProtoInclude takes precedence over flat inheritance
        /// </remarks>
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

        /// <summary>
        /// Registers an enum type name for enum detection during code generation.
        /// Called by SerializerGenerator for each enum type discovered during Roslyn analysis.
        /// </summary>
        /// <param name="enumTypeName">Fully qualified enum type name.</param>
        /// <remarks>
        /// Used by code generators to distinguish enum fields from other types.
        /// Enums are serialized as varint-encoded underlying values (not nested messages).
        /// </remarks>
        public void RegisterEnum(string enumTypeName)
        {
            _enumTypes.Add(enumTypeName);
        }

        #endregion

        #region Lookups

        /// <summary>
        /// Retrieves type definition by fully qualified name.
        /// Returns null if type not registered.
        /// </summary>
        /// <param name="fullName">Fully qualified type name (e.g., "MyNamespace.MyClass").</param>
        /// <returns>TypeDefinition if found, null otherwise. O(1) lookup.</returns>
        public TypeDefinition GetByFullName(string fullName)
        {
            return _byFullName.TryGetValue(fullName, out var type) ? type : null;
        }

        /// <summary>
        /// Retrieves all types registered in the specified namespace.
        /// Used by ObjectTreeV2 for per-namespace code file generation.
        /// </summary>
        /// <param name="ns">Namespace name (e.g., "MyNamespace").</param>
        /// <returns>Collection of types in namespace, or empty collection if none. O(1) lookup.</returns>
        public IEnumerable<TypeDefinition> GetByNamespace(string ns)
        {
            return _byNamespace.TryGetValue(ns, out var list) ? list : EmptyTypeArray;
        }

        /// <summary>
        /// Gets all registered namespaces (used to generate one .cs file per namespace).
        /// </summary>
        /// <returns>Collection of namespace names.</returns>
        public IEnumerable<string> GetAllNamespaces() => _byNamespace.Keys;

        /// <summary>
        /// Gets all registered type definitions (used for cross-type analysis during code generation).
        /// </summary>
        /// <returns>Collection of all TypeDefinitions.</returns>
        public IEnumerable<TypeDefinition> GetAllTypes() => _byFullName.Values;

        /// <summary>
        /// Checks if a type is an enum.
        /// Used by code generators to determine serialization strategy (varint vs nested message).
        /// </summary>
        /// <param name="typeName">Fully qualified type name.</param>
        /// <returns>True if type is enum, false otherwise. O(1) lookup.</returns>
        public bool IsEnum(string typeName)
        {
            return _enumTypes.Contains(typeName);
        }

        /// <summary>
        /// Checks if a type is a readonly struct (struct with readonly fields or readonly struct modifier).
        /// Used by code generators to determine if Populate method is usable or if ReadContent is required.
        /// </summary>
        /// <param name="typeName">Fully qualified type name.</param>
        /// <returns>True if type is readonly struct, false otherwise.</returns>
        /// <remarks>
        /// For readonly structs, the Populate method is a no-op because fields cannot be modified after construction.
        /// Code generators should use Read{ClassName}Content instead of Populate{ClassName} for readonly structs.
        /// </remarks>
        public bool IsReadonlyStruct(string typeName)
        {
            var type = GetByFullName(typeName);
            if (type == null || !type.IsStruct)
                return false;

            // Check if TypeSymbol indicates readonly struct
            if (type.TypeSymbol?.IsReadOnly == true)
                return true;

            // Fallback: Check if struct has readonly fields (for older code or when TypeSymbol.IsReadOnly is false)
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

        /// <summary>
        /// Gets all registered enum type names.
        /// </summary>
        /// <returns>Collection of fully qualified enum type names.</returns>
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

        /// <summary>
        /// Gets the direct parent type in ProtoInclude inheritance hierarchy.
        /// Returns null if type has no parent (is root type or not part of ProtoInclude hierarchy).
        /// </summary>
        /// <param name="typeName">Fully qualified derived type name.</param>
        /// <returns>Parent type name, or null if no parent. O(1) lookup.</returns>
        /// <remarks>
        /// Only tracks ProtoInclude-based inheritance (not flat inheritance).
        /// For flat inheritance, use GetFlatBaseClass().
        /// </remarks>
        public string GetParent(string typeName)
        {
            return _parentOf.TryGetValue(typeName, out var parent) ? parent : null;
        }

        /// <summary>
        /// Gets all direct children of a base type in ProtoInclude hierarchy.
        /// Returns empty collection if type has no children.
        /// </summary>
        /// <param name="typeName">Fully qualified base type name.</param>
        /// <returns>Collection of direct child type names. O(1) lookup.</returns>
        /// <remarks>
        /// Only returns DIRECT children (not transitive).
        /// For all descendants, use GetAllDerivedTypes().
        /// </remarks>
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

        /// <summary>
        /// Gets the root type of an inheritance hierarchy (type with no parent).
        /// Returns typeName itself if it has no parent.
        /// </summary>
        /// <param name="typeName">Type to find root for.</param>
        /// <returns>Root type name (top of inheritance chain). O(depth) traversal.</returns>
        /// <remarks>
        /// Example: For hierarchy Animal -> Dog -> Bulldog, returns "Animal" for all three types.
        /// </remarks>
        public string GetRootType(string typeName)
        {
            var current = typeName;
            while (_parentOf.TryGetValue(current, out var parent))
            {
                current = parent;
            }
            return current;
        }

        /// <summary>
        /// Checks if type is part of any ProtoInclude inheritance hierarchy (as base or derived).
        /// </summary>
        /// <param name="typeName">Type to check.</param>
        /// <returns>True if type is base (has children) or derived (has parent), false otherwise. O(1) lookup.</returns>
        public bool IsPartOfHierarchy(string typeName)
        {
            return IsDerivedType(typeName) || IsBaseType(typeName);
        }

        /// <summary>
        /// Checks if type is a derived type in ProtoInclude hierarchy (has a parent).
        /// </summary>
        /// <param name="typeName">Type to check.</param>
        /// <returns>True if type has parent via ProtoInclude, false otherwise. O(1) lookup.</returns>
        public bool IsDerivedType(string typeName)
        {
            return _parentOf.ContainsKey(typeName);
        }

        /// <summary>
        /// Checks if type is a base type in ProtoInclude hierarchy (has children).
        /// </summary>
        /// <param name="typeName">Type to check.</param>
        /// <returns>True if type has [ProtoInclude] derived types, false otherwise. O(1) lookup.</returns>
        public bool IsBaseType(string typeName)
        {
            return _childrenOf.TryGetValue(typeName, out var children) && children.Count > 0;
        }

        /// <summary>
        /// Gets the ProtoInclude field ID for a derived type as declared in its parent's [ProtoInclude] attribute.
        /// Returns null if type has no parent or parent doesn't declare ProtoInclude for this type.
        /// </summary>
        /// <param name="derivedTypeName">Derived type to get field ID for.</param>
        /// <returns>ProtoInclude field ID, or null if not found. O(1) parent lookup + O(n) ProtoIncludes scan.</returns>
        /// <remarks>
        /// Used to generate length-delimited wrapper tags for derived type content in wire format.
        /// Example: [ProtoInclude(10, typeof(Dog))] -> returns 10 for "Dog" type.
        /// </remarks>
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
    /// Represents a ProtoMember field resolved through flat inheritance chain.
    /// Used by GetMergedFields() to handle field shadowing (derived overrides base).
    /// </summary>
    /// <remarks>
    /// <para><b>Field Shadowing Logic:</b></para>
    /// If base and derived types both have [ProtoMember(1)], the DERIVED field wins.
    /// The base field is marked IsShadowed=true and excluded from serialization.
    ///
    /// <para><b>Usage in Code Generation:</b></para>
    /// Generated deserializers read fields in FieldId order, using the DeclaringType to determine
    /// which property to assign (handles case where field is declared in base but accessed via derived).
    /// </remarks>
    public class MergedFieldInfo
    {
        /// <summary>
        /// Field ID from [ProtoMember(fieldId)] attribute.
        /// </summary>
        public int FieldId { get; set; }

        /// <summary>
        /// Full ProtoMember metadata (type, name, collection info, etc.).
        /// </summary>
        public ProtoMemberAttribute Field { get; set; }

        /// <summary>
        /// Fully qualified name of the type that declares this field.
        /// May differ from type being deserialized if field is inherited.
        /// </summary>
        public string DeclaringType { get; set; }

        /// <summary>
        /// True if field was overridden by derived type with same FieldId (field shadowing).
        /// Shadowed fields are excluded from GetMergedFields() result.
        /// </summary>
        public bool IsShadowed { get; set; }
    }
}
