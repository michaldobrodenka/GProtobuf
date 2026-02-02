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
            }

            // Generate WriteContent methods for ProtoInclude derived types
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

            // Generate WriteContent methods for ProtoInclude types
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

            // Generate Write{Type}_As{Parent} methods for polymorphic serialization
            if (_registry.IsDerivedType(type.FullName))
            {
                GenerateWriteAsParentMethods(type, className);
            }
        }

        /// <summary>
        /// Generates WriteXxxContent method that writes ONLY THIS TYPE'S OWN FIELDS.
        /// NO parent fields, NO wrappers, NO nested types.
        ///
        /// Examples:
        /// - WriteAContent writes StringA
        /// - WriteBContent writes StringB (not StringA)
        /// - WriteCContent writes StringC (not StringA, not StringB)
        ///
        /// Wrapper generation is handled by Write{Type}_As{Parent} methods.
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

            // Write ONLY own fields (not inherited fields)
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
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}ContentSize(ref {calcVar}, instance);");
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
                _sb.AppendIndentedLine($"Write{currentClassName}Content(ref writer, instance);");
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
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}ContentSize(ref {calcVar}, instance);");
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
            // CRITICAL: protobuf-net Level200 wire format requires ProtoInclude field FIRST, then base class fields
            // This is opposite to what you might expect, but it's required for compatibility.
            // Example: MessageDeleteDevicesRequest : MessageBase
            //   protobuf-net wire: [ProtoInclude field 380][base RequestsId field 1][own fields]

            // Step 1: Write ProtoInclude wrappers FIRST
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count >= 2)
            {
                GenerateProtoIncludeWrappers(type, inheritanceChain);
            }

            // Step 2: THEN write base class fields AFTER ProtoInclude
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootType = _registry.GetByFullName(rootTypeName);
            if (rootType?.ProtoMembers != null)
            {
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        private void GenerateProtoIncludeWrappers(TypeDefinition type, IReadOnlyList<string> inheritanceChain)
        {
            // For arbitrary depth: [A, B, C, D, ...]
            // Generate nested wrappers from level 0 to the derived type
            // First calculate all sizes, then write nested wrappers with fields

            if (inheritanceChain.Count < 2)
                return;

            // Generate the outermost wrapper (from root to first derived)
            GenerateNestedWrapper(inheritanceChain, 0);
        }

        /// <summary>
        /// Recursively generates nested ProtoInclude wrappers for the inheritance chain.
        /// </summary>
        private void GenerateNestedWrapper(IReadOnlyList<string> chain, int levelIndex)
        {
            if (levelIndex >= chain.Count - 1)
                return;

            var currentTypeName = chain[levelIndex];
            var nextTypeName = chain[levelIndex + 1];
            var currentType = _registry.GetByFullName(currentTypeName);
            var nextType = _registry.GetByFullName(nextTypeName);
            var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Write tag for this wrapper
            TagCodeHelper.WriteTag(_sb, protoInclude.FieldId, WireType.Len);

            // Calculate size for this wrapper (includes nested wrappers + this level's fields)
            var calcVar = $"calc{levelIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Calculate size for all nested content (from deepest level back up)
            GenerateNestedSizeCalculation(chain, levelIndex, levelIndex + 1, calcVar);

            // Write the length
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){calcVar}.Length);");

            // Write this level's (nextType's) fields first
            if (nextType?.ProtoMembers != null)
            {
                foreach (var member in nextType.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }

            // Then recursively write next wrapper (if any)
            if (levelIndex + 2 < chain.Count)
            {
                GenerateNestedWrapper(chain, levelIndex + 1);
            }
        }

        /// <summary>
        /// Generates size calculation for nested wrappers from the given level to the deepest.
        /// </summary>
        private void GenerateNestedSizeCalculation(IReadOnlyList<string> chain, int startLevel, int levelIndex, string calcVar)
        {
            var typeName = chain[levelIndex];
            var type = _registry.GetByFullName(typeName);
            var className = TypeNameHelper.GetClassName(typeName);

            // Add this level's content size
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref {calcVar}, instance);");

            // If there's a next level, add wrapper overhead
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var protoInclude = GeneratorHelpers.FindProtoInclude(type, nextTypeName);

                if (protoInclude != null)
                {
                    // Add tag size for next wrapper
                    TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested size with unique variable name (includes start level to avoid conflicts)
                    var nestedCalcVar = $"sn{startLevel}_{levelIndex}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursively calculate nested content
                    GenerateNestedSizeCalculation(chain, startLevel, levelIndex + 1, nestedCalcVar);

                    // Add length prefix size and content size
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
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
                    member.IsNullable);
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
            if (member.IsNullable)
            {
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else
            {
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

            // Only generate null check for reference types or nullable value types
            if (!isNonNullableStruct)
            {
                _sb.AppendIndentedLine($"if ({sourceVar} != null)");
                _sb.StartNewBlock();
            }

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // Calculate and write length
            // Use unique variable names to avoid conflicts when multiple fields in same scope
            var calcVar = isNonNullableStruct ? $"calculator_{member.FieldId}" : "calculator";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Check if type is from different namespace and qualify the call
            var typeNamespace = _registry.GetNamespaceForType(member.Type);

            // For nullable enums or structs, use .Value to get the underlying value
            string valueArg = GetNullableValueAccess(sourceVar, member, typeDef);

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
                _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.{_className}.Write{typeName}Content(ref writer, {valueArg});");
            }
            else
            {
                _sb.AppendIndentedLine($"Write{typeName}Content(ref writer, {valueArg});");
            }

            if (!isNonNullableStruct)
            {
                _sb.EndBlock();
            }
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
            if (member.IsNullable)
            {
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt, calculatorVar);
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else
            {
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

        #endregion

    }
}
