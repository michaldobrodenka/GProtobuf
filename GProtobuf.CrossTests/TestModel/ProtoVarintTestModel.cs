using System;
using ProtoBuf;
using ProtoVarintAttribute = GProtobuf.Core.ProtoVarintAttribute;
using ProtoVarintType = GProtobuf.Core.ProtoVarintType;
using ProtoVarintConstructorAttribute = GProtobuf.Core.ProtoVarintConstructorAttribute;
using ProtoVarintValueAttribute = GProtobuf.Core.ProtoVarintValueAttribute;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// KNXAddress struct - represents a KNX device address as a 16-bit value.
    /// Serialized as a single varint instead of a nested message.
    /// </summary>
    [ProtoVarint(ProtoVarintType.UInt32)]
    public readonly struct KNXAddress
    {
        private readonly uint _value;

        [ProtoVarintConstructor]
        public KNXAddress(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// Creates a KNXAddress from area, line, and device components.
        /// </summary>
        public KNXAddress(byte area, byte line, byte device)
        {
            // KNX address format: area (4 bits) | line (4 bits) | device (8 bits)
            _value = (uint)((area & 0x0F) << 12 | (line & 0x0F) << 8 | device);
        }

        [ProtoVarintValue]
        public uint ToUInt32() => _value;

        public byte Area => (byte)((_value >> 12) & 0x0F);
        public byte Line => (byte)((_value >> 8) & 0x0F);
        public byte Device => (byte)(_value & 0xFF);

        public override string ToString() => $"{Area}.{Line}.{Device}";

        public override bool Equals(object obj) => obj is KNXAddress other && _value == other._value;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>
    /// IPv4Address struct - represents an IPv4 address as a 32-bit value.
    /// </summary>
    [ProtoVarint(ProtoVarintType.UInt32)]
    public readonly struct IPv4Address
    {
        private readonly uint _value;

        [ProtoVarintConstructor]
        public IPv4Address(uint value)
        {
            _value = value;
        }

        public IPv4Address(byte a, byte b, byte c, byte d)
        {
            _value = (uint)(a << 24 | b << 16 | c << 8 | d);
        }

        [ProtoVarintValue]
        public uint Value => _value;

        public byte A => (byte)((_value >> 24) & 0xFF);
        public byte B => (byte)((_value >> 16) & 0xFF);
        public byte C => (byte)((_value >> 8) & 0xFF);
        public byte D => (byte)(_value & 0xFF);

        public override string ToString() => $"{A}.{B}.{C}.{D}";

        public override bool Equals(object obj) => obj is IPv4Address other && _value == other._value;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>
    /// SignedOffset struct - represents a signed offset using ZigZag encoding.
    /// </summary>
    [ProtoVarint(ProtoVarintType.SInt32)]
    public readonly struct SignedOffset
    {
        private readonly int _value;

        [ProtoVarintConstructor]
        public SignedOffset(int value)
        {
            _value = value;
        }

        [ProtoVarintValue]
        public int Value => _value;

        public override string ToString() => _value.ToString();

        public override bool Equals(object obj) => obj is SignedOffset other && _value == other._value;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>
    /// LargeTimestamp struct - represents a 64-bit timestamp.
    /// </summary>
    [ProtoVarint(ProtoVarintType.UInt64)]
    public readonly struct LargeTimestamp
    {
        private readonly ulong _value;

        [ProtoVarintConstructor]
        public LargeTimestamp(ulong value)
        {
            _value = value;
        }

        [ProtoVarintValue]
        public ulong Value => _value;

        public DateTime ToDateTime() => DateTimeOffset.FromUnixTimeMilliseconds((long)_value).DateTime;

        public override string ToString() => _value.ToString();

        public override bool Equals(object obj) => obj is LargeTimestamp other && _value == other._value;
        public override int GetHashCode() => _value.GetHashCode();
    }

    /// <summary>
    /// Test model with a single ProtoVarint field.
    /// </summary>
    [ProtoContract]
    public class SimpleProtoVarintModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public KNXAddress Address { get; set; }

        [ProtoMember(3)]
        public string Name { get; set; }
    }

    /// <summary>
    /// Test model with multiple different ProtoVarint types.
    /// </summary>
    [ProtoContract]
    public class MultipleProtoVarintModel
    {
        [ProtoMember(1)]
        public KNXAddress SourceAddress { get; set; }

        [ProtoMember(2)]
        public KNXAddress DestinationAddress { get; set; }

        [ProtoMember(3)]
        public IPv4Address GatewayIP { get; set; }

        [ProtoMember(4)]
        public SignedOffset TimeOffset { get; set; }

        [ProtoMember(5)]
        public LargeTimestamp Timestamp { get; set; }
    }

    /// <summary>
    /// Test model demonstrating ProtoVarint with regular protobuf fields.
    /// </summary>
    [ProtoContract]
    public class MixedProtoVarintModel
    {
        [ProtoMember(1)]
        public string DeviceName { get; set; }

        [ProtoMember(2)]
        public KNXAddress Address { get; set; }

        [ProtoMember(3)]
        public int Version { get; set; }

        [ProtoMember(4)]
        public IPv4Address IPAddress { get; set; }

        [ProtoMember(5)]
        public double Temperature { get; set; }

        [ProtoMember(6)]
        public SignedOffset CalibrationOffset { get; set; }
    }

    /// <summary>
    /// Test model with only ProtoVarint fields (no regular types).
    /// </summary>
    [ProtoContract]
    public class OnlyProtoVarintModel
    {
        [ProtoMember(1)]
        public KNXAddress Address1 { get; set; }

        [ProtoMember(2)]
        public KNXAddress Address2 { get; set; }

        [ProtoMember(3)]
        public KNXAddress Address3 { get; set; }
    }

    [ProtoVarint(ProtoVarintType.UInt32)]
    public readonly struct DeviceId
    {
        [ProtoVarintValue]
        public readonly uint Value;

        [ProtoVarintConstructor]
        public DeviceId(uint value)
        {
            Value = value;
        }

        public override string ToString() => Value.ToString();

        public override bool Equals(object obj) => obj is DeviceId other && Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    [ProtoContract]
    public class DeviceIdModel
    {
        [ProtoMember(1)]
        public int SequenceNumber { get; set; }

        [ProtoMember(2)]
        public DeviceId DeviceId { get; set; }

        [ProtoMember(3)]
        public string DeviceName { get; set; }
    }


    /// <summary>
    /// Test model demonstrating default value behavior for ProtoVarint.
    /// </summary>
    [ProtoContract]
    public class DefaultValueProtoVarintModel
    {
        [ProtoMember(1)]
        public KNXAddress ZeroAddress { get; set; }  // Default: 0

        [ProtoMember(2)]
        public SignedOffset ZeroOffset { get; set; }  // Default: 0

        [ProtoMember(3)]
        public KNXAddress NonZeroAddress { get; set; }
    }

    /// <summary>
    /// Test model with Dictionary where ProtoVarint type is the key.
    /// This tests the fix for CalculateMapEntry not calling CalculateXxxContentSize for ProtoVarint types.
    /// </summary>
    [ProtoContract]
    public class DictionaryWithProtoVarintKeyModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public System.Collections.Generic.Dictionary<KNXAddress, string> AddressNames { get; set; }

        [ProtoMember(3)]
        public System.Collections.Generic.Dictionary<DeviceId, int> DeviceValues { get; set; }
    }
}
