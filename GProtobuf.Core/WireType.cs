namespace GProtobuf.Core;

/// <summary>
/// Protocol Buffers wire-format type identifiers.
/// Defines how field values are encoded on the wire according to protobuf specification.
/// These correspond to the 3-bit wire type values in protobuf tags.
/// </summary>
/// <remarks>
/// Wire types are embedded in tags: tag = (field_number << 3) | wire_type.
/// CompatibilityLevel.Level200 compliance: Only supports wire types 0, 1, 2, 5.
/// Legacy wire types 3, 4 (start/end group) are NOT supported.
/// </remarks>
public enum WireType
{
    /// <summary>
    /// Variable-length integer encoding (varint).
    /// Used for: int32, int64, uint32, uint64, sint32 (zigzag), sint64 (zigzag), bool, enum.
    /// Encoding: 7 bits per byte with continuation bit, little-endian.
    /// Range: 1-10 bytes for 64-bit values, 1-5 bytes for 32-bit values.
    /// </summary>
    VarInt = 0,

    /// <summary>
    /// Fixed 64-bit encoding (little-endian).
    /// Used for: fixed64, sfixed64, double.
    /// Encoding: Always exactly 8 bytes, little-endian byte order.
    /// Performance: Faster than varint for large values (> 2^56).
    /// </summary>
    Fixed64b = 1,

    /// <summary>
    /// Length-delimited encoding.
    /// Used for: string, bytes, embedded messages, packed repeated fields.
    /// Format: varint length prefix + N bytes of data.
    /// Level200: Packed repeated primitives MUST use this wire type.
    /// </summary>
    Len = 2,

    /// <summary>
    /// Start of a group (deprecated in proto3, but still in wire format).
    /// Used by protobuf-net for backward compatibility.
    /// </summary>
    StartGroup = 3,

    /// <summary>
    /// End of a group (deprecated in proto3, but still in wire format).
    /// Used by protobuf-net for backward compatibility.
    /// </summary>
    EndGroup = 4,

    /// <summary>
    /// Fixed 32-bit encoding (little-endian).
    /// Used for: fixed32, sfixed32, float.
    /// Encoding: Always exactly 4 bytes, little-endian byte order.
    /// Performance: Faster than varint for values > 2^28.
    /// </summary>
    Fixed32b = 5,
}