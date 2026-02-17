using System.Linq;
using GProtobuf.Generator;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration.Core
{
    /// <summary>
    /// Contains information about a nested derived type for code generation.
    /// </summary>
    internal class NestedDerivedTypeInfo
    {
        /// <summary>Full name of the parent type.</summary>
        public string ParentTypeFullName { get; set; }

        /// <summary>Simple class name of the parent type.</summary>
        public string ParentTypeName { get; set; }

        /// <summary>Parent type definition.</summary>
        public TypeDefinition ParentType { get; set; }

        /// <summary>ProtoInclude attribute linking parent to derived type.</summary>
        public ProtoIncludeAttribute ProtoInclude { get; set; }

        /// <summary>Wire format wrapper tag (fieldId << 3 | WireType.Len).</summary>
        public int WrapperTag { get; set; }

        /// <summary>Namespace of the parent type.</summary>
        public string ParentNamespace { get; set; }
    }

    /// <summary>
    /// Categories of fields for routing to appropriate handlers.
    /// </summary>
    internal enum FieldCategory
    {
        Map,
        Collection,
        Enum,
        Tuple,
        Primitive,
        ProtoVarint,
        Unsupported,
        ComplexType
    }

    /// <summary>
    /// Common helper methods shared across code generators.
    /// </summary>
    internal static class GeneratorHelpers
    {
        /// <summary>
        /// Gets all derived types for a base type, sorted by inheritance depth (most derived first).
        /// This is used for polymorphic field handling where runtime type dispatch is needed.
        /// </summary>
        /// <param name="baseTypeName">The full name of the base type</param>
        /// <param name="registry">The type registry</param>
        /// <returns>Sorted list of derived type names, or null if no derived types</returns>
        public static System.Collections.Generic.List<string> GetSortedDerivedTypes(string baseTypeName, TypeRegistry registry)
        {
            var allDerivedTypes = registry.GetAllDerivedTypes(baseTypeName);
            if (allDerivedTypes == null || allDerivedTypes.Count == 0)
                return null;

            return allDerivedTypes
                .OrderByDescending(d =>
                {
                    var chain = registry.GetInheritanceChain(d);
                    return chain?.Count ?? 0;
                })
                .ToList();
        }

        /// <summary>
        /// Tries to get information about a nested derived type for code generation.
        /// Returns null if the type is not a derived type with ProtoInclude.
        /// </summary>
        /// <param name="memberType">The full type name of the member</param>
        /// <param name="registry">The type registry</param>
        /// <returns>NestedDerivedTypeInfo if the type is a nested derived type, null otherwise</returns>
        public static NestedDerivedTypeInfo TryGetNestedDerivedTypeInfo(string memberType, TypeRegistry registry)
        {
            var parentTypeFullName = registry.GetParent(memberType);
            if (string.IsNullOrEmpty(parentTypeFullName))
                return null;

            var parentType = registry.GetByFullName(parentTypeFullName);
            if (parentType?.ProtoIncludes == null)
                return null;

            var protoInclude = parentType.ProtoIncludes.FirstOrDefault(p => p.Type == memberType);
            if (protoInclude == null)
                return null;

            return new NestedDerivedTypeInfo
            {
                ParentTypeFullName = parentTypeFullName,
                ParentTypeName = TypeNameHelper.GetClassName(parentTypeFullName),
                ParentType = parentType,
                ProtoInclude = protoInclude,
                WrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len,
                ParentNamespace = registry.GetNamespaceForType(parentTypeFullName)
            };
        }

        /// <summary>
        /// Determines the category of a field for routing to the appropriate handler.
        /// This consolidates the branching logic used in GenerateFieldSize, GenerateFieldWrite, and GenerateFieldReadCase.
        /// </summary>
        /// <param name="member">The proto member attribute</param>
        /// <param name="primitiveHandler">The primitive handler for type checking</param>
        /// <returns>The field category for routing</returns>
        public static FieldCategory GetFieldCategory(ProtoMemberAttribute member, PrimitiveHandler primitiveHandler)
        {
            if (member.IsMap)
                return FieldCategory.Map;

            if (member.IsCollection)
                return FieldCategory.Collection;

            if (member.IsEnum)
                return FieldCategory.Enum;

            if (TupleHandler.IsTupleType(member.Type))
                return FieldCategory.Tuple;

            if (primitiveHandler.CanHandle(member.Type))
                return FieldCategory.Primitive;

            if (member.IsProtoVarint)
                return FieldCategory.ProtoVarint;

            if (TypeMapping.IsUnsupportedType(member.Type))
                return FieldCategory.Unsupported;

            return FieldCategory.ComplexType;
        }

        /// <summary>
        /// Finds a ProtoInclude attribute for a specific derived type.
        /// </summary>
        /// <param name="type">The base type definition</param>
        /// <param name="derivedTypeName">The full name of the derived type to find</param>
        /// <returns>The ProtoInclude attribute if found, null otherwise</returns>
        public static ProtoIncludeAttribute FindProtoInclude(TypeDefinition type, string derivedTypeName)
        {
            if (type?.ProtoIncludes == null)
                return null;

            foreach (var include in type.ProtoIncludes)
            {
                if (include.Type == derivedTypeName)
                    return include;
            }
            return null;
        }

        /// <summary>
        /// Checks if a type has inheritance (ProtoInclude-based OR flat inheritance).
        /// Returns true if:
        /// - Type has ProtoIncludes (base type with derived types)
        /// - Type is derived via ProtoInclude (has parent)
        /// - Type has flat inheritance (has BaseClass without ProtoInclude)
        /// </summary>
        /// <param name="type">The type definition to check</param>
        /// <param name="registry">The type registry for checking derived types</param>
        /// <returns>True if the type has inheritance, false otherwise</returns>
        public static bool HasInheritance(TypeDefinition type, TypeRegistry registry)
        {
            return (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
                   || registry.IsDerivedType(type.FullName)
                   || registry.HasFlatInheritance(type.FullName);
        }

        /// <summary>
        /// Gets the correct value access for nullable types.
        /// For nullable enums or structs, returns sourceVar.Value, otherwise returns sourceVar as-is.
        /// </summary>
        /// <param name="sourceVar">The source variable name</param>
        /// <param name="member">The proto member attribute</param>
        /// <param name="typeDef">The type definition (can be null)</param>
        /// <param name="registry">The type registry for checking if type is enum</param>
        /// <returns>The value access expression</returns>
        public static string GetNullableValueAccess(string sourceVar, ProtoMemberAttribute member, TypeDefinition typeDef, TypeRegistry registry)
        {
            // If member.IsNullable = true in context of GenerateComplexType,
            // it means it's a nullable value type (struct or enum), not a reference type.
            // Reference types don't have nullable modifier in protobuf context.
            if (member.IsNullable)
            {
                // Check if we can confirm it's a value type
                bool isEnum = registry != null && registry.IsEnum(member.Type);
                bool isStruct = typeDef != null && typeDef.IsStruct;

                // If we know it's enum or struct, use .Value
                if (isEnum || isStruct)
                {
                    return $"{sourceVar}.Value";
                }

                // Fallback: if typeDef is null but member.IsNullable = true in complex type context,
                // assume it's a nullable struct and use .Value
                // This handles cases where typeDef is null for registered structs like DataType
                if (typeDef == null && !isEnum)
                {
                    return $"{sourceVar}.Value";
                }
            }

            return sourceVar;
        }

        /// <summary>
        /// Gets a qualified method call prefix for cross-namespace calls.
        /// Returns empty string if the type is in the current namespace.
        /// </summary>
        /// <param name="typeNamespace">The namespace of the type</param>
        /// <param name="currentNamespace">The current namespace</param>
        /// <param name="className">The class name (e.g., "SizeCalculators", "StreamWriters")</param>
        /// <returns>Qualified prefix like "global::Namespace.Serialization.ClassName." or "ClassName."</returns>
        public static string GetQualifiedMethodPrefix(string typeNamespace, string currentNamespace, string className)
        {
            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != currentNamespace)
            {
                return $"global::{typeNamespace}.Serialization.{className}.";
            }
            return $"{className}.";
        }

        /// <summary>
        /// Gets a qualified method call prefix for cross-namespace calls, without class name suffix.
        /// Returns empty string if the type is in the current namespace.
        /// </summary>
        /// <param name="typeNamespace">The namespace of the type</param>
        /// <param name="currentNamespace">The current namespace</param>
        /// <returns>Qualified prefix like "global::Namespace.Serialization." or empty string</returns>
        public static string GetNamespacePrefix(string typeNamespace, string currentNamespace)
        {
            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != currentNamespace)
            {
                return $"global::{typeNamespace}.Serialization.";
            }
            return "";
        }
    }
}
