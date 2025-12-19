using System.Collections.Generic;
using GProtobuf.Generator.V2.Handlers;

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
        private readonly string _writerType;
        private readonly string _className;

        public StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, "Stream")
        {
        }

        protected StreamWriterGenerator(StringBuilderWithIndent sb, TypeRegistry registry, string writerKind)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
            _writerType = $"global::GProtobuf.Core.{writerKind}Writer";
            _className = $"{writerKind}Writers";
        }

        /// <summary>
        /// Generates complete Writers class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types)
        {
            _sb.AppendIndentedLine($"public static class {_className}");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateWriteMethod(type);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #region Write Method

        /// <summary>
        /// Generates Write{ClassName}(ref Writer writer, instance) method.
        /// </summary>
        private void GenerateWriteMethod(TypeDefinition type)
        {
            var className = GetClassName(type.FullName);

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
        }

        /// <summary>
        /// Generates WriteXxxContent method that writes just the field content without tag.
        /// Used for nested messages.
        /// </summary>
        private void GenerateWriteContentMethod(TypeDefinition type, string className)
        {
            _sb.AppendIndentedLine($"public static void Write{className}Content(ref {_writerType} writer, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            // For derived types, write all inherited fields first
            bool isDerived = _registry.IsDerivedType(type.FullName);
            if (isDerived)
            {
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

            // Write own fields
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
            // Handle ProtoIncludes with switch on derived types
            _sb.AppendIndentedLine("switch (instance)");
            _sb.StartNewBlock();

            foreach (var include in type.ProtoIncludes)
            {
                var derivedClassName = GetClassName(include.Type);
                _sb.AppendIndentedLine($"case global::{include.Type} derived:");
                _sb.IncreaseIndent();

                // Write tag for ProtoInclude
                GenerateWriteTag(include.FieldId, WireType.Len);

                // Calculate and write length
                _sb.AppendIndentedLine("var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                _sb.AppendIndentedLine($"SizeCalculators.Calculate{derivedClassName}ContentSize(ref calculator, derived);");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)calculator.Length);");

                // Write content
                _sb.AppendIndentedLine($"Write{derivedClassName}Content(ref writer, derived);");
                _sb.AppendIndentedLine("break;");
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();
            _sb.AppendNewLine();

            // Write base class fields
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldWrite(member, "instance");
                }
            }
        }

        private void GenerateWriteMethodForDerived(TypeDefinition type, string className)
        {
            // For derived types, we need to wrap in ProtoInclude from parent
            var inheritanceChain = _registry.GetInheritanceChain(type.FullName);

            if (inheritanceChain.Count >= 2)
            {
                // Write ProtoInclude wrappers from root to this type
                GenerateProtoIncludeWrappers(type, inheritanceChain);
            }

            // Write root type fields
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
            var protoInclude = FindProtoInclude(currentType, nextTypeName);

            if (protoInclude == null)
                return;

            // Write tag for this wrapper
            GenerateWriteTag(protoInclude.FieldId, WireType.Len);

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
            var className = GetClassName(typeName);

            // Add this level's content size
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref {calcVar}, instance);");

            // If there's a next level, add wrapper overhead
            if (levelIndex + 1 < chain.Count)
            {
                var nextTypeName = chain[levelIndex + 1];
                var protoInclude = FindProtoInclude(type, nextTypeName);

                if (protoInclude != null)
                {
                    // Add tag size for next wrapper
                    GenerateSizeTag(_sb, calcVar, protoInclude.FieldId, WireType.Len);

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
            var className = GetClassName(type.FullName);

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
                GenerateWriteTag(member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar}.Value);");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if ((int){sourceVar} != 0)");
                _sb.StartNewBlock();
                GenerateWriteTag(member.FieldId, WireType.VarInt);
                _sb.AppendIndentedLine($"writer.WriteVarInt32((int){sourceVar});");
                _sb.EndBlock();
            }
        }

        private void GenerateMapFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            // TODO: Implement with MapHandler
            _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
        }

        private void GenerateCollectionFieldWrite(ProtoMemberAttribute member, string sourceVar)
        {
            if (_primitiveHandler.CanHandleCollection(member.CollectionElementType))
            {
                if (member.IsPacked)
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
            else
            {
                // TODO: Implement with CollectionHandler for complex types
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
            }
        }

        private void GenerateComplexTypeWrite(ProtoMemberAttribute member, string sourceVar)
        {
            var typeName = GetClassName(member.Type);

            _sb.AppendIndentedLine($"if ({sourceVar} != null)");
            _sb.StartNewBlock();

            GenerateWriteTag(member.FieldId, WireType.Len);

            // Calculate and write length
            _sb.AppendIndentedLine("var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{typeName}ContentSize(ref calculator, {sourceVar});");
            _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)calculator.Length);");

            // Write content
            _sb.AppendIndentedLine($"Write{typeName}Content(ref writer, {sourceVar});");

            _sb.EndBlock();
        }

        #endregion

        #region Tag Generation

        private void GenerateWriteTag(int fieldId, WireType wireType)
        {
            var (bytesString, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);

            if (byteCount == 1)
            {
                _sb.AppendIndentedLine($"writer.WriteSingleByte({bytesString});");
            }
            else
            {
                // Use static ReadOnlySpan from Tags class for zero-allocation
                var tagPropertyName = TagsGenerator.GetTagPropertyName(fieldId, wireType);
                _sb.AppendIndentedLine($"writer.WriteBytes(Tags.{tagPropertyName});");
            }
        }

        private void GenerateSizeTag(StringBuilderWithIndent sb, string calculatorVar, int fieldId, WireType wireType)
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength({byteCount});");
        }

        #endregion

        #region Helpers

        private bool HasInheritance(TypeDefinition type)
        {
            return (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
                   || _registry.IsDerivedType(type.FullName);
        }

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
