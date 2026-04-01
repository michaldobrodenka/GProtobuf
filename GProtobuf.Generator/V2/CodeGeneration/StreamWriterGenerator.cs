using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.V2.CodeGeneration.Core;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;
using GProtobuf.Generator.WireFormat;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates StreamWriters class with Write{ClassName} methods.
    /// Handles serialization from objects to StreamWriter.
    /// </summary>
    internal class StreamWriterGenerator : GeneratorBase
    {
        private readonly string _writerType;
        private readonly string _className;
        private readonly string _writerKind;

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null, null, "Stream")
        {
        }

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : this(sb, registry, virtualMapRegistry, null, "Stream")
        {
        }

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : this(sb, registry, virtualMapRegistry, virtualTupleRegistry, "Stream", null)
        {
        }

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string virtualTypesNamespace)
            : this(sb, registry, virtualMapRegistry, virtualTupleRegistry, "Stream", virtualTypesNamespace)
        {
        }

        protected StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry, string writerKind, string virtualTypesNamespace = null)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true, options: null, virtualTypesNamespace: virtualTypesNamespace)
        {
            _writerKind = writerKind;
            _writerType = $"global::GProtobuf.Core.{writerKind}Writer";
            _className = $"{writerKind}Writers";
        }

        /// <summary>
        /// Generates Writers class containing ONLY virtual types (map entries and tuples).
        /// Used for GProtobuf.Generated namespace which centralizes all virtual type methods.
        /// </summary>
        public void GenerateVirtualTypesOnly(string currentNamespace)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {_className}");
            _sb.StartNewBlock();

            // Generate virtual map entry writers (all types, ignoring IsGenerated flag)
            GenerateVirtualMapEntryWriters(ignoreIsGeneratedFlag: true);

            // Generate virtual tuple writers (all types, ignoring IsGenerated flag)
            GenerateVirtualTupleWriters(ignoreIsGeneratedFlag: true);

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates complete Writers class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            // Store current namespace for cross-namespace method calls
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {_className}");
            _sb.StartNewBlock();

            // Generate dictionary-based type dispatch for large type hierarchies
            var typesList = types.ToList();
            GenerateTypeDispatchDictionaries(typesList);

            foreach (var type in typesList)
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
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

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
            _sb.AppendIndentedLine("// Virtual Map Entry Writers");

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry, _registry, _writerKind, _virtualTypesNamespace);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateWriter(virtualType);
            }

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
            _sb.AppendIndentedLine("// Virtual Tuple Writers");

            var generator = new VirtualTupleGenerator(_sb, _writerKind, _registry, _virtualTypesNamespace);
            foreach (var tupleInfo in tupleTypes)
            {
                generator.GenerateWriter(tupleInfo);
            }
        }

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
                    var allDerivedTypes = _registry.GetAllDerivedTypes(type.FullName);
                    if (allDerivedTypes != null && allDerivedTypes.Count >= DictionaryDispatchThreshold)
                    {
                        // Sort by depth (most derived first)
                        var sortedDerived = allDerivedTypes
                            .OrderByDescending(d =>
                            {
                                var chain = _registry.GetInheritanceChain(d);
                                return chain?.Count ?? 0;
                            })
                            .ToList();

                        var className = TypeNameHelper.GetClassName(type.FullName);
                        if (!generatedDictionaries.Contains(className))
                        {
                            TryGenerateFunctionPointerDispatch(
                                className,
                                type.FullName,
                                _writerType,
                                "writer",
                                sortedDerived,
                                (derivedType, derivedClassName, castVar) =>
                                {
                                    _sb.AppendIndentedLine($"Write{derivedClassName}_As{className}(ref writer, {castVar});");
                                });
                            generatedDictionaries.Add(className);
                        }
                    }
                }
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

            // Add null check for reference types (structs can't be null)
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

            // Add null check for reference types (structs and enums can't be null)
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

            // For custom collection types (implements IEnumerable<T> + Add(T))
            if (type.IsCustomCollection && !string.IsNullOrEmpty(type.CustomCollectionElementType))
            {
                GenerateCustomCollectionWriteContent(type, className);
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

                // Write custom buffer fields
                if (type.CustomBufferMembers != null)
                {
                    foreach (var customMember in type.CustomBufferMembers)
                    {
                        GenerateCustomBufferFieldWrite(customMember, "instance");
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

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (instance == null) return;");
            }

            // Write ONLY the fields defined in this base class, no switch/dispatch
            ForEachProtoMember(type.ProtoMembers, "instance", (member, src) => GenerateFieldWrite(member, src));
            ForEachCustomBufferMember(type.CustomBufferMembers, "instance", (member, src) => GenerateCustomBufferFieldWrite(member, src));

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates WriteContent for custom collection types.
        /// Writes each element with field id 1.
        /// </summary>
        private void GenerateCustomCollectionWriteContent(TypeDefinition type, string className)
        {
            var elementType = type.CustomCollectionElementType;
            var globalElementType = TypeMapping.GetGlobalTypeName(elementType);

            bool isSimpleType = TypeMapping.IsSimpleType(elementType);
            bool isEnum = _registry.IsEnum(elementType);

            _sb.AppendIndentedLine("foreach (var item in instance)");
            _sb.StartNewBlock();

            if (isSimpleType)
            {
                // Write tag and value using TagCodeHelper
                TagCodeHelper.WriteTag(_sb, 1, TypeMapping.GetWireType(elementType, DataFormat.Default));
                var writeExpr = TypeMapping.GetWriteExpression(elementType, "item", DataFormat.Default, "writer");
                _sb.AppendIndentedLine($"{writeExpr};");
            }
            else if (isEnum)
            {
                // Enums are VarInt with field id 1
                _sb.AppendIndentedLine("writer.WriteSingleByte(0x08);"); // Field 1, VarInt
                _sb.AppendIndentedLine("writer.WriteVarInt32((int)item);");
            }
            else if (_registry.IsProtoVarint(TypeMapping.NormalizeTypeName(elementType)))
            {
                var varintType = _registry.GetProtoVarintType(TypeMapping.NormalizeTypeName(elementType)) ?? ProtoVarintType.UInt32;
                var valueMember = _registry.GetProtoVarintValueMember(TypeMapping.NormalizeTypeName(elementType));
                // ProtoVarint type - write as VarInt with field id 1
                _sb.AppendIndentedLine("writer.WriteSingleByte(0x08);"); // Field 1, VarInt
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(varintType);
                _sb.AppendIndentedLine($"writer.{writeMethod}(item.{valueMember});");
            }
            else
            {
                // Complex type - write as length-delimited message
                var elementClassName = TypeNameHelper.GetClassName(elementType);
                var elementNs = _registry.GetNamespaceForType(elementType);

                _sb.AppendIndentedLine("writer.WriteSingleByte(0x0A);"); // Field 1, LengthDelimited

                // Calculate size and write
                var sizeCalcNs = string.IsNullOrEmpty(elementNs) || elementNs == _currentNamespace
                    ? "SizeCalculators"
                    : $"global::{elementNs}.Serialization.SizeCalculators";
                var writeNs = string.IsNullOrEmpty(elementNs) || elementNs == _currentNamespace
                    ? $"Write{elementClassName}Content"
                    : $"global::{elementNs}.Serialization.{_className}.Write{elementClassName}Content";

                _sb.AppendIndentedLine("var sizeCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"{sizeCalcNs}.Calculate{elementClassName}ContentSize(ref sizeCalc, item);");
                _sb.AppendIndentedLine("writer.WriteVarInt32(sizeCalc.Length);");
                _sb.AppendIndentedLine($"{writeNs}(ref writer, item);");
            }

            _sb.EndBlock();
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
                ForEachProtoMember(type.ProtoMembers, "instance", (member, src) => GenerateFieldWrite(member, src));
                return;
            }

            // Type dispatch for polymorphic types:
            // For derived types: call Write{DerivedClassName}_As{BaseClassName} which handles:
            //   1. ProtoInclude wrapper tag + length
            //   2. Derived-specific fields inside the wrapper
            //   3. Base fields OUTSIDE the wrapper
            // For base type instance: just write base fields directly
            _sb.AppendIndentedLine("switch (instance)");
            _sb.StartNewBlock();

            foreach (var derivedType in sortedDerived)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"case global::{derivedType} derived:");
                _sb.IncreaseIndent();
                // Call Write{DerivedClassName}_As{BaseClassName} which includes ProtoInclude wrapper
                _sb.AppendIndentedLine($"Write{derivedClassName}_As{className}(ref writer, derived);");
                _sb.AppendIndentedLine("return;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();

            // Default case - base type instance (not a derived type)
            // Just write base type fields directly
            ForEachProtoMember(type.ProtoMembers, "instance", (member, src) => GenerateFieldWrite(member, src));
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
            var protoInclude = GeneratorHelpers.FindProtoInclude(baseType, firstDerivedTypeName);

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
        /// NOTE: Base class fields are written OUTSIDE the wrapper (at parent message level).
        /// ProtoInclude wrapper is calculated FIRST, then own fields.
        /// </summary>
        private void CalculateWrapperContentSizeForWrite(IReadOnlyList<string> chain, int levelIndex, string calcVar)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // If there's a next level, add nested wrapper size first
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Calculate nested wrapper for {nextClassName} (ProtoInclude first)");

                    // Add nested wrapper tag size
                    TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len, calcVar);

                    // Calculate nested content size - use unique counter instead of levelIndex
                    var nestedCalcVar = GetNextNestedCalcVar();
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Recursive call for nested level
                    CalculateWrapperContentSizeForWrite(chain, levelIndex + 1, nestedCalcVar);

                    // Add length prefix size and content size to parent calculator
                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }
            }

            // Add this level's OWN fields size AFTER nested wrapper (not base fields)
            _sb.AppendIndentedLine($"// Calculate {currentClassName}'s own fields size (after ProtoInclude)");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{currentClassName}OwnFieldsSize(ref {calcVar}, instance);");
            // NOTE: Base class fields are NOT included here - they're written OUTSIDE the wrapper
        }

        /// <summary>
        /// Recursively writes wrapper content (nested wrappers + own fields).
        /// NOTE: Base class fields are written OUTSIDE the wrapper (at parent message level).
        /// ProtoInclude wrapper is written FIRST, then own fields.
        /// </summary>
        private void WriteWrapperContentRecursive(IReadOnlyList<string> chain, int levelIndex)
        {
            if (levelIndex >= chain.Count) return;

            var currentTypeName = chain[levelIndex];
            var currentClassName = TypeNameHelper.GetClassName(currentTypeName);
            var currentType = _registry.GetByFullName(currentTypeName);

            // If there's a next level, write nested wrapper first
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    _sb.AppendIndentedLine($"// Write nested wrapper for {nextClassName} (ProtoInclude first)");

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

            // Write this level's OWN fields AFTER nested wrapper (not base fields)
            _sb.AppendIndentedLine($"// Write {currentClassName}'s own fields (after ProtoInclude)");
            _sb.AppendIndentedLine($"Write{currentClassName}OwnFields(ref writer, instance);");
            // NOTE: Base class fields are NOT written here - they're written OUTSIDE the wrapper
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

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (instance == null) return;");
            }

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
            // Write root type fields (base class fields) using helper method
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootClassName = TypeNameHelper.GetClassName(rootTypeName);
            var rootNamespace = _registry.GetNamespaceForType(rootTypeName);
            var rootNsPrefix = GeneratorHelpers.GetNamespacePrefix(rootNamespace, _currentNamespace);

            _sb.AppendIndentedLine($"// Base class fields ({rootClassName})");
            _sb.AppendIndentedLine($"{rootNsPrefix}{_className}.Write{rootClassName}BaseFieldsOnly(ref writer, instance);");

            // Write own fields (if not root) using helper method
            if (type.FullName != rootTypeName)
            {
                var typeNamespace = _registry.GetNamespaceForType(type.FullName);
                var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

                _sb.AppendIndentedLine($"// Own fields ({className})");
                _sb.AppendIndentedLine($"{typeNsPrefix}{_className}.Write{className}OwnFields(ref writer, instance);");
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
        /// NOTE: For protobuf-net compatibility, ancestor fields are written OUTSIDE the wrapper.
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

            // Add null check for reference types
            if (!type.IsStruct)
            {
                _sb.AppendIndentedLine("if (instance == null) return;");
            }

            // protobuf-net wire format:
            // 1. ProtoInclude wrapper(s) containing ONLY derived fields
            // 2. Ancestor fields written OUTSIDE the wrapper

            // Generate nested ProtoInclude wrappers (derived fields only)
            GenerateNestedWrappersForAsParent(inheritanceChain, ancestorIndex + 1, typeIndex, ancestorIndex);

            // Write ancestor fields OUTSIDE the wrapper
            var ancestorType = _registry.GetByFullName(ancestorTypeName);
            if (ancestorType?.ProtoMembers != null && ancestorType.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine($"// Ancestor fields ({ancestorClassName}) - OUTSIDE wrapper");
                _sb.AppendIndentedLine($"Write{ancestorClassName}BaseFieldsOnly(ref writer, instance);");
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates nested wrappers from currentIndex to targetIndex.
        /// Calculates all sizes once (innermost first), then writes using pre-calculated sizes.
        /// This avoids duplicate size calculations that occurred with the recursive approach.
        /// NOTE: Ancestor fields are written OUTSIDE the wrapper by the caller.
        /// </summary>
        private void GenerateNestedWrappersForAsParent(
            IReadOnlyList<string> chain,
            int currentIndex,
            int targetIndex,
            int ancestorIndex)
        {
            if (currentIndex > targetIndex)
                return;

            // Collect wrapper info for all levels
            var wrapperInfos = new List<(string typeName, string className, string calcVar, int protoIncludeFieldId)>();

            for (int i = currentIndex; i <= targetIndex; i++)
            {
                var typeName = chain[i];
                var className = TypeNameHelper.GetClassName(typeName);
                var parentTypeName = chain[i - 1];
                var parentType = _registry.GetByFullName(parentTypeName);
                var protoInclude = parentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == typeName);
                if (protoInclude == null)
                    return;

                wrapperInfos.Add((typeName, className, $"sz{i}", protoInclude.FieldId));
            }

            // Calculate ALL sizes (innermost first to avoid recalculation)
            // Innermost level - just own fields
            var innermost = wrapperInfos[wrapperInfos.Count - 1];
            _sb.AppendIndentedLine($"var {innermost.calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{innermost.className}OwnFieldsSize(ref {innermost.calcVar}, instance);");

            // Outer wrappers (from second-innermost to outermost)
            for (int i = wrapperInfos.Count - 2; i >= 0; i--)
            {
                var info = wrapperInfos[i];
                var innerInfo = wrapperInfos[i + 1];

                _sb.AppendIndentedLine($"var {info.calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                // Add nested wrapper: tag + length prefix + content
                TagCodeHelper.AddTagSize(_sb, innerInfo.protoIncludeFieldId, WireType.Len, info.calcVar);
                _sb.AppendIndentedLine($"{info.calcVar}.WriteVarUInt32((uint){innerInfo.calcVar}.Length);");
                _sb.AppendIndentedLine($"{info.calcVar}.AddByteLength({innerInfo.calcVar}.Length);");

                // Add current level's OWN fields
                var ownMembers = _registry.GetOwnProtoMembers(info.typeName);
                if (ownMembers != null && ownMembers.Count > 0)
                {
                    foreach (var member in ownMembers)
                    {
                        GenerateFieldSizeCalculation(member, "instance", info.calcVar);
                    }
                }
            }

            // Write all wrappers (outermost first)
            for (int i = 0; i < wrapperInfos.Count; i++)
            {
                var info = wrapperInfos[i];

                // Write tag
                TagCodeHelper.WriteTag(_sb, info.protoIncludeFieldId, WireType.Len);

                // Write length prefix using pre-calculated size
                _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){info.calcVar}.Length);");
            }

            // Write actual content (innermost first for correct wire format)
            for (int i = wrapperInfos.Count - 1; i >= 0; i--)
            {
                var info = wrapperInfos[i];
                _sb.AppendIndentedLine($"Write{info.className}OwnFields(ref writer, instance);");
            }
            // NOTE: Ancestor fields are NOT written here - they're written OUTSIDE the wrapper by the caller
        }

        /// <summary>
        /// Recursively calculates size for nested wrappers.
        /// NOTE: Ancestor fields are NOT included in wrapper - they're calculated separately outside.
        /// ProtoInclude wrapper is calculated FIRST, then own fields.
        /// </summary>
        private void GenerateNestedSizeCalculationForAsParent(
            IReadOnlyList<string> chain,
            int currentIndex,
            int targetIndex,
            string calcVar,
            int ancestorIndex)
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
                // Intermediate level - nested wrapper first, then OWN fields
                var currentType = _registry.GetByFullName(currentTypeName);

                // Add nested wrapper first
                var nextTypeName = chain[currentIndex + 1];
                var nextProtoInclude = currentType?.ProtoIncludes?.FirstOrDefault(p => p.Type == nextTypeName);
                if (nextProtoInclude != null)
                {
                    TagCodeHelper.AddTagSize(_sb, nextProtoInclude.FieldId, WireType.Len, calcVar);

                    var nestedCalcVar = $"n{currentIndex}";
                    _sb.AppendIndentedLine($"var {nestedCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    GenerateNestedSizeCalculationForAsParent(chain, currentIndex + 1, targetIndex, nestedCalcVar, ancestorIndex);

                    _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){nestedCalcVar}.Length);");
                    _sb.AppendIndentedLine($"{calcVar}.AddByteLength({nestedCalcVar}.Length);");
                }

                // Calculate current level own fields after nested wrapper
                var ownMembers = _registry.GetOwnProtoMembers(currentTypeName);
                if (ownMembers != null && ownMembers.Count > 0)
                {
                    foreach (var member in ownMembers)
                    {
                        GenerateFieldSizeCalculation(member, "instance", calcVar);
                    }
                }
            }
            // NOTE: Ancestor fields are NOT included here - they're calculated separately outside the wrapper
        }

        private void GenerateSimpleWriteMethod(TypeDefinition type, string className)
        {
            // Delegate to WriteXContent to avoid code duplication
            // For simple types (no ProtoIncludes, not derived), WriteX and WriteXContent are identical
            _sb.AppendIndentedLine($"Write{className}Content(ref writer, instance);");
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
                // No derived types - just write base fields directly using helper method
                _sb.AppendIndentedLine($"Write{className}BaseFieldsOnly(ref writer, instance);");
                return;
            }

            // Sort by depth (most derived first) to ensure C is checked before B
            var sortedDerived = allDerivedTypes
                .OrderByDescending(d =>
                {
                    var chain = _registry.GetInheritanceChain(d);
                    return chain?.Count ?? 0;
                })
                .ToList();

            // Use function pointer dispatch for large type hierarchies (O(1) lookup + direct call)
            if (sortedDerived.Count >= DictionaryDispatchThreshold)
            {
                GenerateFunctionPointerCall(className, type.FullName, _writerType, "writer", "instance", sortedDerived.Count);
            }
            else
            {
                // Generate switch with all derived types (standard pattern for small hierarchies)
                _sb.AppendIndentedLine("switch (instance)");
                _sb.StartNewBlock();

                foreach (var derivedType in sortedDerived)
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

            // Default case - write base type fields (when instance is exactly the base type, not derived)
            _sb.AppendIndentedLine($"// Base type instance - write base fields only");
            _sb.AppendIndentedLine($"Write{className}BaseFieldsOnly(ref writer, instance);");
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
            // ProtoInclude wrapper generation with protobuf-net compatibility
            // Wire format: [wrapper tag][wrapper length][derived fields ONLY][base fields OUTSIDE wrapper]
            // NOTE: Base class fields are written AFTER the wrapper, at the parent message level

            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count < 2)
            {
                // No inheritance - shouldn't happen for derived types, but handle gracefully
                GenerateWriteContentForDerivedType(type, className);
                return;
            }

            // For single-level derived types (chain length == 2), WriteX and WriteX_AsParent
            // are identical in logic — delegate to avoid duplication
            if (inheritanceChain.Count == 2)
            {
                _sb.AppendIndentedLine($"Write{className}_As{TypeNameHelper.GetClassName(inheritanceChain[0])}(ref writer, instance);");
                return;
            }

            // Reset nested calculator counter for this method
            ResetNestedCalcCounter();

            _sb.AppendIndentedLine($"// ProtoInclude wrapper format (protobuf-net compatible)");
            _sb.AppendIndentedLine($"// Wire: [wrapper tag][length][derived fields ONLY] then [base fields OUTSIDE wrapper]");
            _sb.AppendNewLine();

            // Write outermost wrapper (tag + length + derived fields only)
            GenerateOutermostWrapper(inheritanceChain, type);

            // Write base class fields OUTSIDE the wrapper (at parent message level)
            var baseTypeName = inheritanceChain[0];
            var baseClassName = TypeNameHelper.GetClassName(baseTypeName);
            var baseType = _registry.GetByFullName(baseTypeName);
            if (baseType?.ProtoMembers != null && baseType.ProtoMembers.Count > 0)
            {
                _sb.AppendIndentedLine($"// Base class fields ({baseClassName}) - OUTSIDE wrapper");
                _sb.AppendIndentedLine($"Write{baseClassName}BaseFieldsOnly(ref writer, instance);");
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

        #region Custom Buffer Field Generation

        /// <summary>
        /// Generates code to write a custom buffer field.
        /// Custom buffer fields use user-defined methods for size calculation and buffer filling.
        /// </summary>
        private void GenerateCustomBufferFieldWrite(CustomBufferMember member, string objectName)
        {
            _sb.AppendIndentedLine($"// Custom buffer field {member.FieldId}");

            // Write tag (field ID + WireType.Len)
            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // Call user's size method to get the size
            _sb.AppendIndentedLine($"var customSize_{member.FieldId} = {objectName}.{member.SizeMethodName}();");

            // Write length prefix
            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)customSize_{member.FieldId});");

            // Get buffer and call user's fill method
            _sb.AppendIndentedLine($"var customBuffer_{member.FieldId} = writer.GetSpan(customSize_{member.FieldId});");
            _sb.AppendIndentedLine($"{objectName}.{member.FillMethodName}(customBuffer_{member.FieldId});");
            _sb.AppendIndentedLine($"writer.Advance(customSize_{member.FieldId});");
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
                    _tupleHandler.GenerateTupleWrite(member.FieldId, sourceVar, member.Type, _className);
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
                    _sb.AppendIndentedLine($"// ⚠️ WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported and will be skipped");
                    _sb.AppendIndentedLine($"// Unsupported types: System.Type (reflection metadata cannot be serialized)");
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
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, _className, _registry, _virtualTypesNamespace);
            mapHandler.GenerateWrite(member, sourceVar);
        }

        private void GenerateCollectionFieldWrite(ProtoMemberAttribute member, string sourceVar)
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

            string localVar = sourceVar;

            if (!isNonNullableStruct)
            {
                _sb.StartNewBlock(); // Scope block to avoid name collisions
                _sb.AppendIndentedLine($"var complexValue = {sourceVar};");
                _sb.AppendIndentedLine("if (complexValue != null)");
                _sb.StartNewBlock();
                localVar = "complexValue";
            }

            if (isNestedDerivedType)
            {
                GenerateNestedDerivedTypeWrite(member, localVar, typeName, typeDef);
            }
            else if (isPolymorphicField)
            {
                GeneratePolymorphicFieldWrite(member, localVar, typeName, typeDef);
            }
            else
            {
                GenerateStandardComplexTypeWrite(member, localVar, typeName, typeDef);
            }

            if (!isNonNullableStruct)
            {
                _sb.EndBlock(); // if
                _sb.EndBlock(); // scope
            }
        }

        /// <summary>
        /// Generates write code for nested derived type fields (concrete derived type declared in ProtoMember).
        /// Example: [ProtoMember(1)] ModbusManualTransaction Transaction (where ModbusManualTransaction : ModbusTransaction)
        /// Wire format: [field tag][total length] [wrapper tag][wrapper length][derived fields only][base fields OUTSIDE wrapper]
        /// NOTE: Base fields are written OUTSIDE the wrapper for protobuf-net compatibility.
        /// </summary>
        private void GenerateNestedDerivedTypeWrite(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            bool isNonNullableStruct = typeDef != null && typeDef.IsStruct && !member.IsNullable;
            string valueArg = GeneratorHelpers.GetNullableValueAccess(sourceVar, member, typeDef, _registry);
            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var calcVar = isNonNullableStruct ? $"calculator_{member.FieldId}" : "calculator";

            // Get nested derived type info (parent type, ProtoInclude, wrapper tag)
            var derivedInfo = GeneratorHelpers.TryGetNestedDerivedTypeInfo(member.Type, _registry);
            if (derivedInfo == null)
            {
                _sb.AppendIndentedLine($"// WARNING: No ProtoInclude found for {member.Type}");
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
                return;
            }

            var parentTypeName = derivedInfo.ParentTypeName;
            var wrapperTag = derivedInfo.WrapperTag;
            var protoInclude = derivedInfo.ProtoInclude;

            _sb.AppendIndentedLine($"// ProtoInclude wrapper for nested derived type {typeName}");

            // Step 1: Calculate full size (wrapper + base fields OUTSIDE wrapper)
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendNewLine();

            // Calculate wrapper tag size
            _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32({wrapperTag}u);");

            // Calculate wrapper content size (derived fields ONLY, not base)
            var wrapperContentCalcVar = isNonNullableStruct ? $"wrapperContent_{member.FieldId}" : "wrapperContentCalc";
            _sb.AppendIndentedLine($"var {wrapperContentCalcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            string sizeCalcNamespace = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            _sb.AppendIndentedLine($"{sizeCalcNamespace}SizeCalculators.Calculate{typeName}OwnFieldsSize(ref {wrapperContentCalcVar}, {valueArg});");

            _sb.AppendIndentedLine($"{calcVar}.WriteVarUInt32((uint){wrapperContentCalcVar}.Length);");
            _sb.AppendIndentedLine($"{calcVar}.AddByteLength({wrapperContentCalcVar}.Length);");

            // Base fields size calculated OUTSIDE wrapper
            string parentSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"// Base fields ({parentTypeName}) - OUTSIDE wrapper");
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
            string writerNamespace = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"{writerNamespace}{_writerKind}Writers.Write{typeName}OwnFields(ref writer, {valueArg});");
            _sb.AppendNewLine();

            // Step 5: Write base fields OUTSIDE wrapper
            string parentWriterPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"// Base fields ({parentTypeName}) - OUTSIDE wrapper");
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
            string valueArg = GeneratorHelpers.GetNullableValueAccess(sourceVar, member, typeDef, _registry);

            TagCodeHelper.WriteTag(_sb, member.FieldId, WireType.Len);

            // Calculate and write length
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // NOTE: For top-level serialization (Write{Type}), ProtoInclude wrapper is included in ContentSize.
            // For nested fields of non-derived types, no wrapper is needed.
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);
            _sb.AppendIndentedLine($"{nsPrefix}SizeCalculators.Calculate{typeName}ContentSize(ref {calcVar}, {valueArg});");

            _sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){calcVar}.Length);");

            // Write content
            _sb.AppendIndentedLine($"{nsPrefix}{_writerKind}Writers.Write{typeName}Content(ref writer, {valueArg});");
        }

        /// <summary>
        /// Generates write code for polymorphic fields (field declared as base type with ProtoIncludes).
        /// Example: [ProtoMember(2)] ProtoParameterBase ProtoValue (where ProtoParameterBase has multiple ProtoIncludes)
        /// Generates runtime type dispatch to add ProtoInclude wrapper for derived type instances.
        /// NOTE: Base fields are written OUTSIDE the wrapper for protobuf-net compatibility.
        /// </summary>
        private void GeneratePolymorphicFieldWrite(ProtoMemberAttribute member, string sourceVar, string typeName, TypeDefinition typeDef)
        {
            var sortedDerived = GeneratorHelpers.GetSortedDerivedTypes(member.Type, _registry);
            if (sortedDerived == null)
            {
                // No derived types - fallback to standard write
                GenerateStandardComplexTypeWrite(member, sourceVar, typeName, typeDef);
                return;
            }

            _sb.AppendIndentedLine($"// Polymorphic field: runtime type dispatch for ProtoInclude wrapper");

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

                // Step 1: Calculate total size (wrapper + base fields OUTSIDE wrapper)
                _sb.AppendIndentedLine($"var totalCalc{derivedClassName} = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendNewLine();

                // Wrapper tag size
                _sb.AppendIndentedLine($"// ProtoInclude wrapper (field {protoInclude.FieldId} in {parentTypeName})");
                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.WriteVarUInt32({wrapperTag}u);");

                // Wrapper content size (derived fields ONLY, not base)
                _sb.AppendIndentedLine($"var wrapperCalc{derivedClassName} = new global::GProtobuf.Core.WriteSizeCalculator();");

                string derivedSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNamespace, _currentNamespace);

                _sb.AppendIndentedLine($"{derivedSizeCalcPrefix}SizeCalculators.Calculate{derivedClassName}OwnFieldsSize(ref wrapperCalc{derivedClassName}, derived{derivedClassName});");

                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.WriteVarUInt32((uint)wrapperCalc{derivedClassName}.Length);");
                _sb.AppendIndentedLine($"totalCalc{derivedClassName}.AddByteLength(wrapperCalc{derivedClassName}.Length);");

                // Base fields size calculated OUTSIDE wrapper
                string parentSizeCalcPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
                _sb.AppendIndentedLine($"// Base fields ({parentTypeName}) - OUTSIDE wrapper");
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
                string derivedWriterPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNamespace, _currentNamespace);
                _sb.AppendIndentedLine($"{derivedWriterPrefix}{_writerKind}Writers.Write{derivedClassName}OwnFields(ref writer, derived{derivedClassName});");
                _sb.AppendNewLine();

                // Step 5: Write base fields OUTSIDE wrapper
                string parentWriterPrefix = GeneratorHelpers.GetNamespacePrefix(derivedInfo.ParentNamespace, _currentNamespace);
                _sb.AppendIndentedLine($"// Base fields ({parentTypeName}) - OUTSIDE wrapper");
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
            EnumFieldHelper.GenerateEnumField(
                _sb,
                member,
                sourceVar,
                writeTag: () => TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt, calculatorVar),
                writeValue: (valueExpr, _) => _sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int){valueExpr});"));
        }

        private void GenerateMapFieldSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry, _className, _registry, _virtualTypesNamespace);
            mapHandler.GenerateSize(member, sourceVar, calculatorVar);
        }

        private void GenerateCollectionFieldSizeCalculation(ProtoMemberAttribute member, string sourceVar, string calculatorVar)
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

            string localVar = sourceVar;

            if (!isNonNullableStruct)
            {
                _sb.StartNewBlock(); // Scope block to avoid name collisions
                _sb.AppendIndentedLine($"var complexValue = {sourceVar};");
                _sb.AppendIndentedLine("if (complexValue != null)");
                _sb.StartNewBlock();
                localVar = "complexValue";
            }

            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len, calculatorVar);

            // Calculate content size first
            var lengthVar = isNonNullableStruct ? $"lengthBefore_{member.FieldId}" : "lengthBefore";
            var contentLengthVar = isNonNullableStruct ? $"contentLength_{member.FieldId}" : "contentLength";

            string valueArg = GeneratorHelpers.GetNullableValueAccess(localVar, member, typeDef, _registry);

            _sb.AppendIndentedLine($"var {lengthVar} = {calculatorVar}.Length;");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{typeName}ContentSize(ref {calculatorVar}, {valueArg});");
            _sb.AppendIndentedLine($"var {contentLengthVar} = {calculatorVar}.Length - {lengthVar};");
            _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint){contentLengthVar});");

            if (!isNonNullableStruct)
            {
                _sb.EndBlock(); // if
                _sb.EndBlock(); // scope
            }
        }
        #endregion

    }
}
