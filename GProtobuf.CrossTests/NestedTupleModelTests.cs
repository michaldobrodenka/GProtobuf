using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GProtobuf.CrossTests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class NestedTupleModelTests
    {
        private NestedTupleModel CreateTestModel()
        {
            return new NestedTupleModel
            {
                SimpleNested = new Tuple<int, Tuple<string, bool>>(
                    42,
                    new Tuple<string, bool>("nested", true)
                ),
                DeeplyNested = new Tuple<int, Tuple<int, Tuple<int, int>>>(
                    1,
                    new Tuple<int, Tuple<int, int>>(
                        2,
                        new Tuple<int, int>(3, 4)
                    )
                ),
                NestedTupleList = new List<Tuple<int, Tuple<string, bool>>>
                {
                    new Tuple<int, Tuple<string, bool>>(10, new Tuple<string, bool>("first", true)),
                    new Tuple<int, Tuple<string, bool>>(20, new Tuple<string, bool>("second", false))
                },
                DictWithNestedTuple = new Dictionary<int, Tuple<string, Tuple<int, bool>>>
                {
                    { 1, new Tuple<string, Tuple<int, bool>>("one", new Tuple<int, bool>(100, true)) },
                    { 2, new Tuple<string, Tuple<int, bool>>("two", new Tuple<int, bool>(200, false)) }
                },
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int>(8, 9)
                )
            };
        }

        #region SimpleNested Tests

        [Fact]
        public void Test_PG_SimpleNested()
        {
            var original = new NestedTupleModel
            {
                SimpleNested = new Tuple<int, Tuple<string, bool>>(
                    123,
                    new Tuple<string, bool>("test", true)
                )
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.SimpleNested);
            Assert.Equal(123, deserialized.SimpleNested.Item1);
            Assert.NotNull(deserialized.SimpleNested.Item2);
            Assert.Equal("test", deserialized.SimpleNested.Item2.Item1);
            Assert.True(deserialized.SimpleNested.Item2.Item2);
        }

        [Fact]
        public void Test_GG_SimpleNested()
        {
            var original = new NestedTupleModel
            {
                SimpleNested = new Tuple<int, Tuple<string, bool>>(
                    456,
                    new Tuple<string, bool>("another", false)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.SimpleNested);
            Assert.Equal(456, deserialized.SimpleNested.Item1);
            Assert.NotNull(deserialized.SimpleNested.Item2);
            Assert.Equal("another", deserialized.SimpleNested.Item2.Item1);
            Assert.False(deserialized.SimpleNested.Item2.Item2);
        }

        [Fact]
        public void Test_GP_SimpleNested()
        {
            var original = new NestedTupleModel
            {
                SimpleNested = new Tuple<int, Tuple<string, bool>>(
                    789,
                    new Tuple<string, bool>("gptest", true)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<NestedTupleModel>(ms);

            Assert.NotNull(deserialized.SimpleNested);
            Assert.Equal(789, deserialized.SimpleNested.Item1);
            Assert.NotNull(deserialized.SimpleNested.Item2);
            Assert.Equal("gptest", deserialized.SimpleNested.Item2.Item1);
            Assert.True(deserialized.SimpleNested.Item2.Item2);
        }

        #endregion

        #region DeeplyNested Tests

        [Fact]
        public void Test_PG_DeeplyNested()
        {
            var original = new NestedTupleModel
            {
                DeeplyNested = new Tuple<int, Tuple<int, Tuple<int, int>>>(
                    10,
                    new Tuple<int, Tuple<int, int>>(
                        20,
                        new Tuple<int, int>(30, 40)
                    )
                )
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.DeeplyNested);
            Assert.Equal(10, deserialized.DeeplyNested.Item1);
            Assert.NotNull(deserialized.DeeplyNested.Item2);
            Assert.Equal(20, deserialized.DeeplyNested.Item2.Item1);
            Assert.NotNull(deserialized.DeeplyNested.Item2.Item2);
            Assert.Equal(30, deserialized.DeeplyNested.Item2.Item2.Item1);
            Assert.Equal(40, deserialized.DeeplyNested.Item2.Item2.Item2);
        }

        [Fact]
        public void Test_GG_DeeplyNested()
        {
            var original = new NestedTupleModel
            {
                DeeplyNested = new Tuple<int, Tuple<int, Tuple<int, int>>>(
                    5,
                    new Tuple<int, Tuple<int, int>>(
                        15,
                        new Tuple<int, int>(25, 35)
                    )
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.DeeplyNested);
            Assert.Equal(5, deserialized.DeeplyNested.Item1);
            Assert.NotNull(deserialized.DeeplyNested.Item2);
            Assert.Equal(15, deserialized.DeeplyNested.Item2.Item1);
            Assert.NotNull(deserialized.DeeplyNested.Item2.Item2);
            Assert.Equal(25, deserialized.DeeplyNested.Item2.Item2.Item1);
            Assert.Equal(35, deserialized.DeeplyNested.Item2.Item2.Item2);
        }

        #endregion

        #region NestedTupleList Tests

        [Fact]
        public void Test_PG_NestedTupleList()
        {
            var original = new NestedTupleModel
            {
                NestedTupleList = new List<Tuple<int, Tuple<string, bool>>>
                {
                    new Tuple<int, Tuple<string, bool>>(1, new Tuple<string, bool>("one", true)),
                    new Tuple<int, Tuple<string, bool>>(2, new Tuple<string, bool>("two", false)),
                    new Tuple<int, Tuple<string, bool>>(3, new Tuple<string, bool>("three", true))
                }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.NestedTupleList);
            Assert.Equal(3, deserialized.NestedTupleList.Count);

            Assert.Equal(1, deserialized.NestedTupleList[0].Item1);
            Assert.Equal("one", deserialized.NestedTupleList[0].Item2.Item1);
            Assert.True(deserialized.NestedTupleList[0].Item2.Item2);

            Assert.Equal(2, deserialized.NestedTupleList[1].Item1);
            Assert.Equal("two", deserialized.NestedTupleList[1].Item2.Item1);
            Assert.False(deserialized.NestedTupleList[1].Item2.Item2);

            Assert.Equal(3, deserialized.NestedTupleList[2].Item1);
            Assert.Equal("three", deserialized.NestedTupleList[2].Item2.Item1);
            Assert.True(deserialized.NestedTupleList[2].Item2.Item2);
        }

        [Fact]
        public void Test_GG_NestedTupleList()
        {
            var original = new NestedTupleModel
            {
                NestedTupleList = new List<Tuple<int, Tuple<string, bool>>>
                {
                    new Tuple<int, Tuple<string, bool>>(100, new Tuple<string, bool>("alpha", false)),
                    new Tuple<int, Tuple<string, bool>>(200, new Tuple<string, bool>("beta", true))
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.NestedTupleList);
            Assert.Equal(2, deserialized.NestedTupleList.Count);

            Assert.Equal(100, deserialized.NestedTupleList[0].Item1);
            Assert.Equal("alpha", deserialized.NestedTupleList[0].Item2.Item1);
            Assert.False(deserialized.NestedTupleList[0].Item2.Item2);

            Assert.Equal(200, deserialized.NestedTupleList[1].Item1);
            Assert.Equal("beta", deserialized.NestedTupleList[1].Item2.Item1);
            Assert.True(deserialized.NestedTupleList[1].Item2.Item2);
        }

        #endregion

        #region DictWithNestedTuple Tests

        [Fact]
        public void Test_PG_DictWithNestedTuple()
        {
            var original = new NestedTupleModel
            {
                DictWithNestedTuple = new Dictionary<int, Tuple<string, Tuple<int, bool>>>
                {
                    { 10, new Tuple<string, Tuple<int, bool>>("ten", new Tuple<int, bool>(1000, true)) },
                    { 20, new Tuple<string, Tuple<int, bool>>("twenty", new Tuple<int, bool>(2000, false)) }
                }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.DictWithNestedTuple);
            Assert.Equal(2, deserialized.DictWithNestedTuple.Count);

            Assert.True(deserialized.DictWithNestedTuple.ContainsKey(10));
            Assert.Equal("ten", deserialized.DictWithNestedTuple[10].Item1);
            Assert.Equal(1000, deserialized.DictWithNestedTuple[10].Item2.Item1);
            Assert.True(deserialized.DictWithNestedTuple[10].Item2.Item2);

            Assert.True(deserialized.DictWithNestedTuple.ContainsKey(20));
            Assert.Equal("twenty", deserialized.DictWithNestedTuple[20].Item1);
            Assert.Equal(2000, deserialized.DictWithNestedTuple[20].Item2.Item1);
            Assert.False(deserialized.DictWithNestedTuple[20].Item2.Item2);
        }

        [Fact]
        public void Test_GG_DictWithNestedTuple()
        {
            var original = new NestedTupleModel
            {
                DictWithNestedTuple = new Dictionary<int, Tuple<string, Tuple<int, bool>>>
                {
                    { 5, new Tuple<string, Tuple<int, bool>>("five", new Tuple<int, bool>(500, false)) }
                }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.DictWithNestedTuple);
            Assert.Single(deserialized.DictWithNestedTuple);

            Assert.True(deserialized.DictWithNestedTuple.ContainsKey(5));
            Assert.Equal("five", deserialized.DictWithNestedTuple[5].Item1);
            Assert.Equal(500, deserialized.DictWithNestedTuple[5].Item2.Item1);
            Assert.False(deserialized.DictWithNestedTuple[5].Item2.Item2);
        }

        #endregion

        #region EightElementTuple Tests

        [Fact]
        public void Test_PG_EightElementTuple()
        {
            var original = new NestedTupleModel
            {
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    11, 22, 33, 44, 55, 66, 77,
                    new Tuple<int, int>(88, 99)
                )
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.EightElementTuple);
            Assert.Equal(11, deserialized.EightElementTuple.Item1);
            Assert.Equal(22, deserialized.EightElementTuple.Item2);
            Assert.Equal(33, deserialized.EightElementTuple.Item3);
            Assert.Equal(44, deserialized.EightElementTuple.Item4);
            Assert.Equal(55, deserialized.EightElementTuple.Item5);
            Assert.Equal(66, deserialized.EightElementTuple.Item6);
            Assert.Equal(77, deserialized.EightElementTuple.Item7);
            Assert.NotNull(deserialized.EightElementTuple.Rest);
            Assert.Equal(88, deserialized.EightElementTuple.Rest.Item1);
            Assert.Equal(99, deserialized.EightElementTuple.Rest.Item2);
        }

        [Fact]
        public void Test_GG_EightElementTuple()
        {
            var original = new NestedTupleModel
            {
                EightElementTuple = new Tuple<int, int, int, int, int, int, int, Tuple<int, int>>(
                    1, 2, 3, 4, 5, 6, 7,
                    new Tuple<int, int>(8, 9)
                )
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            Assert.NotNull(deserialized.EightElementTuple);
            Assert.Equal(1, deserialized.EightElementTuple.Item1);
            Assert.Equal(2, deserialized.EightElementTuple.Item2);
            Assert.Equal(3, deserialized.EightElementTuple.Item3);
            Assert.Equal(4, deserialized.EightElementTuple.Item4);
            Assert.Equal(5, deserialized.EightElementTuple.Item5);
            Assert.Equal(6, deserialized.EightElementTuple.Item6);
            Assert.Equal(7, deserialized.EightElementTuple.Item7);
            Assert.NotNull(deserialized.EightElementTuple.Rest);
            Assert.Equal(8, deserialized.EightElementTuple.Rest.Item1);
            Assert.Equal(9, deserialized.EightElementTuple.Rest.Item2);
        }

        #endregion

        #region Complete Model Tests

        [Fact]
        public void Test_PG_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            AssertModelsEqual(original, deserialized);
        }

        [Fact]
        public void Test_GG_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            ms.Position = 0;

            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeNestedTupleModel(bytes);

            AssertModelsEqual(original, deserialized);
        }

        [Fact]
        public void Test_GP_CompleteModel()
        {
            var original = CreateTestModel();

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeNestedTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<NestedTupleModel>(ms);

            AssertModelsEqual(original, deserialized);
        }

        #endregion

        private void AssertModelsEqual(NestedTupleModel expected, NestedTupleModel actual)
        {
            if (expected.SimpleNested != null)
            {
                Assert.NotNull(actual.SimpleNested);
                Assert.Equal(expected.SimpleNested.Item1, actual.SimpleNested.Item1);
                Assert.NotNull(actual.SimpleNested.Item2);
                Assert.Equal(expected.SimpleNested.Item2.Item1, actual.SimpleNested.Item2.Item1);
                Assert.Equal(expected.SimpleNested.Item2.Item2, actual.SimpleNested.Item2.Item2);
            }

            if (expected.DeeplyNested != null)
            {
                Assert.NotNull(actual.DeeplyNested);
                Assert.Equal(expected.DeeplyNested.Item1, actual.DeeplyNested.Item1);
                Assert.NotNull(actual.DeeplyNested.Item2);
                Assert.Equal(expected.DeeplyNested.Item2.Item1, actual.DeeplyNested.Item2.Item1);
                Assert.NotNull(actual.DeeplyNested.Item2.Item2);
                Assert.Equal(expected.DeeplyNested.Item2.Item2.Item1, actual.DeeplyNested.Item2.Item2.Item1);
                Assert.Equal(expected.DeeplyNested.Item2.Item2.Item2, actual.DeeplyNested.Item2.Item2.Item2);
            }

            if (expected.NestedTupleList != null)
            {
                Assert.NotNull(actual.NestedTupleList);
                Assert.Equal(expected.NestedTupleList.Count, actual.NestedTupleList.Count);
                for (int i = 0; i < expected.NestedTupleList.Count; i++)
                {
                    Assert.Equal(expected.NestedTupleList[i].Item1, actual.NestedTupleList[i].Item1);
                    Assert.Equal(expected.NestedTupleList[i].Item2.Item1, actual.NestedTupleList[i].Item2.Item1);
                    Assert.Equal(expected.NestedTupleList[i].Item2.Item2, actual.NestedTupleList[i].Item2.Item2);
                }
            }

            if (expected.DictWithNestedTuple != null)
            {
                Assert.NotNull(actual.DictWithNestedTuple);
                Assert.Equal(expected.DictWithNestedTuple.Count, actual.DictWithNestedTuple.Count);
                foreach (var kvp in expected.DictWithNestedTuple)
                {
                    Assert.True(actual.DictWithNestedTuple.ContainsKey(kvp.Key));
                    Assert.Equal(kvp.Value.Item1, actual.DictWithNestedTuple[kvp.Key].Item1);
                    Assert.Equal(kvp.Value.Item2.Item1, actual.DictWithNestedTuple[kvp.Key].Item2.Item1);
                    Assert.Equal(kvp.Value.Item2.Item2, actual.DictWithNestedTuple[kvp.Key].Item2.Item2);
                }
            }

            if (expected.EightElementTuple != null)
            {
                Assert.NotNull(actual.EightElementTuple);
                Assert.Equal(expected.EightElementTuple.Item1, actual.EightElementTuple.Item1);
                Assert.Equal(expected.EightElementTuple.Item2, actual.EightElementTuple.Item2);
                Assert.Equal(expected.EightElementTuple.Item3, actual.EightElementTuple.Item3);
                Assert.Equal(expected.EightElementTuple.Item4, actual.EightElementTuple.Item4);
                Assert.Equal(expected.EightElementTuple.Item5, actual.EightElementTuple.Item5);
                Assert.Equal(expected.EightElementTuple.Item6, actual.EightElementTuple.Item6);
                Assert.Equal(expected.EightElementTuple.Item7, actual.EightElementTuple.Item7);
                Assert.NotNull(actual.EightElementTuple.Rest);
                Assert.Equal(expected.EightElementTuple.Rest.Item1, actual.EightElementTuple.Rest.Item1);
                Assert.Equal(expected.EightElementTuple.Rest.Item2, actual.EightElementTuple.Rest.Item2);
            }
        }
    }
}
