using ProtoBuf;
using System;
using System.Collections.Generic;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model for DateTime serialization/deserialization with protobuf-net BCL format.
    /// Tests Level200 compatibility with different DateTime scales (Seconds, Milliseconds, Ticks, MinMax).
    /// </summary>
    [ProtoContract]
    public class DateTimeTypesModel
    {
        /// <summary>
        /// DateTime with second precision (most common for APIs).
        /// Expected wire format: Scale=3 (Seconds).
        /// </summary>
        [ProtoMember(1)]
        public DateTime ApiTimestamp { get; set; }

        /// <summary>
        /// DateTime with millisecond precision.
        /// Expected wire format: Scale=4 (Milliseconds).
        /// </summary>
        [ProtoMember(2)]
        public DateTime MillisecondTimestamp { get; set; }

        /// <summary>
        /// DateTime with tick precision (high-precision).
        /// Expected wire format: Scale=5 (Ticks).
        /// </summary>
        [ProtoMember(3)]
        public DateTime HighPrecisionTimestamp { get; set; }

        /// <summary>
        /// DateTime.MinValue special case.
        /// Expected wire format: Scale=15 (MinMax), value=-1.
        /// </summary>
        [ProtoMember(4)]
        public DateTime MinValue { get; set; }

        /// <summary>
        /// DateTime.MaxValue special case.
        /// Expected wire format: Scale=15 (MinMax), value=+1.
        /// </summary>
        [ProtoMember(5)]
        public DateTime MaxValue { get; set; }

        /// <summary>
        /// Nullable DateTime field (proto2 default: not serialized if null).
        /// </summary>
        [ProtoMember(6)]
        public DateTime? OptionalTimestamp { get; set; }

        /// <summary>
        /// List of DateTime values (repeated field).
        /// Level200: each DateTime serialized as nested message.
        /// </summary>
        [ProtoMember(7)]
        public List<DateTime> Timestamps { get; set; }

        /// <summary>
        /// Array of DateTime values (alternative to List).
        /// </summary>
        [ProtoMember(8)]
        public DateTime[] TimestampArray { get; set; }

        /// <summary>
        /// UTC DateTime (Level200: Kind is NOT serialized, always Unspecified on read).
        /// </summary>
        [ProtoMember(9)]
        public DateTime UtcTimestamp { get; set; }

        /// <summary>
        /// Local DateTime (Level200: Kind is NOT serialized, always Unspecified on read).
        /// </summary>
        [ProtoMember(10)]
        public DateTime LocalTimestamp { get; set; }

        /// <summary>
        /// Unix epoch (January 1, 1970 UTC).
        /// Expected wire format: Scale=3, value=0 (or offset).
        /// </summary>
        [ProtoMember(11)]
        public DateTime UnixEpoch { get; set; }
    }
}
