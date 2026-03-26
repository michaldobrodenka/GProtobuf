using GProtobuf.Generator.V2.CodeGeneration;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;
using System.Collections.Generic;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Handles code generation for Tuple types in protobuf.
    /// Uses Virtual Tuple approach - generates separate methods for each unique Tuple signature.
    /// Example: Tuple&lt;int, string&gt; → ReadTupleOfIntAndStringContent()
    /// Supports Tuple with 2-8 items.
    ///
    /// Virtual type methods are generated in a shared namespace (GProtobuf.VirtualTypes.Serialization)
    /// to avoid code duplication.
    /// </summary>
    internal class TupleHandler
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly VirtualTupleTypeRegistry _tupleRegistry;

        public TupleHandler(StringBuilderWithIndent sb, VirtualTupleTypeRegistry tupleRegistry)
        {
            _sb = sb;
            _tupleRegistry = tupleRegistry ?? new VirtualTupleTypeRegistry();
        }

        /// <summary>
        /// Gets the fully qualified class name for SpanReaders in the shared virtual types namespace.
        /// </summary>
        private static string GetSpanReadersClass()
        {
            return $"global::{SharedVirtualTypesGenerator.SharedNamespace}.SpanReaders";
        }

        /// <summary>
        /// Gets the fully qualified class name for StreamReaders in the shared virtual types namespace.
        /// </summary>
        private static string GetStreamReadersClass()
        {
            return $"global::{SharedVirtualTypesGenerator.SharedNamespace}.StreamReaders";
        }

        /// <summary>
        /// Gets the fully qualified class name for SizeCalculators in the shared virtual types namespace.
        /// </summary>
        private static string GetSizeCalculatorsClass()
        {
            return $"global::{SharedVirtualTypesGenerator.SharedNamespace}.SizeCalculators";
        }

        /// <summary>
        /// Gets the fully qualified writer class name in the shared virtual types namespace.
        /// </summary>
        private static string GetWritersClass(string writerClassName)
        {
            return $"global::{SharedVirtualTypesGenerator.SharedNamespace}.{writerClassName}";
        }

        // Legacy overloads for backwards compatibility (parameters ignored)
        private static string GetSpanReadersClass(TupleTypeInfo tupleInfo) => GetSpanReadersClass();
        private static string GetSpanReadersClass(TupleTypeInfo tupleInfo, string currentNamespace) => GetSpanReadersClass();
        private static string GetStreamReadersClass(TupleTypeInfo tupleInfo, string currentNamespace) => GetStreamReadersClass();
        private static string GetSizeCalculatorsClass(TupleTypeInfo tupleInfo) => GetSizeCalculatorsClass();
        private static string GetSizeCalculatorsClass(TupleTypeInfo tupleInfo, string currentNamespace) => GetSizeCalculatorsClass();
        private static string GetWritersClass(TupleTypeInfo tupleInfo, string writerClassName) => GetWritersClass(writerClassName);
        private static string GetWritersClass(TupleTypeInfo tupleInfo, string writerClassName, string currentNamespace, string writerKind) => GetWritersClass(writerClassName);

        #region Type Detection and Parsing

        /// <summary>
        /// Checks if a type is Tuple&lt;...&gt; or ValueTuple&lt;...&gt;.
        /// Supports 2-8 items.
        /// </summary>
        public static bool IsTupleType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return false;

            return typeName.StartsWith("System.Tuple<") ||
                   typeName.StartsWith("Tuple<") ||
                   typeName.StartsWith("System.ValueTuple<") ||
                   typeName.StartsWith("ValueTuple<");
        }

        /// <summary>
        /// Parses Tuple type and extracts all item types.
        /// Example: Tuple&lt;int, string, bool&gt; → ["int", "string", "bool"]
        /// Returns empty list if parsing fails.
        /// </summary>
        public static List<string> ParseTupleTypes(string tupleTypeName)
        {
            if (!IsTupleType(tupleTypeName))
                return new List<string>();

            var genericStart = tupleTypeName.IndexOf('<');
            var genericEnd = tupleTypeName.LastIndexOf('>');

            if (genericStart < 0 || genericEnd <= genericStart)
                return new List<string>();

            var genericArgs = tupleTypeName.Substring(genericStart + 1, genericEnd - genericStart - 1);

            // Parse all comma-separated items (handle nested generics)
            var items = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < genericArgs.Length; i++)
            {
                char c = genericArgs[i];
                if (c == '<' || c == '(') depth++;
                else if (c == '>' || c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    items.Add(genericArgs.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            // Add last item
            items.Add(genericArgs.Substring(start).Trim());

            return items;
        }

        #endregion

        #region Read (Deserialization)

        /// <summary>
        /// Generates code to read a Tuple field.
        /// Registers the tuple type and generates a call to virtual Read method.
        /// </summary>
        public void GenerateTupleRead(
            string targetVar,
            string tupleTypeName,
            string readerVar = "reader",
            string currentNamespace = null)
        {
            var itemTypes = ParseTupleTypes(tupleTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {tupleTypeName}");
                return;
            }

            // Register tuple type for virtual method generation
            var tupleInfo = _tupleRegistry.Register(tupleTypeName, itemTypes);
            var readersClass = GetSpanReadersClass(tupleInfo, currentNamespace ?? string.Empty);

            // Generate call to virtual Read method
            _sb.AppendIndentedLine($"var tupleLength = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var tupleReader = new SpanReader({readerVar}.GetSlice(tupleLength));");
            _sb.AppendIndentedLine($"{targetVar} = {readersClass}.Read{tupleInfo.SafeName}Content(ref tupleReader);");
        }

        /// <summary>
        /// Generates code to read a collection of Tuples.
        /// </summary>
        public void GenerateTupleCollectionRead(
            string targetVar,
            string elementTypeName,
            int fieldId,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader")
        {
            var itemTypes = ParseTupleTypes(elementTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {elementTypeName}");
                return;
            }

            // Register tuple type
            var tupleInfo = _tupleRegistry.Register(elementTypeName, itemTypes);
            var shortElementType = TypeMapping.GetShortTypeName(elementTypeName);

            // For arrays in Populate methods, use temp list
            string actualTargetVar = targetVar;
            if (collectionKind == CollectionKind.Array && targetVar.StartsWith("instance."))
            {
                var fieldName = targetVar.Substring("instance.".Length);
                actualTargetVar = $"_tempList_{fieldName}";
            }

            // Lazy init collection
            _sb.AppendIndentedLine($"if ({actualTargetVar} == null)");
            _sb.StartNewBlock();

            var initExpr = GenerateCollectionInitialization(shortElementType, collectionKind, collectionTypeName);
            _sb.AppendIndentedLine($"{actualTargetVar} = {initExpr};");

            _sb.EndBlock();

            // Read tuple item using virtual method
            var readersClass = GetSpanReadersClass(tupleInfo);
            _sb.AppendIndentedLine($"var length = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var nestedReader = new SpanReader({readerVar}.GetSlice(length));");
            _sb.AppendIndentedLine($"var item = {readersClass}.Read{tupleInfo.SafeName}Content(ref nestedReader);");
            _sb.AppendIndentedLine($"{actualTargetVar}.Add(item);");
        }

        #endregion

        #region Write (Serialization)

        /// <summary>
        /// Generates code to write a Tuple field.
        /// Registers the tuple type and generates a call to virtual Write method.
        /// </summary>
        public void GenerateTupleWrite(
            int fieldId,
            string sourceVar,
            string tupleTypeName,
            string writerClassName)
        {
            var itemTypes = ParseTupleTypes(tupleTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {tupleTypeName}");
                return;
            }

            // Register tuple type
            var tupleInfo = _tupleRegistry.Register(tupleTypeName, itemTypes);

            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var tupleValue = {sourceVar};");
            _sb.AppendIndentedLine("if (tupleValue != null)");
            _sb.StartNewBlock();

            // Write tag for tuple field
            TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);

            // Calculate tuple content size
            var sizeCalcClass = GetSizeCalculatorsClass(tupleInfo);
            _sb.AppendIndentedLine("var tupleCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{tupleInfo.SafeName}ContentSize(ref tupleCalc, tupleValue);");

            // Write length and content
            var writersClass = GetWritersClass(tupleInfo, writerClassName);
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)tupleCalc.Length);");
            _sb.AppendIndentedLine($"{writersClass}.Write{tupleInfo.SafeName}Content(ref writer, tupleValue);");

            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        /// <summary>
        /// Generates code to write a collection of Tuples.
        /// </summary>
        public void GenerateTupleCollectionWrite(
            int fieldId,
            string sourceVar,
            string tupleTypeName,
            string writerClassName)
        {
            var itemTypes = ParseTupleTypes(tupleTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {tupleTypeName}");
                return;
            }

            // Register tuple type
            var tupleInfo = _tupleRegistry.Register(tupleTypeName, itemTypes);

            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var collection = {sourceVar};");
            _sb.AppendIndentedLine("if (collection != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("foreach (var item in collection)");
            _sb.StartNewBlock();

            // Write tag
            TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);

            // Calculate and write length
            var sizeCalcClass = GetSizeCalculatorsClass(tupleInfo);
            _sb.AppendIndentedLine("var tupleCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{tupleInfo.SafeName}ContentSize(ref tupleCalc, item);");
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)tupleCalc.Length);");

            // Write content
            var writersClass = GetWritersClass(tupleInfo, writerClassName);
            _sb.AppendIndentedLine($"{writersClass}.Write{tupleInfo.SafeName}Content(ref writer, item);");

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates code to calculate size of a Tuple field.
        /// </summary>
        public void GenerateTupleSize(
            int fieldId,
            string sourceVar,
            string tupleTypeName,
            string calculatorVar = "calculator")
        {
            var itemTypes = ParseTupleTypes(tupleTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {tupleTypeName}");
                return;
            }

            // Register tuple type
            var tupleInfo = _tupleRegistry.Register(tupleTypeName, itemTypes);

            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var tupleValue = {sourceVar};");
            _sb.AppendIndentedLine("if (tupleValue != null)");
            _sb.StartNewBlock();

            // Add tag size for the tuple field
            TagCodeHelper.AddTagSize(_sb, fieldId, WireType.Len, calculatorVar);

            // Calculate tuple content size
            var sizeCalcClass = GetSizeCalculatorsClass(tupleInfo);
            _sb.AppendIndentedLine("var tupleCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{tupleInfo.SafeName}ContentSize(ref tupleCalc, tupleValue);");

            // Add tuple length varint + content size
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)tupleCalc.Length);");
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(tupleCalc.Length);");

            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        /// <summary>
        /// Generates code to calculate size of a collection of Tuples.
        /// </summary>
        public void GenerateTupleCollectionSize(
            int fieldId,
            string sourceVar,
            string tupleTypeName,
            string calculatorVar = "calculator")
        {
            var itemTypes = ParseTupleTypes(tupleTypeName);
            if (itemTypes.Count == 0)
            {
                _sb.AppendIndentedLine($"// ERROR: Failed to parse Tuple type: {tupleTypeName}");
                return;
            }

            // Register tuple type
            var tupleInfo = _tupleRegistry.Register(tupleTypeName, itemTypes);
            var sizeCalcClass = GetSizeCalculatorsClass(tupleInfo);

            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var collection = {sourceVar};");
            _sb.AppendIndentedLine("if (collection != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("foreach (var item in collection)");
            _sb.StartNewBlock();

            // Add tag size
            TagCodeHelper.AddTagSize(_sb, fieldId, WireType.Len, calculatorVar);

            // Calculate item content size
            _sb.AppendIndentedLine("var tupleCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{tupleInfo.SafeName}ContentSize(ref tupleCalc, item);");

            // Add length varint size + content size
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)tupleCalc.Length);");
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(tupleCalc.Length);");

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        #endregion

        #region Helpers

        private string GenerateCollectionInitialization(string elementType, CollectionKind kind, string collectionTypeName)
        {
            switch (kind)
            {
                case CollectionKind.Array:
                    return $"new global::System.Collections.Generic.List<{elementType}>()";

                case CollectionKind.InterfaceCollection:
                case CollectionKind.ConcreteCollection:
                    if (collectionTypeName != null)
                    {
                        if (TypeHelper.IsHashSetType(collectionTypeName))
                            return $"new global::System.Collections.Generic.HashSet<{elementType}>()";
                        if (TypeHelper.IsListType(collectionTypeName))
                            return $"new global::System.Collections.Generic.List<{elementType}>()";
                    }
                    return $"new global::System.Collections.Generic.List<{elementType}>()";

                default:
                    return $"new global::System.Collections.Generic.List<{elementType}>()";
            }
        }

        #endregion
    }
}
