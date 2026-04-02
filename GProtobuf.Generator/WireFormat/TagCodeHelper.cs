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
            else if (byteCount == 2)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteTwoBytes({bytesString});");
            }
            else
            {
                const int WireTypeBitWidth = 3;
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
            EmitAddByteLength(sb, byteCount, calculatorVar);
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
            EmitAddByteLength(sb, totalSize, calculatorVar);
            return true;
        }

        /// <summary>
        /// Emits the optimal write call for a pre-computed tag value (known at generator time).
        /// Uses WriteSingleByte for tags &lt;= 127, WriteTwoBytes for 2-byte tags, WriteVarUInt32 for larger.
        /// </summary>
        public static void WriteTagValue(StringBuilderWithIndent sb, int tagValue, string writerVar = "writer")
        {
            if (tagValue < 0x80)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteSingleByte(0x{tagValue:X2});");
            }
            else if (tagValue < 0x4000)
            {
                byte b0 = (byte)((tagValue & 0x7F) | 0x80);
                byte b1 = (byte)(tagValue >> 7);
                sb.AppendIndentedLine($"{writerVar}.WriteTwoBytes(0x{b0:X2}, 0x{b1:X2});");
            }
            else
            {
                sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32({tagValue}u);");
            }
        }

        /// <summary>
        /// Emits the optimal AddByteLength call based on the constant value.
        /// </summary>
        public static void EmitAddByteLength(StringBuilderWithIndent sb, int byteCount, string calculatorVar = "calculator")
        {
            switch (byteCount)
            {
                case 1:
                    sb.AppendIndentedLine($"{calculatorVar}.AddByte();");
                    break;
                case 2:
                    sb.AppendIndentedLine($"{calculatorVar}.AddBytes2();");
                    break;
                case 3:
                    sb.AppendIndentedLine($"{calculatorVar}.AddBytes3();");
                    break;
                case 9:
                    sb.AppendIndentedLine($"{calculatorVar}.AddBytes9();");
                    break;
                case 10:
                    sb.AppendIndentedLine($"{calculatorVar}.AddBytes10();");
                    break;
                default:
                    sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({byteCount});");
                    break;
            }
        }
    }
}
