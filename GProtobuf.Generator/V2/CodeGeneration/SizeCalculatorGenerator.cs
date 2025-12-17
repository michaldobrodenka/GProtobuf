using System.Collections.Generic;
using GProtobuf.Generator.V2.Handlers;

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

        public SizeCalculatorGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
        }

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

            _sb.EndBlock();
            _sb.AppendNewLine();
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
            if (inheritanceChain.Count == 2)
            {
                var baseTypeName = inheritanceChain[0];
                var derivedTypeName = inheritanceChain[1];

                var baseType = _registry.GetByFullName(baseTypeName);
                var protoInclude = FindProtoInclude(baseType, derivedTypeName);

                if (protoInclude != null)
                {
                    GenerateSizeTag(protoInclude.FieldId, WireType.Len);

                    // Calculate content size
                    var derivedClassName = GetClassName(derivedTypeName);
                    if (type.ProtoMembers != null && type.ProtoMembers.Count > 0)
                    {
                        _sb.AppendIndentedLine("var tempCalcContent = new global::GProtobuf.Core.WriteSizeCalculator();");
                        _sb.AppendIndentedLine($"Calculate{derivedClassName}ContentSize(ref tempCalcContent, obj);");
                        _sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)tempCalcContent.Length);");
                    }
                    else
                    {
                        _sb.AppendIndentedLine("calculator.WriteVarInt32(0); // No fields inside final ProtoInclude");
                    }
                }
            }
            else if (inheritanceChain.Count == 3)
            {
                var baseTypeName = inheritanceChain[0];
                var middleTypeName = inheritanceChain[1];
                var derivedTypeName = inheritanceChain[2];

                var baseType = _registry.GetByFullName(baseTypeName);
                var middleType = _registry.GetByFullName(middleTypeName);

                var includeBaseToMiddle = FindProtoInclude(baseType, middleTypeName);
                var includeMiddleToDerived = FindProtoInclude(middleType, derivedTypeName);

                if (includeBaseToMiddle != null && includeMiddleToDerived != null)
                {
                    // Calculate tag for outer wrapper (Base -> Middle)
                    GenerateSizeTag(includeBaseToMiddle.FieldId, WireType.Len);

                    // Calculate B wrapper content size
                    _sb.AppendIndentedLine($"var tempCalc{includeBaseToMiddle.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");

                    // Add inner tag size
                    var (_, innerTagBytes) = TypeMapping.PrecomputeTagBytes(includeMiddleToDerived.FieldId, WireType.Len);
                    _sb.AppendIndentedLine($"tempCalc{includeBaseToMiddle.FieldId}.AddByteLength({innerTagBytes});");

                    // Calculate innermost content size
                    var derivedClassName = GetClassName(derivedTypeName);
                    _sb.AppendIndentedLine("var tempCalcC = new global::GProtobuf.Core.WriteSizeCalculator();");
                    _sb.AppendIndentedLine($"Calculate{derivedClassName}ContentSize(ref tempCalcC, obj);");
                    _sb.AppendIndentedLine($"tempCalc{includeBaseToMiddle.FieldId}.WriteVarUInt32((uint)tempCalcC.Length);");
                    _sb.AppendIndentedLine($"tempCalc{includeBaseToMiddle.FieldId}.AddByteLength(tempCalcC.Length);");

                    // Add middle type content
                    var middleClassName = GetClassName(middleTypeName);
                    _sb.AppendIndentedLine($"Calculate{middleClassName}ContentSize(ref tempCalc{includeBaseToMiddle.FieldId}, obj);");
                    _sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempCalc{includeBaseToMiddle.FieldId}.Length);");
                }
            }
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
            // TODO: Implement with MapHandler
            _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
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
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            _sb.AppendIndentedLine($"calculator.AddByteLength({byteCount});");
        }

        #endregion

        #region Helpers

        private static string GetClassName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            var lastDot = fullName.LastIndexOf('.');
            return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
        }

        private static ProtoIncludeAttribute FindProtoInclude(TypeDefinition type, string derivedTypeName)
        {
            if (type?.ProtoIncludes == null)
                return null;

            foreach (var include in type.ProtoIncludes)
            {
                if (include.Type == derivedTypeName)
                    return include;
            }
            return null;
        }

        #endregion
    }
}
