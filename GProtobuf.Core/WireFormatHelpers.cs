using System.Runtime.CompilerServices;

namespace GProtobuf.Core
{
    /// <summary>
    /// Central utility class for Protocol Buffers wire-format operations.
    /// Provides canonical implementations for ZigZag encoding/decoding, tag encoding/decoding,
    /// and varint size calculations to eliminate code duplication across Core and Generator.
    /// </summary>
    /// <remarks>
    /// <para><b>Design Philosophy:</b></para>
    /// - Single source of truth for wire-format primitives
    /// - All methods are AggressiveInlining for zero overhead
    /// - Algorithms chosen for optimal JIT codegen (threshold-based for branching predictability)
    /// - Level200 compliance: Supports only wire types 0, 1, 2, 5
    ///
    /// <para><b>Performance Characteristics:</b></para>
    /// - ZigZag encode/decode: 2-3 CPU instructions (bitwise operations)
    /// - Tag encode/decode: 1-2 CPU instructions (shifts + masks)
    /// - Varint size calculation: Threshold-based branches (JIT-optimized, ~5-10ns)
    ///
    /// <para><b>Usage:</b></para>
    /// Used by BufferWriter, StreamWriter, SpanReader, WriteSizeCalculator to eliminate
    /// duplicate implementations of these core primitives.
    /// </remarks>
    public static class WireFormatHelpers
    {
        #region ZigZag Encoding/Decoding

        /// <summary>
        /// Encodes a signed 32-bit integer using ZigZag encoding.
        /// ZigZag maps signed integers to unsigned for efficient varint encoding:
        /// 0 => 0, -1 => 1, 1 => 2, -2 => 3, 2 => 4, etc.
        /// </summary>
        /// <param name="value">Signed integer to encode.</param>
        /// <returns>ZigZag-encoded unsigned integer.</returns>
        /// <remarks>
        /// <para><b>Algorithm:</b></para>
        /// Formula: (value &lt;&lt; 1) ^ (value &gt;&gt; 31)
        /// - Left shift by 1: Doubles the absolute value
        /// - Arithmetic right shift by 31: Creates all-1s mask for negatives, all-0s for positives
        /// - XOR: Inverts bits for negative values, preserves for positives
        ///
        /// <para><b>Examples:</b></para>
        /// - EncodeZigZag32(0)   = 0x00000000 (0)
        /// - EncodeZigZag32(-1)  = 0x00000001 (1)
        /// - EncodeZigZag32(1)   = 0x00000002 (2)
        /// - EncodeZigZag32(-2)  = 0x00000003 (3)
        /// - EncodeZigZag32(127) = 0x000000FE (254)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeZigZag32(int value)
        {
            return (uint)((value << 1) ^ (value >> 31));
        }

        /// <summary>
        /// Encodes a signed 64-bit integer using ZigZag encoding.
        /// Same algorithm as 32-bit version but for long values.
        /// </summary>
        /// <param name="value">Signed long to encode.</param>
        /// <returns>ZigZag-encoded unsigned long.</returns>
        /// <remarks>
        /// Formula: (value &lt;&lt; 1) ^ (value &gt;&gt; 63)
        /// Arithmetic right shift by 63 creates all-1s mask for negatives.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong EncodeZigZag64(long value)
        {
            return (ulong)((value << 1) ^ (value >> 63));
        }

        /// <summary>
        /// Decodes a ZigZag-encoded unsigned 32-bit integer back to signed form.
        /// </summary>
        /// <param name="encoded">ZigZag-encoded unsigned integer.</param>
        /// <returns>Original signed integer value.</returns>
        /// <remarks>
        /// <para><b>Algorithm:</b></para>
        /// Formula: (encoded &gt;&gt; 1) ^ (0 - (encoded &amp; 1))
        /// - Right shift by 1: Restores doubled value
        /// - (encoded &amp; 1): Extracts sign bit (0 for positive, 1 for negative)
        /// - (0 - bit): Creates all-0s mask for positives, all-1s for negatives
        /// - XOR: Restores original sign via two's complement inversion
        ///
        /// <para><b>Examples:</b></para>
        /// - DecodeZigZag32(0) = 0
        /// - DecodeZigZag32(1) = -1
        /// - DecodeZigZag32(2) = 1
        /// - DecodeZigZag32(3) = -2
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DecodeZigZag32(uint encoded)
        {
            return (int)((encoded >> 1) ^ (0U - (encoded & 1)));
        }

