using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates OnePassStreamWriters class with Write{ClassName} methods.
    /// Uses OnePassStreamWriter with BeginSubMessage/EndSubMessage for nested messages.
    /// This is a simpler one-pass approach compared to the two-pass StreamWriterGenerator.
    /// </summary>
    internal class OnePassStreamWriterGenerator : GeneratorBase
    {
        private const string WriterType = "global::GProtobuf.Core.OnePassStreamWriter";
        private const string ClassName = "OnePassStreamWriters";

        public OnePassStreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry, null, null, passRegistryToPrimitiveHandler: true)
        {
        }

        public OnePassStreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry, null, passRegistryToPrimitiveHandler: true)
        {
        }

        public OnePassStreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true)
        {
        }

        public OnePassStreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string virtualTypesNamespace)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options: null, virtualTypesNamespace: virtualTypesNamespace)
        {
        }

        /// <summary>
        /// Generates OnePassStreamWriters class containing ONLY virtual types (map entries and tuples).
        /// Used for GProtobuf.Generated namespace which centralizes all virtual type methods.
        /// </summary>
        public void GenerateVirtualTypesOnly(string currentNamespace)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {ClassName}");
            _sb.StartNewBlock();

            // Generate virtual map entry writers (all types, ignoring IsGenerated flag)
            GenerateVirtualMapEntryWriters(ignoreIsGeneratedFlag: true);

            // Generate virtual tuple writers (all types, ignoring IsGenerated flag)
            GenerateVirtualTupleWriters(ignoreIsGeneratedFlag: true);

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates complete OnePassStreamWriters class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {ClassName}");
            _sb.StartNewBlock();

            var typesList = types.ToList();

            foreach (var type in typesList)
            {
                GenerateWriteMethod(type);
            }

            // Generate WriteContent methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new HashSet<string>(typesList.Select(t => t.FullName));
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                    GenerateWriteContentMethod(protoIncludeType, className);
                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            // Virtual map entry and tuple writers are NOT generated here - they are centralized
            // in GProtobuf.Generated.Serialization.cs via GenerateVirtualTypesOnly().
            // Types are registered during field processing above, then generated once in the shared file.

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates writer methods for all registered virtual map entry types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualMapEntryWriters(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualMapRegistry.GetAllTypes();

            // Filter types based on IsGenerated flag
            var virtualTypes = ignoreIsGeneratedFlag
                ? allTypes.ToList()
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (virtualTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Writers (OnePass)");

            foreach (var virtualType in virtualTypes)
            {
                GenerateVirtualMapEntryWriter(virtualType);
            }
        }

        private void GenerateVirtualMapEntryWriter(VirtualMapEntryInfo virtualType)
        {
            var methodName = $"Write{virtualType.TypeName}";
            var keyTypeName = GetGlobalTypeName(virtualType.KeyType);
            var valueTypeName = GetGlobalTypeName(virtualType.ValueType);

            _sb.AppendIndentedLine($"public static void {methodName}(ref {WriterType} writer, {keyTypeName} key, {valueTypeName} value)");
            _sb.StartNewBlock();

            // Write key (field 1)
            GenerateMapKeyWrite(virtualType, "key");

            // Write value (field 2)
            GenerateMapValueWrite(virtualType, "value");

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateMapKeyWrite(VirtualMapEntryInfo virtualType, string sourceVar)
        {
            var keyTypeInfo = virtualType.KeyTypeInfo;

            // Check both KeyIsEnum flag and KeyTypeInfo.IsEnum for enum detection
            // This handles cases where enum wasn't detected during registration
            if (virtualType.KeyIsEnum || keyTypeInfo?.IsEnum == true)
            {
                TagCodeHelper.WriteTag(_sb, 1, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
            }
            else if (_primitiveHandler.CanHandle(virtualType.KeyType))
            {
                _primitiveHandler.GenerateWrite(_sb, sourceVar, virtualType.KeyType, DataFormat.Default, 1, false, false);
            }
            else if (TupleHandler.IsTupleType(virtualType.KeyType))
            {
                // Tuple key type - use WriteTupleName (no Content suffix in OnePass mode)
                var itemTypes = TupleHandler.ParseTupleTypes(virtualType.KeyType);
                var tupleInfo = _virtualTupleRegistry.Register(virtualType.KeyType, itemTypes);
                TagCodeHelper.WriteTag(_sb, 1, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{tupleInfo.SafeName}(ref writer, {sourceVar});");
                _sb.AppendIndentedLine("writer.EndSubMessage();");
            }
            else if (keyTypeInfo?.IsArray == true)
            {
                // Array key type - write each element as repeated field 1
                GenerateArrayKeyWrite(virtualType, sourceVar, keyTypeInfo);
            }
            else if (keyTypeInfo?.IsList == true || keyTypeInfo?.IsHashSet == true || keyTypeInfo?.IsCollection == true)
            {
                // Collection key type - write each element as repeated field 1
                GenerateCollectionKeyWrite(virtualType, sourceVar, keyTypeInfo);
            }
            else if (keyTypeInfo?.IsDictionary == true)
            {
                // Dictionary key type - write as nested map entries
                GenerateDictionaryKeyWrite(virtualType, sourceVar, keyTypeInfo);
            }
            else if (keyTypeInfo?.IsProtoVarint == true)
            {
                // ProtoVarint key type - write as simple varint
                var valueMember = keyTypeInfo.ProtoVarintValueMember ?? ProtoVarintConstants.DefaultValueMember;
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(keyTypeInfo.ProtoVarintType);
                TagCodeHelper.WriteTag(_sb, 1, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.{writeMethod}({sourceVar}.{valueMember});");
            }
            else
            {
                // Custom ProtoContract key type - use namespace-qualified call
                var keyClassName = TypeNameHelper.GetClassName(virtualType.KeyType);
                var writersClass = GetWritersClass(virtualType.KeyType);
                var keyTypeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(virtualType.KeyType));
                var keyWriteMethod = (keyTypeDef == null || CanSkipWriteContentMethod(keyTypeDef))
                    ? $"Write{keyClassName}"
                    : $"Write{keyClassName}Content";
                TagCodeHelper.WriteTag(_sb, 1, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"{writersClass}.{keyWriteMethod}(ref writer, {sourceVar});");
                _sb.AppendIndentedLine("writer.EndSubMessage();");
            }
        }

        private void GenerateArrayKeyWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo keyTypeInfo)
        {
            var elementType = keyTypeInfo.CollectionElementType;
            var elemInfo = keyTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var keyItem in {sourceVar})");
            _sb.StartNewBlock();
            GenerateRepeatedElementWrite(elementType, elemInfo, "keyItem", 1);
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionKeyWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo keyTypeInfo)
        {
            var elementType = keyTypeInfo.CollectionElementType;
            var elemInfo = keyTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var keyItem in {sourceVar})");
            _sb.StartNewBlock();
            GenerateRepeatedElementWrite(elementType, elemInfo, "keyItem", 1);
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateDictionaryKeyWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo keyTypeInfo)
        {
            var innerKeyType = keyTypeInfo.DictionaryKeyType;
            var innerValueType = keyTypeInfo.DictionaryValueType;

            bool keyIsEnum = _registry?.IsEnum(innerKeyType) ?? false;
            bool valueIsEnum = _registry?.IsEnum(innerValueType) ?? false;

            var nestedEntryInfo = _virtualMapRegistry.RegisterMapEntry(innerKeyType, innerValueType, keyIsEnum, valueIsEnum);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var keyKvp in {sourceVar})");
            _sb.StartNewBlock();
            TagCodeHelper.WriteTag(_sb, 1, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{nestedEntryInfo.TypeName}(ref writer, keyKvp.Key, keyKvp.Value);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateMapValueWrite(VirtualMapEntryInfo virtualType, string sourceVar)
        {
            var valueTypeInfo = virtualType.ValueTypeInfo;

            // Check both ValueIsEnum flag and ValueTypeInfo.IsEnum for enum detection
            // This handles cases where enum wasn't detected during registration
            if (virtualType.ValueIsEnum || valueTypeInfo?.IsEnum == true)
            {
                TagCodeHelper.WriteTag(_sb, 2, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                return;
            }

            // Check for nullable struct types (T? where T is a custom struct)
            // This must be checked before GetNullableUnderlyingType which only handles simple types
            if (TypeHelper.IsNullableType(virtualType.ValueType))
            {
                var potentialUnderlyingType = TypeHelper.GetNullableUnderlyingType(virtualType.ValueType);
                var potentialTypeInfo = _virtualMapRegistry.AnalyzeType(potentialUnderlyingType);
                if (potentialTypeInfo != null && potentialTypeInfo.IsStruct)
                {
                    // Nullable struct - use .Value accessor
                    GenerateNullableCustomTypeValueWrite(potentialUnderlyingType, potentialTypeInfo, sourceVar);
                    return;
                }
            }

            // Check for nullable types (System.Nullable<T> or T?)
            var underlyingType = GetNullableUnderlyingType(virtualType.ValueType);
            if (underlyingType != null)
            {
                // Nullable type - check if underlying is primitive
                if (_primitiveHandler.CanHandle(underlyingType))
                {
                    // Nullable primitive - check HasValue and access .Value
                    _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                    _sb.StartNewBlock();
                    _primitiveHandler.GenerateWrite(_sb, $"{sourceVar}.Value", underlyingType, DataFormat.Default, 2, false, false);
                    _sb.EndBlock();
                    return;
                }

                // Nullable custom type (enum serialized as complex type, or ProtoContract struct)
                // Use the underlying type for writing, with null check and .Value accessor
                var underlyingTypeInfo = _virtualMapRegistry.AnalyzeType(underlyingType);
                GenerateNullableCustomTypeValueWrite(underlyingType, underlyingTypeInfo, sourceVar);
                return;
            }

            if (_primitiveHandler.CanHandle(virtualType.ValueType))
            {
                _primitiveHandler.GenerateWrite(_sb, sourceVar, virtualType.ValueType, DataFormat.Default, 2, false, false);
                return;
            }

            // Handle array value types (int[], string[], CustomClass[])
            if (valueTypeInfo?.IsArray == true)
            {
                GenerateArrayValueWrite(virtualType, sourceVar, valueTypeInfo);
                return;
            }

            // Handle collection value types (List<T>, HashSet<T>)
            if (valueTypeInfo?.IsList == true || valueTypeInfo?.IsHashSet == true || valueTypeInfo?.IsCollection == true)
            {
                GenerateCollectionValueWrite(virtualType, sourceVar, valueTypeInfo);
                return;
            }

            // Handle dictionary value types (Dictionary<K,V>)
            if (valueTypeInfo?.IsDictionary == true)
            {
                GenerateDictionaryValueWrite(virtualType, sourceVar, valueTypeInfo);
                return;
            }

            // Handle tuple value types
            if (TupleHandler.IsTupleType(virtualType.ValueType))
            {
                GenerateTupleValueWrite(virtualType, sourceVar);
                return;
            }

            // Handle ProtoVarint value types - write as simple varint
            if (valueTypeInfo?.IsProtoVarint == true)
            {
                var valueMember = valueTypeInfo.ProtoVarintValueMember ?? ProtoVarintConstants.DefaultValueMember;
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(valueTypeInfo.ProtoVarintType);
                TagCodeHelper.WriteTag(_sb, 2, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.{writeMethod}({sourceVar}.{valueMember});");
                return;
            }

            // ProtoContract custom type - use BeginSubMessage/EndSubMessage with WriteXXXContent
            GenerateCustomTypeValueWrite(virtualType, sourceVar, valueTypeInfo);
        }

        private void GenerateNullableCustomTypeValueWrite(string underlyingType, TypeAnalysisInfo underlyingTypeInfo, string sourceVar)
        {
            var valueClassName = TypeNameHelper.GetClassName(underlyingType);
            var writersClass = GetWritersClass(underlyingType);
            var valTypeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(underlyingType));
            var valWriteMethod = (valTypeDef == null || CanSkipWriteContentMethod(valTypeDef))
                ? $"Write{valueClassName}"
                : $"Write{valueClassName}Content";

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            TagCodeHelper.WriteTag(_sb, 2, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{writersClass}.{valWriteMethod}(ref writer, {sourceVar}.Value);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
            _sb.EndBlock();
        }

        private void GenerateArrayValueWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo valueTypeInfo)
        {
            var elementType = valueTypeInfo.CollectionElementType;
            var elemInfo = valueTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            GenerateRepeatedElementWrite(elementType, elemInfo, "item", 2);

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionValueWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo valueTypeInfo)
        {
            var elementType = valueTypeInfo.CollectionElementType;
            var elemInfo = valueTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            GenerateRepeatedElementWrite(elementType, elemInfo, "item", 2);

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateDictionaryValueWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo valueTypeInfo)
        {
            var innerKeyType = valueTypeInfo.DictionaryKeyType;
            var innerValueType = valueTypeInfo.DictionaryValueType;

            // Check if key/value types are enums using the registry
            bool keyIsEnum = _registry?.IsEnum(innerKeyType) ?? false;
            bool valueIsEnum = _registry?.IsEnum(innerValueType) ?? false;

            // Register nested map entry type
            var nestedEntryInfo = _virtualMapRegistry.RegisterMapEntry(
                innerKeyType, innerValueType,
                keyIsEnum,
                valueIsEnum);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            // Each nested dictionary entry is written as repeated field 2 with BeginSubMessage/EndSubMessage
            TagCodeHelper.WriteTag(_sb, 2, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{nestedEntryInfo.TypeName}(ref writer, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateTupleValueWrite(VirtualMapEntryInfo virtualType, string sourceVar)
        {
            var itemTypes = TupleHandler.ParseTupleTypes(virtualType.ValueType);
            var tupleInfo = _virtualTupleRegistry.Register(virtualType.ValueType, itemTypes);

            TagCodeHelper.WriteTag(_sb, 2, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{tupleInfo.SafeName}(ref writer, {sourceVar});");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
        }

        private void GenerateCustomTypeValueWrite(VirtualMapEntryInfo virtualType, string sourceVar, TypeAnalysisInfo valueTypeInfo)
        {
            var valueClassName = TypeNameHelper.GetClassName(virtualType.ValueType);
            var writersClass = GetWritersClass(virtualType.ValueType);
            var valTypeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(virtualType.ValueType));
            var valWriteMethod = (valTypeDef == null || CanSkipWriteContentMethod(valTypeDef))
                ? $"Write{valueClassName}"
                : $"Write{valueClassName}Content";
            bool needsNullCheck = valueTypeInfo != null && !valueTypeInfo.IsPrimitive && !valueTypeInfo.IsStruct;

            if (needsNullCheck)
            {
                _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                _sb.StartNewBlock();
            }

            TagCodeHelper.WriteTag(_sb, 2, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{writersClass}.{valWriteMethod}(ref writer, {sourceVar});");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            if (needsNullCheck)
            {
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates write code for a single element in a repeated field (array or collection).
        /// Handles primitives, enums, tuples, and custom types.
        /// </summary>
        private void GenerateRepeatedElementWrite(string elementType, TypeAnalysisInfo elemInfo, string itemVar, int fieldId)
        {
            // Primitive elements
            if (_primitiveHandler.CanHandle(elementType))
            {
                _primitiveHandler.GenerateWrite(_sb, itemVar, elementType, DataFormat.Default, fieldId, false, false);
                return;
            }

            // Enum elements
            if (elemInfo?.IsEnum == true)
            {
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){itemVar});");
                return;
            }

            // ProtoVarint elements
            if (_registry != null && _registry.IsProtoVarint(TypeMapping.NormalizeTypeName(elementType)))
            {
                var normalizedElemType = TypeMapping.NormalizeTypeName(elementType);
                var varintType = _registry.GetProtoVarintType(normalizedElemType) ?? Attributes.ProtoVarintType.UInt32;
                var valueMember = _registry.GetProtoVarintValueMember(normalizedElemType);
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(varintType);
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.{writeMethod}({itemVar}.{valueMember});");
                return;
            }

            // Tuple elements
            if (TupleHandler.IsTupleType(elementType))
            {
                var itemTypes = TupleHandler.ParseTupleTypes(elementType);
                var tupleInfo = _virtualTupleRegistry.Register(elementType, itemTypes);
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{tupleInfo.SafeName}(ref writer, {itemVar});");
                _sb.AppendIndentedLine("writer.EndSubMessage();");
                return;
            }

            // Nested dictionary elements
            if (elemInfo?.IsDictionary == true)
            {
                var innerKeyType = elemInfo.DictionaryKeyType;
                var innerValueType = elemInfo.DictionaryValueType;

                // Check if key/value types are enums using the registry
                bool keyIsEnum = _registry?.IsEnum(innerKeyType) ?? false;
                bool valueIsEnum = _registry?.IsEnum(innerValueType) ?? false;

                var nestedEntryInfo = _virtualMapRegistry.RegisterMapEntry(
                    innerKeyType, innerValueType,
                    keyIsEnum,
                    valueIsEnum);

                // Write the nested dictionary - all entries packed together
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"if ({itemVar} != null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"foreach (var innerKvp in {itemVar})");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{nestedEntryInfo.TypeName}(ref writer, innerKvp.Key, innerKvp.Value);");
                _sb.EndBlock();
                _sb.EndBlock();
                _sb.AppendIndentedLine("writer.EndSubMessage();");
                return;
            }

            // Custom type elements (ProtoContract classes)
            var elementClassName = TypeNameHelper.GetClassName(elementType);
            var writersClass = GetWritersClass(elementType);
            var elementTypeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(elementType));
            var elementWriteMethod = (elementTypeDef == null || CanSkipWriteContentMethod(elementTypeDef))
                ? $"Write{elementClassName}"
                : $"Write{elementClassName}Content";
            _sb.AppendIndentedLine($"if ({itemVar} != null)");
            _sb.StartNewBlock();
            TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{writersClass}.{elementWriteMethod}(ref writer, {itemVar});");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates writer methods for all registered virtual tuple types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualTupleWriters(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualTupleRegistry.GetAllTypes();

            // Filter types based on IsGenerated flag
            var tupleTypes = ignoreIsGeneratedFlag
                ? allTypes
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Writers (OnePass)");

            foreach (var tupleInfo in tupleTypes)
            {
                GenerateVirtualTupleWriter(tupleInfo);
            }
        }

        private void GenerateVirtualTupleWriter(TupleTypeInfo tupleInfo)
        {
            var tupleTypeName = GetGlobalTypeName(tupleInfo.OriginalTypeName);
            _sb.AppendIndentedLine($"public static void Write{tupleInfo.SafeName}(ref {WriterType} writer, {tupleTypeName} value)");
            _sb.StartNewBlock();

            for (int i = 0; i < tupleInfo.ItemTypes.Count; i++)
            {
                var elementType = tupleInfo.ItemTypes[i];
                var itemAccess = GetTupleItemAccessor("value", i);
                var fieldId = i + 1;

                // Analyze the element type
                var elemTypeInfo = _virtualMapRegistry.AnalyzeType(elementType);

                if (_primitiveHandler.CanHandle(elementType))
                {
                    _primitiveHandler.GenerateWrite(_sb, itemAccess, elementType, DataFormat.Default, fieldId, false, false);
                }
                else if (elemTypeInfo?.IsEnum == true)
                {
                    // Enum element - write as VarInt
                    TagCodeHelper.WriteTag(_sb, fieldId, WireType.VarInt);
                    _sb.AppendIndentedLine($"writer.WriteVarInt32((int){itemAccess});");
                }
                else if (elemTypeInfo?.IsArray == true)
                {
                    // Array element - write each item as repeated field
                    GenerateTupleArrayElementWrite(itemAccess, elemTypeInfo, fieldId);
                }
                else if (elemTypeInfo?.IsList == true || elemTypeInfo?.IsHashSet == true || elemTypeInfo?.IsCollection == true)
                {
                    // Collection element - write each item as repeated field
                    GenerateTupleCollectionElementWrite(itemAccess, elemTypeInfo, fieldId);
                }
                else if (elemTypeInfo?.IsDictionary == true)
                {
                    // Dictionary element - write as nested map entries
                    GenerateTupleDictionaryElementWrite(itemAccess, elemTypeInfo, fieldId);
                }
                else if (TupleHandler.IsTupleType(elementType))
                {
                    // Nested tuple - register and call Write method (no Content suffix for tuples)
                    var nestedItemTypes = TupleHandler.ParseTupleTypes(elementType);
                    var nestedTupleInfo = _virtualTupleRegistry.Register(elementType, nestedItemTypes);
                    TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
                    _sb.AppendIndentedLine("writer.BeginSubMessage();");
                    _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{nestedTupleInfo.SafeName}(ref writer, {itemAccess});");
                    _sb.AppendIndentedLine("writer.EndSubMessage();");
                }
                else
                {
                    // ProtoContract custom type element
                    var elementClassName = TypeNameHelper.GetClassName(elementType);
                    var writersClass = GetWritersClass(elementType);
                    var elemTypeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(elementType));
                    var elemWriteMethod = (elemTypeDef == null || CanSkipWriteContentMethod(elemTypeDef))
                        ? $"Write{elementClassName}"
                        : $"Write{elementClassName}Content";
                    _sb.AppendIndentedLine($"if ({itemAccess} != null)");
                    _sb.StartNewBlock();
                    TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
                    _sb.AppendIndentedLine("writer.BeginSubMessage();");
                    _sb.AppendIndentedLine($"{writersClass}.{elemWriteMethod}(ref writer, {itemAccess});");
                    _sb.AppendIndentedLine("writer.EndSubMessage();");
                    _sb.EndBlock();
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Gets the namespace-qualified OnePassStreamWriters class for a type.
        /// </summary>
        private string GetWritersClass(string typeName)
        {
            // Use NamespaceHelper to get the correct namespace-qualified writers class
            return NamespaceHelper.GetWritersClass(typeName, ClassName, _registry);
        }

        /// <summary>
        /// Gets the fully qualified OnePassStreamWriters class in the shared virtual types namespace.
        /// Used for calling virtual type methods (MapEntry, Tuple) from namespace files.
        /// </summary>
        private static string GetVirtualTypesWritersClass()
        {
            return $"global::{SharedVirtualTypesGenerator.SharedNamespace}.{ClassName}";
        }

        private void GenerateTupleArrayElementWrite(string itemAccess, TypeAnalysisInfo elemTypeInfo, int fieldId)
        {
            var arrayElementType = elemTypeInfo.CollectionElementType;
            var arrayElemInfo = elemTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({itemAccess} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var arrayItem in {itemAccess})");
            _sb.StartNewBlock();
            GenerateRepeatedElementWrite(arrayElementType, arrayElemInfo, "arrayItem", fieldId);
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateTupleCollectionElementWrite(string itemAccess, TypeAnalysisInfo elemTypeInfo, int fieldId)
        {
            var collectionElementType = elemTypeInfo.CollectionElementType;
            var collectionElemInfo = elemTypeInfo.CollectionElementTypeInfo;

            _sb.AppendIndentedLine($"if ({itemAccess} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var collItem in {itemAccess})");
            _sb.StartNewBlock();
            GenerateRepeatedElementWrite(collectionElementType, collectionElemInfo, "collItem", fieldId);
            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateTupleDictionaryElementWrite(string itemAccess, TypeAnalysisInfo elemTypeInfo, int fieldId)
        {
            var dictKeyType = elemTypeInfo.DictionaryKeyType;
            var dictValueType = elemTypeInfo.DictionaryValueType;

            // Check if key/value types are enums
            bool keyIsEnum = _registry?.IsEnum(dictKeyType) ?? false;
            bool valueIsEnum = _registry?.IsEnum(dictValueType) ?? false;

            var nestedEntryInfo = _virtualMapRegistry.RegisterMapEntry(dictKeyType, dictValueType, keyIsEnum, valueIsEnum);

            _sb.AppendIndentedLine($"if ({itemAccess} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var dictKvp in {itemAccess})");
            _sb.StartNewBlock();
            TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{nestedEntryInfo.TypeName}(ref writer, dictKvp.Key, dictKvp.Value);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
            _sb.EndBlock();
            _sb.EndBlock();
        }

        #region Write Method

        /// <summary>
        /// Generates Write{ClassName}(ref OnePassStreamWriter writer, instance) method.
        /// </summary>
        private void GenerateWriteMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            // Skip entry-point Write method for types with SkipEntryPoints = true
            if (!type.SkipEntryPoints)
            {
                // Generate main Write method (entry point)
                _sb.AppendIndentedLine($"public static void Write{className}(ref {WriterType} writer, global::{type.FullName} instance)");
                _sb.StartNewBlock();

                // Add null check for reference types
                if (!type.IsStruct)
                {
                    _sb.AppendIndentedLine("if (instance == null) return;");
                }

                GenerateBeforeSerializationCallbacks(type);

                bool hasAfterCallbacks = HasAfterSerializationCallbacks(type);

                if (hasAfterCallbacks)
                {
                    _sb.AppendIndentedLine("try");
                    _sb.StartNewBlock();
                }

                // For simple types without callbacks, inline the body directly into WriteX
                if (CanSkipWriteContentMethod(type))
                {
                    GenerateWriteContentBodyOnePass(type, className);
                }
                else
                {
                    _sb.AppendIndentedLine($"Write{className}Content(ref writer, instance);");
                }

                if (hasAfterCallbacks)
                {
                    _sb.EndBlock(); // try
                    _sb.AppendIndentedLine("finally");
                    _sb.StartNewBlock();
                    GenerateAfterSerializationCallbacks(type);
                    _sb.EndBlock(); // finally
                }

                _sb.EndBlock();
                _sb.AppendNewLine();
            }

            // Generate WriteContent method (skipped for simple types without callbacks)
            GenerateWriteContentMethod(type, className);
        }

        /// <summary>
        /// Generates Write{ClassName}Content method.
        /// Skipped for simple types without callbacks — their body is inlined into WriteX.
        /// </summary>
        private void GenerateWriteContentMethod(TypeDefinition type, string className)
        {
            // Simple types without callbacks don't need WriteXContent — body is inlined into WriteX
            if (CanSkipWriteContentMethod(type))
                return;

            _sb.AppendIndentedLine($"public static void Write{className}Content(ref {WriterType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct && !type.IsEnum)
            {
                _sb.AppendIndentedLine("if (instance == null) return;");
            }

            // For enum types, generate simple VarInt write
            if (type.IsEnum)
            {
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)instance);");
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            bool isDerived = _registry.IsDerivedType(type.FullName);
            bool hasProtoIncludes = type.ProtoIncludes != null && type.ProtoIncludes.Count > 0;

            if (!isDerived && hasProtoIncludes)
            {
                // Base type with derived types - add type dispatch
                GenerateWriteContentWithTypeDispatch(type, className);
            }
            else if (isDerived)
            {
                // Derived type - write base fields first, then own fields
                GenerateWriteContentForDerivedType(type, className);
            }
            else
            {
                // Simple type with callbacks — still need WriteXContent for callback-free callers
                GenerateWriteContentBodyOnePass(type, className);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates the body of write content (field writes) without method signature.
        /// Used by both WriteX (inlined) and WriteXContent.
        /// </summary>
        private void GenerateWriteContentBodyOnePass(TypeDefinition type, string className)
        {
            if (type.IsEnum)
            {
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)instance);");
                return;
            }

            WriteTypeFields(type, "instance");
        }

        private void WriteTypeFields(TypeDefinition type, string objectName)
        {
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, objectName);
                }
            }

            // Write custom buffer fields
            if (type.CustomBufferMembers != null)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    GenerateCustomBufferFieldWrite(customMember, objectName);
                }
            }
        }

        /// <summary>
        /// Generates WriteContent method with type dispatch for base types with ProtoInclude.
        /// </summary>
        private void GenerateWriteContentWithTypeDispatch(TypeDefinition type, string className)
        {
            var sortedDerived = GeneratorHelpers.GetSortedDerivedTypes(type.FullName, _registry);
            if (sortedDerived == null)
            {
                // No derived types - just write own fields
                WriteTypeFields(type, "instance");
                return;
            }

            _sb.AppendIndentedLine("switch (instance)");
            _sb.StartNewBlock();

            foreach (var derivedType in sortedDerived)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"case global::{derivedType} derived:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"Write{derivedClassName}Content(ref writer, derived);");
                _sb.AppendIndentedLine("return;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();

            // Default case - base type fields
            WriteTypeFields(type, "instance");
        }

        /// <summary>
        /// Generates WriteContent for derived types.
        /// For OnePass, we write the ProtoInclude wrapper using BeginSubMessage/EndSubMessage.
        /// </summary>
        private void GenerateWriteContentForDerivedType(TypeDefinition type, string className)
        {
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain == null || inheritanceChain.Count < 2)
            {
                // No inheritance - just write own fields
                WriteTypeFields(type, "instance");
                return;
            }

            // Write ProtoInclude wrappers using BeginSubMessage/EndSubMessage
            GenerateProtoIncludeWrappers(inheritanceChain, type);
        }

        /// <summary>
        /// Generates nested ProtoInclude wrappers using BeginSubMessage/EndSubMessage.
        /// </summary>
        private void GenerateProtoIncludeWrappers(IReadOnlyList<string> chain, TypeDefinition type)
        {
            // For inheritance chain [Base, Mid, Derived], we need:
            // 1. Write wrapper tag for Mid (in Base)
            // 2. BeginSubMessage
            // 3. Write wrapper tag for Derived (in Mid)
            // 4. BeginSubMessage
            // 5. Write Derived's own fields
            // 6. EndSubMessage
            // 7. Write Mid's own fields
            // 8. EndSubMessage
            // 9. Write Base's fields

            // Generate opening wrappers
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var currentTypeName = chain[i];
                var nextTypeName = chain[i + 1];
                var currentType = _registry.GetByFullName(currentTypeName);
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                    _sb.AppendIndentedLine($"// ProtoInclude wrapper for {nextClassName}");
                    TagCodeHelper.WriteTag(_sb, protoInclude.FieldId, WireType.Len);
                    _sb.AppendIndentedLine("writer.BeginSubMessage();");
                }
            }

            // Write derived type's own fields (innermost)
            var ownMembers = _registry.GetOwnProtoMembers(type.FullName);
            if (ownMembers.Count > 0)
            {
                _sb.AppendIndentedLine($"// {TypeNameHelper.GetClassName(type.FullName)}'s own fields");
                foreach (var member in ownMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            // Generate closing wrappers and intermediate fields (in reverse order)
            for (int i = chain.Count - 2; i >= 0; i--)
            {
                _sb.AppendIndentedLine("writer.EndSubMessage();");

                // Write this level's own fields (except for base type which is written outside)
                if (i > 0)
                {
                    var typeName = chain[i];
                    var typeOwnMembers = _registry.GetOwnProtoMembers(typeName);
                    if (typeOwnMembers.Count > 0)
                    {
                        _sb.AppendIndentedLine($"// {TypeNameHelper.GetClassName(typeName)}'s own fields");
                        foreach (var member in typeOwnMembers)
                        {
                            GenerateFieldWrite(member, "instance");
                        }
                    }
                }
            }

            // Write base type fields (outside all wrappers)
            var baseTypeName = chain[0];
            var baseType = _registry.GetByFullName(baseTypeName);
            if (baseType?.ProtoMembers != null && baseType.ProtoMembers.Count > 0)
            {
                var baseClassName = TypeNameHelper.GetClassName(baseTypeName);
                _sb.AppendIndentedLine($"// Base class fields ({baseClassName})");
                foreach (var member in baseType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        #endregion

        #region Custom Buffer Field Generation

        private void GenerateCustomBufferFieldWrite(CustomBufferMember member, string objectName)
        {
            _sb.AppendIndentedLine($"// Custom buffer field {member.FieldId}");
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // For OnePass, we use BeginSubMessage/EndSubMessage pattern
            // But custom buffer is written as raw bytes, so we need to handle it differently
            _sb.AppendIndentedLine($"var customSize_{member.FieldId} = {objectName}.{member.SizeMethodName}();");
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)customSize_{member.FieldId});");

            // Write raw bytes - need to allocate buffer
            _sb.AppendIndentedLine($"var customBuffer_{member.FieldId} = System.Buffers.ArrayPool<byte>.Shared.Rent(customSize_{member.FieldId});");
            _sb.AppendIndentedLine("try");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"var customSpan_{member.FieldId} = customBuffer_{member.FieldId}.AsSpan(0, customSize_{member.FieldId});");
            _sb.AppendIndentedLine($"{objectName}.{member.FillMethodName}(customSpan_{member.FieldId});");
            _sb.AppendIndentedLine($"writer.WriteBytes(customSpan_{member.FieldId});");
            _sb.EndBlock();
            _sb.AppendIndentedLine("finally");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"System.Buffers.ArrayPool<byte>.Shared.Return(customBuffer_{member.FieldId});");
            _sb.EndBlock();
        }

        #endregion

        #region Field Generation

        /// <summary>
        /// Generates code to write a single field based on its type.
        /// </summary>
        private void GenerateFieldWrite(ProtoMemberAttribute member, string objectName)
        {
            string sourceVar = $"{objectName}.{member.Name}";
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            switch (category)
            {
                case FieldCategory.Map:
                    GenerateMapFieldWrite(member, sourceVar);
                    break;
                case FieldCategory.Collection:
                    GenerateCollectionFieldWrite(member, sourceVar);
                    break;
                case FieldCategory.Enum:
                    GenerateEnumFieldWrite(member, sourceVar);
                    break;
                case FieldCategory.Tuple:
                    GenerateTupleFieldWrite(member, sourceVar);
                    break;
                case FieldCategory.Primitive:
                    _primitiveHandler.GenerateWrite(
                        _sb,
                        sourceVar,
                        member.Type,
                        member.DataFormat,
                        member.FieldId,
                        member.IsNullable,
                        member.IsRequired);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateWrite(_sb, member, sourceVar);
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported");
                    break;
                case FieldCategory.ComplexType:
                    GenerateComplexTypeWrite(member, sourceVar);
                    break;
            }
        }

        private void GenerateEnumFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            EnumFieldHelper.GenerateEnumField(
                _sb,
                member,
                sourceVar,
                writeTag: () => TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt),
                writeValue: (valueExpr, _) => _sb.AppendIndentedLine($"writer.WriteVarInt32((int){valueExpr});"));
        }

        private void GenerateMapFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var kvp in {sourceVar})");
            _sb.StartNewBlock();

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");

            // Register virtual map entry type
            var virtualType = _virtualMapRegistry.RegisterMapEntry(
                member.MapKeyType,
                member.MapValueType,
                member.MapKeyIsEnum,
                member.MapValueIsEnum,
                member.MapKeyEnumUnderlyingType,
                member.MapValueEnumUnderlyingType);

            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{virtualType.TypeName}(ref writer, kvp.Key, kvp.Value);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateCollectionFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
                // Level200: Primitives MUST use packed encoding by default
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

                if (shouldBePacked)
                {
                    _primitiveHandler.GeneratePackedArrayWrite(
                        _sb,
                        sourceVar,
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId);
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArrayWrite(
                        _sb,
                        sourceVar,
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection
                GenerateTupleCollectionWrite(member, sourceVar);
            }
            else if (TypeMapping.IsSimpleType(normalizedType))
            {
                // BCL types like DateTime, Guid, TimeSpan that are "simple" but not primitive arrays
                GenerateBclTypeCollectionWrite(member, sourceVar, normalizedType);
            }
            else if (_registry != null && _registry.IsProtoVarint(normalizedType))
            {
                // ProtoVarint type collection - write as VarInt values
                GenerateProtoVarintCollectionWrite(member, sourceVar, normalizedType);
            }
            else
            {
                // Complex type collection - use BeginSubMessage/EndSubMessage
                GenerateComplexCollectionWrite(member, sourceVar);
            }
        }

        /// <summary>
        /// Generates write code for collections of BCL types like DateTime, Guid, TimeSpan.
        /// These are simple types but not included in primitive array handling.
        /// </summary>
        private void GenerateBclTypeCollectionWrite(ProtoMemberAttribute member, string sourceVar, string normalizedType)
        {
            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            // BCL types like DateTime, Guid, TimeSpan use WriteXXX methods directly
            // No null check needed since these are value types
            var wireType = TypeMapping.GetWireType(normalizedType);
            TagCodeHelper.WriteTag(_sb, member.FieldId, wireType);

            var writeExpr = TypeMapping.GetWriteExpression(normalizedType, "item", DataFormat.Default, "writer");
            if (writeExpr != null)
            {
                _sb.AppendIndentedLine($"{writeExpr};");
            }
            else
            {
                // Fallback to generic handling
                _sb.AppendIndentedLine($"// Warning: Unsupported BCL type {normalizedType}");
            }

            _sb.EndBlock();
            _sb.EndBlock();
        }

        /// <summary>
        /// Generates write code for collections of ProtoVarint types (structs with [ProtoVarint] attribute).
        /// These are written as simple VarInt values, not as sub-messages.
        /// </summary>
        private void GenerateProtoVarintCollectionWrite(ProtoMemberAttribute member, string sourceVar, string normalizedType)
        {
            var varintType = _registry.GetProtoVarintType(normalizedType) ?? Attributes.ProtoVarintType.UInt32;
            var valueMember = _registry.GetProtoVarintValueMember(normalizedType);
            var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(varintType);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt);
            _sb.AppendIndentedLine($"writer.{writeMethod}(item.{valueMember});");

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateTupleFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var itemTypes = TupleHandler.ParseTupleTypes(member.Type);
            var tupleInfo = _virtualTupleRegistry.Register(member.Type, itemTypes);
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{tupleInfo.SafeName}(ref writer, {sourceVar});");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
        }

        private void GenerateTupleCollectionWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var itemTypes = TupleHandler.ParseTupleTypes(member.CollectionElementType);
            var tupleInfo = _virtualTupleRegistry.Register(member.CollectionElementType, itemTypes);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"{VirtualTypesPrefix}.{ClassName}.Write{tupleInfo.SafeName}(ref writer, item);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateComplexCollectionWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);

            // Check if element type is a struct (can't be null)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            var elementTypeDef = _registry?.GetByFullName(normalizedType) ?? _registry?.GetByFullName(member.CollectionElementType);
            bool elementIsStruct = elementTypeDef != null && elementTypeDef.IsStruct;

            // Get namespace-qualified writers class for cross-namespace calls
            var writersClass = GetWritersClass(member.CollectionElementType);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            // Only add null check for reference types
            if (!elementIsStruct)
            {
                _sb.AppendIndentedLine("if (item != null)");
                _sb.StartNewBlock();
            }

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            var collWriteMethod = (elementTypeDef == null || CanSkipWriteContentMethod(elementTypeDef))
                ? $"Write{elementClassName}"
                : $"Write{elementClassName}Content";
            _sb.AppendIndentedLine($"{writersClass}.{collWriteMethod}(ref writer, item);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            if (!elementIsStruct)
            {
                _sb.EndBlock();
            }

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateComplexTypeWrite(ProtoMemberAttribute member, string sourceVar)
        {
            // Check if type is a nullable wrapper (System.Nullable<T> or T?)
            // This only applies to VALUE types wrapped in Nullable<>
            var underlyingType = GetNullableUnderlyingType(member.Type);

            // Also check for nullable struct types (T? where T is a custom struct)
            // GetNullableUnderlyingType only handles simple types, so we need TypeHelper for custom structs
            if (underlyingType == null && TypeHelper.IsNullableType(member.Type))
            {
                var potentialUnderlyingType = TypeHelper.GetNullableUnderlyingType(member.Type);
                var potentialTypeDef = _registry.GetByFullName(potentialUnderlyingType);
                if (potentialTypeDef != null && potentialTypeDef.IsStruct)
                {
                    underlyingType = potentialUnderlyingType;
                }
            }

            var actualType = underlyingType ?? member.Type;
            var typeName = TypeNameHelper.GetClassName(actualType);
            var typeDef = _registry.GetByFullName(actualType);

            // Determine if this is a nullable VALUE type (struct wrapped in Nullable<T>)
            // Only Nullable<T> wrappers need .Value accessor, not nullable reference types
            bool isNullableValueType = underlyingType != null;
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && underlyingType == null && !member.IsNullable;

            if (!isNonNullableStruct)
            {
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"var complexValue = {sourceVar};");
                _sb.AppendIndentedLine("if (complexValue != null)");
                _sb.StartNewBlock();
            }

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");

            string valueArg = isNonNullableStruct ? sourceVar : "complexValue";
            if (isNullableValueType)
            {
                valueArg += ".Value";
            }

            var typeNamespace = _registry.GetNamespaceForType(actualType);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            var complexWriteMethod = (typeDef == null || CanSkipWriteContentMethod(typeDef))
                ? $"Write{typeName}"
                : $"Write{typeName}Content";
            _sb.AppendIndentedLine($"{nsPrefix}{ClassName}.{complexWriteMethod}(ref writer, {valueArg});");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            if (!isNonNullableStruct)
            {
                _sb.EndBlock();
                _sb.EndBlock();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts a type name to its code-generation form.
        /// Uses C# keywords for primitives (int, string, etc.) and global:: prefix for custom types.
        /// Handles arrays by recursively processing the element type.
        /// Handles nullable types (System.Nullable&lt;T&gt; and T?) properly.
        /// </summary>
        private static string GetGlobalTypeName(string typeName)
        {
            // Handle nullable types first (System.Nullable<T>)
            if (typeName.StartsWith("System.Nullable<") && typeName.EndsWith(">"))
            {
                var innerType = typeName.Substring(16, typeName.Length - 17);
                var innerTypeName = GetGlobalTypeName(innerType);
                return $"{innerTypeName}?";
            }

            // Handle C# nullable shorthand (T?)
            if (typeName.EndsWith("?"))
            {
                var innerType = typeName.Substring(0, typeName.Length - 1);
                var innerTypeName = GetGlobalTypeName(innerType);
                return $"{innerTypeName}?";
            }

            // Handle array types - recurse to get proper element type name
            // Example: "System.Int32[]" -> "int[]", not "global::int[]"
            if (typeName.EndsWith("[]"))
            {
                var elementType = typeName.Substring(0, typeName.Length - 2);
                var elementTypeName = GetGlobalTypeName(elementType);
                return $"{elementTypeName}[]";
            }

            // Check if it's a primitive/simple type
            if (TypeMapping.IsSimpleType(typeName))
            {
                // Return C# keyword form (int, string, etc.)
                return TypeMapping.GetShortTypeName(typeName);
            }

            // For non-primitive types (including generic types), use GetGlobalGenericTypeName
            // This handles nested generic arguments correctly by applying global:: prefix to each
            return TypeMapping.GetGlobalGenericTypeName(typeName);
        }

        /// <summary>
        /// Gets the correct accessor for a tuple item by index.
        /// For items 0-6, returns "variable.Item1" through "variable.Item7".
        /// For item 7 (8th element), returns "variable.Rest" (the whole TRest value).
        /// For items 8+, when TupleTypeInfo.Arity > 8, the items are within the nested Rest tuple.
        ///
        /// C# Tuple structure: Tuple&lt;T1,T2,T3,T4,T5,T6,T7,TRest&gt;
        /// - Items 1-7 are direct properties
        /// - Rest is the entire TRest value (which may be a nested Tuple)
        /// </summary>
        private static string GetTupleItemAccessor(string variableName, int itemIndex)
        {
            // Items 0-6 use Item1-Item7 directly
            if (itemIndex < 7)
            {
                return $"{variableName}.Item{itemIndex + 1}";
            }

            // Item 7 (8th element, 0-indexed) is the Rest property itself
            // In TupleTypeInfo where Arity = 8, ItemTypes[7] is the 8th element type (which is TRest)
            // We return .Rest to get the whole TRest value
            if (itemIndex == 7)
            {
                return $"{variableName}.Rest";
            }

            // For Arity > 8, TupleTypeInfo represents a "flattened" view
            // Items at index 8+ need to access within the nested Rest tuple
            // Example: For a logical 9-element tuple represented as Tuple<T1..T7, Tuple<T8, T9>>
            // - ItemTypes[7] = T8, accessed as .Rest.Item1
            // - ItemTypes[8] = T9, accessed as .Rest.Item2

            // Actually, for TupleTypeInfo with Arity > 8, the types are already flattened
            // So itemIndex 8 means the 9th logical item, which is .Rest.Item2
            // itemIndex 7 was the 8th item (.Rest or .Rest.Item1 depending on nesting)

            // This case handles when Arity > 8 and we're accessing items 9+
            var itemWithinRest = itemIndex - 6; // Maps index 7->1, 8->2, etc. for Rest.ItemX

            // For deep nesting (15+ elements), we need nested Rest
            if (itemWithinRest > 7)
            {
                var restDepth = (itemWithinRest - 1) / 7;
                var finalItemIndex = ((itemWithinRest - 1) % 7) + 1;

                var accessor = variableName + ".Rest";
                for (int i = 0; i < restDepth; i++)
                {
                    accessor += ".Rest";
                }

                if (finalItemIndex == 1 && restDepth > 0)
                {
                    return accessor; // Return the nested Rest itself
                }

                return $"{accessor}.Item{finalItemIndex}";
            }

            return $"{variableName}.Rest.Item{itemWithinRest}";
        }

        /// <summary>
        /// Gets the underlying type from a nullable VALUE type (System.Nullable&lt;T&gt; or primitive?).
        /// Returns null if the type is not a Nullable&lt;T&gt; wrapper or nullable primitive.
        /// For T? shorthand, only returns the underlying type if it's a known simple value type.
        /// </summary>
        private static string GetNullableUnderlyingType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Handle System.Nullable<T> - only valid for value types
            if (typeName.StartsWith("System.Nullable<") && typeName.EndsWith(">"))
            {
                return typeName.Substring(16, typeName.Length - 17);
            }

            // Handle T? shorthand ONLY for known simple value types (int?, long?, etc.)
            // Reference types in C# 8+ can also use T? but they don't need .Value accessor
            if (typeName.EndsWith("?"))
            {
                var underlyingType = typeName.Substring(0, typeName.Length - 1);
                // Only treat as nullable value type if underlying is a simple type (primitives, DateTime, Guid, etc.)
                if (TypeMapping.IsSimpleType(underlyingType))
                {
                    return underlyingType;
                }
            }

            return null;
        }

        #endregion
    }
}
