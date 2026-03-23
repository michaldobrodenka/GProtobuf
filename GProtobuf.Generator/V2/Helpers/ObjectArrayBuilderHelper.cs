using System;
using System.Collections.Generic;
using GProtobuf.Generator.Analysis;
using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.WireFormat;

namespace GProtobuf.Generator.V2.Helpers
{
    /// <summary>
    /// Shared helper for ObjectArrayBuilder code generation decisions and code generation.
    /// Centralizes the logic for determining when to use ObjectArrayBuilder vs List&lt;T&gt;.
    /// </summary>
    internal static class ObjectArrayBuilderHelper
    {
        /// <summary>
        /// Initial capacity for ObjectArrayBuilder instances.
        /// </summary>
        internal const int InitialCapacity = 16;

        /// <summary>
        /// Determines if a collection member should use ObjectArrayBuilder instead of List&lt;T&gt;.
        /// ObjectArrayBuilder is used for reference types (classes) to leverage unified ArrayPool&lt;object&gt;.
        /// </summary>
        /// <param name="member">The proto member attribute describing the collection.</param>
        /// <param name="registry">The type registry for looking up type information.</param>
        /// <returns>True if ObjectArrayBuilder should be used, false for List&lt;T&gt;.</returns>
        internal static bool ShouldUseObjectArrayBuilder(ProtoMemberAttribute member, TypeRegistry registry)
        {
            if (member?.CollectionElementType == null)
                return false;

            return ShouldUseObjectArrayBuilder(member.CollectionElementType, registry);
        }

        /// <summary>
        /// Determines if a collection with the given element type should use ObjectArrayBuilder.
        /// </summary>
        /// <param name="elementTypeName">The fully qualified element type name.</param>
        /// <param name="registry">The type registry for looking up type information.</param>
        /// <returns>True if ObjectArrayBuilder should be used, false for List&lt;T&gt;.</returns>
        internal static bool ShouldUseObjectArrayBuilder(string elementTypeName, TypeRegistry registry)
        {
            if (string.IsNullOrEmpty(elementTypeName))
                return false;

            // Simple types (primitives, DateTime, Guid, etc.) - use List<T>
            if (TypeMapping.IsSimpleType(elementTypeName))
                return false;

            // Check if it's a known type in registry
            var typeDef = registry?.GetByFullName(elementTypeName);
            if (typeDef != null)
            {
                // Structs cannot use ObjectArrayBuilder (has constraint where T : class)
                if (typeDef.IsStruct)
                    return false;

                // Enums use primitive handling
                if (typeDef.IsEnum)
                    return false;

                // Classes - use ObjectArrayBuilder
                return true;
            }

            // Unknown types - default to false to be safe
            return false;
        }

        /// <summary>
        /// Determines if a collection member needs a class-level _tempList_ declaration.
        /// Returns true only for element types that are handled by CollectionHandler or TupleHandler
        /// (structs, tuples, DateTime arrays).
        /// Returns false for:
        /// - Classes (use ObjectArrayBuilder via _builder_)
        /// - Primitives (PrimitiveHandler uses local storage)
        /// - Enums (PrimitiveHandler uses local storage)
        /// </summary>
        /// <param name="member">The proto member attribute describing the collection.</param>
        /// <param name="registry">The type registry for looking up type information.</param>
        /// <returns>True if _tempList_ declaration is needed, false otherwise.</returns>
        internal static bool NeedsTempListDeclaration(ProtoMemberAttribute member, TypeRegistry registry)
        {
            if (member?.CollectionElementType == null)
                return false;

            // Classes use ObjectArrayBuilder, not _tempList_
            if (ShouldUseObjectArrayBuilder(member, registry))
                return false;

            // Primitives: PrimitiveHandler uses local storage (UnmanagedArrayBuilder or local tempList)
            // This includes: int, long, string, Guid, TimeSpan, byte, etc.
            if (TypeMapping.IsNonPackedArrayType(member.CollectionElementType))
                return false;

            // Enums: PrimitiveHandler uses local storage
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            if (registry?.IsEnum(member.CollectionElementType) == true ||
                registry?.IsEnum(normalizedType) == true)
                return false;

            // Remaining types need _tempList_: structs, tuples, DateTime, etc.
            return true;
        }

