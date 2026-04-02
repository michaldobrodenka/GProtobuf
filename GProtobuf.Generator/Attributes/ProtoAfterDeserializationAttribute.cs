using System;

namespace ProtoBuf
{
    /// <summary>
    /// Marks a method to be called after deserialization completes.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <para>Apply to instance methods that should execute after all fields have been read during deserialization.</para>
    /// <para>Common use cases include:</para>
    /// <list type="bullet">
    ///   <item><description>Validating deserialized data</description></item>
    ///   <item><description>Computing derived values from deserialized fields</description></item>
    ///   <item><description>Resolving parent/child references</description></item>
    ///   <item><description>Decompressing or decrypting data</description></item>
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
    /// public class Order
    /// {
    ///     [ProtoMember(1)]
    ///     public List&lt;OrderItem&gt; Items { get; set; }
    ///
    ///     [ProtoMember(2)]
    ///     public decimal TotalAmount { get; set; }
    ///
    ///     [ProtoAfterDeserialization]
    ///     private void ValidateTotal()
    ///     {
    ///         var calculated = Items?.Sum(x => x.Price * x.Quantity) ?? 0;
    ///         if (calculated != TotalAmount)
    ///             throw new InvalidDataException("Checksum mismatch");
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ProtoAfterDeserializationAttribute : Attribute
    {
    }
}
