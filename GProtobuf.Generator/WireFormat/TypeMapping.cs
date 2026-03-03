using System.Text;
using GProtobuf.Generator.Attributes;

namespace GProtobuf.Generator.WireFormat
{
    /// <summary>
    /// Maps C# types to protobuf wire format operations.
    /// </summary>
    internal static class TypeMapping
    {
        #region Constants

        /// <summary>
        /// Normalized type name for System.Boolean.
        /// </summary>
        public const string BooleanTypeName = "System.Boolean";

        #endregion

        #region Type Classification

        /// <summary>
        /// Checks if type is System.Boolean.
        /// </summary>
        public static bool IsBooleanType(string typeName)
        {
            return NormalizeTypeName(typeName) == BooleanTypeName;
        }

        /// <summary>
        /// Checks if type is a primitive that can be read/written directly.
        /// Includes string, Guid, and TimeSpan.
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
                "System.Guid" or "System.TimeSpan" or "System.DateTime" => true,
                _ => false
            };
        }

        /// <summary>
        /// Checks if type is unsupported for protobuf serialization.
        /// These types cannot be serialized/deserialized by the generator.
        /// Properties of these types will be skipped with a warning comment.
        /// </summary>
        public static bool IsUnsupportedType(string typeName)
        {
            return NormalizeTypeName(typeName) switch
            {
                "System.Type" => true, // Reflection metadata type - cannot serialize
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
        /// Checks if type can be used in non-packed repeated fields (includes string, byte, Guid, TimeSpan).
        /// Note: byte[] is NOT an element type - it's a field type. For List&lt;byte&gt;, element is byte (primitive).
        /// </summary>
        public static bool IsNonPackedArrayType(string elementTypeName)
        {
            var normalized = NormalizeTypeName(elementTypeName);
            return IsPrimitiveArrayType(normalized) ||
                   normalized == "System.String" ||
                   normalized == "System.Byte" ||
                   normalized == "System.Guid" ||
                   normalized == "System.TimeSpan";
        }

        public static bool ShouldBePackedByDefault(string elementTypeName)
        {
            // Level200: primitives use UNPACKED encoding by default
            // PACKED only when explicitly marked with [ProtoMember(N, IsPacked = true)]
            return false;
        }

        /// <summary>
        /// Gets the default value expression for skip-if-default check.
        /// Returns null for types that should always be written.
        /// </summary>
        /// <remarks>
        /// Level200 requirement: Proto2 default values MUST NOT be written to wire.
        /// This includes false for bool, 0 for numbers, null for strings, etc.
        /// </remarks>
        public static string GetDefaultValueCheck(string typeName, string valueExpr)
        {
            var normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => $"{valueExpr} != 0",
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => $"{valueExpr} != 0",
                "System.Single" => $"{valueExpr} != 0f",
                "System.Double" => $"{valueExpr} != 0d",
                "System.Boolean" => $"{valueExpr}", // Level200: skip if false (proto2 default)
                "System.Char" => $"{valueExpr} != '\\0'",
                "System.String" => $"{valueExpr} != null",
                "System.Byte[]" => $"{valueExpr} != null",
                "System.Guid" => $"{valueExpr} != global::System.Guid.Empty",
                "System.TimeSpan" => $"{valueExpr} != global::System.TimeSpan.Zero",
                "System.DateTime" => $"{valueExpr} != default(global::System.DateTime)",
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
                "System.String" or "System.Byte[]" or "System.Guid" or "System.DateTime" or "System.TimeSpan" => WireType.Len, // TimeSpan serialized as sub-message (Level200)
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
                "System.Guid" => $"{readerVar}.ReadGuid({wireTypeVar})",
                "System.TimeSpan" => $"{readerVar}.ReadTimeSpan({wireTypeVar})",
                "System.DateTime" => $"{readerVar}.ReadDateTime({wireTypeVar})",
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
                    DataFormat.FixedSize => $"(short){readerVar}.ReadFixedInt32()",
                    DataFormat.ZigZag => $"(short){readerVar}.ReadZigZagVarInt32()",
                    _ => $"(short){readerVar}.ReadVarInt32()"
                },
                "System.SByte" => format switch
                {
                    DataFormat.FixedSize => $"(sbyte){readerVar}.ReadFixedInt32()",
                    DataFormat.ZigZag => $"(sbyte){readerVar}.ReadZigZagVarInt32()",
                    _ => $"(sbyte){readerVar}.ReadVarInt32()"
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
                "System.Single" => $"{readerVar}.ReadFixedFloat()",
                "System.Double" => $"{readerVar}.ReadFixedDouble()",
                "System.Boolean" => $"{readerVar}.ReadBool(WireType.VarInt)",
                "System.Char" => $"(char){readerVar}.ReadVarUInt32()",
                "System.String" => $"{readerVar}.ReadString(WireType.Len)",
                "System.Guid" => $"{readerVar}.ReadGuid(WireType.Len)",
                "System.TimeSpan" => $"{readerVar}.ReadTimeSpan(WireType.Len)", // TimeSpan is sub-message (Level200)
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
                "System.Boolean" => $"{writerVar}.WriteBoolTrue()", // Non-nullable bool: inside if(value) check, always true
                "System.Char" => $"{writerVar}.WriteVarUInt32((uint){valueExpr})",
                "System.String" => $"{writerVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{writerVar}.WriteVarUInt32((uint){valueExpr}.Length); {writerVar}.WriteBytes({valueExpr})",
                "System.Guid" => $"{writerVar}.WriteGuid({valueExpr})",
                "System.TimeSpan" => $"{writerVar}.WriteTimeSpan({valueExpr})",
                "System.DateTime" => $"{writerVar}.WriteDateTime({valueExpr})",
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
                "System.String" => $"{writerVar}.WriteString({valueExpr})",
                "System.Guid" => $"{writerVar}.WriteGuid({valueExpr})",
                "System.TimeSpan" => $"{writerVar}.WriteTimeSpan({valueExpr})",
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
                "System.Boolean" => $"{calculatorVar}.WriteBoolTrue()", // Non-nullable bool: inside if(value) check, always true
                "System.Char" => $"{calculatorVar}.WriteVarUInt32((uint){valueExpr})",
                "System.String" => $"{calculatorVar}.WriteString({valueExpr})",
                "System.Byte[]" => $"{calculatorVar}.WriteBytes({valueExpr})",
                "System.Guid" => $"{calculatorVar}.WriteGuid({valueExpr})",
                "System.TimeSpan" => $"{calculatorVar}.WriteTimeSpan({valueExpr})",
                "System.DateTime" => $"{calculatorVar}.WriteDateTime({valueExpr})",
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

        /// <summary>
        /// Checks if a type can use inline size calculation (no WriteSizeCalculator needed).
        /// Returns true for primitive types with predictable wire sizes.
        /// </summary>
        public static bool CanUseInlineSizeCalculation(string typeName, bool isEnum = false)
        {
            if (isEnum) return true; // Enums are varint

            var normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                // Varint types - can use WireFormatHelpers.GetVarintSize
                "System.Int32" or "System.Int64" or "System.Int16" or "System.SByte" => true,
                "System.UInt32" or "System.UInt64" or "System.UInt16" or "System.Byte" => true,
                // Fixed size types - constant size
                "System.Single" or "System.Double" => true,
                "System.Boolean" or "System.Char" => true,
                _ => false
            };
        }

        /// <summary>
        /// Gets inline size expression that returns an int directly (no calculator needed).
        /// Used for map entry optimization where we can calculate size inline.
        /// Returns null for types that need WriteSizeCalculator.
        /// </summary>
        public static string GetInlineSizeExpression(
            string typeName,
            string valueExpr,
            DataFormat format = DataFormat.Default,
            bool isEnum = false)
        {
            if (isEnum)
            {
                // Enums are varint-encoded as int32
                return $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize((uint)(int){valueExpr})";
            }

            var normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                // Signed varint types - WriteVarInt32 casts to uint, so use GetVarintSize with cast
                // Note: This matches the writer behavior which casts signed to unsigned before varint encoding
                "System.Int32" => format switch
                {
                    DataFormat.FixedSize => "4",
                    DataFormat.ZigZag => $"global::GProtobuf.Core.WireFormatHelpers.GetZigZagVarintSize({valueExpr})",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize((uint){valueExpr})"
                },
                "System.Int64" => format switch
                {
                    DataFormat.FixedSize => "8",
                    DataFormat.ZigZag => $"global::GProtobuf.Core.WireFormatHelpers.GetZigZagVarintSize64({valueExpr})",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize64((ulong){valueExpr})"
                },
                "System.Int16" => format switch
                {
                    DataFormat.FixedSize => "4",
                    DataFormat.ZigZag => $"global::GProtobuf.Core.WireFormatHelpers.GetZigZagVarintSize({valueExpr})",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize((uint){valueExpr})"
                },
                "System.SByte" => format switch
                {
                    DataFormat.ZigZag => $"global::GProtobuf.Core.WireFormatHelpers.GetZigZagVarintSize({valueExpr})",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize((uint){valueExpr})"
                },
                // Unsigned varint types
                "System.UInt32" => format switch
                {
                    DataFormat.FixedSize => "4",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize({valueExpr})"
                },
                "System.UInt64" => format switch
                {
                    DataFormat.FixedSize => "8",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize64({valueExpr})"
                },
                "System.UInt16" => format switch
                {
                    DataFormat.FixedSize => "4",
                    _ => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize({valueExpr})"
                },
                "System.Byte" => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize({valueExpr})", // Byte values 128-255 need 2 bytes
                // Fixed size types - constant
                "System.Single" => "4",
                "System.Double" => "8",
                "System.Boolean" => "1",
                "System.Char" => $"global::GProtobuf.Core.WireFormatHelpers.GetVarintSize((uint){valueExpr})",
                _ => null
            };
        }

        /// <summary>
        /// Gets the fixed wire size for a type, or -1 if the size is variable.
        /// Used for compile-time size calculation optimization.
        /// </summary>
        public static int GetFixedWireSize(string typeName, DataFormat format = DataFormat.Default)
        {
            var normalized = NormalizeTypeName(typeName);
            return normalized switch
            {
                "System.Single" => 4,
                "System.Double" => 8,
                "System.Boolean" => 1,
                // Note: Byte is NOT fixed size - values 128-255 need 2 bytes as varint
                "System.Int32" when format == DataFormat.FixedSize => 4,
                "System.UInt32" when format == DataFormat.FixedSize => 4,
                "System.Int64" when format == DataFormat.FixedSize => 8,
                "System.UInt64" when format == DataFormat.FixedSize => 8,
                _ => -1 // Variable size
            };
        }

        #endregion

        #region Type Name Utilities

        /// <summary>
        /// Recursively normalizes generic type arguments.
        /// Example: "KeyValuePair&lt;int, string&gt;" -> "System.Collections.Generic.KeyValuePair&lt;System.Int32, System.String&gt;"
        /// </summary>
        public static string NormalizeGenericTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            // Check if it has generic arguments
            int openBracket = typeName.IndexOf('<');
            if (openBracket < 0)
            {
                // No generic arguments - just normalize the base type
                return NormalizeTypeName(typeName);
            }

            int closeBracket = typeName.LastIndexOf('>');
            if (closeBracket <= openBracket)
                return NormalizeTypeName(typeName);

            // Extract base type and generic arguments
            string baseType = typeName.Substring(0, openBracket);
            string argsString = typeName.Substring(openBracket + 1, closeBracket - openBracket - 1);

            // Parse and normalize each generic argument (handling nested generics)
            var normalizedArgs = new System.Collections.Generic.List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < argsString.Length; i++)
            {
                char c = argsString[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    var arg = argsString.Substring(start, i - start).Trim();
                    normalizedArgs.Add(NormalizeGenericTypeName(arg)); // Recursive call
                    start = i + 1;
                }
            }

            // Add the last argument
            if (start < argsString.Length)
            {
                var arg = argsString.Substring(start).Trim();
                normalizedArgs.Add(NormalizeGenericTypeName(arg)); // Recursive call
            }

            // Rebuild the type with normalized arguments
            var normalizedBaseType = NormalizeTypeName(baseType);
            return $"{normalizedBaseType}<{string.Join(", ", normalizedArgs)}>";
        }

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
            if (string.IsNullOrEmpty(typeName))
                return typeName;

            var normalized = NormalizeTypeName(typeName);

            return normalized switch
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
                "System.Guid" => "Guid",
                "System.TimeSpan" => "TimeSpan",
                "System.DateTime" => "DateTime",
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
            // Tag encoding formula: (fieldId << 3) | wireType
            // This matches ProtobufConstants and WireFormatHelpers
            const int WireTypeBitWidth = 3;
            const byte VarintContinuationBit = 0x80;
            const byte VarintValueMask = 0x7F;
            const int VarintShift = 7;
            const int MaxVarint32Size = 5;

            uint tag = (uint)((fieldId << WireTypeBitWidth) | (int)wireType);

            var tagBytes = new byte[MaxVarint32Size];
            int byteCount = 0;

            while (tag >= VarintContinuationBit)
            {
                tagBytes[byteCount++] = (byte)((tag & VarintValueMask) | VarintContinuationBit);
                tag >>= VarintShift;
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
