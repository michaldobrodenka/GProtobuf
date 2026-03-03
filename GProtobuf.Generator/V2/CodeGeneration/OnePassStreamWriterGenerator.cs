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

            // Generate virtual map entry writers
            GenerateVirtualMapEntryWriters();

            // Generate virtual tuple writers
            GenerateVirtualTupleWriters();

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates writer methods for all registered virtual map entry types.
        /// </summary>
        private void GenerateVirtualMapEntryWriters()
        {
            var virtualTypes = _virtualMapRegistry.GetAllTypes();
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
            if (virtualType.KeyIsEnum)
            {
                TagCodeHelper.WriteTag(_sb, 1, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
            }
            else if (_primitiveHandler.CanHandle(virtualType.KeyType))
            {
                _primitiveHandler.GenerateWrite(_sb, sourceVar, virtualType.KeyType, DataFormat.Default, 1, false, false);
            }
            else
            {
                // Complex key type
                var keyClassName = TypeNameHelper.GetClassName(virtualType.KeyType);
                TagCodeHelper.WriteTag(_sb, 1, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"Write{keyClassName}Content(ref writer, {sourceVar});");
                _sb.AppendIndentedLine("writer.EndSubMessage();");
            }
        }

        private void GenerateMapValueWrite(VirtualMapEntryInfo virtualType, string sourceVar)
        {
            if (virtualType.ValueIsEnum)
            {
                TagCodeHelper.WriteTag(_sb, 2, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
            }
            else if (_primitiveHandler.CanHandle(virtualType.ValueType))
            {
                _primitiveHandler.GenerateWrite(_sb, sourceVar, virtualType.ValueType, DataFormat.Default, 2, false, false);
            }
            else
            {
                // Complex value type - use BeginSubMessage/EndSubMessage
                var valueClassName = TypeNameHelper.GetClassName(virtualType.ValueType);
                // Check if value needs null check (reference types)
                var valueTypeInfo = virtualType.ValueTypeInfo;
                bool needsNullCheck = valueTypeInfo != null && !valueTypeInfo.IsPrimitive && !valueTypeInfo.IsStruct;

                if (needsNullCheck)
                {
                    _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                    _sb.StartNewBlock();
                }

                TagCodeHelper.WriteTag(_sb, 2, WireType.Len);
                _sb.AppendIndentedLine("writer.BeginSubMessage();");
                _sb.AppendIndentedLine($"Write{valueClassName}Content(ref writer, {sourceVar});");
                _sb.AppendIndentedLine("writer.EndSubMessage();");

                if (needsNullCheck)
                {
                    _sb.EndBlock();
                }
            }
        }

        /// <summary>
        /// Generates writer methods for all registered virtual tuple types.
        /// </summary>
        private void GenerateVirtualTupleWriters()
        {
            var tupleTypes = _virtualTupleRegistry.GetAllTypes();
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
                var itemAccess = $"value.Item{i + 1}";
                var fieldId = i + 1;

                if (_primitiveHandler.CanHandle(elementType))
                {
                    _primitiveHandler.GenerateWrite(_sb, itemAccess, elementType, DataFormat.Default, fieldId, false, false);
                }
                else
                {
                    // Complex element type
                    var elementClassName = TypeNameHelper.GetClassName(elementType);
                    TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);
                    _sb.AppendIndentedLine("writer.BeginSubMessage();");
                    _sb.AppendIndentedLine($"Write{elementClassName}Content(ref writer, {itemAccess});");
                    _sb.AppendIndentedLine("writer.EndSubMessage();");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #region Write Method

        /// <summary>
        /// Generates Write{ClassName}(ref OnePassStreamWriter writer, instance) method.
        /// </summary>
        private void GenerateWriteMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            // Generate main Write method (entry point)
            _sb.AppendIndentedLine($"public static void Write{className}(ref {WriterType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (instance == null) return;");
            }

            // For OnePass, we simply write content directly
            _sb.AppendIndentedLine($"Write{className}Content(ref writer, instance);");

            _sb.EndBlock();
            _sb.AppendNewLine();

            // Generate WriteContent method
            GenerateWriteContentMethod(type, className);
        }

        /// <summary>
        /// Generates Write{ClassName}Content method.
        /// </summary>
        private void GenerateWriteContentMethod(TypeDefinition type, string className)
        {
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
                // Simple type - just write own fields
                WriteTypeFields(type, "instance");
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
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

            _sb.AppendIndentedLine($"Write{virtualType.TypeName}(ref writer, kvp.Key, kvp.Value);");
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
            else
            {
                // Complex type collection - use BeginSubMessage/EndSubMessage
                GenerateComplexCollectionWrite(member, sourceVar);
            }
        }

        private void GenerateTupleFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var itemTypes = TupleHandler.ParseTupleTypes(member.Type);
            var tupleInfo = _virtualTupleRegistry.Register(member.Type, itemTypes);
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"Write{tupleInfo.SafeName}(ref writer, {sourceVar});");
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
            _sb.AppendIndentedLine($"Write{tupleInfo.SafeName}(ref writer, item);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateComplexCollectionWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            _sb.StartNewBlock();

            _sb.AppendIndentedLine("if (item != null)");
            _sb.StartNewBlock();
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine("writer.BeginSubMessage();");
            _sb.AppendIndentedLine($"Write{elementClassName}Content(ref writer, item);");
            _sb.AppendIndentedLine("writer.EndSubMessage();");
            _sb.EndBlock();

            _sb.EndBlock();
            _sb.EndBlock();
        }

        private void GenerateComplexTypeWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            var typeDef = _registry.GetByFullName(member.Type);
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;

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
            if (member.IsNullable && typeDef != null && typeDef.IsStruct)
            {
                valueArg += ".Value";
            }

            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"{nsPrefix}{ClassName}.Write{typeName}Content(ref writer, {valueArg});");
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

        #endregion
    }
}
