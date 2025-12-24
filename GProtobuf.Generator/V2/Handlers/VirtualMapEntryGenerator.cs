using System;
using GProtobuf.Generator.V2.Handlers.Core;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Generates serialization code for virtual map entry types.
    /// Each map entry is treated as a message with key (field 1) and value (field 2).
    /// </summary>
    internal class VirtualMapEntryGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly VirtualMapTypeRegistry _registry;
        private readonly string _writerType;
        private readonly string _writerClassName;

        public VirtualMapEntryGenerator(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry)
            : this(sb, registry, "Stream")
        {
        }

        public VirtualMapEntryGenerator(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry, string writerKind)
        {
            _sb = sb;
            _registry = registry;
            _writerType = $"global::GProtobuf.Core.{writerKind}Writer";
            _writerClassName = $"{writerKind}Writers";
        }

        #region SpanReader Generation

        /// <summary>
        /// Generates a reader method for a virtual map entry type.
        /// </summary>
        public void GenerateReader(VirtualMapEntryInfo info)
        {
            var methodName = $"Read{info.TypeName}";
            var keyType = GetFullTypeName(info.KeyType, info.KeyTypeInfo);
            var valueType = GetFullTypeName(info.ValueType, info.ValueTypeInfo);

            _sb.AppendIndentedLine($"public static (bool success, {keyType} key, {valueType} value) {methodName}(ref SpanReader reader)");
            _sb.StartNewBlock();

            // Initialize key and value with defaults (collections need empty instances, not null)
            var keyInit = GetDefaultInitializer(info.KeyType, info.KeyTypeInfo);
            var valueInit = GetDefaultInitializer(info.ValueType, info.ValueTypeInfo);
            _sb.AppendIndentedLine($"{keyType} key = {keyInit};");
            _sb.AppendIndentedLine($"{valueType} value = {valueInit};");
            _sb.AppendNewLine();

            // Read entry length
            _sb.AppendIndentedLine("var entryLength = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var entryEnd = reader.Position + (int)entryLength;");
            _sb.AppendNewLine();

            // Read fields
            _sb.AppendIndentedLine("while (reader.Position < entryEnd)");
            _sb.StartNewBlock();

            _sb.AppendIndentedLine("var entryTag = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var entryFieldId = (int)(entryTag >> 3);");
            _sb.AppendIndentedLine("var entryWireType = (int)(entryTag & 0x7);");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("switch (entryFieldId)");
            _sb.StartNewBlock();

            // Field 1: Key
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GenerateFieldRead("key", info.KeyType, info.KeyTypeInfo, info.KeyIsEnum);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Field 2: Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GenerateFieldRead("value", info.ValueType, info.ValueTypeInfo, info.ValueIsEnum);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default: skip
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField((global::GProtobuf.Core.WireType)entryWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("return (true, key, value);");

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateFieldRead(string targetVar, string typeName, TypeAnalysisInfo typeInfo, bool isEnum)
        {
            if (isEnum)
            {
                _sb.AppendIndentedLine($"{targetVar} = ({typeName})reader.ReadVarInt32();");
                return;
            }

            if (typeInfo.IsPrimitive)
            {
                // Try special types (String, Guid)
                if (SpecialTypeHandler.TryGenerateRead(_sb, targetVar, typeName))
                {
                    return;
                }

                // Use TypeMapping for other primitives
                var readExpr = TypeMapping.GetElementReadExpression(typeName, DataFormat.Default, "reader");
                if (readExpr != null)
                {
                    _sb.AppendIndentedLine($"{targetVar} = {readExpr};");
                }
                return;
            }

            if (typeInfo.IsDictionary)
            {
                // Nested dictionary - read as virtual map entry
                GenerateDictionaryFieldRead(targetVar, typeInfo);
                return;
            }

            if (typeInfo.IsCollection)
            {
                // Collection value - need to handle both packed and non-packed formats
                GenerateCollectionFieldRead(targetVar, typeInfo, "entryWireType");
                return;
            }

            if (typeInfo.IsCustomType)
            {
                // Custom message type - use scoped reader to limit reading to message bounds
                var sanitizedName = typeInfo.ShortTypeName;
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(typeName);
                _sb.AppendIndentedLine("var msgLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("var msgSpan = reader.GetSlice((int)msgLength);");
                _sb.AppendIndentedLine("var scopedReader = new SpanReader(msgSpan);");
                _sb.AppendIndentedLine($"{targetVar} = new global::{typeName}();");
                _sb.AppendIndentedLine($"{spanReadersClass}.Populate{sanitizedName}(ref scopedReader, {targetVar});");
                return;
            }
        }

        private void GenerateDictionaryFieldRead(string targetVar, TypeAnalysisInfo typeInfo)
        {
            var keyType = typeInfo.DictionaryKeyType;
            var valueType = typeInfo.DictionaryValueType;
            var mapEntryTypeName = typeInfo.MapEntryTypeName;

            _sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>();");
            _sb.AppendIndentedLine($"var entry = Read{mapEntryTypeName}(ref reader);");
            _sb.AppendIndentedLine($"if (entry.success) {targetVar}[entry.key] = entry.value;");
        }

        private void GenerateCollectionFieldRead(string targetVar, TypeAnalysisInfo typeInfo, string wireTypeVar)
        {
            var elementType = typeInfo.CollectionElementType;
            var elemInfo = typeInfo.CollectionElementTypeInfo;
            var shortElementType = GetFullTypeName(elementType, elemInfo);

            if (typeInfo.IsHashSet)
            {
                _sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.HashSet<{shortElementType}>();");
            }
            else
            {
                _sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
            }

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries
                var innerKeyType = elemInfo.DictionaryKeyType;
                var innerValueType = elemInfo.DictionaryValueType;

                _sb.AppendIndentedLine($"var innerDict = new global::System.Collections.Generic.Dictionary<{innerKeyType}, {innerValueType}>();");
                _sb.AppendIndentedLine("var collectionLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("var collectionEnd = reader.Position + (int)collectionLength;");
                _sb.AppendIndentedLine("while (reader.Position < collectionEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"var innerEntry = Read{elemInfo.MapEntryTypeName}(ref reader);");
                _sb.AppendIndentedLine("if (innerEntry.success) innerDict[innerEntry.key] = innerEntry.value;");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{targetVar}.Add(innerDict);");
            }
            else if (elemInfo.IsPrimitive)
            {
                var normalizedElem = TypeMapping.NormalizeTypeName(elementType);

                // String and Guid are always non-packed (each element has its own tag)
                if (normalizedElem == "System.String")
                {
                    _sb.AppendIndentedLine($"{targetVar}.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len));");
                }
                else if (normalizedElem == "System.Guid")
                {
                    _sb.AppendIndentedLine($"{targetVar}.Add(reader.ReadGuid());");
                }
                else
                {
                    // Numeric primitives can be packed (wire type Len) or non-packed (wire type VarInt/Fixed)
                    // Need to check wire type at runtime to handle both formats
                    _sb.AppendIndentedLine($"if ({wireTypeVar} == 2) // Len - packed format");
                    _sb.StartNewBlock();
                    var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, "reader");
                    if (packedReadExpr != null)
                    {
                        _sb.AppendIndentedLine($"foreach (var item in {packedReadExpr})");
                        _sb.StartNewBlock();
                        _sb.AppendIndentedLine($"{targetVar}.Add(item);");
                        _sb.EndBlock();
                    }
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("else // Non-packed - single element");
                    _sb.StartNewBlock();
                    var readExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, "reader");
                    if (readExpr != null)
                    {
                        _sb.AppendIndentedLine($"{targetVar}.Add({readExpr});");
                    }
                    _sb.EndBlock();
                }
            }
            else if (elemInfo.IsCustomType)
            {
                // Collection of custom types - use scoped reader to limit reading to item bounds
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType);
                _sb.AppendIndentedLine("var itemLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("var itemSpan = reader.GetSlice((int)itemLength);");
                _sb.AppendIndentedLine("var scopedReader = new SpanReader(itemSpan);");
                _sb.AppendIndentedLine($"var item = new global::{elementType}();");
                _sb.AppendIndentedLine($"{spanReadersClass}.Populate{elemInfo.ShortTypeName}(ref scopedReader, item);");
                _sb.AppendIndentedLine($"{targetVar}.Add(item);");
            }
        }

        #endregion

        #region StreamWriter Generation

        /// <summary>
        /// Generates a writer method for a virtual map entry type.
        /// </summary>
        public void GenerateWriter(VirtualMapEntryInfo info)
        {
            var methodName = $"Write{info.TypeName}";
            var keyType = GetFullTypeName(info.KeyType, info.KeyTypeInfo);
            var valueType = GetFullTypeName(info.ValueType, info.ValueTypeInfo);

            _sb.AppendIndentedLine($"public static void {methodName}(ref {_writerType} writer, {keyType} key, {valueType} value)");
            _sb.StartNewBlock();

            // Determine if we need to cache nested content lengths
            bool keyNeedsLengthCache = info.KeyTypeInfo.IsCustomType;
            bool valueNeedsLengthCache = info.ValueTypeInfo.IsCustomType;

            // Declare length cache variables if needed
            if (keyNeedsLengthCache)
                _sb.AppendIndentedLine("int keyContentLength;");
            if (valueNeedsLengthCache)
                _sb.AppendIndentedLine("int valueContentLength;");

            // Calculate entry size first
            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Calculate key size (with length caching for custom types)
            GenerateFieldSizeCalculation("key", info.KeyType, info.KeyTypeInfo, info.KeyIsEnum, "entryCalc", 1,
                keyNeedsLengthCache ? "keyContentLength" : null);

            // Calculate value size (with length caching for custom types)
            GenerateFieldSizeCalculation("value", info.ValueType, info.ValueTypeInfo, info.ValueIsEnum, "entryCalc", 2,
                valueNeedsLengthCache ? "valueContentLength" : null);

            _sb.AppendNewLine();

            // Write length prefix
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entryCalc.Length);");
            _sb.AppendNewLine();

            // Write key (field 1) - use cached length if available
            GenerateFieldWrite("key", info.KeyType, info.KeyTypeInfo, info.KeyIsEnum, 1,
                keyNeedsLengthCache ? "keyContentLength" : null);

            // Write value (field 2) - use cached length if available
            GenerateFieldWrite("value", info.ValueType, info.ValueTypeInfo, info.ValueIsEnum, 2,
                valueNeedsLengthCache ? "valueContentLength" : null);

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateFieldSizeCalculation(string sourceVar, string typeName, TypeAnalysisInfo typeInfo,
            bool isEnum, string calcVar, int fieldId, string lengthCacheVar = null)
        {
            var wireType = isEnum ? WireType.VarInt : GetWireType(typeInfo);
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            // For collections, tag is added per element inside the loop
            if (!typeInfo.IsCollection)
            {
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for field {fieldId}");
            }

            if (isEnum)
            {
                _sb.AppendIndentedLine($"{calcVar}.WriteVarInt32((int){sourceVar});");
                return;
            }

            if (typeInfo.IsPrimitive)
            {
                // Try special types (String, Guid)
                if (SpecialTypeHandler.TryGenerateSize(_sb, sourceVar, typeName, calcVar))
                {
                    return;
                }

                // Use TypeMapping for other primitives
                var sizeExpr = TypeMapping.GetSizeExpression(typeName, sourceVar, DataFormat.Default, calcVar);
                if (sizeExpr != null)
                {
                    _sb.AppendIndentedLine($"{sizeExpr};");
                }
                return;
            }

            if (typeInfo.IsDictionary)
            {
                GenerateDictionaryFieldSize(sourceVar, typeInfo, calcVar);
                return;
            }

            if (typeInfo.IsCollection)
            {
                GenerateCollectionFieldSize(sourceVar, typeInfo, calcVar, fieldId);
                return;
            }

            if (typeInfo.IsCustomType)
            {
                var sanitizedName = typeInfo.ShortTypeName;
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName);
                _sb.AppendIndentedLine($"var tempCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{sanitizedName}ContentSize(ref tempCalc{fieldId}, {sourceVar});");

                // Cache the length if a cache variable is provided
                if (lengthCacheVar != null)
                {
                    _sb.AppendIndentedLine($"{lengthCacheVar} = tempCalc{fieldId}.Length;");
                }

                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)tempCalc{fieldId}.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(tempCalc{fieldId}.Length);");
            }
        }

        private void GenerateDictionaryFieldSize(string sourceVar, TypeAnalysisInfo typeInfo, string calcVar)
        {
            var mapEntryTypeName = typeInfo.MapEntryTypeName;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var innerCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"Calculate{mapEntryTypeName}Size(ref innerCalc, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)innerCalc.Length);");
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength(innerCalc.Length);");
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionFieldSize(string sourceVar, TypeAnalysisInfo typeInfo, string calcVar, int fieldId)
        {
            var elemInfo = typeInfo.CollectionElementTypeInfo;
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries - each dictionary is a repeated field
                _sb.AppendIndentedLine($"foreach (var innerDict in {sourceVar})");
                _sb.StartNewBlock();
                // Add tag for each element in collection
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var dictCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine("foreach (var kvp in innerDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"Calculate{elemInfo.MapEntryTypeName}Size(ref dictCalc, kvp.Key, kvp.Value);");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)dictCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(dictCalc.Length);");
                _sb.EndBlock();
            }
            else if (elemInfo.IsPrimitive)
            {
                var normalizedElem = TypeMapping.NormalizeTypeName(typeInfo.CollectionElementType);

                // String and Guid are NOT packed - each element needs a tag
                if (normalizedElem == "System.String")
                {
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine($"{calcVar}.WriteString(item);");
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.Guid")
                {
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(18);"); // Guid is 16 bytes + 2 bytes for length prefix
                    _sb.EndBlock();
                }
                else
                {
                    // Numeric primitives use packed encoding: single tag + length + all elements
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for packed field {fieldId}");
                    _sb.AppendIndentedLine("var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    var sizeExpr = TypeMapping.GetElementSizeExpression(typeInfo.CollectionElementType, "item", DataFormat.Default, "packedCalc");
                    if (sizeExpr != null)
                    {
                        _sb.AppendIndentedLine($"{sizeExpr};");
                    }
                    _sb.EndBlock();
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)packedCalc.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(packedCalc.Length);");
                }
            }
            else if (elemInfo.IsCustomType)
            {
                // Custom types - each element is a repeated field with tag
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{elemInfo.ShortTypeName}ContentSize(ref itemCalc, item);");
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(itemCalc.Length);");
                _sb.EndBlock();
            }

            _sb.EndBlock();
        }

        private void GenerateFieldWrite(string sourceVar, string typeName, TypeAnalysisInfo typeInfo,
            bool isEnum, int fieldId, string cachedLengthVar = null)
        {
            var wireType = isEnum ? WireType.VarInt : GetWireType(typeInfo);
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            // For collections, tag is written per element inside the loop
            if (!typeInfo.IsCollection)
            {
                // Write tag
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // field {fieldId}");
            }

            if (isEnum)
            {
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                return;
            }

            if (typeInfo.IsPrimitive)
            {
                // Try special types (String, Guid)
                if (SpecialTypeHandler.TryGenerateWrite(_sb, sourceVar, typeName))
                {
                    return;
                }

                // Use TypeMapping for other primitives
                var writeExpr = TypeMapping.GetElementWriteExpression(typeName, sourceVar, DataFormat.Default, "writer");
                if (writeExpr != null)
                {
                    _sb.AppendIndentedLine($"{writeExpr};");
                }
                return;
            }

            if (typeInfo.IsDictionary)
            {
                GenerateDictionaryFieldWrite(sourceVar, typeInfo);
                return;
            }

            if (typeInfo.IsCollection)
            {
                GenerateCollectionFieldWrite(sourceVar, typeInfo, fieldId);
                return;
            }

            if (typeInfo.IsCustomType)
            {
                var sanitizedName = typeInfo.ShortTypeName;
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName);
                var writersClass = NamespaceHelper.GetWritersClass(typeName, _writerClassName);

                // Use cached length if available, otherwise recalculate
                if (cachedLengthVar != null)
                {
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){cachedLengthVar});");
                }
                else
                {
                    _sb.AppendIndentedLine($"var writeCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{sanitizedName}ContentSize(ref writeCalc{fieldId}, {sourceVar});");
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)writeCalc{fieldId}.Length);");
                }

                _sb.AppendIndentedLine($"{writersClass}.Write{sanitizedName}Content(ref writer, {sourceVar});");
            }
        }

        private void GenerateDictionaryFieldWrite(string sourceVar, TypeAnalysisInfo typeInfo)
        {
            var mapEntryTypeName = typeInfo.MapEntryTypeName;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"Write{mapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionFieldWrite(string sourceVar, TypeAnalysisInfo typeInfo, int fieldId)
        {
            var elemInfo = typeInfo.CollectionElementTypeInfo;
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries - each dictionary is a repeated field
                _sb.AppendIndentedLine($"foreach (var innerDict in {sourceVar})");
                _sb.StartNewBlock();
                // Write tag for each element in collection
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("// Write inner dictionary as packed entries");
                _sb.AppendIndentedLine("var dictCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine("foreach (var kvp in innerDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"Calculate{elemInfo.MapEntryTypeName}Size(ref dictCalc, kvp.Key, kvp.Value);");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)dictCalc.Length);");
                _sb.AppendIndentedLine("foreach (var kvp in innerDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"Write{elemInfo.MapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");
                _sb.EndBlock();
                _sb.EndBlock();
            }
            else if (elemInfo.IsPrimitive)
            {
                var normalizedElem = TypeMapping.NormalizeTypeName(typeInfo.CollectionElementType);

                // String and Guid are NOT packed - each element needs a tag
                if (normalizedElem == "System.String")
                {
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine("writer.WriteString(item);");
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.Guid")
                {
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine("writer.WriteGuid(item);");
                    _sb.EndBlock();
                }
                else
                {
                    // Numeric primitives use packed encoding: single tag + length + all elements
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for packed field {fieldId}");
                    _sb.AppendIndentedLine("var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    var sizeExpr = TypeMapping.GetElementSizeExpression(typeInfo.CollectionElementType, "item", DataFormat.Default, "packedCalc");
                    if (sizeExpr != null)
                    {
                        _sb.AppendIndentedLine($"{sizeExpr};");
                    }
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)packedCalc.Length);");
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    var writeExpr = TypeMapping.GetElementWriteExpression(typeInfo.CollectionElementType, "item", DataFormat.Default, "writer");
                    if (writeExpr != null)
                    {
                        _sb.AppendIndentedLine($"{writeExpr};");
                    }
                    _sb.EndBlock();
                }
            }
            else if (elemInfo.IsCustomType)
            {
                // Custom types - each element is a repeated field with tag
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType);
                var writersClass = NamespaceHelper.GetWritersClass(typeInfo.CollectionElementType, _writerClassName);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{elemInfo.ShortTypeName}ContentSize(ref itemCalc, item);");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine($"{writersClass}.Write{elemInfo.ShortTypeName}Content(ref writer, item);");
                _sb.EndBlock();
            }

            _sb.EndBlock();
        }

        #endregion

        #region SizeCalculator Generation

        /// <summary>
        /// Generates a size calculation method for a virtual map entry type.
        /// </summary>
        public void GenerateSizeCalculator(VirtualMapEntryInfo info)
        {
            var methodName = $"Calculate{info.TypeName}Size";
            var keyType = GetFullTypeName(info.KeyType, info.KeyTypeInfo);
            var valueType = GetFullTypeName(info.ValueType, info.ValueTypeInfo);

            _sb.AppendIndentedLine($"public static void {methodName}(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {keyType} key, {valueType} value)");
            _sb.StartNewBlock();

            // Calculate key size
            GenerateFieldSizeCalculation("key", info.KeyType, info.KeyTypeInfo, info.KeyIsEnum, "calculator", 1);

            // Calculate value size
            GenerateFieldSizeCalculation("value", info.ValueType, info.ValueTypeInfo, info.ValueIsEnum, "calculator", 2);

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        #endregion

        #region Helpers

        private static string GetFullTypeName(string typeName, TypeAnalysisInfo typeInfo)
        {
            if (typeInfo.IsPrimitive)
            {
                return typeInfo.ShortTypeName ?? typeName;
            }

            if (typeInfo.IsDictionary)
            {
                return $"global::System.Collections.Generic.Dictionary<{typeInfo.DictionaryKeyType}, {typeInfo.DictionaryValueType}>";
            }

            if (typeInfo.IsList)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                return $"global::System.Collections.Generic.List<{elemType}>";
            }

            if (typeInfo.IsHashSet)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                return $"global::System.Collections.Generic.HashSet<{elemType}>";
            }

            if (typeInfo.IsArray)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                return $"{elemType}[]";
            }

            return $"global::{typeName}";
        }

        /// <summary>
        /// Gets the default initializer for a type. Collections get empty instances, others get 'default'.
        /// </summary>
        private static string GetDefaultInitializer(string typeName, TypeAnalysisInfo typeInfo)
        {
            if (typeInfo.IsDictionary)
            {
                return $"new global::System.Collections.Generic.Dictionary<{typeInfo.DictionaryKeyType}, {typeInfo.DictionaryValueType}>()";
            }

            if (typeInfo.IsList)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                return $"new global::System.Collections.Generic.List<{elemType}>()";
            }

            if (typeInfo.IsHashSet)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                return $"new global::System.Collections.Generic.HashSet<{elemType}>()";
            }

            // For primitives, strings, and custom types, use default
            return "default";
        }

        private static WireType GetWireType(TypeAnalysisInfo typeInfo)
        {
            if (typeInfo.IsString || typeInfo.IsGuid || typeInfo.IsDictionary ||
                typeInfo.IsCollection || typeInfo.IsCustomType || typeInfo.IsArray)
            {
                return WireType.Len;
            }

            // For primitives, use TypeMapping
            return TypeMapping.GetWireType(typeInfo.FullTypeName, DataFormat.Default);
        }


        #endregion
    }
}
