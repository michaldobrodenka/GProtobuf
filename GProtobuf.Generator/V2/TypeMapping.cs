using System;
using System.Text;

namespace GProtobuf.Generator.V2
{
    /// <summary>
    /// Maps C# types to protobuf wire format operations.
    /// </summary>
    public static class TypeMapping
    {
        #region Type Classification

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

        public static bool IsVarintType(string typeName)
        {
            return NormalizeTypeName(typeName) switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => true,
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => true,
                "System.Boolean" or "System.Char" => true,
                _ => false
            };
        }

        public static bool IsFixedType(string typeName, DataFormat format = DataFormat.Default)
        {
            var normalized = NormalizeTypeName(typeName);

            if (normalized is "System.Single" or "System.Double")
                return true;

            if (format == DataFormat.FixedSize)
            {
                return normalized is "System.Int32" or "System.UInt32"
                                  or "System.Int64" or "System.UInt64";
            }

            return false;
        }

        public static bool IsLengthDelimitedType(string typeName)
        {
            var normalized = NormalizeTypeName(typeName);
            return normalized is "System.String" or "System.Byte[]";
        }

        public static bool IsDecimalType(string typeName)
        {
            return NormalizeTypeName(typeName) == "System.Decimal";
        }

        #endregion

        #region Wire Type

