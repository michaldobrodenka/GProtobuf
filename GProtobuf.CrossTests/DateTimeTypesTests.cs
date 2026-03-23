using GProtobuf.CrossTests.TestModel;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Unit tests for DateTime serialization/deserialization.
    /// Tests protobuf-net BCL format compatibility with Level200 compliance.
    /// </summary>
    public class DateTimeTypesTests
    {
        private static byte[] SerializeGProtobuf(DateTimeTypesModel model)
        {
            using var ms = new MemoryStream();
            Span<byte> buffer = stackalloc byte[256];
            var writer = new global::GProtobuf.Core.StreamWriter(ms, buffer);
            global::GProtobuf.CrossTests.TestModel.Serialization.StreamWriters.WriteDateTimeTypesModel(ref writer, model);
            writer.Flush();
            return ms.ToArray();
        }

        private static DateTimeTypesModel DeserializeGProtobuf(byte[] data)
        {
            return global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeDateTimeTypesModel(data);
        }

        private static void AssertRoundTrip(DateTimeTypesModel model, Action<DateTimeTypesModel> assertions)
        {
            var serialized = SerializeGProtobuf(model);
            var deserialized = DeserializeGProtobuf(serialized);
            assertions(deserialized);
        }
        [Fact]
        public void DateTime_ApiTimestamp_SecondPrecision_RoundTrip()
        {
            // Arrange: DateTime with second precision (no milliseconds/ticks)
            var model = new DateTimeTypesModel
            {
                ApiTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc)
            };

            // Act & Assert: GProtobuf round-trip
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.ApiTimestamp, deserialized.ApiTimestamp);
            });
        }

        [Fact]
        public void DateTime_MillisecondTimestamp_RoundTrip()
        {
            // Arrange: DateTime with millisecond precision
            var model = new DateTimeTypesModel
            {
                MillisecondTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, 123, DateTimeKind.Utc)
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.MillisecondTimestamp, deserialized.MillisecondTimestamp);
            });
        }

        [Fact]
        public void DateTime_HighPrecisionTimestamp_TickPrecision_RoundTrip()
        {
            // Arrange: DateTime with tick precision (not aligned to milliseconds)
            var model = new DateTimeTypesModel
            {
                HighPrecisionTimestamp = new DateTime(638409114450000001) // 1 extra tick
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.HighPrecisionTimestamp, deserialized.HighPrecisionTimestamp);
            });
        }

        [Fact]
        public void DateTime_MinValue_SpecialCase_RoundTrip()
        {
            // Arrange: DateTime.MinValue uses Scale=15 (MinMax), value=-1
            var model = new DateTimeTypesModel
            {
                MinValue = DateTime.MinValue
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(DateTime.MinValue, deserialized.MinValue);
            });
        }

        [Fact]
        public void DateTime_MaxValue_SpecialCase_RoundTrip()
        {
            // Arrange: DateTime.MaxValue uses Scale=15 (MinMax), value=+1
            var model = new DateTimeTypesModel
            {
                MaxValue = DateTime.MaxValue
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(DateTime.MaxValue, deserialized.MaxValue);
            });
        }

        [Fact]
        public void DateTime_NullableTimestamp_Null_NotSerialized()
        {
            // Arrange: Nullable DateTime = null (proto2 default: not serialized)
            var model = new DateTimeTypesModel
            {
                OptionalTimestamp = null
            };

            // Act
            var serialized = SerializeGProtobuf(model);

            // Assert: Wire data should be minimal (only default values skipped)
            Assert.NotNull(serialized);

            var deserialized = DeserializeGProtobuf(serialized);
            Assert.Null(deserialized.OptionalTimestamp);
        }

        [Fact]
        public void DateTime_NullableTimestamp_HasValue_RoundTrip()
        {
            // Arrange: Nullable DateTime with value
            var model = new DateTimeTypesModel
            {
                OptionalTimestamp = new DateTime(2024, 6, 15, 14, 30, 0)
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.NotNull(deserialized.OptionalTimestamp);
                Assert.Equal(model.OptionalTimestamp.Value, deserialized.OptionalTimestamp.Value);
            });
        }

        [Fact]
        public void DateTime_List_MultipleValues_RoundTrip()
        {
            // Arrange: List of DateTime values
            var model = new DateTimeTypesModel
            {
                Timestamps = new List<DateTime>
                {
                    new DateTime(2024, 1, 1, 0, 0, 0),
                    new DateTime(2024, 6, 15, 12, 30, 45),
                    DateTime.Now,
                    DateTime.MinValue,
                    DateTime.MaxValue
                }
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.Timestamps.Count, deserialized.Timestamps.Count);
                for (int i = 0; i < model.Timestamps.Count; i++)
                {
                    Assert.Equal(model.Timestamps[i], deserialized.Timestamps[i]);
                }
            });
        }

        [Fact]
        public void DateTime_Array_MultipleValues_RoundTrip()
        {
            // Arrange: Array of DateTime values
            var model = new DateTimeTypesModel
            {
                TimestampArray = new[]
                {
                    new DateTime(2020, 1, 1),
                    new DateTime(2021, 1, 1),
                    new DateTime(2022, 1, 1)
                }
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.TimestampArray.Length, deserialized.TimestampArray.Length);
                for (int i = 0; i < model.TimestampArray.Length; i++)
                {
                    Assert.Equal(model.TimestampArray[i], deserialized.TimestampArray[i]);
                }
            });
        }

        [Fact]
        public void DateTime_UtcKind_LosesKindInformation_Level200Behavior()
        {
            // Arrange: DateTime with UTC Kind
            var model = new DateTimeTypesModel
            {
                UtcTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc)
            };

            // Act
            var serialized = SerializeGProtobuf(model);
            var deserialized = DeserializeGProtobuf(serialized);

            // Assert: Level200 does NOT serialize DateTimeKind, always returns Unspecified
            Assert.Equal(model.UtcTimestamp.Ticks, deserialized.UtcTimestamp.Ticks);
            Assert.Equal(DateTimeKind.Unspecified, deserialized.UtcTimestamp.Kind); // Level200 behavior
        }

        [Fact]
        public void DateTime_LocalKind_LosesKindInformation_Level200Behavior()
        {
            // Arrange: DateTime with Local Kind
            var model = new DateTimeTypesModel
            {
                LocalTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Local)
            };

            // Act
            var serialized = SerializeGProtobuf(model);
            var deserialized = DeserializeGProtobuf(serialized);

            // Assert: Level200 does NOT serialize DateTimeKind
            Assert.Equal(model.LocalTimestamp.Ticks, deserialized.LocalTimestamp.Ticks);
            Assert.Equal(DateTimeKind.Unspecified, deserialized.LocalTimestamp.Kind); // Level200 behavior
        }

        [Fact]
        public void DateTime_UnixEpoch_RoundTrip()
        {
            // Arrange: Unix epoch (January 1, 1970 UTC)
            var model = new DateTimeTypesModel
            {
                UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            // Act & Assert
            AssertRoundTrip(model, deserialized =>
            {
                Assert.Equal(model.UnixEpoch.Ticks, deserialized.UnixEpoch.Ticks);
            });
        }

        [Fact]
        public void DateTime_Default_NotSerialized_Proto2Behavior()
        {
            // Arrange: Default DateTime (proto2: not serialized)
            var model = new DateTimeTypesModel
            {
                ApiTimestamp = default // DateTime.MinValue equivalent
            };

            // Act
            var serialized = SerializeGProtobuf(model);

            // Assert: Minimal wire size (default values not serialized)
            var deserialized = DeserializeGProtobuf(serialized);
            Assert.Equal(default(DateTime), deserialized.ApiTimestamp);
        }

        [Fact]
        public void DateTime_CrossCompatibility_GProtobuf_To_ProtobufNet()
        {
            // Arrange
            var model = new DateTimeTypesModel
            {
                ApiTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc),
                MillisecondTimestamp = new DateTime(2024, 6, 15, 14, 30, 45, 123),
                HighPrecisionTimestamp = new DateTime(638409114450000001),
                MinValue = DateTime.MinValue,
                MaxValue = DateTime.MaxValue,
                OptionalTimestamp = new DateTime(2023, 12, 25),
                Timestamps = new List<DateTime>
                {
                    new DateTime(2020, 1, 1),
                    new DateTime(2021, 1, 1)
                }
            };

            // Act: Serialize with GProtobuf
            var gprotobufData = SerializeGProtobuf(model);

            // Act: Deserialize with protobuf-net
            var deserializedByProtobufNet = ProtoBuf.Serializer.Deserialize<DateTimeTypesModel>(
                new MemoryStream(gprotobufData)
            );

            // Assert: Full compatibility
            Assert.Equal(model.ApiTimestamp, deserializedByProtobufNet.ApiTimestamp);
            Assert.Equal(model.MillisecondTimestamp, deserializedByProtobufNet.MillisecondTimestamp);
            Assert.Equal(model.HighPrecisionTimestamp, deserializedByProtobufNet.HighPrecisionTimestamp);
            Assert.Equal(model.MinValue, deserializedByProtobufNet.MinValue);
            Assert.Equal(model.MaxValue, deserializedByProtobufNet.MaxValue);
            Assert.Equal(model.OptionalTimestamp, deserializedByProtobufNet.OptionalTimestamp);
            Assert.Equal(model.Timestamps.Count, deserializedByProtobufNet.Timestamps.Count);
        }

        [Fact]
        public void DateTime_CrossCompatibility_ProtobufNet_To_GProtobuf()
        {
            // Arrange
            var model = new DateTimeTypesModel
            {
                ApiTimestamp = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc),
                MillisecondTimestamp = new DateTime(2024, 6, 15, 14, 30, 45, 123),
                MinValue = DateTime.MinValue,
                MaxValue = DateTime.MaxValue,
                OptionalTimestamp = null,
                Timestamps = new List<DateTime> { DateTime.Now }
            };

            // Act: Serialize with protobuf-net
            var stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, model);
            var protobufNetData = stream.ToArray();

            // Act: Deserialize with GProtobuf
            var deserializedByGProtobuf = DeserializeGProtobuf(protobufNetData);

            // Assert: Full compatibility
            Assert.Equal(model.ApiTimestamp, deserializedByGProtobuf.ApiTimestamp);
            Assert.Equal(model.MillisecondTimestamp, deserializedByGProtobuf.MillisecondTimestamp);
            Assert.Equal(model.MinValue, deserializedByGProtobuf.MinValue);
            Assert.Equal(model.MaxValue, deserializedByGProtobuf.MaxValue);
            Assert.Null(deserializedByGProtobuf.OptionalTimestamp);
            Assert.Equal(model.Timestamps.Count, deserializedByGProtobuf.Timestamps.Count);
        }
    }
}
