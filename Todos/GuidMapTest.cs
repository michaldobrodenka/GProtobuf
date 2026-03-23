using System;
using System.Collections.Generic;
using System.Linq;
using GProtobuf.Core;
using Todos.Model;

namespace Todos
{
    public class GuidMapTest
    {
        public static void TestGuidLongMap()
        {
            // Create test data
            var guid1 = Guid.NewGuid();
            var guid2 = new Guid("12345678-1234-1234-1234-123456789abc");
            var guid3 = Guid.Empty;
            
            var basic = new Basic
            {
                GuidLongMap = new Dictionary<Guid, long>
                {
                    { guid1, 100 },
                    { guid2, 200 },
                    { guid3, 0 }
                }
            };
            
            Console.WriteLine($"Original GuidLongMap count: {basic.GuidLongMap?.Count ?? 0}");

            // Serialize using Serializers
            var stream = new System.IO.MemoryStream();
            Model.Serialization.Serializers.SerializeBasic(stream, basic);
            var serialized = stream.ToArray();

            // Deserialize
            var deserialized = Model.Serialization.Deserializers.DeserializeBasic(serialized);

            // Verify
            Console.WriteLine("GuidLongMap test:");
            Console.WriteLine($"Original count: {basic.GuidLongMap.Count}");
            Console.WriteLine($"Serialized bytes: {serialized.Length}");
            Console.WriteLine($"Deserialized count: {deserialized.GuidLongMap?.Count ?? 0}");

            if (deserialized.GuidLongMap != null)
            {
                foreach (var kvp in deserialized.GuidLongMap)
                {
                    Console.WriteLine($"  Key: {kvp.Key}, Value: {kvp.Value}");
                    
                    if (!basic.GuidLongMap.TryGetValue(kvp.Key, out var originalValue))
                    {
                        Console.WriteLine($"  ERROR: Guid {kvp.Key} not found in original");
                    }
                    else if (originalValue != kvp.Value)
                    {
                        Console.WriteLine($"  ERROR: Value mismatch for guid {kvp.Key}: {originalValue} != {kvp.Value}");
                    }
                }
                
                // Check all original keys are present
                foreach (var kvp in basic.GuidLongMap)
                {
                    if (!deserialized.GuidLongMap.ContainsKey(kvp.Key))
                    {
                        Console.WriteLine($"  ERROR: Original guid {kvp.Key} not found in deserialized");
                    }
                }
                
                Console.WriteLine("GuidLongMap test: PASSED");
            }
            else
            {
                Console.WriteLine("GuidLongMap test: FAILED - map is null after deserialization");
            }
        }
        
        public static void TestProtobufNetCompatibility()
        {
            Console.WriteLine("\n=== Testing protobuf-net compatibility ===");
            
            var guid1 = new Guid("12345678-1234-1234-1234-123456789abc");
            var basic = new Basic
            {
                GuidLongMap = new Dictionary<Guid, long>
                {
                    { guid1, 42 }
                }
            };
            
            // Serialize with protobuf-net
            var pbNetStream = new System.IO.MemoryStream();
            ProtoBuf.Serializer.Serialize(pbNetStream, basic);
            var pbNetBytes = pbNetStream.ToArray();
            
            Console.WriteLine($"protobuf-net serialized {pbNetBytes.Length} bytes");
            Console.WriteLine($"Bytes: {BitConverter.ToString(pbNetBytes).Replace("-", " ")}");
            
            // Deserialize with GProtobuf
            var gProtobufDeserialized = Model.Serialization.Deserializers.DeserializeBasic(pbNetBytes);
            
            if (gProtobufDeserialized.GuidLongMap != null && gProtobufDeserialized.GuidLongMap.Count == 1)
            {
                var kvp = gProtobufDeserialized.GuidLongMap.First();
                if (kvp.Key == guid1 && kvp.Value == 42)
                {
                    Console.WriteLine("protobuf-net -> GProtobuf: PASSED");
                }
                else
                {
                    Console.WriteLine($"protobuf-net -> GProtobuf: FAILED - got {kvp.Key}:{kvp.Value}");
                }
            }
            else
            {
                Console.WriteLine("protobuf-net -> GProtobuf: FAILED - map is null or wrong count");
            }
            
            // Now test GProtobuf -> protobuf-net
            var gProtobufStream = new System.IO.MemoryStream();
            Model.Serialization.Serializers.SerializeBasic(gProtobufStream, basic);
            var gProtobufBytes = gProtobufStream.ToArray();
            
            Console.WriteLine($"GProtobuf serialized {gProtobufBytes.Length} bytes");
            Console.WriteLine($"Bytes: {BitConverter.ToString(gProtobufBytes).Replace("-", " ")}");
            
            var pbNetDeserialized = ProtoBuf.Serializer.Deserialize<Basic>(new System.IO.MemoryStream(gProtobufBytes));
            
            if (pbNetDeserialized.GuidLongMap != null && pbNetDeserialized.GuidLongMap.Count == 1)
            {
                var kvp = pbNetDeserialized.GuidLongMap.First();
                if (kvp.Key == guid1 && kvp.Value == 42)
                {
                    Console.WriteLine("GProtobuf -> protobuf-net: PASSED");
                }
                else
                {
                    Console.WriteLine($"GProtobuf -> protobuf-net: FAILED - got {kvp.Key}:{kvp.Value}");
                }
            }
            else
            {
                Console.WriteLine("GProtobuf -> protobuf-net: FAILED - map is null or wrong count");
            }
        }
    }
}