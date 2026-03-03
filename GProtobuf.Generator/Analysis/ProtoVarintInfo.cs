using GProtobuf.Generator.Attributes;

namespace GProtobuf.Generator.Analysis
{
    /// <summary>
    /// Contains information about a ProtoVarint type for code generation.
    /// </summary>
    public sealed class ProtoVarintInfo
    {
        /// <summary>
        /// The full type name of the ProtoVarint struct/class.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// The varint encoding type.
        /// </summary>
        public ProtoVarintType VarintType { get; set; }

        /// <summary>
        /// Name of the method or property marked with [ProtoVarintValue].
        /// </summary>
        public string ValueMemberName { get; set; }

        /// <summary>
        /// True if ValueMemberName is a property, false if method.
        /// </summary>
        public bool IsValueProperty { get; set; }

        /// <summary>
        /// Constructor parameter count (should be 1).
        /// </summary>
        public int ConstructorParameterCount { get; set; }

        /// <summary>
        /// The return type of the value member (e.g., "uint", "int", "ulong", "long").
        /// </summary>
        public string ValueReturnType { get; set; }

        /// <summary>
        /// The parameter type of the constructor (e.g., "uint", "int", "ulong", "long").
        /// </summary>
        public string ConstructorParameterType { get; set; }

        /// <summary>
        /// Validation error message, if any. Null if validation passed.
        /// </summary>
        public string ValidationError { get; set; }

        /// <summary>
        /// True if validation passed (no errors).
        /// </summary>
        public bool IsValid => ValidationError == null;
    }
}
