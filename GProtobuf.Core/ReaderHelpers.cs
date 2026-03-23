using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace GProtobuf.Core
{
    /// <summary>
    /// Shared helper methods for SpanReaders and StreamReaders.
    /// Contains validation, exception throwing, and common parsing logic.
    /// </summary>
    internal static class ReaderHelpers
    {
        #region Wire Type Validation

        /// <summary>Validates that the actual wire type matches the expected wire type.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateWireType(WireType actual, WireType expected, string typeName)
        {
            if (actual != expected)
                ThrowUnexpectedWireType(actual, typeName);
        }

        /// <summary>Validates wire type is Len.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateWireTypeLen(WireType actual, string typeName)
        {
            if (actual != WireType.Len)
                ThrowUnexpectedWireType(actual, typeName);
        }

        /// <summary>Validates wire type is VarInt.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ValidateWireTypeVarInt(WireType actual, string typeName)
        {
            if (actual != WireType.VarInt)
                ThrowUnexpectedWireType(actual, typeName);
        }

        #endregion

        #region Exception Throwers (NoInlining to keep hot paths small)

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowUnexpectedWireType(WireType wireType, string typeName)
        {
            throw new InvalidOperationException($"Unexpected wire type {wireType} for {typeName}.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static byte ThrowByteOverflow(uint value)
        {
            throw new OverflowException($"Value {value} is out of range for byte.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ushort ThrowUInt16Overflow(uint value)
        {
            throw new OverflowException($"Value {value} is out of range for ushort.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidGuidLength(int actualLength)
        {
            throw new InvalidDataException(
                $"Expected Guid BCL nested message length of {BclTypeFormats.Guid.NestedContentSize} bytes, got {actualLength}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowIncompleteGuid(bool hasLo, bool hasHi)
        {
            throw new InvalidDataException($"Incomplete Guid BCL format: hasLo={hasLo}, hasHi={hasHi}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowGuidFieldWireType(WireType wireType, string fieldName)
        {
            throw new InvalidDataException($"Expected Fixed64 for Guid.{fieldName}, got {wireType}");
        }

        #endregion

        #region Value Conversion with Overflow Check

        /// <summary>Converts uint to byte with overflow check.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ToByte(uint value)
            => value <= byte.MaxValue ? (byte)value : ThrowByteOverflow(value);

        /// <summary>Converts uint to ushort with overflow check.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ToUInt16(uint value)
            => value <= ushort.MaxValue ? (ushort)value : ThrowUInt16Overflow(value);

        #endregion
    }
}
