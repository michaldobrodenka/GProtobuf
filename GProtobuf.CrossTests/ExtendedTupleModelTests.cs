using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using GProtobuf.CrossTests.TestModel;

namespace GProtobuf.CrossTests
{
    public class ExtendedTupleModelTests
    {
        #region 3-Element Tuple Tests

        [Fact]
        public void Test_GG_ThreeElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(1, 2, 3)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(1, deserialized.ThreeElementTuple.Item1);
            Assert.Equal(2, deserialized.ThreeElementTuple.Item2);
            Assert.Equal(3, deserialized.ThreeElementTuple.Item3);
        }

        [Fact]
        public void Test_PG_ThreeElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(10, 20, 30)
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(10, deserialized.ThreeElementTuple.Item1);
            Assert.Equal(20, deserialized.ThreeElementTuple.Item2);
            Assert.Equal(30, deserialized.ThreeElementTuple.Item3);
        }

        [Fact]
        public void Test_GP_ThreeElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(100, 200, 300)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var deserialized = ProtoBuf.Serializer.Deserialize<ExtendedTupleModel>(ms);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(100, deserialized.ThreeElementTuple.Item1);
            Assert.Equal(200, deserialized.ThreeElementTuple.Item2);
            Assert.Equal(300, deserialized.ThreeElementTuple.Item3);
        }

        #endregion

        #region 4-Element Tuple Tests

        [Fact]
        public void Test_GG_FourElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                FourElementTuple = Tuple.Create(42, "test", 3.14, true)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.FourElementTuple);
            Assert.Equal(42, deserialized.FourElementTuple.Item1);
            Assert.Equal("test", deserialized.FourElementTuple.Item2);
            Assert.Equal(3.14, deserialized.FourElementTuple.Item3);
            Assert.True(deserialized.FourElementTuple.Item4);
        }

        [Fact]
        public void Test_PG_FourElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                FourElementTuple = Tuple.Create(99, "proto", 2.71, false)
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.FourElementTuple);
            Assert.Equal(99, deserialized.FourElementTuple.Item1);
            Assert.Equal("proto", deserialized.FourElementTuple.Item2);
            Assert.Equal(2.71, deserialized.FourElementTuple.Item3);
            Assert.False(deserialized.FourElementTuple.Item4);
        }

        [Fact]
        public void Test_GP_FourElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                FourElementTuple = Tuple.Create(7, "data", 1.41, true)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var deserialized = ProtoBuf.Serializer.Deserialize<ExtendedTupleModel>(ms);

            Assert.NotNull(deserialized.FourElementTuple);
            Assert.Equal(7, deserialized.FourElementTuple.Item1);
            Assert.Equal("data", deserialized.FourElementTuple.Item2);
            Assert.Equal(1.41, deserialized.FourElementTuple.Item3);
            Assert.True(deserialized.FourElementTuple.Item4);
        }

        #endregion

        #region 5-7 Element Tuple Tests

        [Fact]
        public void Test_GG_FiveElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                FiveElementTuple = Tuple.Create(1, 2, 3, 4, 5)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.FiveElementTuple);
            Assert.Equal(1, deserialized.FiveElementTuple.Item1);
            Assert.Equal(2, deserialized.FiveElementTuple.Item2);
            Assert.Equal(3, deserialized.FiveElementTuple.Item3);
            Assert.Equal(4, deserialized.FiveElementTuple.Item4);
            Assert.Equal(5, deserialized.FiveElementTuple.Item5);
        }

        [Fact]
        public void Test_GG_SixElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                SixElementTuple = Tuple.Create("a", 1, 1.1, true, 2.2f, 100L)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.SixElementTuple);
            Assert.Equal("a", deserialized.SixElementTuple.Item1);
            Assert.Equal(1, deserialized.SixElementTuple.Item2);
            Assert.Equal(1.1, deserialized.SixElementTuple.Item3);
            Assert.True(deserialized.SixElementTuple.Item4);
            Assert.Equal(2.2f, deserialized.SixElementTuple.Item5);
            Assert.Equal(100L, deserialized.SixElementTuple.Item6);
        }

        [Fact]
        public void Test_GG_SevenElementTuple()
        {
            var original = new ExtendedTupleModel
            {
                SevenElementTuple = Tuple.Create(10, 20, 30, 40, 50, 60, 70)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.SevenElementTuple);
            Assert.Equal(10, deserialized.SevenElementTuple.Item1);
            Assert.Equal(20, deserialized.SevenElementTuple.Item2);
            Assert.Equal(30, deserialized.SevenElementTuple.Item3);
            Assert.Equal(40, deserialized.SevenElementTuple.Item4);
            Assert.Equal(50, deserialized.SevenElementTuple.Item5);
            Assert.Equal(60, deserialized.SevenElementTuple.Item6);
            Assert.Equal(70, deserialized.SevenElementTuple.Item7);
        }

        #endregion

