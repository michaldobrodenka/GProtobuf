namespace GProtobuf.Generator.Analysis
{
    /// <summary>
    /// Represents a serialization callback method marked with [ProtoBeforeSerialization] or [ProtoAfterSerialization].
    /// Created during source generation by analyzing methods with callback attributes.
    /// </summary>
    public sealed class SerializationCallback
    {
        /// <summary>
        /// Name of the callback method.
        /// </summary>
        public string MethodName { get; set; }

        /// <summary>
        /// True if the method is static (invalid - will generate diagnostic).
        /// </summary>
        public bool IsStatic { get; set; }

        /// <summary>
        /// True if the method has parameters (invalid - will generate diagnostic).
        /// </summary>
        public bool HasParameters { get; set; }
    }
}
