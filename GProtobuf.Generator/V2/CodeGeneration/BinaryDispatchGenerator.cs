using System;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates binary search if-else dispatch code for polymorphic type reading.
    /// This optimization reduces the number of comparisons from O(n) to O(log n).
    /// </summary>
    internal class BinaryDispatchGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly string _fieldIdVar;

        /// <summary>
        /// Creates a new binary dispatch generator.
        /// </summary>
        /// <param name="sb">StringBuilder for code output.</param>
        /// <param name="fieldIdVar">Name of the variable containing the field ID (e.g., "fieldId").</param>
        public BinaryDispatchGenerator(StringBuilderWithIndent sb, string fieldIdVar = "fieldId")
        {
            _sb = sb ?? throw new ArgumentNullException(nameof(sb));
            _fieldIdVar = fieldIdVar;
        }

        /// <summary>
        /// Generates the complete binary dispatch code for a tree.
        /// </summary>
        /// <param name="root">Root of the binary dispatch tree.</param>
        /// <param name="generateCaseBody">Action to generate code for each case.
        /// Parameters are (fieldId, typeName). Should generate the case body without break/continue.</param>
        /// <param name="generateDefault">Action to generate the default case (unknown field handling).</param>
        public void Generate(
            BinaryDispatchNode root,
            Action<int, string> generateCaseBody,
            Action generateDefault = null)
        {
            if (root == null)
            {
                generateDefault?.Invoke();
                return;
            }

            int caseCount = BinaryDispatchAnalyzer.CountCases(root);
            int maxDepth = BinaryDispatchAnalyzer.GetMaxDepth(root);

            _sb.AppendIndentedLine($"// Binary search dispatch: {caseCount} cases, max {maxDepth} comparisons (O(log n))");

            GenerateNode(root, generateCaseBody, generateDefault, isRoot: true);
        }

        /// <summary>
        /// Generates code for a single node and its subtrees.
        /// </summary>
        private void GenerateNode(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateDefault,
            bool isRoot)
        {
            if (node == null)
            {
                generateDefault?.Invoke();
                return;
            }

            if (node.IsLeaf)
            {
                // Leaf node - generate exact match check and case body
                GenerateLeafNode(node, generateCaseBody, generateDefault, isRoot);
                return;
            }

            // Internal node - generate binary branch
            GenerateInternalNode(node, generateCaseBody, generateDefault);
        }

        /// <summary>
        /// Generates code for a leaf node (single case).
        /// </summary>
        private void GenerateLeafNode(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateDefault,
            bool isRoot)
        {
            var leafCase = node.LeafCase;

            if (isRoot)
            {
                // Root leaf - need explicit check
                _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leafCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(leafCase.FieldId, leafCase.TypeName);
                _sb.EndBlock();

                if (generateDefault != null)
                {
                    _sb.AppendIndentedLine("else");
                    _sb.StartNewBlock();
                    generateDefault();
                    _sb.EndBlock();
                }
            }
            else
            {
                // Non-root leaf - already narrowed down by parent comparisons
                // Still need exact check since we may have gaps in field IDs
                _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leafCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(leafCase.FieldId, leafCase.TypeName);
                _sb.EndBlock();

                if (generateDefault != null)
                {
                    _sb.AppendIndentedLine("else");
                    _sb.StartNewBlock();
                    generateDefault();
                    _sb.EndBlock();
                }
            }
        }

        /// <summary>
        /// Generates code for an internal node (binary branch).
        /// </summary>
        private void GenerateInternalNode(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateDefault)
        {
            // Check if both children are leaves - can optimize
            bool leftIsLeaf = node.LeftChild?.IsLeaf ?? true;
            bool rightIsLeaf = node.RightChild?.IsLeaf ?? true;

            if (leftIsLeaf && rightIsLeaf && node.LeftChild != null && node.RightChild != null)
            {
                // Both children are leaves - generate optimized two-way check
                GenerateTwoLeafBranch(node, generateCaseBody, generateDefault);
                return;
            }

            // Standard binary branch
            _sb.AppendIndentedLine($"if ({_fieldIdVar} <= {node.PivotFieldId})");
            _sb.StartNewBlock();
            GenerateNode(node.LeftChild, generateCaseBody, generateDefault, isRoot: false);
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            GenerateNode(node.RightChild, generateCaseBody, generateDefault, isRoot: false);
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates optimized code when both children are leaf nodes.
        /// </summary>
        private void GenerateTwoLeafBranch(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateDefault)
        {
            var leftCase = node.LeftChild.LeafCase;
            var rightCase = node.RightChild.LeafCase;

            _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leftCase.FieldId})");
            _sb.StartNewBlock();
            generateCaseBody(leftCase.FieldId, leftCase.TypeName);
            _sb.EndBlock();
            _sb.AppendIndentedLine($"else if ({_fieldIdVar} == {rightCase.FieldId})");
            _sb.StartNewBlock();
            generateCaseBody(rightCase.FieldId, rightCase.TypeName);
            _sb.EndBlock();

            if (generateDefault != null)
            {
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                generateDefault();
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates binary dispatch with mixed ProtoInclude cases and regular field cases.
        /// ProtoIncludes are handled with binary dispatch, regular fields use a switch.
        /// NOTE: This is the LEGACY method that duplicates switches. Use GenerateRangeSeparatedDispatch instead.
        /// </summary>
        /// <param name="protoIncludeTree">Binary tree for ProtoInclude cases.</param>
        /// <param name="generateProtoIncludeCase">Generates code for a ProtoInclude case.</param>
        /// <param name="generateRegularFieldsSwitch">Generates the switch for regular fields.</param>
        /// <param name="generateDefault">Generates the default case.</param>
        public void GenerateMixedDispatch(
            BinaryDispatchNode protoIncludeTree,
            Action<int, string> generateProtoIncludeCase,
            Action generateRegularFieldsSwitch,
            Action generateDefault)
        {
            if (protoIncludeTree == null)
            {
                // No ProtoIncludes - just generate regular fields switch
                generateRegularFieldsSwitch?.Invoke();
                return;
            }

            // First check ProtoIncludes with binary dispatch
            // Then fall through to regular fields switch
            _sb.AppendIndentedLine("// ProtoInclude binary dispatch");
            GenerateMixedNode(protoIncludeTree, generateProtoIncludeCase, () =>
            {
                // Not a ProtoInclude - check regular fields
                generateRegularFieldsSwitch?.Invoke();
            });
        }

        /// <summary>
        /// Generates OPTIMIZED range-separated dispatch for mixed ProtoInclude and ProtoMember cases.
        /// This method separates field ID ranges to avoid duplicating the regular fields switch.
        ///
        /// Generated code structure:
        /// <code>
        /// if (fieldId >= minProtoIncludeFieldId)
        /// {
        ///     // Binary dispatch for ProtoIncludes - SkipField for unknown
        /// }
        /// else
        /// {
        ///     // Regular fields switch - appears ONCE
        /// }
        /// </code>
        /// </summary>
        /// <param name="protoIncludeTree">Binary tree for ProtoInclude cases.</param>
        /// <param name="generateProtoIncludeCase">Generates code for a ProtoInclude case.</param>
        /// <param name="generateRegularFieldsSwitch">Generates the switch for regular fields (called ONCE).</param>
        /// <param name="generateSkipField">Generates code to skip unknown fields.</param>
        public void GenerateRangeSeparatedDispatch(
            BinaryDispatchNode protoIncludeTree,
            Action<int, string> generateProtoIncludeCase,
            Action generateRegularFieldsSwitch,
            Action generateSkipField)
        {
            if (protoIncludeTree == null)
            {
                // No ProtoIncludes - just generate regular fields switch
                generateRegularFieldsSwitch?.Invoke();
                return;
            }

            int minProtoIncludeFieldId = BinaryDispatchAnalyzer.GetMinFieldId(protoIncludeTree);
            int maxProtoIncludeFieldId = BinaryDispatchAnalyzer.GetMaxFieldId(protoIncludeTree);
            int caseCount = BinaryDispatchAnalyzer.CountCases(protoIncludeTree);
            int maxDepth = BinaryDispatchAnalyzer.GetMaxDepth(protoIncludeTree);

            _sb.AppendIndentedLine($"// Range-separated dispatch: ProtoIncludes [{minProtoIncludeFieldId}-{maxProtoIncludeFieldId}], {caseCount} cases, max {maxDepth} comparisons");

            // Check if fieldId is in ProtoInclude range
            _sb.AppendIndentedLine($"if ({_fieldIdVar} >= {minProtoIncludeFieldId})");
            _sb.StartNewBlock();

            // Binary dispatch for ProtoIncludes only - skip unknown in this range
            GenerateProtoIncludeOnlyNode(protoIncludeTree, generateProtoIncludeCase, generateSkipField);

            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();

            // Regular fields switch - appears ONCE here
            generateRegularFieldsSwitch?.Invoke();

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates binary dispatch for ProtoIncludes only (no fallthrough to regular fields).
        /// Unknown field IDs in this range call SkipField.
        /// </summary>
        private void GenerateProtoIncludeOnlyNode(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateSkipField)
        {
            if (node == null)
            {
                generateSkipField?.Invoke();
                return;
            }

            if (node.IsLeaf)
            {
                var leafCase = node.LeafCase;
                _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leafCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(leafCase.FieldId, leafCase.TypeName);
                _sb.EndBlock();
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                generateSkipField();
                _sb.EndBlock();
                return;
            }

            // Check if both children are leaves - can optimize
            bool leftIsLeaf = node.LeftChild?.IsLeaf ?? true;
            bool rightIsLeaf = node.RightChild?.IsLeaf ?? true;

            if (leftIsLeaf && rightIsLeaf && node.LeftChild != null && node.RightChild != null)
            {
                // Both children are leaves - generate optimized two-way check
                var leftCase = node.LeftChild.LeafCase;
                var rightCase = node.RightChild.LeafCase;

                _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leftCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(leftCase.FieldId, leftCase.TypeName);
                _sb.EndBlock();
                _sb.AppendIndentedLine($"else if ({_fieldIdVar} == {rightCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(rightCase.FieldId, rightCase.TypeName);
                _sb.EndBlock();
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                generateSkipField();
                _sb.EndBlock();
                return;
            }

            // Standard binary branch
            _sb.AppendIndentedLine($"if ({_fieldIdVar} <= {node.PivotFieldId})");
            _sb.StartNewBlock();
            GenerateProtoIncludeOnlyNode(node.LeftChild, generateCaseBody, generateSkipField);
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            GenerateProtoIncludeOnlyNode(node.RightChild, generateCaseBody, generateSkipField);
            _sb.EndBlock();
        }

        private void GenerateMixedNode(
            BinaryDispatchNode node,
            Action<int, string> generateCaseBody,
            Action generateFallthrough)
        {
            if (node == null)
            {
                generateFallthrough?.Invoke();
                return;
            }

            if (node.IsLeaf)
            {
                var leafCase = node.LeafCase;
                _sb.AppendIndentedLine($"if ({_fieldIdVar} == {leafCase.FieldId})");
                _sb.StartNewBlock();
                generateCaseBody(leafCase.FieldId, leafCase.TypeName);
                _sb.EndBlock();
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                generateFallthrough();
                _sb.EndBlock();
                return;
            }

            // For mixed dispatch, we need to check exact matches at leaves
            // and fall through to regular fields if no match
            _sb.AppendIndentedLine($"if ({_fieldIdVar} <= {node.PivotFieldId})");
            _sb.StartNewBlock();
            GenerateMixedNode(node.LeftChild, generateCaseBody, generateFallthrough);
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            GenerateMixedNode(node.RightChild, generateCaseBody, generateFallthrough);
            _sb.EndBlock();
        }
    }
}
