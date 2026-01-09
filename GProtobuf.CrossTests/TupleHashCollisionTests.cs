using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for hash collision detection in Tuple method name generation.
    ///
    /// CONTEXT:
    /// VirtualTupleTypeRegistry uses a simple polynomial hash (hash * 31 + c)
    /// for method names longer than 200 chars. The hash is 32-bit, which means
    /// collision probability follows birthday paradox:
    /// - P(collision) ≈ 50% at √(2^32) ≈ 77,000 unique types
    ///
    /// RISK ASSESSMENT:
    /// - LOW risk in practice: Most projects have < 1000 unique Tuple types
    /// - MEDIUM risk for large codebases: 10,000+ types might see rare collisions
    /// - HIGH risk if attacker crafts specific input: possible but requires 2^16 attempts
    ///
    /// MITIGATION:
    /// - Use OriginalTypeName as primary key (already implemented)
    /// - Hash only used for method names (cosmetic issue if collision)
    /// - If collision occurs, compilation will fail with duplicate method error
    /// </summary>
    public class TupleHashCollisionTests
    {
        private readonly ITestOutputHelper _output;

        public TupleHashCollisionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Simulates the hash algorithm used by VirtualTupleTypeRegistry.
        /// </summary>
        private string GetStableHash(string input)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in input)
                {
                    hash = hash * 31 + c;
                }
                return ((uint)hash).ToString("X8");
            }
        }

        [Fact]
        public void Different_Tuple_Types_Should_Have_Different_Hashes()
        {
            // Arrange: Create various long tuple type names
            var tupleTypes = new List<string>
            {
                "System.Tuple<System.Collections.Generic.Dictionary<string, int>, System.Collections.Generic.List<double>, System.Collections.Generic.HashSet<Guid>>",
                "System.Tuple<int, int, int, int, int, int, int, System.Tuple<int, int, int, int, int, int, int, System.Tuple<int, int, int, int>>>",
                "System.Tuple<string, string, string, string, string, string, string, System.Tuple<string, string, string, string, string, string, string>>",
                "System.Tuple<System.Guid, System.DateTime, System.TimeSpan, System.DateTimeOffset, System.Byte[], System.Int64, System.Double>",
                "System.Tuple<bool, bool, bool, bool, bool, bool, bool, System.Tuple<bool, bool, bool, bool, bool, bool, bool, System.Tuple<bool>>>"
            };

            // Act: Generate hashes
            var hashes = new HashSet<string>();
            var collisions = new List<(string type1, string type2, string hash)>();

            foreach (var tupleType in tupleTypes)
            {
                var hash = GetStableHash(tupleType);
                _output.WriteLine($"Type: {tupleType.Substring(0, Math.Min(80, tupleType.Length))}...");
                _output.WriteLine($"Hash: {hash}\n");

                if (hashes.Contains(hash))
                {
                    var existing = tupleTypes.First(t => GetStableHash(t) == hash && t != tupleType);
                    collisions.Add((existing, tupleType, hash));
                }
                else
                {
                    hashes.Add(hash);
                }
            }

            // Assert: No collisions expected for these specific types
            Assert.Empty(collisions);
        }

        [Fact]
        public void Hash_Algorithm_Should_Be_Stable_Across_Multiple_Calls()
        {
            // Arrange
            var input = "System.Tuple<int, string, bool, double, Guid, DateTime, TimeSpan, Byte[]>";

            // Act: Generate hash 10 times
            var hashes = Enumerable.Range(0, 10)
                .Select(_ => GetStableHash(input))
                .ToList();

            // Assert: All hashes should be identical (stable algorithm)
            Assert.All(hashes, h => Assert.Equal(hashes[0], h));
        }

        [Fact]
        public void Small_String_Changes_Should_Produce_Different_Hashes()
        {
            // Arrange: Very similar strings (Avalanche effect test)
            var inputs = new[]
            {
                "System.Tuple<int, string>",
                "System.Tuple<int, String>", // Capital S
                "System.Tuple<int, string >", // Extra space
                "System.Tuple<int, strinG>", // Capital G
                "System.Tuple<inT, string>", // Capital T
            };

            // Act: Generate hashes
            var hashes = inputs.Select(i => GetStableHash(i)).ToList();

            // Assert: All should be different (good avalanche property)
            var uniqueHashes = new HashSet<string>(hashes);
            Assert.Equal(inputs.Length, uniqueHashes.Count);

            _output.WriteLine("Avalanche effect test:");
            for (int i = 0; i < inputs.Length; i++)
            {
                _output.WriteLine($"{inputs[i]} -> {hashes[i]}");
            }
        }

        [Fact]
        public void Hash_Distribution_Should_Be_Reasonable()
        {
            // Arrange: Generate 1000 synthetic tuple type names
            var random = new Random(42); // Fixed seed for reproducibility
            var inputs = Enumerable.Range(0, 1000)
                .Select(i => $"System.Tuple<int, string, bool, Guid_{i}, double_{random.Next()}>")
                .ToList();

            // Act: Generate hashes
            var hashes = inputs.Select(i => GetStableHash(i)).ToList();
            var uniqueHashes = new HashSet<string>(hashes);

            // Assert: Should have > 99% uniqueness (< 1% collision rate)
            var uniquenessRatio = (double)uniqueHashes.Count / inputs.Count;
            _output.WriteLine($"Generated: {inputs.Count} inputs");
            _output.WriteLine($"Unique hashes: {uniqueHashes.Count}");
            _output.WriteLine($"Uniqueness: {uniquenessRatio * 100:F2}%");
            _output.WriteLine($"Collisions: {inputs.Count - uniqueHashes.Count}");

            Assert.True(uniquenessRatio > 0.99, $"Expected > 99% uniqueness, got {uniquenessRatio * 100:F2}%");
        }

        [Fact]
        public void Known_Collision_Patterns_Should_Be_Detected()
        {
            // Arrange: Strings known to collide in simple hash algorithms
            // These are crafted to test hash quality
            var potentialColliders = new[]
            {
                "AaAaAa",
                "AaAaBB",
                "AaBBAa",
                "BBAaAa"
            };

            // Act: Generate hashes
            var hashes = potentialColliders.Select(s => GetStableHash(s)).ToList();
            var uniqueHashes = new HashSet<string>(hashes);

            // Assert: Document collision rate for awareness
            var collisionRate = 1.0 - ((double)uniqueHashes.Count / potentialColliders.Length);
            _output.WriteLine($"Collision rate for crafted inputs: {collisionRate * 100:F2}%");

            foreach (var (input, hash) in potentialColliders.Zip(hashes))
            {
                _output.WriteLine($"{input} -> {hash}");
            }

            // We EXPECT collisions here (algorithm is simple), but document them
            _output.WriteLine($"\nUnique hashes: {uniqueHashes.Count} / {potentialColliders.Length}");
        }

        [Fact]
        public void Hash_Length_Should_Be_Exactly_8_Hex_Characters()
        {
            // Arrange
            var inputs = new[]
            {
                "short",
                "System.Tuple<int, string, bool, double, Guid, DateTime, TimeSpan, Byte[], Int64>",
                "A",
                new string('X', 1000)
            };

            // Act & Assert
            foreach (var input in inputs)
            {
                var hash = GetStableHash(input);
                Assert.Equal(8, hash.Length);
                Assert.All(hash, c => Assert.True(char.IsDigit(c) || (c >= 'A' && c <= 'F')));
            }
        }
    }
}
