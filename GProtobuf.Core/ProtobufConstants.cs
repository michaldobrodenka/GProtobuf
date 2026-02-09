namespace GProtobuf.Core
{
    /// <summary>
    /// Protocol Buffers wire-format constants.
    /// Central definition for all magic numbers to eliminate hardcoding and ensure consistency.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Philosophy:</b></para>
    /// - Single source of truth for all protobuf protocol constants
    /// - Self-documenting: each constant explains its purpose and usage
    /// - Type-safe: using const for compile-time values
    /// - Organized by category for easy navigation
    ///
    /// <para><b>CompatibilityLevel.Level200 Compliance:</b></para>
    /// </remarks>
    public static class ProtobufConstants
    {
        #region Varint Encoding

        /// <summary>
        /// Continuation bit mask (0x80 = 0b10000000 = 128).
        /// If set in a varint byte, more bytes follow.
        /// If clear, this is the last byte of the varint.
        /// </summary>
        /// <example>
        /// <code>
        /// while (value >= VarintContinuationBit)
        /// {
        ///     buffer[pos++] = (byte)(value | VarintContinuationBit);
        ///     value >>= VarintShift;
        /// }
        /// buffer[pos++] = (byte)value; // Last byte, no continuation bit
        /// </code>
        /// </example>
        public const byte VarintContinuationBit = 0x80;

        /// <summary>
        /// Value bits mask (0x7F = 0b01111111 = 127).
        /// Lower 7 bits of each varint byte contain payload data.
        /// Upper bit (0x80) is reserved for continuation flag.
        /// </summary>
        /// <remarks>
        /// Used to extract value bits: `value = byte &amp; VarintValueMask`
        /// </remarks>
        public const byte VarintValueMask = 0x7F;

        /// <summary>
        /// Varint shift per byte (7 bits).
        /// Each varint byte encodes 7 bits of value.
        /// Shift count for multi-byte varint encoding: 7, 14, 21, 28, 35, 42, 49, 56, 63.
        /// </summary>
        public const int VarintShift = 7;

        /// <summary>
        /// Maximum varint32 size in bytes (5 bytes for 32 bits).
        /// Calculation: Ceiling(32 / 7) = 5
        /// Covers uint32 range [0, 4,294,967,295].
        /// </summary>
        public const int MaxVarint32Size = 5;

        /// <summary>
        /// Maximum varint64 size in bytes (10 bytes for 64 bits).
        /// Calculation: Ceiling(64 / 7) = 10
        /// Covers uint64 range [0, 18,446,744,073,709,551,615].
        /// </summary>
        public const int MaxVarint64Size = 10;

        #endregion

        #region Performance Thresholds

        /// <summary>
        /// String encoding buffer threshold (256 characters).
        /// Strings &lt; 256 chars use stackalloc (zero heap allocations).
        /// Strings &gt;= 256 chars use ArrayPool.Shared (minimal GC pressure).
        /// </summary>
        /// <remarks>
        /// <para><b>Rationale:</b></para>
        /// - 256 chars * 4 bytes/char (max UTF-8) = 1024 bytes (safe for stack)
        /// - Stack allocation eliminates GC overhead for typical strings (&lt;256 chars)
        /// - ArrayPool provides efficient reuse for large strings (>= 256 chars)
        /// </remarks>
        public const int StringStackAllocThreshold = 256;

        /// <summary>
        /// Minimum buffer size for IBufferWriter (1024 bytes).
        /// BufferWriter requests this size from GetSpan() to batch writes efficiently.
        /// </summary>
        /// <remarks>
        /// Balances GetSpan() call frequency vs memory overhead:
        /// - Smaller buffers: more GetSpan() calls, lower memory usage
        /// - Larger buffers: fewer GetSpan() calls, higher memory usage
        /// 1KB is optimal for typical protobuf messages (averages 1-3 GetSpan calls per message).
        /// </remarks>
        public const int MinWriterBufferSize = 1024;

        #endregion

        #region Fixed Size Encoding

        /// <summary>
        /// Fixed32 wire size (4 bytes).
        /// Used for: float, fixed32, sfixed32.
        /// </summary>
        public const int Fixed32Size = 4;

        /// <summary>
        /// Fixed64 wire size (8 bytes).
        /// Used for: double, fixed64, sfixed64.
        /// </summary>
        public const int Fixed64Size = 8;

        /// <summary>
        /// Fixed16 wire size (2 bytes).
        /// Non-standard extension for fixed16, sfixed16.
        /// </summary>
        public const int Fixed16Size = 2;

        #endregion

        #region Tag Encoding

        /// <summary>
        /// WireType field width in tag (3 bits).
        /// Tag encoding formula: tag = (fieldNumber &lt;&lt; WireTypeBitWidth) | wireType.
        /// Allows field numbers up to 2^29 - 1 = 536,870,911.
        /// </summary>
        public const int WireTypeBitWidth = 3;

        /// <summary>
        /// WireType bitmask (0b111 = 7).
        /// Extract wireType from tag: wireType = (WireType)(tag &amp; WireTypeMask).
        /// Extract fieldNumber from tag: fieldNumber = tag &gt;&gt; WireTypeBitWidth.
        /// </summary>
        public const int WireTypeMask = 0b111;

        #endregion

        #region BCL Type Constants

        /// <summary>
        /// Guid byte size (16 bytes = 128 bits).
        /// Standard System.Guid representation size.
        /// </summary>
        public const int GuidByteSize = 16;

        #endregion
    }
}
