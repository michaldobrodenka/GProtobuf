namespace GProtobuf.Generator.V2.Helpers
{
    /// <summary>
    /// Centralized helper for generating primitive type reading code.
    /// Eliminates duplication of switch statements across StreamReaderGenerator and VirtualMapEntryGenerator.
    /// </summary>
    internal static class PrimitiveTypeCodeGenerator
    {
        /// <summary>
        /// Gets the read expression for a primitive type (without assignment).
        /// Returns null if the type is not a recognized primitive.
        /// </summary>
        /// <param name="normalizedType">The normalized type name (e.g., "System.Int32")</param>
        /// <param name="readerVar">The reader variable name (e.g., "reader")</param>
        /// <param name="wireTypeVar">The wire type variable name (e.g., "wireType")</param>
        /// <returns>The read expression, or null if not a primitive type</returns>
        public static string GetReadExpression(string normalizedType, string readerVar, string wireTypeVar = null)
        {
            return normalizedType switch
            {
                "System.Int32" or "int" => $"{readerVar}.ReadVarInt32()",
                "System.UInt32" or "uint" => $"{readerVar}.ReadVarUInt32()",
                "System.Int64" or "long" => $"{readerVar}.ReadVarInt64()",
                "System.UInt64" or "ulong" => $"{readerVar}.ReadVarUInt64()",
                "System.Int16" or "short" => $"(short){readerVar}.ReadVarInt32()",
                "System.UInt16" or "ushort" => $"(ushort){readerVar}.ReadVarUInt32()",
                "System.Byte" or "byte" => $"(byte){readerVar}.ReadVarUInt32()",
                "System.SByte" or "sbyte" => $"(sbyte){readerVar}.ReadVarInt32()",
                "System.Char" or "char" => $"(char){readerVar}.ReadVarUInt32()",
                "System.Boolean" or "bool" => $"{readerVar}.ReadVarInt32() != 0",
                "System.Single" or "float" => $"{readerVar}.ReadFixedFloat()",
                "System.Double" or "double" => $"{readerVar}.ReadFixedDouble()",
                "System.String" or "string" => wireTypeVar != null
                    ? $"global::GProtobuf.Core.StreamReaders.ReadString(ref {readerVar}, {wireTypeVar})"
                    : $"global::GProtobuf.Core.StreamReaders.ReadString(ref {readerVar}, global::GProtobuf.Core.WireType.Len)",
                "System.Guid" => wireTypeVar != null
                    ? $"global::GProtobuf.Core.StreamReaders.ReadGuid(ref {readerVar}, {wireTypeVar})"
                    : $"global::GProtobuf.Core.StreamReaders.ReadGuid(ref {readerVar}, global::GProtobuf.Core.WireType.Len)",
                "System.DateTime" => wireTypeVar != null
                    ? $"global::GProtobuf.Core.StreamReaders.ReadDateTime(ref {readerVar}, {wireTypeVar})"
                    : $"global::GProtobuf.Core.StreamReaders.ReadDateTime(ref {readerVar}, global::GProtobuf.Core.WireType.Len)",
                "System.TimeSpan" => wireTypeVar != null
                    ? $"global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref {readerVar}, {wireTypeVar})"
                    : $"global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref {readerVar}, global::GProtobuf.Core.WireType.Len)",
                "System.Byte[]" => $"global::GProtobuf.Core.StreamReaders.ReadByteArray(ref {readerVar})",
                _ => null
            };
        }

        /// <summary>
        /// Gets the assignment statement for a primitive type.
        /// Returns null if the type is not a recognized primitive.
        /// </summary>
        public static string GetAssignmentStatement(string normalizedType, string varName, string readerVar, string wireTypeVar = null)
        {
            var expr = GetReadExpression(normalizedType, readerVar, wireTypeVar);
            return expr != null ? $"{varName} = {expr};" : null;
        }

        /// <summary>
        /// Gets the collection add statement for a primitive type.
        /// Returns null if the type is not a recognized primitive.
        /// </summary>
        public static string GetCollectionAddExpression(string normalizedType, string readerVar, string wireTypeVar = null)
        {
            var expr = GetReadExpression(normalizedType, readerVar, wireTypeVar);
            return expr;
        }

        /// <summary>
        /// Checks if a type is a packable primitive (can be packed in repeated fields).
        /// </summary>
        public static bool IsPackable(string normalizedType)
        {
            return normalizedType switch
            {
                // Varint types - packable
                "System.Int32" or "int" => true,
                "System.UInt32" or "uint" => true,
                "System.Int64" or "long" => true,
                "System.UInt64" or "ulong" => true,
                "System.Int16" or "short" => true,
                "System.UInt16" or "ushort" => true,
                "System.Byte" or "byte" => true,
                "System.SByte" or "sbyte" => true,
                "System.Char" or "char" => true,
                "System.Boolean" or "bool" => true,
                // Fixed types - packable
                "System.Single" or "float" => true,
                "System.Double" or "double" => true,
                // Length-delimited types - NOT packable
                "System.String" or "string" => false,
                "System.Guid" => false,
                "System.DateTime" => false,
                "System.TimeSpan" => false,
                "System.Byte[]" => false,
                _ => false
            };
        }

        /// <summary>
        /// Checks if a type is a fixed-width primitive (float, double).
        /// </summary>
        public static bool IsFixedWidth(string normalizedType)
        {
            return normalizedType switch
            {
                "System.Single" or "float" => true,
                "System.Double" or "double" => true,
                _ => false
            };
        }

        /// <summary>
        /// Checks if a type is a recognized primitive type.
        /// </summary>
        public static bool IsPrimitive(string normalizedType)
        {
            return GetReadExpression(normalizedType, "_", null) != null;
        }

        /// <summary>
        /// Checks if a type requires wire type parameter for reading.
        /// </summary>
        public static bool RequiresWireType(string normalizedType)
        {
            return normalizedType switch
            {
                "System.String" or "string" => true,
                "System.Guid" => true,
                "System.DateTime" => true,
                "System.TimeSpan" => true,
                _ => false
            };
        }

        /// <summary>
        /// Gets the default value expression for a primitive type.
        /// </summary>
        public static string GetDefaultValue(string normalizedType)
        {
            return normalizedType switch
            {
                "System.String" or "string" => "\"\"",
                "System.Byte[]" => "global::System.Array.Empty<byte>()",
                _ => "default"
            };
        }
    }
}
