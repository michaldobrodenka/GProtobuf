using System;
using System.Text;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Maps C# types to protobuf wire format operations.
    /// </summary>
    internal static class TypeMapping
    {
        #region Type Classification

        /// <summary>
        /// Checks if type is a primitive that can be read/written directly.
        /// Includes string but NOT Guid/TimeSpan (they need special handling).
        /// </summary>
        public static bool IsSimpleType(string typeName)
        {
            return NormalizeTypeName(typeName) switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => true,
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => true,
                "System.Single" or "System.Double" => true,
                "System.Boolean" or "System.String" or "System.Char" => true,
                "System.Byte[]" => true,
                _ => false
            };
        }

        /// <summary>
        /// Checks if type can be used in packed arrays (excludes string, byte).
        /// </summary>
        public static bool IsPrimitiveArrayType(string elementTypeName)
        {
            return NormalizeTypeName(elementTypeName) switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => true,
                "System.UInt32" or "System.UInt64" or "System.UInt16" => true,
                "System.Single" or "System.Double" => true,
                "System.Boolean" or "System.Char" => true,
                // byte is EXCLUDED - byte[] serializes as length-delimited
                _ => false
            };
        }

        /// <summary>
        /// Gets the default value expression for skip-if-default check.
        /// Returns null for types that should always be written.
        /// </summary>
        public static string GetDefaultValueCheck(string typeName, string valueExpr)
        {
            var normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => $"{valueExpr} != 0",
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => $"{valueExpr} != 0",
                "System.Single" => $"{valueExpr} != 0f",
                "System.Double" => $"{valueExpr} != 0d",
                "System.Boolean" => null, // bool is always written (even false)
                "System.Char" => $"{valueExpr} != '\\0'",
                "System.String" => $"{valueExpr} != null",
                "System.Byte[]" => $"{valueExpr} != null",
                _ => null
            };
        }

        #endregion

        #region Wire Type

        public static WireType GetWireType(string typeName, DataFormat format = DataFormat.Default)
        {
            var normalized = NormalizeTypeName(typeName);

            if (format == DataFormat.FixedSize)
            {
                return normalized switch
                {
                    "System.Int32" or "System.UInt32" or "System.Int16" or "System.UInt16" => WireType.Fixed32b,
                    "System.Int64" or "System.UInt64" => WireType.Fixed64b,
                    _ => GetDefaultWireType(normalized)
                };
            }

            return GetDefaultWireType(normalized);
        }

        private static WireType GetDefaultWireType(string normalizedTypeName)
        {
            return normalizedTypeName switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => WireType.VarInt,
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => WireType.VarInt,
                "System.Boolean" or "System.Char" => WireType.VarInt,
                "System.Single" => WireType.Fixed32b,
                "System.Double" => WireType.Fixed64b,
                "System.String" or "System.Byte[]" => WireType.Len,
                _ => WireType.Len
            };
        }

        /// <summary>
        /// Gets wire type as string for code generation (e.g., "WireType.VarInt").
        /// </summary>
        public static string GetWireTypeString(string typeName, DataFormat format = DataFormat.Default)
        {
            var wireType = GetWireType(typeName, format);
            return wireType switch
            {
                WireType.VarInt => "WireType.VarInt",
                WireType.Fixed32b => "WireType.Fixed32b",
                WireType.Fixed64b => "WireType.Fixed64b",
                WireType.Len => "WireType.Len",
                _ => "WireType.VarInt"
            };
        }

        #endregion

        #region Read Expressions

        /// <summary>
        /// Gets read expression for a primitive type.
        /// </summary>
        public static string GetReadExpression(
            string typeName,
            DataFormat format = DataFormat.Default,
            string readerVar = "reader",
            string wireTypeVar = "wireType")
        {
            var normalized = NormalizeTypeName(typeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedInt32()",
                    DataFormat.ZigZag => $"{readerVar}.ReadZigZagVarInt32()",
                    _ => $"{readerVar}.ReadVarInt32()"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedInt64()",
                    DataFormat.ZigZag => $"{readerVar}.ReadInt64({wireTypeVar}, true)",
                    _ => $"{readerVar}.ReadInt64({wireTypeVar}, false)"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => $"(short){readerVar}.ReadFixedInt32()",
                    DataFormat.ZigZag => $"{readerVar}.ReadInt16({wireTypeVar}, true)",
                    _ => $"{readerVar}.ReadInt16({wireTypeVar}, false)"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{readerVar}.ReadSByte({wireTypeVar}, true)",
                    _ => $"{readerVar}.ReadSByte({wireTypeVar}, false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedUInt32()",
                    _ => $"{readerVar}.ReadUInt32({wireTypeVar})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedUInt64()",
                    _ => $"{readerVar}.ReadUInt64({wireTypeVar})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"(ushort){readerVar}.ReadFixedUInt32()",
                    _ => $"{readerVar}.ReadUInt16({wireTypeVar})"
                },
                "System.Byte" => $"{readerVar}.ReadByte({wireTypeVar})",
                "System.Single" => $"{readerVar}.ReadFloat({wireTypeVar})",
                "System.Double" => $"{readerVar}.ReadDouble({wireTypeVar})",
                "System.Boolean" => $"{readerVar}.ReadBool({wireTypeVar})",
                "System.Char" => $"(char){readerVar}.ReadVarUInt32()",
                "System.String" => $"{readerVar}.ReadString({wireTypeVar})",
                "System.Byte[]" => $"{readerVar}.ReadByteArray()",
                _ => null
            };
        }

        /// <summary>
        /// Gets read expression for packed array elements.
        /// </summary>
        public static string GetPackedArrayReadExpression(
            string elementTypeName,
            DataFormat format = DataFormat.Default,
            string readerVar = "reader")
        {
            var normalized = NormalizeTypeName(elementTypeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeInt32Array()",
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedVarIntInt32Array(true)",
                    _ => $"{readerVar}.ReadPackedVarIntInt32Array(false)"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeInt64Array()",
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedVarIntInt64Array(true)",
                    _ => $"{readerVar}.ReadPackedVarIntInt64Array(false)"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeInt16Array()",
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedInt16Array(true)",
                    _ => $"{readerVar}.ReadPackedInt16Array(false)"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedSByteArray(true)",
                    _ => $"{readerVar}.ReadPackedSByteArray(false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeUInt32Array()",
                    _ => $"{readerVar}.ReadPackedUInt32Array()"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeUInt64Array()",
                    _ => $"{readerVar}.ReadPackedUInt64Array()"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeUInt16Array()",
                    _ => $"{readerVar}.ReadPackedUInt16Array()"
                },
                "System.Single" => $"{readerVar}.ReadPackedFloatArray()",
                "System.Double" => $"{readerVar}.ReadPackedDoubleArray()",
                "System.Boolean" => $"{readerVar}.ReadPackedBoolArray()",
                "System.Char" => $"{readerVar}.ReadPackedVarIntCharArray()",
                _ => null
            };
        }

        /// <summary>
        /// Gets read expression for non-packed array element (without wireType).
        /// Used inside while loop for repeated fields.
        /// </summary>
        public static string GetElementReadExpression(
            string elementTypeName,
            DataFormat format = DataFormat.Default,
            string readerVar = "reader")
        {
            var normalized = NormalizeTypeName(elementTypeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedInt32()",
                    DataFormat.ZigZag => $"{readerVar}.ReadZigZagVarInt32()",
                    _ => $"{readerVar}.ReadVarInt32()"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedInt64()",
                    DataFormat.ZigZag => $"{readerVar}.ReadZigZagVarInt64()",
                    _ => $"{readerVar}.ReadVarInt64()"
                },
                "System.Int16" => format switch
                {
                    DataFormat.ZigZag => $"(short){readerVar}.ReadZigZagVarInt32()",
                    _ => $"(short){readerVar}.ReadVarInt32()"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"(sbyte){readerVar}.ReadZigZagVarInt32()",
                    _ => $"(sbyte){readerVar}.ReadVarInt32()"
                },
                "System.UInt32" => $"{readerVar}.ReadVarUInt32()",
                "System.UInt64" => $"{readerVar}.ReadVarUInt64()",
                "System.UInt16" => $"(ushort){readerVar}.ReadVarUInt32()",
                "System.Byte" => $"(byte){readerVar}.ReadVarUInt32()",
                "System.Single" => $"{readerVar}.ReadFixedFloat()",
                "System.Double" => $"{readerVar}.ReadFixedDouble()",
                "System.Boolean" => $"{readerVar}.ReadBool(WireType.VarInt)",
                "System.Char" => $"(char){readerVar}.ReadVarUInt32()",
                _ => null
            };
        }

        #endregion

        #region Write Expressions

        /// <summary>
        /// Gets write expression for a primitive type.
        /// </summary>
        public static string GetWriteExpression(
            string typeName,
            string valueExpr,
            DataFormat format = DataFormat.Default,
            string writerVar = "writer")
        {
            var normalized = NormalizeTypeName(typeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedSizeInt32({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZag32({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt32({valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixed64({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZagVarInt64({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt64({valueExpr})"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedInt32({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteInt16({valueExpr}, true)",
                    _ => $"{writerVar}.WriteInt16({valueExpr}, false)"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{writerVar}.WriteSByte({valueExpr}, true)",
                    _ => $"{writerVar}.WriteSByte({valueExpr}, false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteUInt32({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixed64({valueExpr})",
                    _ => $"{writerVar}.WriteUInt64({valueExpr})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteUInt16({valueExpr})"
                },
                "System.Byte" => $"{writerVar}.WriteByte({valueExpr})",
                "System.Single" => $"{writerVar}.WriteFloat({valueExpr})",
                "System.Double" => $"{writerVar}.WriteDouble({valueExpr})",
                "System.Boolean" => $"{writerVar}.WriteBool({valueExpr})",
                "System.Char" => $"{writerVar}.WriteVarUInt32((uint){valueExpr})",
                "System.String" => $"{writerVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{writerVar}.WriteVarUInt32((uint){valueExpr}.Length); {writerVar}.WriteBytes({valueExpr})",
                _ => null
            };
        }

        /// <summary>
        /// Gets write expression for packed array element.
        /// </summary>
        public static string GetElementWriteExpression(
            string elementTypeName,
            string valueExpr,
            DataFormat format = DataFormat.Default,
            string writerVar = "writer")
        {
            var normalized = NormalizeTypeName(elementTypeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedSizeInt32({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZag32({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt32({valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixed64({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZagVarInt64({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt64({valueExpr})"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedInt32({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteInt16({valueExpr}, true)",
                    _ => $"{writerVar}.WriteInt16({valueExpr}, false)"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{writerVar}.WriteSByte({valueExpr}, true)",
                    _ => $"{writerVar}.WriteSByte({valueExpr}, false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteUInt32({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixed64({valueExpr})",
                    _ => $"{writerVar}.WriteUInt64({valueExpr})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteUInt16({valueExpr})"
                },
                "System.Byte" => $"{writerVar}.WriteByte({valueExpr})",
                "System.Single" => $"{writerVar}.WriteFloat({valueExpr})",
                "System.Double" => $"{writerVar}.WriteDouble({valueExpr})",
                "System.Boolean" => $"{writerVar}.WriteBool({valueExpr})",
                "System.Char" => $"{writerVar}.WriteVarUInt32((uint){valueExpr})",
                _ => null
            };
        }

        #endregion

        #region Size Expressions

        /// <summary>
        /// Gets size calculation expression for a primitive type.
        /// </summary>
        public static string GetSizeExpression(
            string typeName,
            string valueExpr,
            DataFormat format = DataFormat.Default,
            string calculatorVar = "calculator")
        {
            var normalized = NormalizeTypeName(typeName);

            return normalized switch
            {
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.WriteFixedSizeInt32({valueExpr})",
                    DataFormat.ZigZag => $"{calculatorVar}.WriteZigZag32({valueExpr})",
                    _ => $"{calculatorVar}.WriteVarInt32({valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(8)",
                    DataFormat.ZigZag => $"{calculatorVar}.WriteZigZagVarInt64({valueExpr})",
                    _ => $"{calculatorVar}.WriteVarInt64({valueExpr})"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(4)",
                    DataFormat.ZigZag => $"{calculatorVar}.WriteInt16({valueExpr}, true)",
                    _ => $"{calculatorVar}.WriteInt16({valueExpr}, false)"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{calculatorVar}.WriteSByte({valueExpr}, true)",
                    _ => $"{calculatorVar}.WriteSByte({valueExpr}, false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(4)",
                    _ => $"{calculatorVar}.WriteUInt32({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(8)",
                    _ => $"{calculatorVar}.WriteUInt64({valueExpr})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(4)",
                    _ => $"{calculatorVar}.WriteUInt16({valueExpr})"
                },
                "System.Byte" => $"{calculatorVar}.WriteByte({valueExpr})",
                "System.Single" => $"{calculatorVar}.AddByteLength(4)",
                "System.Double" => $"{calculatorVar}.AddByteLength(8)",
                "System.Boolean" => $"{calculatorVar}.WriteBool({valueExpr})",
                "System.Char" => $"{calculatorVar}.WriteVarUInt32((uint){valueExpr})",
                "System.String" => $"{calculatorVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{calculatorVar}.WriteBytes({valueExpr})",
                _ => null
            };
        }

        /// <summary>
        /// Gets size calculation expression for packed array element.
        /// </summary>
        public static string GetElementSizeExpression(
            string elementTypeName,
            string valueExpr,
            DataFormat format = DataFormat.Default,
            string calculatorVar = "calculator")
        {
            // Same as GetSizeExpression for elements
            return GetSizeExpression(elementTypeName, valueExpr, format, calculatorVar);
        }

        #endregion

        #region Type Name Utilities

        public static string NormalizeTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            if (typeName[typeName.Length - 1] == '?')
                typeName = typeName.Substring(0, typeName.Length - 1);

            if (typeName.StartsWith("System.Nullable<") && typeName.EndsWith(">"))
                typeName = typeName.Substring(16, typeName.Length - 17);

            return typeName switch
            {
                "int" => "System.Int32",
                "long" => "System.Int64",
                "short" => "System.Int16",
                "sbyte" => "System.SByte",
                "uint" => "System.UInt32",
                "ulong" => "System.UInt64",
                "ushort" => "System.UInt16",
                "byte" => "System.Byte",
                "float" => "System.Single",
                "double" => "System.Double",
                "decimal" => "System.Decimal",
                "bool" => "System.Boolean",
                "string" => "System.String",
                "char" => "System.Char",
                "object" => "System.Object",
                "byte[]" => "System.Byte[]",
                "Int32" => "System.Int32",
                "Int64" => "System.Int64",
                "Int16" => "System.Int16",
                "SByte" => "System.SByte",
                "UInt32" => "System.UInt32",
                "UInt64" => "System.UInt64",
                "UInt16" => "System.UInt16",
                "Byte" => "System.Byte",
                "Single" => "System.Single",
                "Double" => "System.Double",
                "Boolean" => "System.Boolean",
                "String" => "System.String",
                "Char" => "System.Char",
                _ => typeName
            };
        }

        public static string GetShortTypeName(string typeName)
        {
            return NormalizeTypeName(typeName) switch
            {
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.Int16" => "short",
                "System.SByte" => "sbyte",
                "System.UInt32" => "uint",
                "System.UInt64" => "ulong",
                "System.UInt16" => "ushort",
                "System.Byte" => "byte",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.Decimal" => "decimal",
                "System.Boolean" => "bool",
                "System.String" => "string",
                "System.Char" => "char",
                "System.Object" => "object",
                "System.Byte[]" => "byte[]",
                _ => typeName
            };
        }

        public static string GetClassNameFromFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            var lastDot = fullName.LastIndexOf('.');
            return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
        }

        public static string SanitizeTypeNameForMethod(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            return typeName
                .Replace(".", "")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace(" ", "")
                .Replace("[]", "Array")
                .Replace("?", "Nullable");
        }

        #endregion

        #region Tag Helpers

        /// <summary>
        /// Precomputes tag bytes for code generation.
        /// </summary>
        public static (string BytesString, int ByteCount) PrecomputeTagBytes(int fieldId, WireType wireType)
        {
            uint tag = (uint)((fieldId << 3) | (int)wireType);

            var tagBytes = new byte[5];
            int byteCount = 0;

            while (tag > 0x7F)
            {
                tagBytes[byteCount++] = (byte)((tag & 0x7F) | 0x80);
                tag >>= 7;
            }
            tagBytes[byteCount++] = (byte)tag;

            var sb = new StringBuilder();
            for (int i = 0; i < byteCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("0x").Append(tagBytes[i].ToString("X2"));
            }

            return (sb.ToString(), byteCount);
        }

        #endregion
    }
}
