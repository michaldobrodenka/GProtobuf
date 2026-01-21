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
            _primitiveHandler = new PrimitiveHandler();
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
        /// Generates complete SpanReaders class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types)
        {
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

            // DIAGNOSTIC: Validate all virtual map types and report warnings for problematic ones
            ValidateVirtualMapTypes(virtualTypes);

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Readers");
            _sb.AppendNewLine();

            VirtualMapEntryGenerator generator;
            try
            {
                generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry);
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
            if (_registry.IsDerivedType(type.FullName))
            {
                // For derived types, generate code that handles the full inheritance chain
                GenerateReadMethodForDerived(type, className);
            }
            else
            {
                // For base types with ProtoIncludes, use polymorphic reading
                GenerateReadMethodForBaseWithIncludes(type, className);
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

            // Handle own fields with lazy initialization
            string lazyInit = type.IsAbstract ? null : $"result ??= new global::{type.FullName}();";

            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
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
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates Read method for derived type that handles the full inheritance chain.
        /// Reads all ancestor fields and navigates through nested ProtoInclude wrappers.
        /// </summary>
        private void GenerateReadMethodForDerived(TypeDefinition type, string className)
        {
            // Create instance of the derived type
            _sb.AppendIndentedLine($"global::{type.FullName} result = new global::{type.FullName}();");
            _sb.AppendNewLine();

            // Get inheritance chain: [Root, ..., Parent, This]
            var chain = _registry.GetInheritanceChain(type.FullName);

            // Generate nested reading for each level
            GenerateNestedReading(chain, 0, "reader");

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

            // Handle this level's ProtoMembers
            if (currentType.ProtoMembers != null)
            {
                foreach (var member in currentType.ProtoMembers)
                {
                    GenerateFieldReadCaseForDerived(member, wireTypeVar, readerVar);
                }
            }

            // Handle ProtoInclude to next level in chain
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

                if (protoInclude != null)
                {
                    GenerateNestedProtoIncludeCase(protoInclude, chain, levelIndex + 1, readerVar);
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
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
                mapHandler.GenerateRead(member, $"result.{member.Name}");
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBodyForDerived(member, wireTypeVar, readerVar);
            }
            else if (member.IsEnum)
            {
                var typeName = TypeNameHelper.GetClassName(member.Type);
                _sb.AppendIndentedLine($"result.{member.Name} = ({typeName}){readerVar}.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type, readerVar);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat, readerVar, wireTypeVar);
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

            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                // Level200: Primitives use dual-mode packed encoding (packed + unpacked backward compat with MERGE)
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

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
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId,
                        member.CollectionKind,
                        member.Type,
                        readerVar);
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
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
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

            // Phase 1: Recursion depth guard (Level200 requirement)
            _sb.AppendIndentedLine("using (global::GProtobuf.Core.RecursionGuard.EnterLevel())");
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

            _sb.EndBlock(); // Close using block

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimpleReadContent(TypeDefinition type, string className)
        {
            // Create instance
            _sb.AppendIndentedLine($"global::{type.FullName} result = new global::{type.FullName}();");
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

        private void GenerateReadContentWithInheritance(TypeDefinition type, string className)
        {
            // Create instance (abstract types get default)
            if (type.IsAbstract)
            {
                _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            }
            else
            {
                _sb.AppendIndentedLine($"global::{type.FullName} result = new global::{type.FullName}();");
            }
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

            // Handle own fields
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldReadCase(member);
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

            _sb.AppendIndentedLine($"public static void Populate{className}(ref SpanReader reader, global::{type.FullName} instance)");
            _sb.StartNewBlock();

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
                var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
                mapHandler.GenerateRead(member, $"instance.{member.Name}");
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldPopulateBody(member);
            }
            else if (member.IsEnum)
            {
                var typeName = TypeNameHelper.GetClassName(member.Type);
                _sb.AppendIndentedLine($"instance.{member.Name} = ({typeName})reader.ReadVarInt32();");
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"instance.{member.Name}", member.Type);
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"instance.{member.Name}", member.Type, member.DataFormat);
            }
            else
            {
                // Complex type - nested message
                var typeName = TypeNameHelper.GetClassName(member.Type);
                _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
                _sb.AppendIndentedLine($"instance.{member.Name} = Read{typeName}Content(ref nestedReader);");
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
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                // Level200: Primitives use dual-mode packed encoding (packed + unpacked backward compat with MERGE)
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

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
        /// Gets expected wire type for a field as a string (e.g., "WireType.VarInt").
        /// Handles collections (packed vs unpacked), maps, primitives, and complex types.
        /// </summary>
        private string GetExpectedWireTypeString(ProtoMemberAttribute member)
        {
            // Maps and complex types always use LengthDelimited
            if (member.IsMap || (!member.IsEnum && !_primitiveHandler.CanHandle(member.Type) && !member.IsCollection))
            {
                return "WireType.Len";
            }

            // Collections: check if packed
            if (member.IsCollection)
            {
                // Level200: Primitives use packed encoding by default
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

                if (shouldBePacked)
                {
                    return "WireType.Len"; // Packed encoding uses LengthDelimited
                }
                else
                {
                    // Unpacked: use element's wire type
                    return TypeMapping.GetWireTypeString(member.CollectionElementType, member.DataFormat);
                }
            }

            // FIX: Enum always uses Varint wire type (serialized as int32)
            if (member.IsEnum)
            {
                return "WireType.VarInt";
            }

            // Other primitives
            return TypeMapping.GetWireTypeString(member.Type, member.DataFormat);
        }

        /// <summary>
        /// Determines if wire type validation should be generated for this member.
        /// Collections with dual-mode support don't need validation here (handled in the handler).
        /// </summary>
        private bool ShouldGenerateWireTypeValidation(ProtoMemberAttribute member)
        {
            // Collections with dual-mode (packed/unpacked) support handle wire type internally
            if (member.IsCollection)
            {
                // Only primitive collections use dual-mode
                bool isDualMode = TypeMapping.ShouldBePackedByDefault(member.CollectionElementType) || member.IsPacked;
                return !isDualMode;
            }

            return true;
        }

        /// <summary>
        /// Generates wire type validation check.
        /// If wire type doesn't match expected, skips the field.
        /// </summary>
        private void GenerateWireTypeValidation(ProtoMemberAttribute member, string wireTypeVar = "wireType", string readerVar = "reader")
        {
            // Skip validation for collections with dual-mode support
            if (!ShouldGenerateWireTypeValidation(member))
                return;

            var expectedWireType = GetExpectedWireTypeString(member);
            _sb.AppendIndentedLine($"if ({wireTypeVar} != {expectedWireType})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            _sb.AppendIndentedLine("break;");
            _sb.EndBlock();
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

            // Phase 1: Wire type validation (Level200 requirement)
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

            // Phase 1: Wire type validation (Level200 requirement)
            // ProtoInclude always uses LengthDelimited wire type
            _sb.AppendIndentedLine("if (wireType != WireType.Len)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.EndBlock();

            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");

            // Save current result to copy fields from
            _sb.AppendIndentedLine("var oldResult = result;");

            // Read derived content
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");

            // Copy parent fields from old result to new derived result
            // Get all fields that need to be copied (from root to current type)
            var rootTypeName = _registry.GetRootType(parentType.FullName);
            var inheritanceChain = _registry.GetInheritanceChain(parentType.FullName);

            // Generate field copying for all types in the chain
            foreach (var typeName in inheritanceChain)
            {
                var typeInChain = _registry.GetByFullName(typeName);
                if (typeInChain?.ProtoMembers != null)
                {
                    foreach (var member in typeInChain.ProtoMembers)
                    {
                        _sb.AppendIndentedLine($"if (oldResult != null) result.{member.Name} = oldResult.{member.Name};");
                    }
                }
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
            _sb.DecreaseIndent();
        }

        /// <summary>
        /// Generates code to read a single field based on its type (legacy if-else version).
        /// </summary>
        private void GenerateFieldRead(ProtoMemberAttribute member)
        {
            _sb.AppendIndentedLine($"if (fieldId == {member.FieldId})");
            _sb.StartNewBlock();

            // Route to appropriate handler based on field type
            if (member.IsMap)
            {
                GenerateMapFieldRead(member);
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldRead(member);
            }
            else if (member.IsEnum)
            {
                GenerateEnumFieldRead(member);
            }
            else if (TupleHandler.IsTupleType(member.Type))
            {
                _tupleHandler.GenerateTupleRead($"result.{member.Name}", member.Type);
                _sb.AppendIndentedLine("continue;");
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"result.{member.Name}", member.Type, member.DataFormat);
                _sb.AppendIndentedLine("continue;");
            }
            else
            {
                // Complex type - nested message
                GenerateComplexTypeRead(member);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateProtoIncludeRead(ProtoIncludeAttribute include)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);

            _sb.AppendIndentedLine($"if (fieldId == {include.FieldId})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");
            _sb.AppendIndentedLine("continue;");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        // Body versions for switch case (without continue/break - those are added by GenerateFieldReadCase)
        private void GenerateEnumFieldReadBody(ProtoMemberAttribute member)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine($"result.{member.Name} = ({typeName})reader.ReadVarInt32();");
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
            mapHandler.GenerateRead(member, $"result.{member.Name}");
        }

        private void GenerateCollectionFieldReadBody(ProtoMemberAttribute member)
        {
            // Check if it's a primitive collection that can use PrimitiveHandler
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                // Level200: Primitives use dual-mode packed encoding (packed + unpacked backward compat with MERGE)
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

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
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
        }

        // Legacy versions for if-else (with continue)
        private void GenerateEnumFieldRead(ProtoMemberAttribute member)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine($"result.{member.Name} = ({typeName})reader.ReadVarInt32();");
            _sb.AppendIndentedLine("continue;");
        }

        private void GenerateMapFieldRead(ProtoMemberAttribute member)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
            mapHandler.GenerateRead(member, $"result.{member.Name}");
            _sb.AppendIndentedLine("continue;");
        }

        private void GenerateCollectionFieldRead(ProtoMemberAttribute member)
        {
            // Check if it's a primitive collection that can use PrimitiveHandler
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                // Level200: Primitives use dual-mode packed encoding (packed + unpacked backward compat with MERGE)
                bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

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
                _sb.AppendIndentedLine("continue;");
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
                _sb.AppendIndentedLine("continue;");
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
                _sb.AppendIndentedLine("continue;");
            }
        }

        private void GenerateComplexTypeRead(ProtoMemberAttribute member)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
            _sb.AppendIndentedLine("continue;");
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
