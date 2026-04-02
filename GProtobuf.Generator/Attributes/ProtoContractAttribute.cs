using System;

namespace GProtobuf.Generator.Attributes
{
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

        /// <summary>
        /// When true, generates recursion depth guard to protect against stack overflow.
        /// Default is false (no guard generated).
        /// </summary>
        public bool EnableRecursionGuard { get; set; }

        /// <summary>
        /// When true, suppresses generation of public entry-point methods
        /// (Deserialize, Serialize, Populate, Read, Write) for this type.
        /// Internal helper methods (OwnFields, Content, AsParent) are still generated
        /// for use by the parent type's polymorphic dispatch.
        /// Use on derived types that are only accessed via base-type polymorphic dispatch.
        /// Default is false (all methods generated).
        /// </summary>
        public bool SkipEntryPoints { get; set; }
    }
}
