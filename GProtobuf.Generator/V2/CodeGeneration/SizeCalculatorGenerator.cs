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
    /// Generates SizeCalculators class with Calculate{ClassName}Size and Calculate{ClassName}ContentSize methods.
    /// Handles size calculation for serialization.
    /// </summary>
    internal class SizeCalculatorGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly TypeRegistry _registry;
        private readonly PrimitiveHandler _primitiveHandler;
        private readonly CollectionHandler _collectionHandler;
        private readonly TupleHandler _tupleHandler;
        private readonly VirtualMapTypeRegistry _virtualMapRegistry;
        private readonly VirtualTupleTypeRegistry _virtualTupleRegistry;
        private string _currentNamespace;
        private int _nestedCalcCounter;

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null, null)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : this(sb, registry, virtualMapRegistry, null)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
            _collectionHandler = new CollectionHandler(sb, registry);
            _virtualTupleRegistry = virtualTupleRegistry ?? new VirtualTupleTypeRegistry();
            _virtualMapRegistry = virtualMapRegistry ?? new VirtualMapTypeRegistry(_virtualTupleRegistry, _registry);
            _tupleHandler = new TupleHandler(sb, _virtualTupleRegistry);
        }

        /// <summary>
        /// Generates complete SizeCalculators class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            // Store current namespace for cross-namespace method calls
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine("public static class SizeCalculators");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateCalculateSizeMethod(type);
                GenerateCalculateContentSizeMethod(type);

                // Generate BaseFieldsOnly method for base types with ProtoIncludes
                // This is needed for nested derived type serialization to avoid runtime dispatch
                if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GenerateCalculateBaseFieldsOnlySizeMethod(type, className);
                }

                // Generate OwnFieldsSize method for derived types (used in ProtoInclude wrapper calculation)
                if (_registry.IsDerivedType(type.FullName))
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GenerateOwnFieldsSizeMethod(type, className);
                }
            }

            // Generate ContentSize and OwnFieldsSize methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = new HashSet<string>();

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

            // Generate ContentSize and OwnFieldsSize methods for ProtoInclude types
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    GenerateCalculateContentSizeMethod(protoIncludeType);

                    // Also generate OwnFieldsSize if it's a derived type
                    if (_registry.IsDerivedType(protoIncludeTypeName))
                    {
                        var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                        GenerateOwnFieldsSizeMethod(protoIncludeType, className);
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            // Generate virtual map entry size calculators
            GenerateVirtualMapEntrySizeCalculators();

            // Generate virtual tuple size calculators
            GenerateVirtualTupleSizeCalculators();

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates size calculator methods for all registered virtual map entry types.
        /// </summary>
        private void GenerateVirtualMapEntrySizeCalculators()
        {
            var virtualTypes = _virtualMapRegistry.GetAllTypes();
            if (virtualTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Size Calculators");

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateSizeCalculator(virtualType);
            }

            // Generate KeyValue methods that use MapEntry methods
            var keyValueGenerator = new KeyValueClassGenerator(_sb, _virtualMapRegistry);
            keyValueGenerator.GenerateKeyValueMethods("SizeCalculators");
        }

        /// <summary>
        /// Generates size calculator methods for all registered virtual tuple types.
        /// </summary>
        private void GenerateVirtualTupleSizeCalculators()
        {
            var tupleTypes = _virtualTupleRegistry.GetAllTypes();
            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Size Calculators");

            var generator = new VirtualTupleGenerator(_sb, null, _registry);
            foreach (var tupleInfo in tupleTypes)
            {
                generator.GenerateSizeCalculator(tupleInfo);
            }
        }

        #region CalculateSize Method

        /// <summary>
        /// Generates Calculate{ClassName}Size method.
        /// This is the main entry point that handles inheritance wrappers.
        /// </summary>
        private void GenerateCalculateSizeMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static void Calculate{className}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            bool isDerived = _registry.IsDerivedType(type.FullName);

            if (isDerived)
            {
                GenerateCalculateSizeForDerived(type, className);
            }
            else if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                GenerateCalculateSizeWithInheritance(type, className);
            }
            else
            {
                GenerateSimpleCalculateSize(type, className);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimpleCalculateSize(TypeDefinition type, string className)
        {
            // Calculate each field
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            // Calculate custom buffer fields
            if (type.CustomBufferMembers != null)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    GenerateCustomBufferFieldSize(customMember, "obj");
                }
            }
        }

        private void GenerateCalculateSizeWithInheritance(TypeDefinition type, string className)
        {
            // Wire format requires parent fields come before derived type wrappers
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            // Calculate custom buffer fields
            if (type.CustomBufferMembers != null)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    GenerateCustomBufferFieldSize(customMember, "obj");
                }
            }

            // Handle ProtoIncludes with switch on derived types
            _sb.AppendIndentedLine("switch (obj)");
            _sb.StartNewBlock();

            int caseIndex = 0;
            if (type.ProtoIncludes != null)
            {
                foreach (var include in type.ProtoIncludes)
            {
                var derivedClassName = TypeNameHelper.GetClassName(include.Type);
                _sb.AppendIndentedLine($"case global::{include.Type} derived:");
                _sb.IncreaseIndent();

                // Add tag size for ProtoInclude
                TagCodeHelper.AddTagSize(_sb, include.FieldId, WireType.Len);

                // Calculate content size in separate calculator with unique name
                var tempCalcVar = $"tempCalc{caseIndex}";
                _sb.AppendIndentedLine($"var {tempCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"Calculate{derivedClassName}ContentSize(ref {tempCalcVar}, derived);");
                _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){tempCalcVar}.Length);");
                _sb.AppendIndentedLine($"calculator.AddByteLength({tempCalcVar}.Length);");

                _sb.AppendIndentedLine("return;");  // CRITICAL FIX: return instead of break to exit method
                _sb.DecreaseIndent();
                caseIndex++;
                }
            }

            _sb.EndBlock();
        }

        private void GenerateCalculateSizeForDerived(TypeDefinition type, string className)
        {
            // Phase 2: Correct ProtoInclude wrapper generation
            // Wire format: [wrapper tag][wrapper length][derived fields INSIDE][base fields AFTER]

            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count < 2)
            {
                // No inheritance - shouldn't happen for derived types, but handle gracefully
                GenerateCalculateContentSizeForDerivedType(type, className);
                return;
            }

            // Reset nested calculator counter for this method
            _nestedCalcCounter = 0;

            // Get root (base) type
            var rootTypeName = inheritanceChain[0];
            var rootType = _registry.GetByFullName(rootTypeName);

            _sb.AppendIndentedLine($"// ProtoInclude wrapper format (Level200 compatibility)");
            _sb.AppendIndentedLine($"// Wire: [wrapper tag][length][derived fields INSIDE wrapper][base fields AFTER wrapper]");
            _sb.AppendNewLine();

            // Step 1: Calculate and add outermost wrapper size
            GenerateOutermostWrapperSizeCalculation(inheritanceChain, type);

            // Step 2: Calculate and add base fields size AFTER all wrappers
            if (rootType?.ProtoMembers != null)
            {
                _sb.AppendIndentedLine($"// Base class fields ({TypeNameHelper.GetClassName(rootTypeName)}) - AFTER wrapper");
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }
        }

        // Old ProtoInclude wrapper size methods removed - will be rewritten in Phase 2
        // See PROTOINCLUDE_CLEAN_REWRITE_PLAN.md for new implementation

        #endregion

        #region CalculateContentSize Method

        /// <summary>
        /// Generates Calculate{ClassName}ContentSize method.
        /// For types with ProtoInclude hierarchy, this includes type dispatch and wrapper calculation.
        /// </summary>
        private void GenerateCalculateContentSizeMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static void Calculate{className}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            // For enum types, calculate VarInt32 size
            if (type.IsEnum)
            {
                _sb.AppendIndentedLine("calculator.WriteVarInt32((int)obj);");
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            // Check if type has ProtoInclude hierarchy
            bool isDerived = _registry.IsDerivedType(type.FullName);
            bool hasProtoIncludes = type.ProtoIncludes != null && type.ProtoIncludes.Count > 0;

            if (!isDerived && hasProtoIncludes)
            {
                // Base type with derived types - add type dispatch
                GenerateCalculateContentSizeWithTypeDispatch(type, className);
            }
            else if (isDerived)
            {
                // Derived type - include ProtoInclude wrappers in content
                GenerateCalculateContentSizeForDerivedType(type, className);
            }
            else
            {
                // Simple type - just own fields
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldSize(member, "obj");
                    }
                }

                // Calculate custom buffer fields
                if (type.CustomBufferMembers != null)
                {
                    foreach (var customMember in type.CustomBufferMembers)
                    {
                        GenerateCustomBufferFieldSize(customMember, "obj");
                    }
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates Calculate{ClassName}BaseFieldsOnlySize method that calculates ONLY base class fields without runtime dispatch.
        /// This is used when calculating size for nested derived types to avoid counting duplicate fields.
        /// Example: CalculateModbusTransactionBaseFieldsOnlySize calculates only ModbusTransaction fields,
        /// ignoring the fact that obj might be ModbusManualTransaction.
        /// </summary>
        private void GenerateCalculateBaseFieldsOnlySizeMethod(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Calculates ONLY base {className} fields size without runtime type dispatch.");
            _sb.AppendIndentedLine($"/// Used for nested derived type size calculation after ProtoInclude wrapper.");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static void Calculate{className}BaseFieldsOnlySize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            // Calculate ONLY the fields defined in this base class, no switch/dispatch
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            // Calculate custom buffer fields defined in this base class
            if (type.CustomBufferMembers != null && type.CustomBufferMembers.Count > 0)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    GenerateCustomBufferFieldSize(customMember, "obj");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates ContentSize method with type dispatch for base types with ProtoInclude.
        /// </summary>
        private void GenerateCalculateContentSizeWithTypeDispatch(TypeDefinition type, string className)
        {
            var allDerivedTypes = _registry.GetAllDerivedTypes(type.FullName);
            if (allDerivedTypes.Count == 0)
            {
                // No derived types - just calculate own fields
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldSize(member, "obj");
                    }
                }
                return;
            }

            // Generate switch with type dispatch (most derived first)
            var sortedDerived = allDerivedTypes
                .OrderByDescending(d =>
                {
                    var chain = _registry.GetInheritanceChain(d);
                    return chain?.Count ?? 0;
                })
                .ToList();

            _sb.AppendIndentedLine("switch (obj)");
            _sb.StartNewBlock();

            foreach (var derivedType in sortedDerived)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"case global::{derivedType} derived:");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"Calculate{derivedClassName}ContentSize(ref calculator, derived);");
                _sb.AppendIndentedLine("return;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();

            // Default case - base type fields
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }
        }

        /// <summary>
        /// Generates outermost ProtoInclude wrapper size calculation.
        /// For inheritance chain [Base, Derived1, Derived2, ...], generates wrapper from Base to Derived1.
        /// </summary>
        private void GenerateOutermostWrapperSizeCalculation(IReadOnlyList<string> chain, TypeDefinition type)
        {
            if (chain.Count < 2) return;

            var baseTypeName = chain[0];
            var firstDerivedTypeName = chain[1];
            var baseType = _registry.GetByFullName(baseTypeName);
            var protoInclude = FindProtoInclude(baseType, firstDerivedTypeName);

            if (protoInclude == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {firstDerivedTypeName} in {baseTypeName}");
                return;
            }

            _sb.AppendIndentedLine($"// ProtoInclude wrapper for {TypeNameHelper.GetClassName(firstDerivedTypeName)}");

            // Add wrapper tag size
            TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len);

            // Calculate wrapper content size
            var contentCalcVar = "wrapperContentCalc";
            _sb.AppendIndentedLine($"var {contentCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Calculate wrapper content (derived fields + nested wrappers if any)
            CalculateWrapperContentSizeRecursive(chain, 1, contentCalcVar);

            // Add length prefix size
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){contentCalcVar}.Length);");

            // Add wrapper content size
            _sb.AppendIndentedLine($"calculator.AddByteLength({contentCalcVar}.Length);");
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Recursively calculates wrapper content size for the given level and all nested levels.
        /// </summary>
        private void CalculateWrapperContentSizeRecursive(IReadOnlyList<string> chain, int levelIndex, string calcVar)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // Add this level's OWN fields size
            _sb.AppendIndentedLine($"// Add {currentClassName}'s own fields size");
            _sb.AppendIndentedLine($"Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, obj);");

            // If there's a next level, add nested wrapper size
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Add nested wrapper for {nextClassName}");

                    // Add nested wrapper tag size
                    TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content size - use unique counter instead of levelIndex
                    var nestedCalcVar = $"nestedCalc{_nestedCalcCounter++}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursive call for nested level
                    CalculateWrapperContentSizeRecursive(chain, levelIndex + 1, nestedCalcVar);

                    // Add length prefix size and content size to parent calculator
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }
        }

        /// <summary>
        /// Finds ProtoInclude attribute for a specific derived type in a base type.
        /// </summary>
        private ProtoIncludeAttribute FindProtoInclude(TypeDefinition baseType, string derivedTypeName)
        {
            if (baseType?.ProtoIncludes == null) return null;

            foreach (var include in baseType.ProtoIncludes)
            {
                if (include.Type == derivedTypeName)
                    return include;
            }

            return null;
        }

        /// <summary>
        /// Generates Calculate{ClassName}OwnFieldsSize method for derived types.
        /// Calculates size of ONLY fields defined at this type level (not inherited from base).
        /// Used for ProtoInclude wrapper size calculation.
        /// </summary>
        private void GenerateOwnFieldsSizeMethod(TypeDefinition type, string className)
        {
            var ownMembers = _registry.GetOwnProtoMembers(type.FullName);

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Calculates size of {className}'s OWN fields (not inherited from base).");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static void Calculate{className}OwnFieldsSize(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref global::GProtobuf.Core.WriteSizeCalculator calculator,");
            _sb.AppendIndentedLine($"global::{type.FullName} obj)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            if (ownMembers.Count == 0)
            {
                _sb.AppendIndentedLine("// No own fields (all inherited from base)");
            }
            else
            {
                _sb.AppendIndentedLine($"// Calculate ONLY own fields (not inherited) - {ownMembers.Count} field(s)");
                foreach (var member in ownMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates ContentSize calculation for derived types.
        /// ContentSize calculates ONLY the fields, without ProtoInclude wrapper.
        /// The wrapper is added by Write{Type}_AsParent methods.
        /// </summary>
        private void GenerateCalculateContentSizeForDerivedType(TypeDefinition type, string className)
        {
            // Calculate root type fields (base class fields)
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootType = _registry.GetByFullName(rootTypeName);
            if (rootType?.ProtoMembers != null)
            {
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            // Calculate own fields (if not root)
            if (type.FullName != rootTypeName && type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }
        }

        #endregion

        #region Custom Buffer Field Generation

        /// <summary>
        /// Generates code to calculate size of a custom buffer field.
        /// Custom buffer fields use user-defined methods for size calculation.
        /// </summary>
        private void GenerateCustomBufferFieldSize(CustomBufferMember member, string objectName)
        {
            _sb.AppendIndentedLine($"// Custom buffer field {member.FieldId}");

            // Add tag size (field ID + WireType.Len)
            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            // Call user's size method
            _sb.AppendIndentedLine($"var customSize_{member.FieldId} = {objectName}.{member.SizeMethodName}();");

            // Add length prefix size (VarInt encoding of the size)
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)customSize_{member.FieldId});");

            // Add the actual content size
            _sb.AppendIndentedLine($"calculator.AddByteLength(customSize_{member.FieldId});");
        }

        #endregion

        #region Field Generation

        /// <summary>
        /// Generates code to calculate size of a single field.
        /// </summary>
        private void GenerateFieldSize(ProtoMemberAttribute member, string objectName)
        {
            string sourceVar = $"{objectName}.{member.Name}";

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                GenerateMapFieldSize(member, sourceVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldSize(member, sourceVar);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldSize(member, sourceVar);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleSize(member.FieldId, sourceVar, member.Type);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateSize(
                    _sb,
                    sourceVar,
                    member.Type,
                    member.DataFormat,
                    member.FieldId,
                    member.IsNullable,
                    member.IsRequired);
            }
            else if (TypeMapping.IsUnsupportedType(member.Type))
            {
                // Unsupported type (e.g., System.Type) - skip with warning comment
                _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be serialized)");
            }
            else
            {
                // Complex type - nested message
                GenerateComplexTypeSize(member, sourceVar);
            }
        }

        private void GenerateEnumFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            if (member.IsNullable)
            {
                // Nullable enum: always use HasValue check (IsRequired ignored)
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Non-nullable enum + IsRequired: ALWAYS serialize (no condition)
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar});");
            }
            else
            {
                // Non-nullable enum + NOT required: proto2 default value check
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar});");
                _sb.EndBlock();
            }
        }

        private void GenerateMapFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SizeCalculators", _registry);
            mapHandler.GenerateSize(member, sourceVar);
        }

        private void GenerateCollectionFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                // Level200: Primitives MUST use packed encoding by default
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

                if (shouldBePacked)
                {
                    _primitiveHandler.GeneratePackedArraySize(
                        _sb,
                        sourceVar,
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId);
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArraySize(
                        _sb,
                        sourceVar,
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionSize(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType);
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionSize(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType,
                    elementClassName);
            }
        }

        private void GenerateComplexTypeSize(ProtoMemberAttribute member, string sourceVar)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);

            // Check if type is a non-nullable value type (struct)
            var typeDef = _registry.GetByFullName(member.Type);
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;

            // Check if field is a concrete nested derived type (requires ProtoInclude wrapper)
            bool isNestedDerivedType = _registry.IsConcreteNestedDerivedType(member);

            // Check if field is polymorphic (base type with ProtoIncludes)
            bool isPolymorphicField = _registry.IsPolymorphicField(member);

            // Only generate null check for reference types or nullable value types
            if (!isNonNullableStruct)
            {
                _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                _sb.StartNewBlock();
            }

            if (isNestedDerivedType)
            {
                // CRITICAL: Field declared as concrete derived type - calculate ProtoInclude wrapper size
                GenerateNestedDerivedTypeSize(member, sourceVar, typeName, typeDef);
            }
            else if (isPolymorphicField)
            {
                // CRITICAL: Field declared as polymorphic base type (e.g., ProtoParameterBase ProtoValue)
                // Runtime instance may be derived type, requiring ProtoInclude wrapper.
                // Generate runtime type dispatch to calculate wrapper size.
                GeneratePolymorphicFieldSize(member, sourceVar, typeName, typeDef);
            }
            else
            {
                // Standard complex type size calculation
                GenerateStandardComplexTypeSize(member, sourceVar, typeName, typeDef);
            }

            if (!isNonNullableStruct)
            {
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates size calculation for nested derived type fields (with ProtoInclude wrapper).
        /// Wire format: [field tag][total length] [wrapper tag][wrapper length] [derived fields] [base fields]
        /// </summary>
        private void GenerateNestedDerivedTypeSize(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);
            var typeNamespace = _registry.GetNamespaceForType(member.Type);

            // Get parent type and ProtoInclude field ID
            var parentTypeFullName = _registry.GetParent(member.Type);
            var parentType = _registry.GetByFullName(parentTypeFullName);
            var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == member.Type);

            if (protoInclude == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type} in {parentTypeFullName}");
                GenerateStandardComplexTypeSize(member, sourceVar, typeName, typeDef);
                return;
            }

            var parentTypeName = TypeNameHelper.GetClassName(parentTypeFullName);
            var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;

            _sb.AppendIndentedLine($"// ProtoInclude wrapper for nested derived type {typeName}");

            // Add field tag size
            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            // Calculate total content size in separate calculator
            var lengthVar = isNonNullableStruct ? $"lengthBefore_{member.FieldId}" : "lengthBefore";
            var contentLengthVar = isNonNullableStruct ? $"contentLength_{member.FieldId}" : "contentLength";
            var nestedCalcVar = isNonNullableStruct ? $"nestedCalc_{member.FieldId}" : "nestedCalc";

            _sb.AppendIndentedLine($"var {lengthVar} = calculator.Length;");
            _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendNewLine();

            // Add wrapper tag size
            _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
            _sb.AppendIndentedLine($"{nestedCalcVar}.WriteVarUInt32({wrapperTag}u);");

            // Calculate wrapper content size (derived-specific fields)
            var wrapperContentCalcVar = isNonNullableStruct ? $"wrapperContent_{member.FieldId}" : "wrapperContentCalc";
            _sb.AppendIndentedLine($"var {wrapperContentCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            string sizeCalcPrefix = !string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace
                ? $"global::{typeNamespace}.Serialization."
                : "";

            _sb.AppendIndentedLine($"{sizeCalcPrefix}SizeCalculators.Calculate{typeName}OwnFieldsSize(ref {wrapperContentCalcVar}, {valueArg});");
            _sb.AppendIndentedLine($"{nestedCalcVar}.WriteVarUInt32((uint){wrapperContentCalcVar}.Length);");
            _sb.AppendIndentedLine($"{nestedCalcVar}.AddByteLength({wrapperContentCalcVar}.Length);");
            _sb.AppendNewLine();

            // Add base fields size (AFTER wrapper)
            // IMPORTANT: Use BaseFieldsOnlySize to avoid runtime dispatch that would count derived fields twice
            _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
            string parentNamespace = _registry.GetNamespaceForType(parentTypeFullName);
            string parentSizeCalcPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                ? $"global::{parentNamespace}.Serialization."
                : "";
            _sb.AppendIndentedLine($"{parentSizeCalcPrefix}SizeCalculators.Calculate{parentTypeName}BaseFieldsOnlySize(ref {nestedCalcVar}, {valueArg});");
            _sb.AppendNewLine();

            // Add nested content size and length prefix to main calculator
            _sb.AppendIndentedLine($"var {contentLengthVar} = {nestedCalcVar}.Length;");
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){contentLengthVar});");
            _sb.AppendIndentedLine($"calculator.AddByteLength({contentLengthVar});");
        }

        /// <summary>
        /// Generates standard size calculation for complex types (no nested derived type special handling).
        /// </summary>
        private void GenerateStandardComplexTypeSize(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;
            var lengthVar = isNonNullableStruct ? $"lengthBefore_{member.FieldId}" : "lengthBefore";
            var contentLengthVar = isNonNullableStruct ? $"contentLength_{member.FieldId}" : "contentLength";
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);

            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            _sb.AppendIndentedLine($"var {lengthVar} = calculator.Length;");

            // NOTE: For top-level serialization, ProtoInclude wrapper is included in ContentSize.
            // For nested fields of non-derived types, no wrapper is needed.

            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {valueArg});");
            }
            else
            {
                _sb.AppendIndentedLine($"Calculate{typeName}ContentSize(ref calculator, {valueArg});");
            }

            _sb.AppendIndentedLine($"var {contentLengthVar} = calculator.Length - {lengthVar};");
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){contentLengthVar});");
        }

        /// <summary>
        /// Generates size calculation for polymorphic fields (field declared as base type with ProtoIncludes).
        /// Example: [ProtoMember(2)] ProtoParameterBase ProtoValue (where ProtoParameterBase has multiple ProtoIncludes)
        /// Generates runtime type dispatch to calculate ProtoInclude wrapper size for derived type instances.
        /// </summary>
        private void GeneratePolymorphicFieldSize(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            var allDerivedTypes = _registry.GetAllDerivedTypes(member.Type);
            if (allDerivedTypes == null || allDerivedTypes.Count == 0)
            {
                // No derived types - fallback to standard calculation
                GenerateStandardComplexTypeSize(member, sourceVar, typeName, typeDef);
                return;
            }

            _sb.AppendIndentedLine($"// Polymorphic field: runtime type dispatch for ProtoInclude wrapper size");

            // Add field tag size
            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            // Generate switch with type dispatch (most derived first)
            var sortedDerived = allDerivedTypes
                .OrderByDescending(d =>
                {
                    var chain = _registry.GetInheritanceChain(d);
                    return chain?.Count ?? 0;
                })
                .ToList();

            _sb.AppendIndentedLine($"switch ({sourceVar})");
            _sb.StartNewBlock();

            foreach (var derivedType in sortedDerived)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                var derivedTypeDef = _registry.GetByFullName(derivedType);
                var derivedNamespace = _registry.GetNamespaceForType(derivedType);

                // Get ProtoInclude information
                var parentTypeFullName = _registry.GetParent(derivedType);
                var parentType = _registry.GetByFullName(parentTypeFullName);
                var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == derivedType);

                if (protoInclude == null)
                {
                    _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {derivedType}");
                    continue;
                }

                var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;
                var parentTypeName = TypeNameHelper.GetClassName(parentTypeFullName);

                _sb.AppendIndentedLine($"case global::{derivedType} derived{derivedClassName}:");
                _sb.IncreaseIndent();
                _sb.StartNewBlock();

                // Calculate total size (wrapper + derived fields + base fields)
                var totalCalcVar = $"totalCalc{derivedClassName}";
                _sb.AppendIndentedLine($"var {totalCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendNewLine();

                // Wrapper tag size
                _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
                _sb.AppendIndentedLine($"{totalCalcVar}.WriteVarUInt32({wrapperTag}u);");

                // Wrapper content size (derived fields only)
                var wrapperCalcVar = $"wrapperCalc{derivedClassName}";
                _sb.AppendIndentedLine($"var {wrapperCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                string derivedSizeCalcPrefix = !string.IsNullOrEmpty(derivedNamespace) && derivedNamespace != _currentNamespace
                    ? $"global::{derivedNamespace}.Serialization."
                    : "";

                _sb.AppendIndentedLine($"{derivedSizeCalcPrefix}SizeCalculators.Calculate{derivedClassName}OwnFieldsSize(ref {wrapperCalcVar}, derived{derivedClassName});");
                _sb.AppendIndentedLine($"{totalCalcVar}.WriteVarUInt32((uint){wrapperCalcVar}.Length);");
                _sb.AppendIndentedLine($"{totalCalcVar}.AddByteLength({wrapperCalcVar}.Length);");
                _sb.AppendNewLine();

                // Base fields size (AFTER wrapper)
                _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
                string parentNamespace = _registry.GetNamespaceForType(parentTypeFullName);
                string parentSizeCalcPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                    ? $"global::{parentNamespace}.Serialization."
                    : "";
                _sb.AppendIndentedLine($"{parentSizeCalcPrefix}SizeCalculators.Calculate{parentTypeName}BaseFieldsOnlySize(ref {totalCalcVar}, derived{derivedClassName});");
                _sb.AppendNewLine();

                // Add to main calculator
                _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){totalCalcVar}.Length);");
                _sb.AppendIndentedLine($"calculator.AddByteLength({totalCalcVar}.Length);");

                _sb.EndBlock();
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();
            }

            // Default case - base type instance (always generate, even if no ProtoMembers)
            // This handles cases where base class is non-abstract and can be instantiated,
            // even without explicit ProtoMember fields (e.g., LongRunningTaskParameters)
            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"// Base type instance: {typeName}");

            // Calculate size if base type has own fields, otherwise calculate empty message size
            if (typeDef != null && typeDef.ProtoMembers != null && typeDef.ProtoMembers.Count > 0)
            {
                // Calculate size using standard method (no wrapper for base type instance)
                var lengthVar = "lengthBefore";
                var contentLengthVar = "contentLength";
                var typeNamespace = _registry.GetNamespaceForType(member.Type);

                _sb.AppendIndentedLine($"var {lengthVar} = calculator.Length;");

                if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
                {
                    _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
                }
                else
                {
                    _sb.AppendIndentedLine($"Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
                }

                _sb.AppendIndentedLine($"var {contentLengthVar} = calculator.Length - {lengthVar};");
                _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){contentLengthVar});");
            }
            else
            {
                // Base type has no fields - empty message (length = 0)
                _sb.AppendIndentedLine("calculator.WriteVarUInt32(0u); // Empty base type message");
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // End switch
        }

        /// <summary>
        /// Gets the correct value access for nullable types.
        /// For nullable enums or structs, returns sourceVar.Value, otherwise returns sourceVar as-is.
        /// </summary>
        private string GetNullableValueAccess(string sourceVar, ProtoMemberAttribute member, TypeDefinition typeDef)
        {
            // If member.IsNullable = true in context of GenerateComplexTypeSize,
            // it means it's a nullable value type (struct or enum), not a reference type.
            // Reference types don't have nullable modifier in protobuf context.
            if (member.IsNullable)
            {
                // Check if we can confirm it's a value type
                bool isEnum = _registry != null && _registry.IsEnum(member.Type);
                bool isStruct = typeDef != null && typeDef.IsStruct;

                // If we know it's enum or struct, use .Value
                if (isEnum || isStruct)
                {
                    return $"{sourceVar}.Value";
                }

                // Fallback: if typeDef is null but member.IsNullable = true in complex type context,
                // assume it's a nullable struct and use .Value
                // This handles cases where typeDef is null for registered structs like DataType
                if (typeDef == null && !isEnum)
                {
                    return $"{sourceVar}.Value";
                }
            }

            return sourceVar;
        }

        /// <summary>
        /// Generates code to add ProtoInclude wrapper size for derived types.
        /// protobuf-net Level200 always adds wrapper for derived types, even in direct fields.
        /// </summary>
        private void GenerateProtoIncludeWrapperSize(string derivedTypeName)
        {
            var inheritanceChain = _registry.GetInheritanceChain(derivedTypeName);
            if (inheritanceChain.Count < 2) return;

            var parentTypeName = inheritanceChain[0];
            var parentType = _registry.GetByFullName(parentTypeName);
            var protoInclude = GeneratorHelpers.FindProtoInclude(parentType, derivedTypeName);

            if (protoInclude != null)
            {
                var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;
                _sb.AppendIndentedLine($"// ProtoInclude wrapper for {TypeNameHelper.GetClassName(derivedTypeName)}");
                _sb.AppendIndentedLine($"calculator.WriteVarUInt32({wrapperTag}u);");
                _sb.AppendIndentedLine("calculator.WriteVarUInt32(0); // Empty wrapper marker");
            }
        }

        #endregion

    }
}
