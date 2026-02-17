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

    #region Custom Buffer Serialization Attributes

    /// <summary>
    /// Marks a method as the size calculator for a custom buffer field.
    /// The method must return int and take no parameters.
    /// </summary>
    /// <remarks>
    /// Used in conjunction with <see cref="ProtoMemberBufferFillAttribute"/> and optionally
    /// <see cref="ProtoMemberBufferReadAttribute"/> to define custom serialization logic.
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoMemberBufferSize(10)]
    /// public int GetCustomDataSize() => _customData?.Length ?? 0;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferSizeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified field ID.
        /// </summary>
        /// <param name="fieldId">Unique field ID (tag) for this custom buffer field.</param>
        public ProtoMemberBufferSizeAttribute(int fieldId)
        {
            FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// </summary>
        public int FieldId { get; }
    }

    /// <summary>
    /// Marks a method as the buffer filler for a custom buffer field.
    /// The method must return void and take a Span&lt;byte&gt; parameter.
    /// </summary>
    /// <remarks>
    /// Used in conjunction with <see cref="ProtoMemberBufferSizeAttribute"/> and optionally
    /// <see cref="ProtoMemberBufferReadAttribute"/> to define custom serialization logic.
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoMemberBufferFill(10)]
    /// public void FillCustomData(Span&lt;byte&gt; buffer)
    /// {
    ///     _customData.AsSpan().CopyTo(buffer);
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferFillAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified field ID.
        /// </summary>
        /// <param name="fieldId">Unique field ID (tag) for this custom buffer field.</param>
        public ProtoMemberBufferFillAttribute(int fieldId)
        {
            FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// Must match the field ID used in corresponding ProtoMemberBufferSize attribute.
        /// </summary>
        public int FieldId { get; }
    }

    /// <summary>
    /// Marks a method as the buffer reader for a custom buffer field.
    /// The method must return void and take a ReadOnlySpan&lt;byte&gt; parameter.
    /// </summary>
    /// <remarks>
    /// Used in conjunction with <see cref="ProtoMemberBufferSizeAttribute"/> and
    /// <see cref="ProtoMemberBufferFillAttribute"/> to define custom deserialization logic.
    /// This attribute is optional - if not provided, the field can only be serialized, not deserialized.
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoMemberBufferRead(10)]
    /// public void ReadCustomData(ReadOnlySpan&lt;byte&gt; data)
    /// {
    ///     _customData = data.ToArray();
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferReadAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified field ID.
        /// </summary>
        /// <param name="fieldId">Unique field ID (tag) for this custom buffer field.</param>
        public ProtoMemberBufferReadAttribute(int fieldId)
        {
            FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// Must match the field ID used in corresponding ProtoMemberBufferSize and ProtoMemberBufferFill attributes.
        /// </summary>
        public int FieldId { get; }
    }

    #endregion
}
