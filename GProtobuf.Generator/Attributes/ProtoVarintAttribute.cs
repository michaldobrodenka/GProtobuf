using System;

namespace GProtobuf.Generator.Attributes
{
    /// <summary>
    /// Marks a struct or class to be serialized as a varint instead of a nested message.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintAttribute : Attribute
    {
        public ProtoVarintAttribute(ProtoVarintType type = ProtoVarintType.UInt32)
        {
            Type = type;
        }

        public ProtoVarintType Type { get; }
    }

    /// <summary>
    /// Marks a constructor to be used for deserializing a ProtoVarint type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintConstructorAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a method or property that returns the underlying value for serialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ProtoVarintValueAttribute : Attribute
    {
    }
}
