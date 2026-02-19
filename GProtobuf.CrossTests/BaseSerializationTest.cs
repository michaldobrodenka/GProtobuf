using System.IO;

namespace GProtobuf.Tests;

/// <summary>
/// Base class for serialization tests providing common helper methods.
/// Test naming convention:
/// - GG (GProtobuf to GProtobuf): Serialize with GProtobuf, Deserialize with GProtobuf (SpanReader)
/// - GG_Stream: Serialize with GProtobuf, Deserialize with GProtobuf (StreamReader)
/// - PG (Protobuf-net to GProtobuf): Serialize with protobuf-net, Deserialize with GProtobuf (SpanReader)
/// - PG_Stream: Serialize with protobuf-net, Deserialize with GProtobuf (StreamReader)
/// - GP (GProtobuf to Protobuf-net): Serialize with GProtobuf, Deserialize with protobuf-net
/// </summary>
public abstract class BaseSerializationTest
{
    #region Serialization Helpers

    protected static byte[] SerializeWithGProtobuf<T>(T model, Action<Stream, T> serializer)
    {
        using var ms = new MemoryStream();
        serializer(ms, model);
        return ms.ToArray();
    }

    protected static byte[] SerializeWithProtobufNet<T>(T model)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, model);
        return ms.ToArray();
    }

    #endregion

    #region SpanReader Deserialization (byte[] → SpanReader)

    protected static T DeserializeWithGProtobuf<T>(byte[] data, Func<byte[], T> deserializer)
    {
        return deserializer(data);
    }

    #endregion

    #region StreamReader Deserialization (Stream → StreamReader)

    /// <summary>
    /// Deserialize using StreamReader from a Stream.
    /// </summary>
    protected static T DeserializeWithGProtobufStream<T>(Stream stream, Func<Stream, T> deserializer)
    {
        return deserializer(stream);
    }

    /// <summary>
    /// Deserialize using StreamReader from byte[] (wraps in MemoryStream).
    /// Useful for comparing StreamReader vs SpanReader results.
    /// </summary>
    protected static T DeserializeWithGProtobufStreamFromBytes<T>(byte[] data, Func<Stream, T> deserializer)
    {
        using var ms = new MemoryStream(data);
        return deserializer(ms);
    }

    #endregion

    #region Protobuf-net Deserialization

    protected static T DeserializeWithProtobufNet<T>(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return ProtoBuf.Serializer.Deserialize<T>(ms);
    }

    #endregion
}