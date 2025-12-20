using System;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Handles code generation for map/dictionary types in protobuf.
    /// Maps are serialized as repeated length-delimited messages with key (field 1) and value (field 2).
    /// </summary>
    internal class MapHandler
    {
        private readonly StringBuilderWithIndent _sb;

        public MapHandler(StringBuilderWithIndent sb)
        {
            _sb = sb;
        }

        #region Read (Deserialization)

        /// <summary>
        /// Generates code to read a map field.
        /// </summary>
        public void GenerateRead(ProtoMemberAttribute member, string targetVar)
        {
            var keyType = member.MapKeyType;
            var valueType = member.MapValueType;
            var dictCreationType = GetDictionaryCreationType(member.Type, keyType, valueType);

            // Initialize dictionary if null
            _sb.AppendIndentedLine($"{targetVar} ??= new {dictCreationType}();");

            // Read entry length
            _sb.AppendIndentedLine("var entryLength = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var entryEnd = reader.Position + (int)entryLength;");

            // Initialize key/value with defaults
            _sb.AppendIndentedLine($"{keyType} key = default;");
            _sb.AppendIndentedLine($"{valueType} value = default;");

            // Read entry fields
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
            GeneratePrimitiveRead("key", keyType, member.MapKeyIsEnum);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Field 2: Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GenerateValueRead(valueType, member.MapValueIsEnum);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Default: skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField((global::GProtobuf.Core.WireType)entryWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while

            // Add to dictionary/collection
            if (IsKeyValuePairCollection(member.Type))
            {
                _sb.AppendIndentedLine($"{targetVar}.Add(new global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>(key, value));");
            }
            else
            {
                _sb.AppendIndentedLine($"{targetVar}[key] = value;");
            }
        }

        private void GeneratePrimitiveRead(string targetVar, string typeName, bool isEnum)
        {
            if (isEnum)
            {
                _sb.AppendIndentedLine($"{targetVar} = ({typeName})reader.ReadVarInt32();");
                return;
            }

            // Use TypeMapping for centralized read expression
            var readExpr = TypeMapping.GetElementReadExpression(typeName, DataFormat.Default, "reader");
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"{targetVar} = {readExpr};");
                return;
            }

            // Special cases not in TypeMapping
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine($"{targetVar} = global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len);");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetVar} = reader.ReadGuid();");
                    break;
                default:
                    _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
                    break;
            }
        }

        private void GenerateValueRead(string valueType, bool isEnum)
        {
            if (isEnum)
            {
                _sb.AppendIndentedLine($"value = ({valueType})reader.ReadVarInt32();");
                return;
            }

            // Check for primitive types using TypeMapping
            var readExpr = TypeMapping.GetElementReadExpression(valueType, DataFormat.Default, "reader");
            if (readExpr != null)
            {
                _sb.AppendIndentedLine($"value = {readExpr};");
                return;
            }

            // Check special types
            var normalized = TypeMapping.NormalizeTypeName(valueType);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine("value = global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len);");
                    return;
                case "System.Guid":
                    _sb.AppendIndentedLine("value = reader.ReadGuid();");
                    return;
            }

            // Complex types
            if (valueType.EndsWith("[]"))
            {
                GeneratePackedArrayValueRead(valueType);
            }
            else if (IsListType(valueType) || IsHashSetType(valueType))
            {
                GenerateCollectionValueRead(valueType);
            }
            else
            {
                // Nested message type
                var sanitizedName = TypeNameHelper.GetClassName(valueType);
                _sb.AppendIndentedLine("var valueLength = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("var valueEnd = reader.Position + (int)valueLength;");
                _sb.AppendIndentedLine($"value = new global::{valueType}();");
                _sb.AppendIndentedLine($"SpanReaders.Populate{sanitizedName}(ref reader, value);");
                _sb.AppendIndentedLine("reader.Position = valueEnd;");
            }
        }

        private void GeneratePackedArrayValueRead(string valueType)
        {
            var elementType = valueType.Substring(0, valueType.Length - 2);

            // Try to use optimized packed array read from SpanReader
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, "reader");
            if (packedReadExpr != null)
            {
                _sb.AppendIndentedLine($"value = {packedReadExpr};");
                return;
            }

            // Fallback for types without optimized packed read (e.g., string)
            var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, "reader");
            if (elementReadExpr == null)
            {
                _sb.AppendIndentedLine($"// Unsupported array element type: {elementType}");
                return;
            }

            var shortType = TypeMapping.GetShortTypeName(elementType);
            _sb.AppendIndentedLine("var packedLength = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var packedEnd = reader.Position + (int)packedLength;");
            _sb.AppendIndentedLine($"var tempList = new global::System.Collections.Generic.List<{shortType}>();");
            _sb.AppendIndentedLine("while (reader.Position < packedEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"tempList.Add({elementReadExpr});");
            _sb.EndBlock();
            _sb.AppendIndentedLine("value = tempList.ToArray();");
        }

        private void GenerateCollectionValueRead(string valueType)
        {
            var elementType = GetCollectionElementType(valueType);
            var isHashSet = IsHashSetType(valueType);
            var shortElementType = TypeMapping.GetShortTypeName(elementType);

            // Try to use optimized packed array read, then convert to collection
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, "reader");
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
            var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, "reader");
            if (elementReadExpr == null)
            {
                // Handle string specially
                if (TypeMapping.NormalizeTypeName(elementType) == "System.String")
                {
                    elementReadExpr = "global::GProtobuf.Core.SpanReaders.ReadString(ref reader, global::GProtobuf.Core.WireType.Len)";
                }
                else
                {
                    _sb.AppendIndentedLine($"// Unsupported collection element type: {elementType}");
                    return;
                }
            }

            _sb.AppendIndentedLine("var packedLength = reader.ReadVarUInt32();");
            _sb.AppendIndentedLine("var packedEnd = reader.Position + (int)packedLength;");

            if (isHashSet)
            {
                _sb.AppendIndentedLine($"var tempCollection = new global::System.Collections.Generic.HashSet<{shortElementType}>();");
            }
            else
            {
                _sb.AppendIndentedLine($"var tempCollection = new global::System.Collections.Generic.List<{shortElementType}>();");
            }

            _sb.AppendIndentedLine("while (reader.Position < packedEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"tempCollection.Add({elementReadExpr});");
            _sb.EndBlock();
            _sb.AppendIndentedLine("value = tempCollection;");
        }

        #endregion

        #region Write (Serialization)

        /// <summary>
        /// Generates code to write a map field.
        /// </summary>
        public void GenerateWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var keyType = member.MapKeyType;
            var valueType = member.MapValueType;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            // Reuse calculators outside the loop to reduce allocations
            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Create nested calculator if value type needs it (nested messages or variable-size collections)
            if (NeedsNestedCalculator(valueType))
            {
                _sb.AppendIndentedLine("var nestedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            }

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            // Skip null values for reference types
            if (!IsPrimitiveType(valueType) && !valueType.EndsWith("[]"))
            {
                _sb.AppendIndentedLine("if (kvp.Value == null) continue;");
            }

            // Reset calculator for each entry
            _sb.AppendIndentedLine("entryCalc.Reset();");

            // Calculate entry size
            GenerateEntrySizeCalculation(member, "kvp.Key", "kvp.Value", "entryCalc");

            // Write tag and length
            GenerateWriteTag(member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entryCalc.Length);");

            // Write key
            GeneratePrimitiveWrite(keyType, "kvp.Key", member.MapKeyIsEnum, 1);

            // Write value
            GenerateValueWrite(valueType, "kvp.Value", member.MapValueIsEnum);

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
        }

        private void GeneratePrimitiveWrite(string typeName, string sourceVar, bool isEnum, int fieldId)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(typeName, DataFormat.Default);
            GenerateWriteTagBytes(fieldId, wireType);

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

            // Special cases
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine($"writer.WriteString({sourceVar});");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"writer.WriteGuid({sourceVar});");
                    break;
                default:
                    _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
                    break;
            }
        }

        private void GenerateValueWrite(string valueType, string sourceVar, bool isEnum)
        {
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(valueType, DataFormat.Default);
            GenerateWriteTagBytes(2, wireType);

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

            // Special types
            var normalized = TypeMapping.NormalizeTypeName(valueType);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine($"writer.WriteString({sourceVar});");
                    return;
                case "System.Guid":
                    _sb.AppendIndentedLine($"writer.WriteGuid({sourceVar});");
                    return;
            }

            // Complex types
            if (valueType.EndsWith("[]") || IsListType(valueType) || IsHashSetType(valueType))
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
                _sb.AppendIndentedLine($"StreamWriters.Write{sanitizedName}Content(ref writer, {sourceVar});");
            }
        }

        private void GenerateCollectionValueWrite(string valueType, string sourceVar)
        {
            var elementType = valueType.EndsWith("[]")
                ? valueType.Substring(0, valueType.Length - 2)
                : GetCollectionElementType(valueType);

            var fixedSize = GetFixedElementSize(elementType);

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

            // Special case for string
            if (TypeMapping.NormalizeTypeName(elementType) == "System.String")
            {
                _sb.AppendIndentedLine($"{calcVar}.WriteString({itemVar});");
            }
            else
            {
                _sb.AppendIndentedLine($"// Unsupported element type for size calculation: {elementType}");
            }
        }

        private void GenerateElementWrite(string elementType, string itemVar)
        {
            var writeExpr = TypeMapping.GetElementWriteExpression(elementType, itemVar, DataFormat.Default, "writer");
            if (writeExpr != null)
            {
                _sb.AppendIndentedLine($"{writeExpr};");
                return;
            }

            // Special case for string
            if (TypeMapping.NormalizeTypeName(elementType) == "System.String")
            {
                _sb.AppendIndentedLine($"writer.WriteString({itemVar});");
            }
            else
            {
                _sb.AppendIndentedLine($"// Unsupported element type for write: {elementType}");
            }
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates code to calculate size of a map field.
        /// </summary>
        public void GenerateSize(ProtoMemberAttribute member, string sourceVar)
        {
            var valueType = member.MapValueType;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            // Reuse calculators outside the loop to reduce allocations
            _sb.AppendIndentedLine("var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Create nested calculator if value type needs it (nested messages or variable-size collections)
            if (NeedsNestedCalculator(valueType))
            {
                _sb.AppendIndentedLine("var nestedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            }

            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            // Skip null values for reference types
            if (!IsPrimitiveType(valueType) && !valueType.EndsWith("[]"))
            {
                _sb.AppendIndentedLine("if (kvp.Value == null) continue;");
            }

            // Reset calculator for each entry
            _sb.AppendIndentedLine("entryCalc.Reset();");

            // Calculate entry size
            GenerateEntrySizeCalculation(member, "kvp.Key", "kvp.Value", "entryCalc");

            // Add tag and length prefix size
            var (_, tagBytes) = TypeMapping.PrecomputeTagBytes(member.FieldId, WireType.Len);
            _sb.AppendIndentedLine($"calculator.AddByteLength({tagBytes});");
            _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)entryCalc.Length);");
            _sb.AppendIndentedLine("calculator.AddByteLength(entryCalc.Length);");

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
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

            // Special cases
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine($"{calcVar}.WriteString({sourceVar});");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(18);"); // 2 bytes length prefix + 16 bytes
                    break;
                default:
                    _sb.AppendIndentedLine($"// Unsupported type: {typeName}");
                    break;
            }
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

            // Special types
            var normalized = TypeMapping.NormalizeTypeName(valueType);
            switch (normalized)
            {
                case "System.String":
                    _sb.AppendIndentedLine($"{calcVar}.WriteString({sourceVar});");
                    return;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength(18);");
                    return;
            }

            // Complex types
            if (valueType.EndsWith("[]") || IsListType(valueType) || IsHashSetType(valueType))
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
                : GetCollectionElementType(valueType);

            var fixedSize = GetFixedElementSize(elementType);

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

        private void GenerateWriteTag(int fieldId, WireType wireType)
        {
            var (bytesString, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            if (byteCount == 1)
            {
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString});");
            }
            else
            {
                var tagPropertyName = CodeGeneration.TagsGenerator.GetTagPropertyName(fieldId, wireType);
                _sb.AppendIndentedLine($"writer.WriteBytes(Tags.{tagPropertyName});");
            }
        }

        private void GenerateWriteTagBytes(int fieldId, WireType wireType)
        {
            var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            // For map entry tags (1 and 2), they're always single byte
            _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString});");
        }

        /// <summary>
        /// Returns fixed size in bytes for types with constant size, or 0 for variable-size types.
        /// </summary>
        private static int GetFixedElementSize(string elementType)
        {
            var normalized = TypeMapping.NormalizeTypeName(elementType);
            return normalized switch
            {
                "System.Single" => 4,
                "System.Double" => 8,
                "System.Boolean" => 1,
                "System.Byte" => 1,
                "System.SByte" => 1,
                // VarInt types have variable size
                _ => 0
            };
        }

        /// <summary>
        /// Checks if value type needs a nested calculator (for nested messages or variable-size collections).
        /// </summary>
        private static bool NeedsNestedCalculator(string valueType)
        {
            // Primitive types don't need nested calculator
            if (IsPrimitiveType(valueType))
                return false;

            // Arrays with fixed-size elements don't need nested calculator
            if (valueType.EndsWith("[]"))
            {
                var elementType = valueType.Substring(0, valueType.Length - 2);
                return GetFixedElementSize(elementType) == 0;
            }

            // List/HashSet with fixed-size elements don't need nested calculator
            if (IsListType(valueType) || IsHashSetType(valueType))
            {
                var elementType = GetCollectionElementType(valueType);
                return GetFixedElementSize(elementType) == 0;
            }

            // Nested message types need nested calculator
            return true;
        }

        private static string GetDictionaryCreationType(string mapType, string keyType, string valueType)
        {
            // For List<KeyValuePair<K,V>> we need to use List for intermediate storage
            if (mapType.Contains("List<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>>";
            }

            // For ICollection<KeyValuePair<K,V>> use List
            if (mapType.Contains("ICollection<") && mapType.Contains("KeyValuePair<"))
            {
                return $"global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>>";
            }

            // For interface types (IDictionary<K,V>), use Dictionary<K,V>
            if (mapType.Contains("IDictionary<"))
            {
                return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            // For concrete Dictionary<K,V> or custom types, use the full type
            if (mapType.Contains("Dictionary<") && !IsCustomDictionaryType(mapType))
            {
                return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }

            // For custom derived dictionary types, use the full qualified type name
            return $"global::{mapType}";
        }

        private static bool IsCustomDictionaryType(string mapType)
        {
            return !mapType.StartsWith("System.Collections.Generic.Dictionary<") &&
                   !mapType.StartsWith("Dictionary<") &&
                   mapType.Contains("Dictionary");
        }

        private static bool IsKeyValuePairCollection(string mapType)
        {
            return (mapType.Contains("List<") || mapType.Contains("ICollection<")) &&
                   mapType.Contains("KeyValuePair<");
        }

        private static bool IsPrimitiveType(string typeName)
        {
            return TypeMapping.IsSimpleType(typeName) ||
                   TypeMapping.NormalizeTypeName(typeName) == "System.Guid";
        }

        private static bool IsListType(string typeName)
        {
            return typeName.Contains("List<") && !typeName.Contains("KeyValuePair");
        }

        private static bool IsHashSetType(string typeName)
        {
            return typeName.Contains("HashSet<");
        }

        private static string GetCollectionElementType(string collectionType)
        {
            // Extract element type from List<T> or HashSet<T>
            var startIndex = collectionType.IndexOf('<') + 1;
            var endIndex = collectionType.LastIndexOf('>');
            if (startIndex > 0 && endIndex > startIndex)
            {
                return collectionType.Substring(startIndex, endIndex - startIndex);
            }
            return collectionType;
        }

        #endregion
    }
}