//         #region Collections as Tuple Elements Tests
// 
//         [Fact]
//         public void Test_GG_ListTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 ListTuple = Tuple.Create(
//                     new List<int> { 1, 2, 3 },
//                     new List<string> { "a", "b", "c" }
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.ListTuple);
//             Assert.Equal(new List<int> { 1, 2, 3 }, deserialized.ListTuple.Item1);
//             Assert.Equal(new List<string> { "a", "b", "c" }, deserialized.ListTuple.Item2);
//         }
// 
//         [Fact]
//         public void Test_GG_ArrayTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 ArrayTuple = Tuple.Create(
//                     new int[] { 10, 20, 30 },
//                     new string[] { "x", "y", "z" }
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.ArrayTuple);
//             Assert.Equal(new int[] { 10, 20, 30 }, deserialized.ArrayTuple.Item1);
//             Assert.Equal(new string[] { "x", "y", "z" }, deserialized.ArrayTuple.Item2);
//         }
// 
//         [Fact]
//         public void Test_GG_DictionaryTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 DictionaryTuple = Tuple.Create(
//                     new Dictionary<int, string> { { 1, "one" }, { 2, "two" } },
//                     true
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.DictionaryTuple);
//             Assert.Equal(2, deserialized.DictionaryTuple.Item1.Count);
//             Assert.Equal("one", deserialized.DictionaryTuple.Item1[1]);
//             Assert.Equal("two", deserialized.DictionaryTuple.Item1[2]);
//             Assert.True(deserialized.DictionaryTuple.Item2);
//         }
// 
//         [Fact]
//         public void Test_GG_HashSetTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 HashSetTuple = Tuple.Create(
//                     new HashSet<int> { 1, 2, 3 },
//                     new HashSet<string> { "a", "b", "c" }
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.HashSetTuple);
//             Assert.Equal(new HashSet<int> { 1, 2, 3 }, deserialized.HashSetTuple.Item1);
//             Assert.Equal(new HashSet<string> { "a", "b", "c" }, deserialized.HashSetTuple.Item2);
//         }
// 
//         #endregion

        #region Tuples of Tuples Tests

//         [Fact]
//         public void Test_GG_TupleOfTuples()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 TupleOfTuples = Tuple.Create(
//                     Tuple.Create(1, 2),
//                     Tuple.Create("a", "b")
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.TupleOfTuples);
//             Assert.Equal(1, deserialized.TupleOfTuples.Item1.Item1);
//             Assert.Equal(2, deserialized.TupleOfTuples.Item1.Item2);
//             Assert.Equal("a", deserialized.TupleOfTuples.Item2.Item1);
//             Assert.Equal("b", deserialized.TupleOfTuples.Item2.Item2);
//         }

//         [Fact]
//         public void Test_PG_TupleOfTuples()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 TupleOfTuples = Tuple.Create(
//                     Tuple.Create(10, 20),
//                     Tuple.Create("x", "y")
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             ProtoBuf.Serializer.Serialize(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.TupleOfTuples);
//             Assert.Equal(10, deserialized.TupleOfTuples.Item1.Item1);
//             Assert.Equal(20, deserialized.TupleOfTuples.Item1.Item2);
//             Assert.Equal("x", deserialized.TupleOfTuples.Item2.Item1);
//             Assert.Equal("y", deserialized.TupleOfTuples.Item2.Item2);
//         }