        internal static WireType GetWireType(string typeName, DataFormat format = DataFormat.Default)
        {
            var normalized = NormalizeTypeName(typeName);

            if (format == DataFormat.FixedSize)
            {
                return normalized switch
                {
                    "System.Int32" or "System.UInt32" => WireType.Fixed32b,
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

        #endregion

        #region Read Expressions

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
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedSizeInt32()",
                    DataFormat.ZigZag => $"{readerVar}.ReadZigZagVarInt32()",
                    _ => $"{readerVar}.ReadVarInt32()"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedSizeInt64()",
                    DataFormat.ZigZag => $"{readerVar}.ReadZigZagVarInt64()",
                    _ => $"{readerVar}.ReadVarInt64()"
                },
                "System.Int16" => format switch
                {
                    DataFormat.ZigZag => $"(short){readerVar}.ReadZigZagVarInt32()",
                    DataFormat.FixedSize => $"(short){readerVar}.ReadFixedSizeInt32()",
                    _ => $"(short){readerVar}.ReadVarInt32()"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"{readerVar}.ReadSByte({wireTypeVar}, true)",
                    _ => $"{readerVar}.ReadSByte({wireTypeVar}, false)"
                },
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedUInt32()",
                    _ => $"{readerVar}.ReadVarUInt32()"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadFixedUInt64()",
                    _ => $"{readerVar}.ReadVarUInt64()"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"(ushort){readerVar}.ReadFixedUInt32()",
                    _ => $"(ushort){readerVar}.ReadVarUInt32()"
                },
                "System.Byte" => $"(byte){readerVar}.ReadVarUInt32()",
                "System.Single" => $"{readerVar}.ReadFloat({wireTypeVar})",
                "System.Double" => $"{readerVar}.ReadDouble({wireTypeVar})",
                "System.Boolean" => $"{readerVar}.ReadBool()",
                "System.Char" => $"(char){readerVar}.ReadVarInt32()",
                "System.String" => $"{readerVar}.ReadString({wireTypeVar})",
                "System.Byte[]" => $"{readerVar}.ReadBytes({wireTypeVar})",
                _ => null
            };
        }

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
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedZigZagInt32Array()",
                    _ => $"{readerVar}.ReadPackedVarInt32Array()"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{readerVar}.ReadPackedFixedSizeInt64Array()",
                    DataFormat.ZigZag => $"{readerVar}.ReadPackedZigZagInt64Array()",
                    _ => $"{readerVar}.ReadPackedVarInt64Array()"
                },
                "System.UInt32" => $"{readerVar}.ReadPackedVarUInt32Array()",
                "System.UInt64" => $"{readerVar}.ReadPackedVarUInt64Array()",
                "System.Single" => $"{readerVar}.ReadPackedFloatArray()",
                "System.Double" => $"{readerVar}.ReadPackedDoubleArray()",
                "System.Boolean" => $"{readerVar}.ReadPackedBoolArray()",
                _ => null
            };
        }

        #endregion

        #region Write Expressions

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
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZagVarInt32({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt32({valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedSizeInt64({valueExpr})",
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZagVarInt64({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt64({valueExpr})"
                },
                "System.Int16" => format switch
                {
                    DataFormat.ZigZag => $"{writerVar}.WriteZigZagVarInt32({valueExpr})",
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedSizeInt32({valueExpr})",
                    _ => $"{writerVar}.WriteVarInt32({valueExpr})"
                },
                "System.SByte" => $"{writerVar}.WriteVarInt32({valueExpr})",
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteVarUInt32({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt64({valueExpr})",
                    _ => $"{writerVar}.WriteVarUInt64({valueExpr})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => $"{writerVar}.WriteFixedUInt32({valueExpr})",
                    _ => $"{writerVar}.WriteVarUInt32({valueExpr})"
                },
                "System.Byte" => $"{writerVar}.WriteVarUInt32({valueExpr})",
                "System.Single" => $"{writerVar}.WriteFloat({valueExpr})",
                "System.Double" => $"{writerVar}.WriteDouble({valueExpr})",
                "System.Boolean" => $"{writerVar}.WriteBool({valueExpr})",
                "System.Char" => $"{writerVar}.WriteVarInt32((int){valueExpr})",
                "System.String" => $"{writerVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{writerVar}.WriteBytes({valueExpr})",
                _ => null
            };
        }

        #endregion

        #region Size Expressions

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
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(4)",
                    DataFormat.ZigZag => $"{calculatorVar}.WriteZigZagVarInt32({valueExpr})",
                    _ => $"{calculatorVar}.WriteVarInt32({valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(8)",
                    DataFormat.ZigZag => $"{calculatorVar}.WriteZigZagVarInt64({valueExpr})",
                    _ => $"{calculatorVar}.WriteVarInt64({valueExpr})"
                },
                "System.Int16" or "System.SByte" => $"{calculatorVar}.WriteVarInt32({valueExpr})",
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(4)",
                    _ => $"{calculatorVar}.WriteVarUInt32({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => $"{calculatorVar}.AddByteLength(8)",
                    _ => $"{calculatorVar}.WriteVarUInt64({valueExpr})"
                },
                "System.UInt16" or "System.Byte" => $"{calculatorVar}.WriteVarUInt32({valueExpr})",
                "System.Single" => $"{calculatorVar}.AddByteLength(4)",
                "System.Double" => $"{calculatorVar}.AddByteLength(8)",
                "System.Boolean" => $"{calculatorVar}.WriteBool({valueExpr})",
                "System.Char" => $"{calculatorVar}.WriteVarInt32((int){valueExpr})",
                "System.String" => $"{calculatorVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{calculatorVar}.WriteBytes({valueExpr})",
                _ => null
            };
        }

        #endregion

        #region Type Name Utilities

        public static string NormalizeTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            // Handle nullable types
            if (typeName[typeName.Length - 1] == '?')
                typeName = typeName.Substring(0, typeName.Length - 1);

            // Handle Nullable<T>
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

        #region Enum Support

        public static string GetEnumReadExpression(
            string enumTypeName,
            string underlyingType = "System.Int32",
            string readerVar = "reader")
        {
            var readExpr = GetReadExpression(underlyingType, DataFormat.Default, readerVar);
            return $"({enumTypeName}){readExpr}";
        }

        public static string GetEnumWriteExpression(
            string valueExpr,
            string underlyingType = "System.Int32",
            string writerVar = "writer")
        {
            var normalized = NormalizeTypeName(underlyingType);
            var castExpr = normalized switch
            {
                "System.Int32" => $"(int){valueExpr}",
                "System.Int64" => $"(long){valueExpr}",
                "System.Int16" => $"(short){valueExpr}",
                "System.SByte" => $"(sbyte){valueExpr}",
                "System.UInt32" => $"(uint){valueExpr}",
                "System.UInt64" => $"(ulong){valueExpr}",
                "System.UInt16" => $"(ushort){valueExpr}",
                "System.Byte" => $"(byte){valueExpr}",
                _ => $"(int){valueExpr}"
            };

            return GetWriteExpression(underlyingType, castExpr, DataFormat.Default, writerVar);
        }

        #endregion

        #region Tag Helpers

        internal static (string BytesString, int ByteCount) PrecomputeTagBytes(int fieldId, WireType wireType)
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
