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
    /// Generates StreamWriters class with Write{ClassName} methods.
    /// Handles serialization from objects to StreamWriter.
    /// </summary>
    internal class StreamWriterGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly TypeRegistry _registry;
        private readonly PrimitiveHandler _primitiveHandler;
        private readonly CollectionHandler _collectionHandler;
        private readonly TupleHandler _tupleHandler;
        private readonly VirtualMapTypeRegistry _virtualMapRegistry;
        private readonly VirtualTupleTypeRegistry _virtualTupleRegistry;
        private readonly string _writerType;
        private readonly string _className;
        private readonly string _writerKind;
        private string _currentNamespace;
        private int _nestedCalcCounter;

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null, null, "Stream")
        {
        }

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : this(sb, registry, virtualMapRegistry, null, "Stream")
        {
        }

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : this(sb, registry, virtualMapRegistry, virtualTupleRegistry, "Stream")
        {
        }

        protected StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string writerKind)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
            _collectionHandler = new CollectionHandler(sb, registry);
            _virtualTupleRegistry = virtualTupleRegistry ?? new VirtualTupleTypeRegistry();
            _virtualMapRegistry = virtualMapRegistry ?? new VirtualMapTypeRegistry(_virtualTupleRegistry, _registry);
            _tupleHandler = new TupleHandler(sb, _virtualTupleRegistry);
            _writerKind = writerKind;
            _writerType = $"global::GProtobuf.Core.{writerKind}Writer";
            _className = $"{writerKind}Writers";
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
        /// Generates complete Writers class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            // Store current namespace for cross-namespace method calls
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {_className}");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateWriteMethod(type);

                // Generate OwnFieldsWrite method for derived types (used in ProtoInclude wrapper writing)
                if (_registry.IsDerivedType(type.FullName))
                {
                    var className = TypeNameHelper.GetClassName(type.FullName);
                    GenerateOwnFieldsWriteMethod(type, className);
                }
            }

            // Generate WriteContent and OwnFieldsWrite methods for ProtoInclude derived types
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

            // Generate WriteContent and OwnFieldsWrite methods for ProtoInclude types
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    var className = TypeNameHelper.GetClassName(protoIncludeTypeName);
                    GenerateWriteContentMethod(protoIncludeType, className);

                    // Also generate OwnFieldsWrite if it's a derived type
                    if (_registry.IsDerivedType(protoIncludeTypeName))
                    {
                        GenerateOwnFieldsWriteMethod(protoIncludeType, className);
                    }

                    processedTypes.Add(protoIncludeTypeName);
                }
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
            _sb.AppendIndentedLine("// Virtual Map Entry Writers");

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry, _writerKind);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateWriter(virtualType);
            }

            // Generate KeyValue methods that use MapEntry methods
            var keyValueGenerator = new KeyValueClassGenerator(_sb, _virtualMapRegistry);
            keyValueGenerator.GenerateKeyValueMethods(_className);
        }

        /// <summary>
        /// Generates writer methods for all registered virtual tuple types.
        /// </summary>
        private void GenerateVirtualTupleWriters()
        {
            var tupleTypes = _virtualTupleRegistry.GetAllTypes();
            if (tupleTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Tuple Writers");

            var generator = new VirtualTupleGenerator(_sb, _writerKind, _registry);
            foreach (var tupleInfo in tupleTypes)
            {
                generator.GenerateWriter(tupleInfo);
            }
        }

        #region Write Method

        /// <summary>
        /// Generates Write{ClassName}(ref Writer writer, instance) method.
        /// </summary>
        private void GenerateWriteMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            // Generate main Write method (entry point)
            _sb.AppendIndentedLine($"public static void Write{className}(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            bool isDerived = _registry.IsDerivedType(type.FullName);

            if (isDerived)
            {
                GenerateWriteMethodForDerived(type, className);
            }
            else if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                GenerateWriteMethodWithInheritance(type, className);
            }
            else
            {
                GenerateSimpleWriteMethod(type, className);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();

            // Generate WriteContent method (for nested serialization without tag)
            GenerateWriteContentMethod(type, className);

            // Generate WriteBaseFieldsOnly method for base types with ProtoIncludes
            // This is needed for nested derived type serialization to avoid runtime dispatch
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                GenerateWriteBaseFieldsOnlyMethod(type, className);
            }

            // Generate Write{Type}_As{Parent} methods for polymorphic serialization
            if (_registry.IsDerivedType(type.FullName))
            {
                GenerateWriteAsParentMethods(type, className);
            }
        }

        /// <summary>
        /// Generates WriteXxxContent method.
        /// For types with ProtoInclude hierarchy, this includes type dispatch and wrapper generation.
        /// </summary>
        private void GenerateWriteContentMethod(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"public static void Write{className}Content(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // For enum types, generate simple VarInt write
            if (type.IsEnum)
            {
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)instance);");
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
                GenerateWriteContentWithTypeDispatch(type, className);
            }
            else if (isDerived)
            {
                // Derived type - include ProtoInclude wrappers in content
                GenerateWriteContentForDerivedType(type, className);
            }
            else
            {
                // Simple type - just own fields
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldWrite(member, "instance");
                    }
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates WriteBaseFieldsOnly method that writes ONLY base class fields without runtime dispatch.
        /// This is used when serializing nested derived types to avoid duplicate field writing.
        /// Example: WriteModbusTransactionBaseFieldsOnly writes only ModbusTransaction fields,
        /// ignoring the fact that instance might be ModbusManualTransaction.
        /// </summary>
        private void GenerateWriteBaseFieldsOnlyMethod(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Writes ONLY base {className} fields without runtime type dispatch.");
            _sb.AppendIndentedLine($"/// Used for nested derived type serialization after ProtoInclude wrapper.");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static void Write{className}BaseFieldsOnly(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // Write ONLY the fields defined in this base class, no switch/dispatch
            if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates WriteContent method with type dispatch for base types with ProtoInclude.
        /// </summary>
        private void GenerateWriteContentWithTypeDispatch(TypeDefinition type, string className)
        {
            var allDerivedTypes = _registry.GetAllDerivedTypes(type.FullName);
            if (allDerivedTypes.Count == 0)
            {
                // No derived types - just write own fields
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldWrite(member, "instance");
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
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        /// <summary>
        /// Generates outermost ProtoInclude wrapper writing.
        /// For inheritance chain [Base, Derived1, Derived2, ...], generates wrapper from Base to Derived1.
        /// </summary>
        private void GenerateOutermostWrapper(IReadOnlyList<string> chain, TypeDefinition type)
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

            // Calculate wrapper content size first
            var contentCalcVar = "wrapperContentCalc";
            _sb.AppendIndentedLine($"var {contentCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Calculate wrapper content size (derived fields + nested wrappers if any)
            CalculateWrapperContentSizeForWrite(chain, 1, contentCalcVar);

            // Write wrapper tag
            TagCodeHelper.WriteTag(_sb, protoInclude.FieldId, WireType.Len);

            // Write wrapper length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){contentCalcVar}.Length);");
            _sb.AppendNewLine();

            // Write wrapper content (derived fields + nested wrappers)
            WriteWrapperContentRecursive(chain, 1);
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Calculates wrapper content size for writing (same logic as in SizeCalculatorGenerator).
        /// </summary>
        private void CalculateWrapperContentSizeForWrite(IReadOnlyList<string> chain, int levelIndex, string calcVar)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // Add this level's OWN fields size
            _sb.AppendIndentedLine($"// Calculate {currentClassName}'s own fields size");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, instance);");

            // If there's a next level, add nested wrapper size
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Calculate nested wrapper for {nextClassName}");

                    // Add nested wrapper tag size
                    TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content size - use unique counter instead of levelIndex
                    var nestedCalcVar = $"nestedCalc{_nestedCalcCounter++}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursive call for nested level
                    CalculateWrapperContentSizeForWrite(chain, levelIndex + 1, nestedCalcVar);

                    // Add length prefix size and content size to parent calculator
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }
        }

        /// <summary>
        /// Recursively writes wrapper content (own fields + nested wrappers).
        /// </summary>
        private void WriteWrapperContentRecursive(IReadOnlyList<string> chain, int levelIndex)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // Write this level's OWN fields FIRST
            _sb.AppendIndentedLine($"// Write {currentClassName}'s own fields");
            _sb.AppendIndentedLine($"Write{currentClassName}OwnFields(ref writer, instance);");

            // If there's a next level, write nested wrapper
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Write nested wrapper for {nextClassName}");

                    // Calculate nested wrapper content size
                    var nestedCalcVar = $"nestedWrapperCalc{levelIndex}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    CalculateWrapperContentSizeForWrite(chain, levelIndex + 1, nestedCalcVar);

                    // Write nested wrapper tag
                    TagCodeHelper.WriteTag(_sb, protoInclude.FieldId, WireType.Len);

                    // Write nested wrapper length
                    _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){nestedCalcVar}.Length);");

                    // Recursively write nested content
                    WriteWrapperContentRecursive(chain, levelIndex + 1);
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
        /// Generates Write{ClassName}OwnFields method for derived types.
        /// Writes ONLY fields defined at this type level (not inherited from base).
        /// Used for ProtoInclude wrapper content writing.
        /// </summary>
        private void GenerateOwnFieldsWriteMethod(TypeDefinition type, string className)
        {
            var ownMembers = _registry.GetOwnProtoMembers(type.FullName);

            _sb.AppendIndentedLine($"/// <summary>");
            _sb.AppendIndentedLine($"/// Writes {className}'s OWN fields (not inherited from base).");
            _sb.AppendIndentedLine($"/// </summary>");
            _sb.AppendIndentedLine($"public static void Write{className}OwnFields(");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"ref {_writerType} writer,");
            _sb.AppendIndentedLine($"global::{type.FullName} instance)");
            _sb.DecreaseIndent();
            _sb.StartNewBlock();

            if (ownMembers.Count == 0)
            {
                _sb.AppendIndentedLine("// No own fields (all inherited from base)");
            }
            else
            {
                _sb.AppendIndentedLine($"// Write ONLY own fields (not inherited) - {ownMembers.Count} field(s)");
                foreach (var member in ownMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates WriteContent for derived types.
        /// WriteContent writes ONLY the fields, without ProtoInclude wrapper.
        /// The wrapper is added by Write{Type}_AsParent methods.
        /// </summary>
        private void GenerateWriteContentForDerivedType(TypeDefinition type, string className)
        {
            // Write root type fields (base class fields)
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootType = _registry.GetByFullName(rootTypeName);
            if (rootType?.ProtoMembers != null)
            {
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            // Write own fields (if not root)
            if (type.FullName != rootTypeName && type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        /// <summary>
        /// Generates Write{Type}_As{Parent} methods for all ancestors.
        /// For C:B:A, generates WriteC_AsA and WriteC_AsB.
        /// These methods handle wrapper generation for polymorphic serialization.
        /// </summary>
        private void GenerateWriteAsParentMethods(TypeDefinition type, string className)
        {
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);

            // Generate method for each ancestor (excluding the type itself)
            for (int ancestorIndex = 0; ancestorIndex < inheritanceChain.Count - 1; ancestorIndex++)
            {
                var ancestorTypeName = inheritanceChain[ancestorIndex];
                var ancestorClassName = TypeNameHelper.GetClassName(ancestorTypeName);

                GenerateWriteAsSpecificParent(type, className, ancestorTypeName, ancestorClassName, inheritanceChain, ancestorIndex);
            }
        }

        /// <summary>
        /// Generates Write{Type}_As{Ancestor} method for a specific ancestor.
        /// </summary>
        private void GenerateWriteAsSpecificParent(
            TypeDefinition type,
            string className,
            string ancestorTypeName,
            string ancestorClassName,
            IReadOnlyList<string> inheritanceChain,
            int ancestorIndex)
        {
            var typeIndex = inheritanceChain.Count - 1;

            _sb.AppendIndentedLine($"private static void Write{className}_As{ancestorClassName}(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // CRITICAL: protobuf-net Level200 wire format order for WriteC_AsA with chain [A, B, C]:
            //   1. ProtoInclude wrappers: B wrapper (field 5) containing C wrapper (field 10)
            //   2. Ancestor A fields AFTER the wrappers

            // Step 1: Generate nested ProtoInclude wrappers (B -> C)
            GenerateNestedWrappersForAsParent(inheritanceChain, ancestorIndex + 1, typeIndex);

            // Step 2: Write ancestor (A) fields AFTER the wrappers
            var ancestorType = _registry.GetByFullName(ancestorTypeName);
            if (ancestorType?.ProtoMembers != null)
            {
                foreach (var member in ancestorType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Recursively generates nested wrappers from currentIndex to targetIndex.
        /// </summary>
        private void GenerateNestedWrappersForAsParent(
            IReadOnlyList<string> chain,
            int currentIndex,
            int targetIndex)
        {
            if (currentIndex > targetIndex)
                return;

            var currentTypeName = chain[currentIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var parentTypeName = chain[currentIndex - 1];
            var parentType = _registry.GetByFullName(parentTypeName);

            // Find ProtoInclude field ID for current type in parent
            var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == currentTypeName);
            if (protoInclude == null)
                return;

            // Write tag for wrapper
            TagCodeHelper.WriteTag(_sb, protoInclude.FieldId, WireType.Len);

            // Calculate size for this wrapper
            var calcVar = $"calc{currentIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // If this is the target type, calculate own fields
            // Otherwise, calculate parent fields + nested wrapper
            if (currentIndex == targetIndex)
            {
                // This is the innermost level - calculate own fields
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, instance);");
            }
            else
            {
                // This is intermediate level - calculate own fields + nested wrapper
                var currentType = _registry.GetByFullName(currentTypeName);

                // Calculate current level's fields
                if (currentType?.ProtoMembers != null)
                {
                    foreach (var member in currentType.ProtoMembers)
                    {
                        // Generate size calculation for this field
                        GenerateFieldSizeCalculation(member, "instance", calcVar);
                    }
                }

                // Add nested wrapper size
                var nextTypeName = chain[currentIndex + 1];
                var nextProtoInclude = currentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == nextTypeName);
                if (nextProtoInclude != null)
                {
                    // Add tag size
                    TagCodeHelper.AddTagSize(_sb, nextProtoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content
                    var nestedCalcVar = $"nested{currentIndex}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursively calculate nested size
                    GenerateNestedSizeCalculationForAsParent(chain, currentIndex + 1, targetIndex, nestedCalcVar);

                    // Add length prefix + content size
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }

            // Write length prefix
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){calcVar}.Length);");

            // Write content
            if (currentIndex == targetIndex)
            {
                // Write own fields
                _sb.AppendIndentedLine($"Write{currentClassName}OwnFields(ref writer, instance);");
            }
            else
            {
                // Write parent fields + nested wrapper
                var currentType = _registry.GetByFullName(currentTypeName);
                if (currentType?.ProtoMembers != null)
                {
                    foreach (var member in currentType.ProtoMembers)
                    {
                        GenerateFieldWrite(member, "instance");
                    }
                }

                // Recursively write nested wrapper
                GenerateNestedWrappersForAsParent(chain, currentIndex + 1, targetIndex);
            }
        }

        /// <summary>
        /// Recursively calculates size for nested wrappers.
        /// </summary>
        private void GenerateNestedSizeCalculationForAsParent(
            IReadOnlyList<string> chain,
            int currentIndex,
            int targetIndex,
            string calcVar)
        {
            if (currentIndex > targetIndex)
                return;

            var currentTypeName = chain[currentIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);

            if (currentIndex == targetIndex)
            {
                // Innermost level - just own fields
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, instance);");
            }
            else
            {
                // Intermediate level - own fields + nested wrapper
                var currentType = _registry.GetByFullName(currentTypeName);

                // Calculate current level fields
                if (currentType?.ProtoMembers != null)
                {
                    foreach (var member in currentType.ProtoMembers)
                    {
                        GenerateFieldSizeCalculation(member, "instance", calcVar);
                    }
                }

                // Add nested wrapper
                var nextTypeName = chain[currentIndex + 1];
                var nextProtoInclude = currentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == nextTypeName);
                if (nextProtoInclude != null)
                {
                    TagCodeHelper.AddTagSize(_sb, nextProtoInclude.FieldId, WireType.Len, calcVar);

                    var nestedCalcVar = $"n{currentIndex}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    GenerateNestedSizeCalculationForAsParent(chain, currentIndex + 1, targetIndex, nestedCalcVar);

                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }
        }

        private void GenerateSimpleWriteMethod(TypeDefinition type, string className)
        {
            // Write each field
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        private void GenerateWriteMethodWithInheritance(TypeDefinition type, string className)
        {
            // CRITICAL: protobuf-net Level200 wire format requires ProtoInclude wrapper FIRST, then base class fields
            // When serializing a base type with derived types, we must:
            // 1. Write ProtoInclude wrapper (if instance is derived type)
            // 2. Then write base class fields AFTER the wrapper

            // For A with B:A and C:B, we need cases for both B and C in WriteA
            var allDerivedTypes = _registry.GetAllDerivedTypes(type.FullName);
            if (allDerivedTypes.Count == 0)
            {
                // No derived types - just write base fields directly
                if (type.ProtoMembers != null)
                {
                    foreach (var member in type.ProtoMembers)
                    {
                        GenerateFieldWrite(member, "instance");
                    }
                }
                return;
            }

            // Build list of (Type, ImmediateParentFieldId) for switch cases
            var derivedCases = new List<(string Type, int FieldId)>();
            foreach (var derivedType in allDerivedTypes)
            {
                // Find immediate parent's ProtoInclude field for this derived type
                var parent = _registry.GetParent(derivedType);

                // If parent is current type, use direct ProtoInclude field
                // Otherwise, use parent's ProtoInclude field (for transitive derived)
                int fieldId;
                if (parent == type.FullName)
                {
                    // Direct child - use ProtoInclude from current type
                    var protoInclude = type.ProtoIncludes?.FirstOrDefault(p => p.Type == derivedType);
                    fieldId = protoInclude?.FieldId ?? 0;
                }
                else
                {
                    // Transitive child - find field ID that connects to immediate child of current type
                    // Walk up from derivedType until we find immediate child of current type
                    var current = derivedType;
                    fieldId = 0; // Initialize to avoid uninitialized variable error
                    while (current != null)
                    {
                        var currentParent = _registry.GetParent(current);
                        if (currentParent == type.FullName)
                        {
                            // current is immediate child of type
                            var protoInclude = type.ProtoIncludes?.FirstOrDefault(p => p.Type == current);
                            if (protoInclude != null)
                            {
                                fieldId = protoInclude.FieldId;
                            }
                            break;
                        }
                        current = currentParent;
                    }
                }

                derivedCases.Add((derivedType, fieldId));
            }

            // Sort by depth (most derived first) to ensure C is checked before B
            derivedCases = derivedCases
                .OrderByDescending(d =>
                {
                    var chain = _registry.GetInheritanceChain(d.Type);
                    return chain?.Count ?? 0;
                })
                .ToList();

            // Generate switch with all derived types
            _sb.AppendIndentedLine("switch (instance)");
            _sb.StartNewBlock();

            foreach (var (derivedType, fieldId) in derivedCases)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"case global::{derivedType} derived:");
                _sb.IncreaseIndent();

                // Call Write{Type}_As{CurrentType} method
                // For WriteA with derived C, call WriteC_AsA
                _sb.AppendIndentedLine($"Write{derivedClassName}_As{className}(ref writer, derived);");
                _sb.AppendIndentedLine("return;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();

            // Default case - write base type fields (when instance is exactly the base type, not derived)
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        /// <summary>
        /// Sorts ProtoIncludes from most derived to base to ensure proper type checking.
        /// Example: [C, B] so that "instance is C" is checked before "instance is B"
        /// </summary>
        private List<ProtoIncludeAttribute> SortProtoIncludesByDepth(List<ProtoIncludeAttribute> includes)
        {
            if (includes == null || includes.Count <= 1)
                return includes;

            // Sort by depth in inheritance chain (deepest first)
            var sorted = includes.OrderByDescending(inc =>
            {
                var chain = _registry.GetInheritanceChain(inc.Type);
                return chain?.Count ?? 0;
            }).ToList();

            return sorted;
        }

        private void GenerateWriteMethodForDerived(TypeDefinition type, string className)
        {
            // Phase 2: Correct ProtoInclude wrapper generation
            // Wire format: [wrapper tag][wrapper length][derived fields INSIDE][base fields AFTER]

            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count < 2)
            {
                // No inheritance - shouldn't happen for derived types, but handle gracefully
                GenerateWriteContentForDerivedType(type, className);
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

            // Step 1: Write outermost wrapper (tag + length + content)
            GenerateOutermostWrapper(inheritanceChain, type);

            // Step 2: Write base fields AFTER all wrappers
            if (rootType?.ProtoMembers != null)
            {
                _sb.AppendIndentedLine($"// Base class fields ({TypeNameHelper.GetClassName(rootTypeName)}) - AFTER wrapper");
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        #endregion

        #region Content Method

        /// <summary>
        /// Generates Write{ClassName}Content method for derived type field writing.
        /// </summary>
        public void GenerateContentMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static void Write{className}Content(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #endregion

        #region Field Generation

        /// <summary>
        /// Generates code to write a single field based on its type.
        /// </summary>
        private void GenerateFieldWrite(ProtoMemberAttribute member, string objectName)
        {
            string sourceVar = $"{objectName}.{member.Name}";

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                GenerateMapFieldWrite(member, sourceVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldWrite(member, sourceVar);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldWrite(member, sourceVar);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleWrite(member.FieldId, sourceVar, member.Type, _className);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateWrite(
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
                GenerateComplexTypeWrite(member, sourceVar);
            }
        }

        private void GenerateEnumFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            // protobuf-net 2.3.7 Level200: IsRequired on nullable → ignored, on non-nullable → always serialize
            if (member.IsNullable)
            {
                // Nullable enum: always use HasValue check (IsRequired ignored)
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Non-nullable enum + IsRequired: ALWAYS serialize (no condition)
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
            }
            else
            {
                // Non-nullable enum + NOT required: proto2 default value check
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                _sb.EndBlock();
            }
        }

        private void GenerateMapFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, _className, _registry);
            mapHandler.GenerateWrite(member, sourceVar);
        }

        private void GenerateCollectionFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
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
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionWrite(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType,
                    _className);
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionWrite(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType,
                    elementClassName,
                    _className);
            }
        }

        private void GenerateComplexTypeWrite(ProtoMemberAttribute member, string sourceVar)
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
                // CRITICAL: Field declared as concrete derived type (e.g., ModbusManualTransaction Transaction)
                // Must generate ProtoInclude wrapper for protobuf-net Level200 compatibility.
                // Wire format: [field tag][total length] [wrapper tag][wrapper length] [derived fields] [base fields]
                GenerateNestedDerivedTypeWrite(member, sourceVar, typeName, typeDef);
            }
            else if (isPolymorphicField)
            {
                // CRITICAL: Field declared as polymorphic base type (e.g., ProtoParameterBase ProtoValue)
                // Runtime instance may be derived type, requiring ProtoInclude wrapper.
                // Generate runtime type dispatch to check actual type and add wrapper if needed.
                GeneratePolymorphicFieldWrite(member, sourceVar, typeName, typeDef);
            }
            else
            {
                // Standard complex type write (no inheritance or base type with runtime dispatch)
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
            }

            if (!isNonNullableStruct)
            {
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Generates write code for nested derived type fields (concrete derived type declared in ProtoMember).
        /// Example: [ProtoMember(1)] ModbusManualTransaction Transaction (where ModbusManualTransaction : ModbusTransaction)
        /// Wire format: [field tag][total length] [wrapper tag][wrapper length] [derived fields] [base fields AFTER wrapper]
        /// </summary>
        private void GenerateNestedDerivedTypeWrite(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var calcVar = isNonNullableStruct ? $"calculator_{member.FieldId}" : "calculator";

            // Get parent type and ProtoInclude field ID
            var parentTypeFullName = _registry.GetParent(member.Type);
            var parentType = _registry.GetByFullName(parentTypeFullName);
            var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == member.Type);

            if (protoInclude == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type} in {parentTypeFullName}");
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
                return;
            }

            var parentTypeName = TypeNameHelper.GetClassName(parentTypeFullName);
            var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;

            _sb.AppendIndentedLine($"// ProtoInclude wrapper for nested derived type {typeName}");

            // Step 1: Calculate full size (wrapper + derived fields + base fields)
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendNewLine();

            // Calculate wrapper tag size
            _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32({wrapperTag}u);");

            // Calculate wrapper content size (derived-specific fields only)
            var wrapperContentCalcVar = isNonNullableStruct ? $"wrapperContent_{member.FieldId}" : "wrapperContentCalc";
            _sb.AppendIndentedLine($"var {wrapperContentCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            string sizeCalcNamespace = !string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace
                ? $"global::{typeNamespace}.Serialization."
                : "";

            _sb.AppendIndentedLine($"{sizeCalcNamespace}SizeCalculators.Calculate{typeName}OwnFieldsSize(ref {wrapperContentCalcVar}, {valueArg});");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){wrapperContentCalcVar}.Length);");
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength({wrapperContentCalcVar}.Length);");
            _sb.AppendNewLine();

            // Calculate base fields size (AFTER wrapper)
            // IMPORTANT: Use BaseFieldsOnlySize to avoid runtime dispatch that would count derived fields twice
            _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
            string parentNamespace = _registry.GetNamespaceForType(parentTypeFullName);
            string parentSizeCalcPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                ? $"global::{parentNamespace}.Serialization."
                : "";
            _sb.AppendIndentedLine($"{parentSizeCalcPrefix}SizeCalculators.Calculate{parentTypeName}BaseFieldsOnlySize(ref {calcVar}, {valueArg});");
            _sb.AppendNewLine();

            // Step 2: Write field tag and total length
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){calcVar}.Length);");
            _sb.AppendNewLine();

            // Step 3: Write ProtoInclude wrapper tag and length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32({wrapperTag}u);");
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){wrapperContentCalcVar}.Length);");
            _sb.AppendNewLine();

            // Step 4: Write derived-specific fields (inside wrapper)
            string writerNamespace = !string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace
                ? $"global::{typeNamespace}.Serialization."
                : "";
            _sb.AppendIndentedLine($"{writerNamespace}{_writerKind}Writers.Write{typeName}OwnFields(ref writer, {valueArg});");
            _sb.AppendNewLine();

            // Step 5: Write base fields (AFTER wrapper)
            // IMPORTANT: Use BaseFieldsOnly to avoid runtime dispatch that would duplicate derived fields
            string parentWriterPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                ? $"global::{parentNamespace}.Serialization."
                : "";
            _sb.AppendIndentedLine($"{parentWriterPrefix}{_writerKind}Writers.Write{parentTypeName}BaseFieldsOnly(ref writer, {valueArg});");
        }

        /// <summary>
        /// Generates standard write code for complex types (no nested derived type special handling).
        /// </summary>
        private void GenerateStandardComplexTypeWrite(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;
            var calcVar = isNonNullableStruct ? $"calculator_{member.FieldId}" : "calculator";
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // Calculate and write length
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // NOTE: For top-level serialization (Write{Type}), ProtoInclude wrapper is included in ContentSize.
            // For nested fields of non-derived types, no wrapper is needed.

            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.SizeCalculators.Calculate{typeName}ContentSize(ref {calcVar}, {valueArg});");
            }
            else
            {
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{typeName}ContentSize(ref {calcVar}, {valueArg});");
            }

            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){calcVar}.Length);");

            // Write content
            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.{_writerKind}Writers.Write{typeName}Content(ref writer, {valueArg});");
            }
            else
            {
                _sb.AppendIndentedLine($"Write{typeName}Content(ref writer, {valueArg});");
            }
        }

        /// <summary>
        /// Generates write code for polymorphic fields (field declared as base type with ProtoIncludes).
        /// Example: [ProtoMember(2)] ProtoParameterBase ProtoValue (where ProtoParameterBase has multiple ProtoIncludes)
        /// Generates runtime type dispatch to add ProtoInclude wrapper for derived type instances.
        /// </summary>
        private void GeneratePolymorphicFieldWrite(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            var allDerivedTypes = _registry.GetAllDerivedTypes(member.Type);
            if (allDerivedTypes == null || allDerivedTypes.Count == 0)
            {
                // No derived types - fallback to standard write
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
                return;
            }

            _sb.AppendIndentedLine($"// Polymorphic field: runtime type dispatch for ProtoInclude wrapper");

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

                // Step 1: Calculate total size (wrapper + derived fields + base fields)
                _sb.AppendIndentedLine($"var totalCalc{derivedClassName} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendNewLine();

                // Wrapper tag size
                _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.WriteVarUInt32({wrapperTag}u);");

                // Wrapper content size (derived fields only)
                _sb.AppendIndentedLine($"var wrapperCalc{derivedClassName} = new global::GProtobuf.Core.WriteSizeCalculator();");

                string derivedSizeCalcPrefix = !string.IsNullOrEmpty(derivedNamespace) && derivedNamespace != _currentNamespace
                    ? $"global::{derivedNamespace}.Serialization."
                    : "";

                _sb.AppendIndentedLine($"{derivedSizeCalcPrefix}SizeCalculators.Calculate{derivedClassName}OwnFieldsSize(ref wrapperCalc{derivedClassName}, derived{derivedClassName});");
                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.WriteVarUInt32((uint)wrapperCalc{derivedClassName}.Length);");
                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.AddByteLength(wrapperCalc{derivedClassName}.Length);");
                _sb.AppendNewLine();

                // Base fields size (AFTER wrapper)
                _sb.AppendIndentedLine($"// Base fields ({parentTypeName})");
                string parentNamespace = _registry.GetNamespaceForType(parentTypeFullName);
                string parentSizeCalcPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                    ? $"global::{parentNamespace}.Serialization."
                    : "";
                _sb.AppendIndentedLine($"{parentSizeCalcPrefix}SizeCalculators.Calculate{parentTypeName}BaseFieldsOnlySize(ref totalCalc{derivedClassName}, derived{derivedClassName});");
                _sb.AppendNewLine();

                // Step 2: Write field tag and total length
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)totalCalc{derivedClassName}.Length);");
                _sb.AppendNewLine();

                // Step 3: Write ProtoInclude wrapper tag and length
                _sb.AppendIndentedLine($"writer.WriteVarUInt32({wrapperTag}u);");
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)wrapperCalc{derivedClassName}.Length);");
                _sb.AppendNewLine();

                // Step 4: Write derived fields (inside wrapper)
                string derivedWriterPrefix = !string.IsNullOrEmpty(derivedNamespace) && derivedNamespace != _currentNamespace
                    ? $"global::{derivedNamespace}.Serialization."
                    : "";
                _sb.AppendIndentedLine($"{derivedWriterPrefix}{_writerKind}Writers.Write{derivedClassName}OwnFields(ref writer, derived{derivedClassName});");
                _sb.AppendNewLine();

                // Step 5: Write base fields (AFTER wrapper)
                string parentWriterPrefix = !string.IsNullOrEmpty(parentNamespace) && parentNamespace != _currentNamespace
                    ? $"global::{parentNamespace}.Serialization."
                    : "";
                _sb.AppendIndentedLine($"{parentWriterPrefix}{_writerKind}Writers.Write{parentTypeName}BaseFieldsOnly(ref writer, derived{derivedClassName});");

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

            // Generate write code if base type has own fields, otherwise write empty message
            if (typeDef != null && typeDef.ProtoMembers != null && typeDef.ProtoMembers.Count > 0)
            {
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
            }
            else
            {
                // Base type has no fields - write empty message
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);
                _sb.AppendIndentedLine("writer.WriteVarUInt32(0u); // Empty base type message");
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock(); // End switch
        }

        /// <summary>
        /// Generates code to calculate size of a single field into a specified calculator variable.
        /// Similar to GenerateFieldWrite but for size calculation.
        /// </summary>
        private void GenerateFieldSizeCalculation(ProtoMemberAttribute member, string objectName, string calculatorVar)
        {
            string sourceVar = $"{objectName}.{member.Name}";

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                GenerateMapFieldSizeCalculation(member, sourceVar, calculatorVar);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldSizeCalculation(member, sourceVar, calculatorVar);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldSizeCalculation(member, sourceVar, calculatorVar);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleSize(member.FieldId, sourceVar, member.Type, calculatorVar);
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
                    member.IsRequired,
                    calculatorVar);
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
                GenerateComplexTypeSizeCalculation(member, sourceVar, calculatorVar);
            }
        }

        private void GenerateEnumFieldSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
        {
            // protobuf-net 2.3.7 Level200: IsRequired on nullable → ignored, on non-nullable → always serialize
            if (member.IsNullable)
            {
                // Nullable enum: always use HasValue check (IsRequired ignored)
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt, calculatorVar);
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else if (member.IsRequired)
            {
                // Non-nullable enum + IsRequired: ALWAYS calculate size (no condition)
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt, calculatorVar);
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int){sourceVar});");
            }
            else
            {
                // Non-nullable enum + NOT required: proto2 default value check
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt, calculatorVar);
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int){sourceVar});");
                _sb.EndBlock();
            }
        }

        private void GenerateMapFieldSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, _className, _registry);
            mapHandler.GenerateSize(member, sourceVar, calculatorVar);
        }

        private void GenerateCollectionFieldSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
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
                        member.FieldId,
                        calculatorVar);
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArraySize(
                        _sb,
                        sourceVar,
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        calculatorVar);
                }
            }
            else if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                // Tuple collection - generate inline
                _tupleHandler.GenerateTupleCollectionSize(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType,
                    calculatorVar);
            }
            else
            {
                // Complex type collection
                var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
                _collectionHandler.GenerateComplexCollectionSize(
                    member.FieldId,
                    sourceVar,
                    member.CollectionElementType,
                    elementClassName,
                    calculatorVar);
            }
        }

        private void GenerateComplexTypeSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);

            // Check if type is a non-nullable value type (struct)
            var typeDef = _registry.GetByFullName(member.Type);
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;

            // Only generate null check for reference types or nullable value types
            if (!isNonNullableStruct)
            {
                _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                _sb.StartNewBlock();
            }

            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len, calculatorVar);

            // Calculate content size first
            // Use unique variable names to avoid conflicts when multiple fields in same scope
            var lengthVar = isNonNullableStruct ? $"lengthBefore_{member.FieldId}" : "lengthBefore";
            var contentLengthVar = isNonNullableStruct ? $"contentLength_{member.FieldId}" : "contentLength";

            // For nullable enums or structs, use .Value to get the underlying value
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);

            _sb.AppendIndentedLine($"var {lengthVar} = {calculatorVar}.Length;");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{typeName}ContentSize(ref {calculatorVar}, {valueArg});");
            _sb.AppendIndentedLine($"var {contentLengthVar} = {calculatorVar}.Length - {lengthVar};");
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint){contentLengthVar});");

            if (!isNonNullableStruct)
            {
                _sb.EndBlock();
            }
        }

        /// <summary>
        /// Gets the correct value access for nullable types.
        /// For nullable enums or structs, returns sourceVar.Value, otherwise returns sourceVar as-is.
        /// </summary>
        private string GetNullableValueAccess(string sourceVar, ProtoMemberAttribute member, TypeDefinition typeDef)
        {
            // If member.IsNullable = true in context of GenerateComplexTypeWrite,
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
        /// Generates code to calculate ProtoInclude wrapper size for derived types.
        /// protobuf-net Level200 always adds wrapper for derived types, even in direct fields.
        /// </summary>
        private void GenerateProtoIncludeWrapperSize(string derivedTypeName, string calculatorVar)
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
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32({wrapperTag}u);");
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32(0); // Empty wrapper marker");
            }
        }

        /// <summary>
        /// Generates code to write ProtoInclude wrapper for derived types.
        /// protobuf-net Level200 always adds wrapper for derived types, even in direct fields.
        /// </summary>
        private void GenerateProtoIncludeWrapperWrite(string derivedTypeName)
        {
            var inheritanceChain = _registry.GetInheritanceChain(derivedTypeName);
            if (inheritanceChain.Count < 2) return;

            var parentTypeName = inheritanceChain[0];
            var parentType = _registry.GetByFullName(parentTypeName);
            var protoInclude = GeneratorHelpers.FindProtoInclude(parentType, derivedTypeName);

            if (protoInclude != null)
            {
                var wrapperTag = (protoInclude.FieldId << 3) | (int)WireType.Len;
                _sb.AppendIndentedLine($"writer.WriteVarUInt32({wrapperTag}u); // ProtoInclude wrapper tag");
                _sb.AppendIndentedLine("writer.WriteVarUInt32(0); // Empty wrapper marker");
            }
        }

        #endregion

    }
}
