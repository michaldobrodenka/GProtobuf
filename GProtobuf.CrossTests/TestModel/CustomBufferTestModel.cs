using System;
using System.Text;
using ProtoBuf;

// Aliases for custom buffer attributes from GProtobuf.Core
using ProtoMemberBufferSizeAttribute = GProtobuf.Core.ProtoMemberBufferSizeAttribute;
using ProtoMemberBufferFillAttribute = GProtobuf.Core.ProtoMemberBufferFillAttribute;
using ProtoMemberBufferReadAttribute = GProtobuf.Core.ProtoMemberBufferReadAttribute;

namespace GProtobuf.CrossTests.TestModel
{
    /// <summary>
    /// Test model demonstrating basic custom buffer serialization.
    /// Custom buffer allows user-defined serialization logic for specific fields.
    /// </summary>
    [ProtoContract]
    public class BasicCustomBufferModel
    {
        [ProtoMember(1)]
        public int Id { get; set; }

        [ProtoMember(2)]
        public string Name { get; set; }

        // Custom buffer field at position 10 (no ProtoMember attribute needed)
        private byte[] _customData;

        /// <summary>
        /// Gets or sets the custom data (for test verification).
        /// </summary>
        public byte[] CustomData
        {
            get => _customData;
            set => _customData = value;
        }

        [ProtoMemberBufferSize(10)]
        public int GetCustomDataSize() => _customData?.Length ?? 0;

        [ProtoMemberBufferFill(10)]
        public void FillCustomData(Span<byte> buffer)
        {
            if (_customData != null)
            {
                _customData.AsSpan().CopyTo(buffer);
            }
        }

        [ProtoMemberBufferRead(10)]
        public void ReadCustomData(ReadOnlySpan<byte> data)
        {
            _customData = data.ToArray();
        }
    }

    /// <summary>
    /// Test model with multiple custom buffer fields.
    /// </summary>
    [ProtoContract]
    public class MultipleCustomBufferModel
    {
        [ProtoMember(1)]
        public int Version { get; set; }

        // First custom buffer field (field 5)
        private byte[] _headerData;
        public byte[] HeaderData
        {
            get => _headerData;
            set => _headerData = value;
        }

        [ProtoMemberBufferSize(5)]
        public int GetHeaderSize() => _headerData?.Length ?? 0;

        [ProtoMemberBufferFill(5)]
        public void FillHeader(Span<byte> buffer)
        {
            if (_headerData != null)
                _headerData.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(5)]
        public void ReadHeader(ReadOnlySpan<byte> data)
        {
            _headerData = data.ToArray();
        }

        // Second custom buffer field (field 10)
        private byte[] _payloadData;
        public byte[] PayloadData
        {
            get => _payloadData;
            set => _payloadData = value;
        }

        [ProtoMemberBufferSize(10)]
        public int GetPayloadSize() => _payloadData?.Length ?? 0;

        [ProtoMemberBufferFill(10)]
        public void FillPayload(Span<byte> buffer)
        {
            if (_payloadData != null)
                _payloadData.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(10)]
        public void ReadPayload(ReadOnlySpan<byte> data)
        {
            _payloadData = data.ToArray();
        }

        // Third custom buffer field (field 15)
        private byte[] _footerData;
        public byte[] FooterData
        {
            get => _footerData;
            set => _footerData = value;
        }

        [ProtoMemberBufferSize(15)]
        public int GetFooterSize() => _footerData?.Length ?? 0;

        [ProtoMemberBufferFill(15)]
        public void FillFooter(Span<byte> buffer)
        {
            if (_footerData != null)
                _footerData.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(15)]
        public void ReadFooter(ReadOnlySpan<byte> data)
        {
            _footerData = data.ToArray();
        }
    }

    /// <summary>
    /// Test model with custom buffer for complex data (compressed/encrypted).
    /// Demonstrates real-world use case: storing preprocessed binary data.
    /// </summary>
    [ProtoContract]
    public class CompressedDataModel
    {
        [ProtoMember(1)]
        public string Description { get; set; }

        [ProtoMember(2)]
        public long Timestamp { get; set; }

        // Simulated compressed data buffer (field 20)
        private byte[] _compressedPayload;
        private string _originalText;

        public string OriginalText
        {
            get => _originalText;
            set
            {
                _originalText = value;
                // Simulate "compression" - just encode to bytes
                _compressedPayload = value != null ? Encoding.UTF8.GetBytes(value) : null;
            }
        }

        public byte[] CompressedPayload => _compressedPayload;

        [ProtoMemberBufferSize(20)]
        public int GetCompressedPayloadSize() => _compressedPayload?.Length ?? 0;

        [ProtoMemberBufferFill(20)]
        public void FillCompressedPayload(Span<byte> buffer)
        {
            if (_compressedPayload != null)
                _compressedPayload.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(20)]
        public void ReadCompressedPayload(ReadOnlySpan<byte> data)
        {
            _compressedPayload = data.ToArray();
            // Simulate "decompression" - just decode from bytes
            _originalText = data.Length > 0 ? Encoding.UTF8.GetString(data) : null;
        }
    }

    /// <summary>
    /// Test model with only custom buffer fields (no regular ProtoMember).
    /// </summary>
    [ProtoContract]
    public class OnlyCustomBufferModel
    {
        private byte[] _data1;
        private byte[] _data2;

        public byte[] Data1
        {
            get => _data1;
            set => _data1 = value;
        }

        public byte[] Data2
        {
            get => _data2;
            set => _data2 = value;
        }

