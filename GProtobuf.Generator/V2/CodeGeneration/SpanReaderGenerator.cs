using System.Collections.Generic;
using GProtobuf.Generator.V2.Handlers;

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

        public SpanReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = new PrimitiveHandler();
        }

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
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #region Read Method

        /// <summary>
        /// Generates Read{ClassName}(ref SpanReader reader) method.
        /// Entry point for deserialization.
        /// </summary>
        private void GenerateReadMethod(TypeDefinition type)
        {
            var className = GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref SpanReader reader)");
            _sb.StartNewBlock();

            bool hasInheritance = HasInheritance(type);

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
            _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();");
            _sb.AppendNewLine();

            // Handle ProtoIncludes (derived types)
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeRead(include);
                }
            }

            // Handle own fields for base class, or parent fields for derived class
            if (_registry.IsDerivedType(type.FullName))
            {
                // This is a derived class - handle base class fields
                var rootTypeName = _registry.GetRootType(type.FullName);
                var rootType = _registry.GetByFullName(rootTypeName);
                if (rootType?.ProtoMembers != null)
                {
                    foreach (var member in rootType.ProtoMembers)
                    {
                        GenerateFieldRead(member);
                    }
                }
            }
            else if (type.ProtoMembers != null)
            {
                // This is a base class - handle own fields
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldRead(member);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("// default: skip unknown field");
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.EndBlock();
            _sb.AppendIndentedLine("return result;");
        }

        #endregion

        #region ReadContent Method

        /// <summary>
        /// Generates Read{ClassName}Content(ref SpanReader reader) method.
        /// Creates instance and fills all fields.
        /// </summary>
        private void GenerateReadContentMethod(TypeDefinition type)
        {
            var className = GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}Content(ref SpanReader reader)");
            _sb.StartNewBlock();

            bool hasInheritance = HasInheritance(type);

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

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();");
            _sb.AppendNewLine();

            // Handle each field
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldRead(member);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("// default: skip unknown field");
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.EndBlock();

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

            // Read loop
            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();");
            _sb.AppendNewLine();

            // Handle ProtoIncludes first
            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeRead(include);
                }
            }

            // Handle own fields
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldRead(member);
                }
            }

            // Default - skip unknown fields
            _sb.AppendIndentedLine("// default: skip unknown field");
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.EndBlock();

            _sb.AppendIndentedLine("return result;");
        }

        #endregion

        #region Field Generation

        /// <summary>
        /// Generates code to read a single field based on its type.
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
            var derivedClassName = GetClassName(include.Type);

            _sb.AppendIndentedLine($"if (fieldId == {include.FieldId})");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result = Read{derivedClassName}Content(ref nestedReader);");
            _sb.AppendIndentedLine("continue;");
            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateEnumFieldRead(ProtoMemberAttribute member)
        {
            var typeName = GetClassName(member.Type);
            _sb.AppendIndentedLine($"result.{member.Name} = ({typeName})reader.ReadVarInt32();");
            _sb.AppendIndentedLine("continue;");
        }

        private void GenerateMapFieldRead(ProtoMemberAttribute member)
        {
            // TODO: Implement with MapHandler
            _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
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
                        member.DataFormat);
                }
                else
                {
                    _primitiveHandler.GenerateNonPackedArrayRead(
                        _sb,
                        $"result.{member.Name}",
                        member.CollectionElementType,
                        member.DataFormat,
                        member.FieldId);
                }
                _sb.AppendIndentedLine("continue;");
            }
            else
            {
                // TODO: Implement with CollectionHandler for complex types
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.AppendIndentedLine("continue;");
            }
        }

        private void GenerateComplexTypeRead(ProtoMemberAttribute member)
        {
            var typeName = GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
            _sb.AppendIndentedLine("continue;");
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

        #endregion
    }
}
