using Xunit;
using Xunit.Abstractions;
using System;
using System.Linq;
using System.Text;
using ProtoBuf;
using GProtobuf.Tests.TestModel;
using GProtobuf.Core;

namespace GProtobuf.Tests.DiagnosticTests
{
    /// <summary>
    /// Diagnostic tests to investigate wire format differences between protobuf-net and GProtobuf.
    /// These tests help identify encoding/decoding issues for array types.
    /// </summary>
    public class ArrayEncodingDiagnostics : BaseSerializationTest
    {
        private readonly ITestOutputHelper _output;

        public ArrayEncodingDiagnostics(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Wire Format Helpers

        private void DumpWireFormat(byte[] data, string label)
        {
            _output.WriteLine($"\n=== {label} ({data.Length} bytes) ===");
            _output.WriteLine("Hex: " + BitConverter.ToString(data).Replace("-", " "));

            // Parse as protobuf wire format
            try
            {
                var reader = new SpanReader(data);
                while (!reader.EndOfData)
                {
                    var pos = reader.Position;
                    var (wireType, fieldId) = reader.ReadKey();
                    _output.WriteLine($"  @{pos}: Field {fieldId}, WireType {wireType}");

                    if (wireType == global::GProtobuf.Core.WireType.Len)
                    {
                        int length = reader.ReadVarInt32();
                        _output.WriteLine($"    Length: {length} bytes");
                        if (length <= 64) // Only dump small payloads
                        {
                            var payload = reader.GetSlice(length);
                            _output.WriteLine($"    Data: {BitConverter.ToString(payload.ToArray()).Replace("-", " ")}");
                        }
                        else
                        {
                            reader.Position += length;
                            _output.WriteLine($"    Data: <{length} bytes, too large to dump>");
                        }
                    }
                    else if (wireType == global::GProtobuf.Core.WireType.VarInt)
                    {
                        var value = reader.ReadVarInt64();
                        _output.WriteLine($"    Value: {value}");
                    }
                    else if (wireType == global::GProtobuf.Core.WireType.Fixed32b)
                    {
                        var value = reader.ReadFixedUInt32();
                        _output.WriteLine($"    Value: {value} (0x{value:X8})");
                    }
                    else if (wireType == global::GProtobuf.Core.WireType.Fixed64b)
                    {
                        var value = reader.ReadFixedUInt64();
                        _output.WriteLine($"    Value: {value} (0x{value:X16})");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ERROR parsing wire format: {ex.Message}");
            }
        }

        private void CompareWireFormats(byte[] pnetData, byte[] gprotoData, string testName)
        {
            _output.WriteLine($"\n{'='*80}");
            _output.WriteLine($"TEST: {testName}");
            _output.WriteLine($"{'='*80}");

            DumpWireFormat(pnetData, "protobuf-net");
            DumpWireFormat(gprotoData, "GProtobuf");

            bool match = pnetData.SequenceEqual(gprotoData);
            _output.WriteLine($"\n>>> Wire formats MATCH: {match}");

            if (!match)
            {
                _output.WriteLine($">>> SIZE: protobuf-net={pnetData.Length}, GProtobuf={gprotoData.Length}");

                // Find first difference
                int minLen = Math.Min(pnetData.Length, gprotoData.Length);
                for (int i = 0; i < minLen; i++)
                {
                    if (pnetData[i] != gprotoData[i])
                    {
                        _output.WriteLine($">>> FIRST DIFF at byte {i}: protobuf-net=0x{pnetData[i]:X2}, GProtobuf=0x{gprotoData[i]:X2}");
                        break;
                    }
                }
            }
        }

        #endregion

        #region Short Arrays (Field 26-29)

        [Fact]
        public void Diagnose_ShortArray_NonPacked()
        {
            var model = new PrimitiveArraysTestModel
            {
                ShortArray = new short[] { short.MinValue, -1000, 0, 1000, short.MaxValue }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Short[] Non-Packed");

            // Try cross-deserialization
            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf: [{string.Join(", ", pnetToGproto.ShortArray ?? Array.Empty<short>())}]");

            var gprotoToPnet = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotoData);
            _output.WriteLine($"GProtobuf → Protobuf-net: [{string.Join(", ", gprotoToPnet.ShortArray ?? Array.Empty<short>())}]");
        }

        [Fact]
        public void Diagnose_ShortArrayPacked_Default()
        {
            var model = new PrimitiveArraysTestModel
            {
                ShortArrayPacked = new short[] { 100, 200, 300, 400 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Short[] Packed (Default/VarInt)");

            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf: [{string.Join(", ", pnetToGproto.ShortArrayPacked ?? Array.Empty<short>())}]");

            var gprotoToPnet = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotoData);
            _output.WriteLine($"GProtobuf → Protobuf-net: [{string.Join(", ", gprotoToPnet.ShortArrayPacked ?? Array.Empty<short>())}]");
        }

        [Fact]
        public void Diagnose_ShortArrayPackedZigZag()
        {
            var model = new PrimitiveArraysTestModel
            {
                ShortArrayPackedZigZag = new short[] { -500, -100, 0, 100, 500 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Short[] Packed ZigZag");

            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf: [{string.Join(", ", pnetToGproto.ShortArrayPackedZigZag ?? Array.Empty<short>())}]");

            var gprotoToPnet = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotoData);
            _output.WriteLine($"GProtobuf → Protobuf-net: [{string.Join(", ", gprotoToPnet.ShortArrayPackedZigZag ?? Array.Empty<short>())}]");
        }

        [Fact]
        public void Diagnose_ShortArrayPackedFixed()
        {
            var model = new PrimitiveArraysTestModel
            {
                ShortArrayPackedFixed = new short[] { 1000, 2000, 3000, -1000 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Short[] Packed FixedSize ⚠️ FAILING TEST");

            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf: [{string.Join(", ", pnetToGproto.ShortArrayPackedFixed ?? Array.Empty<short>())}]");
            _output.WriteLine($"  Expected: [1000, 2000, 3000, -1000]");

            var gprotoToPnet = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotoData);
            _output.WriteLine($"GProtobuf → Protobuf-net: [{string.Join(", ", gprotoToPnet.ShortArrayPackedFixed ?? Array.Empty<short>())}]");
        }

        #endregion

        #region UShort Arrays (Field 30-32)

        [Fact]
        public void Diagnose_UShortArray_NonPacked()
        {
            var model = new PrimitiveArraysTestModel
            {
                UShortArray = new ushort[] { 0, 1000, 30000, ushort.MaxValue }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "UShort[] Non-Packed");
        }

        [Fact]
        public void Diagnose_UShortArrayPacked_Default()
        {
            var model = new PrimitiveArraysTestModel
            {
                UShortArrayPacked = new ushort[] { 100, 200, 300, 400, 500 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "UShort[] Packed (Default/VarInt)");
        }

        [Fact]
        public void Diagnose_UShortArrayPackedFixed()
        {
            var model = new PrimitiveArraysTestModel
            {
                UShortArrayPackedFixed = new ushort[] { 1000, 2000, 3000, 4000 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "UShort[] Packed FixedSize");

            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf: [{string.Join(", ", pnetToGproto.UShortArrayPackedFixed ?? Array.Empty<ushort>())}]");
        }

        #endregion

        #region Int Arrays (for comparison)

        [Fact]
        public void Diagnose_IntArrayPackedFixed()
        {
            var model = new PrimitiveArraysTestModel
            {
                // Note: Need to find int[] with FixedSize in the model
                // Using LongArrayPackedFixed as reference
                LongArrayPackedFixed = new long[] { 1000, 2000, 3000, -1000 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Long[] Packed FixedSize (reference)");
        }

        #endregion

        #region Float Arrays

        [Fact]
        public void Diagnose_FloatArrayPacked()
        {
            var model = new PrimitiveArraysTestModel
            {
                FloatArrayPacked = new float[] { 1.5f, 2.5f, 3.5f, 4.5f }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Float[] Packed (Fixed32)");
        }

        #endregion

        #region Double Arrays

        [Fact]
        public void Diagnose_DoubleArrayPacked()
        {
            var model = new PrimitiveArraysTestModel
            {
                DoubleArrayPacked = new double[] { 1.5, 2.5, 3.5, 4.5 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Double[] Packed (Fixed64)");
        }

        #endregion

        #region SByte Arrays

        [Fact]
        public void Diagnose_SByteArrayPacked()
        {
            var model = new PrimitiveArraysTestModel
            {
                SByteArrayPacked = new sbyte[] { -100, -50, 0, 50, 100 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "SByte[] Packed (Default/VarInt)");
        }

        [Fact]
        public void Diagnose_SByteArrayPackedZigZag()
        {
            var model = new PrimitiveArraysTestModel
            {
                SByteArrayPackedZigZag = new sbyte[] { -100, -50, 0, 50, 100 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "SByte[] Packed ZigZag");
        }

        #endregion

        #region Empty Arrays

        [Fact]
        public void Diagnose_EmptyArrays()
        {
            var model = new PrimitiveArraysTestModel
            {
                FloatArrayEmpty = new float[0],
                DoubleArrayPackedEmpty = new double[0]
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            CompareWireFormats(pnetData, gprotoData, "Empty Arrays");

            var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
            _output.WriteLine($"\nProtobuf-net → GProtobuf:");
            _output.WriteLine($"  FloatArrayEmpty: {(pnetToGproto.FloatArrayEmpty == null ? "null" : $"[{pnetToGproto.FloatArrayEmpty.Length}]")}");
            _output.WriteLine($"  DoubleArrayPackedEmpty: {(pnetToGproto.DoubleArrayPackedEmpty == null ? "null" : $"[{pnetToGproto.DoubleArrayPackedEmpty.Length}]")}");
        }

        #endregion

        #region Comprehensive Test - All Types

        [Fact]
        public void Diagnose_AllPrimitiveArrayTypes()
        {
            var model = new PrimitiveArraysTestModel
            {
                // Float
                FloatArray = new float[] { 1.0f, 2.0f },
                FloatArrayPacked = new float[] { 3.0f, 4.0f },

                // Double
                DoubleArray = new double[] { 1.0, 2.0 },
                DoubleArrayPacked = new double[] { 3.0, 4.0 },

                // Long
                LongArray = new long[] { 1, 2 },
                LongArrayPacked = new long[] { 3, 4 },
                LongArrayPackedZigZag = new long[] { -1, 1 },
                LongArrayPackedFixed = new long[] { 1000, 2000 },

                // Bool
                BoolArray = new bool[] { true, false },
                BoolArrayPacked = new bool[] { false, true },

                // SByte
                SByteArray = new sbyte[] { -1, 0, 1 },
                SByteArrayPacked = new sbyte[] { -2, 2 },
                SByteArrayPackedZigZag = new sbyte[] { -3, 3 },

                // Short
                ShortArray = new short[] { -100, 100 },
                ShortArrayPacked = new short[] { 200, 300 },
                ShortArrayPackedZigZag = new short[] { -200, 200 },
                ShortArrayPackedFixed = new short[] { 1000, 2000 },

                // UShort
                UShortArray = new ushort[] { 100, 200 },
                UShortArrayPacked = new ushort[] { 300, 400 },
                UShortArrayPackedFixed = new ushort[] { 1000, 2000 },

                // UInt
                UIntArray = new uint[] { 100, 200 },
                UIntArrayPacked = new uint[] { 300, 400 },
                UIntArrayPackedFixed = new uint[] { 1000, 2000 },

                // ULong
                ULongArray = new ulong[] { 100, 200 },
                ULongArrayPacked = new ulong[] { 300, 400 },
                ULongArrayPackedFixed = new ulong[] { 1000, 2000 }
            };

            var pnetData = SerializeWithProtobufNet(model);
            var gprotoData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializePrimitiveArraysTestModel);

            _output.WriteLine($"TOTAL SIZE: protobuf-net={pnetData.Length} bytes, GProtobuf={gprotoData.Length} bytes");
            _output.WriteLine($"MATCH: {pnetData.SequenceEqual(gprotoData)}");

            // Try deserialization both ways
            try
            {
                var pnetToGproto = TestModel.Serialization.Deserializers.DeserializePrimitiveArraysTestModel(pnetData);
                _output.WriteLine("\n✅ Protobuf-net → GProtobuf: SUCCESS");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"\n❌ Protobuf-net → GProtobuf: FAILED - {ex.Message}");
            }

            try
            {
                var gprotoToPnet = DeserializeWithProtobufNet<PrimitiveArraysTestModel>(gprotoData);
                _output.WriteLine("✅ GProtobuf → Protobuf-net: SUCCESS");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ GProtobuf → Protobuf-net: FAILED - {ex.Message}");
            }
        }

        #endregion
    }
}
