using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
{
    /// <summary>
    /// Generates Read/Write/Size methods for virtual Tuple types.
    /// Similar to VirtualMapEntryGenerator but for Tuples.
    /// </summary>
    internal class VirtualTupleGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly string _writerKind; // "Stream" or "Buffer"

        public VirtualTupleGenerator(StringBuilderWithIndent sb, string writerKind = null)
        {
            _sb = sb;
            _writerKind = writerKind;
        }

        #region Reader

        /// <summary>
        /// Generates Read{TupleName}Content method.
        /// Example: ReadTupleOfIntAndStringContent(ref SpanReader reader)
        /// </summary>
        public void GenerateReader(TupleTypeInfo tupleInfo)
        {
            _sb.AppendIndentedLine($"public static {tupleInfo.OriginalTypeName} Read{tupleInfo.SafeName}Content(ref SpanReader reader)");
            _sb.StartNewBlock();

            // Declare variables for all items
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                var itemType = tupleInfo.ItemTypes[i];
                _sb.AppendIndentedLine($"{itemType} item{i + 1} = default({itemType});");
            }

            _sb.AppendNewLine();

            // Read fields in a loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            // Generate case for each item
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                _sb.AppendIndentedLine($"case {i + 1}:");
                _sb.IncreaseIndent();
                GenerateItemRead($"item{i + 1}", tupleInfo.ItemTypes[i]);
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();
            }

            // Default case: skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            // Construct and return the tuple
            _sb.AppendNewLine();
            var items = string.Join(", ", Enumerable.Range(1, tupleInfo.Arity).Select(i => $"item{i}"));
            _sb.AppendIndentedLine($"return new {tupleInfo.OriginalTypeName}({items});");

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateItemRead(string targetVar, string itemType)
        {
            // Try primitive/string/byte[]/Guid/enum first
            var readExpr = TypeMapping.GetReadExpression(itemType, DataFormat.Default, "reader", "wireType");

            if (readExpr != null)
            {
                // Primitive type, string, byte[], Guid, or enum - TypeMapping handles all
                _sb.AppendIndentedLine($"{targetVar} = {readExpr};");
            }
            else
            {
                // Complex type - read as length-prefixed message
                var className = TypeNameHelper.GetSafeMethodName(itemType);
                _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var itemReader = new SpanReader(reader.GetSlice(itemLength));");
                _sb.AppendIndentedLine($"{targetVar} = SpanReaders.Read{className}Content(ref itemReader);");
            }
        }

        #endregion

        #region Writer

        /// <summary>
        /// Generates Write{TupleName}Content method.
        /// Example: WriteTupleOfIntAndStringContent(ref StreamWriter writer, Tuple&lt;int, string&gt; instance)
        /// </summary>
        public void GenerateWriter(TupleTypeInfo tupleInfo)
        {
            var writerType = _writerKind != null ? $"global::GProtobuf.Core.{_writerKind}Writer" : "global::GProtobuf.Core.StreamWriter";

            _sb.AppendIndentedLine($"public static void Write{tupleInfo.SafeName}Content(ref {writerType} writer, {tupleInfo.OriginalTypeName} instance)");
            _sb.StartNewBlock();

            // Write each item
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                var itemType = tupleInfo.ItemTypes[i];
                var fieldId = i + 1;
                var sourceVar = $"instance.Item{fieldId}";

                GenerateItemWrite(sourceVar, itemType, fieldId);
            }

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateItemWrite(string sourceVar, string itemType, int fieldId)
        {
            // Write tag
            TagCodeHelper.WriteTag(_sb, fieldId, GetWireType(itemType));

            // Write value
            var writeExpr = TypeMapping.GetWriteExpression(itemType, sourceVar, DataFormat.Default, "writer");

            if (writeExpr != null)
            {
                // Primitive type, string, byte[], Guid, or enum - TypeMapping handles all
                _sb.AppendIndentedLine($"{writeExpr};");
            }
            else
            {
                // Complex type - write as length-prefixed message
                var className = TypeNameHelper.GetSafeMethodName(itemType);
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref itemCalc, {sourceVar});");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");

                var writerClass = _writerKind != null ? $"{_writerKind}Writers" : "StreamWriters";
                _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref writer, {sourceVar});");
            }
        }

        #endregion

        #region SizeCalculator

        /// <summary>
        /// Generates Calculate{TupleName}ContentSize method.
        /// Example: CalculateTupleOfIntAndStringContentSize(ref WriteSizeCalculator calculator, Tuple&lt;int, string&gt; instance)
        /// </summary>
        public void GenerateSizeCalculator(TupleTypeInfo tupleInfo)
        {
            _sb.AppendIndentedLine($"public static void Calculate{tupleInfo.SafeName}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {tupleInfo.OriginalTypeName} instance)");
            _sb.StartNewBlock();

            // Calculate size for each item
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                var itemType = tupleInfo.ItemTypes[i];
                var fieldId = i + 1;
                var sourceVar = $"instance.Item{fieldId}";

                GenerateItemSize(sourceVar, itemType, fieldId);
            }

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateItemSize(string sourceVar, string itemType, int fieldId)
        {
            // Add tag size
            TagCodeHelper.AddTagSize(_sb, fieldId, GetWireType(itemType), "calculator");

            // Add value size
            var sizeExpr = TypeMapping.GetSizeExpression(itemType, sourceVar, DataFormat.Default, "calculator");

            if (sizeExpr != null)
            {
                // Primitive type, string, byte[], Guid, or enum - TypeMapping handles all
                _sb.AppendIndentedLine($"{sizeExpr};");
            }
            else
            {
                // Complex type
                var className = TypeNameHelper.GetSafeMethodName(itemType);
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref itemCalc, {sourceVar});");
                _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine("calculator.AddByteLength(itemCalc.Length);");
            }
        }

        #endregion

        #region Helpers

        private WireType GetWireType(string typeName)
        {
            if (TypeHelper.IsPrimitiveType(typeName))
            {
                var normalized = TypeMapping.NormalizeTypeName(typeName);
                return TypeMapping.GetWireType(normalized);
            }

            // String, Guid, enums, and complex types use Len
            return WireType.Len;
        }

        #endregion
    }
}
