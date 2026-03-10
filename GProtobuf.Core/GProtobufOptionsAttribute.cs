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

        /// <summary>
        /// Enable generation of OnePassStreamWriter-based serialization methods.
        /// Default: false
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - Serialize{Type}OnePass(Stream stream, T obj) methods
        /// - OnePassStreamWriters.Write{Type}(ref OnePassStreamWriter writer, T obj) extension methods
        ///
        /// OnePassStreamWriter uses a different approach than StreamWriter:
        /// - StreamWriter (2-pass): calculates size first, then writes data
        /// - OnePassStreamWriter (1-pass): uses memory pooling for nested messages (BeginSubMessage/EndSubMessage)
        /// </remarks>
        public bool GenerateOnePassStreamWriter { get; set; } = false;

        /// <summary>
        /// Enable generation of StackBufferWriter-based serialization methods.
        /// Default: true
        /// </summary>
        /// <remarks>
        /// When enabled, generates:
        /// - SerializeTo{Type}(Span&lt;byte&gt; buffer, T obj) methods - zero-allocation
        /// - SerializeToArray{Type}(T obj) methods - uses stackalloc for small messages
        /// - StackBufferWriters.Write{Type}(ref StackBufferWriter writer, T obj) extension methods
        ///
        /// IoT Optimization:
        /// - Zero heap allocations for messages &lt;512 bytes
        /// - Writes directly to Span&lt;byte&gt;
        /// - Ideal for battery-powered devices
        /// - Deterministic timing (no GC pauses)
        /// </remarks>
        public bool GenerateStackBufferWriter { get; set; } = true;

        /// <summary>
        /// Enable string pooling for deserialization to reduce allocations.
        /// Default: false
        /// </summary>
        /// <remarks>
        /// When enabled, uses StringPool for string field deserialization.
        /// Reduces allocations by 60-85% for repeated string values (device IDs, sensor types, status codes).
        ///
        /// Best for:
        /// - IoT telemetry with limited value sets
        /// - High-frequency messages with repeated strings
        ///
        /// Not recommended for:
        /// - Unique/random strings (UUIDs, timestamps)
        /// - Short-lived processes (pool never warms up)
        /// </remarks>
        public bool UseStringPooling { get; set; } = false;
    }
}
