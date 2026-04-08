using System;
using System.Collections.Generic;
using System.IO;
using GProtobuf.Core;
using GProtobuf.Tests.TestModel;
using GProtobuf.CrossTests.TestModel;
using Xunit;
using System.Drawing;

// Register standalone serializers for List<BasicTypesModel>
[assembly: GenerateSerializer(typeof(List<BasicTypesModel>))]

// Register standalone serializer for List<ModelBase> (polymorphic list with inheritance)
[assembly: GenerateSerializer(typeof(List<ModelBase>))]

// Register standalone serializers for primitive collections
[assembly: GenerateSerializer(typeof(List<bool>))]
[assembly: GenerateSerializer(typeof(List<char>))]
[assembly: GenerateSerializer(typeof(List<int>))]
[assembly: GenerateSerializer(typeof(List<uint>))]
[assembly: GenerateSerializer(typeof(List<float>))]
[assembly: GenerateSerializer(typeof(List<double>))]
[assembly: GenerateSerializer(typeof(List<sbyte>))]
[assembly: GenerateSerializer(typeof(List<byte>))]
[assembly: GenerateSerializer(typeof(List<short>))]
[assembly: GenerateSerializer(typeof(List<ushort>))]
[assembly: GenerateSerializer(typeof(List<long>))]
[assembly: GenerateSerializer(typeof(List<ulong>))]
[assembly: GenerateSerializer(typeof(List<string>))]
[assembly: GenerateSerializer(typeof(List<HorizontalAlign>))]
[assembly: GenerateSerializer(typeof(List<VerticalAlign>))]
[assembly: GenerateSerializer(typeof(int[]))]
[assembly: GenerateSerializer(typeof(string[]))]

