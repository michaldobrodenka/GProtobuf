using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates SpanReaders class with Read{ClassName} and Read{ClassName}Content methods.
    /// Handles deserialization from SpanReader to object instances.
    /// </summary>
    internal class SpanReaderGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly TypeRegistry _registry;
        private readonly PrimitiveHandler _primitiveHandler;
        private readonly CollectionHandler _collectionHandler;
        private readonly TupleHandler _tupleHandler;
        private readonly VirtualMapTypeRegistry _virtualMapRegistry;
        private readonly VirtualTupleTypeRegistry _virtualTupleRegistry;
        private string _currentNamespace;

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null, null)
        {
        }

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : this(sb, registry, virtualMapRegistry, null)
        {
        }

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler(registry);
            _collectionHandler = new CollectionHandler(sb, registry);
            _virtualTupleRegistry = virtualTupleRegistry ?? new VirtualTupleTypeRegistry();
            _virtualMapRegistry = virtualMapRegistry ?? new VirtualMapTypeRegistry(_virtualTupleRegistry, _registry);
            _tupleHandler = new TupleHandler(sb, _virtualTupleRegistry);
        }

        /// <summary>
        /// Gets the virtual map type registry used by this generator.
        /// </summary>
        public VirtualMapTypeRegistry VirtualMapRegistry => _virtualMapRegistry;

        /// <summary>
        /// Gets the virtual tuple type registry used by this generator.
        /// </summary>
        public VirtualTupleTypeRegistry VirtualTupleRegistry => _virtualTupleRegistry;

        /// <summary>
        /// Analyzes type and determines deserialization strategy (parameterless constructor, constructor with params, or FormatterServices).
        /// </summary>
        private ConstructorMatcher.ConstructorMatchResult AnalyzeConstructorStrategy(TypeDefinition type)
        {
            // If TypeSymbol not available, fallback to old behavior
            if (type.TypeSymbol == null)
            {
                if (type.HasParameterlessConstructor)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseParameterlessConstructor = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else if (!type.IsStruct)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseFormatterServices = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        ErrorMessage = $"Struct '{type.FullName}' requires TypeSymbol for constructor analysis"
                    };
                }
            }

            // Collect ProtoMember field information
            var protoFields = new List<ConstructorMatcher.FieldInfo>();
            if (type.ProtoMembers != null)
            {
                foreach (var protoMember in type.ProtoMembers)
                {
                    // Find corresponding field or property in TypeSymbol
                    var member = type.TypeSymbol.GetMembers(protoMember.Name).FirstOrDefault();

                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = field.Type,
                            IsReadonly = field.IsReadOnly
                        });
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = property.Type,
                            IsReadonly = property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public
                        });
                    }
                }
            }

            // Use ConstructorMatcher to find best constructor
            return ConstructorMatcher.FindBestConstructor(type.TypeSymbol, protoFields);
        }

        /// <summary>
        /// Generates object creation code, using FormatterServices if type has no parameterless constructor.
        /// LEGACY: This method is kept for backward compatibility with old code paths.
        /// </summary>
        private void GenerateObjectCreation(TypeDefinition type, string variableName = "result")
        {
            var fullTypeName = $"global::{type.FullName}";

            if (type.HasParameterlessConstructor)
            {
                // Use standard new operator
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = new {fullTypeName}();");
            }
            else
            {
                // Use FormatterServices.GetUninitializedObject() like protobuf-net does
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = ({fullTypeName})");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"System.Runtime.Serialization.FormatterServices.GetUninitializedObject(");
                _sb.AppendIndentedLine($"    typeof({fullTypeName}));");
                _sb.DecreaseIndent();
            }
        }

        /// <summary>
        /// Generates complete SpanReaders class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            // Store current namespace for cross-namespace method calls
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine("public static class SpanReaders");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                try
                {
                    GenerateReadMethod(type);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateReadMethod for type '{type.FullName}'", ex);
                }

                try
                {
                    GenerateReadContentMethod(type);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GenerateReadContentMethod for type '{type.FullName}'", ex);
                }

                try
                {
                    GeneratePopulateMethod(type);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error in GeneratePopulateMethod for type '{type.FullName}'", ex);
                }
            }

            // Generate ReadContent methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new System.Collections.Generic.HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = new System.Collections.Generic.HashSet<string>();

            // Collect all ProtoInclude types from all registered types
            foreach (var registeredType in _registry.GetAllTypes())
            {
                if (registeredType.ProtoIncludes != null)
                {
                    foreach (var include in registeredType.ProtoIncludes)
                    {
                        if (!processedTypes.Contains(include.Type))
                        {
                            protoIncludeTypes.Add(include.Type);
                        }
                    }
                }
            }

            // Generate ReadContent methods for ProtoInclude types
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    try
                    {
                        GenerateReadContentMethod(protoIncludeType);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in GenerateReadContentMethod for ProtoInclude type '{protoIncludeTypeName}'", ex);
                    }

                    try
                    {
                        GeneratePopulateMethod(protoIncludeType);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in GeneratePopulateMethod for ProtoInclude type '{protoIncludeTypeName}'", ex);
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            try
            {
                // Generate virtual map entry readers
                GenerateVirtualMapEntryReaders();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error in GenerateVirtualMapEntryReaders", ex);
            }

            try
            {
                // Generate virtual tuple readers
                GenerateVirtualTupleReaders();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error in GenerateVirtualTupleReaders", ex);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates reader methods for all registered virtual map entry types.
        /// </summary>
        private void GenerateVirtualMapEntryReaders()
        {
            System.Collections.Generic.IReadOnlyList<VirtualMapEntryInfo> virtualTypes;
            try
            {
                virtualTypes = _virtualMapRegistry.GetAllTypes();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error calling _virtualMapRegistry.GetAllTypes()", ex);
            }

            if (virtualTypes.Count == 0) return;

            // Validate all virtual map types and report warnings for problematic ones
            ValidateVirtualMapTypes(virtualTypes);

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Readers");
            _sb.AppendNewLine();

            VirtualMapEntryGenerator generator;
            try
            {
                generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry);
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error creating VirtualMapEntryGenerator", ex);
            }

            // Generate EstimateMapCapacity helper (once, used by all map readers)
            _sb.AppendIndentedLine("#region Map Capacity Estimation");
            _sb.AppendNewLine();
            try
            {
                generator.GenerateEstimateMapCapacityHelper();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error in GenerateEstimateMapCapacityHelper", ex);
            }
            _sb.AppendIndentedLine("#endregion");
            _sb.AppendNewLine();

            // Generate individual map entry readers
            foreach (var virtualType in virtualTypes)
            {
                try
                {
                    generator.GenerateReader(virtualType);
                }
                catch (System.Exception ex)
                {
                    throw new System.Exception($"Error generating reader for virtual map type: KeyType='{virtualType?.KeyType}', ValueType='{virtualType?.ValueType}', TypeName='{virtualType?.TypeName}'", ex);
                }
            }

            // Generate KeyValue methods that use MapEntry methods
            try
            {
                var keyValueGenerator = new KeyValueClassGenerator(_sb, _virtualMapRegistry);
                keyValueGenerator.GenerateKeyValueMethods("SpanReaders");
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error in GenerateKeyValueMethods", ex);
            }
        }

        /// <summary>
        /// Generates reader methods for all registered virtual tuple types.
        /// </summary>
        private void GenerateVirtualTupleReaders()
        {
            var tupleTypes = _virtualTupleRegistry.GetAllTypes();
            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Readers");

            var generator = new VirtualTupleGenerator(_sb, null, _registry);
            foreach (var tupleInfo in tupleTypes)
            {
                generator.GenerateReader(tupleInfo);
            }
        }

        #region Read Method

        /// <summary>
        /// Generates Read{ClassName}(ref SpanReader reader) method.
        /// Entry point for deserialization.
        /// </summary>
        private void GenerateReadMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref SpanReader reader)");
            _sb.StartNewBlock();

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                // Simple case - no inheritance, delegate to Content method
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else
            {
                // Complex case with inheritance - handle ProtoIncludes
                GenerateReadMethodWithInheritance(type, className);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateReadMethodWithInheritance(TypeDefinition type, string className)
        {
            // Check if this is ProtoInclude-based inheritance or flat inheritance
            bool hasProtoInclude = type.ProtoIncludes != null && type.ProtoIncludes.Count > 0;
            bool isProtoIncludeDerived = _registry.IsDerivedType(type.FullName);
            bool isFlatInheritance = _registry.HasFlatInheritance(type.FullName);

            if (isProtoIncludeDerived)
            {
                // For ProtoInclude derived types, generate code that handles the full inheritance chain
                GenerateReadMethodForDerived(type, className);
            }
            else if (hasProtoInclude)
            {
                // For base types with ProtoIncludes, use polymorphic reading
                GenerateReadMethodForBaseWithIncludes(type, className);
            }
            else if (isFlatInheritance)
            {
                // For flat inheritance (без ProtoInclude), use simple delegation
                // Merged field switch is already generated in Content method
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else
            {
                // Fallback: simple delegation (should not reach here if HasInheritance is correct)
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
        }

        /// <summary>
        /// Generates Read method for base type with ProtoIncludes.
        /// Returns the appropriate derived type based on ProtoInclude field.
        /// </summary>
        private void GenerateReadMethodForBaseWithIncludes(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for all fields
            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            // Handle ProtoIncludes (derived types)
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeReadCase(type, include);
                }
            }

            // Handle own fields
            // Abstract base classes with ProtoMembers MUST deserialize their fields
            // even though they can't be instantiated directly. The deserialized values
            // are copied to derived instances via "if (oldResult != null) result.Field = oldResult.Field"
            // pattern in ProtoInclude case handlers.
            //
            // Example: MessageBase (abstract) has RequestsId field that must be read,
            // even though MessageBase itself is never instantiated.
            string lazyInit = type.IsAbstract ? null : $"result ??= new global::{type.FullName}();";

            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    // For abstract types, we still generate field read cases, but without lazy init
                    // The values are stored in the result (which may be null for abstract types)
                    // and copied to derived instances by ProtoInclude handlers
                    GenerateFieldReadCaseWithLazyInit(member, lazyInit);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
            _sb.EndBlock();

            if (!type.IsAbstract)
            {
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("// Fallback: if no ProtoInclude field found, create base type instance");
                _sb.AppendIndentedLine("if (result == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"result = new global::{type.FullName}();");
                _sb.EndBlock();
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates Read method for derived type that handles the full inheritance chain.
        /// Reads all ancestor fields and navigates through nested ProtoInclude wrappers.
        /// </summary>
        private void GenerateReadMethodForDerived(TypeDefinition type, string className)
        {
            // Create instance of the derived type
            GenerateObjectCreation(type, "result");
            _sb.AppendNewLine();

            // Get inheritance chain: [Root, ..., Parent, This]
            var chain = _registry.GetInheritanceChain(type.FullName);

            // Collect all fields needing temp lists from the entire inheritance chain
            var fieldsNeedingTempList = new List<ProtoMemberAttribute>();
            foreach (var typeName in chain)
            {
                var typeInChain = _registry.GetByFullName(typeName);
                if (typeInChain?.ProtoMembers != null)
                {
                    var tempListFields = typeInChain.ProtoMembers
                        .Where(m => m.IsCollection && (
                            m.CollectionKind == CollectionKind.Array ||
                            (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                             TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                             !m.Type.Contains("ICollection") &&
                             !m.Type.Contains("IList"))
                        ))
                        .ToList();

                    fieldsNeedingTempList.AddRange(tempListFields);
                }
            }

            // Declare temp lists for all collection fields from inheritance chain
            if (fieldsNeedingTempList.Count > 0)
            {
                foreach (var member in fieldsNeedingTempList)
                {
                    var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
                }
                _sb.AppendNewLine();
            }

            // Generate nested reading for each level
            GenerateNestedReading(chain, 0, "reader");

            // Convert temp lists to arrays or assign to IEnumerable properties
            if (fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
                foreach (var member in fieldsNeedingTempList)
                {
                    _sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                    _sb.StartNewBlock();

                    if (member.CollectionKind == CollectionKind.Array)
                    {
                        // Arrays need ToArray() conversion
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name}.ToArray();");
                    }
                    else
                    {
                        // IEnumerable can be assigned List directly (List implements IEnumerable)
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name};");
                    }

                    _sb.EndBlock();
                }
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Recursively generates code to read fields at a specific level of the inheritance chain.
        /// </summary>
        private void GenerateNestedReading(IReadOnlyList<string> chain, int levelIndex, string readerVar)
        {
            if (levelIndex >= chain.Count)
                return;

            var currentTypeName = chain[levelIndex];
            var currentType = _registry.GetByFullName(currentTypeName);
            if (currentType == null)
                return;

            var wireTypeVar = levelIndex == 0 ? "wireType" : $"wireType{levelIndex}";
            var fieldIdVar = levelIndex == 0 ? "fieldId" : $"fieldId{levelIndex}";

            _sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{readerVar}.ReadWireTypeAndFieldId(out var {wireTypeVar}, out var {fieldIdVar});");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine($"switch ({fieldIdVar})");
            _sb.StartNewBlock();

            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    GenerateNestedProtoIncludeCase(protoInclude, chain, levelIndex + 1, readerVar);
                }
            }

            if (currentType.ProtoMembers != null)
            {
                foreach (var member in currentType.ProtoMembers)
                {
                    GenerateFieldReadCaseForDerived(member, wireTypeVar, readerVar);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while
        }

        /// <summary>
        /// Generates switch case for ProtoInclude that enters nested level.
        /// </summary>
        private void GenerateNestedProtoIncludeCase(ProtoIncludeAttribute include, IReadOnlyList<string> chain, int nextLevelIndex, string currentReaderVar)
        {
            var nestedReaderVar = $"nestedReader{nextLevelIndex}";

            _sb.AppendIndentedLine($"case {include.FieldId}:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine($"var length{nextLevelIndex} = {currentReaderVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {nestedReaderVar} = new SpanReader({currentReaderVar}.GetSlice(length{nextLevelIndex}));");

            // Recursively generate reading for next level
            GenerateNestedReading(chain, nextLevelIndex, nestedReaderVar);

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
            _sb.DecreaseIndent();
        }

        /// <summary>
        /// Generates switch case for reading a field into 'result' for derived types.
        /// </summary>
        private void GenerateFieldReadCaseForDerived(ProtoMemberAttribute member, string wireTypeVar, string readerVar)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");

            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.IncreaseIndent();
            }

            // Phase 1: Wire type validation (Level200 requirement)
            GenerateWireTypeValidation(member, wireTypeVar, readerVar);

            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry);
                mapHandler.GenerateRead(member, $"result.{member.Name}", readerVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBodyForDerived(member, wireTypeVar, readerVar);
            }
            else if (member.IsEnum)
            {
                // Use fully qualified type name for enums to avoid namespace issues
                _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type}){readerVar}.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type, readerVar);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, readerVar, wireTypeVar);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type (e.g., System.Type) - skip field with warning comment
                _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be deserialized)");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
            else
            {
                GenerateComplexTypeReadBodyWithReader(member, readerVar);
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                _sb.DecreaseIndent();
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GenerateCollectionFieldReadBodyForDerived(ProtoMemberAttribute member, string wireTypeVar, string readerVar)
        {
            if (member.CollectionElementType == null)
            {
                throw new System.Exception($"CollectionElementType is null for collection member '{member.Name}' of type '{member.Type}'");
            }

            // Check if element type is enum (enums use packed encoding like primitives)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
                // Reader must accept both PACKED (for IsPacked=true) and UNPACKED (default) for backward compatibility
                bool shouldBePacked = member.IsPacked;

                if (shouldBePacked)
                {
                    _primitiveHandler.GenerateDualModePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type,
                        wireTypeVar,
                        readerVar);
                }
                else
                {
                    var fieldIdVar = wireTypeVar.Replace("wireType", "fieldId");
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        member.CollectionKind,
                        member.Type,
                        readerVar,
                        wireTypeVar,
                        fieldIdVar);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    member.FieldId,
                    member.CollectionKind,
                    member.Type,
                    readerVar);
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    elementClassName,
                    member.CollectionKind,
                    member.Type,
                    readerVar);
            }
        }

        private void GenerateComplexTypeReadBodyWithReader(ProtoMemberAttribute member, string readerVar)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine($"var length = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var nestedReader = new SpanReader({readerVar}.GetSlice(length));");

            // Check if type is from different namespace and qualify the call
            var typeNamespace = _registry.GetNamespaceForType(member.Type);

            // Derived types need Read{TypeName} (handles ProtoInclude wrapper at field 100)
            // Non-derived types need Read{TypeName}Content (reads fields directly)
            var parentType = _registry.GetParent(member.Type);
            bool isDerivedType = !string.IsNullOrEmpty(parentType);
            string methodSuffix = isDerivedType ? "" : "Content";

            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"result.{member.Name} = global::{typeNamespace}.Serialization.SpanReaders.Read{typeName}{methodSuffix}(ref nestedReader);");
            }
            else
            {
                _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}{methodSuffix}(ref nestedReader);");
            }
        }

        #endregion

        #region ReadContent Method

        /// <summary>
        /// Generates Read{ClassName}Content(ref SpanReader reader) method.
        /// Creates instance and fills all fields.
        /// </summary>
        private void GenerateReadContentMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}Content(ref SpanReader reader)");
            _sb.StartNewBlock();

            // For enum types, generate simple VarInt read
            if (type.IsEnum)
            {
                _sb.AppendIndentedLine($"return (global::{type.FullName})reader.ReadVarInt32();");
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Enter();");
            _sb.AppendIndentedLine("try");
            _sb.StartNewBlock();

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimpleReadContent(type, className);
            }
            else
            {
                GenerateReadContentWithInheritance(type, className);
            }

            _sb.EndBlock(); // Close try block
            _sb.AppendIndentedLine("finally");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Exit();");
            _sb.EndBlock(); // Close finally block

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimpleReadContent(TypeDefinition type, string className)
        {
            // Analyze constructor strategy
            var constructorStrategy = AnalyzeConstructorStrategy(type);

            // DEBUG: Add comment showing strategy
            if (type.IsStruct && type.TypeSymbol != null)
            {
                int mappingCount = constructorStrategy.ParameterMappings?.Count ?? -1;
                _sb.AppendIndentedLine($"// DEBUG: Struct {type.FullName}");
                _sb.AppendIndentedLine($"//   TypeSymbol={type.TypeSymbol != null}, Constructor={constructorStrategy.Constructor != null}");
                _sb.AppendIndentedLine($"//   ParameterMappings={mappingCount}, UseParameterless={constructorStrategy.UseParameterlessConstructor}");
                _sb.AppendIndentedLine($"//   UseFormatterServices={constructorStrategy.UseFormatterServices}");
                _sb.AppendIndentedLine($"//   ProtoMembers count={type.ProtoMembers?.Count ?? 0}");
            }

            if (!constructorStrategy.IsSuccess)
            {
                // Constructor matching failed - generate error comment and skip field
                _sb.AppendIndentedLine($"// ERROR: {constructorStrategy.ErrorMessage}");
                _sb.AppendIndentedLine("reader.SkipField(WireType.VarInt); // Skip all fields due to constructor error");
                _sb.AppendIndentedLine($"return default(global::{type.FullName});");
                return;
            }

            bool useConstructor = constructorStrategy.Constructor != null &&
                                 constructorStrategy.ParameterMappings != null &&
                                 constructorStrategy.ParameterMappings.Count > 0;

            if (useConstructor)
            {
                // Generate deserialization with constructor call
                _sb.AppendIndentedLine($"// Using constructor with {constructorStrategy.ParameterMappings.Count} parameters");
                GenerateReadContentWithConstructor(type, className, constructorStrategy);
                return;
            }

            // Standard path: create instance first, then populate
            GenerateObjectCreation(type, "result");
            _sb.AppendNewLine();

            // Declare temp lists for array fields and IEnumerable interface fields (same as in Populate)
            var fieldsNeedingTempList = type.ProtoMembers?
                .Where(m => m.IsCollection && (
                    m.CollectionKind == CollectionKind.Array ||
                    (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                     TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                     !m.Type.Contains("ICollection") &&
                     !m.Type.Contains("IList"))
                ))
                .ToList();

            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                foreach (var member in fieldsNeedingTempList)
                {
                    var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
                }
                _sb.AppendNewLine();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for fields
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldReadCase(member);
                }

                // Default - skip unknown fields
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
            }
            else
            {
                // No fields - just skip
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.EndBlock();

            // Convert temp lists to arrays or assign to IEnumerable properties
            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
                foreach (var member in fieldsNeedingTempList)
                {
                    _sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                    _sb.StartNewBlock();

                    if (member.CollectionKind == CollectionKind.Array)
                    {
                        // Arrays need ToArray() conversion
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name}.ToArray();");
                    }
                    else
                    {
                        // IEnumerable can be assigned List directly (List implements IEnumerable)
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name};");
                    }

                    _sb.EndBlock();
                }
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates read content for types that use constructor with parameters (readonly struct support).
        /// Creates local variables for constructor parameters and calls constructor at the end.
        /// </summary>
        private void GenerateReadContentWithConstructor(
            TypeDefinition type,
            string className,
            ConstructorMatcher.ConstructorMatchResult constructorStrategy)
        {
            var fullTypeName = $"global::{type.FullName}";
            var mappings = constructorStrategy.ParameterMappings!;

            // Declare local variables for constructor parameters
            _sb.AppendIndentedLine("// Local variables for constructor parameters");
            foreach (var mapping in mappings.OrderBy(m => m.ParameterOrdinal))
            {
                // Use fully qualified type name
                var paramTypeName = mapping.FieldType.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
                _sb.AppendIndentedLine($"{paramTypeName} param_{mapping.ParameterName} = default;");
            }
            _sb.AppendNewLine();

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for fields
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in type.ProtoMembers)
                {
                    // Find mapping for this field
                    var mapping = mappings.FirstOrDefault(m => m.FieldName == member.Name);

                    if (mapping != null)
                    {
                        // This field corresponds to a constructor parameter
                        GenerateFieldReadCaseForParameter(member, $"param_{mapping.ParameterName}");
                    }
                }

                // Default - skip unknown fields
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
            }
            else
            {
                // No fields - just skip
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.EndBlock();

            // Call constructor with parameters
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"// Create instance using constructor");
            var constructorParams = mappings
                .OrderBy(m => m.ParameterOrdinal)
                .Select(m => $"param_{m.ParameterName}")
                .ToList();

            if (constructorParams.Count == 0)
            {
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}();");
            }
            else if (constructorParams.Count <= 3)
            {
                // Inline for short parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}({string.Join(", ", constructorParams)});");
            }
            else
            {
                // Multi-line for long parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}(");
                _sb.IncreaseIndent();
                for (int i = 0; i < constructorParams.Count; i++)
                {
                    var comma = i < constructorParams.Count - 1 ? "," : ");";
                    _sb.AppendIndentedLine($"{constructorParams[i]}{comma}");
                }
                _sb.DecreaseIndent();
            }

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates field read case that assigns to a local variable instead of object field.
        /// Used for constructor-based deserialization.
        /// </summary>
        private void GenerateFieldReadCaseForParameter(ProtoMemberAttribute member, string targetVariable)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");
            _sb.IncreaseIndent();

            var wireType = TypeMapping.GetWireType(member.Type, member.DataFormat);

            // Generate read statement based on type
            if (member.Type == "int" || member.Type == "System.Int32")
            {
                if (member.DataFormat == DataFormat.ZigZag)
                {
                    _sb.AppendIndentedLine($"{targetVariable} = reader.ReadZigZagInt32();");
                }
                else if (member.DataFormat == DataFormat.FixedSize)
                {
                    _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixed32AsInt();");
                }
                else
                {
                    _sb.AppendIndentedLine($"{targetVariable} = reader.ReadVarInt32();");
                }
            }
            else if (member.Type == "uint" || member.Type == "System.UInt32")
            {
                if (member.DataFormat == DataFormat.FixedSize)
                {
                    _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixed32();");
                }
                else
                {
                    _sb.AppendIndentedLine($"{targetVariable} = reader.ReadVarUInt32();");
                }
            }
            else if (member.Type.Contains("Enum") || IsEnumType(member.Type))
            {
                // Enum type
                _sb.AppendIndentedLine($"{targetVariable} = ({member.Type})reader.ReadVarInt32();");
            }
            else
            {
                // Complex type
                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

                var simpleName = Helpers.TypeNameHelper.GetClassName(member.Type);

                if (member.Namespace == _currentNamespace || string.IsNullOrEmpty(member.Namespace))
                {
                    _sb.AppendIndentedLine($"{targetVariable} = SpanReaders.Read{simpleName}Content(ref nestedReader);");
                }
                else
                {
                    _sb.AppendIndentedLine($"{targetVariable} = global::{member.Namespace}.Serialization.SpanReaders.Read{simpleName}Content(ref nestedReader);");
                }
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
        }

        private bool IsEnumType(string typeName)
        {
            // Simple heuristic - can be improved
            return typeName.Contains("Type") || typeName.Contains("Kind") || typeName.Contains("Status");
        }

        private void GenerateReadContentWithInheritance(TypeDefinition type, string className)
        {
            // Create instance (abstract types get default)
            if (type.IsAbstract)
            {
                _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            }
            else
            {
                GenerateObjectCreation(type, "result");
            }
            _sb.AppendNewLine();

            // Declare temp lists for array fields and IEnumerable interface fields (same as in Populate)
            // For flat inheritance, use merged fields; otherwise use own fields
            bool hasFlatInheritance = _registry.HasFlatInheritance(type.FullName);
            List<ProtoMemberAttribute> allMembers;

            if (hasFlatInheritance && (type.ProtoIncludes == null || type.ProtoIncludes.Count == 0))
            {
                // Flat inheritance: use merged fields
                var mergedFields = _registry.GetMergedFields(type.FullName);
                allMembers = mergedFields.Select(mf => mf.Field).ToList();
            }
            else
            {
                // ProtoInclude or no inheritance: use own fields
                allMembers = type.ProtoMembers?.ToList() ?? new List<ProtoMemberAttribute>();
            }

            var fieldsNeedingTempList = allMembers
                .Where(m => m.IsCollection && (
                    m.CollectionKind == CollectionKind.Array ||
                    (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                     TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                     !m.Type.Contains("ICollection") &&
                     !m.Type.Contains("IList"))
                ))
                .ToList();

            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                foreach (var member in fieldsNeedingTempList)
                {
                    var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
                }
                _sb.AppendNewLine();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for all fields including ProtoIncludes
            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            // Handle ProtoIncludes first
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeReadCase(type, include);
                }
            }

            // Check if this is flat inheritance (without ProtoInclude) - hasFlatInheritance already declared above
            if (hasFlatInheritance && (type.ProtoIncludes == null || type.ProtoIncludes.Count == 0))
            {
                // Flat inheritance: generate merged switch (base + derived fields with shadowing)
                var mergedFields = _registry.GetMergedFields(type.FullName);

                if (mergedFields.Count > 0)
                {
                    _sb.AppendIndentedLine($"// Merged fields from inheritance chain (derived shadows base)");
                    foreach (var mergedField in mergedFields)
                    {
                        GenerateFieldReadCase(mergedField.Field);
                    }
                }
            }
            else
            {
                // ProtoInclude inheritance or no inheritance: use own fields only
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldReadCase(member);
                    }
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
            _sb.EndBlock();

            // Convert temp lists to arrays or assign to IEnumerable properties
            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
                foreach (var member in fieldsNeedingTempList)
                {
                    _sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                    _sb.StartNewBlock();

                    if (member.CollectionKind == CollectionKind.Array)
                    {
                        // Arrays need ToArray() conversion
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name}.ToArray();");
                    }
                    else
                    {
                        // IEnumerable can be assigned List directly (List implements IEnumerable)
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name};");
                    }

                    _sb.EndBlock();
                }
            }


            _sb.AppendIndentedLine("return result;");
        }

        #endregion

        #region Populate Method

        /// <summary>
        /// Generates Populate{ClassName}(ref SpanReader reader, T instance) method.
        /// Fills existing instance without allocating a new one.
        /// </summary>
        private void GeneratePopulateMethod(TypeDefinition type)
        {
            // Skip abstract types - can't populate them directly
            if (type.IsAbstract)
                return;

            var className = TypeNameHelper.GetClassName(type.FullName);

            // Check if this is a readonly struct with readonly fields
            bool isReadonlyStruct = false;
            if (type.IsStruct && type.TypeSymbol != null)
            {
                bool hasReadonlyFields = type.ProtoMembers?.Any(m =>
                {
                    var member = type.TypeSymbol.GetMembers(m.Name).FirstOrDefault();
                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        return field.IsReadOnly;
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        return property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public;
                    }
                    return false;
                }) ?? false;

                isReadonlyStruct = hasReadonlyFields;
            }

            // Generate Populate method signature
            _sb.AppendIndentedLine($"public static void Populate{className}(ref SpanReader reader, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            if (isReadonlyStruct)
            {
                // For readonly structs, Populate method is a no-op
                // Fields cannot be modified after construction, so just consume the reader
                _sb.AppendIndentedLine("// Readonly struct - fields cannot be modified after construction");
                _sb.AppendIndentedLine("// This method consumes the reader but does not modify the instance");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var _);");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.EndBlock();
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            // Standard Populate implementation for mutable types

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimplePopulate(type, className);
            }
            else
            {
                // For types with inheritance, populate is more complex
                // For now, just populate own fields
                GenerateSimplePopulate(type, className);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimplePopulate(TypeDefinition type, string className)
        {
            // Declare temp lists for array fields and IEnumerable interface fields
            // Arrays can't use Add(), and IEnumerable<T> doesn't have Add() method
            var fieldsNeedingTempList = type.ProtoMembers?
                .Where(m => m.IsCollection && (
                    m.CollectionKind == CollectionKind.Array ||
                    (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                     TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                     !m.Type.Contains("ICollection") &&
                     !m.Type.Contains("IList"))
                ))
                .ToList();

            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                foreach (var member in fieldsNeedingTempList)
                {
                    var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
                }
                _sb.AppendNewLine();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for fields
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldPopulateCase(member);
                }

                // Default - skip unknown fields
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock();
            }
            else
            {
                // No fields - just skip
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.EndBlock();

            // Convert temp lists to arrays or assign to IEnumerable properties
            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
                foreach (var member in fieldsNeedingTempList)
                {
                    _sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                    _sb.StartNewBlock();

                    if (member.CollectionKind == CollectionKind.Array)
                    {
                        // Arrays need ToArray() conversion
                        _sb.AppendIndentedLine($"instance.{member.Name} = _tempList_{member.Name}.ToArray();");
                    }
                    else
                    {
                        // IEnumerable can be assigned List directly (List implements IEnumerable)
                        _sb.AppendIndentedLine($"instance.{member.Name} = _tempList_{member.Name};");
                    }

                    _sb.EndBlock();
                }
            }
        }

        /// <summary>
        /// Generates switch case for populating a single field.
        /// Uses 'instance' instead of 'result'.
        /// </summary>
        private void GenerateFieldPopulateCase(ProtoMemberAttribute member)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");

            // Determine if we need braces for variable scoping
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.IncreaseIndent();
            }

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry);
                mapHandler.GenerateRead(member, $"instance.{member.Name}");
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldPopulateBody(member);
            }
            else if (member.IsEnum)
            {
                // Use fully qualified type name for enums to avoid namespace issues
                _sb.AppendIndentedLine($"instance.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"instance.{member.Name}", member.Type);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"instance.{member.Name}", member.Type, member.DataFormat);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type (e.g., System.Type) - skip field with warning comment
                _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be deserialized)");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
            else
            {
                // Complex type - nested message
                var typeName = TypeNameHelper.GetClassName(member.Type);
                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

                // Check if type is from different namespace and qualify the call
                var typeNamespace = _registry.GetNamespaceForType(member.Type);
                if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
                {
                    _sb.AppendIndentedLine($"instance.{member.Name} = global::{typeNamespace}.Serialization.SpanReaders.Read{typeName}Content(ref nestedReader);");
                }
                else
                {
                    _sb.AppendIndentedLine($"instance.{member.Name} = Read{typeName}Content(ref nestedReader);");
                }
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                _sb.DecreaseIndent();
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GenerateCollectionFieldPopulateBody(ProtoMemberAttribute member)
        {
            // Check if element type is enum (enums use packed encoding like primitives)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
                // Level200: Primitives use UNPACKED encoding by default
                // Reader must accept both PACKED (for IsPacked=true) and UNPACKED (default) for backward compatibility
                bool shouldBePacked = member.IsPacked;

                if (shouldBePacked)
                {
                    _primitiveHandler.GenerateDualModePackedArrayRead(
                        _sb,
                        $"instance.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type,
                        "wireType",
                        "reader");
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"instance.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        member.CollectionKind,
                        member.Type);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionRead(
                    $"instance.{member.Name}",
                    member.CollectionElementType,
                    member.FieldId,
                    member.CollectionKind,
                    member.Type,
                    "reader");
            }
            else
            {
                // Complex type collection (for Populate methods)
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionRead(
                    $"instance.{member.Name}",
                    member.CollectionElementType,
                    elementClassName,
                    member.CollectionKind,
                    member.Type);
            }
        }

        #endregion

        #region Field Generation

        /// <summary>
        /// Determines if wire type validation should be generated for this member.
        /// Collections with dual-mode support don't need validation here (handled in the handler).
        /// </summary>
        private bool ShouldGenerateWireTypeValidation(ProtoMemberAttribute member)
        {
            // Collections with dual-mode (packed/unpacked) support handle wire type internally
            if (member.IsCollection)
            {
                // Only primitive collections with IsPacked=true use dual-mode
                // (default UNPACKED uses non-packed reader which validates wire type itself)
                bool isDualMode = member.IsPacked;
                return !isDualMode;
            }

            return true;
        }

        /// <summary>
        /// Generates wire type validation check.
        /// OPT-1: Wire type validation REMOVED for performance (fail-fast approach).
        /// Read methods now align with Populate methods behavior.
        /// Malformed data will throw exception in ReadXXX methods.
        /// </summary>
        private void GenerateWireTypeValidation(ProtoMemberAttribute member, string wireTypeVar = "wireType", string readerVar = "reader")
        {
            // OPT-1: Wire type validation removed for performance
            // Read methods now use fail-fast approach (same as Populate methods)
            // Malformed data will throw exception in ReadXXX methods instead of graceful skip

            // Skip validation for collections with dual-mode support
            if (!ShouldGenerateWireTypeValidation(member))
                return;

            // ✅ REMOVED: Wire type check and skip logic
            // Aligns Read behavior with Populate behavior (consistency)
            // Expected performance gain: 10-15% for primitive-heavy messages
            // Trade-off: Malformed data throws exception instead of silent skip

            // var expectedWireType = GetExpectedWireTypeString(member);
            // _sb.AppendIndentedLine($"if ({wireTypeVar} != {expectedWireType})");
            // _sb.StartNewBlock();
            // _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            // _sb.AppendIndentedLine("break;");
            // _sb.EndBlock();
        }

        /// <summary>
        /// Generates switch case for reading a single field with lazy instance initialization.
        /// Used for inheritance scenarios where result starts as null.
        /// </summary>
        private void GenerateFieldReadCaseWithLazyInit(ProtoMemberAttribute member, string lazyInit)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            GenerateWireTypeValidation(member);

            // Add lazy initialization before accessing result
            if (!string.IsNullOrEmpty(lazyInit))
            {
                _sb.AppendIndentedLine(lazyInit);
            }

            // Generate field read body
            if (member.IsMap)
            {
                GenerateMapFieldReadBody(member);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBody(member);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldReadBody(member);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type (e.g., System.Type) - skip field with warning comment
                _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be deserialized)");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
            else
            {
                GenerateComplexTypeReadBody(member);
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
            _sb.DecreaseIndent();
        }

        /// <summary>
        /// Generates switch case for reading a single field based on its type.
        /// </summary>
        private void GenerateFieldReadCase(ProtoMemberAttribute member)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");

            // Determine if we need braces for variable scoping
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.IncreaseIndent();
            }

            // Phase 1: Wire type validation (Level200 requirement)
            GenerateWireTypeValidation(member);

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                GenerateMapFieldReadBody(member);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBody(member);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldReadBody(member);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type (e.g., System.Type) - skip field with warning comment
                _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be deserialized)");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
            else
            {
                // Complex type - nested message
                GenerateComplexTypeReadBody(member);
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                _sb.DecreaseIndent();
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GenerateProtoIncludeReadCase(TypeDefinition parentType, ProtoIncludeAttribute include)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);

            _sb.AppendIndentedLine($"case {include.FieldId}:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // ProtoInclude always expects WireType.Len
            // If wrong wire type, ReadVarInt32() will throw exception

            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

            // When ReadDerivedContent returns a more derived type (e.g., D instead of B),
            // we must preserve fields already read from current type (e.g., StringB in B)
            _sb.AppendIndentedLine($"var oldResult = result;");

            // Read derived content (ONLY derived fields, from nested reader)
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");

            // Copy fields from old result to new result (if old result had values)
            _sb.AppendIndentedLine("if (oldResult != null)");
            _sb.StartNewBlock();
            if (parentType.ProtoMembers != null)
            {
                foreach (var member in parentType.ProtoMembers)
                {
                    // Copy each field from old result to new result
                    // This preserves values read before the ProtoInclude wrapper
                    _sb.AppendIndentedLine($"result.{member.Name} = oldResult.{member.Name};");
                }
            }
            _sb.EndBlock();

            // We continue the while loop to read base fields that come AFTER the ProtoInclude wrapper.
            // The reader position was already moved forward by GetSlice(), so we read from the correct position.
            // Base fields (like RequestsId) will be read by the subsequent case statements in the switch.
            _sb.AppendIndentedLine("continue;");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
            _sb.DecreaseIndent();
        }

        // Body versions for switch case (without continue/break - those are added by GenerateFieldReadCase)
        private void GenerateEnumFieldReadBody(ProtoMemberAttribute member)
        {
            // Use fully qualified type name for enums to avoid namespace issues
            _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
            mapHandler.GenerateRead(member, $"result.{member.Name}");
        }

        private void GenerateCollectionFieldReadBody(ProtoMemberAttribute member)
        {
            // Check if element type is enum (enums use packed encoding like primitives)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            // Check if it's a primitive collection that can use PrimitiveHandler
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
                // Level200: Primitives use UNPACKED encoding by default
                // Reader must accept both PACKED (for IsPacked=true) and UNPACKED (default) for backward compatibility
                bool shouldBePacked = member.IsPacked;

                if (shouldBePacked)
                {
                    _primitiveHandler.GenerateDualModePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type,
                        "wireType",
                        "reader");
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        member.CollectionKind,
                        member.Type);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    member.FieldId,
                    member.CollectionKind,
                    member.Type,
                    "reader");
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionRead(
                    $"result.{member.Name}",
                    member.CollectionElementType,
                    elementClassName,
                    member.CollectionKind,
                    member.Type);
            }
        }

        private void GenerateComplexTypeReadBody(ProtoMemberAttribute member)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);

            // Check if field is a concrete nested derived type (requires ProtoInclude wrapper detection)
            bool isNestedDerivedType = _registry.IsConcreteNestedDerivedType(member);

            if (isNestedDerivedType)
            {
                GenerateNestedDerivedTypeReadBody(member, typeName);
            }
            else
            {
                // Standard complex type read (no wrapper)
                GenerateStandardComplexTypeReadBody(member, typeName);
            }
        }

        /// <summary>
        /// Generates read code for nested derived type fields (with ProtoInclude wrapper detection).
        /// Wire format: [field tag][total length] [wrapper tag][wrapper length] [derived fields] [base fields AFTER]
        /// Uses PeekTag() for non-destructive wrapper detection.
        /// </summary>
        private void GenerateNestedDerivedTypeReadBody(ProtoMemberAttribute member, string typeName)
        {
            var typeNamespace = _registry.GetNamespaceForType(member.Type);

            // Get parent type and expected wrapper tag
            var parentTypeFullName = _registry.GetParent(member.Type);
            var parentType = _registry.GetByFullName(parentTypeFullName);
            var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == member.Type);

            if (protoInclude == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type} in {parentTypeFullName}");
                GenerateStandardComplexTypeReadBody(member, typeName);
                return;
            }

            var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;
            var parentTypeName = TypeNameHelper.GetClassName(parentTypeFullName);

            _sb.AppendIndentedLine($"// ProtoInclude wrapper detection for nested derived type {typeName}");
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendNewLine();

            // Peek at first tag to detect wrapper
            _sb.AppendIndentedLine($"// Detect ProtoInclude wrapper (tag {wrapperTag} for field {protoInclude.FieldId})");
            _sb.AppendIndentedLine("var firstTag = nestedReader.PeekTag();");
            _sb.AppendIndentedLine($"if (firstTag == {wrapperTag}u)");
            _sb.StartNewBlock();

            // Wrapper detected - read wrapper and derived fields
            _sb.AppendIndentedLine("// Wrapper present - read derived fields from wrapper");
            _sb.AppendIndentedLine("nestedReader.ReadWireTypeAndFieldId(out var wrapperWireType, out var wrapperFieldId);");
            _sb.AppendIndentedLine("var wrapperLength = nestedReader.ReadVarInt32();");
            _sb.AppendIndentedLine("var wrapperReader = new SpanReader(nestedReader.GetSlice(wrapperLength));");
            _sb.AppendNewLine();

            // Read derived content (wrapper + base fields)
            // IMPORTANT: Read{Type}Content for derived type already handles:
            // 1. Reading derived-specific fields from wrapperReader
            // 2. Reading base fields from nestedReader (after wrapper)
            // We need to read ONLY the wrapper content here, then let ReadContent handle base fields

            string derivedReaderPrefix = !string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace
                ? $"global::{typeNamespace}.Serialization.SpanReaders."
                : "";

            _sb.AppendIndentedLine($"// Read derived object from wrapper (derived fields only)");
            _sb.AppendIndentedLine($"result.{member.Name} = new global::{member.Type}();");
            _sb.AppendIndentedLine($"{derivedReaderPrefix}Populate{typeName}(ref wrapperReader, result.{member.Name});");
            _sb.AppendNewLine();

            // Read remaining base fields (AFTER wrapper) by reading parent instance and copying fields
            _sb.AppendIndentedLine($"// Read base fields (AFTER wrapper) - read as parent type and copy fields");

            // Get parent type reader prefix
            var parentNamespace = _registry.GetNamespaceForType(parentTypeFullName);
            string parentReaderPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                ? $"global::{parentNamespace}.Serialization.SpanReaders."
                : "";

            // Read parent instance
            _sb.AppendIndentedLine($"var baseInstance = {parentReaderPrefix}Read{parentTypeName}Content(ref nestedReader);");

            // Copy base fields from parent instance to derived instance
            var parentTypeDef = parentType;
            if (parentTypeDef?.ProtoMembers != null && parentTypeDef.ProtoMembers.Any())
            {
                _sb.AppendIndentedLine($"// Copy base fields from parent instance to derived instance");
                foreach (var baseMember in parentTypeDef.ProtoMembers)
                {
                    _sb.AppendIndentedLine($"result.{member.Name}.{baseMember.Name} = baseInstance.{baseMember.Name};");
                }
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();

            // No wrapper - direct deserialization (backward compatibility or base type instance)
            _sb.AppendIndentedLine("// No wrapper - read as standard type");
            _sb.AppendIndentedLine($"result.{member.Name} = {derivedReaderPrefix}Read{typeName}Content(ref nestedReader);");

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates standard read code for complex types (no wrapper detection).
        /// </summary>
        private void GenerateStandardComplexTypeReadBody(ProtoMemberAttribute member, string typeName)
        {
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

            // Check if type is from different namespace and qualify the call
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"result.{member.Name} = global::{typeNamespace}.Serialization.SpanReaders.Read{typeName}Content(ref nestedReader);");
            }
            else
            {
                _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
            }
        }

        /// <summary>
        /// Validates all virtual map types and writes diagnostic warnings to generated code for problematic types.
        /// This helps identify type analysis bugs where CollectionElementTypeInfo or other info is null.
        /// </summary>
        private void ValidateVirtualMapTypes(System.Collections.Generic.IReadOnlyList<VirtualMapEntryInfo> virtualTypes)
        {
            var warnings = new System.Collections.Generic.List<string>();

            foreach (var virtualType in virtualTypes)
            {
                // Check KeyTypeInfo
                if (virtualType.KeyTypeInfo == null)
                {
                    warnings.Add($"Virtual map '{virtualType.TypeName}': KeyTypeInfo is null (KeyType='{virtualType.KeyType}')");
                }

                // Check ValueTypeInfo
                if (virtualType.ValueTypeInfo == null)
                {
                    warnings.Add($"Virtual map '{virtualType.TypeName}': ValueTypeInfo is null (ValueType='{virtualType.ValueType}')");
                }
                else
                {
                    // Check nested type info for collections
                    if (virtualType.ValueTypeInfo.IsCollection && virtualType.ValueTypeInfo.CollectionElementTypeInfo == null)
                    {
                        warnings.Add($"Virtual map '{virtualType.TypeName}': ValueType is collection but CollectionElementTypeInfo is null (ValueType='{virtualType.ValueType}', ElementType='{virtualType.ValueTypeInfo.CollectionElementType}')");
                    }

                    // Check nested type info for dictionaries
                    if (virtualType.ValueTypeInfo.IsDictionary)
                    {
                        if (string.IsNullOrEmpty(virtualType.ValueTypeInfo.DictionaryKeyType))
                        {
                            warnings.Add($"Virtual map '{virtualType.TypeName}': ValueType is dictionary but DictionaryKeyType is null/empty (ValueType='{virtualType.ValueType}')");
                        }
                        if (string.IsNullOrEmpty(virtualType.ValueTypeInfo.DictionaryValueType))
                        {
                            warnings.Add($"Virtual map '{virtualType.TypeName}': ValueType is dictionary but DictionaryValueType is null/empty (ValueType='{virtualType.ValueType}')");
                        }
                    }
                }
            }

            // If there are warnings, add them as comments in generated code for diagnostics
            if (warnings.Count > 0)
            {
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("// ═══════════════════════════════════════════════════════════════════════════════");
                _sb.AppendIndentedLine($"// ⚠️  TYPE ANALYSIS WARNINGS ({warnings.Count} issues found)");
                _sb.AppendIndentedLine("// ═══════════════════════════════════════════════════════════════════════════════");
                _sb.AppendIndentedLine("// The following virtual map types have null or incomplete type analysis info.");
                _sb.AppendIndentedLine("// This indicates a bug in VirtualMapTypeRegistry.AnalyzeType or ParseSingleGenericArg.");
                _sb.AppendIndentedLine("// Generation will proceed with fallback behavior but may produce incorrect code.");
                _sb.AppendIndentedLine("// ═══════════════════════════════════════════════════════════════════════════════");

                foreach (var warning in warnings)
                {
                    _sb.AppendIndentedLine($"// ⚠️  {warning}");
                }

                _sb.AppendIndentedLine("// ═══════════════════════════════════════════════════════════════════════════════");
                _sb.AppendNewLine();
            }
        }

        #endregion
    }
}
