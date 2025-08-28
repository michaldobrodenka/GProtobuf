using FluentAssertions;
using GProtobuf.CrossTests.TestModel;
using GProtobuf.Tests;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GProtobuf.CrossTests
{
    public class MapWithArrayKeysTests : BaseSerializationTest
    {
        // Helper class to compare arrays as dictionary keys
        private class ArrayKeyComparer<T> : IEqualityComparer<T[]>
        {
            public bool Equals(T[] x, T[] y)
            {
                if (x == null && y == null) return true;
                if (x == null || y == null) return false;
                return x.SequenceEqual(y);
            }
            
            public int GetHashCode(T[] obj)
            {
                if (obj == null) return 0;
                int hash = 17;
                foreach (var item in obj)
                {
                    hash = hash * 31 + (item?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }
        
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_int_array_keys()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                IntArrayStringMap = new Dictionary<int[], string>(new ArrayKeyComparer<int>())
                {
                    { new int[] { 1, 2, 3 }, "first" },
                    { new int[] { 4, 5 }, "second" },
                    { new int[] { }, "empty" },
                    { new int[] { 100 }, "single" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.IntArrayStringMap.Should().NotBeNull();
            result.IntArrayStringMap.Should().HaveCount(4);
            
            // Check each entry by finding matching keys
            AssertDictionaryContainsKey(result.IntArrayStringMap, new int[] { 1, 2, 3 }, "first");
            AssertDictionaryContainsKey(result.IntArrayStringMap, new int[] { 4, 5 }, "second");
            AssertDictionaryContainsKey(result.IntArrayStringMap, new int[] { }, "empty");
            AssertDictionaryContainsKey(result.IntArrayStringMap, new int[] { 100 }, "single");
        }
        
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_string_array_keys()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                StringArrayIntMap = new Dictionary<string[], int>(new ArrayKeyComparer<string>())
                {
                    { new string[] { "hello", "world" }, 42 },
                    { new string[] { "foo", "bar", "baz" }, 100 },
                    { new string[] { "single" }, 1 },
                    { new string[] { }, 0 }
                }
            };
            
            // Act - Serialize with GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.StringArrayIntMap.Should().NotBeNull();
            result.StringArrayIntMap.Should().HaveCount(4);
            
            AssertDictionaryContainsKey(result.StringArrayIntMap, new string[] { "hello", "world" }, 42);
            AssertDictionaryContainsKey(result.StringArrayIntMap, new string[] { "foo", "bar", "baz" }, 100);
            AssertDictionaryContainsKey(result.StringArrayIntMap, new string[] { "single" }, 1);
            AssertDictionaryContainsKey(result.StringArrayIntMap, new string[] { }, 0);
        }
        
        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_custom_array_keys_and_collection_values()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                CustomNestedArrayIntListMap = new Dictionary<CustomNested[], List<int>>(new ArrayKeyComparer<CustomNested>())
                {
                    { 
                        new CustomNested[] 
                        { 
                            new CustomNested { Id = 1, Name = "First", Value = 1.5 },
                            new CustomNested { Id = 2, Name = "Second", Value = 2.5 }
                        }, 
                        new List<int> { 10, 20, 30 } 
                    },
                    { 
                        new CustomNested[] 
                        { 
                            new CustomNested { Id = 3, Name = "Third", Value = 3.5 }
                        }, 
                        new List<int> { 40, 50 } 
                    },
                    { 
                        new CustomNested[] { }, 
                        new List<int> { 60 } 
                    }
                }
            };
            
            // Act - Serialize with GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.CustomNestedArrayIntListMap.Should().NotBeNull();
            result.CustomNestedArrayIntListMap.Should().HaveCount(3);
            
            // Check first entry
            var key1 = result.CustomNestedArrayIntListMap.Keys.FirstOrDefault(k => 
                k.Length == 2 && k[0].Id == 1 && k[1].Id == 2);
            key1.Should().NotBeNull();
            result.CustomNestedArrayIntListMap[key1].Should().Equal(10, 20, 30);
            
            // Check second entry
            var key2 = result.CustomNestedArrayIntListMap.Keys.FirstOrDefault(k => 
                k.Length == 1 && k[0].Id == 3);
            key2.Should().NotBeNull();
            result.CustomNestedArrayIntListMap[key2].Should().Equal(40, 50);
            
            // Check empty key entry
            var emptyKey = result.CustomNestedArrayIntListMap.Keys.FirstOrDefault(k => k.Length == 0);
            emptyKey.Should().NotBeNull();
            result.CustomNestedArrayIntListMap[emptyKey].Should().Equal(60);
        }
        
        private string GetHexDump(byte[] data, string label)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"\n{label} ({data.Length} bytes):");
            for (int i = 0; i < data.Length; i += 16)
            {
                sb.Append($"{i:X4}: ");
                
                // Hex bytes
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                        sb.Append($"{data[i + j]:X2} ");
                    else
                        sb.Append("   ");
                }
                
                sb.Append("  ");
                
                // ASCII representation
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    byte b = data[i + j];
                    if (b >= 32 && b < 127)
                        sb.Append((char)b);
                    else
                        sb.Append('.');
                }
                
                sb.AppendLine();
            }
            return sb.ToString();
        }

        [Fact]
        public void Should_serialize_and_deserialize_Dictionary_with_byte_array_keys()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                ByteArrayStringMap = new Dictionary<byte[], string>(new ArrayKeyComparer<byte>())
                {
                    { new byte[] { 0x01, 0x02, 0x03 }, "binary1" },
                    { new byte[] { 0xFF, 0xFE }, "binary2" },
                    { new byte[] { 0x00 }, "null_byte" },
                    { new byte[] { }, "empty_bytes" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var gpData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Serialize with protobuf-net for comparison
            var pbData = SerializeWithProtobufNet(model);
            
            // Debug output  
            Console.WriteLine($"\nGProtobuf bytes: {string.Join(",", gpData.Select(b => b.ToString("X2")))}");
            Console.WriteLine($"protobuf-net bytes: {string.Join(",", pbData.Select(b => b.ToString("X2")))}");
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(gpData, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.ByteArrayStringMap.Should().NotBeNull();
            result.ByteArrayStringMap.Should().HaveCount(4);
            
            AssertDictionaryContainsKey(result.ByteArrayStringMap, new byte[] { 0x01, 0x02, 0x03 }, "binary1");
            AssertDictionaryContainsKey(result.ByteArrayStringMap, new byte[] { 0xFF, 0xFE }, "binary2");
            AssertDictionaryContainsKey(result.ByteArrayStringMap, new byte[] { 0x00 }, "null_byte");
            AssertDictionaryContainsKey(result.ByteArrayStringMap, new byte[] { }, "empty_bytes");
        }
        
        [Fact]
        public void Should_handle_null_maps_with_array_keys()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                IntArrayStringMap = null,
                StringArrayIntMap = null,
                CustomNestedArrayIntListMap = null,
                ByteArrayStringMap = null
            };
            
            // Act - Serialize with GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.IntArrayStringMap.Should().BeNull();
            result.StringArrayIntMap.Should().BeNull();
            result.CustomNestedArrayIntListMap.Should().BeNull();
            result.ByteArrayStringMap.Should().BeNull();
        }
        
        [Fact]
        public void Should_maintain_cross_compatibility_with_protobuf_net_for_array_keys()
        {
            // Arrange
            var model = new MapWithArrayKeysModel
            {
                IntArrayStringMap = new Dictionary<int[], string>(new ArrayKeyComparer<int>())
                {
                    { new int[] { 1, 2, 3 }, "test1" },
                    { new int[] { 4, 5 }, "test2" }
                },
                ByteArrayStringMap = new Dictionary<byte[], string>(new ArrayKeyComparer<byte>())
                {
                    { new byte[] { 0x01, 0x02 }, "bytes" }
                }
            };

            var gpData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            var pbData = SerializeWithProtobufNet(model);
            // Debug output
            Console.WriteLine(GetHexDump(gpData, "GProtobuf serialization"));
            Console.WriteLine(GetHexDump(pbData, "protobuf-net serialization"));

            // Test GProtobuf -> protobuf-net
            var gprotobufData = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            var fromGProtobuf = DeserializeWithProtobufNet<MapWithArrayKeysModel>(gprotobufData);

            // Assert GProtobuf -> protobuf-net
            fromGProtobuf.IntArrayStringMap.Should().NotBeNull();
            fromGProtobuf.IntArrayStringMap.Should().HaveCount(2);
            AssertDictionaryContainsKey(fromGProtobuf.IntArrayStringMap, new int[] { 1, 2, 3 }, "test1");
            AssertDictionaryContainsKey(fromGProtobuf.IntArrayStringMap, new int[] { 4, 5 }, "test2");
            
            fromGProtobuf.ByteArrayStringMap.Should().NotBeNull();
            AssertDictionaryContainsKey(fromGProtobuf.ByteArrayStringMap, new byte[] { 0x01, 0x02 }, "bytes");
            
            // Test protobuf-net -> GProtobuf
            var protobufData = SerializeWithProtobufNet(model);
            var fromProtobuf = DeserializeWithGProtobuf(protobufData, 
                bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert protobuf-net -> GProtobuf
            fromProtobuf.IntArrayStringMap.Should().NotBeNull();
            fromProtobuf.IntArrayStringMap.Should().HaveCount(2);
            AssertDictionaryContainsKey(fromProtobuf.IntArrayStringMap, new int[] { 1, 2, 3 }, "test1");
            AssertDictionaryContainsKey(fromProtobuf.IntArrayStringMap, new int[] { 4, 5 }, "test2");
            
            fromProtobuf.ByteArrayStringMap.Should().NotBeNull();
            AssertDictionaryContainsKey(fromProtobuf.ByteArrayStringMap, new byte[] { 0x01, 0x02 }, "bytes");
        }
        
        [Fact]
        public void Should_handle_large_arrays_as_keys()
        {
            // Arrange
            var largeKey1 = Enumerable.Range(1, 100).ToArray();
            var largeKey2 = Enumerable.Range(101, 100).ToArray();
            
            var model = new MapWithArrayKeysModel
            {
                IntArrayStringMap = new Dictionary<int[], string>(new ArrayKeyComparer<int>())
                {
                    { largeKey1, "large1" },
                    { largeKey2, "large2" }
                }
            };
            
            // Act - Serialize with GProtobuf
            var data = SerializeWithGProtobuf(model, TestModel.Serialization.Serializers.SerializeMapWithArrayKeysModel);
            
            // Act - Deserialize with GProtobuf
            var result = DeserializeWithGProtobuf(data, bytes => TestModel.Serialization.Deserializers.DeserializeMapWithArrayKeysModel(bytes));
            
            // Assert
            result.IntArrayStringMap.Should().NotBeNull();
            result.IntArrayStringMap.Should().HaveCount(2);
            
            var resultKey1 = result.IntArrayStringMap.Keys.FirstOrDefault(k => 
                k.Length == 100 && k[0] == 1 && k[99] == 100);
            resultKey1.Should().NotBeNull();
            result.IntArrayStringMap[resultKey1].Should().Be("large1");
            
            var resultKey2 = result.IntArrayStringMap.Keys.FirstOrDefault(k => 
                k.Length == 100 && k[0] == 101 && k[99] == 200);
            resultKey2.Should().NotBeNull();
            result.IntArrayStringMap[resultKey2].Should().Be("large2");
        }
        
        // Helper method to assert dictionary contains key-value pair when keys are arrays
        private void AssertDictionaryContainsKey<T, TValue>(Dictionary<T[], TValue> dict, T[] expectedKey, TValue expectedValue)
        {
            var actualKey = dict.Keys.FirstOrDefault(k => 
                k.Length == expectedKey.Length && k.SequenceEqual(expectedKey));
            
            actualKey.Should().NotBeNull($"Dictionary should contain key [{string.Join(", ", expectedKey)}]");
            dict[actualKey].Should().Be(expectedValue);
        }
    }
}