// Register standalone serializers for dictionaries
[assembly: GenerateSerializer(typeof(Dictionary<int, int>))]
[assembly: GenerateSerializer(typeof(Dictionary<string, int>))]
[assembly: GenerateSerializer(typeof(Dictionary<int, string>))]
[assembly: GenerateSerializer(typeof(Dictionary<string, string>))]

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for standalone type serialization (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;)
    /// using [GenerateSerializer] attribute.
    /// </summary>
    public class StandaloneSerializerTests
    {
        [Fact]
        public void Test_PG_ListOfBasicTypesModel()
        {
            // Arrange: Create list and serialize with protobuf-net
            var original = new List<BasicTypesModel>
            {
                new BasicTypesModel { IntValue = 42, StringValue = "Hello" },
                new BasicTypesModel { IntValue = 100, StringValue = "World" },
                new BasicTypesModel { IntValue = -1, StringValue = "Negative" }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeListOfBasicTypesModel(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(3, deserialized.Count);
            Assert.Equal(42, deserialized[0].IntValue);
            Assert.Equal("Hello", deserialized[0].StringValue);
            Assert.Equal(100, deserialized[1].IntValue);
            Assert.Equal("World", deserialized[1].StringValue);
            Assert.Equal(-1, deserialized[2].IntValue);
            Assert.Equal("Negative", deserialized[2].StringValue);
        }

        [Fact]
        public void Test_GG_ListOfBasicTypesModel()
        {
            // Arrange: Create list and serialize with GProtobuf
            var original = new List<BasicTypesModel>
            {
                new BasicTypesModel { IntValue = 42, StringValue = "Hello" },
                new BasicTypesModel { IntValue = 100, StringValue = "World" }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeListOfBasicTypesModel(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeListOfBasicTypesModel(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(2, deserialized.Count);
            Assert.Equal(42, deserialized[0].IntValue);
            Assert.Equal("Hello", deserialized[0].StringValue);
            Assert.Equal(100, deserialized[1].IntValue);
            Assert.Equal("World", deserialized[1].StringValue);
        }

        [Fact]
        public void Test_GP_ListOfBasicTypesModel()
        {
            // Arrange: Create list and serialize with GProtobuf
            var original = new List<BasicTypesModel>
            {
                new BasicTypesModel { IntValue = 42, StringValue = "Hello", DoubleValue = 3.14 },
                new BasicTypesModel { IntValue = 100, StringValue = "World", BoolValue = true }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeListOfBasicTypesModel(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<List<BasicTypesModel>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(2, deserialized.Count);
            Assert.Equal(42, deserialized[0].IntValue);
            Assert.Equal("Hello", deserialized[0].StringValue);
            Assert.Equal(3.14, deserialized[0].DoubleValue, 2);
            Assert.Equal(100, deserialized[1].IntValue);
            Assert.Equal("World", deserialized[1].StringValue);
            Assert.True(deserialized[1].BoolValue);
        }

        [Fact]
        public void Test_EmptyList()
        {
            // Arrange: Empty list
            var original = new List<BasicTypesModel>();

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeListOfBasicTypesModel(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Empty(deserialized);
        }

        #region List<char> Tests

        [Fact]
        public void Test_GG_ListOfChar()
        {
            // Arrange: Serialize with GProtobuf
            var original = new List<char> { 'A', 'B', 'C', 'Z', '\u00E9' };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfChar(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeListOfChar(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        #endregion

        #region List<int> Tests

        [Fact]
        public void Test_PG_ListOfInt()
        {
            // Arrange: Serialize with protobuf-net
            var original = new List<int> { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeListOfInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GP_ListOfInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new List<int> { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<List<int>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GG_ListOfInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new List<int> { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeListOfInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        #endregion

        #region List<string> Tests

        [Fact]
        public void Test_PG_ListOfString()
        {
            // Arrange: Serialize with protobuf-net
            var original = new List<string> { "Hello", "World", "Test" };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeListOfString(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GP_ListOfString()
        {
            // Arrange: Serialize with GProtobuf
            var original = new List<string> { "Hello", "World", "Test" };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfString(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<List<string>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GG_ListOfString()
        {
            // Arrange: Serialize with GProtobuf
            var original = new List<string> { "Hello", "World", "Test" };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfString(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeListOfString(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        #endregion

        #region Null Serialization Tests

        [Fact]
        public void Test_SerializeDictionary_Null_ShouldNotThrow()
        {
            // Arrange: null dictionary
            Dictionary<int, int> nullDict = null;

            using var ms = new MemoryStream();

            // Act & Assert: should not throw NullReferenceException
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfInt32AndInt32(ms, nullDict);

            // Stream should be empty (nothing written for null)
            Assert.Equal(0, ms.Length);
        }

        [Fact]
        public void Test_SerializeDictionary_Null_StringString_ShouldNotThrow()
        {
            // Arrange: null dictionary
            Dictionary<string, string> nullDict = null;

            using var ms = new MemoryStream();

            // Act & Assert: should not throw NullReferenceException
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfStringAndString(ms, nullDict);

            // Stream should be empty (nothing written for null)
            Assert.Equal(0, ms.Length);
        }

        [Fact]
        public void Test_SerializeList_Null_ShouldNotThrow()
        {
            // Arrange: null list
            List<int> nullList = null;

            using var ms = new MemoryStream();

            // Act & Assert: should not throw NullReferenceException
            global::GProtobuf.Generated.Serialization.Serializers.SerializeListOfInt32(ms, nullList);

            // Stream should be empty (nothing written for null)
            Assert.Equal(0, ms.Length);
        }

        [Fact]
        public void Test_SerializeArray_Null_ShouldNotThrow()
        {
            // Arrange: null array
            int[] nullArray = null;

            using var ms = new MemoryStream();

            // Act & Assert: should not throw NullReferenceException
            global::GProtobuf.Generated.Serialization.Serializers.SerializeArrayOfInt32(ms, nullArray);

            // Stream should be empty (nothing written for null)
            Assert.Equal(0, ms.Length);
        }

        #endregion

        #region Dictionary<int, int> Tests

        [Fact]
        public void Test_PG_DictionaryIntInt()
        {
            // Arrange: Serialize with protobuf-net
            var original = new Dictionary<int, int> { { 1, 100 }, { 2, 200 }, { 3, 300 } };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeDictionaryOfInt32AndInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        [Fact]
        public void Test_GP_DictionaryIntInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new Dictionary<int, int> { { 1, 100 }, { 2, 200 }, { 3, 300 } };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfInt32AndInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<Dictionary<int, int>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        [Fact]
        public void Test_GG_DictionaryIntInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new Dictionary<int, int> { { 1, 100 }, { 2, 200 }, { 3, 300 } };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfInt32AndInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeDictionaryOfInt32AndInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        #endregion

        #region Dictionary<string, int> Tests

        [Fact]
        public void Test_PG_DictionaryStringInt()
        {
            // Arrange: Serialize with protobuf-net
            var original = new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeDictionaryOfStringAndInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        [Fact]
        public void Test_GP_DictionaryStringInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfStringAndInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<Dictionary<string, int>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        [Fact]
        public void Test_GG_DictionaryStringInt()
        {
            // Arrange: Serialize with GProtobuf
            var original = new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeDictionaryOfStringAndInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeDictionaryOfStringAndInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Count, deserialized.Count);
            foreach (var kvp in original)
            {
                Assert.True(deserialized.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value, deserialized[kvp.Key]);
            }
        }

        #endregion

        #region int[] Array Tests

        [Fact]
        public void Test_PG_IntArray()
        {
            // Arrange: Serialize with protobuf-net
            var original = new int[] { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeArrayOfInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Length, deserialized.Length);
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GP_IntArray()
        {
            // Arrange: Serialize with GProtobuf
            var original = new int[] { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeArrayOfInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<int[]>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Length, deserialized.Length);
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        [Fact]
        public void Test_GG_IntArray()
        {
            // Arrange: Serialize with GProtobuf
            var original = new int[] { 1, 2, 3, 42, 100, -1 };

            using var ms = new MemoryStream();
            global::GProtobuf.Generated.Serialization.Serializers.SerializeArrayOfInt32(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.Generated.Serialization.Deserializers.DeserializeArrayOfInt32(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(original.Length, deserialized.Length);
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(original[i], deserialized[i]);
            }
        }

        #endregion

        #region List<ModelBase> Polymorphic Tests

        /// <summary>
        /// Test that protobuf-net serialized List of base class with derived instances
        /// can be deserialized by GProtobuf.
        /// </summary>
        [Fact]
        public void Test_PG_ListOfModelBase_Polymorphic()
        {
            // Arrange: Create list with mixed derived types and serialize with protobuf-net
            var original = new List<ModelBase>
            {
                new ModelInh1 { Id = 1, Description = "First", Guid = Guid.NewGuid() },
                new ModelInh2 { Id = 2, Description1 = "Second", Guid1 = Guid.NewGuid() },
                new ModelInh3 { Id = 3, Description2 = "Third", Guid2 = Guid.NewGuid() }
            };

            using var ms = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeListOfModelBase(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(3, deserialized.Count);

            Assert.IsType<ModelInh1>(deserialized[0]);
            var item1 = (ModelInh1)deserialized[0];
            Assert.Equal(1, item1.Id);
            Assert.Equal("First", item1.Description);

            Assert.IsType<ModelInh2>(deserialized[1]);
            var item2 = (ModelInh2)deserialized[1];
            Assert.Equal(2, item2.Id);
            Assert.Equal("Second", item2.Description1);

            Assert.IsType<ModelInh3>(deserialized[2]);
            var item3 = (ModelInh3)deserialized[2];
            Assert.Equal(3, item3.Id);
            Assert.Equal("Third", item3.Description2);
        }

        /// <summary>
        /// Test that GProtobuf serialized List of base class with derived instances
        /// can be deserialized by GProtobuf.
        /// </summary>
        [Fact]
        public void Test_GG_ListOfModelBase_Polymorphic()
        {
            // Arrange: Create list with mixed derived types and serialize with GProtobuf
            var original = new List<ModelBase>
            {
                new ModelInh1 { Id = 10, Description = "Hello", Guid = Guid.NewGuid() },
                new ModelInh2 { Id = 20, Description1 = "World", Guid1 = Guid.NewGuid() }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeListOfModelBase(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with GProtobuf
            var deserialized = global::GProtobuf.CrossTests.TestModel.Serialization.Deserializers.DeserializeListOfModelBase(bytes);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(2, deserialized.Count);

            Assert.IsType<ModelInh1>(deserialized[0]);
            var item1 = (ModelInh1)deserialized[0];
            Assert.Equal(10, item1.Id);
            Assert.Equal("Hello", item1.Description);

            Assert.IsType<ModelInh2>(deserialized[1]);
            var item2 = (ModelInh2)deserialized[1];
            Assert.Equal(20, item2.Id);
            Assert.Equal("World", item2.Description1);
        }

        /// <summary>
        /// Test that GProtobuf serialized List of base class with derived instances
        /// can be deserialized by protobuf-net (cross-compatibility).
        /// This tests the ProtoInclude wrapper fix.
        /// </summary>
        [Fact]
        public void Test_GP_ListOfModelBase_Polymorphic()
        {
            // Arrange: Create list with mixed derived types and serialize with GProtobuf
            var original = new List<ModelBase>
            {
                new ModelInh1 { Id = 100, Description = "Test1", Guid = Guid.NewGuid() },
                new ModelInh2 { Id = 200, Description1 = "Test2", Guid1 = Guid.NewGuid() },
                new ModelInh3 { Id = 300, Description2 = "Test3", Guid2 = Guid.NewGuid() }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeListOfModelBase(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<List<ModelBase>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(3, deserialized.Count);

            Assert.IsType<ModelInh1>(deserialized[0]);
            var item1 = (ModelInh1)deserialized[0];
            Assert.Equal(100, item1.Id);
            Assert.Equal("Test1", item1.Description);

            Assert.IsType<ModelInh2>(deserialized[1]);
            var item2 = (ModelInh2)deserialized[1];
            Assert.Equal(200, item2.Id);
            Assert.Equal("Test2", item2.Description1);

            Assert.IsType<ModelInh3>(deserialized[2]);
            var item3 = (ModelInh3)deserialized[2];
            Assert.Equal(300, item3.Id);
            Assert.Equal("Test3", item3.Description2);
        }

        /// <summary>
        /// Test serialization roundtrip for multi-level inheritance (Model15 extends ModelInh1 extends ModelBase).
        /// NOTE: Multi-level inheritance has a separate issue with nested wrapper calculation.
        /// This test is skipped until that issue is fixed.
        /// </summary>
        [Fact]
        public void Test_GP_ListOfModelBase_MultiLevelInheritance()
        {
            // Arrange: Create list with multi-level inheritance and serialize with GProtobuf
            var original = new List<ModelBase>
            {
                new Model15 { Id = 42, Description = "Parent", Guid = Guid.NewGuid(), Description15 = "Child", Guid15 = Guid.NewGuid() }
            };

            using var ms = new MemoryStream();
            global::GProtobuf.CrossTests.TestModel.Serialization.Serializers.SerializeListOfModelBase(ms, original);
            var bytes = ms.ToArray();

            // Act: Deserialize with protobuf-net
            ms.Position = 0;
            var deserialized = ProtoBuf.Serializer.Deserialize<List<ModelBase>>(ms);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Single(deserialized);

            Assert.IsType<Model15>(deserialized[0]);
            var item = (Model15)deserialized[0];
            Assert.Equal(42, item.Id);
            Assert.Equal("Parent", item.Description);
            Assert.Equal("Child", item.Description15);
        }

        #endregion
    }
}
