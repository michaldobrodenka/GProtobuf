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
        private readonly TypeRegistry _registry;
        private readonly TupleItemTypeHandler _typeHandler;

        public VirtualTupleGenerator(StringBuilderWithIndent sb, string writerKind, TypeRegistry registry)
        {
            _sb = sb;
            _writerKind = writerKind;
            _registry = registry;
            _typeHandler = new TupleItemTypeHandler(_registry);
        }

        #region Reader

        /// <summary>
        /// Generates Read{TupleName}Content method.
        /// Example: ReadTupleOfIntAndStringContent(ref SpanReader reader)
        /// </summary>
        public void GenerateReader(TupleTypeInfo tupleInfo)
        {
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
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
            var typeInfo = _typeHandler.AnalyzeType(itemType);

            // For nullable types, just read the value (no HasValue check needed during deserialization)
            var actualType = typeInfo.UnderlyingType;

            switch (typeInfo.Category)
            {
                case TypeCategory.Primitive:
                    // Primitive type, string, byte[], Guid - TypeMapping handles all
                    var readExpr = TypeMapping.GetReadExpression(actualType, DataFormat.Default, "reader", "wireType");
                    _sb.AppendIndentedLine($"{targetVar} = {readExpr};");
                    break;

                case TypeCategory.Enum:
                    // Enum type - read as VarInt32 and cast
                    _sb.AppendIndentedLine($"{targetVar} = ({actualType})reader.ReadVarInt32();");
                    break;

                case TypeCategory.Complex:
                    // Complex type - read as length-prefixed message
                    var className = TypeNameHelper.GetSafeMethodName(actualType);
                    _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
                    _sb.AppendIndentedLine("var itemReader = new SpanReader(reader.GetSlice(itemLength));");
                    _sb.AppendIndentedLine($"{targetVar} = SpanReaders.Read{className}Content(ref itemReader);");
                    break;
            }
        }

        #endregion

        #region StreamReader

        /// <summary>
        /// Generates Read{TupleName}Content method for StreamReader.
        /// </summary>
        public void GenerateStreamReader(TupleTypeInfo tupleInfo)
        {
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static {tupleInfo.OriginalTypeName} Read{tupleInfo.SafeName}Content(ref global::GProtobuf.Core.StreamReader reader)");
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
                GenerateStreamReaderItemRead($"item{i + 1}", tupleInfo.ItemTypes[i]);
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

        private void GenerateStreamReaderItemRead(string targetVar, string itemType)
        {
            var typeInfo = _typeHandler.AnalyzeType(itemType);
            var actualType = typeInfo.UnderlyingType;
            var normalizedType = TypeMapping.NormalizeTypeName(actualType);

            switch (typeInfo.Category)
            {
                case TypeCategory.Primitive:
                    // Handle primitives with StreamReaders extension methods
                    switch (normalizedType)
                    {
                        case "System.Int32":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadVarInt32();");
                            break;
                        case "System.UInt32":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadVarUInt32();");
                            break;
                        case "System.Int64":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadVarInt64();");
                            break;
                        case "System.UInt64":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadVarUInt64();");
                            break;
                        case "System.Double":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadFixedDouble();");
                            break;
                        case "System.Single":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadFixedFloat();");
                            break;
                        case "System.Boolean":
                            _sb.AppendIndentedLine($"{targetVar} = reader.ReadVarInt32() != 0;");
                            break;
                        case "System.String":
                            _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                            break;
                        case "System.Byte[]":
                            _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                            break;
                        case "System.Guid":
                            _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                            break;
                        case "System.DateTime":
                            _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                            break;
                        case "System.TimeSpan":
                            _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                            break;
                        default:
                            _sb.AppendIndentedLine($"// WARNING: Unknown primitive type '{normalizedType}'");
                            _sb.AppendIndentedLine("reader.SkipField(wireType);");
                            break;
                    }
                    break;

                case TypeCategory.Enum:
                    _sb.AppendIndentedLine($"{targetVar} = ({actualType})reader.ReadVarInt32();");
                    break;

                case TypeCategory.Complex:
                    // Use PushLimit for zero-allocation nested message reading
                    var className = TypeNameHelper.GetSafeMethodName(actualType);
                    _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
                    _sb.AppendIndentedLine("var itemOldLimit = reader.PushLimit(itemLength);");
                    _sb.AppendIndentedLine($"{targetVar} = StreamReaders.Read{className}Content(ref reader);");
                    _sb.AppendIndentedLine("reader.PopLimit(itemOldLimit);");
                    break;
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

            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Write{tupleInfo.SafeName}Content(ref {writerType} writer, {tupleInfo.OriginalTypeName} instance)");
            _sb.StartNewBlock();

            // Pre-declare WriteSizeCalculator for reuse across complex items (ref struct - stack allocated)
            // Only declare if there are complex types that need it
            bool hasComplexItems = tupleInfo.ItemTypes.Any(t => _typeHandler.AnalyzeType(t).Category == TypeCategory.Complex);
            if (hasComplexItems)
            {
                _sb.AppendIndentedLine("global::GProtobuf.Core.WriteSizeCalculator itemCalc;");
                _sb.AppendNewLine();
            }

            // Write each item
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                var itemType = tupleInfo.ItemTypes[i];
                var fieldId = i + 1;
                var sourceVar = GetTupleItemAccessor(fieldId, "instance");

                GenerateItemWrite(sourceVar, itemType, fieldId);
            }

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateItemWrite(string sourceVar, string itemType, int fieldId)
        {
            var typeInfo = _typeHandler.AnalyzeType(itemType);

            // For nullable types, wrap in HasValue check
            if (typeInfo.IsNullable)
            {
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                GenerateItemWriteCore($"{sourceVar}.Value", typeInfo.UnderlyingType, typeInfo.Category, fieldId);
                _sb.EndBlock();
            }
            else
            {
                GenerateItemWriteCore(sourceVar, typeInfo.UnderlyingType, typeInfo.Category, fieldId);
            }
        }

        private void GenerateItemWriteCore(string sourceVar, string actualType, TypeCategory category, int fieldId)
        {
            // Write tag
            TagCodeHelper.WriteTag(_sb, fieldId, GetWireType(actualType));

            switch (category)
            {
                case TypeCategory.Primitive:
                    // Primitive type, string, byte[], Guid - TypeMapping handles all
                    var writeExpr = TypeMapping.GetWriteExpression(actualType, sourceVar, DataFormat.Default, "writer");
                    _sb.AppendIndentedLine($"{writeExpr};");
                    break;

                case TypeCategory.Enum:
                    // Enum type - write as VarInt32
                    _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                    break;

                case TypeCategory.Complex:
                    // Complex type - write as length-prefixed message
                    var className = TypeNameHelper.GetSafeMethodName(actualType);
                    // Reuse pre-declared itemCalc (ref struct reinitializes on assignment)
                    _sb.AppendIndentedLine("itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref itemCalc, {sourceVar});");
                    _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");

                    var writerClass = _writerKind != null ? $"{_writerKind}Writers" : "StreamWriters";
                    _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref writer, {sourceVar});");
                    break;
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
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Calculate{tupleInfo.SafeName}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {tupleInfo.OriginalTypeName} instance)");
            _sb.StartNewBlock();

            // Pre-declare WriteSizeCalculator for reuse across complex items (ref struct - stack allocated)
            // Only declare if there are complex types that need it
            bool hasComplexItems = tupleInfo.ItemTypes.Any(t => _typeHandler.AnalyzeType(t).Category == TypeCategory.Complex);
            if (hasComplexItems)
            {
                _sb.AppendIndentedLine("global::GProtobuf.Core.WriteSizeCalculator itemCalc;");
                _sb.AppendNewLine();
            }

            // Calculate size for each item
            for (int i = 0; i < tupleInfo.Arity; i++)
            {
                var itemType = tupleInfo.ItemTypes[i];
                var fieldId = i + 1;
                var sourceVar = GetTupleItemAccessor(fieldId, "instance");

                GenerateItemSize(sourceVar, itemType, fieldId);
            }

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateItemSize(string sourceVar, string itemType, int fieldId)
        {
            var typeInfo = _typeHandler.AnalyzeType(itemType);

            // For nullable types, wrap in HasValue check
            if (typeInfo.IsNullable)
            {
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                GenerateItemSizeCore($"{sourceVar}.Value", typeInfo.UnderlyingType, typeInfo.Category, fieldId);
                _sb.EndBlock();
            }
            else
            {
                GenerateItemSizeCore(sourceVar, typeInfo.UnderlyingType, typeInfo.Category, fieldId);
            }
        }

        private void GenerateItemSizeCore(string sourceVar, string actualType, TypeCategory category, int fieldId)
        {
            // Add tag size
            TagCodeHelper.AddTagSize(_sb, fieldId, GetWireType(actualType), "calculator");

            switch (category)
            {
                case TypeCategory.Primitive:
                    // Primitive type, string, byte[], Guid - TypeMapping handles all
                    var sizeExpr = TypeMapping.GetSizeExpression(actualType, sourceVar, DataFormat.Default, "calculator");
                    _sb.AppendIndentedLine($"{sizeExpr};");
                    break;

                case TypeCategory.Enum:
                    // Enum type - calculate as VarInt32
                    _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar});");
                    break;

                case TypeCategory.Complex:
                    // Complex type
                    var className = TypeNameHelper.GetSafeMethodName(actualType);
                    // Reuse pre-declared itemCalc (ref struct reinitializes on assignment)
                    _sb.AppendIndentedLine("itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref itemCalc, {sourceVar});");
                    _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)itemCalc.Length);");
                    _sb.AppendIndentedLine("calculator.AddByteLength(itemCalc.Length);");
                    break;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets the proper accessor for a Tuple item, handling Rest property for 8+ elements.
        /// For items 1-7: instance.Item1, instance.Item2, etc.
        /// For item 8: instance.Rest (the Rest property IS the 8th element, which is TRest)
        /// </summary>
        private string GetTupleItemAccessor(int itemIndex, string instanceVar)
        {
            if (itemIndex <= 7)
            {
                return $"{instanceVar}.Item{itemIndex}";
            }
            else
            {
                // For Tuple<T1,T2,T3,T4,T5,T6,T7,TRest>, the Rest property IS TRest
                // For example: Tuple<int,int,int,int,int,int,int,Tuple<int,int>>
                // Item 8 is instance.Rest (which is of type Tuple<int,int>), not instance.Rest.Item1
                return $"{instanceVar}.Rest";
            }
        }

        private WireType GetWireType(string typeName)
        {
            if (TypeHelper.IsPrimitiveType(typeName))
            {
                var normalized = TypeMapping.NormalizeTypeName(typeName);
                return TypeMapping.GetWireType(normalized);
            }

            // Check if it's an enum - enums use VarInt (protobuf wire format)
            if (_registry != null)
            {
                var normalizedType = TypeMapping.NormalizeTypeName(typeName);
                if (_registry.IsEnum(typeName) || _registry.IsEnum(normalizedType))
                {
                    return WireType.VarInt;
                }
            }

            // String, Guid, and complex types use Len
            return WireType.Len;
        }

        #endregion
    }
}
