using System.Collections.Generic;
using GProtobuf.Generator.V2.Helpers;

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

            // ReadOnlySpan<byte> overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(ReadOnlySpan<byte> data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var list = new {returnType}();");
            _sb.AppendIndentedLine("var reader = new SpanReader(data);");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            if (info.ElementIsPrimitive)
            {
                // Primitive types: protobuf-net uses unpacked format [tag=0x08][value] for each element
                // tag 0x08 = field 1, wire type 0 (varint) for int/long/bool
                // or wire type 1 (fixed64) for double/long, wire type 5 (fixed32) for float
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                var expectedWireType = GetWireType(elementType);
                _sb.AppendIndentedLine($"if ((tag & 0x07) != {expectedWireType}) throw new InvalidDataException($\"Expected wire type {expectedWireType}, got {{tag & 0x07}}\");");
                var readExpr = GetPrimitiveReadExpression(elementType);
                _sb.AppendIndentedLine($"list.Add({readExpr});");
            }
            else
            {
                // Complex types: protobuf-net uses [tag=0x0A][length][message] for each item
                // tag 0x0A = field 1, wire type 2 (length-delimited)
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("if ((tag & 0x07) != 2) throw new InvalidDataException($\"Expected wire type 2, got {tag & 0x07}\");");
                GenerateComplexElementRead(elementType, "list.Add");
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine("return list;");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // byte[] overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(byte[] data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"return {methodName}(new ReadOnlySpan<byte>(data));");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateArrayDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var returnType = $"{GetGlobalTypeName(elementType)}[]";

            // ReadOnlySpan<byte> overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(ReadOnlySpan<byte> data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var list = new global::System.Collections.Generic.List<{GetGlobalTypeName(elementType)}>();");
            _sb.AppendIndentedLine("var reader = new SpanReader(data);");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();

            if (info.ElementIsPrimitive)
            {
                // Primitive types: [tag][value] for each element
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                var expectedWireType = GetWireType(elementType);
                _sb.AppendIndentedLine($"if ((tag & 0x07) != {expectedWireType}) throw new InvalidDataException($\"Expected wire type {expectedWireType}, got {{tag & 0x07}}\");");
                var readExpr = GetPrimitiveReadExpression(elementType);
                _sb.AppendIndentedLine($"list.Add({readExpr});");
            }
            else
            {
                // Complex types: [tag=0x0A][length][message] for each item
                _sb.AppendIndentedLine("var tag = reader.ReadVarUInt32();");
                _sb.AppendIndentedLine("if ((tag & 0x07) != 2) throw new InvalidDataException($\"Expected wire type 2, got {tag & 0x07}\");");
                GenerateComplexElementRead(elementType, "list.Add");
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine("return list.ToArray();");
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
            _sb.AppendIndentedLine("var subSpan = reader.GetSlice(length);");
            _sb.AppendIndentedLine("var subReader = new SpanReader(subSpan);");
            _sb.AppendIndentedLine($"{addMethod}(SpanReaders.Read{elementClassName}Content(ref subReader));");
        }

        private void GenerateDictionaryDeserializer(StandaloneTypeInfo info, string methodName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;
            var returnType = $"global::System.Collections.Generic.Dictionary<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";

            // ReadOnlySpan<byte> overload
            _sb.AppendIndentedLine($"public static {returnType} {methodName}(ReadOnlySpan<byte> data)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var dict = new {returnType}();");
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
            _sb.AppendIndentedLine($"{GetGlobalTypeName(valueType)} value = default;");
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
            GenerateFieldRead(keyType, "key");
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            // Value field (field 2)
            _sb.AppendIndentedLine("case 2:");
            _sb.IndentLevel++;
            GenerateFieldRead(valueType, "value");
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.AppendIndentedLine("default:");
            _sb.IndentLevel++;
            _sb.AppendIndentedLine("reader.SkipField((WireType)(tag & 0x07));");
            _sb.AppendIndentedLine("break;");
            _sb.IndentLevel--;

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while inner

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

        private void GenerateFieldRead(string typeName, string varName)
        {
            if (IsPrimitiveType(typeName))
            {
                // All primitives use GetPrimitiveReadExpression which includes proper WireType
                var readExpr = GetPrimitiveReadExpression(typeName);
                _sb.AppendIndentedLine($"{varName} = {readExpr};");
            }
            else
            {
                // Complex type - read as length-delimited, create sub-reader
                var className = TypeNameHelper.GetClassName(typeName);
                _sb.AppendIndentedLine($"var len_{varName} = (int)reader.ReadVarUInt32();");
                _sb.AppendIndentedLine($"var subSpan_{varName} = reader.GetSlice(len_{varName});");
                _sb.AppendIndentedLine($"var subReader_{varName} = new SpanReader(subSpan_{varName});");
                _sb.AppendIndentedLine($"{varName} = SpanReaders.Read{className}Content(ref subReader_{varName});");
            }
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

            // Stream serializer
            _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} list)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            _sb.AppendIndentedLine("foreach (var item in list)");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, info.ElementIsPrimitive, "item", "writer", false);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // IBufferWriter serializer
            _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} list)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
            _sb.AppendIndentedLine("foreach (var item in list)");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, info.ElementIsPrimitive, "item", "writer", true);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateArraySerializer(StandaloneTypeInfo info, string methodName)
        {
            var elementType = info.ElementType!;
            var paramType = $"{GetGlobalTypeName(elementType)}[]";

            // Stream serializer
            _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} array)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            _sb.AppendIndentedLine("foreach (var item in array)");
            _sb.StartNewBlock();
            GenerateElementWrite(elementType, info.ElementIsPrimitive, "item", "writer", false);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();

            // IBufferWriter serializer
            _sb.AppendIndentedLine($"public static void {methodName}(IBufferWriter<byte> buffer, {paramType} array)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
            _sb.AppendIndentedLine("foreach (var item in array)");
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
                _sb.AppendIndentedLine("var sizeCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref sizeCalc, {varName});");
                _sb.AppendIndentedLine($"{writerName}.WriteVarUInt32((uint)sizeCalc.Length);");
                var writerClass = isBufferWriter ? "BufferWriters" : "StreamWriters";
                _sb.AppendIndentedLine($"{writerClass}.Write{className}Content(ref {writerName}, {varName});");
            }
        }

        private void GenerateDictionarySerializer(StandaloneTypeInfo info, string methodName)
        {
            var keyType = info.KeyType!;
            var valueType = info.ValueType!;
            var paramType = $"global::System.Collections.Generic.Dictionary<{GetGlobalTypeName(keyType)}, {GetGlobalTypeName(valueType)}>";

            // Stream serializer
            _sb.AppendIndentedLine($"public static void {methodName}(Stream stream, {paramType} dict)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            _sb.AppendIndentedLine("foreach (var kvp in dict)");
            _sb.StartNewBlock();
            GenerateMapEntryWrite(keyType, valueType);
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
            GenerateMapEntryWrite(keyType, valueType);
            _sb.EndBlock();
            _sb.AppendIndentedLine("writer.Flush();");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateMapEntryWrite(string keyType, string valueType)
        {
            // protobuf-net format: [tag=0x0A][length][entry] for each map entry
            // Write outer tag first
            _sb.AppendIndentedLine("writer.WriteVarUInt32(0x0A); // field 1, wire type 2 (map entry)");

            // Calculate entry size
            _sb.AppendIndentedLine("// Calculate entry size");
            _sb.AppendIndentedLine($"var keySize = {GetFieldSizeExpression(keyType, "kvp.Key", 1)};");
            _sb.AppendIndentedLine($"var valueSize = {GetFieldSizeExpression(valueType, "kvp.Value", 2)};");
            _sb.AppendIndentedLine("var entrySize = keySize + valueSize;");
            _sb.AppendNewLine();

            // Write entry length
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)entrySize);");

            // Write key (field 1)
            _sb.AppendIndentedLine("// Key (field 1)");
            GenerateTaggedFieldWrite(keyType, "kvp.Key", 1);

            // Write value (field 2)
            _sb.AppendIndentedLine("// Value (field 2)");
            GenerateTaggedFieldWrite(valueType, "kvp.Value", 2);
        }

        private void GenerateTaggedFieldWrite(string typeName, string varName, int fieldNumber)
        {
            var wireType = GetWireType(typeName);
            var tag = (fieldNumber << 3) | wireType;

            _sb.AppendIndentedLine($"writer.WriteVarUInt32({tag}); // field {fieldNumber}, wire type {wireType}");

            if (IsPrimitiveType(typeName))
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
            else
            {
                // Complex type - write as length-delimited
                var className = TypeNameHelper.GetClassName(typeName);
                _sb.AppendIndentedLine($"var size_{fieldNumber} = SizeCalculators.Calculate{className}({varName});");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)size_{fieldNumber});");
                _sb.AppendIndentedLine($"StreamWriters.Write{className}Content(ref writer, {varName});");
            }
        }

        #endregion

        #region Helpers

        private static string GetGlobalTypeName(string typeName)
        {
            // For primitive types, return the C# keyword form
            switch (typeName)
            {
                case "int":
                case "System.Int32":
                    return "int";
                case "uint":
                case "System.UInt32":
                    return "uint";
                case "long":
                case "System.Int64":
                    return "long";
                case "ulong":
                case "System.UInt64":
                    return "ulong";
                case "short":
                case "System.Int16":
                    return "short";
                case "ushort":
                case "System.UInt16":
                    return "ushort";
                case "byte":
                case "System.Byte":
                    return "byte";
                case "sbyte":
                case "System.SByte":
                    return "sbyte";
                case "bool":
                case "System.Boolean":
                    return "bool";
                case "float":
                case "System.Single":
                    return "float";
                case "double":
                case "System.Double":
                    return "double";
                case "string":
                case "System.String":
                    return "string";
                default:
                    // For non-primitive types, use global:: prefix
                    return $"global::{typeName}";
            }
        }

        private static bool IsPrimitiveType(string typeName)
        {
            switch (typeName)
            {
                case "int":
                case "System.Int32":
                case "uint":
                case "System.UInt32":
                case "long":
                case "System.Int64":
                case "ulong":
                case "System.UInt64":
                case "short":
                case "System.Int16":
                case "ushort":
                case "System.UInt16":
                case "byte":
                case "System.Byte":
                case "sbyte":
                case "System.SByte":
                case "bool":
                case "System.Boolean":
                case "float":
                case "System.Single":
                case "double":
                case "System.Double":
                case "string":
                case "System.String":
                    return true;
                default:
                    return false;
            }
        }

        private static string GetPrimitiveReadExpression(string typeName)
        {
            switch (typeName)
            {
                case "int":
                case "System.Int32":
                    return "reader.ReadVarInt32()";
                case "uint":
                case "System.UInt32":
                    return "reader.ReadVarUInt32()";
                case "long":
                case "System.Int64":
                    return "reader.ReadVarInt64()";
                case "ulong":
                case "System.UInt64":
                    return "reader.ReadVarUInt64()";
                case "bool":
                case "System.Boolean":
                    return "reader.ReadVarUInt32() != 0";
                case "string":
                case "System.String":
                    return "reader.ReadString(WireType.Len)";
                case "double":
                case "System.Double":
                    return "reader.ReadDouble(WireType.Fixed64b)";
                case "float":
                case "System.Single":
                    return "reader.ReadFloat(WireType.Fixed32b)";
                default:
                    return "reader.ReadVarInt32()";
            }
        }

        private static string GetPrimitiveWriteExpression(string typeName, string varName)
        {
            switch (typeName)
            {
                case "int":
                case "System.Int32":
                    return $"WriteVarInt32({varName})";
                case "uint":
                case "System.UInt32":
                    return $"WriteVarUInt32({varName})";
                case "long":
                case "System.Int64":
                    return $"WriteVarInt64({varName})";
                case "ulong":
                case "System.UInt64":
                    return $"WriteVarUInt64({varName})";
                case "bool":
                case "System.Boolean":
                    return $"WriteVarUInt32({varName} ? 1u : 0u)";
                case "string":
                case "System.String":
                    return $"WriteString({varName})";
                case "double":
                case "System.Double":
                    return $"WriteDouble({varName})";
                case "float":
                case "System.Single":
                    return $"WriteFloat({varName})";
                default:
                    return $"WriteVarInt32({varName})";
            }
        }

        private static string GetPrimitiveSizeExpression(string typeName, string varName)
        {
            switch (typeName)
            {
                case "int":
                case "System.Int32":
                    return $"global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})";
                case "uint":
                case "System.UInt32":
                    return $"global::GProtobuf.Core.Utils.GetVarintSize({varName})";
                case "long":
                case "System.Int64":
                    return $"global::GProtobuf.Core.Utils.GetVarInt64Size({varName})";
                case "ulong":
                case "System.UInt64":
                    return $"global::GProtobuf.Core.Utils.GetVarUInt64Size({varName})";
                case "bool":
                case "System.Boolean":
                    return "1";
                case "string":
                case "System.String":
                    // String size = length prefix (varint) + UTF-8 byte count
                    return $"(global::System.Text.Encoding.UTF8.GetByteCount({varName}) is var _strLen_{varName.Replace(".", "_")} ? global::GProtobuf.Core.Utils.GetVarintSize((uint)_strLen_{varName.Replace(".", "_")}) + _strLen_{varName.Replace(".", "_")} : 0)";
                case "double":
                case "System.Double":
                    return "8";
                case "float":
                case "System.Single":
                    return "4";
                default:
                    return $"global::GProtobuf.Core.Utils.GetVarintSize((uint){varName})";
            }
        }

        private static string GetFieldSizeExpression(string typeName, string varName, int fieldNumber)
        {
            var tagSize = fieldNumber < 16 ? 1 : 2; // Tag size (field number + wire type)

            if (IsPrimitiveType(typeName))
            {
                switch (typeName)
                {
                    case "string":
                    case "System.String":
                        // String: tag + length prefix (varint) + UTF-8 bytes
                        var safeVarName = varName.Replace(".", "_");
                        return $"(global::System.Text.Encoding.UTF8.GetByteCount({varName}) is var _strLen_{safeVarName} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_strLen_{safeVarName}) + _strLen_{safeVarName} : 0)";
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
            else
            {
                var className = TypeNameHelper.GetClassName(typeName);
                // Complex type: tag + length prefix (varint) + content
                return $"(SizeCalculators.Calculate{className}({varName}) is var _contentSize_{fieldNumber} ? {tagSize} + global::GProtobuf.Core.Utils.GetVarintSize((uint)_contentSize_{fieldNumber}) + _contentSize_{fieldNumber} : 0)";
            }
        }

        private static int GetWireType(string typeName)
        {
            switch (typeName)
            {
                // Varint types (wire type 0)
                case "int":
                case "System.Int32":
                case "uint":
                case "System.UInt32":
                case "long":
                case "System.Int64":
                case "ulong":
                case "System.UInt64":
                case "short":
                case "System.Int16":
                case "ushort":
                case "System.UInt16":
                case "byte":
                case "System.Byte":
                case "sbyte":
                case "System.SByte":
                case "bool":
                case "System.Boolean":
                    return 0; // Varint
                // Fixed64 (wire type 1)
                case "double":
                case "System.Double":
                    return 1; // Fixed64
                // Fixed32 (wire type 5)
                case "float":
                case "System.Single":
                    return 5; // Fixed32
                // Length-delimited (wire type 2)
                case "string":
                case "System.String":
                    return 2; // Length-delimited
                default:
                    return 2; // Length-delimited (complex types)
            }
        }

        #endregion
    }
}
