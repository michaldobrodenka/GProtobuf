using System;

namespace ProtoBuf
{
    /// <summary>
    /// Marks a method to be called before serialization begins.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b></para>
    /// <para>Apply to instance methods that should execute before any fields are written during serialization.</para>
    /// <para>Common use cases include:</para>
    /// <list type="bullet">
    ///   <item><description>Computing derived values before serialization</description></item>
    ///   <item><description>Validating object state</description></item>
    ///   <item><description>Encrypting or compressing data</description></item>
    ///   <item><description>Setting computed checksums</description></item>
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
    ///     [ProtoBeforeSerialization]
    ///     private void CalculateTotal()
    ///     {
    ///         TotalAmount = Items?.Sum(x => x.Price * x.Quantity) ?? 0;
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class ProtoBeforeSerializationAttribute : Attribute
    {
    }
}
