using GProtobuf.Generator.V2.Handlers.VirtualTypes;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates StackBufferWriters class with Write{ClassName} methods.
    /// Handles serialization from objects to StackBufferWriter (stack-only, zero allocation).
    /// Inherits from StreamWriterGenerator with different writer type.
    /// </summary>
    /// <remarks>
    /// <para><b>IoT Optimization:</b></para>
    /// - Zero heap allocations for messages &lt;512 bytes
    /// - Writes directly to Span&lt;byte&gt;
    /// - Ideal for battery-powered devices
    /// - Deterministic timing (no GC pauses)
    /// </remarks>
    internal class StackBufferWriterGenerator : StreamWriterGenerator
    {
        public StackBufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry, null, null, "StackBuffer")
        {
        }

        public StackBufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry, null, "StackBuffer")
        {
        }

        public StackBufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, "StackBuffer")
        {
        }
    }
}
