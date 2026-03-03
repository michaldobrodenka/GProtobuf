using System;

namespace GProtobuf.Generator.Attributes
{
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
}