        /// <summary>
        /// Decodes a ZigZag-encoded unsigned 64-bit integer back to signed form.
        /// Same algorithm as 32-bit version but for ulong values.
        /// </summary>
        /// <param name="encoded">ZigZag-encoded unsigned long.</param>
        /// <returns>Original signed long value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long DecodeZigZag64(ulong encoded)
        {
            return (long)((encoded >> 1) ^ (0UL - (encoded & 1)));
        }

        #endregion

        #region Tag Encoding/Decoding

        /// <summary>
        /// Encodes a field number and wire type into a protobuf tag.
        /// Tags are varint-encoded as: tag = (field_number &lt;&lt; 3) | wire_type.
        /// </summary>
        /// <param name="fieldNumber">Field number (1-536870911). Field 0 is reserved.</param>
        /// <param name="wireType">Wire type (0, 1, 2, or 5 for Level200).</param>
        /// <returns>Encoded tag value ready for varint serialization.</returns>
        /// <remarks>
        /// <para><b>Tag Structure:</b></para>
        /// - Lower 3 bits: Wire type (0-7, but Level200 only uses 0, 1, 2, 5)
        /// - Upper 29 bits: Field number (1 to 2^29-1 = 536,870,911)
        ///
        /// <para><b>Examples:</b></para>
        /// - EncodeTag(1, VarInt)   = 0x08 (field 1, wire type 0)
        /// - EncodeTag(1, Len)      = 0x0A (field 1, wire type 2)
        /// - EncodeTag(15, Fixed64) = 0x79 (field 15, wire type 1)
        /// - EncodeTag(16, VarInt)  = 0x80 0x01 (requires 2-byte varint)
        ///
        /// <para><b>Performance Note:</b></para>
        /// Field numbers 1-15 with wire types 0-5 fit in single-byte varints (tag &lt; 128).
        /// Generator can precompute these as byte literals for optimal codegen.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeTag(int fieldNumber, WireType wireType)
        {
            return (uint)((fieldNumber << ProtobufConstants.WireTypeBitWidth) | (int)wireType);
        }

        /// <summary>
        /// Decodes a protobuf tag into field number and wire type.
        /// Inverse operation of EncodeTag.
        /// </summary>
        /// <param name="tag">Encoded tag value (from ReadVarInt32).</param>
        /// <returns>Tuple containing (fieldNumber, wireType).</returns>
        /// <remarks>
        /// Extraction formulas:
        /// - wireType   = tag &amp; 0b111 (lower 3 bits)
        /// - fieldNumber = tag &gt;&gt; 3   (upper 29 bits)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (int fieldNumber, WireType wireType) DecodeTag(uint tag)
        {
            int fieldNumber = (int)(tag >> ProtobufConstants.WireTypeBitWidth);
            WireType wireType = (WireType)(tag & ProtobufConstants.WireTypeMask);
            return (fieldNumber, wireType);
        }

        /// <summary>
        /// Decodes a protobuf tag using out parameters (zero-allocation version).
        /// Preferred in hot paths to avoid tuple allocation overhead.
        /// </summary>
        /// <param name="tag">Encoded tag value.</param>
        /// <param name="fieldNumber">Output: decoded field number.</param>
        /// <param name="wireType">Output: decoded wire type.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeTag(uint tag, out int fieldNumber, out WireType wireType)
        {
            fieldNumber = (int)(tag >> ProtobufConstants.WireTypeBitWidth);
            wireType = (WireType)(tag & ProtobufConstants.WireTypeMask);
        }

        #endregion

        #region Varint Size Calculation (Threshold-Based)

