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
                // Use static ReadOnlySpan from Tags class for zero-allocation
                var tagPropertyName = TagsGenerator.GetTagPropertyName(fieldId, wireType);
                sb.AppendIndentedLine($"{writerVar}.WriteBytes(Tags.{tagPropertyName});");
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
