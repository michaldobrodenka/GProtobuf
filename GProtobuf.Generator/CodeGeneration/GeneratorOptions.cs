namespace GProtobuf.Generator.CodeGeneration
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
        /// Enable generation of OnePassStreamWriter-based serialization methods.
        /// </summary>
        public bool GenerateOnePassStreamWriter { get; init; } = false;

        /// <summary>
        /// Enable generation of StackBufferWriter-based serialization methods.
        /// Zero-allocation serialization for IoT devices with messages &lt;512 bytes.
        /// </summary>
        public bool GenerateStackBufferWriter { get; init; } = true;

        /// <summary>
        /// Enable string pooling for deserialization to reduce allocations.
        /// Uses StringPool for repeated string values (device IDs, sensor types).
        /// </summary>
        public bool UseStringPooling { get; init; } = false;

        /// <summary>
        /// Default options with all generators enabled.
        /// </summary>
        public static GeneratorOptions Default { get; } = new();
    }
}
