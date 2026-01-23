using System;
using System.Linq;
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
        private readonly TypeRegistry _typeRegistry;
        private readonly string _writerType;
        private readonly string _writerClassName;

        public VirtualMapEntryGenerator(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry, TypeRegistry typeRegistry = null)
            : this(sb, registry, typeRegistry, "Stream")
        {
        }

        public VirtualMapEntryGenerator(StringBuilderWithIndent sb, VirtualMapTypeRegistry registry, TypeRegistry typeRegistry, string writerKind)
        {
            _sb = sb;
            _registry = registry;
            _typeRegistry = typeRegistry;
            _writerType = $"global::GProtobuf.Core.{writerKind}Writer";
            _writerClassName = $"{writerKind}Writers";
        }

        /// <summary>
        /// Gets the correct SpanReaders class path for a type, handling nested classes correctly.
        /// Uses TypeRegistry when available to get the correct namespace.
        /// For nested classes, searches for the outer class in TypeRegistry.
        /// </summary>
        private string GetSpanReadersClass(string fullTypeName)
        {
            // Try to get namespace from TypeRegistry first (handles nested classes correctly)
            if (_typeRegistry != null)
            {
                // Try different type name variants (for nested classes, we need to find the outer class)
                var typeVariantsToTry = new System.Collections.Generic.List<string> { fullTypeName };

                // For nested classes like "A.B.C.OuterClass.NestedClass", try "A.B.C.OuterClass"
                var lastDot = fullTypeName.LastIndexOf('.');
                while (lastDot > 0)
                {
                    var parentType = fullTypeName.Substring(0, lastDot);
                    typeVariantsToTry.Add(parentType);
                    lastDot = parentType.LastIndexOf('.');
                }

                // Search for any of these type variants in TypeRegistry
                foreach (var typeToTry in typeVariantsToTry)
                {
                    foreach (var ns in _typeRegistry.GetAllNamespaces())
                    {
                        var typesInNs = _typeRegistry.GetByNamespace(ns);
                        if (typesInNs.Any(t => t.FullName == typeToTry))
                        {
                            // Found the type (or its outer class) - return the namespace
                            return $"global::{ns}.Serialization.SpanReaders";
                        }
                    }
                }
            }

            // Fallback to NamespaceHelper if TypeRegistry is not available or type not found
            return NamespaceHelper.GetSpanReadersClass(fullTypeName);
        }

        #region SpanReader Generation

        /// <summary>
        /// Generates the EstimateMapCapacity helper method (generated once, used by all map readers).
        /// </summary>
        public void GenerateEstimateMapCapacityHelper()
        {
            _sb.AppendIndentedLine("/// <summary>");
            _sb.AppendIndentedLine("/// Estimates dictionary capacity from length-delimited bytes.");
            _sb.AppendIndentedLine("///");
            _sb.AppendIndentedLine("/// RATIONALE:");
            _sb.AppendIndentedLine("/// - Protobuf maps don't have entry count metadata in the wire format");
            _sb.AppendIndentedLine("/// - We estimate based on total byte length of the serialized dictionary");
            _sb.AppendIndentedLine("/// - Average entry size varies by type:");
            _sb.AppendIndentedLine("///   * Primitives (int->int): ~12 bytes per entry");
            _sb.AppendIndentedLine("///   * String maps (string->string): ~32+ bytes per entry");
            _sb.AppendIndentedLine("///   * Complex types: ~48+ bytes per entry");
            _sb.AppendIndentedLine("///");
            _sb.AppendIndentedLine("/// TRADE-OFF:");
            _sb.AppendIndentedLine("/// - Over-estimating wastes memory (~25% overhead is acceptable)");
            _sb.AppendIndentedLine("/// - Under-estimating causes expensive resizes (2x cost: copy + re-hash)");
            _sb.AppendIndentedLine("/// - Conservative estimate (16 bytes/entry) minimizes resizes while limiting waste");
            _sb.AppendIndentedLine("///");
            _sb.AppendIndentedLine("/// SAFETY:");
            _sb.AppendIndentedLine("/// - Caps at 1024 entries to prevent huge allocations from malformed data");
            _sb.AppendIndentedLine("/// - Beyond 1024, dictionary will auto-resize as needed during parsing");
            _sb.AppendIndentedLine("/// </summary>");
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine("private static int EstimateMapCapacity(uint lengthBytes)");
            _sb.StartNewBlock();

            _sb.AppendIndentedLine("// Empty or single entry");
            _sb.AppendIndentedLine("if (lengthBytes < 16) return 1;");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("// Cap at 1024 to prevent huge allocations from malformed/malicious data");
            _sb.AppendIndentedLine("// Beyond 1024 entries, dictionary will auto-resize as needed");
            _sb.AppendIndentedLine("if (lengthBytes > 16384) return 1024;");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("// Heuristic: 16 bytes per entry (conservative average)");
            _sb.AppendIndentedLine("// - Simple types (int->int): ~12 bytes (tag:1 + len:1 + tag:1 + key:4 + tag:1 + value:4)");
            _sb.AppendIndentedLine("// - String maps: ~32+ bytes (varies with string length)");
            _sb.AppendIndentedLine("// - Complex types: ~48+ bytes");
            _sb.AppendIndentedLine("return (int)(lengthBytes / 16);");

            _sb.EndBlock(); // method
            _sb.AppendNewLine();
        }

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
            // byte[] is a primitive type (bytes), not a collection - check this FIRST before anything else
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
                var normalizedElem = TypeMapping.NormalizeTypeName(elementType);

                // Check for special non-packable types first (String, Guid, TimeSpan)
                if (normalizedElem == "System.String")
                {
                    _sb.AppendIndentedLine($"{tempListVar}.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len));");
                }
                else if (normalizedElem == "System.Guid")
                {
                    _sb.AppendIndentedLine($"{tempListVar}.Add(reader.ReadGuid(global::GProtobuf.Core.WireType.Len));");
                }
                else if (normalizedElem == "System.TimeSpan")
                {
                    _sb.AppendIndentedLine($"{tempListVar}.Add(reader.ReadTimeSpan(global::GProtobuf.Core.WireType.VarInt));");
                }
                else if (elemInfo.IsPrimitive)
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
                    var spanReadersClass = GetSpanReadersClass(elementType);
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
                var spanReadersClass = GetSpanReadersClass(typeName);
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

            // Read inner dictionary length for adaptive capacity
            _sb.AppendIndentedLine("var innerDictLength = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var innerDictEnd = reader.Position + (int)innerDictLength;");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("// OPTIMIZATION: adaptive capacity estimation");
            _sb.AppendIndentedLine("int estimatedCapacity = EstimateMapCapacity(innerDictLength);");
            _sb.AppendIndentedLine($"{targetVar} = new global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>(estimatedCapacity);");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("// Read inner dictionary entries");
            _sb.AppendIndentedLine("while (reader.Position < innerDictEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var entry = Read{mapEntryTypeName}(ref reader);");
            _sb.AppendIndentedLine($"if (entry.success) {targetVar}[entry.key] = entry.value;");
            _sb.EndBlock();
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

            // NULL CHECK: elemInfo can be null if ParseSingleGenericArg failed in AnalyzeType
            // Also check if elementType is empty/null
            if (elemInfo == null || string.IsNullOrWhiteSpace(elementType))
            {
                // Generate warning comment ONLY - skip code generation to avoid compile errors
                _sb.AppendIndentedLine($"// ⚠️ CRITICAL: Element type '{elementType}' could not be analyzed (elemInfo={elemInfo}, elementType='{elementType}')");
                _sb.AppendIndentedLine($"// This is a bug in VirtualMapTypeRegistry.AnalyzeType or ParseSingleGenericArg");
                _sb.AppendIndentedLine($"// CANNOT generate deserialization code - skipping field read");
                _sb.AppendIndentedLine($"// TODO: Fix type analysis to properly handle non-generic collections that implement ICollection<T>");
                _sb.AppendIndentedLine($"reader.SkipField({wireTypeVar});");
                return;
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

                // String, Guid, and TimeSpan are always non-packed (each element has its own tag)
                if (normalizedElem == "System.String")
                {
                    _sb.AppendIndentedLine($"{targetVar}.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len));");
                }
                else if (normalizedElem == "System.Guid")
                {
                    _sb.AppendIndentedLine($"{targetVar}.Add(reader.ReadGuid(global::GProtobuf.Core.WireType.Len));");
                }
                else if (normalizedElem == "System.TimeSpan")
                {
                    _sb.AppendIndentedLine($"{targetVar}.Add(reader.ReadTimeSpan(global::GProtobuf.Core.WireType.VarInt));");
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
                // Enum types can be packed (wire type Len) or non-packed (wire type VarInt)
                _sb.AppendIndentedLine($"if ({wireTypeVar} == 2) // Len - packed format");
                _sb.StartNewBlock();
                // Read packed enums: length prefix followed by all enum values
                _sb.AppendIndentedLine("var packedLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("var packedEnd = reader.Position + (int)packedLength;");
                _sb.AppendIndentedLine("while (reader.Position < packedEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetVar}.Add(({elementType})reader.ReadVarInt32());");
                _sb.EndBlock();
                _sb.EndBlock();
                _sb.AppendIndentedLine("else // Non-packed - single element");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetVar}.Add(({elementType})reader.ReadVarInt32());");
                _sb.EndBlock();
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
                var spanReadersClass = GetSpanReadersClass(elementType);
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
            // byte[] is a primitive type (bytes), not a collection - check this FIRST before anything else
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

            // byte[] is a primitive type (bytes), not a collection - handle BEFORE other checks
            if (isByteArray)
            {
                var sizeExpr = TypeMapping.GetSizeExpression(typeName, sourceVar, DataFormat.Default, calcVar);
                if (sizeExpr != null)
                {
                    _sb.AppendIndentedLine($"{sizeExpr};");
                }
                return;
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
            var keyType = typeInfo.DictionaryKeyType;
            var valueType = typeInfo.DictionaryValueType;

            _sb.AppendIndentedLine("// INVARIANT: treat null as empty dictionary");
            _sb.AppendIndentedLine($"var nestedDict = {sourceVar} ?? new global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>();");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("if (nestedDict.Count == 0)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("// Empty dictionary: length = varint(0) = 1 byte");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32(0);");
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("// Non-empty: calculate actual size INCLUDING per-entry length prefixes");
            _sb.AppendIndentedLine("var innerCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine("foreach (var kvp in nestedDict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var innerEntryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{mapEntryTypeName}Size(ref innerEntryCalc, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine("// Each entry is written as: length_prefix + content");
            _sb.AppendIndentedLine("innerCalc.WriteVarUInt32((uint)innerEntryCalc.Length);  // size of length prefix");
            _sb.AppendIndentedLine("innerCalc.AddByteLength(innerEntryCalc.Length);         // size of content");
            _sb.EndBlock();
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)innerCalc.Length);");
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength(innerCalc.Length);");
            _sb.EndBlock();
        }

        private void GenerateCollectionFieldSize(string sourceVar, TypeAnalysisInfo typeInfo, string calcVar, int fieldId)
        {
            var elemInfo = typeInfo.CollectionElementTypeInfo;

            // NULL CHECK: elemInfo can be null if ParseSingleGenericArg failed in AnalyzeType
            if (elemInfo == null)
            {
                // Generate warning comment and skip size calculation
                _sb.AppendIndentedLine($"// ⚠️  WARNING: Element type '{typeInfo.CollectionElementType}' could not be analyzed (null TypeInfo)");
                _sb.AppendIndentedLine($"// This is likely a bug in VirtualMapTypeRegistry.AnalyzeType or ParseSingleGenericArg");
                _sb.AppendIndentedLine($"// Skipping size calculation for this collection field");
                return;
            }

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries - each dictionary is a repeated field
                var (_, dictTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                _sb.AppendIndentedLine($"foreach (var collectionDict in {sourceVar})");
                _sb.StartNewBlock();
                // Add tag for each element in collection
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({dictTagBytes}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var dictCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine("foreach (var kvp in collectionDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("var innerEntryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{elemInfo.MapEntryTypeName}Size(ref innerEntryCalc, kvp.Key, kvp.Value);");
                _sb.AppendIndentedLine("// Each entry is written as: length_prefix + content");
                _sb.AppendIndentedLine("dictCalc.WriteVarUInt32((uint)innerEntryCalc.Length);  // size of length prefix");
                _sb.AppendIndentedLine("dictCalc.AddByteLength(innerEntryCalc.Length);         // size of content");
                _sb.EndBlock();
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)dictCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(dictCalc.Length);");
                _sb.EndBlock();
            }
            else if (elemInfo.IsPrimitive)
            {
                var normalizedElem = TypeMapping.NormalizeTypeName(typeInfo.CollectionElementType);

                // String, Guid, and TimeSpan are NOT packed - each element needs a tag
                if (normalizedElem == "System.String")
                {
                    var (_, strTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({strTagBytes}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine($"{calcVar}.WriteString(item);");
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.Guid")
                {
                    var (_, guidTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({guidTagBytes}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(19);"); // Guid BCL format: 1 byte length + 18 bytes nested message = 19 bytes total
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.TimeSpan")
                {
                    var (_, tsTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.VarInt);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tsTagBytes}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine($"{calcVar}.WriteTimeSpan(item);");
                    _sb.EndBlock();
                }
                else
                {
                    // Numeric primitives use packed encoding: single tag + length + all elements
                    var (_, packedTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({packedTagBytes}); // tag for packed field {fieldId}");
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
                var (_, enumTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({enumTagBytes}); // tag for packed field {fieldId}");
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
                var (_, customTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({customTagBytes}); // tag for repeated field {fieldId}");
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
            // byte[] is a primitive type (bytes), not a collection - check this FIRST before anything else
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

            // byte[] is a primitive type (bytes), not a collection - handle BEFORE other checks
            if (isByteArray)
            {
                var writeExpr = TypeMapping.GetWriteExpression(typeName, sourceVar, DataFormat.Default, "writer");
                if (writeExpr != null)
                {
                    _sb.AppendIndentedLine($"{writeExpr};");
                }
                return;
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
            var keyType = typeInfo.DictionaryKeyType;
            var valueType = typeInfo.DictionaryValueType;

            // NOTE: nestedDict variable already created in size calculation phase
            // Just use it directly here

            _sb.AppendIndentedLine("if (nestedDict.Count == 0)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("// Empty dictionary: write length = 0");
            _sb.AppendIndentedLine("writer.WriteVarUInt32(0);");
            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("// Non-empty: calculate and write actual size INCLUDING per-entry length prefixes");
            _sb.AppendIndentedLine("var innerCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine("foreach (var kvp in nestedDict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var innerEntryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{mapEntryTypeName}Size(ref innerEntryCalc, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine("// Each entry is written as: length_prefix + content");
            _sb.AppendIndentedLine("innerCalc.WriteVarUInt32((uint)innerEntryCalc.Length);  // size of length prefix");
            _sb.AppendIndentedLine("innerCalc.AddByteLength(innerEntryCalc.Length);         // size of content");
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)innerCalc.Length);");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Write inner dictionary entries");
            _sb.AppendIndentedLine("foreach (var kvp in nestedDict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{_writerClassName}.Write{mapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionFieldWrite(string sourceVar, TypeAnalysisInfo typeInfo, int fieldId)
        {
            var elemInfo = typeInfo.CollectionElementTypeInfo;

            // NULL CHECK: elemInfo can be null if ParseSingleGenericArg failed in AnalyzeType
            if (elemInfo == null)
            {
                // Generate warning comment and fallback code
                _sb.AppendIndentedLine($"// ⚠️  WARNING: Element type '{typeInfo.CollectionElementType}' could not be analyzed (null TypeInfo)");
                _sb.AppendIndentedLine($"// This is likely a bug in VirtualMapTypeRegistry.AnalyzeType or ParseSingleGenericArg");
                _sb.AppendIndentedLine($"// Skipping write for this collection field");
                return;
            }

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries - each dictionary is a repeated field
                var (dictBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                _sb.AppendIndentedLine($"foreach (var collectionDict in {sourceVar})");
                _sb.StartNewBlock();
                // Write tag for each element in collection
                _sb.AppendIndentedLine($"writer.WriteSingleByte({dictBytesString}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("// Write inner dictionary as packed entries INCLUDING per-entry length prefixes");
                _sb.AppendIndentedLine("var dictCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine("foreach (var kvp in collectionDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("var innerEntryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{elemInfo.MapEntryTypeName}Size(ref innerEntryCalc, kvp.Key, kvp.Value);");
                _sb.AppendIndentedLine("// Each entry is written as: length_prefix + content");
                _sb.AppendIndentedLine("dictCalc.WriteVarUInt32((uint)innerEntryCalc.Length);  // size of length prefix");
                _sb.AppendIndentedLine("dictCalc.AddByteLength(innerEntryCalc.Length);         // size of content");
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)dictCalc.Length);");
                _sb.AppendIndentedLine("foreach (var kvp in collectionDict)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{_writerClassName}.Write{elemInfo.MapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");
                _sb.EndBlock();
                _sb.EndBlock();
            }
            else if (elemInfo.IsPrimitive)
            {
                var normalizedElem = TypeMapping.NormalizeTypeName(typeInfo.CollectionElementType);

                // String, Guid, and TimeSpan are NOT packed - each element needs a tag
                if (normalizedElem == "System.String")
                {
                    var (strBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({strBytesString}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine("writer.WriteString(item);");
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.Guid")
                {
                    var (guidBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({guidBytesString}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine("writer.WriteGuid(item);");
                    _sb.EndBlock();
                }
                else if (normalizedElem == "System.TimeSpan")
                {
                    var (tsBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.VarInt);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({tsBytesString}); // tag for repeated field {fieldId}");
                    _sb.AppendIndentedLine("writer.WriteTimeSpan(item);");
                    _sb.EndBlock();
                }
                else
                {
                    // Numeric primitives use packed encoding: single tag + length + all elements
                    var (packedBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({packedBytesString}); // tag for packed field {fieldId}");
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
                var (enumBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                _sb.AppendIndentedLine($"writer.WriteSingleByte({enumBytesString}); // tag for packed field {fieldId}");
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
                var (customBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType);
                var writersClass = NamespaceHelper.GetWritersClass(typeInfo.CollectionElementType, _writerClassName);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"writer.WriteSingleByte({customBytesString}); // tag for repeated field {fieldId}");
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
            // Null/empty check - prevent generating invalid generic types like HashSet<>
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return "object /* ERROR: typeName is null/empty */";
            }

            // Null check - fallback to typeName if typeInfo is null
            if (typeInfo == null)
            {
                return typeName;
            }

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
            // Safety check - if typeInfo is null or typeName is empty, return default
            if (typeInfo == null || string.IsNullOrWhiteSpace(typeName))
            {
                return "default /* ERROR: cannot initialize - typeInfo or typeName is null/empty */";
            }

            if (typeInfo.IsDictionary)
            {
                return $"new global::System.Collections.Generic.Dictionary<{typeInfo.DictionaryKeyType}, {typeInfo.DictionaryValueType}>()";
            }

            if (typeInfo.IsList)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                // Check if element type is valid before generating constructor
                if (elemType.Contains("ERROR") || string.IsNullOrWhiteSpace(typeInfo.CollectionElementType))
                {
                    return "default /* ERROR: cannot initialize List with unknown element type */";
                }
                return $"new global::System.Collections.Generic.List<{elemType}>()";
            }

            if (typeInfo.IsHashSet)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                // Check if element type is valid before generating constructor
                if (elemType.Contains("ERROR") || string.IsNullOrWhiteSpace(typeInfo.CollectionElementType))
                {
                    return "default /* ERROR: cannot initialize HashSet with unknown element type */";
                }
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
