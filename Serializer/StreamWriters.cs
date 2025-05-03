using GProtobuf.Core;
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Intrinsics.Wasm;

namespace Model.Serialization
{
    public static class Serializers
    {
        public static void SerializeModelClassBase(Stream stream, global::Model.ModelClassBase obj)
        {
            switch (obj)
            {
                case global::Model.ModelClass obj1:
                    SerializeModelClass(stream, obj1);
                    return;
            };

            //var reader = new SpanReader(data);
            //return SpanReaders.ReadModelClassBase(ref reader);
        }

        public static void SerializeModelClass(Stream stream, global::Model.ModelClass obj)
        {
            //var reader = new SpanReader(data);
            //return SpanReaders.ReadModelClass(ref reader);
        }

        public static void SerializeClassWithCollections(Stream stream, global::Model.ClassWithCollections obj)
        {
            var writer = new global::GProtobuf.Core.StreamWriter(stream);
            StreamWriters.WriteClassWithCollections(writer, obj);
            //var reader = new SpanReader(data);
            //return SpanReaders.ReadClassWithCollections(ref reader);
        }
    }

    public static class StreamWriters
    {
        public static void WriteModelClassBase(Stream stream, global::Model.ModelClassBase obj)
        {
            switch (obj)
            {
                case global::Model.ModelClass obj1:
                    WriteModelClass(stream, obj1);
                    return;
            }
            ;
        }
        //public static void WriteModelClass(Stream stream, global::Model.ModelClass obj)
        //{
        //    var writer = new StreamWriter(stream);
        //    writer.WriteTag(1, WireType.LengthDelimited);
        //    writer.WriteVarint32(0);
        //    writer.WriteTag(3122, WireType.Fixed64);
        //    writer.WriteDouble(obj.A);
        //    writer.WriteTag(201, WireType.Varint);
        //    writer.WriteZigZag32(obj.B);
        //    writer.WriteTag(1234568, WireType.LengthDelimited);
        //    writer.WriteString(obj.Str);
        //    if (obj.Model2 != null)
        //    {
        //        writer.WriteTag(2, WireType.LengthDelimited);
        //        writer.WriteVarint32(0);
        //        WriteClassWithCollections(writer.Stream, obj.Model2);
        //    }
        //}
        public static void WriteModelClass(Stream stream, global::Model.ModelClass obj)
        {
            //var reader = new SpanReader(data);
            //return SpanReaders.ReadModelClass(ref reader);
        }

        public static void WriteClassWithCollections(global::GProtobuf.Core.StreamWriter writer, global::Model.ClassWithCollections obj)
        {
            if (obj.SomeInt != 0)
            {
                writer.WriteTag(1, WireType.VarInt);
                writer.WriteVarint32(obj.SomeInt);
            }

            if (obj.Bytes != null)
            {
                writer.WriteTag(6, WireType.Len);
                writer.WriteVarint32(obj.Bytes.Length);
                writer.Stream.Write(obj.Bytes);
            }

            // PACKED, NON ZIGZAG
            if (obj.PackedInts != null)
            {
                writer.WriteTag(7, WireType.Len);
                var packedSize = Utils.GetVarintPackedCollectionSize(obj.PackedInts);
                
                writer.WriteVarint32(packedSize);
                foreach (var i in obj.PackedInts)
                {
                    writer.WriteVarint32(i);
                }
            }

            // packed fixed size
            if (obj.PackedFixedSizeInts != null)
            {
                writer.WriteTag(8, WireType.Len);
                writer.WriteVarint32(obj.PackedFixedSizeInts.Length << 2);
                writer.WritePackedFixedSizeIntArray(obj.PackedFixedSizeInts);
            }

            if (obj.NonPackedInts != null)
            {
                var tagAndWireType = Utils.GetTagAndWireType(9, WireType.VarInt);

                foreach(var value in obj.NonPackedInts)
                {
                    writer.WriteVarint32(tagAndWireType);
                    writer.WriteVarint32(value);
                }
            }

            if (obj.NonPackedFixedSizeInts != null)
            {
                var tagAndWireType = Utils.GetTagAndWireType(10, WireType.Fixed32b);

                foreach (var value in obj.NonPackedFixedSizeInts)
                {
                    writer.WriteVarint32(tagAndWireType);
                    writer.WriteFixedSizeInt32(value);
                }
            }
        }
    }
}
