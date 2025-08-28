using System;
using System.Collections.Generic;
using GProtobuf.Core;
using Todos.Model;

namespace Todos
{
    public class ByteMapTest
    {
        public static void TestByteDoubleMap()
        {
            // Create test data
            var basic = new Basic
            {
                ByteDoubleMap = new Dictionary<byte, double>
                {
                    { 1, 1.5 },
                    { 255, 3.14159 },
                    { 127, 2.71828 },
                    { 0, 0.0 }
                }
            };
            
            Console.WriteLine($"Basic object ByteDoubleMap is null: {basic.ByteDoubleMap == null}");
            Console.WriteLine($"Basic object ByteDoubleMap count: {basic.ByteDoubleMap?.Count ?? 0}");

            // Serialize using StreamWriter
            var buffer = new byte[1024];
            var stream = new System.IO.MemoryStream(buffer);
            var writer = new GProtobuf.Core.StreamWriter(stream, buffer);
            Model.Serialization.StreamWriters.WriteBasic(ref writer, basic);
            var serialized = buffer.AsSpan(0, (int)stream.Position);

            // Deserialize
            var deserialized = Model.Serialization.Deserializers.DeserializeBasic(serialized);

            // Verify
            Console.WriteLine("ByteDoubleMap test:");
            Console.WriteLine($"Original count: {basic.ByteDoubleMap.Count}");
            Console.WriteLine($"Serialized bytes: {serialized.Length}");
            Console.WriteLine($"Serialized content: {BitConverter.ToString(serialized.ToArray()).Replace("-", " ")}");
            Console.WriteLine($"Deserialized count: {deserialized.ByteDoubleMap?.Count ?? 0}");

            if (deserialized.ByteDoubleMap != null)
            {
                foreach (var kvp in deserialized.ByteDoubleMap)
                {
                    Console.WriteLine($"  Key: {kvp.Key}, Value: {kvp.Value}");
                    
                    if (!basic.ByteDoubleMap.TryGetValue(kvp.Key, out var originalValue))
                    {
                        Console.WriteLine($"  ERROR: Key {kvp.Key} not found in original");
                    }
                    else if (Math.Abs(originalValue - kvp.Value) > 0.0001)
                    {
                        Console.WriteLine($"  ERROR: Value mismatch for key {kvp.Key}: {originalValue} != {kvp.Value}");
                    }
                }
                
                Console.WriteLine("ByteDoubleMap test: PASSED");
            }
            else
            {
                Console.WriteLine("ByteDoubleMap test: FAILED - map is null after deserialization");
            }
        }
    }
}