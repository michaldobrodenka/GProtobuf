using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    /// <summary>
    /// Sub-format to use when serializing/deserializing data
    /// </summary>
    public enum DataFormat
    {
        /// <summary>
        /// Uses the default encoding for the data-type.
        /// </summary>
        Default,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that zigzag variant encoding will be used. This means that values
        /// with small magnitude (regardless of sign) take a small amount
        /// of space to encode.
        /// </summary>
        ZigZag,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that two's-complement variant encoding will be used.
        /// This means that any -ve number will take 10 bytes (even for 32-bit),
        /// so should only be used for compatibility.
        /// </summary>
        TwosComplement,

        /// <summary>
        /// When applied to signed integer-based data (including Decimal), this
        /// indicates that a fixed amount of space will be used.
        /// </summary>
        FixedSize,

        /// <summary>
        /// When applied to a sub-message, indicates that the value should be treated
        /// as group-delimited.
        /// </summary>
        Group,

        /// <summary>
        /// When applied to members of types such as DateTime or TimeSpan, specifies
        /// that the "well known" standardized representation should be use; DateTime uses Timestamp,
        /// TimeSpan uses Duration.
        /// </summary>
        //[Obsolete("This option is replaced with " + nameof(CompatibilityLevel) + ", and is only used for " + nameof(CompatibilityLevel.Level200) + ", where it changes this field to " + nameof(CompatibilityLevel.Level240), false)]
        WellKnown,
    }

    /// <summary>
    /// Represents the kind of collection type for serialization
    /// </summary>
    public enum CollectionKind
    {
        /// <summary>
        /// Not a collection type
        /// </summary>
        None,
        
        /// <summary>
        /// Array type (T[]) - deserialize to List&lt;T&gt; then convert to array
        /// </summary>
        Array,
        
        /// <summary>
        /// Interface collection type (ICollection&lt;T&gt;, IList&lt;T&gt;, IEnumerable&lt;T&gt;) - deserialize to List&lt;T&gt;
        /// </summary>
        InterfaceCollection,
        
        /// <summary>
        /// Concrete collection type (List&lt;T&gt;, MyCollection&lt;T&gt;) - deserialize to actual type
        /// </summary>
        ConcreteCollection
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoContractAttribute : Attribute
    {
        public string Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
    public class ProtoIncludeAttribute : Attribute
    {
        public ProtoIncludeAttribute(int fieldId, string type, string nmspace)
        {
            FieldId = fieldId;
            Type = type;
            this.Namespace = nmspace;
        }

        public int FieldId { get; set; }

        public string Namespace { get; set; }

        public string Type { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class ProtoMemberAttribute : Attribute
    {
        public ProtoMemberAttribute(int fieldId)
        {
            this.FieldId = fieldId;
        }

        public int FieldId { get; set; }

        public string Type { get; set; }

        public string Namespace { get; set; }

        public string Name { get; set; }

        public bool IsPacked { get; set; }

        public bool IsRequired { get; set; }

        public DataFormat DataFormat { get; set; }
        public List<string> Interfaces { get; set; }
        
        /// Is nullable non reference type
        public bool IsNullable { get; set; }
        
        /// <summary>
        /// Indicates if this member is a collection type
        /// </summary>
        public bool IsCollection { get; set; }
        
        /// <summary>
        /// The element type of the collection (e.g., "int" for List&lt;int&gt;)
        /// </summary>
        public string CollectionElementType { get; set; }
        
        /// <summary>
        /// The kind of collection type
        /// </summary>
        public CollectionKind CollectionKind { get; set; }
        
        /// <summary>
        /// Indicates if this member is a map/dictionary type (ICollection<KeyValuePair<K,V>>, Dictionary<K,V>, etc.)
        /// </summary>
        public bool IsMap { get; set; }
        
        /// <summary>
        /// The key type for map/dictionary types (e.g., "string" for Dictionary<string, int>)
        /// </summary>
        public string MapKeyType { get; set; }
        
        /// <summary>
        /// The value type for map/dictionary types (e.g., "int" for Dictionary<string, int>)
        /// </summary>
        public string MapValueType { get; set; }
    }
}
