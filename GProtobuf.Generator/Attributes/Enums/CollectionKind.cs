namespace GProtobuf.Generator.Attributes
{
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
        ConcreteCollection,

        /// <summary>
        /// Custom collection type (non-generic class implementing ICollection&lt;T&gt; + Add method)
        /// Used for protobuf-net compatible collections without [ProtoContract]
        /// </summary>
        CustomCollection,

        /// <summary>
        /// Custom enumerable type (non-generic class implementing IEnumerable&lt;T&gt; + Add method)
        /// Used for protobuf-net compatible collections without [ProtoContract]
        /// </summary>
        CustomEnumerable
    }
}
