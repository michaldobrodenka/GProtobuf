namespace GProtobuf.Generator
{
    /// <summary>
    /// Options extracted from [assembly: GProtobufOptions(...)] attribute.
    /// Controls which code generators are enabled.
    /// </summary>
    public sealed record GeneratorOptions
    {
        /// <summary>
        /// Enable generation of SpanReader-based deserialization methods.
        /// </summary>
        public bool GenerateSpanReader { get; init; } = true;

        /// <summary>
        /// Enable generation of StreamReader-based deserialization methods.
        /// </summary>
        public bool GenerateStreamReader { get; init; } = true;

        /// <summary>
        /// Enable generation of StreamWriter-based serialization methods.
        /// </summary>
        public bool GenerateStreamWriter { get; init; } = true;

        /// <summary>
        /// Enable generation of IBufferWriter-based serialization methods.
        /// </summary>
        public bool GenerateBufferWriter { get; init; } = true;

        /// <summary>
        /// Default options with all generators enabled.
        /// </summary>
        public static GeneratorOptions Default { get; } = new();
    }
}
