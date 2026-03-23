namespace GProtobuf.Generator.V2
{
    internal static class ReadExpressions
    {
        // Integers
        public const string ReadVarInt32 = "reader.ReadVarInt32()";
        public const string ReadVarInt64 = "reader.ReadVarInt64()";
        public const string ReadFixedSizeInt32 = "reader.ReadFixedSizeInt32()";
        public const string ReadFixedSizeInt64 = "reader.ReadFixedSizeInt64()";
        public const string ReadZigZagVarInt32 = "reader.ReadZigZagVarInt32()";
        public const string ReadZigZagVarInt64 = "reader.ReadZigZagVarInt64()";

        // Unsigned
        public const string ReadVarUInt32 = "reader.ReadVarUInt32()";
        public const string ReadVarUInt64 = "reader.ReadVarUInt64()";
        public const string ReadFixedUInt32 = "reader.ReadFixedUInt32()";
        public const string ReadFixedUInt64 = "reader.ReadFixedUInt64()";

        // Casts
        public const string ReadShortVarInt = "(short)reader.ReadVarInt32()";
        public const string ReadShortZigZag = "(short)reader.ReadZigZagVarInt32()";
        public const string ReadShortFixed = "(short)reader.ReadFixedSizeInt32()";
        public const string ReadUShortVarInt = "(ushort)reader.ReadVarUInt32()";
        public const string ReadUShortFixed = "(ushort)reader.ReadFixedUInt32()";
        public const string ReadByteVarInt = "(byte)reader.ReadVarUInt32()";
        public const string ReadCharVarInt = "(char)reader.ReadVarInt32()";

        // Other
        public const string ReadBool = "reader.ReadBool()";
        public const string ReadFloatWireType = "reader.ReadFloat(wireType)";
        public const string ReadDoubleWireType = "reader.ReadDouble(wireType)";
        public const string ReadStringWireType = "reader.ReadString(wireType)";
        public const string ReadBytesWireType = "reader.ReadBytes(wireType)";
        public const string ReadSByteDefault = "reader.ReadSByte(wireType, false)";
        public const string ReadSByteZigZag = "reader.ReadSByte(wireType, true)";

        // Packed arrays
        public const string ReadPackedVarInt32Array = "reader.ReadPackedVarInt32Array()";
        public const string ReadPackedVarInt64Array = "reader.ReadPackedVarInt64Array()";
        public const string ReadPackedFixedSizeInt32Array = "reader.ReadPackedFixedSizeInt32Array()";
        public const string ReadPackedFixedSizeInt64Array = "reader.ReadPackedFixedSizeInt64Array()";
        public const string ReadPackedZigZagInt32Array = "reader.ReadPackedZigZagInt32Array()";
        public const string ReadPackedZigZagInt64Array = "reader.ReadPackedZigZagInt64Array()";
        public const string ReadPackedVarUInt32Array = "reader.ReadPackedVarUInt32Array()";
        public const string ReadPackedVarUInt64Array = "reader.ReadPackedVarUInt64Array()";
        public const string ReadPackedFloatArray = "reader.ReadPackedFloatArray()";
        public const string ReadPackedDoubleArray = "reader.ReadPackedDoubleArray()";
        public const string ReadPackedBoolArray = "reader.ReadPackedBoolArray()";
    }
}
