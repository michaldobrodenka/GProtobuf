using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Generator
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

    /// <summary>
    /// Represents the kind of collection type for serialization
    /// </summary>
    public enum CollectionKind
    {
        /// <summary>
        /// Not a collection type
        /// </summary>
        None,

        /// <summary>
        /// Array type (T[]) - deserialize to List&lt;T&gt; then convert to array
        /// </summary>
        Array,

        /// <summary>
        /// Interface collection type (ICollection&lt;T&gt;, IList&lt;T&gt;, IEnumerable&lt;T&gt;) - deserialize to List&lt;T&gt;
        /// </summary>
        InterfaceCollection,

        /// <summary>
        /// Concrete collection type (List&lt;T&gt;, MyCollection&lt;T&gt;) - deserialize to actual type
        /// </summary>
        ConcreteCollection,

        /// <summary>
        /// Custom collection type (non-generic class implementing ICollection&lt;T&gt; + Add method)
        /// Used for protobuf-net compatible collections without [ProtoContract]
        /// </summary>
        CustomCollection,

        /// <summary>
        /// Custom enumerable type (non-generic class implementing IEnumerable&lt;T&gt; + Add method)
        /// Used for protobuf-net compatible collections without [ProtoContract]
        /// </summary>
        CustomEnumerable
    }

    /// <summary>
    /// Marks a type as protobuf-serializable, enabling source code generation for serialization/deserialization.
    /// Applied to classes, structs, enums, or interfaces at compile-time.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// - Classes/Structs: Generates Deserialize{ClassName}, Read{ClassName}, Write{ClassName} methods
    /// - Enums: Generates varint encoding/decoding for enum values
    /// - Interfaces: Marks interface for ProtoInclude polymorphism (base type tracking)
    ///
    /// <para><b>Requirements:</b></para>
    /// - Type must have [ProtoContract] OR [ProtoInclude] (detected by SerializerGenerator)
    /// - Members must be marked with [ProtoMember(fieldId)] to be serialized
    /// - Field IDs must be unique within type hierarchy
    ///
    /// <para><b>CompatibilityLevel.Level200:</b></para>
    /// - Default values are NOT serialized on wire
    /// - Unknown fields are skipped (not preserved for round-tripping)
    /// - Packed encoding for repeated primitives
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoContractAttribute : Attribute
    {
        /// <summary>
        /// Optional custom name for the type (not currently used in code generation).
        /// Reserved for future schema generation or debugging purposes.
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// Declares a derived type in a protobuf inheritance hierarchy (polymorphism support).
    /// Applied to base class/interface to specify derived types and their field IDs.
    /// Multiple [ProtoInclude] attributes can be applied to support multiple derived types.
    /// </summary>
    /// <remarks>
    /// <para><b>Wire Format (Level200):</b></para>
    /// Base class fields serialize first, followed by a length-delimited wrapper for derived type:
    /// <code>
    /// [base field 1][base field 2]...[fieldId: derived wrapper][length][derived field 1][derived field 2]...
    /// </code>
    ///
    /// <para><b>Deserialization Strategy:</b></para>
    /// - Reads base class fields first
    /// - Peeks at next tag to detect ProtoInclude wrapper field ID
    /// - If wrapper detected: Reads length prefix, deserializes derived type content
    /// - If no wrapper: Creates base class instance
    ///
    /// <para><b>Requirements:</b></para>
    /// - Field IDs must NOT conflict with base class [ProtoMember] field IDs
    /// - Field IDs must be unique across all [ProtoInclude] in same type
    /// - Derived types should have [ProtoContract] (optional but recommended)
    ///
    /// <para><b>Example:</b></para>
    /// <code>
    /// [ProtoContract]
    /// [ProtoInclude(10, typeof(Dog))]
    /// [ProtoInclude(11, typeof(Cat))]
    /// public abstract class Animal { }
    /// </code>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
    public class ProtoIncludeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new ProtoIncludeAttribute with field ID, type name, and namespace.
        /// Called by SerializerGenerator during Roslyn analysis phase.
        /// </summary>
        /// <param name="fieldId">Field ID for the derived type wrapper (must be unique in hierarchy).</param>
        /// <param name="type">Fully qualified type name of derived type.</param>
        /// <param name="nmspace">Namespace of derived type.</param>
        public ProtoIncludeAttribute(int fieldId, string type, string nmspace)
        {
            FieldId = fieldId;
            Type = type;
            this.Namespace = nmspace;
        }

        /// <summary>
        /// Field ID for the length-delimited wrapper containing derived type content.
        /// Must NOT conflict with base class [ProtoMember] field IDs.
        /// </summary>
        public int FieldId { get; set; }

        /// <summary>
        /// Namespace of the derived type (used for code generation and type resolution).
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Fully qualified type name of the derived type.
        /// </summary>
        public string Type { get; set; }
    }

    /// <summary>
    /// Marks a field or property for protobuf serialization, assigning a unique field ID (tag).
    /// Applied to members at compile-time to include them in generated serialization code.
    /// </summary>
    /// <remarks>
    /// <para><b>Field ID Requirements:</b></para>
    /// - Must be unique within type (and base types if using inheritance)
    /// - Range: 1-536870911 (field 0 reserved, 19000-19999 reserved by protobuf spec)
    /// - Lower field IDs (1-15) use 1-byte tags (more efficient)
    /// - Field IDs 16-2047 use 2-byte tags
    ///
    /// <para><b>Serialization Behavior (Level200):</b></para>
    /// - Default values: NOT serialized on wire (0, null, empty string, etc.)
    /// - Repeated fields (collections): MUST use packed encoding for primitives
    /// - Unknown fields: Skipped during deserialization (forward compatibility)
    /// - Field order: Can appear in any order on wire (last value wins for non-repeated)
    ///
    /// <para><b>Property vs Field:</b></para>
    /// - Properties: Must have setter (get-only properties ignored)
    /// - Fields: Can be readonly (for struct initialization via constructor)
    ///
    /// <para><b>Supported Types:</b></para>
    /// - Primitives: int, long, bool, string, byte[], Guid, DateTime, TimeSpan, etc.
    /// - Collections: T[], List&lt;T&gt;, ICollection&lt;T&gt;, custom IEnumerable&lt;T&gt; + Add method
    /// - Maps: Dictionary&lt;K,V&gt;, IDictionary&lt;K,V&gt;, ICollection&lt;KeyValuePair&lt;K,V&gt;&gt;
    /// - Messages: Other [ProtoContract] types (nested messages)
    /// - Enums: Serialized as varint
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class ProtoMemberAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new ProtoMemberAttribute with required field ID.
        /// </summary>
        /// <param name="fieldId">
        /// Unique field ID (tag) for this member within the type hierarchy.
        /// Range: 1-536870911 (avoid 19000-19999).
        /// </param>
        public ProtoMemberAttribute(int fieldId)
        {
            this.FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this member. Encoded in wire format as (fieldId &lt;&lt; 3) | wireType.
        /// </summary>
        public int FieldId { get; set; }

        /// <summary>
        /// Fully qualified type name of the member (populated by SerializerGenerator during analysis).
        /// Example: "System.Int32", "System.Collections.Generic.List&lt;string&gt;".
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Namespace of the containing type (populated by SerializerGenerator during analysis).
        /// Used for code generation and type resolution.
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Name of the property or field (populated by SerializerGenerator during analysis).
        /// Used in generated code for property access (e.g., "obj.PropertyName").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// For repeated fields (collections): Use packed encoding (Length-delimited with contiguous values).
        /// Level200: Packed encoding is REQUIRED for repeated primitives (int, long, bool, etc.).
        /// Ignored for non-repeated fields and non-primitive collections.
        /// </summary>
        public bool IsPacked { get; set; }

        /// <summary>
        /// Marks field as required (validation only, not enforced in Level200 wire format).
        /// If true, deserializer may throw if field is missing (implementation-specific).
        /// Level200: Optional feature, typically not enforced for backwards compatibility.
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// Specifies wire encoding format for numeric types.
        /// See <see cref="DataFormat"/> enum for options (Default, ZigZag, FixedSize, etc.).
        /// </summary>
        public DataFormat DataFormat { get; set; }

        /// <summary>
        /// List of interfaces implemented by the member's type (populated during analysis).
        /// Used to detect ICollection&lt;T&gt;, IDictionary&lt;K,V&gt;, etc. for code generation.
        /// </summary>
        public List<string> Interfaces { get; set; }

        /// <summary>
        /// True if member is a nullable value type (e.g., int?, Guid?, DateTime?).
        /// Does NOT apply to reference types (which are always nullable).
        /// Used to generate null-checking logic in serialization code.
        /// </summary>
        public bool IsNullable { get; set; }
        
        /// <summary>
        /// Indicates if this member is a collection type
        /// </summary>
        public bool IsCollection { get; set; }
        
        /// <summary>
        /// The element type of the collection (e.g., "int" for List&lt;int&gt;)
        /// </summary>
        public string CollectionElementType { get; set; }
        
        /// <summary>
        /// The kind of collection type
        /// </summary>
        public CollectionKind CollectionKind { get; set; }
        
        /// <summary>
        /// Indicates if this member is a map/dictionary type (ICollection<KeyValuePair<K,V>>, Dictionary<K,V>, etc.)
        /// </summary>
        public bool IsMap { get; set; }
        
        /// <summary>
        /// The key type for map/dictionary types (e.g., "string" for Dictionary<string, int>)
        /// </summary>
        public string MapKeyType { get; set; }
        
        /// <summary>
        /// The value type for map/dictionary types (e.g., "int" for Dictionary<string, int>)
        /// </summary>
        public string MapValueType { get; set; }
        
        /// <summary>
        /// Indicates if this member is an enum type
        /// </summary>
        public bool IsEnum { get; set; }
        
        /// <summary>
        /// The underlying type of the enum (e.g., "System.Int32")
        /// </summary>
        public string EnumUnderlyingType { get; set; }
        
        /// <summary>
        /// Indicates if the map key type is an enum
        /// </summary>
        public bool MapKeyIsEnum { get; set; }
        
        /// <summary>
        /// The underlying type of the map key enum
        /// </summary>
        public string MapKeyEnumUnderlyingType { get; set; }
        
        /// <summary>
        /// Indicates if the map value type is an enum
        /// </summary>
        public bool MapValueIsEnum { get; set; }
        
        /// <summary>
        /// The underlying type of the map value enum
        /// </summary>
        public string MapValueEnumUnderlyingType { get; set; }
    }

    /// <summary>
    /// Marks a method that returns the serialized size of a custom buffer field.
    /// Used together with <see cref="ProtoMemberBufferFillAttribute"/> and optionally <see cref="ProtoMemberBufferReadAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Method Signature:</b></para>
    /// The method must return int and take no parameters:
    /// <code>
    /// [ProtoMemberBufferSize(7)]
    /// public int GetCustomFieldSize() => _data?.Length ?? 0;
    /// </code>
    ///
    /// <para><b>Usage:</b></para>
    /// - Field ID must be unique within the type (same rules as ProtoMember)
    /// - Must have corresponding ProtoMemberBufferFill method with same field ID
    /// - Optionally have ProtoMemberBufferRead method for deserialization
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferSizeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new ProtoMemberBufferSizeAttribute with the specified field ID.
        /// </summary>
        /// <param name="fieldId">Unique field ID (tag) for this custom buffer field.</param>
        public ProtoMemberBufferSizeAttribute(int fieldId)
        {
            FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// Must match the field ID used in corresponding ProtoMemberBufferFill and ProtoMemberBufferRead attributes.
        /// </summary>
        public int FieldId { get; }
    }

    /// <summary>
    /// Marks a method that fills a buffer with serialized data for a custom buffer field.
    /// Used together with <see cref="ProtoMemberBufferSizeAttribute"/> and optionally <see cref="ProtoMemberBufferReadAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Method Signature:</b></para>
    /// The method must return void and accept Span&lt;byte&gt;:
    /// <code>
    /// [ProtoMemberBufferFill(7)]
    /// public void FillCustomField(Span&lt;byte&gt; buffer)
    /// {
    ///     _data.CopyTo(buffer);
    /// }
    /// </code>
    ///
    /// <para><b>Usage:</b></para>
    /// - Field ID must match the corresponding ProtoMemberBufferSize method
    /// - The buffer size will be exactly what GetSize method returned
    /// - Must write exactly that many bytes to the buffer
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferFillAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new ProtoMemberBufferFillAttribute with the specified field ID.
        /// </summary>
        /// <param name="fieldId">Unique field ID (tag) for this custom buffer field.</param>
        public ProtoMemberBufferFillAttribute(int fieldId)
        {
            FieldId = fieldId;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// Must match the field ID used in corresponding ProtoMemberBufferSize and ProtoMemberBufferRead attributes.
        /// </summary>
        public int FieldId { get; }
    }

    /// <summary>
    /// Marks a method that reads serialized data from a buffer for a custom buffer field.
    /// Used together with <see cref="ProtoMemberBufferSizeAttribute"/> and <see cref="ProtoMemberBufferFillAttribute"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Method Signature:</b></para>
    /// The method must return void and accept ReadOnlySpan&lt;byte&gt;:
    /// <code>
    /// [ProtoMemberBufferRead(7)]
    /// public void ReadCustomField(ReadOnlySpan&lt;byte&gt; data)
    /// {
    ///     _data = data.ToArray();
    /// }
    /// </code>
    ///
    /// <para><b>Usage:</b></para>
    /// - Field ID must match the corresponding ProtoMemberBufferSize and ProtoMemberBufferFill methods
    /// - The data span contains exactly the bytes that were written by FillCustomField
    /// - If not provided, the field will be write-only (serialization only)
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoMemberBufferReadAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new ProtoMemberBufferReadAttribute with the specified field ID.
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

    /// <summary>
    /// Represents a custom buffer member with associated size, fill, and read methods.
    /// Created during source generation by analyzing methods with ProtoMemberBuffer* attributes.
    /// </summary>
    public sealed class CustomBufferMember
    {
        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// </summary>
        public int FieldId { get; set; }

        /// <summary>
        /// Name of the method marked with [ProtoMemberBufferSize].
        /// Returns int, takes no parameters.
        /// </summary>
        public string SizeMethodName { get; set; }

        /// <summary>
        /// Name of the method marked with [ProtoMemberBufferFill].
        /// Returns void, takes Span&lt;byte&gt;.
        /// </summary>
        public string FillMethodName { get; set; }

        /// <summary>
        /// Name of the method marked with [ProtoMemberBufferRead].
        /// Returns void, takes ReadOnlySpan&lt;byte&gt;.
        /// May be null if only serialization is supported.
        /// </summary>
        public string ReadMethodName { get; set; }

        /// <summary>
        /// Indicates whether deserialization is supported (ReadMethodName is not null).
        /// </summary>
        public bool SupportsDeserialization => !string.IsNullOrEmpty(ReadMethodName);
    }
}
