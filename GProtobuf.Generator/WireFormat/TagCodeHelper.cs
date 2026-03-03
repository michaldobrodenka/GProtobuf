using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.Utilities;

namespace GProtobuf.Generator.WireFormat
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

        /// <summary>
        /// Generates code to add combined tag + fixed value size to calculator.
        /// Returns true if combined generation was used, false if caller should generate value size separately.
        /// </summary>
        public static bool TryAddCombinedTagAndValueSize(
            StringBuilderWithIndent sb,
            int fieldId,
            WireType wireType,
            string typeName,
            DataFormat format,
            string calculatorVar = "calculator")
        {
            var fixedValueSize = TypeMapping.GetFixedWireSize(typeName, format);
            if (fixedValueSize < 0)
            {
                // Variable size - just add tag, caller handles value
                AddTagSize(sb, fieldId, wireType, calculatorVar);
                return false;
            }

            // Both tag and value are fixed size - combine them
            var (_, tagByteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            var totalSize = tagByteCount + fixedValueSize;
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({totalSize});");
            return true;
        }
    }
}
