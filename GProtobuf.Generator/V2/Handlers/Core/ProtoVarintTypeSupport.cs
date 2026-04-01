using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Provides code generation support for ProtoVarint types.
    /// ProtoVarint types are structs/classes that serialize as primitive varints
    /// instead of nested messages, using [ProtoVarint], [ProtoVarintConstructor],
    /// and [ProtoVarintValue] attributes.
    /// </summary>
    internal static class ProtoVarintTypeSupport
    {
        #region Read Generation

        /// <summary>
        /// Generates code to read a ProtoVarint field using ProtoMemberAttribute info.
        /// </summary>
        public static void GenerateRead(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string targetVar,
            string readerVar = "reader")
        {
            GenerateRead(sb, member.Type, member.ProtoVarintType, targetVar, readerVar);
        }

        /// <summary>
        /// Generates code to read a ProtoVarint field with explicit varint type.
        /// Reads the varint and constructs the ProtoVarint type using the marked constructor.
        /// </summary>
        public static void GenerateRead(
            StringBuilderWithIndent sb,
            string typeName,
            ProtoVarintType varintType,
            string targetVar,
            string readerVar = "reader")
        {
            string readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType);
            var globalTypeName = WireFormat.TypeMapping.GetGlobalGenericTypeName(typeName);
            sb.AppendIndentedLine($"{targetVar} = new {globalTypeName}({readerVar}.{readMethod}());");
        }

        #endregion

        #region Write Generation

        /// <summary>
        /// Generates code to write a ProtoVarint field using ProtoMemberAttribute info.
        /// </summary>
        public static void GenerateWrite(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string sourceVar,
            string writerVar = "writer")
        {
            string valueAccess = GetValueAccess(sourceVar, member.ProtoVarintValueMember, member.ProtoVarintValueIsProperty);
            string writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(member.ProtoVarintType);
            string castPrefix = PrimitiveTypeCodeGenerator.GetProtoVarintCastPrefix(member.ProtoVarintType);

            if (member.IsNullable)
            {
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                sb.StartNewBlock();
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                string nullableValueAccess = GetValueAccess($"{sourceVar}.Value", member.ProtoVarintValueMember, member.ProtoVarintValueIsProperty);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{nullableValueAccess});");
                sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{valueAccess});");
            }
            else
            {
                sb.AppendIndentedLine($"if ({valueAccess} != 0)");
                sb.StartNewBlock();
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{valueAccess});");
                sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates code to write a ProtoVarint field with explicit parameters (no tag).
        /// Used for collection elements where tag is written separately.
        /// </summary>
        public static void GenerateWriteValue(
            StringBuilderWithIndent sb,
            ProtoVarintType varintType,
            string valueMember,
            string sourceVar,
            string writerVar = "writer")
        {
            string writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(varintType);
            sb.AppendIndentedLine($"{writerVar}.{writeMethod}({sourceVar}.{valueMember});");
        }

        #endregion

        #region Size Calculation Generation

        /// <summary>
        /// Generates code to calculate size of a ProtoVarint field using ProtoMemberAttribute info.
        /// </summary>
        public static void GenerateSize(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string sourceVar,
            string calculatorVar = "calculator")
        {
            string valueAccess = GetValueAccess(sourceVar, member.ProtoVarintValueMember, member.ProtoVarintValueIsProperty);
            string sizeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(member.ProtoVarintType); // Size uses same method names as write
            string castPrefix = PrimitiveTypeCodeGenerator.GetProtoVarintCastPrefix(member.ProtoVarintType);

            if (member.IsNullable)
            {
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                sb.StartNewBlock();
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                string nullableValueAccess = GetValueAccess($"{sourceVar}.Value", member.ProtoVarintValueMember, member.ProtoVarintValueIsProperty);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{nullableValueAccess});");
                sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{valueAccess});");
            }
            else
            {
                sb.AppendIndentedLine($"if ({valueAccess} != 0)");
                sb.StartNewBlock();
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{valueAccess});");
                sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates code to calculate size of a ProtoVarint value with explicit parameters (no tag).
        /// Used for collection elements where tag size is calculated separately.
        /// </summary>
        public static void GenerateSizeValue(
            StringBuilderWithIndent sb,
            ProtoVarintType varintType,
            string valueMember,
            string sourceVar,
            string calculatorVar = "calculator")
        {
            string sizeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(varintType);
            sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({sourceVar}.{valueMember});");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the value accessor expression (method call or property access).
        /// </summary>
        private static string GetValueAccess(string sourceVar, string valueMember, bool isProperty)
        {
            return isProperty ? $"{sourceVar}.{valueMember}" : $"{sourceVar}.{valueMember}()";
        }

        #endregion
    }
}
