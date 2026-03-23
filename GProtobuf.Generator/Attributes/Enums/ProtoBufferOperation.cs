namespace GProtobuf.Generator.Attributes
{
    /// <summary>
    /// Defines the type of operation for custom buffer serialization.
    /// </summary>
    public enum ProtoBufferOperation
    {
        /// <summary>
        /// Method returns the size of the buffer (int, no parameters).
        /// </summary>
        GetSize,

        /// <summary>
        /// Method writes data to the buffer (void, Span&lt;byte&gt; parameter).
        /// </summary>
        Write,

        /// <summary>
        /// Method reads data from the buffer (void, ReadOnlySpan&lt;byte&gt; parameter).
        /// </summary>
        Read
    }
}
