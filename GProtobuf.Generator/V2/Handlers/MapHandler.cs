using System;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Handles code generation for map/dictionary types in protobuf.
    /// Maps are serialized as repeated length-delimited messages with key (field 1) and value (field 2).
    ///
    /// For complex types (custom classes as keys, nested collections), uses VirtualMapTypeRegistry
    /// to generate reusable virtual type serializers.
    /// </summary>
    internal class MapHandler
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly VirtualMapTypeRegistry _registry;
        private readonly string _writerClassName;
        private readonly TypeRegistry _typeRegistry;


        public MapHandler(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry)
            : this(sb, registry, "StreamWriters", null)
        {
        }

        public MapHandler(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry, string writerClassName)
            : this(sb, registry, writerClassName, null)
        {
        }

        public MapHandler(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry, string writerClassName, TypeRegistry typeRegistry)
        {
            _sb = sb;
            _registry = registry;
            _writerClassName = writerClassName ?? "StreamWriters";
            _typeRegistry = typeRegistry;
        }

        /// <summary>
        /// Checks if a map type requires virtual type generation.
        /// NEW IMPLEMENTATION: Always use KeyValue classes for ALL dictionaries.
        /// </summary>
        public static bool RequiresVirtualType(string keyType, string valueType)
        {
            // Always use KeyValue classes for all dictionaries
            return true;
        }

        /// <summary>
        /// Checks if a dictionary value type needs null checking.
        /// Returns true if null check should be generated, false otherwise.
        /// </summary>
        private bool NeedsNullCheck(string valueType, bool isEnum = false)
        {
            // Enums are value types, never need null check
            if (isEnum)
                return false;

            // Primitives and arrays never need null check
            if (TypeHelper.IsPrimitiveType(valueType) || valueType.EndsWith("[]"))
                return false;

            // If we have TypeRegistry, check if it's a non-nullable struct or enum
            if (_typeRegistry != null)
            {
                var typeDef = _typeRegistry.GetByFullName(valueType);
                if (typeDef != null && typeDef.IsStruct)
                {
                    // Non-nullable struct doesn't need null check
                    return false;
                }

                // Check if it's an enum type
                if (_typeRegistry.IsEnum(valueType))
                {
                    return false;
                }
            }

            // Reference types or nullable types need null check
            return true;
        }

        /// <summary>
        /// Checks if a type is complex (requires virtual type handling).
        /// </summary>
        private static bool IsComplexType(string typeName)
        {
            // Simple primitives are not complex
            if (TypeMapping.IsSimpleType(typeName))
                return false;

            var normalized = TypeMapping.NormalizeTypeName(typeName);

            // String and Guid are handled inline
            if (normalized == "System.String" || normalized == "System.Guid")
                return false;

            // Simple arrays of primitives are handled inline
            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);
                return !TypeMapping.IsSimpleType(elementType) &&
                       TypeMapping.NormalizeTypeName(elementType) != "System.String";
            }

            // Nested dictionaries are complex
            if (typeName.Contains("Dictionary<") || typeName.Contains("IDictionary<"))
                return true;

            // Collections are always complex (inline MapHandler doesn't support them as keys/values)
            if (typeName.Contains("List<") || typeName.Contains("HashSet<"))
                return true;

            // Custom classes are complex
            return true;
        }

        /// <summary>
        /// Registers the map type in the virtual type registry.
        /// Always registers since we use KeyValue classes for all dictionaries.
        /// </summary>
        public VirtualMapEntryInfo RegisterIfNeeded(ProtoMemberAttribute member)
        {
            if (_registry == null)
                return null;

            var keyType = member.MapKeyType;
            var valueType = member.MapValueType;

            // Always register - we use KeyValue classes for all dictionaries now
            return _registry.RegisterMapEntry(
                keyType, valueType,
                member.MapKeyIsEnum, member.MapValueIsEnum,
                member.MapKeyEnumUnderlyingType, member.MapValueEnumUnderlyingType);
        }

        #region Read (Deserialization)

        /// <summary>
        /// Generates code to read a map field.
        /// Uses virtual type methods for complex types, inline code for simple types.
        /// </summary>
        public void GenerateRead(ProtoMemberAttribute member, string targetVar, string readerVar = "reader")
        {
            var keyType = member.MapKeyType;
            var valueType = member.MapValueType;
            var dictCreationType = TypeHelper.GetDictionaryCreationType(member.Type, keyType, valueType);

            // Check if we should use virtual type
            var virtualInfo = RegisterIfNeeded(member);

            if (virtualInfo != null)
            {
                // Use virtual type reader
                GenerateVirtualTypeRead(targetVar, virtualInfo, member, dictCreationType, readerVar);
            }
            else
            {
                // Use inline reading for simple types
                GenerateInlineRead(targetVar, keyType, valueType, member, dictCreationType, readerVar);
            }
        }

        /// <summary>
        /// Generates code that calls ReadMapEntry directly without creating intermediate KeyValue object.
        /// ReadMapEntry returns (bool success, TKey key, TValue value) tuple.
        /// </summary>
        private void GenerateVirtualTypeRead(string targetVar, VirtualMapEntryInfo virtualInfo, ProtoMemberAttribute member, string dictCreationType, string readerVar)
        {
            var mapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(virtualInfo.KeyType, virtualInfo.ValueType);

            // Call ReadMapEntry - generated in current namespace
            _sb.AppendIndentedLine($"var entry = {_writerClassName}.Read{mapEntryTypeName}(ref {readerVar});");
            _sb.AppendIndentedLine($"if (entry.success)");
            _sb.StartNewBlock();

            // Initialize dictionary AFTER reading data, only if we have valid entry
            _sb.AppendIndentedLine($"{targetVar} ??= new {dictCreationType}();");

            if (TypeHelper.IsKeyValuePairCollection(member.Type))
            {
                _sb.AppendIndentedLine($"{targetVar}.Add(new global::System.Collections.Generic.KeyValuePair<{member.MapKeyType}, {member.MapValueType}>(entry.key, entry.value));");
            }
            else
            {
                _sb.AppendIndentedLine($"{targetVar}[entry.key] = entry.value;");
            }

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates inline reading code for simple map types.
        /// </summary>
        private void GenerateInlineRead(string targetVar, string keyType, string valueType, ProtoMemberAttribute member, string dictCreationType, string readerVar)
        {
            // Read entry length
            _sb.AppendIndentedLine($"var entryLength = {readerVar}.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var entryEnd = {readerVar}.Position + (int)entryLength;");

            // Initialize key/value with defaults
            var keyDefault = GetDefaultValueForType(keyType);
            var valueDefault = GetDefaultValueForType(valueType);
            _sb.AppendIndentedLine($"{keyType} key = {keyDefault};");
            _sb.AppendIndentedLine($"{valueType} value = {valueDefault};");

            // Read entry fields
            _sb.AppendIndentedLine($"while ({readerVar}.Position < entryEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var entryTag = {readerVar}.ReadVarUInt32();");
            _sb.AppendIndentedLine("var entryFieldId = (int)(entryTag >> 3);");
            _sb.AppendIndentedLine("var entryWireType = (int)(entryTag & 0x7);");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("switch (entryFieldId)");
            _sb.StartNewBlock();

            // Field 1: Key
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GeneratePrimitiveRead("key", keyType, member.MapKeyIsEnum, readerVar);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Field 2: Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GenerateValueRead(valueType, member.MapValueIsEnum, readerVar);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default: skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"{readerVar}.SkipField((global::GProtobuf.Core.WireType)entryWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            // Initialize dictionary AFTER reading entry data successfully
            _sb.AppendIndentedLine($"{targetVar} ??= new {dictCreationType}();");

            // Add to dictionary/collection
            if (TypeHelper.IsKeyValuePairCollection(member.Type))
            {
                _sb.AppendIndentedLine($"{targetVar}.Add(new global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>(key, value));");
            }
            else
            {
                _sb.AppendIndentedLine($"{targetVar}[key] = value;");
            }
        }

        private void GeneratePrimitiveRead(string targetVar, string typeName, bool isEnum, string readerVar = "reader")
        {
            if (isEnum)
            {
                _sb.AppendIndentedLine($"{targetVar} = ({typeName}){readerVar}.ReadVarInt32();");
                return;
            }

            var normalized = TypeMapping.NormalizeTypeName(typeName);

            // Special handling for types that need wireType parameter
            if (normalized == "System.String")
            {
                _sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadString((WireType)entryWireType);");
                return;
            }

            if (normalized == "System.Guid")
            {
                _sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadGuid((WireType)entryWireType);");
                return;
            }

            if (normalized == "System.TimeSpan")
            {
                _sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadTimeSpan((WireType)entryWireType);");
                return;
            }

            // Use TypeMapping for centralized read expression
            var readExpr = TypeMapping.GetElementReadExpression(typeName, DataFormat.Default, readerVar);
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"{targetVar} = {readExpr};");
                return;
            }

            // Try special types
            if (SpecialTypeHandler.TryGenerateRead(_sb, targetVar, typeName, readerVar))
            {
                return;
            }

            _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
        }

        private void GenerateValueRead(string valueType, bool isEnum, string readerVar = "reader")
        {
            if (isEnum)
            {
                _sb.AppendIndentedLine($"value = ({valueType}){readerVar}.ReadVarInt32();");
                return;
            }

            var normalized = TypeMapping.NormalizeTypeName(valueType);

            // Special handling for types that need wireType parameter
            if (normalized == "System.String")
            {
                _sb.AppendIndentedLine($"value = {readerVar}.ReadString((WireType)entryWireType);");
                return;
            }

            if (normalized == "System.Guid")
            {
                _sb.AppendIndentedLine($"value = {readerVar}.ReadGuid((WireType)entryWireType);");
                return;
            }

            if (normalized == "System.TimeSpan")
            {
                _sb.AppendIndentedLine($"value = {readerVar}.ReadTimeSpan((WireType)entryWireType);");
                return;
            }

            // Check for primitive types using TypeMapping
            var readExpr = TypeMapping.GetElementReadExpression(valueType, DataFormat.Default, readerVar);
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"value = {readExpr};");
                return;
            }

            // Try special types
            if (SpecialTypeHandler.TryGenerateRead(_sb, "value", valueType, readerVar))
            {
                return;
            }

            // Complex types
            if (valueType.EndsWith("[]"))
            {
                GeneratePackedArrayValueRead(valueType, readerVar);
            }
            else if (TypeHelper.IsListType(valueType) || TypeHelper.IsHashSetType(valueType))
            {
                GenerateCollectionValueRead(valueType, readerVar);
            }
            else
            {
                // Nested message type
                var sanitizedName = TypeNameHelper.GetClassName(valueType);
                _sb.AppendIndentedLine($"var valueLength = {readerVar}.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var valueEnd = {readerVar}.Position + (int)valueLength;");
                _sb.AppendIndentedLine($"value = new global::{valueType}();");
                _sb.AppendIndentedLine($"{_writerClassName}.Populate{sanitizedName}(ref {readerVar}, value);");
                _sb.AppendIndentedLine($"{readerVar}.Position = valueEnd;");
            }
        }

        private void GeneratePackedArrayValueRead(string valueType, string readerVar)
        {
            var elementType = valueType.Substring(0, valueType.Length - 2);

            // Try to use optimized packed array read from SpanReader
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, readerVar);
            if (packedReadExpr != null)
            {
                _sb.AppendIndentedLine($"value = {packedReadExpr};");
                return;
            }

            // Fallback for types without optimized packed read (e.g., string)
            var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, readerVar);
            if (elementReadExpr == null)
            {
                _sb.AppendIndentedLine($"// Unsupported array element type: {elementType}");
                return;
            }

            var shortType = TypeMapping.GetShortTypeName(elementType);
            _sb.AppendIndentedLine($"var packedLength = {readerVar}.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var packedEnd = {readerVar}.Position + (int)packedLength;");
            _sb.AppendIndentedLine($"var tempList = new global::System.Collections.Generic.List<{shortType}>();");
            _sb.AppendIndentedLine($"while ({readerVar}.Position < packedEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"tempList.Add({elementReadExpr});");
            _sb.EndBlock();
            _sb.AppendIndentedLine("value = tempList.ToArray();");
        }

        private void GenerateCollectionValueRead(string valueType, string readerVar)
        {
            var elementType = TypeHelper.GetCollectionElementType(valueType);
            var isHashSet = TypeHelper.IsHashSetType(valueType);
            var shortElementType = TypeMapping.GetShortTypeName(elementType);

            // Try to use optimized packed array read, then convert to collection
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, readerVar);
            if (packedReadExpr != null)
            {
                if (isHashSet)
                {
                    _sb.AppendIndentedLine($"value = new global::System.Collections.Generic.HashSet<{shortElementType}>({packedReadExpr});");
                }
                else
                {
                    _sb.AppendIndentedLine($"value = new global::System.Collections.Generic.List<{shortElementType}>({packedReadExpr});");
                }
                return;
            }

            // Fallback for types without optimized packed read
            var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, readerVar);
            if (elementReadExpr == null)
            {
                // Handle string specially - use extension method syntax that works for both SpanReader and StreamReader
                if (TypeMapping.NormalizeTypeName(elementType) == "System.String")
                {
                    elementReadExpr = $"{readerVar}.ReadString(global::GProtobuf.Core.WireType.Len)";
                }
                else
                {
                    _sb.AppendIndentedLine($"// Unsupported collection element type: {elementType}");
                    return;
                }
            }

            _sb.AppendIndentedLine($"var packedLength = {readerVar}.ReadVarUInt32();");
            _sb.AppendIndentedLine($"var packedEnd = {readerVar}.Position + (int)packedLength;");

            if (isHashSet)
            {
                _sb.AppendIndentedLine($"var tempCollection = new global::System.Collections.Generic.HashSet<{shortElementType}>();");
            }
            else
            {
                _sb.AppendIndentedLine($"var tempCollection = new global::System.Collections.Generic.List<{shortElementType}>();");
            }

            _sb.AppendIndentedLine($"while ({readerVar}.Position < packedEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"tempCollection.Add({elementReadExpr});");
            _sb.EndBlock();
            _sb.AppendIndentedLine("value = tempCollection;");
        }

        #endregion

        #region Write (Serialization)

        /// <summary>
        /// Generates code to write a map field.
        /// Uses virtual type methods for complex types, inline code for simple types.
        /// </summary>
        public void GenerateWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var keyType = member.MapKeyType;
            var valueType = member.MapValueType;

            // Check if we should use virtual type
            var virtualInfo = RegisterIfNeeded(member);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (virtualInfo != null)
            {
                // Use virtual type writer
                GenerateVirtualTypeWrite(sourceVar, virtualInfo, member);
            }
            else
            {
                // Use inline writing for simple types
                GenerateInlineWrite(sourceVar, keyType, valueType, member);
            }

            _sb.EndBlock(); // if
        }

        /// <summary>
        /// Generates code that calls WriteMapEntry directly with keyValue.Key, keyValue.Value.
        /// </summary>
        private void GenerateVirtualTypeWrite(string sourceVar, VirtualMapEntryInfo virtualInfo, ProtoMemberAttribute member)
        {
            var mapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(virtualInfo.KeyType, virtualInfo.ValueType);

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();


            // Write tag
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // Call WriteMapEntry - generated in current namespace
            _sb.AppendIndentedLine($"{_writerClassName}.Write{mapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");

            _sb.EndBlock(); // foreach
        }

        /// <summary>
        /// Generates inline writing code for simple map types.
        /// </summary>
        private void GenerateInlineWrite(string sourceVar, string keyType, string valueType, ProtoMemberAttribute member)
        {
            // Reuse calculators outside the loop to reduce allocations
            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Create nested calculator if value type needs it (nested messages or variable-size collections)
            if (TypeHelper.NeedsNestedCalculator(valueType))
            {
                _sb.AppendIndentedLine("var nestedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            }

            bool valueNeedsNullCheck = NeedsNullCheck(valueType, member.MapValueIsEnum);

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            // Reset calculator for each entry
            _sb.AppendIndentedLine("entryCalc.Reset();");

            // Calculate key size (always written)
            GeneratePrimitiveSizeCalculation(member.MapKeyType, "kvp.Key", "entryCalc", member.MapKeyIsEnum, 1);

            // Calculate value size only if value is not null (matching protobuf-net behavior)
            if (valueNeedsNullCheck)
            {
                _sb.AppendIndentedLine("if (kvp.Value != null)");
                _sb.StartNewBlock();
            }
            GenerateValueSizeCalculation(member.MapValueType, "kvp.Value", "entryCalc", member.MapValueIsEnum);
            if (valueNeedsNullCheck)
            {
                _sb.EndBlock();
            }

            // Write tag and length
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entryCalc.Length);");

            // Write key (always written)
            GeneratePrimitiveWrite(keyType, "kvp.Key", member.MapKeyIsEnum, 1);

            // Write value only if not null (matching protobuf-net behavior)
            if (valueNeedsNullCheck)
            {
                _sb.AppendIndentedLine("if (kvp.Value != null)");
                _sb.StartNewBlock();
            }
            GenerateValueWrite(valueType, "kvp.Value", member.MapValueIsEnum);
            if (valueNeedsNullCheck)
            {
                _sb.EndBlock();
            }

            _sb.EndBlock(); // foreach
        }

        private void GeneratePrimitiveWrite(string typeName, string sourceVar, bool isEnum, int fieldId)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(typeName, DataFormat.Default);
            TagCodeHelper.WriteSingleByteTag(_sb, fieldId, wireType);

            if (isEnum)
            {
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                return;
            }

            // Use TypeMapping for centralized write expression
            var writeExpr = TypeMapping.GetElementWriteExpression(typeName, sourceVar, DataFormat.Default, "writer");
            if (writeExpr != null)
            {
                _sb.AppendIndentedLine($"{writeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateWrite(_sb, sourceVar, typeName))
            {
                return;
            }

            _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
        }

        private void GenerateValueWrite(string valueType, string sourceVar, bool isEnum)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(valueType, DataFormat.Default);
            TagCodeHelper.WriteSingleByteTag(_sb, 2, wireType);

            if (isEnum)
            {
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                return;
            }

            // Check for primitive types
            var writeExpr = TypeMapping.GetElementWriteExpression(valueType, sourceVar, DataFormat.Default, "writer");
            if (writeExpr != null)
            {
                _sb.AppendIndentedLine($"{writeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateWrite(_sb, sourceVar, valueType))
            {
                return;
            }

            // Complex types
            if (valueType.EndsWith("[]") || TypeHelper.IsListType(valueType) || TypeHelper.IsHashSetType(valueType))
            {
                GenerateCollectionValueWrite(valueType, sourceVar);
            }
            else
            {
                // Nested message type - reuse nestedCalc from outer scope
                var sanitizedName = TypeNameHelper.GetClassName(valueType);
                _sb.AppendIndentedLine("nestedCalc.Reset();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedName}ContentSize(ref nestedCalc, {sourceVar});");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)nestedCalc.Length);");
                _sb.AppendIndentedLine($"{_writerClassName}.Write{sanitizedName}Content(ref writer, {sourceVar});");
            }
        }

        private void GenerateCollectionValueWrite(string valueType, string sourceVar)
        {
            var elementType = valueType.EndsWith("[]")
                ? valueType.Substring(0, valueType.Length - 2)
                : TypeHelper.GetCollectionElementType(valueType);

            var fixedSize = TypeHelper.GetFixedElementSize(elementType);

            if (fixedSize > 0)
            {
                // Optimization: for fixed-size elements, calculate size without iteration
                if (valueType.EndsWith("[]"))
                {
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({sourceVar}.Length * {fixedSize}));");
                }
                else
                {
                    // For List/HashSet we need Count
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({sourceVar}.Count * {fixedSize}));");
                }
            }
            else
            {
                // Variable-size elements: reuse nestedCalc from outer scope
                _sb.AppendIndentedLine("nestedCalc.Reset();");
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                GenerateElementSizeCalculation(elementType, "item", "nestedCalc");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)nestedCalc.Length);");
            }

            // Write elements
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, "item");
            _sb.EndBlock();
        }

        private void GenerateElementSizeCalculation(string elementType, string itemVar, string calcVar)
        {
            var sizeExpr = TypeMapping.GetElementSizeExpression(elementType, itemVar, DataFormat.Default, calcVar);
            if (sizeExpr != null)
            {
                _sb.AppendIndentedLine($"{sizeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateSize(_sb, itemVar, elementType, calcVar))
            {
                return;
            }

            _sb.AppendIndentedLine($"// Unsupported element type for size calculation: {elementType}");
        }

        private void GenerateElementWrite(string elementType, string itemVar)
        {
            var writeExpr = TypeMapping.GetElementWriteExpression(elementType, itemVar, DataFormat.Default, "writer");
            if (writeExpr != null)
            {
                _sb.AppendIndentedLine($"{writeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateWrite(_sb, itemVar, elementType))
            {
                return;
            }

            _sb.AppendIndentedLine($"// Unsupported element type for write: {elementType}");
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates code to calculate size of a map field.
        /// Uses virtual type methods for complex types, inline code for simple types.
        /// </summary>
        public void GenerateSize(ProtoMemberAttribute member, string sourceVar)
        {
            GenerateSize(member, sourceVar, "calculator");
        }

        /// <summary>
        /// Generates code to calculate size of a map field into a specified calculator variable.
        /// Uses virtual type methods for complex types, inline code for simple types.
        /// </summary>
        public void GenerateSize(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
        {
            var valueType = member.MapValueType;

            // Check if we should use virtual type
            var virtualInfo = RegisterIfNeeded(member);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (virtualInfo != null)
            {
                // Use virtual type size calculator
                GenerateVirtualTypeSize(sourceVar, virtualInfo, member, calculatorVar);
            }
            else
            {
                // Use inline size calculation for simple types
                GenerateInlineSize(sourceVar, valueType, member, calculatorVar);
            }

            _sb.EndBlock(); // if
        }

        /// <summary>
        /// Generates code that calls CalculateMapEntrySize directly with keyValue.Key, keyValue.Value.
        /// </summary>
        private void GenerateVirtualTypeSize(string sourceVar, VirtualMapEntryInfo virtualInfo, ProtoMemberAttribute member, string calculatorVar = "calculator")
        {
            var mapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(virtualInfo.KeyType, virtualInfo.ValueType);
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(member.FieldId, WireType.Len);

            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();


            // Calculate size - generated in current namespace
            _sb.AppendIndentedLine("entryCalc.Reset();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{mapEntryTypeName}Size(ref entryCalc, kvp.Key, kvp.Value);");

            // Add tag and length prefix size
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({tagBytes});");
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)entryCalc.Length);");
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(entryCalc.Length);");

            _sb.EndBlock(); // foreach
        }

        /// <summary>
        /// Generates inline size calculation code for simple map types.
        /// </summary>
        private void GenerateInlineSize(string sourceVar, string valueType, ProtoMemberAttribute member, string calculatorVar = "calculator")
        {
            // Reuse calculators outside the loop to reduce allocations
            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Create nested calculator if value type needs it (nested messages or variable-size collections)
            if (TypeHelper.NeedsNestedCalculator(valueType))
            {
                _sb.AppendIndentedLine("var nestedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            }

            bool valueNeedsNullCheck = NeedsNullCheck(valueType, member.MapValueIsEnum);

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            // Reset calculator for each entry
            _sb.AppendIndentedLine("entryCalc.Reset();");

            // Calculate key size (always)
            GeneratePrimitiveSizeCalculation(member.MapKeyType, "kvp.Key", "entryCalc", member.MapKeyIsEnum, 1);

            // Calculate value size only if not null (matching protobuf-net behavior)
            if (valueNeedsNullCheck)
            {
                _sb.AppendIndentedLine("if (kvp.Value != null)");
                _sb.StartNewBlock();
            }
            GenerateValueSizeCalculation(member.MapValueType, "kvp.Value", "entryCalc", member.MapValueIsEnum);
            if (valueNeedsNullCheck)
            {
                _sb.EndBlock();
            }

            // Add tag and length prefix size
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(member.FieldId, WireType.Len);
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({tagBytes});");
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)entryCalc.Length);");
            _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(entryCalc.Length);");

            _sb.EndBlock(); // foreach
        }

        private void GenerateEntrySizeCalculation(ProtoMemberAttribute member, string keyVar, string valueVar, string calcVar)
        {
            // Key size (field 1)
            GeneratePrimitiveSizeCalculation(member.MapKeyType, keyVar, calcVar, member.MapKeyIsEnum, 1);

            // Value size (field 2)
            GenerateValueSizeCalculation(member.MapValueType, valueVar, calcVar, member.MapValueIsEnum);
        }

        private void GeneratePrimitiveSizeCalculation(string typeName, string sourceVar, string calcVar, bool isEnum, int fieldId)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(typeName, DataFormat.Default);
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes});");

            if (isEnum)
            {
                _sb.AppendIndentedLine($"{calcVar}.WriteVarInt32((int){sourceVar});");
                return;
            }

            // Use TypeMapping for centralized size expression
            var sizeExpr = TypeMapping.GetSizeExpression(typeName, sourceVar, DataFormat.Default, calcVar);
            if (sizeExpr != null)
            {
                _sb.AppendIndentedLine($"{sizeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateSize(_sb, sourceVar, typeName, calcVar))
            {
                return;
            }

            _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
        }

        private void GenerateValueSizeCalculation(string valueType, string sourceVar, string calcVar, bool isEnum)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(valueType, DataFormat.Default);
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(2, wireType);
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes});");

            if (isEnum)
            {
                _sb.AppendIndentedLine($"{calcVar}.WriteVarInt32((int){sourceVar});");
                return;
            }

            // Check for primitive types
            var sizeExpr = TypeMapping.GetSizeExpression(valueType, sourceVar, DataFormat.Default, calcVar);
            if (sizeExpr != null)
            {
                _sb.AppendIndentedLine($"{sizeExpr};");
                return;
            }

            // Try special types (String, Guid)
            if (SpecialTypeHandler.TryGenerateSize(_sb, sourceVar, valueType, calcVar))
            {
                return;
            }

            // Complex types
            if (valueType.EndsWith("[]") || TypeHelper.IsListType(valueType) || TypeHelper.IsHashSetType(valueType))
            {
                GenerateCollectionValueSizeCalculation(valueType, sourceVar, calcVar);
            }
            else
            {
                // Nested message type - reuse nestedCalc from outer scope
                var sanitizedName = TypeNameHelper.GetClassName(valueType);
                _sb.AppendIndentedLine("nestedCalc.Reset();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedName}ContentSize(ref nestedCalc, {sourceVar});");
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)nestedCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(nestedCalc.Length);");
            }
        }

        private void GenerateCollectionValueSizeCalculation(string valueType, string sourceVar, string calcVar)
        {
            var elementType = valueType.EndsWith("[]")
                ? valueType.Substring(0, valueType.Length - 2)
                : TypeHelper.GetCollectionElementType(valueType);

            var fixedSize = TypeHelper.GetFixedElementSize(elementType);

            if (fixedSize > 0)
            {
                // Optimization: for fixed-size elements, calculate size without iteration
                if (valueType.EndsWith("[]"))
                {
                    _sb.AppendIndentedLine($"var packedSize = {sourceVar}.Length * {fixedSize};");
                }
                else
                {
                    _sb.AppendIndentedLine($"var packedSize = {sourceVar}.Count * {fixedSize};");
                }
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)packedSize);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(packedSize);");
            }
            else
            {
                // Variable-size elements - reuse nestedCalc from outer scope
                _sb.AppendIndentedLine("nestedCalc.Reset();");
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                GenerateElementSizeCalculation(elementType, "item", "nestedCalc");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)nestedCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(nestedCalc.Length);");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Returns the appropriate default value for a type.
        /// </summary>
        private static string GetDefaultValueForType(string typeName)
        {
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            if (normalized == "System.String" || normalized == "string")
            {
                return "\"\"";
            }
            return "default";
        }

        #endregion

    }
}
