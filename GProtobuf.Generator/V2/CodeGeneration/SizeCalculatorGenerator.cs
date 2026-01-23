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
            }

            // Generate ContentSize methods for ProtoInclude derived types
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

            // Generate ContentSize methods for ProtoInclude types
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    GenerateCalculateContentSizeMethod(protoIncludeType);
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
            // Wire format requires parent fields come before derived type wrappers
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootType = _registry.GetByFullName(rootTypeName);
            if (rootType?.ProtoMembers != null)
            {
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            // Then add ProtoInclude wrapper sizes
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);
            if (inheritanceChain.Count >= 2)
            {
                GenerateProtoIncludeWrapperSizes(type, inheritanceChain);
            }
        }

        private void GenerateProtoIncludeWrapperSizes(TypeDefinition type, IReadOnlyList<string> inheritanceChain)
        {
            // Generate size calculation for nested wrappers from level 0 to the derived type
            if (inheritanceChain.Count < 2)
                return;

            // Generate the outermost wrapper size (from root to first derived)
            GenerateNestedWrapperSize(inheritanceChain, 0);
        }

        /// <summary>
        /// Recursively generates size calculation for nested ProtoInclude wrappers.
        /// </summary>
        private void GenerateNestedWrapperSize(IReadOnlyList<string> chain, int levelIndex)
        {
            if (levelIndex >= chain.Count - 1)
                return;

            var currentTypeName = chain[levelIndex];
            var nextTypeName = chain[levelIndex + 1];
            var currentType = _registry.GetByFullName(currentTypeName);
            var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Add tag size for this wrapper
            TagCodeHelper.AddTagSize(_sb, protoInclude.FieldId, WireType.Len);

            // Calculate wrapper content size (includes nested wrappers + this level's fields)
            var calcVar = $"wrapperCalc{levelIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Add this level's (nextType's) content size
            var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
            _sb.AppendIndentedLine($"Calculate{nextClassName}ContentSize(ref {calcVar}, obj);");

            // If there's another level, add its wrapper size recursively
            if (levelIndex + 2 < chain.Count)
            {
                GenerateNestedWrapperSizeInner(chain, levelIndex + 1, calcVar);
            }

            // Write the length prefix size
            _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint){calcVar}.Length);");
            // Add the actual content size
            _sb.AppendIndentedLine($"calculator.AddByteLength({calcVar}.Length);");
        }

        /// <summary>
        /// Generates inner wrapper size calculation (adds to parent calculator).
        /// </summary>
        private void GenerateNestedWrapperSizeInner(IReadOnlyList<string> chain, int levelIndex, string parentCalcVar)
        {
            if (levelIndex >= chain.Count - 1)
                return;

            var currentTypeName = chain[levelIndex];
            var nextTypeName = chain[levelIndex + 1];
            var currentType = _registry.GetByFullName(currentTypeName);
            var protoInclude = GeneratorHelpers.FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Add tag size for this inner wrapper
            var tagBytes = TagCodeHelper.GetTagByteCount(protoInclude.FieldId, WireType.Len);
            _sb.AppendIndentedLine($"{parentCalcVar}.AddByteLength({tagBytes});");

            // Calculate inner wrapper content size
            var calcVar = $"innerCalc{levelIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Add next level's content size
            var nextClassName = TypeNameHelper.GetClassName(nextTypeName);
            _sb.AppendIndentedLine($"Calculate{nextClassName}ContentSize(ref {calcVar}, obj);");

            // If there's another level, add its wrapper size recursively
            if (levelIndex + 2 < chain.Count)
            {
                GenerateNestedWrapperSizeInner(chain, levelIndex + 1, calcVar);
            }

            // Add length prefix size and content size to parent
            _sb.AppendIndentedLine($"{parentCalcVar}.WriteVarUInt32((uint){calcVar}.Length);");
            _sb.AppendIndentedLine($"{parentCalcVar}.AddByteLength({calcVar}.Length);");
        }

        #endregion

        #region CalculateContentSize Method

        /// <summary>
        /// Generates Calculate{ClassName}ContentSize method.
        /// This calculates size of just this type's own fields.
        /// </summary>
        private void GenerateCalculateContentSizeMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static void Calculate{className}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{type.FullName} obj)");
            _sb.StartNewBlock();

            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
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
                    member.IsNullable);
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
                _sb.AppendIndentedLine($"if ({sourceVar}.HasValue)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar});");
                _sb.EndBlock();
            }
        }

        private void GenerateMapFieldSize(ProtoMemberAttribute member, string sourceVar)
        {
            var mapHandler = new MapHandler(_sb, _virtualMapRegistry);
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

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            TagCodeHelper.AddTagSize(_sb, member.FieldId, WireType.Len);

            // Calculate content size first
            _sb.AppendIndentedLine("var lengthBefore = calculator.Length;");

            // Check if type is from different namespace and qualify the call
            var typeNamespace = TypeNameHelper.GetNamespace(member.Type);
            if (!string.IsNullOrEmpty(typeNamespace) && typeNamespace != _currentNamespace)
            {
                _sb.AppendIndentedLine($"global::{typeNamespace}.Serialization.SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
            }
            else
            {
                _sb.AppendIndentedLine($"Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
            }

            _sb.AppendIndentedLine("var contentLength = calculator.Length - lengthBefore;");
            _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)contentLength);");

            _sb.EndBlock();
        }

        #endregion

    }
}
