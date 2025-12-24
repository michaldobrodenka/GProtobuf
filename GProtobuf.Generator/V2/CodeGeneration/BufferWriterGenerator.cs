using GProtobuf.Generator.V2.Handlers;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates BufferWriters class with Write{ClassName} methods.
    /// Handles serialization from objects to BufferWriter (IBufferWriter).
    /// Inherits from StreamWriterGenerator with different writer type.
    /// </summary>
    internal class BufferWriterGenerator : StreamWriterGenerator
    {
        public BufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry, null, "Buffer")
        {
        }

        public BufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry, "Buffer")
        {
        }
    }
}
