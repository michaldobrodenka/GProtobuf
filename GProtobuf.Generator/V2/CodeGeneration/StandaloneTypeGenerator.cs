using System.Collections.Generic;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;

// Use existing type mapping and helper classes for primitive type handling
using static GProtobuf.Generator.V2.TypeMapping;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates serialization/deserialization code for standalone types
    /// (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;) registered via [GenerateSerializer] attribute.
    ///
    /// Wire format for List/Array (protobuf-net compatible):
    /// [varint length₁][message₁ bytes][varint length₂][message₂ bytes]...
    ///
    /// Wire format for Dictionary (map entries):
    /// [varint entryLength][field1=key][field2=value][varint entryLength][field1=key][field2=value]...
    /// </summary>
    internal class StandaloneTypeGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly TypeRegistry _registry;

        public StandaloneTypeGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
        {
            _sb = sb;
            _registry = registry;
        }

        #region Deserializers

        public void GenerateDeserializers(List<StandaloneTypeInfo> standaloneTypes)
        {
            foreach (var standalone in standaloneTypes)
            {
                GenerateDeserializer(standalone);
            }
        }

        private void GenerateDeserializer(StandaloneTypeInfo info)
        {
            var methodName = $"Deserialize{info.MethodNameSuffix}";

            switch (info.Kind)
            {
                case StandaloneTypeKind.List:
                    GenerateListDeserializer(info, methodName);
                    break;
                case StandaloneTypeKind.Array:
                    GenerateArrayDeserializer(info, methodName);
                    break;
                case StandaloneTypeKind.Dictionary:
                    GenerateDictionaryDeserializer(info, methodName);
                    break;
                case StandaloneTypeKind.Primitive:
                    // Primitives are typically not serialized as standalone top-level
                    break;
            }
        }

        private void GenerateListDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var returnType = $"global::System.Collections.Generic.List<{GetGlobalTypeName(elementType)}>";
            GenerateCollectionDeserializerCore(info, methodName, returnType, "return list;");
        }

        private void GenerateArrayDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var returnType = $"{GetGlobalTypeName(elementType)}[]";
            GenerateCollectionDeserializerCore(info, methodName, returnType, "return list.ToArray();");
        }

        /// <summary>
        /// Shared implementation for List and Array deserializers.
        /// </summary>
        private void GenerateCollectionDeserializerCore(StandaloneTypeInfo info, string methodName, string returnType, string returnStatement)
        {
            var elementType = info.ElementType!;
            var listType = $"global::System.Collections.Generic.List<{GetGlobalTypeName(elementType)}>";

            // ReadOnlySpan<byte> overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(ReadOnlySpan<byte> data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var list = new {listType}();");
            _sb.AppendIndentedLine("var reader = new SpanReader(data);");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            if (info.ElementIsPrimitive)
            {
                // Primitive types: protobuf-net uses unpacked format [tag=0x08][value] for each element
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                var expectedWireType = GetWireType(elementType);
                _sb.AppendIndentedLine($"if ((tag & 0x07) != {expectedWireType}) throw new InvalidDataException($\"Expected wire type {expectedWireType}, got {{tag & 0x07}}\");");
                var readExpr = GetPrimitiveReadExpression(elementType);
                _sb.AppendIndentedLine($"list.Add({readExpr});");
            }
            else
            {
                // Complex types: protobuf-net uses [tag=0x0A][length][message] for each item
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("if ((tag & 0x07) != 2) throw new InvalidDataException($\"Expected wire type 2, got {tag & 0x07}\");");
                GenerateComplexElementRead(elementType, "list.Add");
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine(returnStatement);
            _sb.EndBlock();
            _sb.AppendNewLine();

            // byte[] overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(byte[] data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"return {methodName}(new ReadOnlySpan<byte>(data));");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateComplexElementRead(string elementType, string addMethod)
        {
            // Read length-prefixed message
            _sb.AppendIndentedLine("var length = (int)reader.ReadVarUInt32();");
            var elementClassName = TypeNameHelper.GetClassName(elementType);
            var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
            _sb.AppendIndentedLine("var subReader = reader.CreateSubReader(length);");
            _sb.AppendIndentedLine($"{addMethod}({spanReadersClass}.Read{elementClassName}Content(ref subReader));");
        }

        private void GenerateDictionaryDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;

            // Determine return type - use custom dictionary if specified
            string returnType;
            string dictInstantiation;
            if (info.IsCustomDictionaryType && info.CustomDictionaryTypeName != null)
            {
                returnType = $"global::{info.CustomDictionaryTypeName}<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";
                dictInstantiation = returnType;
            }
            else
            {
                returnType = $"global::System.Collections.Generic.Dictionary<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";
                dictInstantiation = returnType;
            }

            // ReadOnlySpan<byte> overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(ReadOnlySpan<byte> data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var dict = new {dictInstantiation}();");
            _sb.AppendIndentedLine("var reader = new SpanReader(data);");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            // protobuf-net format: [tag=0x0A][length][entry] for each map entry
            _sb.AppendIndentedLine("var entryTag = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("if ((entryTag & 0x07) != 2) throw new InvalidDataException($\"Expected wire type 2 for map entry, got {entryTag & 0x07}\");");

            // Each entry is length-prefixed with key=field1, value=field2
            _sb.AppendIndentedLine("var entryLength = (int)reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var entryEnd = reader.Position + entryLength;");
            _sb.AppendIndentedLine($"{GetGlobalTypeName(keyType)} key = default;");

            // Generate value initialization based on type
            GenerateValueInitialization(info, valueType);
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("while (reader.Position < entryEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var tag = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var fieldNumber = tag >> 3;");
            _sb.AppendIndentedLine("switch (fieldNumber)");
            _sb.StartNewBlock();

            // Key field (field 1)
            _sb.AppendIndentedLine("case 1:");
            _sb.IndentLevel++;
            GenerateFieldRead(keyType, "key", info.KeyIsPrimitive, null);
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            // Value field (field 2)
            // For collection values, each item is a separate field 2 occurrence
            _sb.AppendIndentedLine("case 2:");
            _sb.IndentLevel++;
            if (info.ValueKind == StandaloneTypeKind.List || info.ValueKind == StandaloneTypeKind.Array)
            {
                GenerateCollectionValueItemRead(valueType, "value", info.NestedValueInfo!);
            }
            else
            {
                GenerateFieldRead(valueType, "value", info.ValueIsPrimitive, info.NestedValueInfo);
            }
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.AppendIndentedLine("default:");
            _sb.IndentLevel++;
            _sb.AppendIndentedLine("reader.SkipField((WireType)(tag & 0x07));");
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while inner

            // For array values, convert temp list to array
            if (info.ValueKind == StandaloneTypeKind.Array && info.NestedValueInfo != null)
            {
                _sb.AppendIndentedLine("value = _tempList_value?.ToArray();");
            }

            _sb.AppendIndentedLine("dict[key] = value;");
            _sb.EndBlock(); // while outer

            _sb.AppendIndentedLine("return dict;");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // byte[] overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(byte[] data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"return {methodName}(new ReadOnlySpan<byte>(data));");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateValueInitialization(StandaloneTypeInfo info, string valueType)
        {
            // For nested collections (List, Array), initialize collection to accumulate items
            if (info.ValueKind == StandaloneTypeKind.List && info.NestedValueInfo != null)
            {
                var elementType = info.NestedValueInfo.ElementType!;
                var globalElementType = GetGlobalTypeName(elementType);
                _sb.AppendIndentedLine($"var value = new global::System.Collections.Generic.List<{globalElementType}>();");
            }
            else if (info.ValueKind == StandaloneTypeKind.Array && info.NestedValueInfo != null)
            {
                // Use temp list for arrays, convert to array after reading
                var elementType = info.NestedValueInfo.ElementType!;
                var globalElementType = GetGlobalTypeName(elementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{globalElementType}> _tempList_value = null;");
                _sb.AppendIndentedLine($"{GetGlobalTypeName(valueType)} value = null;");
            }
            else
            {
                _sb.AppendIndentedLine($"{GetGlobalTypeName(valueType)} value = default;");
            }
        }

        private void GenerateCollectionValueItemRead(string valueType, string varName, StandaloneTypeInfo nestedInfo)
        {
            // Each field 2 occurrence contains a single element of the collection
            var elementType = nestedInfo.ElementType!;

            if (nestedInfo.Kind == StandaloneTypeKind.Array)
            {
                // For arrays, accumulate in temp list
                var globalElementType = GetGlobalTypeName(elementType);
                _sb.AppendIndentedLine($"_tempList_{varName} ??= new global::System.Collections.Generic.List<{globalElementType}>();");

                if (nestedInfo.ElementIsPrimitive)
                {
                    var readExpr = GetPrimitiveReadExpression(elementType);
                    _sb.AppendIndentedLine($"_tempList_{varName}.Add({readExpr});");
                }
                else
                {
                    var className = TypeNameHelper.GetClassName(elementType);
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                    _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                    _sb.AppendIndentedLine($"_tempList_{varName}.Add({spanReadersClass}.Read{className}Content(ref itemReader_{varName}));");
                }
            }
            else
            {
                // For lists, add directly
                if (nestedInfo.ElementIsPrimitive)
                {
                    var readExpr = GetPrimitiveReadExpression(elementType);
                    _sb.AppendIndentedLine($"{varName}.Add({readExpr});");
                }
                else
                {
                    var className = TypeNameHelper.GetClassName(elementType);
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                    _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                    _sb.AppendIndentedLine($"{varName}.Add({spanReadersClass}.Read{className}Content(ref itemReader_{varName}));");
                }
            }
        }

        private void GenerateFieldRead(string typeName, string varName, bool isPrimitive, StandaloneTypeInfo? nestedInfo)
        {
            if (isPrimitive || IsPrimitiveType(typeName))
            {
                // All primitives use GetPrimitiveReadExpression which includes proper WireType
                var readExpr = GetPrimitiveReadExpression(typeName);
                _sb.AppendIndentedLine($"{varName} = {readExpr};");
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.List)
            {
                // Nested List<T> - read as length-delimited collection
                GenerateNestedListRead(typeName, varName, nestedInfo);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Array)
            {
                // Nested T[] - read as length-delimited collection, convert to array
                GenerateNestedArrayRead(typeName, varName, nestedInfo);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Dictionary)
            {
                // Nested Dictionary - read as length-delimited
                GenerateNestedDictionaryRead(typeName, varName, nestedInfo);
            }
            else
            {
                // Custom type - read as length-delimited, create sub-reader
                var className = TypeNameHelper.GetClassName(typeName);
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(typeName, _registry);
                _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var subReader_{varName} = reader.CreateSubReader(len_{varName});");
                _sb.AppendIndentedLine($"{varName} = {spanReadersClass}.Read{className}Content(ref subReader_{varName});");
            }
        }

        private void GenerateNestedListRead(string listTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var elementType = nestedInfo.ElementType!;
            var globalElementType = GetGlobalTypeName(elementType);

            // Read length prefix for the list
            _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var end_{varName} = reader.Position + len_{varName};");
            _sb.AppendIndentedLine($"{varName} = new global::System.Collections.Generic.List<{globalElementType}>();");

            // Read each element
            _sb.AppendIndentedLine($"while (reader.Position < end_{varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var itemTag_{varName} = reader.ReadVarUInt32();");

            if (nestedInfo.ElementIsPrimitive)
            {
                // Primitive element
                var readExpr = GetPrimitiveReadExpression(elementType);
                _sb.AppendIndentedLine($"{varName}.Add({readExpr});");
            }
            else
            {
                // Complex element - read length-delimited
                var className = TypeNameHelper.GetClassName(elementType);
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                _sb.AppendIndentedLine($"{varName}.Add({spanReadersClass}.Read{className}Content(ref itemReader_{varName}));");
            }

            _sb.EndBlock();
        }

        private void GenerateNestedArrayRead(string arrayTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var elementType = nestedInfo.ElementType!;
            var globalElementType = GetGlobalTypeName(elementType);

            // Read length prefix for the array
            _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var end_{varName} = reader.Position + len_{varName};");
            _sb.AppendIndentedLine($"var tempList_{varName} = new global::System.Collections.Generic.List<{globalElementType}>();");

            // Read each element
            _sb.AppendIndentedLine($"while (reader.Position < end_{varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var itemTag_{varName} = reader.ReadVarUInt32();");

            if (nestedInfo.ElementIsPrimitive)
            {
                // Primitive element
                var readExpr = GetPrimitiveReadExpression(elementType);
                _sb.AppendIndentedLine($"tempList_{varName}.Add({readExpr});");
            }
            else
            {
                // Complex element - read length-delimited
                var className = TypeNameHelper.GetClassName(elementType);
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                _sb.AppendIndentedLine($"tempList_{varName}.Add({spanReadersClass}.Read{className}Content(ref itemReader_{varName}));");
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine($"{varName} = tempList_{varName}.ToArray();");
        }

        private void GenerateNestedDictionaryRead(string dictTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var keyType = nestedInfo.KeyType!;
            var valueType = nestedInfo.ValueType!;
            var globalKeyType = GetGlobalTypeName(keyType);
            var globalValueType = GetGlobalTypeName(valueType);

            // Read length prefix for the nested dictionary
            _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var end_{varName} = reader.Position + len_{varName};");
            _sb.AppendIndentedLine($"{varName} = new global::System.Collections.Generic.Dictionary<{globalKeyType}, {globalValueType}>();");

            // Read each entry
            _sb.AppendIndentedLine($"while (reader.Position < end_{varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var entryTag_{varName} = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var entryLen_{varName} = (int)reader.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var entryEnd_{varName} = reader.Position + entryLen_{varName};");
            _sb.AppendIndentedLine($"{globalKeyType} innerKey_{varName} = default;");
            _sb.AppendIndentedLine($"{globalValueType} innerValue_{varName} = default;");

            _sb.AppendIndentedLine($"while (reader.Position < entryEnd_{varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var innerTag_{varName} = reader.ReadVarInt32();");
            _sb.AppendIndentedLine($"var innerField_{varName} = innerTag_{varName} >> 3;");
            _sb.AppendIndentedLine($"switch (innerField_{varName})");
            _sb.StartNewBlock();

            _sb.AppendIndentedLine("case 1:");
            _sb.IndentLevel++;
            GenerateFieldRead(keyType, $"innerKey_{varName}", nestedInfo.KeyIsPrimitive, null);
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.AppendIndentedLine("case 2:");
            _sb.IndentLevel++;
            GenerateFieldRead(valueType, $"innerValue_{varName}", nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo);
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.AppendIndentedLine("default:");
            _sb.IndentLevel++;
            _sb.AppendIndentedLine($"reader.SkipField((WireType)(innerTag_{varName} & 0x07));");
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while inner

            _sb.AppendIndentedLine($"{varName}[innerKey_{varName}] = innerValue_{varName};");
            _sb.EndBlock(); // while outer
        }

        #endregion

        #region Serializers

        public void GenerateSerializers(List<StandaloneTypeInfo> standaloneTypes)
        {
            foreach (var standalone in standaloneTypes)
            {
                GenerateSerializer(standalone);
            }
        }

        private void GenerateSerializer(StandaloneTypeInfo info)
        {
            var methodName = $"Serialize{info.MethodNameSuffix}";

            switch (info.Kind)
            {
                case StandaloneTypeKind.List:
                    GenerateListSerializer(info, methodName);
                    break;
                case StandaloneTypeKind.Array:
                    GenerateArraySerializer(info, methodName);
                    break;
                case StandaloneTypeKind.Dictionary:
                    GenerateDictionarySerializer(info, methodName);
                    break;
                case StandaloneTypeKind.Primitive:
                    break;
            }
        }

        private void GenerateListSerializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var paramType = $"global::System.Collections.Generic.List<{GetGlobalTypeName(elementType)}>";
            GenerateCollectionSerializerCore(info, methodName, paramType, "list");
        }

        private void GenerateArraySerializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var paramType = $"{GetGlobalTypeName(elementType)}[]";
            GenerateCollectionSerializerCore(info, methodName, paramType, "array");
        }

        /// <summary>
        /// Shared implementation for List and Array serializers.
        /// </summary>
        private void GenerateCollectionSerializerCore(StandaloneTypeInfo info, string methodName, string paramType, string varName)
        {
            var elementType = info.ElementType!;

            // Stream serializer
            _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} {varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            _sb.AppendIndentedLine($"foreach (var item in {varName})");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, info.ElementIsPrimitive, "item", "writer", false);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // IBufferWriter serializer
            _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} {varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
            _sb.AppendIndentedLine($"foreach (var item in {varName})");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, info.ElementIsPrimitive, "item", "writer", true);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateElementWrite(string elementType, bool isPrimitive, string varName, string writerName, bool isBufferWriter)
        {
            if (isPrimitive)
            {
                // Primitive types: [tag][value] for each element (unpacked format)
                var wireType = GetWireType(elementType);
                var tag = (1 << 3) | wireType; // field 1 + wire type
                _sb.AppendIndentedLine($"{writerName}.WriteVarUInt32(0x{tag:X2}); // field 1, wire type {wireType}");

                // For string, use WriteString which handles length prefix
                if (elementType == "string" || elementType == "System.String")
                {
                    _sb.AppendIndentedLine($"{writerName}.WriteString({varName});");
                }
                else
                {
                    var writeExpr = GetPrimitiveWriteExpression(elementType, varName);
                    _sb.AppendIndentedLine($"{writerName}.{writeExpr};");
                }
            }
            else
            {
                // Complex types: [tag=0x0A][length][message] for each item
                // tag 0x0A = field 1, wire type 2 (length-delimited)
                _sb.AppendIndentedLine($"{writerName}.WriteVarUInt32(0x0A); // field 1, wire type 2");
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                var writerClass = NamespaceHelper.GetWritersClass(elementType, isBufferWriter ? "BufferWriters" : "StreamWriters", _registry);
                _sb.AppendIndentedLine("var sizeCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref sizeCalc, {varName});");
                _sb.AppendIndentedLine($"{writerName}.WriteVarUInt32((uint)sizeCalc.Length);");
                _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref {writerName}, {varName});");
            }
        }

        private void GenerateDictionarySerializer(StandaloneTypeInfo info, string methodName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;

            // Determine parameter type - use IDictionary for custom dictionary types
            string paramType;
            if (info.IsCustomDictionaryType)
            {
                paramType = $"global::System.Collections.Generic.IDictionary<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";
            }
            else
            {
                paramType = $"global::System.Collections.Generic.Dictionary<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";
            }

            // Stream serializer
            _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} dict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            _sb.AppendIndentedLine("foreach (var kvp in dict)");
            _sb.StartNewBlock();
            GenerateMapEntryWrite(info, "StreamWriters");
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // IBufferWriter serializer
            _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} dict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
            _sb.AppendIndentedLine("foreach (var kvp in dict)");
            _sb.StartNewBlock();
            GenerateMapEntryWrite(info, "BufferWriters");
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateMapEntryWrite(StandaloneTypeInfo info, string writerClassName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;

            // protobuf-net format: [tag=0x0A][length][entry] for each map entry
            // Write outer tag first
            _sb.AppendIndentedLine("writer.WriteVarUInt32(0x0A); // field 1, wire type 2 (map entry)");

            // Calculate entry size
            _sb.AppendIndentedLine("// Calculate entry size");
            GenerateSizeCalculation(keyType, "kvp.Key", 1, info.KeyIsPrimitive, null, "keySize");
            GenerateSizeCalculation(valueType, "kvp.Value", 2, info.ValueIsPrimitive, info.NestedValueInfo, "valueSize");
            _sb.AppendIndentedLine("var entrySize = keySize + valueSize;");
            _sb.AppendNewLine();

            // Write entry length
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entrySize);");

            // Write key (field 1)
            _sb.AppendIndentedLine("// Key (field 1)");
            GenerateTaggedFieldWrite(keyType, "kvp.Key", 1, info.KeyIsPrimitive, null, writerClassName);

            // Write value (field 2)
            _sb.AppendIndentedLine("// Value (field 2)");
            GenerateTaggedFieldWrite(valueType, "kvp.Value", 2, info.ValueIsPrimitive, info.NestedValueInfo, writerClassName);
        }

        private void GenerateSizeCalculation(string typeName, string varName, int fieldNumber, bool isPrimitive, StandaloneTypeInfo? nestedInfo, string resultVarName)
        {
            var tagSize = fieldNumber < 16 ? 1 : 2;

            if (isPrimitive || IsPrimitiveType(typeName))
            {
                // For primitives, we can use inline expressions
                var expr = GetFieldSizeExpression(typeName, varName, fieldNumber, isPrimitive, nestedInfo);
                _sb.AppendIndentedLine($"var {resultVarName} = {expr};");
            }
            else if (nestedInfo != null && (nestedInfo.Kind == StandaloneTypeKind.List || nestedInfo.Kind == StandaloneTypeKind.Array))
            {
                // Nested collection - generate loop for size calculation
                GenerateNestedCollectionSizeCalculation(varName, fieldNumber, nestedInfo, tagSize, resultVarName);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Dictionary)
            {
                // Nested dictionary - generate loop
                GenerateNestedDictionarySizeCalculation(varName, fieldNumber, nestedInfo, tagSize, resultVarName);
            }
            else
            {
                // Complex type - use WriteSizeCalculator
                var className = TypeNameHelper.GetClassName(typeName);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName, _registry);
                var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");
                _sb.AppendIndentedLine($"var _calc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref _calc_{safeVarName}_{fieldNumber}, {varName});");
                _sb.AppendIndentedLine($"var {resultVarName} = {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_calc_{safeVarName}_{fieldNumber}.Length) + _calc_{safeVarName}_{fieldNumber}.Length;");
            }
        }

        private void GenerateNestedCollectionSizeCalculation(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize, string resultVarName)
        {
            var elementType = nestedInfo.ElementType!;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            if (nestedInfo.ElementIsPrimitive)
            {
                // For primitive elements, use LINQ (no ref params needed)
                var elemSize = GetPrimitiveSizeExpression(elementType, "x");
                _sb.AppendIndentedLine($"var _listContentSize_{safeVarName}_{fieldNumber} = {varName}.Sum(x => 1 + {elemSize});");
            }
            else
            {
                // For complex elements, generate loop with WriteSizeCalculator
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                _sb.AppendIndentedLine($"var _listContentSize_{safeVarName}_{fieldNumber} = 0;");
                _sb.AppendIndentedLine($"foreach (var _item_{safeVarName}_{fieldNumber} in {varName})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"var _itemCalc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref _itemCalc_{safeVarName}_{fieldNumber}, _item_{safeVarName}_{fieldNumber});");
                _sb.AppendIndentedLine($"_listContentSize_{safeVarName}_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)_itemCalc_{safeVarName}_{fieldNumber}.Length) + _itemCalc_{safeVarName}_{fieldNumber}.Length;");
                _sb.EndBlock();
            }
            _sb.AppendIndentedLine($"var {resultVarName} = {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_listContentSize_{safeVarName}_{fieldNumber}) + _listContentSize_{safeVarName}_{fieldNumber};");
        }

        private void GenerateNestedDictionarySizeCalculation(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize, string resultVarName)
        {
            var keyType = nestedInfo.KeyType!;
            var valueType = nestedInfo.ValueType!;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            _sb.AppendIndentedLine($"var _dictContentSize_{safeVarName}_{fieldNumber} = 0;");
            _sb.AppendIndentedLine($"foreach (var _kvp_{safeVarName}_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            // Calculate key size
            GenerateSizeCalculation(keyType, $"_kvp_{safeVarName}_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, $"_innerKeySize_{safeVarName}_{fieldNumber}");
            // Calculate value size
            GenerateSizeCalculation(valueType, $"_kvp_{safeVarName}_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, $"_innerValueSize_{safeVarName}_{fieldNumber}");

            _sb.AppendIndentedLine($"var _innerEntrySize_{safeVarName}_{fieldNumber} = _innerKeySize_{safeVarName}_{fieldNumber} + _innerValueSize_{safeVarName}_{fieldNumber};");
            _sb.AppendIndentedLine($"_dictContentSize_{safeVarName}_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)_innerEntrySize_{safeVarName}_{fieldNumber}) + _innerEntrySize_{safeVarName}_{fieldNumber};");
            _sb.EndBlock();
            _sb.AppendIndentedLine($"var {resultVarName} = {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_dictContentSize_{safeVarName}_{fieldNumber}) + _dictContentSize_{safeVarName}_{fieldNumber};");
        }

        private void GenerateTaggedFieldWrite(string typeName, string varName, int fieldNumber, bool isPrimitive, StandaloneTypeInfo? nestedInfo, string writerClassName)
        {
            var wireType = GetWireType(typeName);
            var tag = (fieldNumber << 3) | wireType;

            _sb.AppendIndentedLine($"writer.WriteVarUInt32({tag}); // field {fieldNumber}, wire type {wireType}");

            if (isPrimitive || IsPrimitiveType(typeName))
            {
                if (typeName == "string" || typeName == "System.String")
                {
                    _sb.AppendIndentedLine($"writer.WriteString({varName});");
                }
                else
                {
                    var writeExpr = GetPrimitiveWriteExpression(typeName, varName);
                    _sb.AppendIndentedLine($"writer.{writeExpr};");
                }
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.List)
            {
                // Nested List<T> - calculate and write list content
                GenerateNestedListWrite(varName, fieldNumber, nestedInfo, writerClassName);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Array)
            {
                // Nested T[] - calculate and write array content
                GenerateNestedArrayWrite(varName, fieldNumber, nestedInfo, writerClassName);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Dictionary)
            {
                // Nested Dictionary - calculate and write
                GenerateNestedDictionaryWrite(varName, fieldNumber, nestedInfo, writerClassName);
            }
            else
            {
                // Complex type - write as length-delimited using WriteSizeCalculator
                var className = TypeNameHelper.GetClassName(typeName);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName, _registry);
                var writerClass = NamespaceHelper.GetWritersClass(typeName, writerClassName, _registry);
                _sb.AppendIndentedLine($"var sizeCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref sizeCalc_{fieldNumber}, {varName});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)sizeCalc_{fieldNumber}.Length);");
                _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref writer, {varName});");
            }
        }

        private void GenerateNestedListWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName)
        {
            var elementType = nestedInfo.ElementType!;

            // Calculate list size first
            _sb.AppendIndentedLine($"var listSize_{fieldNumber} = 0;");
            _sb.AppendIndentedLine($"foreach (var item_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                var wireType = GetWireType(elementType);
                var itemTag = (1 << 3) | wireType;
                _sb.AppendIndentedLine($"listSize_{fieldNumber} += 1 + {GetPrimitiveSizeExpression(elementType, $"item_{fieldNumber}")};");
            }
            else
            {
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                _sb.AppendIndentedLine($"var itemCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref itemCalc_{fieldNumber}, item_{fieldNumber});");
                _sb.AppendIndentedLine($"listSize_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)itemCalc_{fieldNumber}.Length) + itemCalc_{fieldNumber}.Length;");
            }
            _sb.EndBlock();

            // Write list length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)listSize_{fieldNumber});");

            // Write each element
            _sb.AppendIndentedLine($"foreach (var item_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                var wireType = GetWireType(elementType);
                var itemTag = (1 << 3) | wireType;
                _sb.AppendIndentedLine($"writer.WriteVarUInt32(0x{itemTag:X2}); // field 1, wire type {wireType}");

                if (elementType == "string" || elementType == "System.String")
                {
                    _sb.AppendIndentedLine($"writer.WriteString(item_{fieldNumber});");
                }
                else
                {
                    var writeExpr = GetPrimitiveWriteExpression(elementType, $"item_{fieldNumber}");
                    _sb.AppendIndentedLine($"writer.{writeExpr};");
                }
            }
            else
            {
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                var writerClass = NamespaceHelper.GetWritersClass(elementType, writerClassName, _registry);
                _sb.AppendIndentedLine("writer.WriteVarUInt32(0x0A); // field 1, wire type 2");
                _sb.AppendIndentedLine($"var itemWriteCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}ContentSize(ref itemWriteCalc_{fieldNumber}, item_{fieldNumber});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)itemWriteCalc_{fieldNumber}.Length);");
                _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref writer, item_{fieldNumber});");
            }
            _sb.EndBlock();
        }

        private void GenerateNestedArrayWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName)
        {
            // Arrays use the same format as Lists
            GenerateNestedListWrite(varName, fieldNumber, nestedInfo, writerClassName);
        }

        private void GenerateNestedDictionaryWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName)
        {
            var keyType = nestedInfo.KeyType!;
            var valueType = nestedInfo.ValueType!;

            // Calculate nested dictionary size
            _sb.AppendIndentedLine($"var dictSize_{fieldNumber} = 0;");
            _sb.AppendIndentedLine($"foreach (var innerKvp_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            // Entry size = tag(1) + length varint + key field + value field
            GenerateSizeCalculation(keyType, $"innerKvp_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, $"innerKeySize_{fieldNumber}");
            GenerateSizeCalculation(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, $"innerValueSize_{fieldNumber}");
            _sb.AppendIndentedLine($"var innerEntrySize_{fieldNumber} = innerKeySize_{fieldNumber} + innerValueSize_{fieldNumber};");
            _sb.AppendIndentedLine($"dictSize_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)innerEntrySize_{fieldNumber}) + innerEntrySize_{fieldNumber};");
            _sb.EndBlock();

            // Write dictionary length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)dictSize_{fieldNumber});");

            // Write each entry
            _sb.AppendIndentedLine($"foreach (var innerKvp_{fieldNumber} in {varName})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("writer.WriteVarUInt32(0x0A); // entry tag");

            // Calculate and write entry size
            GenerateSizeCalculation(keyType, $"innerKvp_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, $"innerKeySize2_{fieldNumber}");
            GenerateSizeCalculation(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, $"innerValueSize2_{fieldNumber}");
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)(innerKeySize2_{fieldNumber} + innerValueSize2_{fieldNumber}));");

            // Write key and value
            GenerateTaggedFieldWrite(keyType, $"innerKvp_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, writerClassName);
            GenerateTaggedFieldWrite(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, writerClassName);
            _sb.EndBlock();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Converts a type name to its code-generation form.
        /// Uses C# keywords for primitives (int, string, etc.) and global:: prefix for custom types.
        /// </summary>
        private static string GetGlobalTypeName(string typeName)
        {
            // Check if it's a primitive/simple type first
            if (TypeMapping.IsSimpleType(typeName))
            {
                // Return C# keyword form (int, string, etc.)
                return TypeMapping.GetShortTypeName(typeName);
            }

            // For non-primitive types, use global:: prefix
            return $"global::{typeName}";
        }

        /// <summary>
        /// Checks if a type is a primitive type (can be serialized directly).
        /// Delegates to TypeMapping.IsSimpleType.
        /// </summary>
        private static bool IsPrimitiveType(string typeName)
        {
            return TypeMapping.IsSimpleType(typeName);
        }

        /// <summary>
        /// Gets the read expression for a primitive type.
        /// Delegates to TypeMapping.GetElementReadExpression.
        /// </summary>
        private static string GetPrimitiveReadExpression(string typeName)
        {
            // Use TypeMapping for read expression - it provides more comprehensive handling
            var readExpr = TypeMapping.GetElementReadExpression(typeName, DataFormat.Default, "reader");

            // Fallback for unexpected types
            return readExpr ?? "reader.ReadVarInt32()";
        }

        /// <summary>
        /// Gets the write expression for a primitive type (without writer. prefix).
        /// Delegates to TypeMapping.GetElementWriteExpression and strips the writer prefix.
        /// </summary>
        private static string GetPrimitiveWriteExpression(string typeName, string varName)
        {
            // Use TypeMapping for write expression
            var writeExpr = TypeMapping.GetElementWriteExpression(typeName, varName, DataFormat.Default, "writer");

            // Strip the "writer." prefix since the caller adds it
            if (writeExpr != null && writeExpr.StartsWith("writer."))
                return writeExpr.Substring(7);

            // Fallback for unexpected types
            return $"WriteVarInt32({varName})";
        }

        /// <summary>
        /// Gets the inline size expression for a primitive type.
        /// This is used for calculating sizes without WriteSizeCalculator.
        /// </summary>
        private static string GetPrimitiveSizeExpression(string typeName, string varName)
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);

            return normalized switch
            {
                "System.Int32" => $"global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})",
                "System.UInt32" => $"global::GProtobuf.Core.Utils.GetVarintSize({varName})",
                "System.Int64" => $"global::GProtobuf.Core.Utils.GetVarInt64Size({varName})",
                "System.UInt64" => $"global::GProtobuf.Core.Utils.GetVarUInt64Size({varName})",
                "System.Int16" => $"global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})",
                "System.UInt16" => $"global::GProtobuf.Core.Utils.GetVarintSize({varName})",
                "System.Byte" => "1",
                "System.SByte" => "1",
                "System.Boolean" => "1",
                "System.Single" => "4",
                "System.Double" => "8",
                "System.String" => $"(global::System.Text.Encoding.UTF8.GetByteCount({varName}) is var _strLen_{varName.Replace(".", "_")} ? global::GProtobuf.Core.Utils.GetVarintSize((uint)_strLen_{varName.Replace(".", "_")}) + _strLen_{varName.Replace(".", "_")} : 0)",
                _ => $"global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})"
            };
        }

        private static string GetFieldSizeExpression(string typeName, string varName, int fieldNumber, bool isPrimitive = false, StandaloneTypeInfo? nestedInfo = null)
        {
            var tagSize = fieldNumber < 16 ? 1 : 2; // Tag size (field number + wire type)
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            if (isPrimitive || IsPrimitiveType(typeName))
            {
                switch (typeName)
                {
                    case "string":
                    case "System.String":
                        // String: tag + length prefix (varint) + UTF-8 bytes
                        return $"(global::System.Text.Encoding.UTF8.GetByteCount({varName}) is var _strLen_{safeVarName}_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_strLen_{safeVarName}_{fieldNumber}) + _strLen_{safeVarName}_{fieldNumber} : 0)";
                    case "double":
                    case "System.Double":
                        return $"{tagSize} + 8";
                    case "float":
                    case "System.Single":
                        return $"{tagSize} + 4";
                    case "bool":
                    case "System.Boolean":
                        return $"{tagSize} + 1";
                    case "long":
                    case "System.Int64":
                        return $"{tagSize} + global::GProtobuf.Core.Utils.GetVarInt64Size({varName})";
                    case "ulong":
                    case "System.UInt64":
                        return $"{tagSize} + global::GProtobuf.Core.Utils.GetVarUInt64Size({varName})";
                    default:
                        // int, uint, short, ushort, byte, sbyte
                        return $"{tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})";
                }
            }
            else if (nestedInfo != null && (nestedInfo.Kind == StandaloneTypeKind.List || nestedInfo.Kind == StandaloneTypeKind.Array))
            {
                // Nested collection - calculate size inline
                return GetNestedCollectionSizeExpression(varName, fieldNumber, nestedInfo, tagSize);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Dictionary)
            {
                // Nested dictionary - calculate size inline
                return GetNestedDictionarySizeExpression(varName, fieldNumber, nestedInfo, tagSize);
            }
            else
            {
                var className = TypeNameHelper.GetClassName(typeName);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName);
                // Complex type: tag + length prefix (varint) + content
                return $"({sizeCalcClass}.Calculate{className}({varName}) is var _contentSize_{safeVarName}_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_contentSize_{safeVarName}_{fieldNumber}) + _contentSize_{safeVarName}_{fieldNumber} : 0)";
            }
        }

        private static string GetNestedCollectionSizeExpression(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize)
        {
            // For nested collections, we need to calculate the size of all elements
            // This returns a complex expression that calculates size at runtime
            var elementType = nestedInfo.ElementType!;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            if (nestedInfo.ElementIsPrimitive)
            {
                // For primitive elements, calculate size per element
                var elemWireType = GetWireType(elementType);
                var elemTagSize = 1; // field 1 always fits in 1 byte
                var elemSize = GetPrimitiveSizeExpression(elementType, "x");

                return $"({varName}.Sum(x => {elemTagSize} + {elemSize}) is var _listContentSize_{safeVarName}_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_listContentSize_{safeVarName}_{fieldNumber}) + _listContentSize_{safeVarName}_{fieldNumber} : 0)";
            }
            else
            {
                // For complex elements, use SizeCalculators
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType);
                return $"({varName}.Sum(x => {{ var s = {sizeCalcClass}.Calculate{className}(x); return 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)s) + s; }}) is var _listContentSize_{safeVarName}_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_listContentSize_{safeVarName}_{fieldNumber}) + _listContentSize_{safeVarName}_{fieldNumber} : 0)";
            }
        }

        private static string GetNestedDictionarySizeExpression(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize)
        {
            // For nested dictionaries, this is complex - use inline calculation
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            // Return a placeholder that will be calculated at runtime via the loop
            // This is a simplification - the actual size calculation happens in the write loop
            return $"(CalculateNestedDictSize_{safeVarName}_{fieldNumber}({varName}) is var _dictSize_{safeVarName}_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_dictSize_{safeVarName}_{fieldNumber}) + _dictSize_{safeVarName}_{fieldNumber} : 0)";
        }

        /// <summary>
        /// Gets the wire type for a given type.
        /// Delegates to TypeMapping.GetWireType and casts to int.
        /// </summary>
        private static int GetWireType(string typeName)
        {
            return (int)TypeMapping.GetWireType(typeName);
        }

        #endregion
    }
}