        /// <summary>
        /// Calculates the varint-encoded size in bytes for an unsigned 32-bit integer.
        /// Uses threshold-based algorithm optimized for JIT branch prediction.
        /// </summary>
        /// <param name="value">Unsigned integer value.</param>
        /// <returns>Size in bytes (1-5).</returns>
        /// <remarks>
        /// <para><b>Algorithm: Threshold-Based Branching</b></para>
        /// Chosen over bit-operations approach for better JIT codegen and branch prediction.
        /// Modern CPUs handle predictable branches faster than bit manipulation for small values.
        ///
        /// <para><b>Size Thresholds (uint32):</b></para>
        /// - [0, 127]:                     1 byte  (7 bits)
        /// - [128, 16383]:                 2 bytes (14 bits)
        /// - [16384, 2097151]:             3 bytes (21 bits)
        /// - [2097152, 268435455]:         4 bytes (28 bits)
        /// - [268435456, 4294967295]:      5 bytes (32 bits)
        ///
        /// <para><b>Why Threshold-Based?</b></para>
        /// 1. Branch predictor friendly: Sequential thresholds are predictable
        /// 2. JIT optimization: Can inline comparisons as constant checks
        /// 3. Distribution: 90%+ of protobuf varints are 1-2 bytes (small values)
        /// 4. Clarity: Easier to understand and verify correctness
        ///
        /// <para><b>Alternative Algorithms Considered:</b></para>
        /// - Bit-operations: (32 - LeadingZeroCount(value) + 6) / 7
        ///   Pros: Branchless, uniform performance
        ///   Cons: Slower for small values, LeadingZeroCount not always hardware-accelerated
        /// - Loop-based: while (value >= 0x80) { size++; value >>= 7; }
        ///   Pros: Minimal code size
        ///   Cons: Unpredictable branch count, worst performance
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintSize(uint value)
        {
            // Thresholds calculated as: 1 << (VarintShift * byteCount)
            if (value < (1u << ProtobufConstants.VarintShift))  return 1; // 128
            if (value < (1u << (ProtobufConstants.VarintShift * 2))) return 2; // 16,384
            if (value < (1u << (ProtobufConstants.VarintShift * 3))) return 3; // 2,097,152
            if (value < (1u << (ProtobufConstants.VarintShift * 4))) return 4; // 268,435,456
            return ProtobufConstants.MaxVarint32Size;
        }

        /// <summary>
        /// Calculates the varint-encoded size in bytes for an unsigned 64-bit integer.
        /// Uses threshold-based algorithm (same rationale as 32-bit version).
        /// </summary>
        /// <param name="value">Unsigned long value.</param>
        /// <returns>Size in bytes (1-10).</returns>
        /// <remarks>
        /// <para><b>Size Thresholds (uint64):</b></para>
        /// - 1 byte:  [0, 127]                         (7 bits)
        /// - 2 bytes: [128, 16383]                     (14 bits)
        /// - 3 bytes: [16384, 2097151]                 (21 bits)
        /// - 4 bytes: [2097152, 268435455]             (28 bits)
        /// - 5 bytes: [268435456, 34359738367]         (35 bits)
        /// - 6 bytes: [34359738368, 4398046511103]     (42 bits)
        /// - 7 bytes: [4398046511104, 562949953421311] (49 bits)
        /// - 8 bytes: [562949953421312, ...]           (56 bits)
        /// - 9 bytes: [2^56, 2^63-1]                   (63 bits)
        /// - 10 bytes: [2^63, 2^64-1]                  (64 bits)
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetVarintSize(ulong value)
        {
            if (value < (1UL << ProtobufConstants.VarintShift))  return 1;
            if (value < (1UL << (ProtobufConstants.VarintShift * 2))) return 2;
            if (value < (1UL << (ProtobufConstants.VarintShift * 3))) return 3;
            if (value < (1UL << (ProtobufConstants.VarintShift * 4))) return 4;
            if (value < (1UL << (ProtobufConstants.VarintShift * 5))) return 5;
            if (value < (1UL << (ProtobufConstants.VarintShift * 6))) return 6;
            if (value < (1UL << (ProtobufConstants.VarintShift * 7))) return 7;
            if (value < (1UL << (ProtobufConstants.VarintShift * 8))) return 8;
            if (value < (1UL << (ProtobufConstants.VarintShift * 9))) return 9;
            return ProtobufConstants.MaxVarint64Size;
        }

        /// <summary>
        /// Calculates the varint-encoded size for a ZigZag-encoded signed 32-bit integer.
        /// Combines ZigZag encoding with varint size calculation in one step.
        /// </summary>
        /// <param name="value">Signed integer value.</param>
        /// <returns>Size in bytes after ZigZag encoding and varint serialization (1-5).</returns>
        /// <remarks>
        /// Equivalent to: GetVarintSize(EncodeZigZag32(value))
        /// Inlined for performance - avoids intermediate variable allocation.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetZigZagVarintSize(int value)
        {
            uint encoded = EncodeZigZag32(value);
            return GetVarintSize(encoded);
        }

        /// <summary>
        /// Calculates the varint-encoded size for a ZigZag-encoded signed 64-bit integer.
        /// </summary>
        /// <param name="value">Signed long value.</param>
        /// <returns>Size in bytes after ZigZag encoding and varint serialization (1-10).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetZigZagVarintSize(long value)
        {
            ulong encoded = EncodeZigZag64(value);
            return GetVarintSize(encoded);
        }

        #endregion
    }
}
