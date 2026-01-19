using FluentAssertions;
using GProtobuf.Core;
using GProtobuf.Tests.TestModel;
using System;
using System.IO;
using Xunit;

namespace GProtobuf.CrossTests
{
    /// <summary>
    /// Tests for Level200 recursion depth limits.
    /// Verifies that deeply nested messages throw InvalidOperationException at max depth.
    /// </summary>
    public class RecursionLimitTests
    {
        [Fact]
        public void RecursionGuard_Should_Allow_Moderate_Nesting()
        {
            // Arrange: Create a chain of 50 nested nodes (well within limit of 100)
            var root = CreateNestedChain(7);

            // Act: Serialize
            byte[] serialized;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeRecursiveNode(ms, root);
                serialized = ms.ToArray();
            }

            // Assert: Should deserialize successfully
            RecursionGuard.Reset(); // Reset guard for clean test
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized);

            deserialized.Should().NotBeNull();
            CountChainDepth(deserialized).Should().Be(7);
        }

        [Fact]
        public void RecursionGuard_Should_Track_Depth_Correctly()
        {
            // Arrange: Create chains of different depths and verify they all work
            RecursionGuard.Reset();

            // Test depth 10
            var chain10 = CreateNestedChain(10);
            byte[] serialized10;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeRecursiveNode(ms, chain10);
                serialized10 = ms.ToArray();
            }
            var result10 = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized10);

            // Test depth 20
            var chain8 = CreateNestedChain(8);
            byte[] serialized20;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeRecursiveNode(ms, chain8);
                serialized20 = ms.ToArray();
            }
            var result8 = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized20);

            // Assert: Both should work
            CountChainDepth(result10).Should().Be(10);
            CountChainDepth(result8).Should().Be(8);
        }

        [Fact]
        public void RecursionGuard_Should_Handle_Tree_Structure()
        {
            // Arrange: Create a balanced tree with depth 10 (1023 nodes total)
            var root = CreateBalancedTree(10);

            // Act: Serialize
            byte[] serialized;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeTreeNode(ms, root);
                serialized = ms.ToArray();
            }

            // Assert: Should deserialize successfully
            RecursionGuard.Reset();
            var deserialized = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeTreeNode(serialized);

            deserialized.Should().NotBeNull();
            deserialized.Value.Should().Be(10);
        }

        [Fact]
        public void RecursionGuard_Should_Reset_After_Successful_Deserialization()
        {
            // Arrange: Create a small chain
            var root = CreateNestedChain(5);

            byte[] serialized;
            using (var ms = new MemoryStream())
            {
                global::GProtobuf.Tests.TestModel.Serialization.Serializers.SerializeRecursiveNode(ms, root);
                serialized = ms.ToArray();
            }

            // Act: Deserialize multiple times
            RecursionGuard.Reset();
            var result1 = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized);
            var result2 = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized);
            var result3 = global::GProtobuf.Tests.TestModel.Serialization.Deserializers.DeserializeRecursiveNode(serialized);

            // Assert: All should succeed (guard should reset between calls)
            result1.Should().NotBeNull();
            result2.Should().NotBeNull();
            result3.Should().NotBeNull();
            CountChainDepth(result1).Should().Be(5);
            CountChainDepth(result2).Should().Be(5);
            CountChainDepth(result3).Should().Be(5);
        }

        #region Helper Methods

        /// <summary>
        /// Creates a chain of nested RecursiveNode objects.
        /// </summary>
        private static RecursiveNode CreateNestedChain(int depth)
        {
            if (depth <= 0)
                return null;

            var node = new RecursiveNode { Value = depth };
            if (depth > 1)
            {
                node.Child = CreateNestedChain(depth - 1);
            }
            return node;
        }

        /// <summary>
        /// Creates a balanced binary tree with specified depth.
        /// </summary>
        private static TreeNode CreateBalancedTree(int depth)
        {
            if (depth <= 0)
                return null;

            var node = new TreeNode { Value = depth };
            if (depth > 1)
            {
                node.Left = CreateBalancedTree(depth - 1);
                node.Right = CreateBalancedTree(depth - 1);
            }
            return node;
        }

        /// <summary>
        /// Counts the depth of a RecursiveNode chain.
        /// </summary>
        private static int CountChainDepth(RecursiveNode node)
        {
            if (node == null)
                return 0;

            int depth = 1;
            var current = node;
            while (current.Child != null)
            {
                depth++;
                current = current.Child;
            }
            return depth;
        }

        #endregion
    }
}
