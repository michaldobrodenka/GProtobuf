using GProtobuf.Generator.V2.Handlers.Core;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Generates code for collections of complex types (List&lt;CustomMessage&gt;).
    /// Reuses existing Write{ClassName}Content and Calculate{ClassName}ContentSize methods.
    /// </summary>
    internal class CollectionHandler
    {
        private readonly StringBuilderWithIndent _sb;

        public CollectionHandler(StringBuilderWithIndent sb)
        {
            _sb = sb;
        }

        #region Write (Serialization)

        /// <summary>
        /// Generates write code for List&lt;ComplexType&gt;.
        /// For each item: write tag, calculate length, write length, write content.
        /// </summary>
        public void GenerateComplexCollectionWrite(
            int fieldId,
            string sourceVar,
            string elementClassName)
        {
            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            // Write tag
            TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);

            // Calculate and write length
            _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{elementClassName}ContentSize(ref itemCalc, item);");
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");

            // Write content
            _sb.AppendIndentedLine($"Write{elementClassName}Content(ref writer, item);");

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
        }

        #endregion

        #region Read (Deserialization)

        /// <summary>
        /// Generates read code for List&lt;ComplexType&gt;.
        /// Lazy-initializes collection, reads length-prefixed item, adds to collection.
        /// </summary>
        public void GenerateComplexCollectionRead(
            string targetVar,
            string elementTypeName,
            string elementClassName,
            CollectionKind collectionKind,
            string collectionTypeName)
        {
            var shortElementType = TypeMapping.GetShortTypeName(elementTypeName);

            // Lazy init collection
            _sb.AppendIndentedLine($"if ({targetVar} == null)");
            _sb.StartNewBlock();

            var initExpr = GenerateCollectionInitialization(shortElementType, collectionKind, collectionTypeName);
            _sb.AppendIndentedLine($"{targetVar} = {initExpr};");

            _sb.EndBlock();

            // Read length-prefixed item
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"var item = Read{elementClassName}Content(ref nestedReader);");

            // Add to collection
            _sb.AppendIndentedLine($"{targetVar}.Add(item);");
        }

        private string GenerateCollectionInitialization(string elementType, CollectionKind kind, string collectionTypeName)
        {
            switch (kind)
            {
                case CollectionKind.Array:
                    // Arrays can't be used with Add(), so use List
                    return $"new global::System.Collections.Generic.List<{elementType}>()";

                case CollectionKind.InterfaceCollection:
                case CollectionKind.ConcreteCollection:
                    // Check for specific types
                    if (collectionTypeName != null)
                    {
                        if (collectionTypeName.Contains("HashSet<"))
                            return $"new global::System.Collections.Generic.HashSet<{elementType}>()";
                        if (collectionTypeName.Contains("List<"))
                            return $"new global::System.Collections.Generic.List<{elementType}>()";
                    }
                    // Default to List
                    return $"new global::System.Collections.Generic.List<{elementType}>()";

                default:
                    return $"new global::System.Collections.Generic.List<{elementType}>()";
            }
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates size calculation for List&lt;ComplexType&gt;.
        /// For each item: add tag size, calculate content size, add length varint + content.
        /// </summary>
        public void GenerateComplexCollectionSize(
            int fieldId,
            string sourceVar,
            string elementClassName,
            string calculatorVar = "calculator")
        {
            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            // Add tag size
            TagCodeHelper.AddTagSize(_sb, fieldId, WireType.Len, calculatorVar);

            // Calculate item content size
            _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{elementClassName}ContentSize(ref itemCalc, item);");

            // Add length varint size + content size
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)itemCalc.Length);");
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(itemCalc.Length);");

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
        }

        #endregion
    }
}
