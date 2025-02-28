//using GProtobuf.Core;
//using System;
//using Model;

//using System.Collections.Generic;
//using System.Linq;
//using System.Security.AccessControl;
//using System.Text;
//using System.Threading.Tasks;

//namespace Model.Serialization
//{
//    public static class Deserializers
//    {
//        //public static ModelClass DeserializeModel(ReadOnlySpan<byte> data)
//        //{
//        //    var reader = new SpanReader(data);

//        //    return reader.ReadModel();
//        //}

//        public static ModelClassBase DeserializeModelClassBase(ReadOnlySpan<byte> data)
//        {
//            var reader = new SpanReader(data);

//            //return reader.ReadModelClassBase();
//            return global::Model.Serialization.SpanReaders.ReadModelClassBase(ref reader);
//        }

//        //public void SerializeModelClassBase(Memory<byte> data)
//        //{

//        //}
//    }

//    //public static class SpanReaders
//    //{
//    //    //public static Model.ModelClassBase ReadModelClassBase(this ref SpanReader reader)
//    //    //{
//    //    //    ModelClassBase result = null;

//    //    //    while (!reader.IsEnd)
//    //    //    {
//    //    //        var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//    //    //        if (fieldId == 1)
//    //    //        {
//    //    //            var length = reader.ReadVarInt32();
//    //    //            //result = new ModelClass();

//    //    //            var readerModelClass = new SpanReader(reader.GetSlice(length));

//    //    //            result = readerModelClass.ReadModelClass();
//    //    //            continue;
//    //    //        }

//    //    //        if (result == null)
//    //    //            throw new InvalidOperationException($"ProtoInclude field must be first. Is {fieldId} defined in ProtoInclude attributes?");

//    //    //        if (fieldId == 3122)
//    //    //        {
//    //    //            result.A = reader.ReadDouble(wireType);
//    //    //            continue;
//    //    //        }

//    //    //        if (fieldId == 201)
//    //    //        {
//    //    //            result.B = reader.ReadInt32(wireType, zigZag: false);
//    //    //            continue;
//    //    //        }

//    //    //        if (fieldId == 1234568)
//    //    //        {
//    //    //            result.Str = reader.ReadString(wireType);
//    //    //            continue;
//    //    //        }

//    //    //        // default
//    //    //        reader.SkipField(wireType);
//    //    //    }

//    //    //    return result;
//    //    //}

//    //    //public static Model.ModelClass ReadModelClass(this ref SpanReader reader)
//    //    //{
//    //    //    var result = new ModelClass();

//    //    //    while (!reader.IsEnd)
//    //    //    {
//    //    //        var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//    //    //        if (fieldId == 1)
//    //    //        {
//    //    //            result.D = reader.ReadInt32(wireType, zigZag: false);
//    //    //            continue;
//    //    //        }

//    //    //        // default
//    //    //        reader.SkipField(wireType);
//    //    //    }

//    //    //    return result;
//    //    //}

//    //    ////public static Model.ModelClass ReadModel(this ref SpanReader reader)
//    //    ////{
//    //    ////    var result = new ModelClass();

//    //    ////    while (!reader.IsEnd)
//    //    ////    {
//    //    ////        var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//    //    ////        if (fieldId == 3122)
//    //    ////        {
//    //    ////            result.A = reader.ReadDouble(wireType);
//    //    ////            continue;
//    //    ////        }

//    //    ////        if (fieldId == 201)
//    //    ////        {
//    //    ////            result.B = reader.ReadInt32(wireType, zigZag: false);
//    //    ////            continue;
//    //    ////        }

//    //    ////        if (fieldId == 1234568)
//    //    ////        {
//    //    ////            result.Str = reader.ReadString(wireType);
//    //    ////            continue;
//    //    ////        }

//    //    ////        // default
//    //    ////        reader.SkipField(wireType);
//    //    ////    }

//    //    ////    return result;
//    //    ////}

//    //}
//}
