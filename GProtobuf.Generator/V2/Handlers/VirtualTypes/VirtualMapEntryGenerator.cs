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
            // Use NamespaceHelper with TypeRegistry for accurate namespace resolution
            return NamespaceHelper.GetSpanReadersClass(fullTypeName, _typeRegistry);
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
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                // Empty arrays should deserialize as empty arrays, not null (protobuf semantics)
                var keyElementType = info.KeyTypeInfo.CollectionElementType;
                var keyElemInfo = info.KeyTypeInfo.CollectionElementTypeInfo;
                var shortKeyElementType = GetFullTypeName(keyElementType, keyElemInfo);
                _sb.AppendIndentedLine($"key = global::System.Array.Empty<{shortKeyElementType}>();");
                _sb.EndBlock();
            }

            if (info.ValueTypeInfo.IsArray)
            {
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("if (_tempList_value != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("value = _tempList_value.ToArray();");
                _sb.EndBlock();
                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();
                // Empty arrays should deserialize as empty arrays, not null (protobuf semantics)
                var valueElementType = info.ValueTypeInfo.CollectionElementType;
                var valueElemInfo = info.ValueTypeInfo.CollectionElementTypeInfo;
                var shortValueElementType = GetFullTypeName(valueElementType, valueElemInfo);
                _sb.AppendIndentedLine($"value = global::System.Array.Empty<{shortValueElementType}>();");
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
                    _sb.AppendIndentedLine($"{tempListVar}.Add(reader.ReadTimeSpan(global::GProtobuf.Core.WireType.Len));"); // TimeSpan is sub-message (Level200)
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

                // Handle nullable types - create temp variable for Populate
                if (typeName.EndsWith("?"))
                {
                    var nonNullableType = typeName.Substring(0, typeName.Length - 1);
                    _sb.AppendIndentedLine($"var {fieldPrefix}Temp = new global::{nonNullableType}();");
                    _sb.AppendIndentedLine($"{spanReadersClass}.Populate{sanitizedName}(ref {fieldPrefix}ScopedReader, {fieldPrefix}Temp);");
                    _sb.AppendIndentedLine($"{targetVar} = {fieldPrefix}Temp;");
                }
                else
                {
                    _sb.AppendIndentedLine($"{targetVar} = new global::{typeName}();");
                    _sb.AppendIndentedLine($"{spanReadersClass}.Populate{sanitizedName}(ref {fieldPrefix}ScopedReader, {targetVar});");
                }
                return;
            }
        }

        private void GenerateDictionaryFieldRead(string targetVar, TypeAnalysisInfo typeInfo)
        {
            var mapEntryTypeName = typeInfo.MapEntryTypeName;

            _sb.AppendIndentedLine($"// Each field 2 is ONE entry of nested dictionary (repeated field semantics)");
            _sb.AppendIndentedLine($"var innerEntry = Read{mapEntryTypeName}(ref reader);");
            _sb.AppendIndentedLine($"if (innerEntry.success) {targetVar}[innerEntry.key] = innerEntry.value;");
        }

        private void GenerateCollectionFieldRead(string targetVar, TypeAnalysisInfo typeInfo, string wireTypeVar, string fieldPrefix = "")
        {
            var elementType = typeInfo.CollectionElementType;
            var elemInfo = typeInfo.CollectionElementTypeInfo;
            var shortElementType = GetFullTypeName(elementType, elemInfo);

            if (typeInfo.IsHashSet)
            {
                // Check if it's a custom hashset type (e.g., ValueLogTypeHashSet)
                if (TypeHelper.IsCustomHashSetType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom hashsets
                    _sb.AppendIndentedLine($"{targetVar} ??= new global::{typeInfo.FullTypeName}();");
                }
                else
                {
                    // Standard HashSet<T>
                    _sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.HashSet<{shortElementType}>();");
                }
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
                _sb.AppendIndentedLine($"reader.SkipField((global::GProtobuf.Core.WireType){wireTypeVar});");
                return;
            }

            if (elemInfo.IsDictionary)
            {
                // Collection of dictionaries
                var innerKeyType = elemInfo.DictionaryKeyType;
                var innerValueType = elemInfo.DictionaryValueType;

                // Use original dictionary type if it's a custom type (ConcurrentDictionary, ListDictionary, etc.)
                string innerDictType;
                if (TypeHelper.IsCustomDictionaryType(elemInfo.FullTypeName))
                {
                    innerDictType = $"global::{elemInfo.FullTypeName}";
                }
                else
                {
                    innerDictType = $"global::System.Collections.Generic.Dictionary<{innerKeyType}, {innerValueType}>";
                }
                _sb.AppendIndentedLine($"var {fieldPrefix}InnerDict = new {innerDictType}();");
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
                    _sb.AppendIndentedLine($"{targetVar}.Add(reader.ReadTimeSpan(global::GProtobuf.Core.WireType.Len));"); // TimeSpan is sub-message (Level200)
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

            // For collections, arrays, and dictionaries, tag is added per element inside the loop
            // Exception: byte[] is treated as a primitive and needs tag added here
            if (!typeInfo.IsCollection && !typeInfo.IsDictionary && (!typeInfo.IsArray || isByteArray))
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
                // Use .Value for nullable enums
                var enumValue = GetNullableValueAccess(sourceVar, typeName);
                _sb.AppendIndentedLine($"{calcVar}.WriteVarInt32((int){enumValue});");
                return;
            }

            if (typeInfo.IsPrimitive)
            {
                // Get value access (adds .Value for nullable types)
                var actualValue = GetNullableValueAccess(sourceVar, typeName);

                // Try special types (String, Guid)
                if (SpecialTypeHandler.TryGenerateSize(_sb, actualValue, typeName, calcVar))
                {
                    return;
                }

                // Use TypeMapping for other primitives
                // Remove '?' from typeName for GetSizeExpression (it expects base type)
                var baseTypeName = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;
                var sizeExpr = TypeMapping.GetSizeExpression(baseTypeName, actualValue, DataFormat.Default, calcVar);
                if (sizeExpr != null)
                {
                    _sb.AppendIndentedLine($"{sizeExpr};");
                }
                return;
            }

            if (typeInfo.IsDictionary)
            {
                GenerateDictionaryFieldSize(sourceVar, typeInfo, calcVar, fieldId);
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

                // Handle nullable types - append .Value for Content method calls
                var valueAccess = GetNullableValueAccess(sourceVar, typeName);
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref tempCalc{fieldId}, {valueAccess});");

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
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName, _typeRegistry);
                _sb.AppendIndentedLine($"var tempCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");

                // Handle nullable types - append .Value for Content method calls
                var valueAccess = GetNullableValueAccess(sourceVar, typeName);
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{sanitizedName}ContentSize(ref tempCalc{fieldId}, {valueAccess});");

                // Cache the length if a cache variable is provided
                if (lengthCacheVar != null)
                {
                    _sb.AppendIndentedLine($"{lengthCacheVar} = tempCalc{fieldId}.Length;");
                }

                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)tempCalc{fieldId}.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(tempCalc{fieldId}.Length);");
            }
        }

        private void GenerateDictionaryFieldSize(string sourceVar, TypeAnalysisInfo typeInfo, string calcVar, int fieldId)
        {
            var mapEntryTypeName = typeInfo.MapEntryTypeName;
            var keyType = typeInfo.DictionaryKeyType;
            var valueType = typeInfo.DictionaryValueType;

            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);

            // Determine the correct dictionary type to instantiate (custom or standard)
            string dictType;
            if (TypeHelper.IsCustomDictionaryType(typeInfo.FullTypeName))
            {
                dictType = $"global::{typeInfo.FullTypeName}";
            }
            else
            {
                dictType = $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            _sb.AppendIndentedLine("// INVARIANT: treat null as empty dictionary");
            _sb.AppendIndentedLine($"var nestedDict = {sourceVar} ?? new {dictType}();");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("// Each inner entry is a repeated field with its own tag (protobuf-net Level200 format)");
            _sb.AppendIndentedLine("foreach (var kvp in nestedDict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId}");
            _sb.AppendIndentedLine("var innerEntryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{mapEntryTypeName}Size(ref innerEntryCalc, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine("// Each entry is written as: length_prefix + content");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)innerEntryCalc.Length);  // size of length prefix");
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength(innerEntryCalc.Length);         // size of content");
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
                    // MapEntry Level200: use UNPACKED encoding for primitives (protobuf-net 2.3.7 compatibility)
                    // Inside MapEntry, repeated fields are serialized with one tag per element (not packed)
                    // This differs from top-level repeated fields which use packed encoding
                    var wireType = TypeMapping.GetWireType(typeInfo.CollectionElementType, DataFormat.Default);
                    var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({tagBytes}); // tag for repeated field {fieldId} (unpacked)");
                    var sizeExpr = TypeMapping.GetElementSizeExpression(typeInfo.CollectionElementType, "item", DataFormat.Default, calcVar);
                    if (sizeExpr != null)
                    {
                        _sb.AppendIndentedLine($"{sizeExpr};");
                    }
                    _sb.EndBlock();
                }
            }
            else if (elemInfo.IsEnum)
            {
                // MapEntry Level200: use UNPACKED encoding for enums (protobuf-net 2.3.7 compatibility)
                var (_, enumTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({enumTagBytes}); // tag for repeated field {fieldId} (unpacked)");
                _sb.AppendIndentedLine($"{calcVar}.WriteVarInt32((int)item);");
                _sb.EndBlock();
            }
            else if (elemInfo.IsCustomType)
            {
                // Custom types - each element is a repeated field with tag
                var (_, customTagBytes) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType, _typeRegistry);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                // CRITICAL: null check for array/collection elements (protobuf semantics allow null in arrays)
                _sb.AppendIndentedLine("if (item != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength({customTagBytes}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{elemInfo.ShortTypeName}ContentSize(ref itemCalc, item);");
                _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine($"{calcVar}.AddByteLength(itemCalc.Length);");
                _sb.EndBlock();
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

            // For collections, arrays, and dictionaries, tag is written per element inside the loop
            // Exception: byte[] is treated as a primitive and needs tag written here
            if (!typeInfo.IsCollection && !typeInfo.IsDictionary && (!typeInfo.IsArray || isByteArray))
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
                // Use .Value for nullable enums
                var enumValue = GetNullableValueAccess(sourceVar, typeName);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){enumValue});");
                return;
            }

            if (typeInfo.IsPrimitive)
            {
                // Get value access (adds .Value for nullable types)
                var actualValue = GetNullableValueAccess(sourceVar, typeName);

                // Try special types (String, Guid)
                if (SpecialTypeHandler.TryGenerateWrite(_sb, actualValue, typeName))
                {
                    return;
                }

                // Use TypeMapping for other primitives
                // Remove '?' from typeName for GetElementWriteExpression (it expects base type)
                var baseTypeName = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;
                var writeExpr = TypeMapping.GetElementWriteExpression(baseTypeName, actualValue, DataFormat.Default, "writer");
                if (writeExpr != null)
                {
                    _sb.AppendIndentedLine($"{writeExpr};");
                }
                return;
            }

            if (typeInfo.IsDictionary)
            {
                GenerateDictionaryFieldWrite(sourceVar, typeInfo, fieldId);
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

                // Handle nullable types - append .Value for Content method calls
                var valueAccess = GetNullableValueAccess(sourceVar, typeName);

                // Calculate size
                _sb.AppendIndentedLine($"var writeCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref writeCalc{fieldId}, {valueAccess});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)writeCalc{fieldId}.Length);");

                // Write content
                _sb.AppendIndentedLine($"{writersClass}.Write{className}Content(ref writer, {valueAccess});");
                return;
            }

            if (typeInfo.IsCustomType)
            {
                var sanitizedName = typeInfo.ShortTypeName;
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeName, _typeRegistry);
                var writersClass = NamespaceHelper.GetWritersClass(typeName, _writerClassName, _typeRegistry);

                // Handle nullable types - append .Value for Content method calls
                var valueAccess = GetNullableValueAccess(sourceVar, typeName);

                // Use cached length if available, otherwise recalculate
                if (cachedLengthVar != null)
                {
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){cachedLengthVar});");
                }
                else
                {
                    _sb.AppendIndentedLine($"var writeCalc{fieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{sanitizedName}ContentSize(ref writeCalc{fieldId}, {valueAccess});");
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)writeCalc{fieldId}.Length);");
                }

                _sb.AppendIndentedLine($"{writersClass}.Write{sanitizedName}Content(ref writer, {valueAccess});");
            }
        }

        private void GenerateDictionaryFieldWrite(string sourceVar, TypeAnalysisInfo typeInfo, int fieldId)
        {
            var mapEntryTypeName = typeInfo.MapEntryTypeName;
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);

            // NOTE: nestedDict variable already created in size calculation phase
            // Just use it directly here

            _sb.AppendIndentedLine("// Each inner entry is a repeated field with its own tag (protobuf-net Level200 format)");
            _sb.AppendIndentedLine("foreach (var kvp in nestedDict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId}");
            _sb.AppendIndentedLine($"{_writerClassName}.Write{mapEntryTypeName}(ref writer, kvp.Key, kvp.Value);");
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
                    // MapEntry Level200: use UNPACKED encoding for primitives (protobuf-net 2.3.7 compatibility)
                    // Inside MapEntry, repeated fields are serialized with one tag per element (not packed)
                    var wireType = TypeMapping.GetWireType(typeInfo.CollectionElementType, DataFormat.Default);
                    var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
                    _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString}); // tag for repeated field {fieldId} (unpacked)");
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
                // MapEntry Level200: use UNPACKED encoding for enums (protobuf-net 2.3.7 compatibility)
                var (enumBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"writer.WriteSingleByte({enumBytesString}); // tag for repeated field {fieldId} (unpacked)");
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)item);");
                _sb.EndBlock();
            }
            else if (elemInfo.IsCustomType)
            {
                // Custom types - each element is a repeated field with tag
                var (customBytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, WireType.Len);
                var sizeCalcClass = NamespaceHelper.GetSizeCalculatorsClass(typeInfo.CollectionElementType, _typeRegistry);
                var writersClass = NamespaceHelper.GetWritersClass(typeInfo.CollectionElementType, _writerClassName, _typeRegistry);
                _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                _sb.StartNewBlock();
                // CRITICAL: null check for array/collection elements (protobuf semantics allow null in arrays)
                _sb.AppendIndentedLine("if (item != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"writer.WriteSingleByte({customBytesString}); // tag for repeated field {fieldId}");
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcClass}.Calculate{elemInfo.ShortTypeName}ContentSize(ref itemCalc, item);");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine($"{writersClass}.Write{elemInfo.ShortTypeName}Content(ref writer, item);");
                _sb.EndBlock();
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

        /// <summary>
        /// Gets the correct value access expression for nullable types.
        /// For nullable value types (ending with ?), returns sourceVar.Value, otherwise returns sourceVar as-is.
        /// This is used when passing nullable parameters to Content methods that expect non-nullable types.
        /// </summary>
        private static string GetNullableValueAccess(string sourceVar, string typeName)
        {
            return typeName.EndsWith("?") ? $"{sourceVar}.Value" : sourceVar;
        }

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
                var baseType = typeInfo.ShortTypeName ?? typeName;
                // Preserve nullable marker if present in original typeName
                if (TypeHelper.IsNullableType(typeName) && !baseType.EndsWith("?"))
                {
                    return baseType + "?";
                }
                return baseType;
            }

            if (typeInfo.IsDictionary)
            {
                // Check if it's a custom dictionary type (ListDictionary, ConcurrentDictionary, etc.)
                if (TypeHelper.IsCustomDictionaryType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom dictionaries
                    return $"global::{typeInfo.FullTypeName}";
                }
                // Standard Dictionary<K,V> or IDictionary<K,V>
                return $"global::System.Collections.Generic.Dictionary<{typeInfo.DictionaryKeyType}, {typeInfo.DictionaryValueType}>";
            }

            if (typeInfo.IsList)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                // Check if it's a custom list type
                if (TypeHelper.IsCustomListType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom lists
                    return $"global::{typeInfo.FullTypeName}";
                }
                // Standard List<T>
                return $"global::System.Collections.Generic.List<{elemType}>";
            }

            if (typeInfo.IsHashSet)
            {
                var elemType = GetFullTypeName(typeInfo.CollectionElementType, typeInfo.CollectionElementTypeInfo);
                // Check if it's a custom hashset type (e.g., ValueLogTypeHashSet)
                if (TypeHelper.IsCustomHashSetType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom hashsets
                    return $"global::{typeInfo.FullTypeName}";
                }
                // Standard HashSet<T>
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
                // Check if it's a custom dictionary type (ListDictionary, ConcurrentDictionary, etc.)
                if (TypeHelper.IsCustomDictionaryType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom dictionaries
                    return $"new global::{typeInfo.FullTypeName}()";
                }
                // Standard Dictionary<K,V>
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
                // Check if it's a custom list type
                if (TypeHelper.IsCustomListType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom lists
                    return $"new global::{typeInfo.FullTypeName}()";
                }
                // Standard List<T>
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
                // Check if it's a custom hashset type (e.g., ValueLogTypeHashSet)
                if (TypeHelper.IsCustomHashSetType(typeInfo.FullTypeName))
                {
                    // Use the original type name for custom hashsets
                    return $"new global::{typeInfo.FullTypeName}()";
                }
                // Standard HashSet<T>
                return $"new global::System.Collections.Generic.HashSet<{elemType}>()";
            }

            // For strings, use ""
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            if (normalized == "System.String" || normalized == "string")
            {
                return "\"\"";
            }

            // For primitives and custom types, use default
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
