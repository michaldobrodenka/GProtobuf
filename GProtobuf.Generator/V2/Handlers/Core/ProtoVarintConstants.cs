namespace GProtobuf.Generator.V2.Handlers.Core
{
    /// <summary>
    /// Constants for ProtoVarint attribute handling.
    /// Centralizes hardcoded strings to avoid duplication and typos.
    /// </summary>
    internal static class ProtoVarintConstants
    {
        /// <summary>
        /// Name of the [ProtoVarint] attribute class.
        /// </summary>
        public const string AttributeName = "ProtoVarintAttribute";

        /// <summary>
        /// Name of the [ProtoVarintValue] attribute class.
        /// </summary>
        public const string ValueAttributeName = "ProtoVarintValueAttribute";

        /// <summary>
        /// Name of the [ProtoVarintConstructor] attribute class.
        /// </summary>
        public const string ConstructorAttributeName = "ProtoVarintConstructorAttribute";

        /// <summary>
        /// Default name for the value member when not explicitly specified.
        /// </summary>
        public const string DefaultValueMember = "Value";
    }
}
