using System.Collections.Generic;
using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;
using GProtobuf.Generator.WireFormat;

// Use existing type mapping and helper classes for primitive type handling
using static GProtobuf.Generator.WireFormat.TypeMapping;

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
        private readonly GeneratorOptions _options;

        public StandaloneTypeGenerator(StringBuilderWithIndent sb, TypeRegistry registry, GeneratorOptions options = null)
        {
            _sb = sb;
            _registry = registry;
            _options = options ?? GeneratorOptions.Default;
        }

        /// <summary>
        /// Returns the write method suffix: "" for derived types or simple types without callbacks,
        /// "Content" for types that still need WriteXContent (base types with ProtoIncludes, types with callbacks).
        /// </summary>
        private string GetWriteMethodSuffix(string typeName)
        {
            bool isDerivedType = _registry.IsDerivedType(typeName);
            if (isDerivedType) return "";
            var typeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(typeName));
            if (typeDef == null) return "Content"; // fallback for unknown types
            bool hasProtoIncludes = typeDef.ProtoIncludes != null && typeDef.ProtoIncludes.Count > 0;
            bool hasCallbacks = (typeDef.BeforeSerializationCallbacks != null && typeDef.BeforeSerializationCallbacks.Count > 0)
                || (typeDef.AfterSerializationCallbacks != null && typeDef.AfterSerializationCallbacks.Count > 0);
            return (!hasProtoIncludes && !hasCallbacks) ? "" : "Content";
        }

        #region Deserializers

        public void GenerateDeserializers(List<StandaloneTypeInfo> standaloneTypes)
        {
            // Only generate if SpanReader is enabled (standalone deserializers use SpanReader)
            if (!_options.GenerateSpanReader)
            {
                return;
            }

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
            var returnType = $"global::System.Collections.Generic.List<{TypeMapping.GetGlobalGenericTypeName(elementType)}>";
            GenerateCollectionDeserializerCore(info, methodName, returnType, "return list;");
        }

        private void GenerateArrayDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var returnType = $"{TypeMapping.GetGlobalGenericTypeName(elementType)}[]";
            GenerateCollectionDeserializerCore(info, methodName, returnType, "return list.ToArray();");
        }

        /// <summary>
        /// Shared implementation for List and Array deserializers.
        /// </summary>
        private void GenerateCollectionDeserializerCore(StandaloneTypeInfo info, string methodName, string returnType, string returnStatement)
        {
            var elementType = info.ElementType!;
            var listType = $"global::System.Collections.Generic.List<{TypeMapping.GetGlobalGenericTypeName(elementType)}>";

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

                if (info.ElementIsEnum)
                {
                    // Enums are serialized as varints (wire type 0)
                    _sb.AppendIndentedLine("if ((tag & 0x07) != 0) throw new InvalidDataException($\"Expected wire type 0 for enum, got {tag & 0x07}\");");
                    _sb.AppendIndentedLine($"list.Add((global::{elementType})reader.ReadVarInt32());");
                }
                else
                {
                    var expectedWireType = GetWireType(elementType);
                    _sb.AppendIndentedLine($"if ((tag & 0x07) != {expectedWireType}) throw new InvalidDataException($\"Expected wire type {expectedWireType}, got {{tag & 0x07}}\");");
                    var readExpr = GetPrimitiveReadExpression(elementType);
                    _sb.AppendIndentedLine($"list.Add({readExpr});");
                }
            }
            else
            {
                var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElementType))
                {
                    // ProtoVarint types: serialized as varints (wire type 0)
                    var varintType = _registry.GetProtoVarintType(normalizedElementType) ?? ProtoVarintType.UInt32;
                    var valueMember = _registry.GetProtoVarintValueMember(normalizedElementType);
                    _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine("if ((tag & 0x07) != 0) throw new InvalidDataException($\"Expected wire type 0 for ProtoVarint, got {tag & 0x07}\");");
                    var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType);
                    var globalTypeName = TypeMapping.GetGlobalGenericTypeName(elementType);
                    _sb.AppendIndentedLine($"list.Add(new {globalTypeName}(reader.{readMethod}()));");
                }
                else
                {
                    // Complex types: protobuf-net uses [tag=0x0A][length][message] for each item
                    _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine("if ((tag & 0x07) != 2) throw new InvalidDataException($\"Expected wire type 2, got {tag & 0x07}\");");
                    GenerateComplexElementRead(elementType, "list.Add");
                }
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine(returnStatement);
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

            // Check if element type has ProtoInclude inheritance
            // If so, we need to use Populate{ClassName} which handles the base class wrapper
            bool hasProtoIncludeInheritance = _registry.IsDerivedType(elementType);

            if (hasProtoIncludeInheritance)
            {
                // Type has ProtoInclude inheritance - need to use Populate which handles base class wrapper
                _sb.AppendIndentedLine($"var instance = new {TypeMapping.GetGlobalGenericTypeName(elementType)}();");
                _sb.AppendIndentedLine($"{spanReadersClass}.Populate{elementClassName}(ref subReader, {GeneratorHelpers.GetPopulateInstanceArgument(_registry, elementType, "instance")});");
                _sb.AppendIndentedLine($"{addMethod}(instance);");
            }
            else
            {
                // No inheritance - use ReadContent directly
                _sb.AppendIndentedLine($"{addMethod}({spanReadersClass}.Read{elementClassName}Content(ref subReader));");
            }
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
                returnType = $"global::{info.CustomDictionaryTypeName}<{TypeMapping.GetGlobalGenericTypeName(keyType)}, {TypeMapping.GetGlobalGenericTypeName(valueType)}>";
                dictInstantiation = returnType;
            }
            else
            {
                returnType = $"global::System.Collections.Generic.Dictionary<{TypeMapping.GetGlobalGenericTypeName(keyType)}, {TypeMapping.GetGlobalGenericTypeName(valueType)}>";
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
            _sb.AppendIndentedLine($"{TypeMapping.GetGlobalGenericTypeName(keyType)} key = default;");

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
        }

        private void GenerateValueInitialization(StandaloneTypeInfo info, string valueType)
        {
            // For nested collections (List, Array), initialize collection to accumulate items
            if (info.ValueKind == StandaloneTypeKind.List && info.NestedValueInfo != null)
            {
                var elementType = info.NestedValueInfo.ElementType!;
                var globalElementType = TypeMapping.GetGlobalGenericTypeName(elementType);
                _sb.AppendIndentedLine($"var value = new global::System.Collections.Generic.List<{globalElementType}>();");
            }
            else if (info.ValueKind == StandaloneTypeKind.Array && info.NestedValueInfo != null)
            {
                // Use temp list for arrays, convert to array after reading
                var elementType = info.NestedValueInfo.ElementType!;
                var globalElementType = TypeMapping.GetGlobalGenericTypeName(elementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{globalElementType}> _tempList_value = null;");
                _sb.AppendIndentedLine($"{TypeMapping.GetGlobalGenericTypeName(valueType)} value = null;");
            }
            else
            {
                _sb.AppendIndentedLine($"{TypeMapping.GetGlobalGenericTypeName(valueType)} value = default;");
            }
        }

        private void GenerateCollectionValueItemRead(string valueType, string varName, StandaloneTypeInfo nestedInfo)
        {
            // Each field 2 occurrence contains a single element of the collection
            var elementType = nestedInfo.ElementType!;

            if (nestedInfo.Kind == StandaloneTypeKind.Array)
            {
                // For arrays, accumulate in temp list
                var globalElementType = TypeMapping.GetGlobalGenericTypeName(elementType);
                _sb.AppendIndentedLine($"_tempList_{varName} ??= new global::System.Collections.Generic.List<{globalElementType}>();");

                if (nestedInfo.ElementIsPrimitive)
                {
                    if (nestedInfo.ElementIsEnum)
                    {
                        // Enum element - read as varint and cast
                        _sb.AppendIndentedLine($"_tempList_{varName}.Add((global::{elementType})reader.ReadVarInt32());");
                    }
                    else
                    {
                        var readExpr = GetPrimitiveReadExpression(elementType);
                        _sb.AppendIndentedLine($"_tempList_{varName}.Add({readExpr});");
                    }
                }
                else
                {
                    var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                    if (_registry.IsProtoVarint(normalizedElemType))
                    {
                        // ProtoVarint type - read as simple varint
                        var elemVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                        var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(elemVarintType);
                        var globalElementTypeProtoVarint = TypeMapping.GetGlobalGenericTypeName(elementType);
                        _sb.AppendIndentedLine($"_tempList_{varName}.Add(new {globalElementTypeProtoVarint}(reader.{readMethod}()));");
                    }
                    else
                    {
                        var className = TypeNameHelper.GetClassName(elementType);
                        var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                        _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                        _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                        // For derived types with ProtoInclude, use full Read method (handles wrapper)
                        // For non-derived types, use ReadContent method
                        bool isDerivedType = _registry.IsDerivedType(elementType);
                        var methodSuffix = isDerivedType ? "" : "Content";
                        _sb.AppendIndentedLine($"_tempList_{varName}.Add({spanReadersClass}.Read{className}{methodSuffix}(ref itemReader_{varName}));");
                    }
                }
            }
            else
            {
                // For lists, add directly
                if (nestedInfo.ElementIsPrimitive)
                {
                    if (nestedInfo.ElementIsEnum)
                    {
                        // Enum element - read as varint and cast
                        _sb.AppendIndentedLine($"{varName}.Add((global::{elementType})reader.ReadVarInt32());");
                    }
                    else
                    {
                        var readExpr = GetPrimitiveReadExpression(elementType);
                        _sb.AppendIndentedLine($"{varName}.Add({readExpr});");
                    }
                }
                else
                {
                    var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                    if (_registry.IsProtoVarint(normalizedElemType))
                    {
                        // ProtoVarint type - read as simple varint
                        var listElemVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                        var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(listElemVarintType);
                        var globalElementTypeProtoVarint = TypeMapping.GetGlobalGenericTypeName(elementType);
                        _sb.AppendIndentedLine($"{varName}.Add(new {globalElementTypeProtoVarint}(reader.{readMethod}()));");
                    }
                    else
                    {
                        var className = TypeNameHelper.GetClassName(elementType);
                        var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                        _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                        _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                        // For derived types with ProtoInclude, use full Read method (handles wrapper)
                        // For non-derived types, use ReadContent method
                        bool isDerivedType = _registry.IsDerivedType(elementType);
                        var methodSuffix = isDerivedType ? "" : "Content";
                        _sb.AppendIndentedLine($"{varName}.Add({spanReadersClass}.Read{className}{methodSuffix}(ref itemReader_{varName}));");
                    }
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
                var normalizedType = TypeMapping.NormalizeTypeName(typeName);
                if (_registry.IsProtoVarint(normalizedType))
                {
                    // ProtoVarint type - read as simple varint and construct via constructor
                    var varintType = _registry.GetProtoVarintType(normalizedType) ?? ProtoVarintType.UInt32;
                    var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType);
                    var globalTypeName = TypeMapping.GetGlobalGenericTypeName(typeName);
                    _sb.AppendIndentedLine($"{varName} = new {globalTypeName}(reader.{readMethod}());");
                }
                else
                {
                    // Custom type - read as length-delimited, create sub-reader
                    var className = TypeNameHelper.GetClassName(typeName);
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(typeName, _registry);
                    _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var subReader_{varName} = reader.CreateSubReader(len_{varName});");
                    // For derived types with ProtoInclude, use full Read method (handles wrapper)
                    // For non-derived types, use ReadContent method
                    bool isDerivedType = _registry.IsDerivedType(typeName);
                    var methodSuffix = isDerivedType ? "" : "Content";
                    _sb.AppendIndentedLine($"{varName} = {spanReadersClass}.Read{className}{methodSuffix}(ref subReader_{varName});");
                }
            }
        }

        private void GenerateNestedListRead(string listTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var elementType = nestedInfo.ElementType!;
            var globalElementType = TypeMapping.GetGlobalGenericTypeName(elementType);

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
                if (nestedInfo.ElementIsEnum)
                {
                    // Enum element - read as varint and cast
                    _sb.AppendIndentedLine($"{varName}.Add((global::{elementType})reader.ReadVarInt32());");
                }
                else
                {
                    // Primitive element
                    var readExpr = GetPrimitiveReadExpression(elementType);
                    _sb.AppendIndentedLine($"{varName}.Add({readExpr});");
                }
            }
            else
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElemType))
                {
                    // ProtoVarint type - read as simple varint
                    var nestedListVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                    var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(nestedListVarintType);
                    var globalElementTypeProtoVarint = TypeMapping.GetGlobalGenericTypeName(elementType);
                    _sb.AppendIndentedLine($"{varName}.Add(new {globalElementTypeProtoVarint}(reader.{readMethod}()));");
                }
                else
                {
                    // Complex element - read length-delimited
                    var className = TypeNameHelper.GetClassName(elementType);
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                    _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                    // For derived types with ProtoInclude, use full Read method (handles wrapper)
                    // For non-derived types, use ReadContent method
                    bool isDerivedType = _registry.IsDerivedType(elementType);
                    var methodSuffix = isDerivedType ? "" : "Content";
                    _sb.AppendIndentedLine($"{varName}.Add({spanReadersClass}.Read{className}{methodSuffix}(ref itemReader_{varName}));");
                }
            }

            _sb.EndBlock();
        }

        private void GenerateNestedArrayRead(string arrayTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var elementType = nestedInfo.ElementType!;
            var globalElementType = TypeMapping.GetGlobalGenericTypeName(elementType);

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
                if (nestedInfo.ElementIsEnum)
                {
                    // Enum element - read as varint and cast
                    _sb.AppendIndentedLine($"tempList_{varName}.Add((global::{elementType})reader.ReadVarInt32());");
                }
                else
                {
                    // Primitive element
                    var readExpr = GetPrimitiveReadExpression(elementType);
                    _sb.AppendIndentedLine($"tempList_{varName}.Add({readExpr});");
                }
            }
            else
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElemType))
                {
                    // ProtoVarint type - read as simple varint
                    var arrayElemVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                    var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(arrayElemVarintType);
                    var globalElementTypeProtoVarint = TypeMapping.GetGlobalGenericTypeName(elementType);
                    _sb.AppendIndentedLine($"tempList_{varName}.Add(new {globalElementTypeProtoVarint}(reader.{readMethod}()));");
                }
                else
                {
                    // Complex element - read length-delimited
                    var className = TypeNameHelper.GetClassName(elementType);
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType, _registry);
                    _sb.AppendIndentedLine($"var itemLen_{varName} = (int)reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var itemReader_{varName} = reader.CreateSubReader(itemLen_{varName});");
                    // For derived types with ProtoInclude, use full Read method (handles wrapper)
                    // For non-derived types, use ReadContent method
                    bool isDerivedType = _registry.IsDerivedType(elementType);
                    var methodSuffix = isDerivedType ? "" : "Content";
                    _sb.AppendIndentedLine($"tempList_{varName}.Add({spanReadersClass}.Read{className}{methodSuffix}(ref itemReader_{varName}));");
                }
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine($"{varName} = tempList_{varName}.ToArray();");
        }

        private void GenerateNestedDictionaryRead(string dictTypeName, string varName, StandaloneTypeInfo nestedInfo)
        {
            var keyType = nestedInfo.KeyType!;
            var valueType = nestedInfo.ValueType!;
            var globalKeyType = TypeMapping.GetGlobalGenericTypeName(keyType);
            var globalValueType = TypeMapping.GetGlobalGenericTypeName(valueType);

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
            // Only generate if at least one writer is enabled
            if (!_options.GenerateStreamWriter && !_options.GenerateBufferWriter)
            {
                return;
            }

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
            var paramType = $"global::System.Collections.Generic.List<{TypeMapping.GetGlobalGenericTypeName(elementType)}>";
            GenerateCollectionSerializerCore(info, methodName, paramType, "list");
        }

        private void GenerateArraySerializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var paramType = $"{TypeMapping.GetGlobalGenericTypeName(elementType)}[]";
            GenerateCollectionSerializerCore(info, methodName, paramType, "array");
        }

        /// <summary>
        /// Shared implementation for List and Array serializers.
        /// </summary>
        private void GenerateCollectionSerializerCore(StandaloneTypeInfo info, string methodName, string paramType, string varName)
        {
            var elementType = info.ElementType!;

            // Stream serializer
            if (_options.GenerateStreamWriter)
            {
                _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} {varName})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"if ({varName} == null) return;");
                _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
                _sb.AppendIndentedLine($"foreach (var item in {varName})");
                _sb.StartNewBlock();
                GenerateElementWrite(elementType, info.ElementIsPrimitive, info.ElementIsEnum, "item", "writer", false);
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.Flush();");
                _sb.EndBlock();
                _sb.AppendNewLine();
            }

            // IBufferWriter serializer
            if (_options.GenerateBufferWriter)
            {
                _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} {varName})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"if ({varName} == null) return;");
                _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
                _sb.AppendIndentedLine($"foreach (var item in {varName})");
                _sb.StartNewBlock();
                GenerateElementWrite(elementType, info.ElementIsPrimitive, info.ElementIsEnum, "item", "writer", true);
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.Flush();");
                _sb.EndBlock();
                _sb.AppendNewLine();
            }
        }

        private void GenerateElementWrite(string elementType, bool isPrimitive, bool isEnum, string varName, string writerName, bool isBufferWriter)
        {
            if (isPrimitive)
            {
                if (isEnum)
                {
                    // Enums: [tag (wire type 0)][varint value]
                    var tag = (1 << 3) | 0; // field 1 + wire type 0 (varint)
                    _sb.AppendIndentedLine($"{writerName}.WriteSingleByte(0x{tag:X2}); // field 1, wire type 0 (varint for enum)");
                    _sb.AppendIndentedLine($"{writerName}.WriteVarInt32((int){varName});");
                }
                else
                {
                    // Primitive types: [tag][value] for each element (unpacked format)
                    var wireType = GetWireType(elementType);
                    var tag = (1 << 3) | wireType; // field 1 + wire type
                    _sb.AppendIndentedLine($"{writerName}.WriteSingleByte(0x{tag:X2}); // field 1, wire type {wireType}");

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
            }
            else
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElemType))
                {
                    // ProtoVarint types use wire type 0 (varint)
                    // tag 0x08 = field 1, wire type 0 (varint)
                    var elemWriteVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                    var elemWriteValueMember = _registry.GetProtoVarintValueMember(normalizedElemType);
                    _sb.AppendIndentedLine($"{writerName}.WriteSingleByte(0x08); // field 1, wire type 0 (varint for ProtoVarint)");
                    var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(elemWriteVarintType);
                    _sb.AppendIndentedLine($"{writerName}.{writeMethod}({varName}.{elemWriteValueMember});");
                }
                else
                {
                    // Complex types: [tag=0x0A][length][message] for each item
                    // tag 0x0A = field 1, wire type 2 (length-delimited)
                    _sb.AppendIndentedLine($"{writerName}.WriteSingleByte(0x0A); // field 1, wire type 2");
                    var className = TypeNameHelper.GetClassName(elementType);
                    var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                    var writerClass = NamespaceHelper.GetWritersClass(elementType, isBufferWriter ? "BufferWriters" : "StreamWriters", _registry);
                    var methodSuffix = GetWriteMethodSuffix(elementType);
                    var sizeSuffix = "ContentSize";
                    _sb.AppendIndentedLine("var sizeCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref sizeCalc, {varName});");
                    _sb.AppendIndentedLine($"{writerName}.WriteVarUInt32((uint)sizeCalc.Length);");
                    _sb.AppendIndentedLine($"{writerClass}.Write{className}{methodSuffix}(ref {writerName}, {varName});");
                }
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
                paramType = $"global::System.Collections.Generic.IDictionary<{TypeMapping.GetGlobalGenericTypeName(keyType)}, {TypeMapping.GetGlobalGenericTypeName(valueType)}>";
            }
            else
            {
                paramType = $"global::System.Collections.Generic.Dictionary<{TypeMapping.GetGlobalGenericTypeName(keyType)}, {TypeMapping.GetGlobalGenericTypeName(valueType)}>";
            }

            // Stream serializer
            if (_options.GenerateStreamWriter)
            {
                _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} dict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("if (dict == null) return;");
                _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
                _sb.AppendIndentedLine("foreach (var kvp in dict)");
                _sb.StartNewBlock();
                GenerateMapEntryWrite(info, "StreamWriters");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.Flush();");
                _sb.EndBlock();
                _sb.AppendNewLine();
            }

            // IBufferWriter serializer
            if (_options.GenerateBufferWriter)
            {
                _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} dict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("if (dict == null) return;");
                _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
                _sb.AppendIndentedLine("foreach (var kvp in dict)");
                _sb.StartNewBlock();
                GenerateMapEntryWrite(info, "BufferWriters");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.Flush();");
                _sb.EndBlock();
                _sb.AppendNewLine();
            }
        }

        private void GenerateMapEntryWrite(StandaloneTypeInfo info, string writerClassName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;

            var isArrayOrListValue = info.NestedValueInfo != null &&
                (info.NestedValueInfo.Kind == StandaloneTypeKind.List ||
                 info.NestedValueInfo.Kind == StandaloneTypeKind.Array);

            // protobuf-net format: [tag=0x0A][length][entry] for each map entry
            // Write outer tag first
            _sb.AppendIndentedLine("writer.WriteSingleByte(0x0A); // field 1, wire type 2 (map entry)");

            // Calculate entry size
            _sb.AppendIndentedLine("// Calculate entry size");
            GenerateSizeCalculation(keyType, "kvp.Key", 1, info.KeyIsPrimitive, null, "keySize");

            if (isArrayOrListValue)
            {
                GenerateRepeatedFieldArraySizeCalculation("kvp.Value", 2, info.NestedValueInfo!, "valueSize");
            }
            else
            {
                GenerateSizeCalculation(valueType, "kvp.Value", 2, info.ValueIsPrimitive, info.NestedValueInfo, "valueSize");
            }
            _sb.AppendIndentedLine("var entrySize = keySize + valueSize;");
            _sb.AppendNewLine();

            // Write entry length
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entrySize);");

            // Write key (field 1)
            _sb.AppendIndentedLine("// Key (field 1)");
            GenerateTaggedFieldWrite(keyType, "kvp.Key", 1, info.KeyIsPrimitive, null, writerClassName);

            // Write value (field 2) - only if not null (for reference types)
            _sb.AppendIndentedLine("// Value (field 2)");

            if (isArrayOrListValue)
            {
                _sb.AppendIndentedLine("if (kvp.Value != null)");
                _sb.StartNewBlock();
                GenerateRepeatedFieldArrayWrite("kvp.Value", 2, info.NestedValueInfo!, writerClassName);
                _sb.EndBlock();
            }
            else
            {
                var isCollectionValue = info.NestedValueInfo != null &&
                    info.NestedValueInfo.Kind == StandaloneTypeKind.Dictionary;

                if (isCollectionValue)
                {
                    _sb.AppendIndentedLine("if (kvp.Value != null)");
                    _sb.StartNewBlock();
                    GenerateTaggedFieldWrite(valueType, "kvp.Value", 2, info.ValueIsPrimitive, info.NestedValueInfo, writerClassName);
                    _sb.EndBlock();
                }
                else
                {
                    GenerateTaggedFieldWrite(valueType, "kvp.Value", 2, info.ValueIsPrimitive, info.NestedValueInfo, writerClassName);
                }
            }
        }

        /// <summary>
        /// Generate size calculation for array/list as repeated field entries (for dictionary values).
        /// </summary>
        private void GenerateRepeatedFieldArraySizeCalculation(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string resultVarName)
        {
            var elementType = nestedInfo.ElementType!;
            var tagSize = fieldNumber < 16 ? 1 : 2;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            // Initialize size to 0, then calculate only if not null
            _sb.AppendIndentedLine($"var {resultVarName} = 0;");
            _sb.AppendIndentedLine($"if ({varName} != null)");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                if (nestedInfo.ElementIsEnum)
                {
                    // For enum elements: each is field_tag + varint
                    _sb.AppendIndentedLine($"foreach (var _elem_{safeVarName}_{fieldNumber} in {varName})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{resultVarName} += {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)(int)_elem_{safeVarName}_{fieldNumber});");
                    _sb.EndBlock();
                }
                else
                {
                    // For primitive elements: each is field_tag + value
                    var elemSize = GetPrimitiveSizeExpression(elementType, $"_elem_{safeVarName}_{fieldNumber}");
                    _sb.AppendIndentedLine($"foreach (var _elem_{safeVarName}_{fieldNumber} in {varName})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{resultVarName} += {tagSize} + {elemSize};");
                    _sb.EndBlock();
                }
            }
            else
            {
                // For complex elements: each is field_tag + length_prefix + content
                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                bool isDerivedType = _registry.IsDerivedType(elementType);
                var sizeSuffix = "ContentSize";

                _sb.AppendIndentedLine($"foreach (var _elem_{safeVarName}_{fieldNumber} in {varName})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"var _elemCalc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref _elemCalc_{safeVarName}_{fieldNumber}, _elem_{safeVarName}_{fieldNumber});");
                _sb.AppendIndentedLine($"{resultVarName} += {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_elemCalc_{safeVarName}_{fieldNumber}.Length) + _elemCalc_{safeVarName}_{fieldNumber}.Length;");
                _sb.EndBlock();
            }

            _sb.EndBlock(); // end if (varName != null)
        }

        /// <summary>
        /// Generate write code for array/list as repeated field entries (for dictionary values).
        /// </summary>
        private void GenerateRepeatedFieldArrayWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName)
        {
            var elementType = nestedInfo.ElementType!;
            var wireType = 2; // length-delimited for complex types, but varies for primitives
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            _sb.AppendIndentedLine($"foreach (var _elem_{safeVarName}_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                if (nestedInfo.ElementIsEnum)
                {
                    // Enum: field_tag (wire type 0) + varint value
                    var tag = (fieldNumber << 3) | 0;
                    TagCodeHelper.WriteTagValue(_sb, tag);
                    _sb.AppendIndentedLine($"writer.WriteVarInt32((int)_elem_{safeVarName}_{fieldNumber});");
                }
                else
                {
                    // Primitive: field_tag + value
                    wireType = GetWireType(elementType);
                    var tag = (fieldNumber << 3) | wireType;
                    TagCodeHelper.WriteTagValue(_sb, tag);

                    if (elementType == "string" || elementType == "System.String")
                    {
                        _sb.AppendIndentedLine($"writer.WriteString(_elem_{safeVarName}_{fieldNumber});");
                    }
                    else
                    {
                        var writeExpr = GetPrimitiveWriteExpression(elementType, $"_elem_{safeVarName}_{fieldNumber}");
                        _sb.AppendIndentedLine($"writer.{writeExpr};");
                    }
                }
            }
            else
            {
                // Complex type: field_tag (wire type 2) + length + content
                var tag = (fieldNumber << 3) | 2;
                TagCodeHelper.WriteTagValue(_sb, tag);

                var className = TypeNameHelper.GetClassName(elementType);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                var writerClass = NamespaceHelper.GetWritersClass(elementType, writerClassName, _registry);
                var methodSuffix = GetWriteMethodSuffix(elementType);
                var sizeSuffix = "ContentSize";

                _sb.AppendIndentedLine($"var _elemWriteCalc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref _elemWriteCalc_{safeVarName}_{fieldNumber}, _elem_{safeVarName}_{fieldNumber});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)_elemWriteCalc_{safeVarName}_{fieldNumber}.Length);");
                _sb.AppendIndentedLine($"{writerClass}.Write{className}{methodSuffix}(ref writer, _elem_{safeVarName}_{fieldNumber});");
            }

            _sb.EndBlock();
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
                // Nested collection (always reference type) - generate loop for size calculation with null check
                GenerateNestedCollectionSizeCalculation(varName, fieldNumber, nestedInfo, tagSize, resultVarName);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Dictionary)
            {
                // Nested dictionary (always reference type) - generate loop with null check
                GenerateNestedDictionarySizeCalculation(varName, fieldNumber, nestedInfo, tagSize, resultVarName);
            }
            else
            {
                var normalizedType = TypeMapping.NormalizeTypeName(typeName);
                if (_registry.IsProtoVarint(normalizedType))
                {
                    // ProtoVarint types are written directly as varints (no length prefix), so just wire type 0
                    var sizeCalcVarintType = _registry.GetProtoVarintType(normalizedType) ?? ProtoVarintType.UInt32;
                    var sizeCalcValueMember = _registry.GetProtoVarintValueMember(normalizedType);
                    var sizeExpr = PrimitiveTypeCodeGenerator.GetProtoVarintSizeExpression(sizeCalcVarintType, $"{varName}.{sizeCalcValueMember}");
                    _sb.AppendIndentedLine($"var {resultVarName} = {tagSize} + {sizeExpr};");
                }
                else
                {
                    // Custom type (class or struct) - no null check, let it fail naturally if null class is passed
                    var className = TypeNameHelper.GetClassName(typeName);
                    var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName, _registry);
                    var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");
                    // For derived types with ProtoInclude, use full Calculate method (handles wrapper)
                    // For non-derived types, use CalculateContentSize method
                    bool isDerivedType = _registry.IsDerivedType(typeName);
                    var sizeSuffix = "ContentSize";
                    _sb.AppendIndentedLine($"var _calc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref _calc_{safeVarName}_{fieldNumber}, {varName});");
                    _sb.AppendIndentedLine($"var {resultVarName} = {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_calc_{safeVarName}_{fieldNumber}.Length) + _calc_{safeVarName}_{fieldNumber}.Length;");
                }
            }
        }

        private void GenerateNestedCollectionSizeCalculation(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize, string resultVarName)
        {
            var elementType = nestedInfo.ElementType!;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            // Initialize size to 0, then calculate only if not null
            _sb.AppendIndentedLine($"var _listContentSize_{safeVarName}_{fieldNumber} = 0;");
            _sb.AppendIndentedLine($"if ({varName} != null)");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                if (nestedInfo.ElementIsEnum)
                {
                    // For enum elements, calculate varint size
                    _sb.AppendIndentedLine($"_listContentSize_{safeVarName}_{fieldNumber} = {varName}.Sum(x => 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)(int)x));");
                }
                else
                {
                    // For primitive elements, use LINQ (no ref params needed)
                    var elemSize = GetPrimitiveSizeExpression(elementType, "x");
                    _sb.AppendIndentedLine($"_listContentSize_{safeVarName}_{fieldNumber} = {varName}.Sum(x => 1 + {elemSize});");
                }
            }
            else
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElemType))
                {
                    // ProtoVarint elements: [tag=varint][varint value]
                    var nestedCollVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                    var nestedCollValueMember = _registry.GetProtoVarintValueMember(normalizedElemType);
                    var sizeExpr = PrimitiveTypeCodeGenerator.GetProtoVarintSizeExpression(nestedCollVarintType, $"x.{nestedCollValueMember}");
                    _sb.AppendIndentedLine($"_listContentSize_{safeVarName}_{fieldNumber} = {varName}.Sum(x => 1 + {sizeExpr});");
                }
                else
                {
                    // For complex elements, generate loop with WriteSizeCalculator
                    var className = TypeNameHelper.GetClassName(elementType);
                    var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                    // For derived types with ProtoInclude, use full Calculate method (handles wrapper)
                    // For non-derived types, use CalculateContentSize method
                    bool isDerivedType = _registry.IsDerivedType(elementType);
                    var sizeSuffix = "ContentSize";
                    _sb.AppendIndentedLine($"foreach (var _item_{safeVarName}_{fieldNumber} in {varName})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"var _itemCalc_{safeVarName}_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref _itemCalc_{safeVarName}_{fieldNumber}, _item_{safeVarName}_{fieldNumber});");
                    _sb.AppendIndentedLine($"_listContentSize_{safeVarName}_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)_itemCalc_{safeVarName}_{fieldNumber}.Length) + _itemCalc_{safeVarName}_{fieldNumber}.Length;");
                    _sb.EndBlock();
                }
            }

            _sb.EndBlock(); // end if (varName != null)
            // If null, resultVarName is 0 (field not written); otherwise include tag + length prefix + content
            _sb.AppendIndentedLine($"var {resultVarName} = {varName} != null ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_listContentSize_{safeVarName}_{fieldNumber}) + _listContentSize_{safeVarName}_{fieldNumber} : 0;");
        }

        private void GenerateNestedDictionarySizeCalculation(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, int tagSize, string resultVarName)
        {
            var keyType = nestedInfo.KeyType!;
            var valueType = nestedInfo.ValueType!;
            var safeVarName = varName.Replace(".", "_").Replace("[", "_").Replace("]", "_");

            // Initialize size to 0, then calculate only if not null
            _sb.AppendIndentedLine($"var _dictContentSize_{safeVarName}_{fieldNumber} = 0;");
            _sb.AppendIndentedLine($"if ({varName} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var _kvp_{safeVarName}_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            // Calculate key size
            GenerateSizeCalculation(keyType, $"_kvp_{safeVarName}_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, $"_innerKeySize_{safeVarName}_{fieldNumber}");
            // Calculate value size
            GenerateSizeCalculation(valueType, $"_kvp_{safeVarName}_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, $"_innerValueSize_{safeVarName}_{fieldNumber}");

            _sb.AppendIndentedLine($"var _innerEntrySize_{safeVarName}_{fieldNumber} = _innerKeySize_{safeVarName}_{fieldNumber} + _innerValueSize_{safeVarName}_{fieldNumber};");
            _sb.AppendIndentedLine($"_dictContentSize_{safeVarName}_{fieldNumber} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)_innerEntrySize_{safeVarName}_{fieldNumber}) + _innerEntrySize_{safeVarName}_{fieldNumber};");
            _sb.EndBlock(); // end foreach
            _sb.EndBlock(); // end if (varName != null)
            // If null, resultVarName is 0 (field not written); otherwise include tag + length prefix + content
            _sb.AppendIndentedLine($"var {resultVarName} = {varName} != null ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_dictContentSize_{safeVarName}_{fieldNumber}) + _dictContentSize_{safeVarName}_{fieldNumber} : 0;");
        }

        private void GenerateTaggedFieldWrite(string typeName, string varName, int fieldNumber, bool isPrimitive, StandaloneTypeInfo? nestedInfo, string writerClassName, string? precalculatedListSizeVar = null)
        {
            // Check for ProtoVarint types first - they use wire type 0 (varint), not 2 (length-delimited)
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            if (_registry.IsProtoVarint(normalizedType))
            {
                // ProtoVarint types use wire type 0 (varint)
                var taggedVarintType = _registry.GetProtoVarintType(normalizedType) ?? ProtoVarintType.UInt32;
                var taggedValueMember = _registry.GetProtoVarintValueMember(normalizedType);
                var tag = (fieldNumber << 3) | 0; // wire type 0 = varint
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(taggedVarintType);
                TagCodeHelper.WriteTagValue(_sb, tag);
                _sb.AppendIndentedLine($"writer.{writeMethod}({varName}.{taggedValueMember});");
                return;
            }

            var wireType = GetWireType(typeName);
            var tagNormal = (fieldNumber << 3) | wireType;

            TagCodeHelper.WriteTagValue(_sb, tagNormal);

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
                GenerateNestedListWrite(varName, fieldNumber, nestedInfo, writerClassName, precalculatedListSizeVar);
            }
            else if (nestedInfo != null && nestedInfo.Kind == StandaloneTypeKind.Array)
            {
                // Nested T[] - calculate and write array content
                GenerateNestedArrayWrite(varName, fieldNumber, nestedInfo, writerClassName, precalculatedListSizeVar);
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
                var methodSuffix = GetWriteMethodSuffix(typeName);
                var sizeSuffix = "ContentSize";
                _sb.AppendIndentedLine($"var sizeCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref sizeCalc_{fieldNumber}, {varName});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)sizeCalc_{fieldNumber}.Length);");
                _sb.AppendIndentedLine($"{writerClass}.Write{className}{methodSuffix}(ref writer, {varName});");
            }
        }

        private void GenerateNestedListWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName, string? precalculatedListSizeVar = null)
        {
            var elementType = nestedInfo.ElementType!;
            string listSizeVar;

            if (precalculatedListSizeVar != null)
            {
                // Reuse pre-calculated size variable from GenerateSizeCalculation
                listSizeVar = precalculatedListSizeVar;
            }
            else
            {
                // Calculate list size (only when not pre-calculated)
                listSizeVar = $"listSize_{fieldNumber}";
                _sb.AppendIndentedLine($"var {listSizeVar} = 0;");
                _sb.AppendIndentedLine($"foreach (var item_{fieldNumber} in {varName})");
                _sb.StartNewBlock();

                if (nestedInfo.ElementIsPrimitive)
                {
                    if (nestedInfo.ElementIsEnum)
                    {
                        // Enum element - varint size
                        _sb.AppendIndentedLine($"{listSizeVar} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)(int)item_{fieldNumber});");
                    }
                    else
                    {
                        var wireType = GetWireType(elementType);
                        var itemTag = (1 << 3) | wireType;
                        _sb.AppendIndentedLine($"{listSizeVar} += 1 + {GetPrimitiveSizeExpression(elementType, $"item_{fieldNumber}")};");
                    }
                }
                else
                {
                    var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                    if (_registry.IsProtoVarint(normalizedElemType))
                    {
                        // ProtoVarint elements: [tag=varint][varint value]
                        var listSizeVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                        var listSizeValueMember = _registry.GetProtoVarintValueMember(normalizedElemType);
                        var sizeExpr = PrimitiveTypeCodeGenerator.GetProtoVarintSizeExpression(listSizeVarintType, $"item_{fieldNumber}.{listSizeValueMember}");
                        _sb.AppendIndentedLine($"{listSizeVar} += 1 + {sizeExpr};");
                    }
                    else
                    {
                        var className = TypeNameHelper.GetClassName(elementType);
                        var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                        // For derived types with ProtoInclude, use full Calculate method (handles wrapper)
                        // For non-derived types, use CalculateContentSize method
                        bool isDerivedType = _registry.IsDerivedType(elementType);
                        var sizeSuffix = "ContentSize";
                        _sb.AppendIndentedLine($"var itemCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref itemCalc_{fieldNumber}, item_{fieldNumber});");
                        _sb.AppendIndentedLine($"{listSizeVar} += 1 + global::GProtobuf.Core.Utils.GetVarintSize((uint)itemCalc_{fieldNumber}.Length) + itemCalc_{fieldNumber}.Length;");
                    }
                }
                _sb.EndBlock();
            }

            // Write list length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){listSizeVar});");

            // Write each element
            _sb.AppendIndentedLine($"foreach (var item_{fieldNumber} in {varName})");
            _sb.StartNewBlock();

            if (nestedInfo.ElementIsPrimitive)
            {
                if (nestedInfo.ElementIsEnum)
                {
                    // Enum element - write as varint
                    _sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, wire type 0 (varint for enum)");
                    _sb.AppendIndentedLine($"writer.WriteVarInt32((int)item_{fieldNumber});");
                }
                else
                {
                    var wireType = GetWireType(elementType);
                    var itemTag = (1 << 3) | wireType;
                    _sb.AppendIndentedLine($"writer.WriteSingleByte(0x{itemTag:X2}); // field 1, wire type {wireType}");

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
            }
            else
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                if (_registry.IsProtoVarint(normalizedElemType))
                {
                    // ProtoVarint elements: [tag=varint][varint value]
                    var listWriteVarintType = _registry.GetProtoVarintType(normalizedElemType) ?? ProtoVarintType.UInt32;
                    var listWriteValueMember = _registry.GetProtoVarintValueMember(normalizedElemType);
                    _sb.AppendIndentedLine("writer.WriteSingleByte(0x08); // field 1, wire type 0 (varint for ProtoVarint)");
                    var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(listWriteVarintType);
                    _sb.AppendIndentedLine($"writer.{writeMethod}(item_{fieldNumber}.{listWriteValueMember});");
                }
                else
                {
                    var className = TypeNameHelper.GetClassName(elementType);
                    var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(elementType, _registry);
                    var writerClass = NamespaceHelper.GetWritersClass(elementType, writerClassName, _registry);
                    var methodSuffix = GetWriteMethodSuffix(elementType);
                    var sizeSuffix = "ContentSize";
                    _sb.AppendIndentedLine("writer.WriteSingleByte(0x0A); // field 1, wire type 2");
                    _sb.AppendIndentedLine($"var itemWriteCalc_{fieldNumber} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{className}{sizeSuffix}(ref itemWriteCalc_{fieldNumber}, item_{fieldNumber});");
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)itemWriteCalc_{fieldNumber}.Length);");
                    _sb.AppendIndentedLine($"{writerClass}.Write{className}{methodSuffix}(ref writer, item_{fieldNumber});");
                }
            }
            _sb.EndBlock();
        }

        private void GenerateNestedArrayWrite(string varName, int fieldNumber, StandaloneTypeInfo nestedInfo, string writerClassName, string? precalculatedListSizeVar = null)
        {
            // Arrays use the same format as Lists
            GenerateNestedListWrite(varName, fieldNumber, nestedInfo, writerClassName, precalculatedListSizeVar);
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
            _sb.AppendIndentedLine("writer.WriteSingleByte(0x0A); // entry tag");

            // Calculate and write entry size
            GenerateSizeCalculation(keyType, $"innerKvp_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, $"innerKeySize2_{fieldNumber}");
            GenerateSizeCalculation(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, $"innerValueSize2_{fieldNumber}");
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)(innerKeySize2_{fieldNumber} + innerValueSize2_{fieldNumber}));");

            // Write key (always)
            GenerateTaggedFieldWrite(keyType, $"innerKvp_{fieldNumber}.Key", 1, nestedInfo.KeyIsPrimitive, null, writerClassName);

            // Only add null check for collection types (List, Array, Dictionary) which are always reference types
            var isCollectionValue = nestedInfo.NestedValueInfo != null &&
                (nestedInfo.NestedValueInfo.Kind == StandaloneTypeKind.List ||
                 nestedInfo.NestedValueInfo.Kind == StandaloneTypeKind.Array ||
                 nestedInfo.NestedValueInfo.Kind == StandaloneTypeKind.Dictionary);

            if (isCollectionValue)
            {
                _sb.AppendIndentedLine($"if (innerKvp_{fieldNumber}.Value != null)");
                _sb.StartNewBlock();
                GenerateTaggedFieldWrite(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, writerClassName);
                _sb.EndBlock();
            }
            else
            {
                GenerateTaggedFieldWrite(valueType, $"innerKvp_{fieldNumber}.Value", 2, nestedInfo.ValueIsPrimitive, nestedInfo.NestedValueInfo, writerClassName);
            }
            _sb.EndBlock();
        }

        #endregion

        #region Helpers

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
