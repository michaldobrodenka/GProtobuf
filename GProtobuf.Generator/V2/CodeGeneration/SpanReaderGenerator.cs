using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.CodeGeneration;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;
using GProtobuf.Generator.WireFormat;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates SpanReaders class with Read{ClassName} and Read{ClassName}Content methods.
    /// Handles deserialization from SpanReader to object instances.
    /// </summary>
    internal class SpanReaderGenerator : GeneratorBase
    {
        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, GeneratorOptions options = null)
            : base(sb, registry, options)
        {
        }

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, GeneratorOptions options = null)
            : base(sb, registry, virtualMapRegistry, options)
        {
        }

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, GeneratorOptions options = null)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options)
        {
        }

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, GeneratorOptions options, string virtualTypesNamespace)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options, virtualTypesNamespace)
        {
        }

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
        /// Generates SpanReaders class containing ONLY virtual types (map entries and tuples).
        /// Used for GProtobuf.Generated namespace which centralizes all virtual type methods.
        /// </summary>
        public void GenerateVirtualTypesOnly(string currentNamespace)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine("public static class SpanReaders");
            _sb.StartNewBlock();

            // Generate virtual map entry readers (all types, ignoring IsGenerated flag)
            GenerateVirtualMapEntryReaders(ignoreIsGeneratedFlag: true);

            // Generate virtual tuple readers (all types, ignoring IsGenerated flag)
            GenerateVirtualTupleReaders(ignoreIsGeneratedFlag: true);

            _sb.EndBlock();
            _sb.AppendNewLine();
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

                if (!_registry.IsReadonlyStruct(type.FullName))
                {
                    try
                    {
                        GeneratePopulateMethod(type);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in GeneratePopulateMethod for type '{type.FullName}'", ex);
                    }
                }

                // Generate OwnFieldsPopulate for derived types
                if (_registry.IsDerivedType(type.FullName))
                {
                    try
                    {
                        var className = TypeNameHelper.GetClassName(type.FullName);
                        GenerateOwnFieldsPopulateMethod(type, className);
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception($"Error in GenerateOwnFieldsPopulateMethod for type '{type.FullName}'", ex);
                    }
                }
            }

            // Generate ReadContent methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

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

                    if (!_registry.IsReadonlyStruct(protoIncludeTypeName))
                    {
                        try
                        {
                            GeneratePopulateMethod(protoIncludeType);
                        }
                        catch (System.Exception ex)
                        {
                            throw new System.Exception($"Error in GeneratePopulateMethod for ProtoInclude type '{protoIncludeTypeName}'", ex);
                        }
                    }

                    // Generate OwnFieldsPopulate for ProtoInclude derived types
                    if (_registry.IsDerivedType(protoIncludeTypeName))
                    {
                        try
                        {
                            var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                            GenerateOwnFieldsPopulateMethod(protoIncludeType, className);
                        }
                        catch (System.Exception ex)
                        {
                            throw new System.Exception($"Error in GenerateOwnFieldsPopulateMethod for ProtoInclude type '{protoIncludeTypeName}'", ex);
                        }
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            // Virtual map entry and tuple readers are NOT generated here - they are centralized
            // in GProtobuf.Generated.Serialization.cs via GenerateVirtualTypesOnly().
            // Types are registered during field processing above, then generated once in the shared file.

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates reader methods for all registered virtual map entry types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualMapEntryReaders(bool ignoreIsGeneratedFlag)
        {
            System.Collections.Generic.IReadOnlyList<VirtualMapEntryInfo> allTypes;
            try
            {
                allTypes = _virtualMapRegistry.GetAllTypes();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("Error calling _virtualMapRegistry.GetAllTypes()", ex);
            }

            // Filter types based on IsGenerated flag
            var virtualTypes = ignoreIsGeneratedFlag
                ? allTypes.ToList()
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (virtualTypes.Count == 0) return;

            // Validate all virtual map types and report warnings for problematic ones
            ValidateVirtualMapTypes(virtualTypes);

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Readers");
            _sb.AppendNewLine();

            VirtualMapEntryGenerator generator;
            try
            {
                generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry, "Span", _virtualTypesNamespace);
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

        }

        /// <summary>
        /// Generates reader methods for all registered virtual tuple types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualTupleReaders(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualTupleRegistry.GetAllTypes();

            // Filter types based on IsGenerated flag
            var tupleTypes = ignoreIsGeneratedFlag
                ? allTypes
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Readers");

            var generator = new VirtualTupleGenerator(_sb, null, _registry, _virtualTypesNamespace);
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
            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                // Simple case - no inheritance, use expression body for compact code
                _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref SpanReader reader) => Read{className}Content(ref reader);");
                _sb.AppendNewLine();
            }
            else
            {
                // Complex case with inheritance - handle ProtoIncludes
                _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref SpanReader reader)");
                _sb.StartNewBlock();
                GenerateReadMethodWithInheritance(type, className);
                _sb.EndBlock();
                _sb.AppendNewLine();
            }
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
        /// Uses binary dispatch for O(log n) performance when there are many ProtoIncludes.
        /// </summary>
        private void GenerateReadMethodForBaseWithIncludes(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            int protoIncludeCount = type.ProtoIncludes?.Count ?? 0;
            int protoMemberCount = type.ProtoMembers?.Count ?? 0;
            bool useBinaryDispatch = BinaryDispatchAnalyzer.ShouldUseBinaryDispatch(protoIncludeCount);

            if (useBinaryDispatch && protoIncludeCount > 0)
            {
                // Use binary dispatch for ProtoIncludes
                GenerateReadMethodWithBinaryDispatch(type, className);
            }
            else
            {
                // Fall back to switch-based dispatch
                GenerateReadMethodWithSwitchDispatch(type, className);
            }

            _sb.EndBlock(); // while

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
        /// Generates the dispatch logic using binary search for ProtoIncludes.
        /// O(log n) comparisons instead of O(n) with linear switch.
        /// </summary>
        private void GenerateReadMethodWithBinaryDispatch(TypeDefinition type, string className)
        {
            var protoIncludeTree = BinaryDispatchAnalyzer.BuildTree(type.ProtoIncludes);
            var binaryDispatch = new BinaryDispatchGenerator(_sb, "fieldId");

            string lazyInit = type.IsAbstract ? null : $"result ??= new global::{type.FullName}();";
            bool hasProtoMembers = type.ProtoMembers != null && type.ProtoMembers.Count > 0;

            if (hasProtoMembers)
            {
                // Optimized range-separated dispatch:
                // - ProtoIncludes checked first (binary dispatch)
                // - Regular fields switch appears ONCE (not duplicated in every branch)
                binaryDispatch.GenerateRangeSeparatedDispatch(
                    protoIncludeTree,
                    generateProtoIncludeCase: (fieldId, typeName) =>
                    {
                        GenerateProtoIncludeReadCaseBody(type, fieldId, typeName);
                    },
                    generateRegularFieldsSwitch: () =>
                    {
                        // Generate switch for ProtoMembers - appears ONCE
                        _sb.AppendIndentedLine("// Regular fields switch (sorted by field ID for optimal branch prediction)");
                        _sb.AppendIndentedLine("switch (fieldId)");
                        _sb.StartNewBlock();

                        // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                        foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                        {
                            GenerateFieldReadCaseWithLazyInit(member, lazyInit);
                        }

                        // Default - skip unknown fields
                        _sb.AppendIndentedLine("default:");
                        _sb.IncreaseIndent();
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                        _sb.AppendIndentedLine("break;");
                        _sb.DecreaseIndent();

                        _sb.EndBlock();
                    },
                    generateSkipField: () =>
                    {
                        // Unknown ProtoInclude field ID - skip it
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    }
                );
            }
            else
            {
                // Pure ProtoIncludes - binary dispatch only
                binaryDispatch.Generate(
                    protoIncludeTree,
                    generateCaseBody: (fieldId, typeName) =>
                    {
                        GenerateProtoIncludeReadCaseBody(type, fieldId, typeName);
                    },
                    generateDefault: () =>
                    {
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    }
                );
            }
        }

        /// <summary>
        /// Generates the traditional switch-based dispatch for ProtoIncludes.
        /// Used when the number of cases is below the threshold.
        /// </summary>
        private void GenerateReadMethodWithSwitchDispatch(TypeDefinition type, string className)
        {
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
            // Base classes with ProtoMembers read their fields via the switch cases.
            // Fields with lower IDs than ProtoInclude are read BEFORE the wrapper,
            // then copied to the derived instance via oldResult pattern.
            //
            // Example: A has StringA (field 1) and ProtoInclude(5, B).
            // Wire format: [tag 1 = StringA][value][tag 5 = B wrapper][B data]
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
        }

        /// <summary>
        /// Generates the body of a ProtoInclude case (without case X: prefix).
        /// Used by binary dispatch generator.
        /// </summary>
        private void GenerateProtoIncludeReadCaseBody(TypeDefinition parentType, int fieldId, string derivedTypeName)
        {
            var derivedClassName = TypeNameHelper.GetClassName(derivedTypeName);

            // ProtoInclude always expects WireType.Len
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

            // Read derived content (derived fields from nested reader)
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");

            // Continue to next iteration
            _sb.AppendIndentedLine("continue;");
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

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();
            var fieldsUsingTempList = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.NeedsTempListDeclaration(m, _registry)).ToList();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for struct/primitive element collections
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if (fieldsNeedingTempList.Count > 0)
                _sb.AppendNewLine();

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Generate nested reading for each level
            GenerateNestedReading(chain, 0, "reader");

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "result");

            // Finalize List<T> fields
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "result");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
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

            _sb.AppendIndentedLine($"case {include.FieldId}: {{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine($"var length{nextLevelIndex} = {currentReaderVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {nestedReaderVar} = new SpanReader({currentReaderVar}.GetSlice(length{nextLevelIndex}));");

            // Recursively generate reading for next level
            GenerateNestedReading(chain, nextLevelIndex, nestedReaderVar);

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates switch case for reading a field into 'result' for derived types.
        /// </summary>
        private void GenerateFieldReadCaseForDerived(ProtoMemberAttribute member, string wireTypeVar, string readerVar)
        {
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            // Phase 1: Wire type validation (Level200 requirement)
            GenerateWireTypeValidation(member, wireTypeVar, readerVar);

            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry, _virtualTypesNamespace);
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
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, readerVar, wireTypeVar, UseStringPooling);
            }
            else if (member.IsProtoVarint)
            {
                // ProtoVarint type - read varint and construct using the constructor
                ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}", readerVar);
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
                    readerVar,
                    useObjectArrayBuilder: ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry));
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

            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"result.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}{methodSuffix}(ref nestedReader);");
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

            // For custom collection types (implements IEnumerable<T> + Add(T))
            // Serialize as repeated field with implicit field id 1
            if (type.IsCustomCollection && !string.IsNullOrEmpty(type.CustomCollectionElementType))
            {
                GenerateCustomCollectionReadContent(type, className);
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            if (type.EnableRecursionGuard)
            {
                _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Enter();");
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimpleReadContent(type, className);
            }
            else
            {
                GenerateReadContentWithInheritance(type, className);
            }

            if (type.EnableRecursionGuard)
            {
                _sb.EndBlock(); // Close try block
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Exit();");
                _sb.EndBlock(); // Close finally block
            }

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
                _sb.AppendIndentedLine($"// ERROR: {constructorStrategy.ErrorMessage}");
                _sb.AppendIndentedLine($"throw new global::System.InvalidOperationException(\"Cannot deserialize type '{type.FullName}': {constructorStrategy.ErrorMessage?.Replace("\"", "\\\"")}\");");
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

            GenerateObjectCreation(type, "result");
            _sb.AppendIndentedLine($"Populate{className}(ref reader, {GeneratorHelpers.GetPopulateInstanceArgument(type, "result")});");
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates read content for custom collection types (implements IEnumerable&lt;T&gt; + Add(T)).
        /// Reads repeated elements with implicit field id 1 and adds them to the collection.
        /// </summary>
        private void GenerateCustomCollectionReadContent(TypeDefinition type, string className)
        {
            var elementType = type.CustomCollectionElementType;
            var globalElementType = TypeMapping.GetGlobalTypeName(elementType);

            _sb.AppendIndentedLine($"var result = new global::{type.FullName}();");
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Field id 1 is used for collection elements (protobuf-net convention)
            _sb.AppendIndentedLine("if (fieldId == 1)");
            _sb.StartNewBlock();

            // Check if element type is primitive, enum, or complex
            bool isSimpleType = TypeMapping.IsSimpleType(elementType);
            bool isEnum = _registry.IsEnum(elementType);

            if (isSimpleType)
            {
                var readExpr = TypeMapping.GetReadExpression(elementType, DataFormat.Default, "reader", "wireType");
                _sb.AppendIndentedLine($"result.Add({readExpr});");
            }
            else if (isEnum)
            {
                _sb.AppendIndentedLine($"result.Add(({globalElementType})reader.ReadVarInt32());");
            }
            else if (_registry.IsProtoVarint(TypeMapping.NormalizeTypeName(elementType)))
            {
                // ProtoVarint type - read varint and construct using the constructor
                var varintType = _registry.GetProtoVarintType(TypeMapping.NormalizeTypeName(elementType)) ?? Attributes.ProtoVarintType.UInt32;
                var valueMember = _registry.GetProtoVarintValueMember(TypeMapping.NormalizeTypeName(elementType));
                var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType);
                _sb.AppendIndentedLine($"result.Add(new {globalElementType}(reader.{readMethod}()));");
            }
            else
            {
                // Complex type - need to read length-prefixed message
                var elementClassName = TypeNameHelper.GetClassName(elementType);
                var elementNs = _registry.GetNamespaceForType(elementType);
                var qualifiedReadCall = string.IsNullOrEmpty(elementNs) || elementNs == _currentNamespace
                    ? $"Read{elementClassName}Content"
                    : $"global::{elementNs}.Serialization.SpanReaders.Read{elementClassName}Content";

                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
                _sb.AppendIndentedLine($"result.Add({qualifiedReadCall}(ref nestedReader));");
            }

            _sb.EndBlock(); // if fieldId == 1
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.EndBlock(); // else

            _sb.EndBlock(); // while
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

            // Generate switch for fields (sorted by field ID for optimal branch prediction)
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
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
            else if (member.IsProtoVarint)
            {
                // ProtoVarint type - read varint and construct using the constructor
                ProtoVarintTypeSupport.GenerateRead(_sb, member, targetVariable);
            }
            else
            {
                // Complex type
                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

                var simpleName = GProtobuf.Generator.Utilities.TypeNameHelper.GetClassName(member.Type);

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

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList?.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();
            var fieldsUsingTempList = fieldsNeedingTempList?.Where(m => ObjectArrayBuilderHelper.NeedsTempListDeclaration(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for struct/primitive element collections
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if ((fieldsNeedingTempList?.Count ?? 0) > 0)
                _sb.AppendNewLine();

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            int protoIncludeCount = type.ProtoIncludes?.Count ?? 0;
            bool useBinaryDispatch = BinaryDispatchAnalyzer.ShouldUseBinaryDispatch(protoIncludeCount);

            if (useBinaryDispatch && protoIncludeCount > 0)
            {
                // Use binary dispatch for ProtoIncludes (O(log n) comparisons)
                GenerateReadContentWithBinaryDispatch(type, hasFlatInheritance);
            }
            else
            {
                // Fall back to switch-based dispatch
                GenerateReadContentWithSwitchDispatch(type, hasFlatInheritance);
            }

            _sb.EndBlock(); // while

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "result");

            // Finalize List<T> fields
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "result");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }

            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates binary dispatch for ProtoIncludes in ReadContent method.
        /// </summary>
        private void GenerateReadContentWithBinaryDispatch(TypeDefinition type, bool hasFlatInheritance)
        {
            var protoIncludeTree = BinaryDispatchAnalyzer.BuildTree(type.ProtoIncludes);
            var binaryDispatch = new BinaryDispatchGenerator(_sb, "fieldId");

            // Determine which fields to include in the fallback switch
            System.Collections.Generic.List<ProtoMemberAttribute> fieldsToInclude;
            if (hasFlatInheritance && (type.ProtoIncludes == null || type.ProtoIncludes.Count == 0))
            {
                var mergedFields = _registry.GetMergedFields(type.FullName);
                fieldsToInclude = mergedFields.Select(mf => mf.Field).ToList();
            }
            else
            {
                fieldsToInclude = type.ProtoMembers?.ToList() ?? new System.Collections.Generic.List<ProtoMemberAttribute>();
            }

            bool hasRegularFields = fieldsToInclude.Count > 0 || (type.CustomBufferMembers?.Count ?? 0) > 0;

            // Optimized range-separated dispatch:
            // - ProtoIncludes checked first (binary dispatch)
            // - Regular fields switch appears ONCE (not duplicated in every branch)
            binaryDispatch.GenerateRangeSeparatedDispatch(
                protoIncludeTree,
                generateProtoIncludeCase: (fieldId, typeName) =>
                {
                    GenerateProtoIncludeReadCaseBody(type, fieldId, typeName);
                },
                generateRegularFieldsSwitch: () =>
                {
                    // Generate switch for regular fields - appears ONCE
                    _sb.AppendIndentedLine("// Regular fields switch");
                    _sb.AppendIndentedLine("switch (fieldId)");
                    _sb.StartNewBlock();

                    if (hasFlatInheritance && (type.ProtoIncludes == null || type.ProtoIncludes.Count == 0))
                    {
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
                        // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                        if (type.ProtoMembers != null)
                        {
                            foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                            {
                                GenerateFieldReadCase(member);
                            }
                        }
                    }

                    // Custom buffer fields
                    if (type.CustomBufferMembers != null)
                    {
                        foreach (var customMember in type.CustomBufferMembers)
                        {
                            GenerateCustomBufferFieldReadCase(customMember, "result");
                        }
                    }

                    // Default - skip unknown fields
                    _sb.AppendIndentedLine("default:");
                    _sb.IncreaseIndent();
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    _sb.AppendIndentedLine("break;");
                    _sb.DecreaseIndent();

                    _sb.EndBlock();
                },
                generateSkipField: () =>
                {
                    // Unknown ProtoInclude field ID - skip it
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                }
            );
        }

        /// <summary>
        /// Generates traditional switch dispatch for ReadContent method.
        /// </summary>
        private void GenerateReadContentWithSwitchDispatch(TypeDefinition type, bool hasFlatInheritance)
        {
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

            // Check if this is flat inheritance (without ProtoInclude)
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
                // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                if (type.ProtoMembers != null)
                {
                    foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                    {
                        GenerateFieldReadCase(member);
                    }
                }
            }

            // Custom buffer fields (for all inheritance cases)
            if (type.CustomBufferMembers != null)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    GenerateCustomBufferFieldReadCase(customMember, "result");
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

        #endregion

        #region Populate Method

        /// <summary>
        /// Generates Populate{ClassName}(ref SpanReader reader, T instance) method.
        /// Fills existing instance without allocating a new one.
        /// </summary>
        private void GeneratePopulateMethod(TypeDefinition type)
        {

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
            _sb.AppendIndentedLine($"public static void Populate{className}(ref SpanReader reader, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
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

            // For custom collection types, populate by reading repeated elements
            if (type.IsCustomCollection && !string.IsNullOrEmpty(type.CustomCollectionElementType))
            {
                GenerateCustomCollectionPopulate(type, className);
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
                // Check if this is a ProtoInclude derived type (has parent with ProtoInclude)
                bool isProtoIncludeDerived = _registry.IsDerivedType(type.FullName);

                if (isProtoIncludeDerived)
                {
                    // For ProtoInclude derived types, need to handle:
                    // 1. Base class fields (e.g., field 1, 2, 3)
                    // 2. ProtoInclude wrapper field (e.g., field 100) containing derived fields
                    GeneratePopulateWithProtoInclude(type, className);
                }
                else
                {
                    // Flat inheritance or base type with ProtoIncludes - use simple populate
                    GenerateSimplePopulate(type, className);
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates Populate{ClassName}OwnFields method for derived types.
        /// Reads ONLY fields defined at this type level (not inherited from base)
        /// </summary>
        private void GenerateOwnFieldsPopulateMethod(TypeDefinition type, string className)
        {
            var ownMembers = _registry.GetOwnProtoMembers(type.FullName);

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Populates {className}'s OWN fields (not inherited from base).");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static void Populate{className}OwnFields(ref SpanReader reader, {GeneratorHelpers.GetPopulateInstanceParameter(type)})");
            _sb.StartNewBlock();

            if (ownMembers == null || ownMembers.Count == 0)
            {
                _sb.AppendIndentedLine("// No own fields (all inherited from base)");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var _);");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.EndBlock();
            }
            else
            {
                // Declare temp lists for array and IEnumerable fields
                var fieldsNeedingTempList = ownMembers
                    .Where(m => m.IsCollection && (
                        m.CollectionKind == CollectionKind.Array ||
                        (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                         TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                         !m.Type.Contains("ICollection") &&
                         !m.Type.Contains("IList"))
                    ))
                    .ToList();

                // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
                var fieldsUsingObjectBuilder = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();
                var fieldsUsingTempList = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.NeedsTempListDeclaration(m, _registry)).ToList();

                // Declare ObjectArrayBuilder for class element collections
                ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                    m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

                // Declare temp lists for struct/primitive element collections
                foreach (var member in fieldsUsingTempList)
                {
                    var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                    _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
                }

                if (fieldsNeedingTempList.Count > 0)
                    _sb.AppendNewLine();

                // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
                if (fieldsUsingObjectBuilder.Count > 0)
                {
                    _sb.AppendIndentedLine("try");
                    _sb.StartNewBlock();
                }

                _sb.AppendIndentedLine($"// Read ONLY own fields (not inherited) - {ownMembers.Count} field(s)");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
                _sb.AppendNewLine();
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                foreach (var member in ownMembers)
                {
                    GenerateFieldPopulateCase(member);
                }

                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock(); // switch
                _sb.EndBlock(); // while

                // Convert ObjectArrayBuilder fields to arrays/lists
                ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "instance");

                // Finalize List<T> fields
                ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "instance");

                if (fieldsUsingObjectBuilder.Count > 0)
                {
                    _sb.EndBlock();
                    _sb.AppendIndentedLine("finally");
                    _sb.StartNewBlock();
                    ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                    _sb.EndBlock();
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates Populate method for ProtoInclude derived types.
        /// Handles base class fields and ProtoInclude wrapper containing derived fields.
        /// </summary>
        private void GeneratePopulateWithProtoInclude(TypeDefinition type, string className)
        {
            // Get parent type info
            var parentTypeName = _registry.GetParent(type.FullName);
            var parentType = _registry.GetByFullName(parentTypeName);

            // Get ProtoInclude field id for this derived type
            var protoIncludeFieldId = _registry.GetProtoIncludeFieldId(type.FullName);

            if (parentType == null || protoIncludeFieldId == null)
            {
                // Fallback to simple populate if we can't find parent info
                GenerateSimplePopulate(type, className);
                return;
            }

            // Collect fields needing temp lists from both base and derived
            var allFields = new List<ProtoMemberAttribute>();
            if (parentType.ProtoMembers != null)
                allFields.AddRange(parentType.ProtoMembers);
            if (type.ProtoMembers != null)
                allFields.AddRange(type.ProtoMembers);

            var fieldsNeedingTempList = allFields
                .Where(m => m.IsCollection && (
                    m.CollectionKind == CollectionKind.Array ||
                    (m.CollectionKind == CollectionKind.InterfaceCollection && m.Type != null &&
                     TypeMapping.NormalizeTypeName(m.Type).StartsWith("System.Collections.Generic.IEnumerable<") &&
                     !m.Type.Contains("ICollection") &&
                     !m.Type.Contains("IList"))
                ))
                .ToList();

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList();
            var fieldsUsingTempList = fieldsNeedingTempList.Where(m => ObjectArrayBuilderHelper.NeedsTempListDeclaration(m, _registry)).ToList();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for struct/primitive element collections
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if (fieldsNeedingTempList.Count > 0)
                _sb.AppendNewLine();

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            // Generate cases for base class fields
            if (parentType.ProtoMembers != null && parentType.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine($"// Base class fields from {parentTypeName}");
                foreach (var member in parentType.ProtoMembers)
                {
                    GenerateFieldPopulateCase(member);
                }
            }

            // Generate case for ProtoInclude wrapper field containing derived class fields
            _sb.AppendIndentedLine($"// ProtoInclude wrapper field {protoIncludeFieldId} containing derived class fields");
            _sb.AppendIndentedLine($"case {protoIncludeFieldId}: {{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine("var protoIncludeLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var protoIncludeReader = new SpanReader(reader.GetSlice(protoIncludeLength));");
            _sb.AppendNewLine();

            // Read derived fields from nested reader
            _sb.AppendIndentedLine("while (!protoIncludeReader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("protoIncludeReader.ReadWireTypeAndFieldId(out var derivedWireType, out var derivedFieldId);");
            _sb.AppendNewLine();

            // Use sorted dispatch for optimal branch prediction (PGO heuristic)
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine("switch (derivedFieldId)");
                _sb.StartNewBlock();

                foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                {
                    GenerateFieldPopulateCaseWithReader(member, "protoIncludeReader", "derivedWireType");
                }

                // Default - skip unknown fields
                _sb.AppendIndentedLine("default:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine("protoIncludeReader.SkipField(derivedWireType);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();

                _sb.EndBlock(); // switch
            }
            else
            {
                _sb.AppendIndentedLine("protoIncludeReader.SkipField(derivedWireType);");
            }

            _sb.EndBlock(); // while protoIncludeReader

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");

            // Default - skip unknown fields
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // switch
            _sb.EndBlock(); // while reader

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "instance");

            // Finalize List<T> fields
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "instance");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates field populate case using a custom reader variable name.
        /// Used for nested reading in ProtoInclude scenarios.
        /// </summary>
        private void GenerateFieldPopulateCaseWithReader(ProtoMemberAttribute member, string readerVar, string wireTypeVar)
        {
            // Determine if we need braces for variable scoping
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry, _virtualTypesNamespace);
                mapHandler.GenerateRead(member, $"instance.{member.Name}", readerVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldPopulateBodyWithReader(member, readerVar, wireTypeVar);
            }
            else if (member.IsEnum)
            {
                _sb.AppendIndentedLine($"instance.{member.Name} = (global::{member.Type}){readerVar}.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"instance.{member.Name}", member.Type, readerVar);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"instance.{member.Name}", member.Type, member.DataFormat, readerVar, wireTypeVar, UseStringPooling);
            }
            else if (member.IsProtoVarint)
            {
                ProtoVarintTypeSupport.GenerateRead(_sb, member, $"instance.{member.Name}", readerVar);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
            else
            {
                // Complex type - nested message
                var typeName = TypeNameHelper.GetClassName(member.Type);
                _sb.AppendIndentedLine($"var complexLen_{member.FieldId} = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var complexReader_{member.FieldId} = new SpanReader({readerVar}.GetSlice(complexLen_{member.FieldId}));");

                var typeNamespace = _registry.GetNamespaceForType(member.Type);
                var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

                // Check if type is part of any inheritance hierarchy (either as base with ProtoInclude or as derived)
                // For hierarchy types, use Read{typeName} to handle ProtoInclude discriminator
                // For non-hierarchy types, use Populate for merge semantics (per protobuf spec)
                bool isPartOfHierarchy = _registry?.IsPartOfHierarchy(member.Type) ?? false;

                if (isPartOfHierarchy)
                {
                    // For types with inheritance, we need to read the discriminator to determine actual type
                    _sb.AppendIndentedLine($"instance.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}(ref complexReader_{member.FieldId});");
                }
                else
                {
                    // Per protobuf spec: When the same embedded message field appears multiple times,
                    // the contents should be MERGED (not overwritten).
                    // Strip nullable marker (?) when creating instance - can't instantiate nullable types directly
                    var instanceType = member.Type.TrimEnd('?');

                    // Check if the type is a struct (value type) - structs cannot be compared to null
                    var memberTypeInfo = _registry?.GetByFullName(instanceType);
                    bool isStruct = memberTypeInfo?.IsStruct ?? false;

                    if (!isStruct)
                    {
                        // Only generate null check for reference types (classes)
                        _sb.AppendIndentedLine($"if (instance.{member.Name} == null)");
                        _sb.StartNewBlock();
                        _sb.AppendIndentedLine($"instance.{member.Name} = new global::{instanceType}();");
                        _sb.EndBlock();
                        _sb.AppendIndentedLine($"{nsPrefix}SpanReaders.Populate{typeName}(ref complexReader_{member.FieldId}, instance.{member.Name});");
                    }
                    else
                    {
                        // Check if this is a readonly struct - use ReadContent instead of Populate
                        bool isReadonlyStruct = _registry?.IsReadonlyStruct(instanceType) ?? false;
                        if (isReadonlyStruct)
                        {
                            // Readonly struct - use ReadContent which uses constructor
                            _sb.AppendIndentedLine($"instance.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}Content(ref complexReader_{member.FieldId});");
                        }
                        else
                        {
                            // Mutable struct - use Populate pattern
                            _sb.AppendIndentedLine($"var _temp_{member.Name} = new global::{instanceType}();");
                            _sb.AppendIndentedLine($"{nsPrefix}SpanReaders.Populate{typeName}(ref complexReader_{member.FieldId}, ref _temp_{member.Name});");
                            _sb.AppendIndentedLine($"instance.{member.Name} = _temp_{member.Name};");
                        }
                    }
                }
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        /// <summary>
        /// Generates collection field populate body using a custom reader variable.
        /// </summary>
        private void GenerateCollectionFieldPopulateBodyWithReader(ProtoMemberAttribute member, string readerVar, string wireTypeVar)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
            {
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
                        wireTypeVar,
                        readerVar);
                }
                else
                {
                    var fieldIdVar = wireTypeVar.Replace("wireType", "fieldId").Replace("WireType", "FieldId");
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"instance.{member.Name}",
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
                _tupleHandler.GenerateTupleCollectionRead(
                    $"instance.{member.Name}",
                    member.CollectionElementType,
                    member.FieldId,
                    member.CollectionKind,
                    member.Type,
                    readerVar);
            }
            else
            {
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionRead(
                    $"instance.{member.Name}",
                    member.CollectionElementType,
                    elementClassName,
                    member.CollectionKind,
                    member.Type,
                    readerVar,
                    useObjectArrayBuilder: ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry));
            }
        }

        /// <summary>
        /// Generates Populate method for custom collection types.
        /// Reads repeated elements with implicit field id 1 and adds them to the collection.
        /// </summary>
        private void GenerateCustomCollectionPopulate(TypeDefinition type, string className)
        {
            var elementType = type.CustomCollectionElementType;
            var globalElementType = TypeMapping.GetGlobalTypeName(elementType);

            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Field id 1 is used for collection elements (protobuf-net convention)
            _sb.AppendIndentedLine("if (fieldId == 1)");
            _sb.StartNewBlock();

            // Check if element type is primitive, enum, or complex
            bool isSimpleType = TypeMapping.IsSimpleType(elementType);
            bool isEnum = _registry.IsEnum(elementType);

            if (isSimpleType)
            {
                var readExpr = TypeMapping.GetReadExpression(elementType, DataFormat.Default, "reader", "wireType");
                _sb.AppendIndentedLine($"instance.Add({readExpr});");
            }
            else if (isEnum)
            {
                _sb.AppendIndentedLine($"instance.Add(({globalElementType})reader.ReadVarInt32());");
            }
            else if (_registry.IsProtoVarint(TypeMapping.NormalizeTypeName(elementType)))
            {
                // ProtoVarint type - read varint and construct using the constructor
                var varintType2 = _registry.GetProtoVarintType(TypeMapping.NormalizeTypeName(elementType)) ?? Attributes.ProtoVarintType.UInt32;
                var valueMember2 = _registry.GetProtoVarintValueMember(TypeMapping.NormalizeTypeName(elementType));
                var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType2);
                _sb.AppendIndentedLine($"instance.Add(new {globalElementType}(reader.{readMethod}()));");
            }
            else
            {
                // Complex type - need to read length-prefixed message
                var elementClassName = TypeNameHelper.GetClassName(elementType);
                var elementNs = _registry.GetNamespaceForType(elementType);
                var qualifiedReadCall = string.IsNullOrEmpty(elementNs) || elementNs == _currentNamespace
                    ? $"Read{elementClassName}Content"
                    : $"global::{elementNs}.Serialization.SpanReaders.Read{elementClassName}Content";

                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
                _sb.AppendIndentedLine($"instance.Add({qualifiedReadCall}(ref nestedReader));");
            }

            _sb.EndBlock(); // if fieldId == 1
            _sb.AppendIndentedLine("else");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.EndBlock(); // else

            _sb.EndBlock(); // while
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

            // Separate fields into ObjectArrayBuilder (for classes) and List<T> (for structs/primitives)
            var fieldsUsingObjectBuilder = fieldsNeedingTempList?.Where(m => ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();
            var fieldsUsingTempList = fieldsNeedingTempList?.Where(m => ObjectArrayBuilderHelper.NeedsTempListDeclaration(m, _registry)).ToList() ?? new List<ProtoMemberAttribute>();

            // Declare ObjectArrayBuilder for class element collections
            ObjectArrayBuilderHelper.GenerateDeclarations(_sb, fieldsUsingObjectBuilder,
                m => TypeMapping.GetGlobalTypeName(m.CollectionElementType));

            // Declare temp lists for struct/primitive element collections
            foreach (var member in fieldsUsingTempList)
            {
                var elementType = TypeMapping.GetGlobalTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"global::System.Collections.Generic.List<{elementType}> _tempList_{member.Name} = null;");
            }

            if ((fieldsNeedingTempList?.Count ?? 0) > 0)
                _sb.AppendNewLine();

            // Wrap in try/finally for exception safety (ObjectArrayBuilder must be disposed)
            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.AppendIndentedLine("try");
                _sb.StartNewBlock();
            }

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            // Generate switch for fields
            bool hasProtoMembers = type.ProtoMembers != null && type.ProtoMembers.Count > 0;
            bool hasCustomBufferMembers = type.CustomBufferMembers != null && type.CustomBufferMembers.Count > 0;

            if (hasProtoMembers || hasCustomBufferMembers)
            {
                _sb.AppendIndentedLine("switch (fieldId)");
                _sb.StartNewBlock();

                // Use sorted dispatch for optimal branch prediction (PGO heuristic)
                if (hasProtoMembers)
                {
                    foreach (var member in GeneratorHelpers.GetSortedFieldsForDispatch(type.ProtoMembers))
                    {
                        GenerateFieldPopulateCase(member);
                    }
                }

                // Custom buffer fields
                if (hasCustomBufferMembers)
                {
                    foreach (var customMember in type.CustomBufferMembers)
                    {
                        GenerateCustomBufferFieldPopulateCase(customMember);
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

            // Convert ObjectArrayBuilder fields to arrays/lists
            ObjectArrayBuilderHelper.GenerateConversion(_sb, fieldsUsingObjectBuilder, "instance");

            // Finalize List<T> fields
            ObjectArrayBuilderHelper.GenerateTempListFinalization(_sb, fieldsUsingTempList, "instance");

            if (fieldsUsingObjectBuilder.Count > 0)
            {
                _sb.EndBlock();
                _sb.AppendIndentedLine("finally");
                _sb.StartNewBlock();
                ObjectArrayBuilderHelper.GenerateDispose(_sb, fieldsUsingObjectBuilder);
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates switch case for populating a single field.
        /// Uses 'instance' instead of 'result'.
        /// </summary>
        private void GenerateFieldPopulateCase(ProtoMemberAttribute member)
        {
            // Determine if we need braces for variable scoping
            bool needsBraces = member.IsMap || member.IsCollection ||
                              (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type));

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry, _virtualTypesNamespace);
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
                _primitiveHandler.GenerateRead(_sb, $"instance.{member.Name}", member.Type, member.DataFormat, "reader", "wireType", UseStringPooling);
            }
            else if (member.IsProtoVarint)
            {
                // ProtoVarint type - read varint and construct using the constructor
                ProtoVarintTypeSupport.GenerateRead(_sb, member, $"instance.{member.Name}");
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
                var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

                // Check if type is part of any inheritance hierarchy
                bool isPartOfHierarchy = _registry?.IsPartOfHierarchy(member.Type) ?? false;

                if (isPartOfHierarchy)
                {
                    // For types with inheritance, we need to read the discriminator to determine actual type
                    _sb.AppendIndentedLine($"instance.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}(ref nestedReader);");
                }
                else
                {
                    // Per protobuf spec: When the same embedded message field appears multiple times,
                    // the contents should be
                    var instanceType = member.Type.TrimEnd('?');

                    // Check if the type is a struct (value type) - structs cannot be compared to null
                    var memberTypeInfo = _registry?.GetByFullName(instanceType);
                    bool isStruct = memberTypeInfo?.IsStruct ?? false;

                    if (!isStruct)
                    {
                        // Only generate null check for reference types (classes)
                        _sb.AppendIndentedLine($"if (instance.{member.Name} == null)");
                        _sb.StartNewBlock();
                        _sb.AppendIndentedLine($"instance.{member.Name} = new global::{instanceType}();");
                        _sb.EndBlock();
                        _sb.AppendIndentedLine($"{nsPrefix}SpanReaders.Populate{typeName}(ref nestedReader, instance.{member.Name});");
                    }
                    else
                    {
                        // Check if this is a readonly struct - use ReadContent instead of Populate
                        bool isReadonlyStruct = _registry?.IsReadonlyStruct(instanceType) ?? false;
                        if (isReadonlyStruct)
                        {
                            // Readonly struct - use ReadContent which uses constructor
                            _sb.AppendIndentedLine($"instance.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}Content(ref nestedReader);");
                        }
                        else
                        {
                            // Mutable struct - use Populate pattern
                            _sb.AppendIndentedLine($"var _temp_{member.Name} = new global::{instanceType}();");
                            _sb.AppendIndentedLine($"{nsPrefix}SpanReaders.Populate{typeName}(ref nestedReader, ref _temp_{member.Name});");
                            _sb.AppendIndentedLine($"instance.{member.Name} = _temp_{member.Name};");
                        }
                    }
                }
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
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
                    member.Type,
                    useObjectArrayBuilder: ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry));
            }
        }

        #endregion

        #region Custom Buffer Field Generation

        /// <summary>
        /// Generates switch case for reading a custom buffer field.
        /// Custom buffer fields use user-defined methods for reading.
        /// </summary>
        private void GenerateCustomBufferFieldReadCase(CustomBufferMember member, string objectName)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}: {{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine($"// Custom buffer field {member.FieldId}");

            if (!string.IsNullOrEmpty(member.ReadMethodName))
            {
                // Read length-delimited data and call user's read method
                _sb.AppendIndentedLine("var customData = reader.ReadByteArraySpan();");
                _sb.AppendIndentedLine($"{objectName}.{member.ReadMethodName}(customData);");
            }
            else
            {
                // No read method - skip the field
                _sb.AppendIndentedLine("// WARNING: No ReadMethod defined, skipping field");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Generates switch case for populating a custom buffer field.
        /// Uses 'instance' instead of 'result'.
        /// </summary>
        private void GenerateCustomBufferFieldPopulateCase(CustomBufferMember member)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}: {{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine($"// Custom buffer field {member.FieldId}");

            if (!string.IsNullOrEmpty(member.ReadMethodName))
            {
                // Read length-delimited data and call user's read method
                _sb.AppendIndentedLine("var customData = reader.ReadByteArraySpan();");
                _sb.AppendIndentedLine($"instance.{member.ReadMethodName}(customData);");
            }
            else
            {
                // No read method - skip the field
                _sb.AppendIndentedLine("// WARNING: No ReadMethod defined, skipping field");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
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
            _sb.AppendIndentedLine($"case {member.FieldId}: {{");
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
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, "reader", "wireType", UseStringPooling);
            }
            else if (member.IsProtoVarint)
            {
                // ProtoVarint type - read varint and construct using the constructor
                ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}");
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
        }

        /// <summary>
        /// Generates switch case for reading a single field based on its type.
        /// </summary>
        private void GenerateFieldReadCase(ProtoMemberAttribute member)
        {
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            // Determine if we need braces for variable scoping
            bool needsBraces = category == FieldCategory.Map ||
                              category == FieldCategory.Collection ||
                              category == FieldCategory.ComplexType ||
                              category == FieldCategory.Tuple ||
                              category == FieldCategory.ProtoVarint;

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            // Phase 1: Wire type validation (Level200 requirement)
            GenerateWireTypeValidation(member);

            // Route to appropriate handler based on field category
            switch (category)
            {
                case FieldCategory.Map:
                    GenerateMapFieldReadBody(member);
                    break;
                case FieldCategory.Collection:
                    GenerateCollectionFieldReadBody(member);
                    break;
                case FieldCategory.Enum:
                    GenerateEnumFieldReadBody(member);
                    break;
                case FieldCategory.Tuple:
                    _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type);
                    break;
                case FieldCategory.Primitive:
                    _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, "reader", "wireType", UseStringPooling);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}");
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                    _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be deserialized)");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
                case FieldCategory.ComplexType:
                    GenerateComplexTypeReadBody(member);
                    break;
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GenerateProtoIncludeReadCase(TypeDefinition parentType, ProtoIncludeAttribute include)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);

            _sb.AppendIndentedLine($"case {include.FieldId}: {{");
            _sb.IncreaseIndent();

            // ProtoInclude always expects WireType.Len
            // If wrong wire type, ReadVarInt32() will throw exception

            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

            // Read derived content (derived fields from nested reader)
            // Note: ProtoInclude must be the first tag, so no base fields can have been read before this
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");

            // Continue to read any remaining fields after the ProtoInclude wrapper
            _sb.AppendIndentedLine("continue;");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        // Body versions for switch case (without continue/break - those are added by GenerateFieldReadCase)
        private void GenerateEnumFieldReadBody(ProtoMemberAttribute member)
        {
            // Use fully qualified type name for enums to avoid namespace issues
            _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SpanReaders", _registry, _virtualTypesNamespace);
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
                    member.Type,
                    useObjectArrayBuilder: ObjectArrayBuilderHelper.ShouldUseObjectArrayBuilder(member, _registry));
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

            // Get nested derived type info (parent type, ProtoInclude, wrapper tag)
            var derivedInfo = GeneratorHelpers.TryGetNestedDerivedTypeInfo(member.Type, _registry);
            if (derivedInfo == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type}");
                GenerateStandardComplexTypeReadBody(member, typeName);
                return;
            }

            var wrapperTag = derivedInfo.WrapperTag;
            var parentTypeName = derivedInfo.ParentTypeName;
            var protoInclude = derivedInfo.ProtoInclude;
            var parentType = derivedInfo.ParentType;

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

            string derivedReaderPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace) + "SpanReaders.";

            _sb.AppendIndentedLine($"// Read derived object from wrapper (derived fields only)");
            _sb.AppendIndentedLine($"result.{member.Name} = new global::{member.Type}();");
            _sb.AppendIndentedLine($"{derivedReaderPrefix}Populate{typeName}OwnFields(ref wrapperReader, result.{member.Name});");
            _sb.AppendNewLine();

            // Read remaining base fields (AFTER wrapper) directly into the derived instance
            _sb.AppendIndentedLine($"// Read base fields (AFTER wrapper) - populate directly on derived instance");
            string parentReaderPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace) + "SpanReaders.";
            _sb.AppendIndentedLine($"{parentReaderPrefix}Populate{parentTypeName}(ref nestedReader, result.{member.Name});");

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
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"result.{member.Name} = {nsPrefix}SpanReaders.Read{typeName}Content(ref nestedReader);");
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
