using System;

namespace GProtobuf.Core
{
    /// <summary>
    /// Assembly-level attribute to control which code generators are enabled.
    /// Use this attribute to turn off generation of specific reader/writer types.
    /// </summary>
    /// <example>
    /// <code>
    /// // Disable StreamReader and StreamWriter generation (only use Span-based APIs)
    /// [assembly: GProtobufOptions(GenerateStreamReader = false, GenerateStreamWriter = false)]
    ///
    /// // Disable everything except SpanReader
    /// [assembly: GProtobufOptions(
    ///     GenerateStreamReader = false,
    ///     GenerateStreamWriter = false,
    ///     GenerateBufferWriter = false)]
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public class GProtobufOptionsAttribute : Attribute
    {
        /// <summary>
        /// Enable generation of SpanReader-based deserialization methods.
        /// Default: true
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - Deserialize{Type}(ReadOnlySpan&lt;byte&gt; data) methods
        /// - SpanReaders.Read{Type}(ref SpanReader reader) extension methods
        /// </remarks>
        public bool GenerateSpanReader { get; set; } = true;

        /// <summary>
        /// Enable generation of StreamReader-based deserialization methods.
        /// Default: true
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - Deserialize{Type}(Stream stream) methods
        /// - StreamReaders.Read{Type}(ref StreamReader reader) extension methods
        /// </remarks>
        public bool GenerateStreamReader { get; set; } = true;

        /// <summary>
        /// Enable generation of StreamWriter-based serialization methods.
        /// Default: true
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - Serialize{Type}(Stream stream, T obj) methods
        /// - StreamWriters.Write{Type}(ref StreamWriter writer, T obj) extension methods
        /// </remarks>
        public bool GenerateStreamWriter { get; set; } = true;

        /// <summary>
        /// Enable generation of IBufferWriter-based serialization methods.
        /// Default: true
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - Serialize{Type}(IBufferWriter&lt;byte&gt; buffer, T obj) methods
        /// - BufferWriters.Write{Type}(ref BufferWriter writer, T obj) extension methods
        /// </remarks>
        public bool GenerateBufferWriter { get; set; } = true;
    }
}
