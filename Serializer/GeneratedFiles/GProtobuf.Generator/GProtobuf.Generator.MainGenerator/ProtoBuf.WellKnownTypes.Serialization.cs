using GProtobuf.Core;
using System;
using System.Collections.Generic;

namespace ProtoBuf.WellKnownTypes.Serialization
{
    public static class Deserializers
    {
        public static global::ProtoBuf.WellKnownTypes.Duration DeserializeDuration(ReadOnlySpan<byte> data)
        {
            var reader = new SpanReader(data);
            return SpanReaders.ReadDuration(ref reader);
        }

        public static global::ProtoBuf.WellKnownTypes.Empty DeserializeEmpty(ReadOnlySpan<byte> data)
        {
            var reader = new SpanReader(data);
            return SpanReaders.ReadEmpty(ref reader);
        }

        public static global::ProtoBuf.WellKnownTypes.Timestamp DeserializeTimestamp(ReadOnlySpan<byte> data)
        {
            var reader = new SpanReader(data);
            return SpanReaders.ReadTimestamp(ref reader);
        }

    }

    public static class SpanReaders
    {
        public static global::ProtoBuf.WellKnownTypes.Duration ReadDuration(ref SpanReader reader)
        {
            global::ProtoBuf.WellKnownTypes.Duration result = new global::ProtoBuf.WellKnownTypes.Duration();

            while(!reader.IsEnd)
            {
                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

                // default
                reader.SkipField(wireType);
            }

            return result;
        }

        public static global::ProtoBuf.WellKnownTypes.Empty ReadEmpty(ref SpanReader reader)
        {
            global::ProtoBuf.WellKnownTypes.Empty result = new global::ProtoBuf.WellKnownTypes.Empty();

            while(!reader.IsEnd)
            {
                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

                // default
                reader.SkipField(wireType);
            }

            return result;
        }

        public static global::ProtoBuf.WellKnownTypes.Timestamp ReadTimestamp(ref SpanReader reader)
        {
            global::ProtoBuf.WellKnownTypes.Timestamp result = new global::ProtoBuf.WellKnownTypes.Timestamp();

            while(!reader.IsEnd)
            {
                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

                // default
                reader.SkipField(wireType);
            }

            return result;
        }

    }
}
