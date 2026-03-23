using System;
using System.Collections.Generic;

namespace GProtobuf.Generator.Attributes
{
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
        /// Indicates if this member is a map/dictionary type (ICollection&lt;KeyValuePair&lt;K,V&gt;&gt;, Dictionary&lt;K,V&gt;, etc.)
        /// </summary>
        public bool IsMap { get; set; }

        /// <summary>
        /// The key type for map/dictionary types (e.g., "string" for Dictionary&lt;string, int&gt;)
        /// </summary>
        public string MapKeyType { get; set; }

        /// <summary>
        /// The value type for map/dictionary types (e.g., "int" for Dictionary&lt;string, int&gt;)
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

        /// <summary>
        /// Indicates if this member's type is marked with [ProtoVarint]
        /// </summary>
        public bool IsProtoVarint { get; set; }

        /// <summary>
        /// The varint encoding type (UInt32, Int32, SInt32, etc.) for ProtoVarint types
        /// </summary>
        public ProtoVarintType ProtoVarintType { get; set; }

        /// <summary>
        /// Name of the method/property marked with [ProtoVarintValue] for serialization
        /// </summary>
        public string ProtoVarintValueMember { get; set; }

        /// <summary>
        /// True if ProtoVarintValueMember is a property, false if method
        /// </summary>
        public bool ProtoVarintValueIsProperty { get; set; }
    }
}
