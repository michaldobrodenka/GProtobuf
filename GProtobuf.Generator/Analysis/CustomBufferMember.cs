namespace GProtobuf.Generator.Analysis
{
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
