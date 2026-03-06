using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2.Helpers
{
    /// <summary>
    /// Represents a node in the binary dispatch tree.
    /// Can be either an internal node (with pivot and children) or a leaf node (with case data).
    /// </summary>
    public class BinaryDispatchNode
    {
        /// <summary>
        /// For internal nodes: the pivot field ID for comparison.
        /// All cases with fieldId &lt;= PivotFieldId go to LeftChild.
        /// </summary>
        public int PivotFieldId { get; set; }

        /// <summary>
        /// Left subtree - contains cases where fieldId &lt;= PivotFieldId.
        /// </summary>
        public BinaryDispatchNode LeftChild { get; set; }

        /// <summary>
        /// Right subtree - contains cases where fieldId &gt; PivotFieldId.
        /// </summary>
        public BinaryDispatchNode RightChild { get; set; }

        /// <summary>
        /// For leaf nodes: the single case at this leaf.
        /// Null for internal nodes.
        /// </summary>
        public DispatchCase LeafCase { get; set; }

        /// <summary>
        /// True if this is a leaf node (single case).
        /// </summary>
        public bool IsLeaf => LeafCase != null;

        /// <summary>
        /// Depth of this subtree (for debugging/optimization analysis).
        /// </summary>
        public int Depth { get; set; }
    }

    /// <summary>
    /// Represents a single case in the dispatch (field ID -> type name mapping).
    /// </summary>
    public class DispatchCase
    {
        public int FieldId { get; set; }
        public string TypeName { get; set; }

        public DispatchCase(int fieldId, string typeName)
        {
            FieldId = fieldId;
            TypeName = typeName;
        }
    }

    /// <summary>
    /// Analyzes ProtoInclude cases and builds optimal binary dispatch trees.
    /// </summary>
    public static class BinaryDispatchAnalyzer
    {
        /// <summary>
        /// Minimum number of cases required to use binary dispatch.
        /// Below this threshold, a simple switch is more efficient.
        /// </summary>
        public const int MinCasesForBinaryDispatch = 8;

        /// <summary>
        /// Determines if binary dispatch should be used based on case count.
        /// </summary>
        /// <param name="caseCount">Number of switch cases.</param>
        /// <returns>True if binary dispatch is recommended.</returns>
        public static bool ShouldUseBinaryDispatch(int caseCount)
        {
            return caseCount >= MinCasesForBinaryDispatch;
        }

        /// <summary>
        /// Builds a balanced binary search tree from a list of dispatch cases.
        /// The tree is optimized for O(log n) comparisons.
        /// </summary>
        /// <param name="cases">List of (FieldId, TypeName) pairs. Will be sorted internally.</param>
        /// <returns>Root node of the binary dispatch tree.</returns>
        public static BinaryDispatchNode BuildTree(IEnumerable<DispatchCase> cases)
        {
            var sortedCases = cases.OrderBy(c => c.FieldId).ToList();

            if (sortedCases.Count == 0)
                return null;

            return BuildTreeRecursive(sortedCases, 0, sortedCases.Count - 1, 0);
        }

        /// <summary>
        /// Builds a balanced binary search tree from ProtoInclude attributes.
        /// </summary>
        /// <param name="protoIncludes">List of ProtoInclude attributes.</param>
        /// <returns>Root node of the binary dispatch tree.</returns>
        public static BinaryDispatchNode BuildTree(IEnumerable<ProtoIncludeAttribute> protoIncludes)
        {
            var cases = protoIncludes
                .Select(pi => new DispatchCase(pi.FieldId, pi.Type))
                .ToList();

            return BuildTree(cases);
        }

        /// <summary>
        /// Recursively builds a balanced binary tree using median partitioning.
        /// </summary>
        private static BinaryDispatchNode BuildTreeRecursive(
            IReadOnlyList<DispatchCase> cases,
            int start,
            int end,
            int depth)
        {
            if (start > end)
                return null;

            // Single element - create leaf node
            if (start == end)
            {
                return new BinaryDispatchNode
                {
                    LeafCase = cases[start],
                    Depth = depth
                };
            }

            // Two elements - special case for efficiency
            if (end - start == 1)
            {
                return new BinaryDispatchNode
                {
                    PivotFieldId = cases[start].FieldId,
                    LeftChild = new BinaryDispatchNode
                    {
                        LeafCase = cases[start],
                        Depth = depth + 1
                    },
                    RightChild = new BinaryDispatchNode
                    {
                        LeafCase = cases[end],
                        Depth = depth + 1
                    },
                    Depth = depth
                };
            }

            // Find median index for balanced tree
            int mid = (start + end) / 2;

            // Create internal node with median as pivot
            return new BinaryDispatchNode
            {
                PivotFieldId = cases[mid].FieldId,
                LeftChild = BuildTreeRecursive(cases, start, mid, depth + 1),
                RightChild = BuildTreeRecursive(cases, mid + 1, end, depth + 1),
                Depth = depth
            };
        }

        /// <summary>
        /// Calculates the maximum depth of a binary dispatch tree.
        /// Useful for performance analysis.
        /// </summary>
        public static int GetMaxDepth(BinaryDispatchNode root)
        {
            if (root == null)
                return 0;

            if (root.IsLeaf)
                return 1;

            int leftDepth = GetMaxDepth(root.LeftChild);
            int rightDepth = GetMaxDepth(root.RightChild);

            return 1 + (leftDepth > rightDepth ? leftDepth : rightDepth);
        }

        /// <summary>
        /// Counts the total number of cases in a tree.
        /// </summary>
        public static int CountCases(BinaryDispatchNode root)
        {
            if (root == null)
                return 0;

            if (root.IsLeaf)
                return 1;

            return CountCases(root.LeftChild) + CountCases(root.RightChild);
        }

        /// <summary>
        /// Validates that a tree correctly contains all expected field IDs.
        /// </summary>
        public static bool ValidateTree(BinaryDispatchNode root, IEnumerable<int> expectedFieldIds)
        {
            var expected = new HashSet<int>(expectedFieldIds);
            var actual = new HashSet<int>();
            CollectFieldIds(root, actual);
            return expected.SetEquals(actual);
        }

        private static void CollectFieldIds(BinaryDispatchNode node, HashSet<int> fieldIds)
        {
            if (node == null)
                return;

            if (node.IsLeaf)
            {
                fieldIds.Add(node.LeafCase.FieldId);
                return;
            }

            CollectFieldIds(node.LeftChild, fieldIds);
            CollectFieldIds(node.RightChild, fieldIds);
        }

        /// <summary>
        /// Gets the minimum field ID in the tree.
        /// Useful for range-based dispatch optimization.
        /// </summary>
        public static int GetMinFieldId(BinaryDispatchNode root)
        {
            if (root == null)
                return int.MaxValue;

            if (root.IsLeaf)
                return root.LeafCase.FieldId;

            // Left child always has smaller field IDs in a balanced tree
            return GetMinFieldId(root.LeftChild);
        }

        /// <summary>
        /// Gets the maximum field ID in the tree.
        /// </summary>
        public static int GetMaxFieldId(BinaryDispatchNode root)
        {
            if (root == null)
                return int.MinValue;

            if (root.IsLeaf)
                return root.LeafCase.FieldId;

            // Right child always has larger field IDs in a balanced tree
            return GetMaxFieldId(root.RightChild);
        }
    }
}
