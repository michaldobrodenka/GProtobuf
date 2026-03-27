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
    internal class SizeCalculatorGenerator : GeneratorBase
    {
        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string virtualTypesNamespace)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options: null, virtualTypesNamespace: virtualTypesNamespace)
        {
        }

        /// <summary>
        /// Generates SizeCalculators class containing ONLY virtual types (map entries and tuples).
        /// Used for GProtobuf.Generated namespace which centralizes all virtual type methods.
        /// </summary>
        public void GenerateVirtualTypesOnly(string currentNamespace)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine("public static class SizeCalculators");
            _sb.StartNewBlock();

            // Generate virtual map entry size calculators (all types, ignoring IsGenerated flag)
            GenerateVirtualMapEntrySizeCalculators(ignoreIsGeneratedFlag: true);

            // Generate virtual tuple size calculators (all types, ignoring IsGenerated flag)
            GenerateVirtualTupleSizeCalculators(ignoreIsGeneratedFlag: true);

            _sb.EndBlock();
            _sb.AppendNewLine();
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

            // Generate dictionary-based type dispatch for large type hierarchies
            var typesList = types.ToList();
            GenerateTypeDispatchDictionaries(typesList);

            foreach (var type in typesList)
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

                // Generate OwnFieldsSize and WrapperSize methods for derived types
                if (_registry.IsDerivedType(type.FullName))
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GenerateOwnFieldsSizeMethod(type, className);
                    GenerateWrapperSizeMethod(type, className);
                }
            }

            // Generate ContentSize and OwnFieldsSize methods for ProtoInclude derived types
            // that are not in the main types list (types without [ProtoContract])
            var processedTypes = new HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

            // Generate ContentSize, OwnFieldsSize, and WrapperSize methods for ProtoInclude types
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    GenerateCalculateContentSizeMethod(protoIncludeType);

                    // Also generate OwnFieldsSize and WrapperSize if it's a derived type
                    if (_registry.IsDerivedType(protoIncludeTypeName))
                    {
                        var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                        GenerateOwnFieldsSizeMethod(protoIncludeType, className);
                        GenerateWrapperSizeMethod(protoIncludeType, className);
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
            }
            // Virtual map entry and tuple size calculators are NOT generated here - they are centralized
            // in GProtobuf.Generated.Serialization.cs via GenerateVirtualTypesOnly().
            // Types are registered during field processing above, then generated once in the shared file.

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates size calculator methods for all registered virtual map entry types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualMapEntrySizeCalculators(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualMapRegistry.GetAllTypes();

            // Filter types based on IsGenerated flag
            var virtualTypes = ignoreIsGeneratedFlag
                ? allTypes.ToList()
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (virtualTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Size Calculators");

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry, _virtualTypesNamespace);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateSizeCalculator(virtualType);
            }

        }

        /// <summary>
        /// Generates size calculator methods for all registered virtual tuple types.
        /// </summary>
        /// <param name="ignoreIsGeneratedFlag">If true, generates all types regardless of IsGenerated flag (for GProtobuf.Generated).
        /// If false, skips types that have already been generated.</param>
        private void GenerateVirtualTupleSizeCalculators(bool ignoreIsGeneratedFlag)
        {
            var allTypes = _virtualTupleRegistry.GetAllTypes();

            // Filter types based on IsGenerated flag
            var tupleTypes = ignoreIsGeneratedFlag
                ? allTypes
                : allTypes.Where(t => !t.IsGenerated).ToList();

            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Size Calculators");

            var generator = new VirtualTupleGenerator(_sb, null, _registry, _virtualTypesNamespace);
            foreach (var tupleInfo in tupleTypes)
            {
                generator.GenerateSizeCalculator(tupleInfo);
            }
        }

        private const string CalculatorType = "global::GProtobuf.Core.WriteSizeCalculator";

        /// <summary>
        /// Generates function pointer dispatch tables for types with many derived classes.
        /// This provides O(1) type lookup with direct function pointer call vs O(n) type pattern matching.
        /// </summary>
        private void GenerateTypeDispatchDictionaries(List<TypeDefinition> types)
        {
            var generatedDictionaries = new HashSet<string>();

            foreach (var type in types)
            {
                if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
                {
                    var sortedDerived = GeneratorHelpers.GetSortedDerivedTypes(type.FullName, _registry);
                    if (sortedDerived != null && sortedDerived.Count >= DictionaryDispatchThreshold)
                    {
                        var className = TypeNameHelper.GetClassName(type.FullName);
                        if (!generatedDictionaries.Contains(className))
                        {
                            TryGenerateFunctionPointerDispatch(
                                className,
                                type.FullName,
                                CalculatorType,
                                "calculator",
                                sortedDerived,
                                (derivedType, derivedClassName, castVar) =>
                                {
                                    _sb.AppendIndentedLine($"Calculate{derivedClassName}WrapperSize(ref calculator, {castVar});");
                                });
                            generatedDictionaries.Add(className);
                        }
                    }
                }
            }
        }

        #region CalculateSize Method

        /// <summary>
        /// Generates Calculate{ClassName}Size method.
        /// This is the main entry point that handles inheritance wrappers.
        /// For root types with ProtoIncludes, no Size method is generated - use ContentSize instead.
        /// </summary>
        private void GenerateCalculateSizeMethod(TypeDefinition type)
        {
            // Skip generating Size method for root types with ProtoIncludes
            // These types should use ContentSize directly
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0 && !_registry.IsDerivedType(type.FullName))
            {
                return;
            }

            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Calculate{className}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (obj == null) return;");
            }

            bool isDerived = _registry.IsDerivedType(type.FullName);

            if (isDerived)
            {
                GenerateCalculateSizeForDerived(type, className);
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
            ForEachTypeMember(
                type,
                "obj",
                (member, src) => GenerateFieldSize(member, src),
                (customMember, src) => GenerateCustomBufferFieldSize(customMember, src));
        }

        private void GenerateCalculateSizeForDerived(TypeDefinition type, string className)
        {
            // Wire format: [wrapper tag][length][derived fields INSIDE][base fields AFTER]

            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count < 2)
            {
                // No inheritance - shouldn't happen for derived types, but handle gracefully
                GenerateCalculateContentSizeForDerivedType(type, className);
                return;
            }

            // Get root (base) type
            var rootTypeName = inheritanceChain[0];

            _sb.AppendIndentedLine($"// ProtoInclude wrapper format (Level200 compatibility)");
            _sb.AppendIndentedLine($"// Wire: [wrapper tag][length][derived fields INSIDE wrapper][base fields AFTER wrapper]");
            _sb.AppendNewLine();

            // Call WrapperSize for each level in inheritance chain (except root)
            // Chain is [Root, Level1, Level2, ..., CurrentType]
            for (int i = 1; i < inheritanceChain.Count; i++)
            {
                var levelTypeName = inheritanceChain[i];
                var levelClassName = TypeNameHelper.GetClassName(levelTypeName);
                var levelNamespace = _registry.GetNamespaceForType(levelTypeName);
                var nsPrefix = GeneratorHelpers.GetNamespacePrefix(levelNamespace, _currentNamespace);
                _sb.AppendIndentedLine($"{nsPrefix}SizeCalculators.Calculate{levelClassName}WrapperSize(ref calculator, obj);");
            }

            // Calculate and add base fields size AFTER all wrappers
            var rootClassName = TypeNameHelper.GetClassName(rootTypeName);
            var rootNamespace = _registry.GetNamespaceForType(rootTypeName);
            var rootNsPrefix = GeneratorHelpers.GetNamespacePrefix(rootNamespace, _currentNamespace);
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"// Base class fields ({rootClassName}) - AFTER wrapper");
            _sb.AppendIndentedLine($"{rootNsPrefix}SizeCalculators.Calculate{rootClassName}BaseFieldsOnlySize(ref calculator, obj);");
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

            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Calculate{className}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            // Add null check for reference types (structs and enums can't be null)
            if (!type.IsStruct && !type.IsEnum)
            {
                _sb.AppendIndentedLine("if (obj == null) return;");
            }

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
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Calculate{className}BaseFieldsOnlySize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (obj == null) return;");
            }

            // Calculate ONLY the fields defined in this base class, no switch/dispatch
            ForEachProtoMember(type.ProtoMembers, "obj", (member, src) => GenerateFieldSize(member, src));
            ForEachCustomBufferMember(type.CustomBufferMembers, "obj", (member, src) => GenerateCustomBufferFieldSize(member, src));

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates ContentSize method with type dispatch for base types with ProtoInclude.
        /// For derived types: calls WrapperSize (wrapper + own fields).
        /// Then always calls BaseFieldsOnlySize (base fields).
        /// Uses dictionary-based O(1) dispatch for types with many derived classes.
        /// </summary>
        private void GenerateCalculateContentSizeWithTypeDispatch(TypeDefinition type, string className)
        {
            var sortedDerived = GeneratorHelpers.GetSortedDerivedTypes(type.FullName, _registry);
            if (sortedDerived == null)
            {
                // No derived types - just calculate own fields
                ForEachProtoMember(type.ProtoMembers, "obj", (member, src) => GenerateFieldSize(member, src));
                return;
            }

            // Use function pointer dispatch for large type hierarchies (O(1) lookup + direct call)
            if (sortedDerived.Count >= DictionaryDispatchThreshold)
            {
                _sb.AppendIndentedLine("// Dispatch to derived type wrapper calculation");
                GenerateFunctionPointerCall(className, type.FullName, CalculatorType, "calculator", "obj", sortedDerived.Count, returnAfterCall: false);
            }
            else
            {
                // Use regular type switch for small hierarchies (JIT optimizes these well)
                _sb.AppendIndentedLine("// Dispatch to derived type wrapper calculation");
                _sb.AppendIndentedLine("switch (obj)");
                _sb.StartNewBlock();

                foreach (var derivedType in sortedDerived)
                {
                    var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                    _sb.AppendIndentedLine($"case global::{derivedType} derived:");
                    _sb.IncreaseIndent();

                    // Check for multi-level inheritance relative to current base type
                    var fullChain = _registry.GetInheritanceChain(derivedType);
                    var baseIndex = FindIndexInChain(fullChain, type.FullName);
                    var levelsFromBase = fullChain.Count - 1 - baseIndex;

                    if (baseIndex < 0 || levelsFromBase <= 1)
                    {
                        // Single-level inheritance - use existing wrapper calculation
                        _sb.AppendIndentedLine($"Calculate{derivedClassName}WrapperSize(ref calculator, derived);");
                    }
                    else
                    {
                        // Multi-level inheritance - calculate ALL wrapper levels from base to derived
                        _sb.AppendIndentedLine($"// Multi-level inheritance: {levelsFromBase} levels from {className}");
                        GenerateMultiLevelWrapperSizeCalculation(fullChain, baseIndex + 1, fullChain.Count - 1, "derived");
                    }

                    _sb.AppendIndentedLine("break;");  // break, NOT return - continue to base fields
                    _sb.DecreaseIndent();
                }

                _sb.EndBlock();
            }
            _sb.AppendNewLine();

            // Base fields - always executed (for all types including derived)
            _sb.AppendIndentedLine("// Base fields - always calculated");
            _sb.AppendIndentedLine($"Calculate{className}BaseFieldsOnlySize(ref calculator, obj);");
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
            var protoInclude = GeneratorHelpers.FindProtoInclude(baseType, firstDerivedTypeName);

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
        /// ProtoInclude wrapper is calculated FIRST, then own fields.
        /// </summary>
        private void CalculateWrapperContentSizeRecursive(IReadOnlyList<string> chain, int levelIndex, string calcVar)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // If there's a next level, add nested wrapper size FIRST (ProtoInclude before own fields)
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Add nested wrapper for {nextClassName} (ProtoInclude first)");

                    // Add nested wrapper tag size
                    TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content size - use unique counter instead of levelIndex
                    var nestedCalcVar = GetNextNestedCalcVar();
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursive call for nested level
                    CalculateWrapperContentSizeRecursive(chain, levelIndex + 1, nestedCalcVar);

                    // Add length prefix size and content size to parent calculator
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }
            _sb.AppendIndentedLine($"Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, obj);");
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
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"public static void Calculate{className}OwnFieldsSize(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref global::GProtobuf.Core.WriteSizeCalculator calculator,");
            _sb.AppendIndentedLine($"global::{type.FullName} obj)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (obj == null) return;");
            }

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
        /// Generates Calculate{ClassName}WrapperSize method for derived types.
        /// Calculates ProtoInclude wrapper tag + length + own fields size.
        /// Used by ContentSize methods to avoid code duplication.
        /// </summary>
        private void GenerateWrapperSizeMethod(TypeDefinition type, string className)
        {
            var derivedInfo = GeneratorHelpers.TryGetNestedDerivedTypeInfo(type.FullName, _registry);
            if (derivedInfo == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {className}, skipping WrapperSize generation");
                return;
            }

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Calculates ProtoInclude wrapper size for {className}.");
            _sb.AppendIndentedLine($"/// Wrapper tag + length prefix + own fields.");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            _sb.AppendIndentedLine($"private static void Calculate{className}WrapperSize(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref global::GProtobuf.Core.WriteSizeCalculator calculator,");
            _sb.AppendIndentedLine($"global::{type.FullName} obj)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (obj == null) return;");
            }

            // Tag size for ProtoInclude wrapper
            TagCodeHelper.AddTagSize(_sb, derivedInfo.ProtoInclude.FieldId, WireType.Len);

            // Calculate wrapper content (own fields only)
            _sb.AppendIndentedLine($"var wrapperCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"Calculate{className}OwnFieldsSize(ref wrapperCalc, obj);");
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)wrapperCalc.Length);");
            _sb.AppendIndentedLine($"calculator.AddByteLength(wrapperCalc.Length);");

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates inline size calculation code for multi-level inheritance wrappers.
        /// For chain [Base, Mid, Derived] with startIndex=1 and targetIndex=2:
        /// Calculates: Mid wrapper (containing Derived wrapper + Mid fields)
        /// </summary>
        private void GenerateMultiLevelWrapperSizeCalculation(
            IReadOnlyList<string> chain,
            int startIndex,
            int targetIndex,
            string instanceVar)
        {
            // Generate outermost wrapper calculation
            var startTypeName = chain[startIndex];
            var startClassName = TypeNameHelper.GetClassName(startTypeName);
            var parentTypeName = chain[startIndex - 1];
            var parentType = _registry.GetByFullName(parentTypeName);

            // Find ProtoInclude field ID for start type in parent
            var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == startTypeName);
            if (protoInclude == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {startTypeName} in {parentTypeName}");
                return;
            }

            // Tag size for outermost wrapper
            TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len);

            // Calculate outermost wrapper content
            var outerCalcVar = GetNextNestedCalcVar();
            _sb.AppendIndentedLine($"var {outerCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Recursively calculate nested wrappers
            GenerateNestedWrapperSizeCalculationRecursive(chain, startIndex, targetIndex, outerCalcVar, instanceVar);

            // Add length prefix and content to main calculator
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){outerCalcVar}.Length);");
            _sb.AppendIndentedLine($"calculator.AddByteLength({outerCalcVar}.Length);");
        }

        /// <summary>
        /// Recursively generates size calculation for nested wrappers.
        /// Each level adds: nested wrapper tag + length + content, then own fields.
        /// </summary>
        private void GenerateNestedWrapperSizeCalculationRecursive(
            IReadOnlyList<string> chain,
            int currentIndex,
            int targetIndex,
            string calcVar,
            string instanceVar)
        {
            var currentTypeName = chain[currentIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);
            var currentNamespace = _registry.GetNamespaceForType(currentTypeName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(currentNamespace, _currentNamespace);

            if (currentIndex == targetIndex)
            {
                // Innermost level - just own fields
                _sb.AppendIndentedLine($"{nsPrefix}SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, {instanceVar});");
            }
            else
            {
                // Intermediate level - add nested wrapper FIRST, then own fields
                var nextTypeName = chain[currentIndex + 1];
                var nextProtoInclude = currentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == nextTypeName);

                if (nextProtoInclude != null)
                {
                    // Add nested wrapper tag size
                    TagCodeHelper.AddTagSize(_sb, nextProtoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content
                    var nestedCalcVar = GetNextNestedCalcVar();
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursively calculate nested size
                    GenerateNestedWrapperSizeCalculationRecursive(chain, currentIndex + 1, targetIndex, nestedCalcVar, instanceVar);

                    // Add length prefix + content size
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }

                // Add current level's OWN fields AFTER nested wrapper
                _sb.AppendIndentedLine($"{nsPrefix}SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, {instanceVar});");
            }
        }

        /// <summary>
        /// Generates ContentSize calculation for derived types.
        /// Calls WrapperSize + BaseFieldsOnlySize for proper wire format.
        /// Wire format: [wrapper tag][wrapper length][own fields inside][base fields outside]
        /// </summary>
        private void GenerateCalculateContentSizeForDerivedType(TypeDefinition type, string className)
        {
            // 1. Wrapper + own fields
            _sb.AppendIndentedLine($"// ProtoInclude wrapper + own fields");
            _sb.AppendIndentedLine($"Calculate{className}WrapperSize(ref calculator, obj);");

            // 2. Base fields
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootClassName = TypeNameHelper.GetClassName(rootTypeName);
            var rootNamespace = _registry.GetNamespaceForType(rootTypeName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(rootNamespace, _currentNamespace);

            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"// Base fields ({rootClassName})");
            _sb.AppendIndentedLine($"{nsPrefix}SizeCalculators.Calculate{rootClassName}BaseFieldsOnlySize(ref calculator, obj);");
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
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            switch (category)
            {
                case FieldCategory.Map:
                    GenerateMapFieldSize(member, sourceVar);
                    break;
                case FieldCategory.Collection:
                    GenerateCollectionFieldSize(member, sourceVar);
                    break;
                case FieldCategory.Enum:
                    GenerateEnumFieldSize(member, sourceVar);
                    break;
                case FieldCategory.Tuple:
                    _tupleHandler.GenerateTupleSize(member.FieldId, sourceVar, member.Type);
                    break;
                case FieldCategory.Primitive:
                    _primitiveHandler.GenerateSize(
                        _sb,
                        sourceVar,
                        member.Type,
                        member.DataFormat,
                        member.FieldId,
                        member.IsNullable,
                        member.IsRequired);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateSize(_sb, member, sourceVar);
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                    _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be serialized)");
                    break;
                case FieldCategory.ComplexType:
                    GenerateComplexTypeSize(member, sourceVar);
                    break;
            }
        }

        private void GenerateEnumFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            EnumFieldHelper.GenerateEnumField(
                _sb,
                member,
                sourceVar,
                writeTag: () => TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt),
                writeValue: (valueExpr, _) => _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){valueExpr});"));
        }

        private void GenerateMapFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, "SizeCalculators", _registry, _virtualTypesNamespace);
            mapHandler.GenerateSize(member, sourceVar);
        }

        private void GenerateCollectionFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            // Check if element type is enum (enums use varint encoding like primitives)
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection)
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

            if (!isNonNullableStruct)
            {
                _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                _sb.StartNewBlock();
            }

            if (isNestedDerivedType)
            {
                GenerateNestedDerivedTypeSize(member, sourceVar, typeName, typeDef);
            }
            else if (isPolymorphicField)
            {
                GeneratePolymorphicFieldSize(member, sourceVar, typeName, typeDef);
            }
            else
            {
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
            string valueArg = GeneratorHelpers.GetNullableValueAccess(sourceVar, member, typeDef, _registry);
            var typeNamespace = _registry.GetNamespaceForType(member.Type);

            // Get nested derived type info (parent type, ProtoInclude, wrapper tag)
            var derivedInfo = GeneratorHelpers.TryGetNestedDerivedTypeInfo(member.Type, _registry);
            if (derivedInfo == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type}");
                GenerateStandardComplexTypeSize(member, sourceVar, typeName, typeDef);
                return;
            }

            var parentTypeName = derivedInfo.ParentTypeName;
            var wrapperTag = derivedInfo.WrapperTag;
            var protoInclude = derivedInfo.ProtoInclude;
            var parentTypeFullName = derivedInfo.ParentTypeFullName;

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

            string sizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            _sb.AppendIndentedLine($"{sizeCalcPrefix}SizeCalculators.Calculate{typeName}OwnFieldsSize(ref {wrapperContentCalcVar}, {valueArg});");
            _sb.AppendIndentedLine($"{nestedCalcVar}.WriteVarUInt32((uint){wrapperContentCalcVar}.Length);");
            _sb.AppendIndentedLine($"{nestedCalcVar}.AddByteLength({wrapperContentCalcVar}.Length);");
            _sb.AppendNewLine();

            // Add base fields size (AFTER wrapper)
            _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
            string parentSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
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
            string valueArg = GeneratorHelpers.GetNullableValueAccess(sourceVar, member, typeDef, _registry);

            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            _sb.AppendIndentedLine($"var {lengthVar} = calculator.Length;");

            // NOTE: For top-level serialization, ProtoInclude wrapper is included in ContentSize.
            // For nested fields of non-derived types, no wrapper is needed.
            var sizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"{sizeCalcPrefix}SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {valueArg});");

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
            var sortedDerived = GeneratorHelpers.GetSortedDerivedTypes(member.Type, _registry);
            if (sortedDerived == null)
            {
                // No derived types - fallback to standard calculation
                GenerateStandardComplexTypeSize(member, sourceVar, typeName, typeDef);
                return;
            }

            _sb.AppendIndentedLine($"// Polymorphic field: runtime type dispatch for ProtoInclude wrapper size");

            // Add field tag size
            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            _sb.AppendIndentedLine($"switch ({sourceVar})");
            _sb.StartNewBlock();

            foreach (var derivedType in sortedDerived)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                var derivedNamespace = _registry.GetNamespaceForType(derivedType);

                // Get ProtoInclude information using helper
                var derivedInfo = GeneratorHelpers.TryGetNestedDerivedTypeInfo(derivedType, _registry);
                if (derivedInfo == null)
                {
                    _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {derivedType}");
                    continue;
                }

                var wrapperTag = derivedInfo.WrapperTag;
                var parentTypeName = derivedInfo.ParentTypeName;
                var protoInclude = derivedInfo.ProtoInclude;

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

                string derivedSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNamespace, _currentNamespace);

                _sb.AppendIndentedLine($"{derivedSizeCalcPrefix}SizeCalculators.Calculate{derivedClassName}OwnFieldsSize(ref {wrapperCalcVar}, derived{derivedClassName});");
                _sb.AppendIndentedLine($"{totalCalcVar}.WriteVarUInt32((uint){wrapperCalcVar}.Length);");
                _sb.AppendIndentedLine($"{totalCalcVar}.AddByteLength({wrapperCalcVar}.Length);");
                _sb.AppendNewLine();

                // Base fields size (AFTER wrapper)
                _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
                string parentSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
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
                var sizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
                _sb.AppendIndentedLine($"{sizeCalcPrefix}SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
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

        /// <summary>
        /// Finds the index of a type name in an inheritance chain.
        /// Returns -1 if not found.
        /// </summary>
        private static int FindIndexInChain(IReadOnlyList<string> chain, string typeName)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i] == typeName)
                    return i;
            }
            return -1;
        }

        #endregion

    }
}