//         [Fact]
//         public void Test_GP_TupleOfTuples()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 TupleOfTuples = Tuple.Create(
//                     Tuple.Create(100, 200),
//                     Tuple.Create("foo", "bar")
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var deserialized = ProtoBuf.Serializer.Deserialize<ExtendedTupleModel>(ms);
// 
//             Assert.NotNull(deserialized.TupleOfTuples);
//             Assert.Equal(100, deserialized.TupleOfTuples.Item1.Item1);
//             Assert.Equal(200, deserialized.TupleOfTuples.Item1.Item2);
//             Assert.Equal("foo", deserialized.TupleOfTuples.Item2.Item1);
//             Assert.Equal("bar", deserialized.TupleOfTuples.Item2.Item2);
//         }

//         [Fact]
//         public void Test_GG_ComplexTupleOfTuples()
//         {
//             var guid1 = Guid.Parse("12345678-1234-1234-1234-123456789012");
//             var ts1 = TimeSpan.FromMinutes(42);
// 
//             var original = new ExtendedTupleModel
//             {
//                 ComplexTupleOfTuples = Tuple.Create(
//                     Tuple.Create(guid1, ts1),
//                     Tuple.Create(99, true)
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.ComplexTupleOfTuples);
//             Assert.Equal(guid1, deserialized.ComplexTupleOfTuples.Item1.Item1);
//             Assert.Equal(ts1, deserialized.ComplexTupleOfTuples.Item1.Item2);
//             Assert.Equal(99, deserialized.ComplexTupleOfTuples.Item2.Item1);
//             Assert.True(deserialized.ComplexTupleOfTuples.Item2.Item2);
//         }

        #endregion

        #region Nested Tuples (8+ Elements) Tests

        [Fact]
        public void Test_GG_EightElementNestedTuple()
        {
            var original = new ExtendedTupleModel
            {
                EightElementNestedTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int>>(
                    1, 2, 3, 4, 5, 6, 7, Tuple.Create(8)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.EightElementNestedTuple);
            Assert.Equal(1, deserialized.EightElementNestedTuple.Item1);
            Assert.Equal(2, deserialized.EightElementNestedTuple.Item2);
            Assert.Equal(3, deserialized.EightElementNestedTuple.Item3);
            Assert.Equal(4, deserialized.EightElementNestedTuple.Item4);
            Assert.Equal(5, deserialized.EightElementNestedTuple.Item5);
            Assert.Equal(6, deserialized.EightElementNestedTuple.Item6);
            Assert.Equal(7, deserialized.EightElementNestedTuple.Item7);
            Assert.Equal(8, deserialized.EightElementNestedTuple.Rest.Item1);
        }

        [Fact]
        public void Test_GG_NineElementNestedTuple()
        {
            var guid = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
            var ts = TimeSpan.FromHours(5);

            var original = new ExtendedTupleModel
            {
                NineElementNestedTuple = new Tuple<int, string, double, bool, float, long, Guid, Tuple<TimeSpan, int>>(
                    1, "test", 3.14, true, 2.71f, 999L, guid, Tuple.Create(ts, 42)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.NineElementNestedTuple);
            Assert.Equal(1, deserialized.NineElementNestedTuple.Item1);
            Assert.Equal("test", deserialized.NineElementNestedTuple.Item2);
            Assert.Equal(3.14, deserialized.NineElementNestedTuple.Item3);
            Assert.True(deserialized.NineElementNestedTuple.Item4);
            Assert.Equal(2.71f, deserialized.NineElementNestedTuple.Item5);
            Assert.Equal(999L, deserialized.NineElementNestedTuple.Item6);
            Assert.Equal(guid, deserialized.NineElementNestedTuple.Item7);
            Assert.Equal(ts, deserialized.NineElementNestedTuple.Rest.Item1);
            Assert.Equal(42, deserialized.NineElementNestedTuple.Rest.Item2);
        }

        [Fact]
        public void Test_GG_TenElementNestedTuple()
        {
            var original = new ExtendedTupleModel
            {
                TenElementNestedTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int, int>>(
                    1, 2, 3, 4, 5, 6, 7, Tuple.Create(8, 9, 10)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.TenElementNestedTuple);
            Assert.Equal(1, deserialized.TenElementNestedTuple.Item1);
            Assert.Equal(7, deserialized.TenElementNestedTuple.Item7);
            Assert.Equal(8, deserialized.TenElementNestedTuple.Rest.Item1);
            Assert.Equal(9, deserialized.TenElementNestedTuple.Rest.Item2);
            Assert.Equal(10, deserialized.TenElementNestedTuple.Rest.Item3);
        }

        #endregion

        #region Collections of Tuples Tests

        [Fact]
        public void Test_GG_TupleList()
        {
            var original = new ExtendedTupleModel
            {
                TupleList = new List<Tuple<int, string>>
                {
                    Tuple.Create(1, "one"),
                    Tuple.Create(2, "two"),
                    Tuple.Create(3, "three")
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.TupleList);
            Assert.Equal(3, deserialized.TupleList.Count);
            Assert.Equal(1, deserialized.TupleList[0].Item1);
            Assert.Equal("one", deserialized.TupleList[0].Item2);
            Assert.Equal(2, deserialized.TupleList[1].Item1);
            Assert.Equal("two", deserialized.TupleList[1].Item2);
        }

        [Fact]
        public void Test_GG_ThreeElementTupleList()
        {
            var original = new ExtendedTupleModel
            {
                ThreeElementTupleList = new List<Tuple<int, int, int>>
                {
                    Tuple.Create(1, 2, 3),
                    Tuple.Create(10, 20, 30),
                    Tuple.Create(100, 200, 300)
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeElementTupleList);
            Assert.Equal(3, deserialized.ThreeElementTupleList.Count);
            Assert.Equal(Tuple.Create(1, 2, 3), deserialized.ThreeElementTupleList[0]);
            Assert.Equal(Tuple.Create(10, 20, 30), deserialized.ThreeElementTupleList[1]);
            Assert.Equal(Tuple.Create(100, 200, 300), deserialized.ThreeElementTupleList[2]);
        }

        #endregion

        #region Additional 3-element Variations Tests

        [Fact]
        public void Test_GG_ThreeGuidTuple()
        {
            var guid1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var guid2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var guid3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

            var original = new ExtendedTupleModel
            {
                ThreeGuidTuple = Tuple.Create(guid1, guid2, guid3)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeGuidTuple);
            Assert.Equal(guid1, deserialized.ThreeGuidTuple.Item1);
            Assert.Equal(guid2, deserialized.ThreeGuidTuple.Item2);
            Assert.Equal(guid3, deserialized.ThreeGuidTuple.Item3);
        }

        [Fact]
        public void Test_GG_ThreeStringTuple()
        {
            var original = new ExtendedTupleModel
            {
                ThreeStringTuple = Tuple.Create("first", "second", "third")
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeStringTuple);
            Assert.Equal("first", deserialized.ThreeStringTuple.Item1);
            Assert.Equal("second", deserialized.ThreeStringTuple.Item2);
            Assert.Equal("third", deserialized.ThreeStringTuple.Item3);
        }

        [Fact]
        public void Test_GG_ThreeTimeSpanTuple()
        {
            var ts1 = TimeSpan.FromHours(1);
            var ts2 = TimeSpan.FromMinutes(30);
            var ts3 = TimeSpan.FromSeconds(45);

            var original = new ExtendedTupleModel
            {
                ThreeTimeSpanTuple = Tuple.Create(ts1, ts2, ts3)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeTimeSpanTuple);
            Assert.Equal(ts1, deserialized.ThreeTimeSpanTuple.Item1);
            Assert.Equal(ts2, deserialized.ThreeTimeSpanTuple.Item2);
            Assert.Equal(ts3, deserialized.ThreeTimeSpanTuple.Item3);
        }

        #endregion

//         #region Mixed Complex Scenarios Tests
// 
//         [Fact]
//         public void Test_GG_CollectionMixTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 CollectionMixTuple = Tuple.Create(
//                     new List<int> { 1, 2, 3 },
//                     new Dictionary<string, int> { { "a", 1 }, { "b", 2 } }
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.CollectionMixTuple);
//             Assert.Equal(new List<int> { 1, 2, 3 }, deserialized.CollectionMixTuple.Item1);
//             Assert.Equal(2, deserialized.CollectionMixTuple.Item2.Count);
//             Assert.Equal(1, deserialized.CollectionMixTuple.Item2["a"]);
//             Assert.Equal(2, deserialized.CollectionMixTuple.Item2["b"]);
//         }
// 
//         [Fact]
//         public void Test_GG_NestedWithCollectionTuple()
//         {
//             var original = new ExtendedTupleModel
//             {
//                 NestedWithCollectionTuple = Tuple.Create(
//                     Tuple.Create(42, "answer"),
//                     new List<int> { 10, 20, 30 }
//                 )
//             };
// 
//             using var ms = new MemoryStream();
//             global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
//             ms.Position = 0;
// 
//             var bytes = ms.ToArray();
//             var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);
// 
//             Assert.NotNull(deserialized.NestedWithCollectionTuple);
//             Assert.Equal(42, deserialized.NestedWithCollectionTuple.Item1.Item1);
//             Assert.Equal("answer", deserialized.NestedWithCollectionTuple.Item1.Item2);
//             Assert.Equal(new List<int> { 10, 20, 30 }, deserialized.NestedWithCollectionTuple.Item2);
//         }
// 
//         #endregion

        #region Complete Model Test

        [Fact]
        public void Test_GG_CompleteModel()
        {
            var guid = Guid.Parse("12345678-ABCD-ABCD-ABCD-123456789ABC");
            var ts = TimeSpan.FromMinutes(30);

            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(1, 2, 3),
                FourElementTuple = Tuple.Create(10, "test", 5.5, true),
                FiveElementTuple = Tuple.Create(1, 2, 3, 4, 5),
                SixElementTuple = Tuple.Create("x", 9, 8.8, false, 7.7f, 999L),
                SevenElementTuple = Tuple.Create(10, 20, 30, 40, 50, 60, 70),
                EightElementNestedTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int>>(1, 2, 3, 4, 5, 6, 7, Tuple.Create(8)),
                TupleList = new List<Tuple<int, string>> { Tuple.Create(1, "one") },
                ThreeElementTupleList = new List<Tuple<int, int, int>> { Tuple.Create(1, 2, 3) },
                ThreeGuidTuple = Tuple.Create(guid, guid, guid),
                ThreeStringTuple = Tuple.Create("a", "b", "c"),
                ThreeTimeSpanTuple = Tuple.Create(ts, ts, ts)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(1, deserialized.ThreeElementTuple.Item1);
            Assert.NotNull(deserialized.FourElementTuple);
            Assert.Equal("test", deserialized.FourElementTuple.Item2);
            Assert.NotNull(deserialized.SixElementTuple);
            Assert.Equal("x", deserialized.SixElementTuple.Item1);
            Assert.NotNull(deserialized.EightElementNestedTuple);
            Assert.Equal(8, deserialized.EightElementNestedTuple.Rest.Item1);
        }

        [Fact]
        public void Test_PG_CompleteModel()
        {
            var guid = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
            var ts = TimeSpan.FromHours(1);

            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(100, 200, 300),
                FourElementTuple = Tuple.Create(99, "proto", 9.9, false),
                FiveElementTuple = Tuple.Create(10, 20, 30, 40, 50),
                TupleList = new List<Tuple<int, string>> { Tuple.Create(10, "ten"), Tuple.Create(20, "twenty") }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeExtendedTupleModel(bytes);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(100, deserialized.ThreeElementTuple.Item1);
            Assert.Equal(200, deserialized.ThreeElementTuple.Item2);
            Assert.NotNull(deserialized.FourElementTuple);
            Assert.Equal("proto", deserialized.FourElementTuple.Item2);
            Assert.NotNull(deserialized.TupleList);
            Assert.Equal(2, deserialized.TupleList.Count);
        }

        [Fact]
        public void Test_GP_CompleteModel()
        {
            var original = new ExtendedTupleModel
            {
                ThreeElementTuple = Tuple.Create(7, 8, 9),
                FourElementTuple = Tuple.Create(1, "data", 2.3, true),
                SixElementTuple = Tuple.Create("a", 1, 1.1, true, 2.2f, 100L),
                TupleList = new List<Tuple<int, string>> { Tuple.Create(5, "five") }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeExtendedTupleModel(ms, original);
            ms.Position = 0;

            var deserialized = ProtoBuf.Serializer.Deserialize<ExtendedTupleModel>(ms);

            Assert.NotNull(deserialized.ThreeElementTuple);
            Assert.Equal(7, deserialized.ThreeElementTuple.Item1);
            Assert.Equal(8, deserialized.ThreeElementTuple.Item2);
            Assert.Equal(9, deserialized.ThreeElementTuple.Item3);
            Assert.NotNull(deserialized.SixElementTuple);
            Assert.Equal("a", deserialized.SixElementTuple.Item1);
        }

        #endregion
    }
}
