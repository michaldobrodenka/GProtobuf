using GProtobuf.Generator.V2.CodeGeneration;

namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Centralized tag generation for serialization code.
    /// Handles both single-byte and multi-byte tag writing.
    /// </summary>
    internal static class TagCodeHelper
    {
        /// <summary>
        /// Generates code to write a tag with optimal method (single byte or span).
        /// </summary>
        public static void WriteTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string writerVar = "writer")
        {
            var (bytesString, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            if (byteCount == 1)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteSingleByte({bytesString});");
            }
            else
            {
                // Generate inline WriteVarUInt32 for multi-byte tags
                // This avoids dependency on Tags constants which may not be generated for all fields
                // Tag encoding formula: (fieldId << 3) | wireType
                // This matches WireFormatHelpers.EncodeTag implementation
                const int WireTypeBitWidth = 3; // Same as ProtobufConstants.WireTypeBitWidth
                var tagValue = (fieldId << WireTypeBitWidth) | (int)wireType;
                sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32({tagValue}u);");
            }
        }

        /// <summary>
        /// Generates code to write a single-byte tag (for field IDs 1-15).
        /// Use when you know the tag is always single byte (like map entry fields 1, 2).
        /// </summary>
        public static void WriteSingleByteTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string writerVar = "writer")
        {
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            sb.AppendIndentedLine($"{writerVar}.WriteSingleByte({bytesString});");
        }

        /// <summary>
        /// Generates code to add tag size to a calculator.
        /// </summary>
        public static void AddTagSize(StringBuilderWithIndent sb, int fieldId, WireType wireType, string calculatorVar = "calculator")
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({byteCount});");
        }

        /// <summary>
        /// Gets the precomputed tag byte count for size calculations.
        /// </summary>
        public static int GetTagByteCount(int fieldId, WireType wireType)
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            return byteCount;
        }
    }
}
