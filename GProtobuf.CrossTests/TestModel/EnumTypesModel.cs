using ProtoBuf;

namespace GProtobuf.Tests.TestModel
{
    /// <summary>
    /// Test enum with default value = 0
    /// </summary>
    public enum Status
    {
        Unknown = 0,  // Proto2 default - should NOT serialize
        Active = 1,
        Deleted = 2,
        Archived = 3
    }

    /// <summary>
    /// Test enum without zero value
    /// </summary>
    public enum Priority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 10
    }

    /// <summary>
    /// Test enum with negative values
    /// </summary>
    public enum SignedEnum
    {
        NegativeTwo = -2,
        NegativeOne = -1,
        Zero = 0,
        One = 1,
        Two = 2
    }

    [ProtoContract]
    public class EnumTypesModel
    {
        /// <summary>
        /// Basic enum field with default value = 0
        /// </summary>
        [ProtoMember(1)]
        public Status Status { get; set; }

        /// <summary>
        /// Enum without zero value
        /// </summary>
        [ProtoMember(2)]
        public Priority Priority { get; set; }

        /// <summary>
        /// Signed enum with negative values
        /// </summary>
        [ProtoMember(3)]
        public SignedEnum SignedValue { get; set; }

        /// <summary>
        /// Nullable enum
        /// </summary>
        [ProtoMember(4)]
        public Status? NullableStatus { get; set; }

        // TODO: Enum arrays not yet supported - generator tries to call SpanReaders.ReadStatusContent()
        // which is not generated for enum types. Need to implement enum array handling in generator.
        // Workaround: Use List<TEnum> which works correctly.
        // Error: CS0117: 'SpanReaders' does not contain a definition for 'ReadStatusContent'

        // /// <summary>
        // /// Enum array (non-packed by default in Level200)
        // /// </summary>
        // [ProtoMember(5)]
        // public Status[] StatusArray { get; set; }

        // /// <summary>
        // /// Enum array with packed encoding
        // /// </summary>
        // [ProtoMember(6, IsPacked = true)]
        // public Priority[] PriorityArrayPacked { get; set; }
    }
}
