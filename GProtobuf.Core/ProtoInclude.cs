using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    /// <summary>
    /// Sub-format to use when serializing/deserializing data
    /// </summary>
    public enum DataFormat
    {
        /// <summary>
        /// Uses the default encoding for the data-type.
        /// </summary>
        Default,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that zigzag variant encoding will be used. This means that values
        /// with small magnitude (regardless of sign) take a small amount
        /// of space to encode.
        /// </summary>
        ZigZag,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that two's-complement variant encoding will be used.
        /// This means that any -ve number will take 10 bytes (even for 32-bit),
        /// so should only be used for compatibility.
        /// </summary>
        TwosComplement,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that a fixed amount of space will be used.
        /// </summary>
        FixedSize,

        /// <summary>
        /// When applied to a sub-message, indicates that the value should be treated
        /// as group-delimited.
        /// </summary>
        Group,

        /// <summary>
        /// When applied to members of types such as DateTime or TimeSpan, specifies
        /// that the "well known" standardized representation should be use; DateTime uses Timestamp,
        /// TimeSpan uses Duration.
        /// </summary>
        //[Obsolete("This option is replaced with " + nameof(CompatibilityLevel) + ", and is only used for " + nameof(CompatibilityLevel.Level200) + ", where it changes this field to " + nameof(CompatibilityLevel.Level240), false)]
        WellKnown,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoContractAttribute : Attribute
    {
        public string Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
    public class ProtoIncludeAttribute : Attribute
    {
        public ProtoIncludeAttribute(int fieldId, string type)
        {
            FieldId = fieldId;
            Type = type;
        }

        public int FieldId { get; set; }

        public string Type { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class ProtoMemberAttribute : Attribute
    {
        public ProtoMemberAttribute(int fieldId)
        {
            this.FieldId = fieldId;
        }

        public int FieldId { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public bool IsPacked { get; set; }

        public bool IsRequired { get; set; }

        public DataFormat DataFormat { get; set; }
        public List<string> Interfaces { get; set; }
    }

    #region Custom Buffer Serialization

    /// <summary>
    /// Defines the type of operation for custom buffer serialization.
    /// </summary>
    public enum ProtoBufferOperation
    {
        /// <summary>
        /// Method returns the size of the buffer (int, no parameters).
        /// </summary>
        GetSize,

        /// <summary>
        /// Method writes data to the buffer (void, Span&lt;byte&gt; parameter).
        /// </summary>
        Write,

        /// <summary>
        /// Method reads data from the buffer (void, ReadOnlySpan&lt;byte&gt; parameter).
        /// </summary>
        Read
    }

    /// <summary>
    /// Marks a method as part of custom buffer serialization for a specific field.
    /// </summary>
    /// <remarks>
    /// <para>Custom buffer serialization allows user-defined logic for specific fields.</para>
    /// <para>Three methods are needed for full support:</para>
    /// <list type="bullet">
    ///   <item><description>GetSize - returns int, no parameters</description></item>
    ///   <item><description>Write - returns void, takes Span&lt;byte&gt;</description></item>
    ///   <item><description>Read - returns void, takes ReadOnlySpan&lt;byte&gt; (optional)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoBuffer(10, ProtoBufferOperation.GetSize)]
    /// public int GetCustomDataSize() => _customData?.Length ?? 0;
    ///
    /// [ProtoBuffer(10, ProtoBufferOperation.Write)]
    /// public void WriteCustomData(Span&lt;byte&gt; buffer) => _customData.AsSpan().CopyTo(buffer);
    ///
    /// [ProtoBuffer(10, ProtoBufferOperation.Read)]
    /// public void ReadCustomData(ReadOnlySpan&lt;byte&gt; data) => _customData = data.ToArray();
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoBufferAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified tag and operation.
        /// </summary>
        /// <param name="tag">Unique field ID (tag) for this custom buffer field.</param>
        /// <param name="operation">The type of operation this method performs.</param>
        public ProtoBufferAttribute(int tag, ProtoBufferOperation operation)
        {
            Tag = tag;
            Operation = operation;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// All three operations (GetSize, Write, Read) must use the same tag.
        /// </summary>
        public int Tag { get; }

        /// <summary>
        /// The type of operation this method performs.
        /// </summary>
        public ProtoBufferOperation Operation { get; }
    }

    #endregion

    #region ProtoVarint - Struct/Class as Varint

    /// <summary>
    /// Specifies the varint encoding type for ProtoVarint structs.
    /// </summary>
    public enum ProtoVarintType
    {
        /// <summary>
        /// Unsigned 32-bit integer (varint encoding).
        /// </summary>
        UInt32,

        /// <summary>
        /// Signed 32-bit integer (varint encoding, inefficient for negative numbers).
        /// </summary>
        Int32,

        /// <summary>
        /// Signed 32-bit integer with ZigZag encoding (efficient for negative numbers).
        /// </summary>
        SInt32,

        /// <summary>
        /// Unsigned 64-bit integer (varint encoding).
        /// </summary>
        UInt64,

        /// <summary>
        /// Signed 64-bit integer (varint encoding, inefficient for negative numbers).
        /// </summary>
        Int64,

        /// <summary>
        /// Signed 64-bit integer with ZigZag encoding (efficient for negative numbers).
        /// </summary>
        SInt64
    }

    /// <summary>
    /// Marks a struct or class to be serialized as a varint instead of a nested message.
    /// The type must have a constructor marked with [ProtoVarintConstructor] and
    /// a method/property marked with [ProtoVarintValue].
    /// </summary>
    /// <remarks>
    /// <para>This allows strongly-typed wrappers around numeric values to be serialized
    /// efficiently as varints without the overhead of a nested message.</para>
    /// <para>Wire format is identical to the corresponding primitive type.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoVarint(ProtoVarintType.UInt32)]
    /// public readonly struct KNXAddress
    /// {
    ///     private readonly uint _value;
    ///
    ///     [ProtoVarintConstructor]
    ///     public KNXAddress(uint value) => _value = value;
    ///
    ///     [ProtoVarintValue]
    ///     public uint ToUInt32() => _value;
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified varint type.
        /// </summary>
        /// <param name="type">The varint encoding type. Defaults to UInt32.</param>
        public ProtoVarintAttribute(ProtoVarintType type = ProtoVarintType.UInt32)
        {
            Type = type;
        }

        /// <summary>
        /// The varint encoding type for this struct.
        /// </summary>
        public ProtoVarintType Type { get; }
    }

    /// <summary>
    /// Marks a constructor to be used for deserializing a ProtoVarint type.
    /// The constructor must have exactly one parameter matching the ProtoVarintType.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintConstructorAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a method or property that returns the underlying value for serialization.
    /// The return type must match the ProtoVarintType specified on the struct.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintValueAttribute : Attribute
    {
    }

    #endregion
}
