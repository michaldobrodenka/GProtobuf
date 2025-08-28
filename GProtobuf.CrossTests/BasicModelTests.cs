using System;
using System.Collections.Generic;
using System.IO;
using GProtobuf.Tests.TestModel;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class DictionaryTupleModelTests
    {
        private DictionaryTupleModel CreateTestModelWithTupleMap()
        {
            return new DictionaryTupleModel
            {
                DoubleTuple = new Tuple<double, double>(3.14, 2.71),
                ThermostatOutputMap = new Dictionary<int, Tuple<double, double>>
                {
                    { 1, new Tuple<double, double>(21.5, 68.0) },
                    { 2, new Tuple<double, double>(22.0, 70.5) },
                    { 3, new Tuple<double, double>(20.5, 65.2) }
                },
                NameValueMap = new Dictionary<string, Tuple<int, string>>
                {
                    { "first", new Tuple<int, string>(1, "One") },
                    { "second", new Tuple<int, string>(2, "Two") }
                },
                TupleKeyMap = new Dictionary<Tuple<int, string>, double>
                {
                    { new Tuple<int, string>(10, "ten"), 10.0 },
                    { new Tuple<int, string>(20, "twenty"), 20.0 }
                }
            };
        }

        [Fact]
        public void Test_PG_ThermostatOutputMap()
        {
            var original = CreateTestModelWithTupleMap();

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDictionaryTupleModel(bytes);

            Assert.NotNull(deserialized.ThermostatOutputMap);
            Assert.Equal(3, deserialized.ThermostatOutputMap.Count);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(1));
            Assert.Equal(21.5, deserialized.ThermostatOutputMap[1].Item1, 2);
            Assert.Equal(68.0, deserialized.ThermostatOutputMap[1].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(2));
            Assert.Equal(22.0, deserialized.ThermostatOutputMap[2].Item1, 2);
            Assert.Equal(70.5, deserialized.ThermostatOutputMap[2].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(3));
            Assert.Equal(20.5, deserialized.ThermostatOutputMap[3].Item1, 2);
            Assert.Equal(65.2, deserialized.ThermostatOutputMap[3].Item2, 2);
        }

        [Fact]
        public void Test_GG_ThermostatOutputMap()
        {
            var original = CreateTestModelWithTupleMap();

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDictionaryTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDictionaryTupleModel(bytes);

            Assert.NotNull(deserialized.ThermostatOutputMap);
            Assert.Equal(3, deserialized.ThermostatOutputMap.Count);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(1));
            Assert.Equal(21.5, deserialized.ThermostatOutputMap[1].Item1, 2);
            Assert.Equal(68.0, deserialized.ThermostatOutputMap[1].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(2));
            Assert.Equal(22.0, deserialized.ThermostatOutputMap[2].Item1, 2);
            Assert.Equal(70.5, deserialized.ThermostatOutputMap[2].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(3));
            Assert.Equal(20.5, deserialized.ThermostatOutputMap[3].Item1, 2);
            Assert.Equal(65.2, deserialized.ThermostatOutputMap[3].Item2, 2);
        }

        [Fact]
        public void Test_GP_ThermostatOutputMap()
        {
            var original = CreateTestModelWithTupleMap();

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDictionaryTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<DictionaryTupleModel>(ms);

            Assert.NotNull(deserialized.ThermostatOutputMap);
            Assert.Equal(3, deserialized.ThermostatOutputMap.Count);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(1));
            Assert.Equal(21.5, deserialized.ThermostatOutputMap[1].Item1, 2);
            Assert.Equal(68.0, deserialized.ThermostatOutputMap[1].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(2));
            Assert.Equal(22.0, deserialized.ThermostatOutputMap[2].Item1, 2);
            Assert.Equal(70.5, deserialized.ThermostatOutputMap[2].Item2, 2);
            
            Assert.True(deserialized.ThermostatOutputMap.ContainsKey(3));
            Assert.Equal(20.5, deserialized.ThermostatOutputMap[3].Item1, 2);
            Assert.Equal(65.2, deserialized.ThermostatOutputMap[3].Item2, 2);
        }

        [Fact]
        public void Test_PG_DoubleTuple()
        {
            var original = new DictionaryTupleModel
            {
                DoubleTuple = new Tuple<double, double>(1.234, 5.678)
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDictionaryTupleModel(bytes);

            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(1.234, deserialized.DoubleTuple.Item1, 3);
            Assert.Equal(5.678, deserialized.DoubleTuple.Item2, 3);
        }

        [Fact]
        public void Test_GG_DoubleTuple()
        {
            var original = new DictionaryTupleModel
            {
                DoubleTuple = new Tuple<double, double>(9.876, 4.321)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDictionaryTupleModel(ms, original);
            ms.Position = 0;
            
            var bytes = ms.ToArray();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeDictionaryTupleModel(bytes);

            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(9.876, deserialized.DoubleTuple.Item1, 3);
            Assert.Equal(4.321, deserialized.DoubleTuple.Item2, 3);
        }

        [Fact]
        public void Test_GP_DoubleTuple()
        {
            var original = new DictionaryTupleModel
            {
                DoubleTuple = new Tuple<double, double>(0.123, 7.89)
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeDictionaryTupleModel(ms, original);
            var bytes = ms.ToArray();

            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<DictionaryTupleModel>(ms);

            Assert.NotNull(deserialized.DoubleTuple);
            Assert.Equal(0.123, deserialized.DoubleTuple.Item1, 3);
            Assert.Equal(7.89, deserialized.DoubleTuple.Item2, 3);
        }
    }
}