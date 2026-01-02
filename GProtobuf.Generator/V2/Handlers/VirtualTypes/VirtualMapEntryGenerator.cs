using System;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
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

            // Declare temp lists for array key/values
            if (info.KeyTypeInfo.IsArray)
            {
                var elementType = info.KeyTypeInfo.CollectionElementType;
                var elemInfo = info.KeyTypeInfo.CollectionElementTypeInfo;
                var shortElementType = GetFullTypeName(elementType, elemInfo);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{shortElementType}> _tempList_key = null;");
                _sb.AppendNewLine();
            }

            if (info.ValueTypeInfo.IsArray)
            {
                var elementType = info.ValueTypeInfo.CollectionElementType;
                var elemInfo = info.ValueTypeInfo.CollectionElementTypeInfo;
                var shortElementType = GetFullTypeName(elementType, elemInfo);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{shortElementType}> _tempList_value = null;");
                _sb.AppendNewLine();
            }

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
            GenerateFieldRead("key", info.KeyType, info.KeyTypeInfo, info.KeyIsEnum, "key");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Field 2: Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GenerateFieldRead("value", info.ValueType, info.ValueTypeInfo, info.ValueIsEnum, "value");
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

            // Convert temp lists to arrays for array key/values
            if (info.KeyTypeInfo.IsArray)
            {
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("if (_tempList_key != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("key = _tempList_key.ToArray();");
                _sb.EndBlock();
            }

            if (info.ValueTypeInfo.IsArray)
            {
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("if (_tempList_value != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("value = _tempList_value.ToArray();");
                _sb.EndBlock();
            }

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("return (true, key, value);");

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

        private void GenerateFieldRead(string targetVar, string typeName, TypeAnalysisInfo typeInfo, bool isEnum, string fieldPrefix = "")
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

            // byte[] is a primitive type (bytes), not a collection
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            if (normalizedType == "System.Byte[]")
            {
                var readExpr = TypeMapping.GetReadExpression(typeName, DataFormat.Default, "reader");
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

            if (typeInfo.IsArray)
            {
                // Array value - use temp list inside MapEntry reading
                // Note: Arrays are handled specially because entry reading is inside a while loop
                // and repeated field 2 values need to be accumulated
                var tempListVar = $"_tempList_{targetVar}";
                var elementType = typeInfo.CollectionElementType;
                var elemInfo = typeInfo.CollectionElementTypeInfo;
                var shortElementType = GetFullTypeName(elementType, elemInfo);

                // Initialize temp list on first occurrence
                _sb.AppendIndentedLine($"{tempListVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");

                // Generate array-specific reading logic (inline, not calling GenerateCollectionFieldRead)
                if (elemInfo.IsPrimitive)
                {
                    var normalizedElem = TypeMapping.NormalizeTypeName(elementType);

                    // String and Guid are always non-packed
                    if (normalizedElem == "System.String")
                    {
                        _sb.AppendIndentedLine($"{tempListVar}.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len));");
                    }
                    else if (normalizedElem == "System.Guid")
                    {
                        _sb.AppendIndentedLine($"{tempListVar}.Add(reader.ReadGuid(global::GProtobuf.Core.WireType.Len));");
                    }
                    else
                    {
                        // Numeric primitives - packed format reads entire array at once
                        _sb.AppendIndentedLine($"if (entryWireType == 2) // Len - packed format");
                        _sb.StartNewBlock();
                        var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, "reader");
                        if (packedReadExpr != null)
                        {
                            // For packed arrays, AddRange is more efficient than foreach
                            _sb.AppendIndentedLine($"{tempListVar}.AddRange({packedReadExpr});");
                        }
                        _sb.EndBlock();
                        _sb.AppendIndentedLine("else // Non-packed - single element");
                        _sb.StartNewBlock();
                        var readExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, "reader");
                        if (readExpr != null)
                        {
                            _sb.AppendIndentedLine($"{tempListVar}.Add({readExpr});");
                        }
                        _sb.EndBlock();
                    }
                }
                else if (elemInfo.IsEnum)
                {
                    _sb.AppendIndentedLine($"{tempListVar}.Add(({elementType})reader.ReadVarInt32());");
                }
                else if (TupleHandler.IsTupleType(elementType))
                {
                    var className = TypeNameHelper.GetSafeMethodName(elementType);
                    _sb.AppendIndentedLine($"var {fieldPrefix}ItemLength = reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var {fieldPrefix}ItemSpan = reader.GetSlice((int){fieldPrefix}ItemLength);");
                    _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}ItemSpan);");
                    _sb.AppendIndentedLine($"var {fieldPrefix}Item = SpanReaders.Read{className}Content(ref {fieldPrefix}ScopedReader);");
                    _sb.AppendIndentedLine($"{tempListVar}.Add({fieldPrefix}Item);");
                }
                else if (elemInfo.IsCustomType)
                {
                    var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType);
                    _sb.AppendIndentedLine($"var {fieldPrefix}ItemLength = reader.ReadVarUInt32();");
                    _sb.AppendIndentedLine($"var {fieldPrefix}ItemSpan = reader.GetSlice((int){fieldPrefix}ItemLength);");
                    _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}ItemSpan);");
                    _sb.AppendIndentedLine($"var {fieldPrefix}Item = new global::{elementType}();");
                    _sb.AppendIndentedLine($"{spanReadersClass}.Populate{elemInfo.ShortTypeName}(ref {fieldPrefix}ScopedReader, {fieldPrefix}Item);");
                    _sb.AppendIndentedLine($"{tempListVar}.Add({fieldPrefix}Item);");
                }

                return;
            }

            if (typeInfo.IsCollection)
            {
                // Collection value - need to handle both packed and non-packed formats
                GenerateCollectionFieldRead(targetVar, typeInfo, "entryWireType", fieldPrefix);
                return;
            }

            // Check if this is a Tuple type (before IsCustomType check)
            if (TupleHandler.IsTupleType(typeName))
            {
                // Tuple type - use virtual Tuple Read method
                var className = TypeNameHelper.GetSafeMethodName(typeName);
                _sb.AppendIndentedLine($"var {fieldPrefix}MsgLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var {fieldPrefix}MsgSpan = reader.GetSlice((int){fieldPrefix}MsgLength);");
                _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}MsgSpan);");
                _sb.AppendIndentedLine($"{targetVar} = SpanReaders.Read{className}Content(ref {fieldPrefix}ScopedReader);");
                return;
            }

            if (typeInfo.IsCustomType)
            {
                // Custom message type - use scoped reader to limit reading to message bounds
                var sanitizedName = typeInfo.ShortTypeName;
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(typeName);
                _sb.AppendIndentedLine($"var {fieldPrefix}MsgLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var {fieldPrefix}MsgSpan = reader.GetSlice((int){fieldPrefix}MsgLength);");
                _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}MsgSpan);");
                _sb.AppendIndentedLine($"{targetVar} = new global::{typeName}();");
                _sb.AppendIndentedLine($"{spanReadersClass}.Populate{sanitizedName}(ref {fieldPrefix}ScopedReader, {targetVar});");
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

        private void GenerateCollectionFieldRead(string targetVar, TypeAnalysisInfo typeInfo, string wireTypeVar, string fieldPrefix = "")
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

                _sb.AppendIndentedLine($"var {fieldPrefix}InnerDict = new global::System.Collections.Generic.Dictionary<{innerKeyType}, {innerValueType}>();");
                _sb.AppendIndentedLine($"var {fieldPrefix}CollectionLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var {fieldPrefix}CollectionEnd = reader.Position + (int){fieldPrefix}CollectionLength;");
                _sb.AppendIndentedLine($"while (reader.Position < {fieldPrefix}CollectionEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"var {fieldPrefix}InnerEntry = Read{elemInfo.MapEntryTypeName}(ref reader);");
                _sb.AppendIndentedLine($"if ({fieldPrefix}InnerEntry.success) {fieldPrefix}InnerDict[{fieldPrefix}InnerEntry.key] = {fieldPrefix}InnerEntry.value;");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{targetVar}.Add({fieldPrefix}InnerDict);");
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
                    _sb.AppendIndentedLine($"{targetVar}.Add(reader.ReadGuid(global::GProtobuf.Core.WireType.Len));");
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
                        _sb.AppendIndentedLine($"foreach (var {fieldPrefix}Item in {packedReadExpr})");
                        _sb.StartNewBlock();
                        _sb.AppendIndentedLine($"{targetVar}.Add({fieldPrefix}Item);");
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
            else if (elemInfo.IsEnum)
            {
                // Collection of enum types - read as VarInt32 and cast
                _sb.AppendIndentedLine($"{targetVar}.Add(({elementType})reader.ReadVarInt32());");
            }
            else if (TupleHandler.IsTupleType(elementType))
            {
                // Collection of Tuple types - use virtual Tuple Read method
                var className = TypeNameHelper.GetSafeMethodName(elementType);
                _sb.AppendIndentedLine($"var {fieldPrefix}ItemLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var {fieldPrefix}ItemSpan = reader.GetSlice((int){fieldPrefix}ItemLength);");
                _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}ItemSpan);");
                _sb.AppendIndentedLine($"var {fieldPrefix}Item = SpanReaders.Read{className}Content(ref {fieldPrefix}ScopedReader);");
                _sb.AppendIndentedLine($"{targetVar}.Add({fieldPrefix}Item);");
            }
            else if (elemInfo.IsCustomType)
            {
                // Collection of custom types - use scoped reader to limit reading to item bounds
                var spanReadersClass = NamespaceHelper.GetSpanReadersClass(elementType);
                _sb.AppendIndentedLine($"var {fieldPrefix}ItemLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var {fieldPrefix}ItemSpan = reader.GetSlice((int){fieldPrefix}ItemLength);");
                _sb.AppendIndentedLine($"var {fieldPrefix}ScopedReader = new SpanReader({fieldPrefix}ItemSpan);");
                _sb.AppendIndentedLine($"var {fieldPrefix}Item = new global::{elementType}();");
                _sb.AppendIndentedLine($"{spanReadersClass}.Populate{elemInfo.ShortTypeName}(ref {fieldPrefix}ScopedReader, {fieldPrefix}Item);");
                _sb.AppendIndentedLine($"{targetVar}.Add({fieldPrefix}Item);");
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
            // byte[] is a primitive type (bytes), not a collection - check this FIRST
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            bool isByteArray = (normalizedType == "System.Byte[]");

            var wireType = isEnum ? WireType.VarInt : GetWireType(typeInfo);
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            // For collections and arrays, tag is added per element inside the loop
            // Exception: byte[] is treated as a primitive and needs tag added here
            if (!typeInfo.IsCollection && (!typeInfo.IsArray || isByteArray))
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

            // byte[] is a primitive type (bytes), not a collection
            if (isByteArray)
            {
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

            if (typeInfo.IsArray || typeInfo.IsCollection)
            {
                GenerateCollectionFieldSize(sourceVar, typeInfo, calcVar, fieldId);
                return;
            }

            // Check if this is a Tuple type (before IsCustomType check)
            if (TupleHandler.IsTupleType(typeName))
            {
                // Tuple type - use virtual Tuple Size method
                var className = TypeNameHelper.GetSafeMethodName(typeName);

                _sb.AppendIndentedLine($"var tempCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref tempCalc{fieldId}, {sourceVar});");

                // Cache the length if a cache variable is provided
                if (lengthCacheVar != null)
                {
                    _sb.AppendIndentedLine($"{lengthCacheVar} = tempCalc{fieldId}.Length;");
                }

                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)tempCalc{fieldId}.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(tempCalc{fieldId}.Length);");
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
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(17);"); // Guid is 16 bytes + 1 byte for length prefix (varint 16)
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
            else if (elemInfo.IsEnum)
            {
                // Enum types - use packed encoding like numeric primitives
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for packed field {fieldId}");
                _sb.AppendIndentedLine("var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("packedCalc.WriteVarInt32((int)item);");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)packedCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(packedCalc.Length);");
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
            // byte[] is a primitive type (bytes), not a collection - check this FIRST
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            bool isByteArray = (normalizedType == "System.Byte[]");

            var wireType = isEnum ? WireType.VarInt : GetWireType(typeInfo);
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            // For collections and arrays, tag is written per element inside the loop
            // Exception: byte[] is treated as a primitive and needs tag written here
            if (!typeInfo.IsCollection && (!typeInfo.IsArray || isByteArray))
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

            // byte[] is a primitive type (bytes), not a collection
            if (isByteArray)
            {
                var writeExpr = TypeMapping.GetWriteExpression(typeName, sourceVar, DataFormat.Default, "writer");
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

            if (typeInfo.IsArray || typeInfo.IsCollection)
            {
                GenerateCollectionFieldWrite(sourceVar, typeInfo, fieldId);
                return;
            }

            // Check if this is a Tuple type (before IsCustomType check)
            if (TupleHandler.IsTupleType(typeName))
            {
                // Tuple type - use virtual Tuple Write method
                var className = TypeNameHelper.GetSafeMethodName(typeName);
                var writersClass = _writerClassName ?? "StreamWriters";

                // Calculate size
                _sb.AppendIndentedLine($"var writeCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref writeCalc{fieldId}, {sourceVar});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)writeCalc{fieldId}.Length);");

                // Write content
                _sb.AppendIndentedLine($"{writersClass}.Write{className}Content(ref writer, {sourceVar});");
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
            else if (elemInfo.IsEnum)
            {
                // Enum types - use packed encoding like numeric primitives
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for packed field {fieldId}");
                _sb.AppendIndentedLine("var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("packedCalc.WriteVarInt32((int)item);");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)packedCalc.Length);");
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)item);");
                _sb.EndBlock();
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
