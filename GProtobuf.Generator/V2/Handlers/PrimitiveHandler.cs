using System;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Handles code generation for primitive types (int, long, float, string, etc.)
    /// Generates deserialization, serialization, and size calculation code.
    /// </summary>
    internal class PrimitiveHandler
    {
        #region Type Classification

        /// <summary>
        /// Checks if this handler can handle the given type (single value, not collection).
        /// </summary>
        public bool CanHandle(string typeName)
        {
            return TypeMapping.IsSimpleType(typeName);
        }

        /// <summary>
        /// Checks if this handler can handle primitive collections (packed/non-packed arrays).
        /// </summary>
        public bool CanHandleCollection(string elementTypeName)
        {
            return TypeMapping.IsPrimitiveArrayType(elementTypeName);
        }

        #endregion

        #region Deserialization

        /// <summary>
        /// Generates read code for a single primitive value.
        /// </summary>
        public void GenerateRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string typeName,
            DataFormat format,
            string readerVar = "reader",
            string wireTypeVar = "wireType")
        {
            var readExpr = TypeMapping.GetReadExpression(typeName, format, readerVar, wireTypeVar);
            if (readExpr != null)
            {
                sb.AppendIndentedLine($"{targetVar} = {readExpr};");
            }
        }

        /// <summary>
        /// Generates read code for packed primitive array.
        /// Returns the array directly.
        /// </summary>
        public void GeneratePackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            string readerVar = "reader")
        {
            GeneratePackedArrayRead(sb, targetVar, elementTypeName, format, CollectionKind.Array, null, readerVar);
        }

        /// <summary>
        /// Generates read code for packed primitive array with collection kind support.
        /// </summary>
        public void GeneratePackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader")
        {
            var readExpr = TypeMapping.GetPackedArrayReadExpression(elementTypeName, format, readerVar);
            if (readExpr != null)
            {
                var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, readExpr);
                sb.AppendIndentedLine(assignment);
            }
        }

        /// <summary>
        /// Generates read code for non-packed repeated primitive field.
        /// Uses UnmanagedCollectionCollector for efficiency.
        /// </summary>
        public void GenerateNonPackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string readerVar = "reader")
        {
            GenerateNonPackedArrayRead(sb, targetVar, elementTypeName, format, fieldId, CollectionKind.Array, null, readerVar);
        }

        /// <summary>
        /// Generates read code for non-packed repeated primitive field with collection kind support.
        /// </summary>
        public void GenerateNonPackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader")
        {
            var shortType = TypeMapping.GetShortTypeName(elementTypeName);
            var elementReadExpr = TypeMapping.GetElementReadExpression(elementTypeName, format, readerVar);
            var expectedWireType = TypeMapping.GetWireTypeString(elementTypeName, format);

            sb.AppendIndentedLine($"using var resultCollector = new global::GProtobuf.Core.UnmanagedCollectionCollector<{shortType}>(stackalloc {shortType}[256 / sizeof({shortType})], 1024);");
            sb.AppendIndentedLine($"var wireType1 = wireType;");
            sb.AppendIndentedLine($"var fieldId1 = fieldId;");
            sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == {expectedWireType})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"resultCollector.Add({elementReadExpr});");
            sb.AppendIndentedLine($"if ({readerVar}.EndOfData) break;");
            sb.AppendIndentedLine($"var p = {readerVar}.Position;");
            sb.AppendIndentedLine($"(wireType1, fieldId1) = {readerVar}.ReadKey();");
            sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{readerVar}.Position = p; // rewind");
            sb.AppendIndentedLine($"break;");
            sb.EndBlock();
            sb.EndBlock();

            // Generate assignment based on collection kind
            var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, "resultCollector.ToArray()");
            sb.AppendIndentedLine(assignment);
        }

        /// <summary>
        /// Generates the correct collection assignment based on CollectionKind.
        /// </summary>
        private string GenerateCollectionAssignment(
            string targetVar,
            string elementTypeName,
            CollectionKind collectionKind,
            string collectionTypeName,
            string arrayExpr)
        {
            var shortElementType = TypeMapping.GetShortTypeName(elementTypeName);

            switch (collectionKind)
            {
                case CollectionKind.Array:
                    return $"{targetVar} = {arrayExpr};";

                case CollectionKind.InterfaceCollection:
                case CollectionKind.ConcreteCollection:
                    // Check for specific collection types
                    if (collectionTypeName != null)
                    {
                        if (collectionTypeName.Contains("HashSet<") || collectionTypeName.Contains("System.Collections.Generic.HashSet<"))
                        {
                            return $"{targetVar} = new global::System.Collections.Generic.HashSet<{shortElementType}>({arrayExpr});";
                        }
                        else if (collectionTypeName.Contains("List<") || collectionTypeName.Contains("System.Collections.Generic.List<"))
                        {
                            return $"{targetVar} = new global::System.Collections.Generic.List<{shortElementType}>({arrayExpr});";
                        }
                    }
                    // Default to List for interface collections
                    return $"{targetVar} = new global::System.Collections.Generic.List<{shortElementType}>({arrayExpr});";

                default:
                    return $"{targetVar} = {arrayExpr};";
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Generates write code for a single primitive value with tag.
        /// Includes default value check for non-nullable types.
        /// </summary>
        public void GenerateWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string typeName,
            DataFormat format,
            int fieldId,
            bool isNullable,
            string writerVar = "writer")
        {
            var wireType = TypeMapping.GetWireType(typeName, format);

            // Check if type is supported
            if (!TypeMapping.IsSimpleType(typeName)) return;

            // Generate condition
            if (isNullable)
            {
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
            }
            else
            {
                var defaultCheck = TypeMapping.GetDefaultValueCheck(typeName, sourceVar);
                if (defaultCheck != null)
                {
                    sb.AppendIndentedLine($"if ({defaultCheck})");
                }
            }

            sb.StartNewBlock();

            // Write tag
            GenerateWriteTag(sb, fieldId, wireType, writerVar);

            // Write value
            var valueExpr = isNullable ? $"{sourceVar}.Value" : sourceVar;
            var actualWriteExpr = TypeMapping.GetWriteExpression(typeName, valueExpr, format, writerVar);
            sb.AppendIndentedLine($"{actualWriteExpr};");

            sb.EndBlock();
        }

        /// <summary>
        /// Generates write code for packed primitive array.
        /// </summary>
        public void GeneratePackedArrayWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string writerVar = "writer")
        {
            sb.AppendIndentedLine($"if ({sourceVar} != null)");
            sb.StartNewBlock();

            // Write Len tag
            GenerateWriteTag(sb, fieldId, WireType.Len, writerVar);

            // Calculate packed size first
            sb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, "calculator");
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementSizeExpr};");
            sb.EndBlock();

            // Write length
            sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint)calculator.Length);");

            // Write elements
            var elementWriteExpr = TypeMapping.GetElementWriteExpression(elementTypeName, "item", format, writerVar);
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementWriteExpr};");
            sb.EndBlock();

            sb.EndBlock();
        }

        /// <summary>
        /// Generates write code for non-packed repeated primitive field.
        /// </summary>
        public void GenerateNonPackedArrayWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string writerVar = "writer")
        {
            var wireType = TypeMapping.GetWireType(elementTypeName, format);

            sb.AppendIndentedLine($"if ({sourceVar} != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();

            GenerateWriteTag(sb, fieldId, wireType, writerVar);
            var elementWriteExpr = TypeMapping.GetElementWriteExpression(elementTypeName, "item", format, writerVar);
            sb.AppendIndentedLine($"{elementWriteExpr};");

            sb.EndBlock();
            sb.EndBlock();
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates size calculation for a single primitive value.
        /// </summary>
        public void GenerateSize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string typeName,
            DataFormat format,
            int fieldId,
            bool isNullable,
            string calculatorVar = "calculator")
        {
            var wireType = TypeMapping.GetWireType(typeName, format);

            // Generate condition
            if (isNullable)
            {
                sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
            }
            else
            {
                var defaultCheck = TypeMapping.GetDefaultValueCheck(typeName, sourceVar);
                if (defaultCheck != null)
                {
                    sb.AppendIndentedLine($"if ({defaultCheck})");
                }
            }

            sb.StartNewBlock();

            // Add tag size
            GenerateSizeTag(sb, fieldId, wireType, calculatorVar);

            // Add value size
            var valueExpr = isNullable ? $"{sourceVar}.Value" : sourceVar;
            var sizeExpr = TypeMapping.GetSizeExpression(typeName, valueExpr, format, calculatorVar);
            sb.AppendIndentedLine($"{sizeExpr};");

            sb.EndBlock();
        }

        /// <summary>
        /// Generates size calculation for packed primitive array.
        /// </summary>
        public void GeneratePackedArraySize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string calculatorVar = "calculator")
        {
            sb.AppendIndentedLine($"if ({sourceVar} != null)");
            sb.StartNewBlock();

            // Add Len tag size
            GenerateSizeTag(sb, fieldId, WireType.Len, calculatorVar);

            // Calculate packed content size
            sb.AppendIndentedLine($"var tempCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, "tempCalculator");
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementSizeExpr};");
            sb.EndBlock();

            // Add length varint + content size
            sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)tempCalculator.Length);");
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(tempCalculator.Length);");

            sb.EndBlock();
        }

        /// <summary>
        /// Generates size calculation for non-packed repeated primitive field.
        /// </summary>
        public void GenerateNonPackedArraySize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string calculatorVar = "calculator")
        {
            var wireType = TypeMapping.GetWireType(elementTypeName, format);

            sb.AppendIndentedLine($"if ({sourceVar} != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();

            GenerateSizeTag(sb, fieldId, wireType, calculatorVar);
            var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, calculatorVar);
            sb.AppendIndentedLine($"{elementSizeExpr};");

            sb.EndBlock();
            sb.EndBlock();
        }

        #endregion

        #region Tag Generation

        /// <summary>
        /// Generates code to write a precomputed tag.
        /// </summary>
        private void GenerateWriteTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string writerVar)
        {
            var (bytesString, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            if (byteCount == 1)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteSingleByte({bytesString});");
            }
            else
            {
                // Use static ReadOnlySpan from Tags class for zero-allocation
                var tagPropertyName = CodeGeneration.TagsGenerator.GetTagPropertyName(fieldId, wireType);
                sb.AppendIndentedLine($"{writerVar}.WriteBytes(Tags.{tagPropertyName});");
            }
        }

        /// <summary>
        /// Generates code to add tag size to calculator.
        /// </summary>
        private void GenerateSizeTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string calculatorVar)
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({byteCount});");
        }

        #endregion
    }
}
