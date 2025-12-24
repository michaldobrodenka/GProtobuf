namespace GProtobuf.Generator.V2.Helpers
{
    /// <summary>
    /// Common helper methods shared across code generators.
    /// </summary>
    internal static class GeneratorHelpers
    {
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
        /// Checks if a type has inheritance (either as base type with ProtoIncludes or as derived type).
        /// </summary>
        /// <param name="type">The type definition to check</param>
        /// <param name="registry">The type registry for checking derived types</param>
        /// <returns>True if the type has inheritance, false otherwise</returns>
        public static bool HasInheritance(TypeDefinition type, TypeRegistry registry)
        {
            return (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
                   || registry.IsDerivedType(type.FullName);
        }
    }
}
