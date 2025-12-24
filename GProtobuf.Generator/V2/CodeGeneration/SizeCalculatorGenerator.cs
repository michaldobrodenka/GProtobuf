using System.Collections.Generic;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
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
        private readonly VirtualMapTypeRegistry _virtualMapRegistry;

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null)
        {
        }

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
            _virtualMapRegistry = virtualMapRegistry ?? new VirtualMapTypeRegistry();
        }

        /// <summary>
        /// Gets the virtual map type registry used by this generator.
        /// </summary>
        public VirtualMapTypeRegistry VirtualMapRegistry => _virtualMapRegistry;

        /// <summary>
        /// Generates complete SizeCalculators class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types)
        {
            _sb.AppendIndentedLine("public static class SizeCalculators");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateCalculateSizeMethod(type);
                GenerateCalculateContentSizeMethod(type);
            }

            // Generate virtual map entry size calculators
            GenerateVirtualMapEntrySizeCalculators();

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

            var generator = new VirtualMapEntryGenerator(_sb, _virtualMapRegistry);
            foreach (var virtualType in virtualTypes)
            {
                generator.GenerateSizeCalculator(virtualType);
            }
        }

        #region CalculateSize Method

        /// <summary>
        /// Generates Calculate{ClassName}Size method.
        /// This is the main entry point that handles inheritance wrappers.
        /// </summary>
        private void GenerateCalculateSizeMethod(TypeDefinition type)
        {
            var className = GetClassName(type.FullName);

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
            // Handle ProtoIncludes with switch on derived types
            _sb.AppendIndentedLine("switch (obj)");
            _sb.StartNewBlock();

            foreach (var include in type.ProtoIncludes)
            {
                var derivedClassName = GetClassName(include.Type);
                _sb.AppendIndentedLine($"case global::{include.Type} derived:");
                _sb.IncreaseIndent();

                // Add tag size for ProtoInclude
                GenerateSizeTag(include.FieldId, WireType.Len);

                // Calculate content size first
                _sb.AppendIndentedLine("var lengthBefore = calculator.Length;");
                _sb.AppendIndentedLine($"Calculate{derivedClassName}Size(ref calculator, derived);");
                _sb.AppendIndentedLine("var contentLength = calculator.Length - lengthBefore;");
                _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)contentLength);");

                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();
            _sb.AppendNewLine();

            // Calculate base class fields
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
            }
        }

        private void GenerateCalculateSizeForDerived(TypeDefinition type, string className)
        {
            // For derived types, we need to add ProtoInclude wrapper sizes
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);

            if (inheritanceChain.Count >= 2)
            {
                GenerateProtoIncludeWrapperSizes(type, inheritanceChain);
            }

            // Add root type fields
            var rootTypeName = _registry.GetRootType(type.FullName);
            var rootType = _registry.GetByFullName(rootTypeName);
            if (rootType?.ProtoMembers != null)
            {
                foreach (var member in rootType.ProtoMembers)
                {
                    GenerateFieldSize(member, "obj");
                }
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
            var protoInclude = FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Add tag size for this wrapper
            GenerateSizeTag(protoInclude.FieldId, WireType.Len);

            // Calculate wrapper content size (includes nested wrappers + this level's fields)
            var calcVar = $"wrapperCalc{levelIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Add this level's (nextType's) content size
            var nextClassName = GetClassName(nextTypeName);
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
            var protoInclude = FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Add tag size for this inner wrapper
            var tagBytes = TagGenerator.GetTagByteCount(protoInclude.FieldId, WireType.Len);
            _sb.AppendIndentedLine($"{parentCalcVar}.AddByteLength({tagBytes});");

            // Calculate inner wrapper content size
            var calcVar = $"innerCalc{levelIndex}";
            _sb.AppendIndentedLine($"var {calcVar} = new global::GProtobuf.Core.WriteSizeCalculator();");

            // Add next level's content size
            var nextClassName = GetClassName(nextTypeName);
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
            var className = GetClassName(type.FullName);

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
                GenerateSizeTag(member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"calculator.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                GenerateSizeTag(member.FieldId, WireType.VarInt);
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
                if (member.IsPacked)
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
            else
            {
                // TODO: Implement with CollectionHandler for complex types
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
            }
        }

        private void GenerateComplexTypeSize(ProtoMemberAttribute member, string sourceVar)
        {
            var typeName = GetClassName(member.Type);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            GenerateSizeTag(member.FieldId, WireType.Len);

            // Calculate content size first
            _sb.AppendIndentedLine("var lengthBefore = calculator.Length;");
            _sb.AppendIndentedLine($"Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
            _sb.AppendIndentedLine("var contentLength = calculator.Length - lengthBefore;");
            _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)contentLength);");

            _sb.EndBlock();
        }

        #endregion

        #region Tag Generation

        private void GenerateSizeTag(int fieldId, WireType wireType)
        {
            TagGenerator.AddTagSize(_sb, fieldId, wireType);
        }

        #endregion

        #region Helpers

        private static string GetClassName(string fullName) => TypeNameHelper.GetClassName(fullName);

        private static ProtoIncludeAttribute FindProtoInclude(TypeDefinition type, string derivedTypeName) =>
            GeneratorHelpers.FindProtoInclude(type, derivedTypeName);

        #endregion
    }
}
