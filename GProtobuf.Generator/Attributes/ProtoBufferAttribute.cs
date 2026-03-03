using System;

namespace GProtobuf.Generator.Attributes
{
    /// <summary>
    /// Marks a method as part of custom buffer serialization for a specific field.
    /// </summary>
    /// <remarks>
    /// <para>Custom buffer serialization allows user-defined logic for specific fields.</para>
    /// <para>Three methods are needed for full support:</para>
    /// <list type="bullet">
    ///   <item><description>GetSize - returns int, no parameters</description></item>
    ///   <item><description>Write - returns void, takes Span&lt;byte&gt;</description></item>
    ///   <item><description>Read - returns void, takes ReadOnlySpan&lt;byte&gt; (optional)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoBuffer(10, ProtoBufferOperation.GetSize)]
    /// public int GetCustomDataSize() => _customData?.Length ?? 0;
    ///
    /// [ProtoBuffer(10, ProtoBufferOperation.Write)]
    /// public void WriteCustomData(Span&lt;byte&gt; buffer) => _customData.AsSpan().CopyTo(buffer);
    ///
    /// [ProtoBuffer(10, ProtoBufferOperation.Read)]
    /// public void ReadCustomData(ReadOnlySpan&lt;byte&gt; data) => _customData = data.ToArray();
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoBufferAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance with the specified tag and operation.
        /// </summary>
        /// <param name="tag">Unique field ID (tag) for this custom buffer field.</param>
        /// <param name="operation">The type of operation this method performs.</param>
        public ProtoBufferAttribute(int tag, ProtoBufferOperation operation)
        {
            Tag = tag;
            Operation = operation;
        }

        /// <summary>
        /// Unique field ID (tag) for this custom buffer field.
        /// All three operations (GetSize, Write, Read) must use the same tag.
        /// </summary>
        public int Tag { get; }

        /// <summary>
        /// The type of operation this method performs.
        /// </summary>
        public ProtoBufferOperation Operation { get; }
    }
}
