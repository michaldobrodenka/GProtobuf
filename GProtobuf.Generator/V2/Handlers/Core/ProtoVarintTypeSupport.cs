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
        /// Generates code to read a ProtoVarint field.
        /// Reads the varint and constructs the ProtoVarint type using the marked constructor.
        /// </summary>
        public static void GenerateRead(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string targetVar,
            string readerVar = "reader")
        {
            // Determine the read method based on ProtoVarintType
            string readMethod = GetReadMethod(member.ProtoVarintType);

            // Construct the ProtoVarint type using the constructor
            // For structs, use: new TypeName(reader.ReadVarUInt32())
            sb.AppendIndentedLine($"{targetVar} = new global::{member.Type}({readerVar}.{readMethod}());");
        }

        #endregion

        #region Write Generation

        /// <summary>
        /// Generates code to write a ProtoVarint field.
        /// Extracts the value using the marked accessor and writes as varint.
        /// </summary>
        public static void GenerateWrite(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string sourceVar,
            string writerVar = "writer")
        {
            // Get the value accessor (method call or property access)
            string valueAccess = member.ProtoVarintValueIsProperty
                ? $"{sourceVar}.{member.ProtoVarintValueMember}"
                : $"{sourceVar}.{member.ProtoVarintValueMember}()";

            // Determine the write method based on ProtoVarintType
            string writeMethod = GetWriteMethod(member.ProtoVarintType);

            // Cast for signed types if needed
            string castPrefix = GetCastPrefix(member.ProtoVarintType);

            if (member.IsNullable)
            {
                // Nullable ProtoVarint
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                sb.StartNewBlock();
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{sourceVar}.Value.{member.ProtoVarintValueMember}{(member.ProtoVarintValueIsProperty ? "" : "()")});");
                sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Required ProtoVarint - always write
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{valueAccess});");
            }
            else
            {
                // Optional ProtoVarint - check for non-default value
                sb.AppendIndentedLine($"if ({valueAccess} != 0)");
                sb.StartNewBlock();
                TagCodeHelper.WriteTag(sb, member.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"{writerVar}.{writeMethod}({castPrefix}{valueAccess});");
                sb.EndBlock();
            }
        }

        #endregion

        #region Size Calculation Generation

        /// <summary>
        /// Generates code to calculate size of a ProtoVarint field.
        /// </summary>
        public static void GenerateSize(
            StringBuilderWithIndent sb,
            ProtoMemberAttribute member,
            string sourceVar,
            string calculatorVar = "calculator")
        {
            // Get the value accessor (method call or property access)
            string valueAccess = member.ProtoVarintValueIsProperty
                ? $"{sourceVar}.{member.ProtoVarintValueMember}"
                : $"{sourceVar}.{member.ProtoVarintValueMember}()";

            // Determine the size method based on ProtoVarintType
            string sizeMethod = GetSizeMethod(member.ProtoVarintType);

            // Cast for signed types if needed
            string castPrefix = GetCastPrefix(member.ProtoVarintType);

            if (member.IsNullable)
            {
                // Nullable ProtoVarint
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                sb.StartNewBlock();
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{sourceVar}.Value.{member.ProtoVarintValueMember}{(member.ProtoVarintValueIsProperty ? "" : "()")});");
                sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Required ProtoVarint - always calculate size
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{valueAccess});");
            }
            else
            {
                // Optional ProtoVarint - check for non-default value
                sb.AppendIndentedLine($"if ({valueAccess} != 0)");
                sb.StartNewBlock();
                TagCodeHelper.AddTagSize(sb, member.FieldId, WireType.VarInt, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.{sizeMethod}({castPrefix}{valueAccess});");
                sb.EndBlock();
            }
        }

        #endregion

        #region Method Mapping

        /// <summary>
        /// Gets the SpanReader method name for reading the specified varint type.
        /// </summary>
        private static string GetReadMethod(ProtoVarintType type)
        {
            return type switch
            {
                ProtoVarintType.UInt32 => "ReadVarUInt32",
                ProtoVarintType.Int32 => "ReadVarInt32",
                ProtoVarintType.SInt32 => "ReadZigZagVarInt32",
                ProtoVarintType.UInt64 => "ReadVarUInt64",
                ProtoVarintType.Int64 => "ReadVarInt64",
                ProtoVarintType.SInt64 => "ReadZigZagVarInt64",
                _ => "ReadVarUInt32"
            };
        }

        /// <summary>
        /// Gets the StreamWriter/SpanWriter method name for writing the specified varint type.
        /// </summary>
        private static string GetWriteMethod(ProtoVarintType type)
        {
            return type switch
            {
                ProtoVarintType.UInt32 => "WriteVarUInt32",
                ProtoVarintType.Int32 => "WriteVarInt32",
                ProtoVarintType.SInt32 => "WriteZigZag32",
                ProtoVarintType.UInt64 => "WriteVarUInt64",
                ProtoVarintType.Int64 => "WriteVarInt64",
                ProtoVarintType.SInt64 => "WriteZigZag64",
                _ => "WriteVarUInt32"
            };
        }

        /// <summary>
        /// Gets the WriteSizeCalculator method name for calculating size of the specified varint type.
        /// </summary>
        private static string GetSizeMethod(ProtoVarintType type)
        {
            return type switch
            {
                ProtoVarintType.UInt32 => "WriteVarUInt32",
                ProtoVarintType.Int32 => "WriteVarInt32",
                ProtoVarintType.SInt32 => "WriteZigZag32",
                ProtoVarintType.UInt64 => "WriteVarUInt64",
                ProtoVarintType.Int64 => "WriteVarInt64",
                ProtoVarintType.SInt64 => "WriteZigZag64",
                _ => "WriteVarUInt32"
            };
        }

        /// <summary>
        /// Gets the cast prefix for signed types.
        /// </summary>
        private static string GetCastPrefix(ProtoVarintType type)
        {
            return type switch
            {
                ProtoVarintType.Int32 => "(int)",
                ProtoVarintType.SInt32 => "(int)",
                ProtoVarintType.Int64 => "(long)",
                ProtoVarintType.SInt64 => "(long)",
                _ => ""
            };
        }

        #endregion
    }
}
