namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Handles code generation for special types (String, Guid) that require custom serialization logic.
    /// </summary>
    internal static class SpecialTypeHandler
    {
        #region Read Generation

        /// <summary>
        /// Tries to generate read code for special types (String, Guid).
        /// Returns true if the type was handled, false otherwise.
        /// </summary>
        public static bool TryGenerateRead(StringBuilderWithIndent sb, string targetVar, string typeName, string readerVar = "reader")
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            switch (normalized)
            {
                case "System.String":
                    sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.SpanReaders.ReadString(ref {readerVar}, global::GProtobuf.Core.WireType.Len);");
                    return true;

                case "System.Guid":
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadGuid();");
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Write Generation

        /// <summary>
        /// Tries to generate write code for special types (String, Guid).
        /// Returns true if the type was handled, false otherwise.
        /// </summary>
        public static bool TryGenerateWrite(StringBuilderWithIndent sb, string sourceVar, string typeName, string writerVar = "writer")
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            switch (normalized)
            {
                case "System.String":
                    sb.AppendIndentedLine($"{writerVar}.WriteString({sourceVar});");
                    return true;

                case "System.Guid":
                    sb.AppendIndentedLine($"{writerVar}.WriteGuid({sourceVar});");
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Size Generation

        /// <summary>
        /// Tries to generate size calculation code for special types (String, Guid).
        /// Returns true if the type was handled, false otherwise.
        /// </summary>
        public static bool TryGenerateSize(StringBuilderWithIndent sb, string sourceVar, string typeName, string calculatorVar = "calculator")
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            switch (normalized)
            {
                case "System.String":
                    sb.AppendIndentedLine($"{calculatorVar}.WriteString({sourceVar});");
                    return true;

                case "System.Guid":
                    // Guid is 16 bytes + 2 bytes for length prefix
                    sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(18);");
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Type Checks

        /// <summary>
        /// Checks if a type is a special type that requires custom handling.
        /// </summary>
        public static bool IsSpecialType(string typeName)
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            return normalized == "System.String" || normalized == "System.Guid";
        }

        /// <summary>
        /// Gets the wire type for special types.
        /// </summary>
        public static WireType GetWireType(string typeName)
        {
            // Both String and Guid use length-delimited wire type
            return WireType.Len;
        }

        #endregion
    }
}
