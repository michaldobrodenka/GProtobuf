﻿//using GProtobuf.Core;
//using System;
//using System.Collections.Generic;

//namespace Model.Serialization2
//{
//    public static class SpanReaders
//    {
//        public static Model.ModelClassBase ReadModelClassBase(ref SpanReader reader)
//        {
//            Model.ModelClassBase result = default(global::Model.ModelClassBase);

//            while (!reader.IsEnd)
//            {
//                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//                if (fieldId == 1)
//                {
//                    var length = reader.ReadVarInt32();
//                    var reader1 = new SpanReader(reader.GetSlice(length));
//                    result = ReadModelClass(ref reader1);
//                    continue;
//                }

//                if (result == null)
//                    throw new InvalidOperationException($"ProtoInclude field must be first. Is {fieldId} defined in ProtoInclude attributes?");

//                if (fieldId == 3122)
//                {
//                    result.A = reader.ReadDouble(wireType);
//                    continue;
//                }

//                if (fieldId == 201)
//                {
//                    result.B = reader.ReadVarInt32();
//                    continue;
//                }

//                if (fieldId == 1234568)
//                {
//                    result.Str = reader.ReadString(wireType);
//                    continue;
//                }

//                // default
//                reader.SkipField(wireType);
//            }

//            return result;
//        }
//        public static Model.ModelClass ReadModelClass(ref SpanReader reader)
//        {
//            Model.ModelClass result = new global::Model.ModelClass();

//            while (!reader.IsEnd)
//            {
//                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//                if (fieldId == 1)
//                {
//                    result.D = reader.ReadVarInt32();
//                    continue;
//                }

//                if (fieldId == 2)
//                {
//                    var length = reader.ReadVarInt32();
//                    var reader1 = new SpanReader(reader.GetSlice(length));
//                    result.Model2 = reader1.ReadClassWithCollections();
//                    continue;
//                }

//                // default
//                reader.SkipField(wireType);
//            }

//            return result;
//        }
//        public static Model.ClassWithCollections ReadClassWithCollections(ref SpanReader reader)
//        {
//            Model.ClassWithCollections result = new global::Model.ClassWithCollections();

//            while (!reader.IsEnd)
//            {
//                var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();

//                if (fieldId == 1)
//                {
//                    result.SomeInt = reader.ReadVarInt32();
//                    continue;
//                }

//                if (fieldId == 6)
//                {
//                    result.Bytes = reader.ReadByteArray();
//                    continue;
//                }

//                if (fieldId == 7)
//                {
//                    result.PackedInts = reader.ReadPackedVarIntInt32Array(false);
//                    continue;
//                }

//                if (fieldId == 8)
//                {
//                    result.PackedFixedSizeInts = reader.ReadPackedFixedSizeInt32Array();
//                    continue;
//                }

//                if (fieldId == 9)
//                {
//                    List<int> resultList = new();
//                    var wireType1 = wireType;
//                    var fieldId1 = fieldId;

//                    while (!reader.IsEnd && fieldId == fieldId1)
//                    {
//                        var number = reader.ReadVarInt32();
//                        resultList.Add(number);
//                        (wireType1, fieldId1) = reader.ReadWireTypeAndFieldId();
//                    }

//                    result.NonPackedInts = resultList.ToArray();
//                    continue;
//                }

//                // default
//                reader.SkipField(wireType);
//            }

//            return result;
//        }
//    }
//}
