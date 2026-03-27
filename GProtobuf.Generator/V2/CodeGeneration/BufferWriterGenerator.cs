using GProtobuf.Generator.V2.Handlers.VirtualTypes;

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
            : base(sb, registry, null, null, "Buffer")
        {
        }

        public BufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry, null, "Buffer")
        {
        }

        public BufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, "Buffer", null)
        {
        }

        public BufferWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string virtualTypesNamespace)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, "Buffer", virtualTypesNamespace)
        {
        }
    }
}