        /// <summary>
        /// Generates declaration code for ObjectArrayBuilder fields.
        /// </summary>
        /// <param name="sb">The string builder to write to.</param>
        /// <param name="members">The collection members using ObjectArrayBuilder.</param>
        /// <param name="getElementType">Function to get the element type for a member.</param>
        internal static void GenerateDeclarations(
            StringBuilderWithIndent sb,
            IReadOnlyList<ProtoMemberAttribute> members,
            Func<ProtoMemberAttribute, string> getElementType)
        {
            if (members == null || members.Count == 0)
                return;

            foreach (var member in members)
            {
                var elementType = getElementType(member);
                sb.AppendIndentedLine($"var _builder_{member.Name} = new global::GProtobuf.Core.ObjectArrayBuilder<{elementType}>({InitialCapacity});");
            }
        }

        /// <summary>
        /// Generates finalization code for List&lt;T&gt; temp list fields.
        /// Converts to array if needed and assigns to target property.
        /// </summary>
        /// <param name="sb">The string builder to write to.</param>
        /// <param name="members">The collection members using temp lists.</param>
        /// <param name="targetVar">The target variable name (e.g., "result" or "instance").</param>
        /// <param name="isArrayType">Optional function to check if member should use ToArray().
        /// If null, defaults to checking CollectionKind == Array.</param>
        internal static void GenerateTempListFinalization(
            StringBuilderWithIndent sb,
            IReadOnlyList<ProtoMemberAttribute> members,
            string targetVar,
            Func<ProtoMemberAttribute, bool> isArrayType = null)
        {
            if (members == null || members.Count == 0)
                return;

            sb.AppendNewLine();
            foreach (var member in members)
            {
                sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                sb.StartNewBlock();

                bool useToArray = isArrayType != null
                    ? isArrayType(member)
                    : member.CollectionKind == CollectionKind.Array;

                if (useToArray)
                {
                    sb.AppendIndentedLine($"{targetVar}.{member.Name} = _tempList_{member.Name}.ToArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"{targetVar}.{member.Name} = _tempList_{member.Name};");
                }

                sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates conversion code for ObjectArrayBuilder fields (inside try block).
        /// Converts collected items to array/list and assigns to target property.
        /// </summary>
        /// <param name="sb">The string builder to write to.</param>
        /// <param name="members">The collection members using ObjectArrayBuilder.</param>
        /// <param name="targetVar">The target variable name (e.g., "result" or "instance").</param>
        /// <param name="isArrayType">Optional function to check if member should use ToArray().
        /// If null, defaults to checking CollectionKind == Array.</param>
        internal static void GenerateConversion(
            StringBuilderWithIndent sb,
            IReadOnlyList<ProtoMemberAttribute> members,
            string targetVar,
            Func<ProtoMemberAttribute, bool> isArrayType = null)
        {
            if (members == null || members.Count == 0)
                return;

            sb.AppendNewLine();
            foreach (var member in members)
            {
                sb.AppendIndentedLine($"if (_builder_{member.Name}.Count > 0)");
                sb.StartNewBlock();

                bool useToArray = isArrayType != null
                    ? isArrayType(member)
                    : member.CollectionKind == CollectionKind.Array;

                if (useToArray)
                {
                    sb.AppendIndentedLine($"{targetVar}.{member.Name} = _builder_{member.Name}.ToArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"{targetVar}.{member.Name} = _builder_{member.Name}.ToList();");
                }

                sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates dispose code for ObjectArrayBuilder fields (inside finally block).
        /// </summary>
        /// <param name="sb">The string builder to write to.</param>
        /// <param name="members">The collection members using ObjectArrayBuilder.</param>
        internal static void GenerateDispose(
            StringBuilderWithIndent sb,
            IReadOnlyList<ProtoMemberAttribute> members)
        {
            if (members == null || members.Count == 0)
                return;

            foreach (var member in members)
            {
                sb.AppendIndentedLine($"_builder_{member.Name}.Dispose();");
            }
        }
    }
}
