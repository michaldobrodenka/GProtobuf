using System;

namespace ProtoBuf
{
    /// <summary>
    /// Marks a method to be called after serialization completes.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <para>Apply to instance methods that should execute after all fields have been written during serialization.</para>
    /// <para>Common use cases include:</para>
    /// <list type="bullet">
    ///   <item><description>Cleanup after serialization</description></item>
    ///   <item><description>Resetting temporary state</description></item>
    ///   <item><description>Logging or auditing</description></item>
    ///   <item><description>Releasing resources used during serialization</description></item>
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
    /// public class SecureData
    /// {
    ///     [ProtoMember(1)]
    ///     public byte[] EncryptedPayload { get; set; }
    ///
    ///     private byte[] _tempBuffer;
    ///
    ///     [ProtoBeforeSerialization]
    ///     private void Encrypt() { /* encryption logic */ }
    ///
    ///     [ProtoAfterSerialization]
    ///     private void ClearTempBuffer()
    ///     {
    ///         Array.Clear(_tempBuffer, 0, _tempBuffer.Length);
    ///         _tempBuffer = null;
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ProtoAfterSerializationAttribute : Attribute
    {
    }
}
