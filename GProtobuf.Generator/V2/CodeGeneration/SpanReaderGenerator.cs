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
            _collectionHandler = new CollectionHandler(sb);
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
                GenerateReadMethod(type);
                GenerateReadContentMethod(type);
                GeneratePopulateMethod(type);
            }

            // Generate virtual map entry readers
            GenerateVirtualMapEntryReaders();

            // Generate virtual tuple readers
            GenerateVirtualTupleReaders();

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Generates reader methods for all registered virtual map entry types.
        /// </summary>
        private void GenerateVirtualMapEntryReaders()
        {
            var virtualTypes = _virtualMapRegistry.GetAllTypes();
            if (virtualTypes.Count == 0) return;

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("// Virtual Map Entry Readers");

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateReader(virtualType);
            }

            // Generate KeyValue methods that use MapEntry methods
            var keyValueGenerator = new KeyValueClassGenerator(_sb, _virtualMapRegistry);
            keyValueGenerator.GenerateKeyValueMethods("SpanReaders");
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
                    GenerateProtoIncludeReadCase(include);
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
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                if (member.IsPacked)
                {
                    _primitiveHandler.GeneratePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type,
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

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimpleReadContent(type, className);
            }
            else
            {
                GenerateReadContentWithInheritance(type, className);
            }

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

            // Initialize null collection properties to empty collections
            if (type.ProtoMembers != null)
            {
                var collectionMembers = type.ProtoMembers
                    .Where(m => m.IsCollection || m.IsMap)
                    .ToList();

                if (collectionMembers.Count > 0)
                {
                    _sb.AppendNewLine();
                    _sb.AppendIndentedLine("// Initialize any null collections to prevent null reference exceptions");

                    foreach (var member in collectionMembers)
                    {
                        // Skip arrays (remain null if no data)
                        if (member.CollectionKind == CollectionKind.Array)
                            continue;

                        // Skip custom concrete collection types (can't safely initialize)
                        if (member.CollectionKind == CollectionKind.ConcreteCollection)
                            continue;

                        if (member.IsMap)
                        {
                            // Check if this is a standard Dictionary (not a derived type like DerivedDictionary)
                            var normalizedType = TypeMapping.NormalizeTypeName(member.Type ?? "");
                            if (normalizedType.StartsWith("System.Collections.Generic.Dictionary<") ||
                                normalizedType.StartsWith("System.Collections.Generic.IDictionary<"))
                            {
                                // Initialize Dictionary if null
                                var keyType = TypeMapping.GetShortTypeName(member.MapKeyType);
                                var valueType = TypeMapping.GetShortTypeName(member.MapValueType);
                                _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>();");
                            }
                            // Otherwise skip (custom derived dictionary type)
                        }
                        else if (member.Type != null && member.Type.Contains("HashSet"))
                        {
                            // Initialize HashSet if null
                            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                            _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.HashSet<{elementType}>();");
                        }
                        else if (member.IsCollection)
                        {
                            // Initialize List if null (covers List, IList, ICollection, IEnumerable)
                            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                            _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.List<{elementType}>();");
                        }
                    }
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
                    GenerateProtoIncludeReadCase(include);
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

            // Initialize null collection properties to empty collections
            if (type.ProtoMembers != null)
            {
                var collectionMembers = type.ProtoMembers
                    .Where(m => m.IsCollection || m.IsMap)
                    .ToList();

                if (collectionMembers.Count > 0)
                {
                    _sb.AppendNewLine();
                    _sb.AppendIndentedLine("// Initialize any null collections to prevent null reference exceptions");

                    foreach (var member in collectionMembers)
                    {
                        // Skip arrays (remain null if no data)
                        if (member.CollectionKind == CollectionKind.Array)
                            continue;

                        // Skip custom concrete collection types (can't safely initialize)
                        if (member.CollectionKind == CollectionKind.ConcreteCollection)
                            continue;

                        if (member.IsMap)
                        {
                            // Check if this is a standard Dictionary (not a derived type like DerivedDictionary)
                            var normalizedType = TypeMapping.NormalizeTypeName(member.Type ?? "");
                            if (normalizedType.StartsWith("System.Collections.Generic.Dictionary<") ||
                                normalizedType.StartsWith("System.Collections.Generic.IDictionary<"))
                            {
                                // Initialize Dictionary if null
                                var keyType = TypeMapping.GetShortTypeName(member.MapKeyType);
                                var valueType = TypeMapping.GetShortTypeName(member.MapValueType);
                                _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>();");
                            }
                            // Otherwise skip (custom derived dictionary type)
                        }
                        else if (member.Type != null && member.Type.Contains("HashSet"))
                        {
                            // Initialize HashSet if null
                            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                            _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.HashSet<{elementType}>();");
                        }
                        else if (member.IsCollection)
                        {
                            // Initialize List if null (covers List, IList, ICollection, IEnumerable)
                            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
                            _sb.AppendIndentedLine($"result.{member.Name} ??= new global::System.Collections.Generic.List<{elementType}>();");
                        }
                    }
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
                if (member.IsPacked)
                {
                    _primitiveHandler.GeneratePackedArrayRead(
                        _sb,
                        $"instance.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type);
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
        /// Generates switch case for reading a single field with lazy instance initialization.
        /// Used for inheritance scenarios where result starts as null.
        /// </summary>
        private void GenerateFieldReadCaseWithLazyInit(ProtoMemberAttribute member, string lazyInit)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

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

        private void GenerateProtoIncludeReadCase(ProtoIncludeAttribute include)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);

            _sb.AppendIndentedLine($"case {include.FieldId}:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");
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
                if (member.IsPacked)
                {
                    _primitiveHandler.GeneratePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type);
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
                if (member.IsPacked)
                {
                    _primitiveHandler.GeneratePackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.CollectionKind,
                        member.Type);
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

        #endregion
    }
}
