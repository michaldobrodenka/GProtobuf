using System;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration.Core
{
    /// <summary>
    /// Helper for generating enum field serialization code.
    /// Eliminates duplication between SizeCalculatorGenerator, StreamWriterGenerator, etc.
    /// </summary>
    internal static class EnumFieldHelper
    {
        /// <summary>
        /// Generates enum field handling code with nullable/required/default branching.
        /// </summary>
        /// <param name="sb">StringBuilder for output</param>
        /// <param name="member">The proto member attribute</param>
        /// <param name="sourceVar">Source variable name (e.g., "obj.EnumField")</param>
        /// <param name="writeTag">Action to write the tag (uses TagCodeHelper.AddTagSize or WriteTag)</param>
        /// <param name="writeValue">Action to write the value. Parameters: (valueExpression, isNullableValue)</param>
        public static void GenerateEnumField(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string sourceVar,
            Action writeTag,
            Action<string, bool> writeValue)
        {
            if (member.IsNullable)
            {
                // Nullable enum: always use HasValue check (IsRequired ignored)
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                sb.StartNewBlock();
                writeTag();
                writeValue($"{sourceVar}.Value", true);
                sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Non-nullable enum + IsRequired: ALWAYS serialize (no condition)
                writeTag();
                writeValue(sourceVar, false);
            }
            else
            {
                // Non-nullable enum + NOT required: proto2 default value check
                sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                sb.StartNewBlock();
                writeTag();
                writeValue(sourceVar, false);
                sb.EndBlock();
            }
        }
    }
}
