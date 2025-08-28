using System;
using System.Collections.Generic;
using System.IO;
using GProtobuf.Tests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class TupleModelTests
    {
        private TupleModel CreateTestModel()
        {
            return new TupleModel
            {
                IntStringTuple = new Tuple<int, string>(42, "test"),
                DoubleTuple = new Tuple<double, double>(3.14, 2.71),
                StringIntTuple = new Tuple<string, int>("hello", 100),
                LongBoolTuple = new Tuple<long, bool>(1234567890L, true),
                FloatTuple = new Tuple<float, float>(1.23f, 4.56f),
                TupleList = new List<Tuple<int, string>>
                {
                    new Tuple<int, string>(1, "one"),
                    new Tuple<int, string>(2, "two"),
                    new Tuple<int, string>(3, "three")
                },
                EnumStringTuple = new Tuple<TestEnum, string>(TestEnum.Second, "enum test"),
                CharIntTuple = new Tuple<char, int>('A', 65)
            };
        }

        [Fact]
        public void Test_PG_IntStringTuple()
        {
            var original = new TupleModel
            {
                IntStringTuple = new Tuple<int, string>(123, "test string")
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.IntStringTuple);
            Assert.Equal(123, deserialized.IntStringTuple.Item1);
            Assert.Equal("test string", deserialized.IntStringTuple.Item2);
        }

        [Fact]
        public void Test_GG_IntStringTuple()
        {
            var original = new TupleModel
            {
                IntStringTuple = new Tuple<int, string>(456, "another test")
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.IntStringTuple);
            Assert.Equal(456, deserialized.IntStringTuple.Item1);
            Assert.Equal("another test", deserialized.IntStringTuple.Item2);
        }

        [Fact]
        public void Test_GP_IntStringTuple()
        {
            var original = new TupleModel
            {
                IntStringTuple = new Tuple<int, string>(789, "protobuf test")
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<TupleModel>(ms);
            
            Assert.NotNull(deserialized.IntStringTuple);
            Assert.Equal(789, deserialized.IntStringTuple.Item1);
            Assert.Equal("protobuf test", deserialized.IntStringTuple.Item2);
        }

        [Fact]
        public void Test_PG_DoubleTuple()
        {
            var original = new TupleModel
            {
                DoubleTuple = new Tuple<double, double>(3.14159, 2.71828)
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(3.14159, deserialized.DoubleTuple.Item1, 5);
            Assert.Equal(2.71828, deserialized.DoubleTuple.Item2, 5);
        }

        [Fact]
        public void Test_GG_DoubleTuple()
        {
            var original = new TupleModel
            {
                DoubleTuple = new Tuple<double, double>(1.23456, 7.89012)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(1.23456, deserialized.DoubleTuple.Item1, 5);
            Assert.Equal(7.89012, deserialized.DoubleTuple.Item2, 5);
        }

        [Fact]
        public void Test_GP_DoubleTuple()
        {
            var original = new TupleModel
            {
                DoubleTuple = new Tuple<double, double>(9.87654, 3.21098)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<TupleModel>(ms);
            
            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(9.87654, deserialized.DoubleTuple.Item1, 5);
            Assert.Equal(3.21098, deserialized.DoubleTuple.Item2, 5);
        }

        [Fact]
        public void Test_PG_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            AssertTupleModelsEqual(original, deserialized);
        }

        [Fact]
        public void Test_GG_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            AssertTupleModelsEqual(original, deserialized);
        }

        [Fact]
        public void Test_GP_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<TupleModel>(ms);
            
            AssertTupleModelsEqual(original, deserialized);
        }

        [Fact]
        public void Test_PG_TupleList()
        {
            var original = new TupleModel
            {
                TupleList = new List<Tuple<int, string>>
                {
                    new Tuple<int, string>(10, "ten"),
                    new Tuple<int, string>(20, "twenty"),
                    new Tuple<int, string>(30, "thirty")
                }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.TupleList);
            Assert.Equal(3, deserialized.TupleList.Count);
            Assert.Equal(10, deserialized.TupleList[0].Item1);
            Assert.Equal("ten", deserialized.TupleList[0].Item2);
            Assert.Equal(20, deserialized.TupleList[1].Item1);
            Assert.Equal("twenty", deserialized.TupleList[1].Item2);
            Assert.Equal(30, deserialized.TupleList[2].Item1);
            Assert.Equal("thirty", deserialized.TupleList[2].Item2);
        }

        [Fact]
        public void Test_GG_TupleList()
        {
            var original = new TupleModel
            {
                TupleList = new List<Tuple<int, string>>
                {
                    new Tuple<int, string>(100, "hundred"),
                    new Tuple<int, string>(200, "two hundred")
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.TupleList);
            Assert.Equal(2, deserialized.TupleList.Count);
            Assert.Equal(100, deserialized.TupleList[0].Item1);
            Assert.Equal("hundred", deserialized.TupleList[0].Item2);
            Assert.Equal(200, deserialized.TupleList[1].Item1);
            Assert.Equal("two hundred", deserialized.TupleList[1].Item2);
        }

        [Fact]
        public void Test_GP_TupleList()
        {
            var original = new TupleModel
            {
                TupleList = new List<Tuple<int, string>>
                {
                    new Tuple<int, string>(-1, "negative"),
                    new Tuple<int, string>(0, "zero"),
                    new Tuple<int, string>(1, "positive")
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<TupleModel>(ms);
            
            Assert.NotNull(deserialized.TupleList);
            Assert.Equal(3, deserialized.TupleList.Count);
            Assert.Equal(-1, deserialized.TupleList[0].Item1);
            Assert.Equal("negative", deserialized.TupleList[0].Item2);
            Assert.Equal(0, deserialized.TupleList[1].Item1);
            Assert.Equal("zero", deserialized.TupleList[1].Item2);
            Assert.Equal(1, deserialized.TupleList[2].Item1);
            Assert.Equal("positive", deserialized.TupleList[2].Item2);
        }

        [Fact]
        public void Test_PG_EnumStringTuple()
        {
            var original = new TupleModel
            {
                EnumStringTuple = new Tuple<TestEnum, string>(TestEnum.Third, "third value")
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.EnumStringTuple);
            Assert.Equal(TestEnum.Third, deserialized.EnumStringTuple.Item1);
            Assert.Equal("third value", deserialized.EnumStringTuple.Item2);
        }

        [Fact]
        public void Test_CharIntTuple()
        {
            var original = new TupleModel
            {
                CharIntTuple = new Tuple<char, int>('Z', 90)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTupleModel(bytes);
            
            Assert.NotNull(deserialized.CharIntTuple);
            Assert.Equal('Z', deserialized.CharIntTuple.Item1);
            Assert.Equal(90, deserialized.CharIntTuple.Item2);
        }

        private void AssertTupleModelsEqual(TupleModel expected, TupleModel actual)
        {
            if (expected.IntStringTuple != null)
            {
                Assert.NotNull(actual.IntStringTuple);
                Assert.Equal(expected.IntStringTuple.Item1, actual.IntStringTuple.Item1);
                Assert.Equal(expected.IntStringTuple.Item2, actual.IntStringTuple.Item2);
            }

            if (expected.DoubleTuple != null)
            {
                Assert.NotNull(actual.DoubleTuple);
                Assert.Equal(expected.DoubleTuple.Item1, actual.DoubleTuple.Item1, 5);
                Assert.Equal(expected.DoubleTuple.Item2, actual.DoubleTuple.Item2, 5);
            }

            if (expected.StringIntTuple != null)
            {
                Assert.NotNull(actual.StringIntTuple);
                Assert.Equal(expected.StringIntTuple.Item1, actual.StringIntTuple.Item1);
                Assert.Equal(expected.StringIntTuple.Item2, actual.StringIntTuple.Item2);
            }

            if (expected.LongBoolTuple != null)
            {
                Assert.NotNull(actual.LongBoolTuple);
                Assert.Equal(expected.LongBoolTuple.Item1, actual.LongBoolTuple.Item1);
                Assert.Equal(expected.LongBoolTuple.Item2, actual.LongBoolTuple.Item2);
            }

            if (expected.FloatTuple != null)
            {
                Assert.NotNull(actual.FloatTuple);
                Assert.Equal(expected.FloatTuple.Item1, actual.FloatTuple.Item1, 3);
                Assert.Equal(expected.FloatTuple.Item2, actual.FloatTuple.Item2, 3);
            }

            if (expected.TupleList != null)
            {
                Assert.NotNull(actual.TupleList);
                Assert.Equal(expected.TupleList.Count, actual.TupleList.Count);
                for (int i = 0; i < expected.TupleList.Count; i++)
                {
                    Assert.Equal(expected.TupleList[i].Item1, actual.TupleList[i].Item1);
                    Assert.Equal(expected.TupleList[i].Item2, actual.TupleList[i].Item2);
                }
            }

            if (expected.EnumStringTuple != null)
            {
                Assert.NotNull(actual.EnumStringTuple);
                Assert.Equal(expected.EnumStringTuple.Item1, actual.EnumStringTuple.Item1);
                Assert.Equal(expected.EnumStringTuple.Item2, actual.EnumStringTuple.Item2);
            }

            if (expected.CharIntTuple != null)
            {
                Assert.NotNull(actual.CharIntTuple);
                Assert.Equal(expected.CharIntTuple.Item1, actual.CharIntTuple.Item1);
                Assert.Equal(expected.CharIntTuple.Item2, actual.CharIntTuple.Item2);
            }
        }
    }
}