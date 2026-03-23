using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GProtobuf.Core;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.CrossTests.TestModel.Serialization;
using ProtoBuf;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for nested dictionary with float values.
    /// Specifically tests Dictionary&lt;enum, ConcurrentDictionary&lt;int, float&gt;&gt; to diagnose wireType == 6 issue.
    /// </summary>
    public class NestedMapWithFloatTests
    {
        private readonly ITestOutputHelper _output;

        public NestedMapWithFloatTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Analyze protobuf-net wire format for simple int-&gt;float map
        /// </summary>
        [Fact]
        public void AnalyzeSimpleIntFloatMap_ProtobufNetWireFormat()
        {
            var model = new NestedMapWithFloatModel
            {
                SimpleIntFloatMap = new Dictionary<int, float>
                {
                    { 1, 1.5f },
                    { 2, 2.5f }
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"SimpleIntFloatMap serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");
            DumpWireFormat(bytes, "SimpleIntFloatMap");
        }

        /// <summary>
        /// Analyze protobuf-net wire format for nested Dictionary&lt;enum, Dictionary&lt;int, float&gt;&gt;
        /// </summary>
        [Fact]
        public void AnalyzeEnumToIntFloatNestedMap_ProtobufNetWireFormat()
        {
            var model = new NestedMapWithFloatModel
            {
                EnumToIntFloatNestedMap = new Dictionary<ValueLogType, Dictionary<int, float>>
                {
                    {
                        ValueLogType.Temperature, new Dictionary<int, float>
                        {
                            { 1, 22.5f },
                            { 2, 23.0f }
                        }
                    }
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"EnumToIntFloatNestedMap serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");
            DumpWireFormat(bytes, "EnumToIntFloatNestedMap");
        }

        /// <summary>
        /// Analyze protobuf-net wire format for nested Dictionary&lt;enum, ConcurrentDictionary&lt;int, float&gt;&gt;
        /// This is the problematic type that causes wireType == 6
        /// </summary>
        [Fact]
        public void AnalyzeEnumToConcurrentIntFloatNestedMap_ProtobufNetWireFormat()
        {
            var model = new NestedMapWithFloatModel
            {
                EnumToConcurrentIntFloatNestedMap = new Dictionary<ValueLogType, ConcurrentDictionary<int, float>>
                {
                    {
                        ValueLogType.Temperature, new ConcurrentDictionary<int, float>(
                            new Dictionary<int, float>
                            {
                                { 1, 22.5f },
                                { 2, 23.0f }
                            })
                    }
                }
            };

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"EnumToConcurrentIntFloatNestedMap serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");
            DumpWireFormat(bytes, "EnumToConcurrentIntFloatNestedMap");
        }

        /// <summary>
        /// Test round-trip for simple int-&gt;float map
        /// </summary>
        [Fact]
        public void SimpleIntFloatMap_RoundTrip()
        {
            var model = new NestedMapWithFloatModel
            {
                SimpleIntFloatMap = new Dictionary<int, float>
                {
                    { 1, 1.5f },
                    { 2, 2.5f },
                    { 100, 99.99f }
                }
            };

            // Serialize with protobuf-net
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"Protobuf-net serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");

            // Deserialize with GProtobuf
            var result = Deserializers.DeserializeNestedMapWithFloatModel(bytes);

            Assert.NotNull(result.SimpleIntFloatMap);
            Assert.Equal(3, result.SimpleIntFloatMap.Count);
            Assert.Equal(1.5f, result.SimpleIntFloatMap[1]);
            Assert.Equal(2.5f, result.SimpleIntFloatMap[2]);
            Assert.Equal(99.99f, result.SimpleIntFloatMap[100], precision: 2);
        }

        /// <summary>
        /// Test round-trip for nested Dictionary&lt;enum, Dictionary&lt;int, float&gt;&gt;
        /// </summary>
        [Fact]
        public void EnumToIntFloatNestedMap_RoundTrip()
        {
            var model = new NestedMapWithFloatModel
            {
                EnumToIntFloatNestedMap = new Dictionary<ValueLogType, Dictionary<int, float>>
                {
                    {
                        ValueLogType.Temperature, new Dictionary<int, float>
                        {
                            { 1, 22.5f },
                            { 2, 23.0f }
                        }
                    },
                    {
                        ValueLogType.Humidity, new Dictionary<int, float>
                        {
                            { 10, 50.0f },
                            { 20, 60.0f }
                        }
                    }
                }
            };

            // Serialize with protobuf-net
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"Protobuf-net serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");

            // Deserialize with GProtobuf
            var result = Deserializers.DeserializeNestedMapWithFloatModel(bytes);

            Assert.NotNull(result.EnumToIntFloatNestedMap);
            Assert.Equal(2, result.EnumToIntFloatNestedMap.Count);

            var tempDict = result.EnumToIntFloatNestedMap[ValueLogType.Temperature];
            Assert.Equal(2, tempDict.Count);
            Assert.Equal(22.5f, tempDict[1]);
            Assert.Equal(23.0f, tempDict[2]);

            var humDict = result.EnumToIntFloatNestedMap[ValueLogType.Humidity];
            Assert.Equal(2, humDict.Count);
            Assert.Equal(50.0f, humDict[10]);
            Assert.Equal(60.0f, humDict[20]);
        }

        /// <summary>
        /// Test round-trip for nested Dictionary&lt;enum, ConcurrentDictionary&lt;int, float&gt;&gt;
        /// This is the problematic test case
        /// </summary>
        [Fact]
        public void EnumToConcurrentIntFloatNestedMap_RoundTrip()
        {
            var model = new NestedMapWithFloatModel
            {
                EnumToConcurrentIntFloatNestedMap = new Dictionary<ValueLogType, ConcurrentDictionary<int, float>>
                {
                    {
                        ValueLogType.Temperature, new ConcurrentDictionary<int, float>(
                            new Dictionary<int, float>
                            {
                                { 1, 22.5f }
                            })
                    }
                }
            };

            // Serialize with protobuf-net
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, model);
            var bytes = ms.ToArray();

            _output.WriteLine($"Protobuf-net serialized length: {bytes.Length}");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes)}");
            DumpWireFormat(bytes, "ConcurrentDict roundtrip");

            // Deserialize with GProtobuf
            var result = Deserializers.DeserializeNestedMapWithFloatModel(bytes);

            Assert.NotNull(result.EnumToConcurrentIntFloatNestedMap);
            Assert.Single(result.EnumToConcurrentIntFloatNestedMap);

            var tempDict = result.EnumToConcurrentIntFloatNestedMap[ValueLogType.Temperature];
            Assert.Single(tempDict);
            Assert.Equal(22.5f, tempDict[1]);
        }

        private void DumpWireFormat(byte[] bytes, string label)
        {
            _output.WriteLine($"\n=== Wire format dump: {label} ===");
            var reader = new SpanReader(bytes);

            while (reader.Position < bytes.Length)
            {
                var startPos = reader.Position;
                var tag = reader.ReadVarUInt32();
                var fieldId = (int)(tag >> 3);
                var wireType = (global::GProtobuf.Core.WireType)(tag & 0x7);

                _output.WriteLine($"[{startPos:X2}] Tag: 0x{tag:X2}, Field: {fieldId}, WireType: {wireType} ({(int)wireType})");

                switch (wireType)
                {
                    case global::GProtobuf.Core.WireType.VarInt:
                        var varint = reader.ReadVarInt64();
                        _output.WriteLine($"  -> VarInt value: {varint}");
                        break;

                    case global::GProtobuf.Core.WireType.Fixed64b:
                        var fixed64 = reader.ReadFixedDouble();
                        _output.WriteLine($"  -> Fixed64 value: {fixed64}");
                        break;

                    case global::GProtobuf.Core.WireType.Len:
                        var len = reader.ReadVarUInt32();
                        _output.WriteLine($"  -> Length: {len} bytes");
                        // Skip the content manually using GetSlice
                        reader.GetSlice((int)len);
                        break;

                    case global::GProtobuf.Core.WireType.Fixed32b:
                        var fixed32 = reader.ReadFixedFloat();
                        _output.WriteLine($"  -> Fixed32 value: {fixed32}");
                        break;

                    default:
                        _output.WriteLine($"  -> UNKNOWN WireType: {(int)wireType}");
                        return; // Stop parsing on unknown wire type
                }
            }
        }
    }
}
