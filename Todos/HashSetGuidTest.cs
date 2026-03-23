using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GProtobuf.Core;
using Todos.Model;

namespace Todos
{
    public class HashSetGuidTest
    {
        public static void TestHashSetGuid()
        {
            // Create test data with HashSet<Guid>
            var guid1 = new Guid("12345678-1234-1234-1234-123456789abc");
            var guid2 = new Guid("87654321-4321-4321-4321-cba987654321");
            var guid3 = Guid.Empty;
            var guid4 = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            
            var basic = new Basic
            {
                Guids = new HashSet<Guid>
                {
                    guid1,
                    guid2,
                    guid3,
                    guid4
                }
            };
            
            Console.WriteLine($"Original HashSet<Guid> contains {basic.Guids.Count} items:");
            foreach (var guid in basic.Guids)
            {
                Console.WriteLine($"  {guid}");
            }

            // Serialize using GProtobuf
            var stream = new MemoryStream();
            Model.Serialization.Serializers.SerializeBasic(stream, basic);
            var serialized = stream.ToArray();

            Console.WriteLine($"\nSerialized bytes: {serialized.Length}");
            Console.WriteLine($"Serialized content: {BitConverter.ToString(serialized).Replace("-", " ")}");

            // Deserialize using GProtobuf
            var deserialized = Model.Serialization.Deserializers.DeserializeBasic(serialized);

            // Verify
            Console.WriteLine($"\nDeserialized HashSet<Guid>:");
            if (deserialized.Guids != null)
            {
                Console.WriteLine($"  Count: {deserialized.Guids.Count}");
                foreach (var guid in deserialized.Guids)
                {
                    Console.WriteLine($"  {guid}");
                }
                
                // Check if all original guids are present
                bool allPresent = true;
                foreach (var originalGuid in basic.Guids)
                {
                    if (!deserialized.Guids.Contains(originalGuid))
                    {
                        Console.WriteLine($"  ERROR: Missing guid {originalGuid}");
                        allPresent = false;
                    }
                }
                
                if (allPresent && deserialized.Guids.Count == basic.Guids.Count)
                {
                    Console.WriteLine("\n✅ HashSet<Guid> test: PASSED");
                }
                else
                {
                    Console.WriteLine("\n❌ HashSet<Guid> test: FAILED");
                }
            }
            else
            {
                Console.WriteLine("  HashSet is null after deserialization");
                Console.WriteLine("\n❌ HashSet<Guid> test: FAILED - HashSet is null");
            }
        }
        
        public static void TestProtobufNetCompatibility()
        {
            Console.WriteLine("\n=== Testing HashSet<Guid> protobuf-net Compatibility ===");
            
            // Create test data
            var guid1 = new Guid("11111111-2222-3333-4444-555555555555");
            var guid2 = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var guid3 = Guid.Empty;
            
            var basic = new Basic
            {
                Guids = new HashSet<Guid> { guid1, guid2, guid3 }
            };
            
            // Serialize with GProtobuf
            var gpStream = new MemoryStream();
            Model.Serialization.Serializers.SerializeBasic(gpStream, basic);
            var gpBytes = gpStream.ToArray();
            Console.WriteLine($"GProtobuf serialized {gpBytes.Length} bytes");
            
            // Serialize with protobuf-net
            var pbStream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(pbStream, basic);
            var pbBytes = pbStream.ToArray();
            Console.WriteLine($"protobuf-net serialized {pbBytes.Length} bytes");
            
            // Test GProtobuf -> protobuf-net
            Console.WriteLine("\nGProtobuf -> protobuf-net:");
            var pbDeserialized = ProtoBuf.Serializer.Deserialize<Basic>(new MemoryStream(gpBytes));
            if (pbDeserialized.Guids != null)
            {
                Console.WriteLine($"  Deserialized {pbDeserialized.Guids.Count} guids");
                bool gpToPbOk = pbDeserialized.Guids.SetEquals(basic.Guids);
                Console.WriteLine($"  Result: {(gpToPbOk ? "✅ PASSED" : "❌ FAILED")}");
            }
            else
            {
                Console.WriteLine("  Result: ❌ FAILED - HashSet is null");
            }
            
            // Test protobuf-net -> GProtobuf
            Console.WriteLine("\nprotobuf-net -> GProtobuf:");
            var gpDeserialized = Model.Serialization.Deserializers.DeserializeBasic(pbBytes);
            if (gpDeserialized.Guids != null)
            {
                Console.WriteLine($"  Deserialized {gpDeserialized.Guids.Count} guids");
                bool pbToGpOk = gpDeserialized.Guids.SetEquals(basic.Guids);
                Console.WriteLine($"  Result: {(pbToGpOk ? "✅ PASSED" : "❌ FAILED")}");
            }
            else
            {
                Console.WriteLine("  Result: ❌ FAILED - HashSet is null");
            }
        }
    }
}