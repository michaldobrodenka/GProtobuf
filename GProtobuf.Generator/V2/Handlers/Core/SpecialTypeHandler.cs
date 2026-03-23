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
        public static bool TryGenerateRead(StringBuilderWithIndent sb, string targetVar, string typeName, string readerVar = "reader", bool useStringPooling = false)
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            switch (normalized)
            {
                case "System.String":
                    if (useStringPooling)
                    {
                        // Use StringPool for deduplication (60-85% allocation reduction)
                        sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadStringPooled();");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.SpanReaders.ReadString(ref {readerVar}, global::GProtobuf.Core.WireType.Len);");
                    }
                    return true;

                case "System.Guid":
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadGuid(global::GProtobuf.Core.WireType.Len);");
                    return true;

                case "System.TimeSpan":
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadTimeSpan(global::GProtobuf.Core.WireType.Len);"); // TimeSpan is sub-message (Level200)
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
                    // Guid BCL format: 1 byte length + 18 bytes nested message (1 tag + 8 lo + 1 tag + 8 hi) = 19 bytes total
                    sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(19);");
                    return true;

                default:
                    return false;
            }
        }

        #endregion
    }
}
