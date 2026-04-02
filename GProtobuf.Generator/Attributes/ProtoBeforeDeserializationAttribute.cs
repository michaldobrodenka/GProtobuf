using System;

namespace ProtoBuf
{
    /// <summary>
    /// Marks a method to be called before deserialization begins.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <para>Apply to instance methods that should execute before any fields are read during deserialization.</para>
    /// <para>Common use cases include:</para>
    /// <list type="bullet">
    ///   <item><description>Resetting state before deserialization</description></item>
    ///   <item><description>Initializing collections or default values</description></item>
    ///   <item><description>Preparing resources needed during deserialization</description></item>
    ///   <item><description>Clearing cached or computed values</description></item>
    /// </list>
    ///
    /// <para><b>Method Requirements:</b></para>
    /// <list type="bullet">
    ///   <item><description>Must be an instance method (not static)</description></item>
    ///   <item><description>Must be parameterless</description></item>
    ///   <item><description>Return type is ignored (can be void or any type)</description></item>
    ///   <item><description>Can have any access modifier (private, protected, internal, public)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProtoContract]
    /// public class CachedData
    /// {
    ///     [ProtoMember(1)]
    ///     public byte[] CompressedData { get; set; }
    ///
    ///     private byte[] _decompressedCache;
    ///
    ///     [ProtoBeforeDeserialization]
    ///     private void ResetState()
    ///     {
    ///         CompressedData = null;
    ///         _decompressedCache = null;
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ProtoBeforeDeserializationAttribute : Attribute
    {
    }
}
