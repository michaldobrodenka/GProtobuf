using System.IO;

namespace GProtobuf.Tests;

/// <summary>
/// Base class for serialization tests providing common helper methods.
/// Test naming convention:
/// - GG (GProtobuf to GProtobuf): Serialize with GProtobuf, Deserialize with GProtobuf
/// - PG (Protobuf-net to GProtobuf): Serialize with protobuf-net, Deserialize with GProtobuf  
/// - GP (GProtobuf to Protobuf-net): Serialize with GProtobuf, Deserialize with protobuf-net
/// </summary>
public abstract class BaseSerializationTest
{
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

    protected static T DeserializeWithGProtobuf<T>(byte[] data, Func<byte[], T> deserializer)
    {
        return deserializer(data);
    }

    protected static T DeserializeWithProtobufNet<T>(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return ProtoBuf.Serializer.Deserialize<T>(ms);
    }
}