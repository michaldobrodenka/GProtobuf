using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GProtobuf.Tests.TestModel;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Diagnostic test to understand wire format differences between protobuf-net and GProtobuf
    /// for top-level List&lt;T&gt; serialization.
    /// </summary>
    public class WireFormatDiagnosticTest
    {
        private readonly ITestOutputHelper _output;

        public WireFormatDiagnosticTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Diagnose_ProtobufNet_ListSerialization_Format()
        {
            // Create simple test data
            var list = new List<BasicTypesModel>
            {
                new BasicTypesModel { IntValue = 42, StringValue = "Hello" },
                new BasicTypesModel { IntValue = 100, StringValue = "World" }
            };

            // Serialize with protobuf-net
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, list);
            var protobufNetBytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized List<BasicTypesModel> ===");
            _output.WriteLine($"Total length: {protobufNetBytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(protobufNetBytes).Replace("-", " ")}");
            _output.WriteLine("");
            PrintWireFormatAnalysis(protobufNetBytes, "protobuf-net");

            // Serialize with GProtobuf
            using var ms2 = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeListOfBasicTypesModel(ms2, list);
            var gprotobufBytes = ms2.ToArray();

            _output.WriteLine("");
            _output.WriteLine("=== GProtobuf serialized List<BasicTypesModel> ===");
            _output.WriteLine($"Total length: {gprotobufBytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(gprotobufBytes).Replace("-", " ")}");
            _output.WriteLine("");
            PrintWireFormatAnalysis(gprotobufBytes, "GProtobuf");

            // Compare
            _output.WriteLine("");
            _output.WriteLine("=== Comparison ===");
            _output.WriteLine($"Formats are {(BytesEqual(protobufNetBytes, gprotobufBytes) ? "IDENTICAL" : "DIFFERENT")}");
        }

        [Fact]
        public void Diagnose_ProtobufNet_SingleItem_Format()
        {
            // Single item - easier to analyze
            var item = new BasicTypesModel { IntValue = 42, StringValue = "Hi" };

            // Serialize single item with protobuf-net
            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, item);
            var singleItemBytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized single BasicTypesModel ===");
            _output.WriteLine($"Total length: {singleItemBytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(singleItemBytes).Replace("-", " ")}");
            PrintWireFormatAnalysis(singleItemBytes, "single item");

            // Now serialize as List with single item
            var list = new List<BasicTypesModel> { item };
            using var ms2 = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms2, list);
            var listBytes = ms2.ToArray();

            _output.WriteLine("");
            _output.WriteLine("=== protobuf-net serialized List<BasicTypesModel> with 1 item ===");
            _output.WriteLine($"Total length: {listBytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(listBytes).Replace("-", " ")}");
            PrintWireFormatAnalysis(listBytes, "list with 1 item");

            _output.WriteLine("");
            _output.WriteLine("=== Analysis ===");
            if (listBytes.Length == singleItemBytes.Length + 1 && listBytes[0] == (byte)singleItemBytes.Length)
            {
                _output.WriteLine("Format: [length][message] - simple length-prefixed");
            }
            else if (listBytes.Length > singleItemBytes.Length)
            {
                var diff = listBytes.Length - singleItemBytes.Length;
                _output.WriteLine($"List has {diff} extra bytes compared to single item");
                _output.WriteLine("First bytes of list: " + BitConverter.ToString(listBytes, 0, Math.Min(10, listBytes.Length)));

                // Check if first byte is a field tag
                if (listBytes.Length > 0)
                {
                    var firstByte = listBytes[0];
                    var wireType = firstByte & 0x07;
                    var fieldNumber = firstByte >> 3;
                    _output.WriteLine($"First byte as tag: field={fieldNumber}, wireType={wireType}");
                }
            }
        }

        [Fact]
        public void Diagnose_ProtobufNet_SerializeWithLengthPrefix()
        {
            var item = new BasicTypesModel { IntValue = 42, StringValue = "Hi" };

            // Regular Serialize
            using var ms1 = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms1, item);
            var regularBytes = ms1.ToArray();

            // SerializeWithLengthPrefix (various styles)
            using var ms2 = new MemoryStream();
            ProtoBuf.Serializer.SerializeWithLengthPrefix(ms2, item, ProtoBuf.PrefixStyle.Base128);
            var base128Bytes = ms2.ToArray();

            using var ms3 = new MemoryStream();
            ProtoBuf.Serializer.SerializeWithLengthPrefix(ms3, item, ProtoBuf.PrefixStyle.Fixed32);
            var fixed32Bytes = ms3.ToArray();

            _output.WriteLine("=== Regular Serialize ===");
            _output.WriteLine($"Length: {regularBytes.Length}, Hex: {BitConverter.ToString(regularBytes).Replace("-", " ")}");

            _output.WriteLine("");
            _output.WriteLine("=== SerializeWithLengthPrefix (Base128) ===");
            _output.WriteLine($"Length: {base128Bytes.Length}, Hex: {BitConverter.ToString(base128Bytes).Replace("-", " ")}");

            _output.WriteLine("");
            _output.WriteLine("=== SerializeWithLengthPrefix (Fixed32) ===");
            _output.WriteLine($"Length: {fixed32Bytes.Length}, Hex: {BitConverter.ToString(fixed32Bytes).Replace("-", " ")}");

            // Now List with SerializeWithLengthPrefix for each item
            _output.WriteLine("");
            _output.WriteLine("=== Manual List serialization with SerializeWithLengthPrefix ===");
            var list = new List<BasicTypesModel> { item, new BasicTypesModel { IntValue = 100 } };
            using var ms4 = new MemoryStream();
            foreach (var i in list)
            {
                ProtoBuf.Serializer.SerializeWithLengthPrefix(ms4, i, ProtoBuf.PrefixStyle.Base128);
            }
            var manualListBytes = ms4.ToArray();
            _output.WriteLine($"Length: {manualListBytes.Length}, Hex: {BitConverter.ToString(manualListBytes).Replace("-", " ")}");
        }

        private void PrintWireFormatAnalysis(byte[] data, string label)
        {
            _output.WriteLine($"Wire format analysis for {label}:");

            int pos = 0;
            int itemCount = 0;

            while (pos < data.Length && itemCount < 10) // Limit to 10 items
            {
                if (pos >= data.Length) break;

                var startPos = pos;
                var firstByte = data[pos];
                var wireType = firstByte & 0x07;
                var fieldNumber = firstByte >> 3;

                _output.WriteLine($"  Position {pos}: byte=0x{firstByte:X2}, as tag: field={fieldNumber}, wireType={wireType}");

                // Try to read as varint (length prefix)
                pos = startPos;
                if (TryReadVarint(data, ref pos, out var varint))
                {
                    _output.WriteLine($"  Position {startPos}: as varint = {varint}");
                    if (varint > 0 && varint < 1000 && startPos + 1 + (int)varint <= data.Length)
                    {
                        _output.WriteLine($"    Could be length prefix for {varint} bytes");
                    }
                }

                itemCount++;
                pos = startPos + 1; // Move to next byte for analysis
            }
        }

        private bool TryReadVarint(byte[] data, ref int pos, out ulong value)
        {
            value = 0;
            int shift = 0;

            while (pos < data.Length && shift < 64)
            {
                byte b = data[pos++];
                value |= (ulong)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                    return true;

                shift += 7;
            }

            return false;
        }

        private bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        [Fact]
        public void Diagnose_ProtobufNet_ListOfInt_Format()
        {
            // List<int> serialization format in protobuf-net
            var list = new List<int> { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, list);
            var bytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized List<int> ===");
            _output.WriteLine($"Total length: {bytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes).Replace("-", " ")}");
            _output.WriteLine("");

            // Analyze wire format
            int pos = 0;
            while (pos < bytes.Length)
            {
                var startPos = pos;
                var firstByte = bytes[pos];
                var wireType = firstByte & 0x07;
                var fieldNumber = firstByte >> 3;
                _output.WriteLine($"Position {pos}: byte=0x{firstByte:X2}, field={fieldNumber}, wireType={wireType}");
                pos++;

                if (wireType == 2 && pos < bytes.Length)
                {
                    // Length-delimited - read length
                    var length = bytes[pos++];
                    _output.WriteLine($"  Length-delimited block: {length} bytes");
                    _output.WriteLine($"  Content: {BitConverter.ToString(bytes, pos, Math.Min(length, bytes.Length - pos)).Replace("-", " ")}");
                    pos += length;
                }
                else if (wireType == 0 && pos < bytes.Length)
                {
                    // Varint
                    _output.WriteLine($"  Varint value follows");
                    // Skip varint
                    while (pos < bytes.Length && (bytes[pos] & 0x80) != 0) pos++;
                    if (pos < bytes.Length) pos++;
                }
            }
        }

        [Fact]
        public void Diagnose_ProtobufNet_ListOfString_Format()
        {
            var list = new List<string> { "Hello", "World" };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, list);
            var bytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized List<string> ===");
            _output.WriteLine($"Total length: {bytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes).Replace("-", " ")}");
        }

        [Fact]
        public void Diagnose_ProtobufNet_DictionaryIntInt_Format()
        {
            var dict = new Dictionary<int, int> { { 1, 100 }, { 2, 200 } };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, dict);
            var bytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized Dictionary<int, int> ===");
            _output.WriteLine($"Total length: {bytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes).Replace("-", " ")}");
            _output.WriteLine("");

            // Analyze wire format
            int pos = 0;
            int entryCount = 0;
            while (pos < bytes.Length)
            {
                var firstByte = bytes[pos];
                var wireType = firstByte & 0x07;
                var fieldNumber = firstByte >> 3;
                _output.WriteLine($"Position {pos}: byte=0x{firstByte:X2}, field={fieldNumber}, wireType={wireType}");
                pos++;

                if (wireType == 2 && pos < bytes.Length)
                {
                    var length = bytes[pos++];
                    _output.WriteLine($"  Entry {++entryCount}: {length} bytes");
                    _output.WriteLine($"  Content: {BitConverter.ToString(bytes, pos, Math.Min(length, bytes.Length - pos)).Replace("-", " ")}");
                    pos += length;
                }
            }
        }

        [Fact]
        public void Diagnose_ProtobufNet_DictionaryStringInt_Format()
        {
            var dict = new Dictionary<string, int> { { "key1", 100 }, { "key2", 200 } };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, dict);
            var bytes = ms.ToArray();

            _output.WriteLine("=== protobuf-net serialized Dictionary<string, int> ===");
            _output.WriteLine($"Total length: {bytes.Length} bytes");
            _output.WriteLine($"Hex: {BitConverter.ToString(bytes).Replace("-", " ")}");
        }
    }
}