        [ProtoMemberBufferSize(1)]
        public int GetData1Size() => _data1?.Length ?? 0;

        [ProtoMemberBufferFill(1)]
        public void FillData1(Span<byte> buffer)
        {
            if (_data1 != null)
                _data1.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(1)]
        public void ReadData1(ReadOnlySpan<byte> data)
        {
            _data1 = data.ToArray();
        }

        [ProtoMemberBufferSize(2)]
        public int GetData2Size() => _data2?.Length ?? 0;

        [ProtoMemberBufferFill(2)]
        public void FillData2(Span<byte> buffer)
        {
            if (_data2 != null)
                _data2.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(2)]
        public void ReadData2(ReadOnlySpan<byte> data)
        {
            _data2 = data.ToArray();
        }
    }

    /// <summary>
    /// Test model demonstrating custom buffer with empty/null data.
    /// </summary>
    [ProtoContract]
    public class EmptyCustomBufferModel
    {
        [ProtoMember(1)]
        public int Marker { get; set; }

        private byte[] _optionalData;

        public byte[] OptionalData
        {
            get => _optionalData;
            set => _optionalData = value;
        }

        [ProtoMemberBufferSize(5)]
        public int GetOptionalDataSize() => _optionalData?.Length ?? 0;

        [ProtoMemberBufferFill(5)]
        public void FillOptionalData(Span<byte> buffer)
        {
            if (_optionalData != null && _optionalData.Length > 0)
                _optionalData.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(5)]
        public void ReadOptionalData(ReadOnlySpan<byte> data)
        {
            _optionalData = data.Length > 0 ? data.ToArray() : null;
        }
    }

    /// <summary>
    /// Test model demonstrating custom buffer with large data.
    /// </summary>
    [ProtoContract]
    public class LargeCustomBufferModel
    {
        [ProtoMember(1)]
        public string Tag { get; set; }

        private byte[] _largeData;

        public byte[] LargeData
        {
            get => _largeData;
            set => _largeData = value;
        }

        [ProtoMemberBufferSize(100)]
        public int GetLargeDataSize() => _largeData?.Length ?? 0;

        [ProtoMemberBufferFill(100)]
        public void FillLargeData(Span<byte> buffer)
        {
            if (_largeData != null)
                _largeData.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(100)]
        public void ReadLargeData(ReadOnlySpan<byte> data)
        {
            _largeData = data.ToArray();
        }
    }

    /// <summary>
    /// Test model demonstrating custom buffer with transformation (XOR "encryption").
    /// </summary>
    [ProtoContract]
    public class TransformedBufferModel
    {
        [ProtoMember(1)]
        public int Key { get; set; }

        private byte[] _plainData;

        public byte[] PlainData
        {
            get => _plainData;
            set => _plainData = value;
        }

        [ProtoMemberBufferSize(10)]
        public int GetTransformedDataSize() => _plainData?.Length ?? 0;

        [ProtoMemberBufferFill(10)]
        public void FillTransformedData(Span<byte> buffer)
        {
            if (_plainData == null) return;

            // XOR "encryption" during serialization
            byte xorKey = (byte)(Key & 0xFF);
            for (int i = 0; i < _plainData.Length; i++)
            {
                buffer[i] = (byte)(_plainData[i] ^ xorKey);
            }
        }

        [ProtoMemberBufferRead(10)]
        public void ReadTransformedData(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                _plainData = null;
                return;
            }

            // XOR "decryption" during deserialization
            _plainData = new byte[data.Length];
            byte xorKey = (byte)(Key & 0xFF);
            for (int i = 0; i < data.Length; i++)
            {
                _plainData[i] = (byte)(data[i] ^ xorKey);
            }
        }
    }

    /// <summary>
    /// Test model combining regular ProtoMembers with custom buffer in various positions.
    /// </summary>
    [ProtoContract]
    public class InterleavedFieldsModel
    {
        [ProtoMember(1)]
        public int FirstField { get; set; }

        // Custom buffer at position 2
        private byte[] _customBuffer2;
        public byte[] CustomBuffer2
        {
            get => _customBuffer2;
            set => _customBuffer2 = value;
        }

        [ProtoMemberBufferSize(2)]
        public int GetCustomBuffer2Size() => _customBuffer2?.Length ?? 0;

        [ProtoMemberBufferFill(2)]
        public void FillCustomBuffer2(Span<byte> buffer)
        {
            if (_customBuffer2 != null)
                _customBuffer2.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(2)]
        public void ReadCustomBuffer2(ReadOnlySpan<byte> data)
        {
            _customBuffer2 = data.ToArray();
        }

        [ProtoMember(3)]
        public string MiddleField { get; set; }

        // Custom buffer at position 4
        private byte[] _customBuffer4;
        public byte[] CustomBuffer4
        {
            get => _customBuffer4;
            set => _customBuffer4 = value;
        }

        [ProtoMemberBufferSize(4)]
        public int GetCustomBuffer4Size() => _customBuffer4?.Length ?? 0;

        [ProtoMemberBufferFill(4)]
        public void FillCustomBuffer4(Span<byte> buffer)
        {
            if (_customBuffer4 != null)
                _customBuffer4.AsSpan().CopyTo(buffer);
        }

        [ProtoMemberBufferRead(4)]
        public void ReadCustomBuffer4(ReadOnlySpan<byte> data)
        {
            _customBuffer4 = data.ToArray();
        }

        [ProtoMember(5)]
        public double LastField { get; set; }
    }
}
