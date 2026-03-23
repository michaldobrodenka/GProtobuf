namespace GProtobuf.Core
{
    /// <summary>
    /// Wire-format constants for BCL types (Guid, DateTime, TimeSpan) serialization.
    /// Defines protobuf-net BCL nested message format tags and sizes.
    /// </summary>
    /// <remarks>
    /// <para><b>BCL Format Overview:</b></para>
    /// protobuf-net encodes .NET BCL types as nested messages with specific field structure:
    /// - Guid: 2 fixed64 fields (lo/hi)
    /// - DateTime/TimeSpan: 2 varint fields (value as sint64, scale as int32)
    ///
    /// <para><b>Tag Encoding:</b></para>
    /// Each tag is pre-encoded as (field_number &lt;&lt; 3) | wire_type:
    /// - 0x08: field 1, WireType.VarInt (0x08 = 1 &lt;&lt; 3 | 0)
    /// - 0x09: field 1, WireType.Fixed64b (0x09 = 1 &lt;&lt; 3 | 1)
    /// - 0x10: field 2, WireType.VarInt (0x10 = 2 &lt;&lt; 3 | 0)
    /// - 0x11: field 2, WireType.Fixed64b (0x11 = 2 &lt;&lt; 3 | 1)
    ///
    /// <para><b>Level200 Compliance:</b></para>
    /// - DateTimeKind is NOT serialized (field 3 omitted)
    /// - Guid.Empty serialized as length=0 (no nested content)
    /// - All nested messages use length-prefix encoding
    ///
    /// <para><b>Performance Note:</b></para>
    /// Pre-encoded tags eliminate runtime tag computation overhead.
    /// Single-byte tags (fields 1-15) enable direct WriteSingleByte() calls.
    /// </remarks>
    public static class BclTypeFormats
    {
        #region Guid Constants

        /// <summary>
        /// Guid wire format: nested message with 2 fixed64 fields (lo/hi).
        /// Total size: 19 bytes (1 length prefix + 18 nested content).
        /// </summary>
        /// <remarks>
        /// Wire layout:
        /// [0x12] (length=18 as varint)
        /// [0x09] (field 1 tag, Fixed64b) [8 bytes lo]
        /// [0x11] (field 2 tag, Fixed64b) [8 bytes hi]
        /// </remarks>
        public static class Guid
        {
            /// <summary>
            /// Field 1 (lo): tag for low 64 bits, WireType.Fixed64b.
            /// Pre-encoded tag: 0x09 = (1 &lt;&lt; 3) | 1.
            /// </summary>
            public const byte FieldLoTag = 0x09;

            /// <summary>
            /// Field 2 (hi): tag for high 64 bits, WireType.Fixed64b.
            /// Pre-encoded tag: 0x11 = (2 &lt;&lt; 3) | 1.
            /// </summary>
            public const byte FieldHiTag = 0x11;

            /// <summary>
            /// Fixed nested message size in bytes.
            /// Breakdown: 1 byte (lo tag) + 8 bytes (lo data) + 1 byte (hi tag) + 8 bytes (hi data) = 18 bytes.
            /// </summary>
            public const int NestedContentSize = 18;

            /// <summary>
            /// Total serialized size including length prefix.
            /// 1 byte (varint length=18) + 18 bytes (nested content) = 19 bytes.
            /// </summary>
            public const int TotalSize = 19;

            /// <summary>
            /// Field number for low 64 bits (used for parsing).
            /// </summary>
            public const int FieldLoNumber = 1;

            /// <summary>
            /// Field number for high 64 bits (used for parsing).
            /// </summary>
            public const int FieldHiNumber = 2;
        }

        #endregion

        #region DateTime / TimeSpan Constants

        /// <summary>
        /// DateTime/TimeSpan wire format: nested message with 2 varint fields (value/scale).
        /// Variable size: depends on magnitude of value and scale.
        /// </summary>
        /// <remarks>
        /// Wire layout:
        /// [varint length]
        /// [0x08] (field 1 tag, VarInt) [varint value as sint64 zigzag]
        /// [0x10] (field 2 tag, VarInt) [varint scale as int32]
        ///
        /// Level200: field 3 (DateTimeKind) is NOT written or read.
        ///
        /// <para><b>Scale Values (TimeSpanScale enum):</b></para>
        /// - Days = 0
        /// - Hours = 1
        /// - Minutes = 2
        /// - Seconds = 3
        /// - Milliseconds = 4
        /// - Ticks = 5 (default if not specified)
        /// - MinMax = 15 (DateTime.MinValue/MaxValue special encoding)
        /// </remarks>
        public static class DateTimeTimeSpan
        {
            /// <summary>
            /// Field 1 (value): tag for scaled ticks/timestamp, WireType.VarInt.
            /// Pre-encoded tag: 0x08 = (1 &lt;&lt; 3) | 0.
            /// Value is ZigZag-encoded sint64 (efficient for negative values).
            /// </summary>
            public const byte FieldValueTag = 0x08;

            /// <summary>
            /// Field 2 (scale): tag for TimeSpanScale enum, WireType.VarInt.
            /// Pre-encoded tag: 0x10 = (2 &lt;&lt; 3) | 0.
            /// Value is regular varint int32.
            /// </summary>
            public const byte FieldScaleTag = 0x10;

            /// <summary>
            /// Field number for value (used for parsing).
            /// </summary>
            public const int FieldValueNumber = 1;

            /// <summary>
            /// Field number for scale (used for parsing).
            /// </summary>
            public const int FieldScaleNumber = 2;

            /// <summary>
            /// Field number for DateTimeKind (field 3).
            /// Level200: This field is NEVER written or read.
            /// Included only for documentation of protobuf-net format.
            /// </summary>
            public const int FieldKindNumber = 3;
        }

        #endregion

        #region Parsing Helper Methods

        /// <summary>
        /// Checks if a field number matches a known BCL type field.
        /// Used during nested message parsing to validate field structure.
        /// </summary>
        /// <param name="fieldNumber">Field number extracted from tag.</param>
        /// <param name="bclType">BCL type being parsed (Guid, DateTime, or TimeSpan).</param>
        /// <returns>True if field number is valid for the BCL type.</returns>
        public static bool IsValidBclField(int fieldNumber, BclType bclType)
        {
            return bclType switch
            {
                BclType.Guid => fieldNumber == Guid.FieldLoNumber || fieldNumber == Guid.FieldHiNumber,
                BclType.DateTime => fieldNumber == DateTimeTimeSpan.FieldValueNumber ||
                                   fieldNumber == DateTimeTimeSpan.FieldScaleNumber,
                BclType.TimeSpan => fieldNumber == DateTimeTimeSpan.FieldValueNumber ||
                                   fieldNumber == DateTimeTimeSpan.FieldScaleNumber,
                _ => false
            };
        }

        #endregion
    }

    /// <summary>
    /// BCL type enumeration for parsing validation.
    /// </summary>
    public enum BclType
    {
        Guid,
        DateTime,
        TimeSpan
    }
}
