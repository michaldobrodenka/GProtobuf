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
                GeneratePopulateMethod(type);
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
                var protoInclude = FindProtoInclude(currentType, nextTypeName);

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
                _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldReadBodyForDerived(member, wireTypeVar, readerVar);
            }
            else if (member.IsEnum)
            {
                var typeName = GetClassName(member.Type);
                _sb.AppendIndentedLine($"result.{member.Name} = ({typeName}){readerVar}.ReadVarInt32();");
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
            else
            {
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
                _sb.AppendIndentedLine($"{readerVar}.SkipField({wireTypeVar});");
            }
        }

        private void GenerateComplexTypeReadBodyWithReader(ProtoMemberAttribute member, string readerVar)
        {
            var typeName = GetClassName(member.Type);
            _sb.AppendIndentedLine($"var length = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var nestedReader = new SpanReader({readerVar}.GetSlice(length));");
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
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

            var className = GetClassName(type.FullName);

            _sb.AppendIndentedLine($"public static void Populate{className}(ref SpanReader reader, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            bool hasInheritance = HasInheritance(type);

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
                _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
            else if (member.IsCollection)
            {
                GenerateCollectionFieldPopulateBody(member);
            }
            else if (member.IsEnum)
            {
                var typeName = GetClassName(member.Type);
                _sb.AppendIndentedLine($"instance.{member.Name} = ({typeName})reader.ReadVarInt32();");
            }
            else if (_primitiveHandler.CanHandle(member.Type))
            {
                _primitiveHandler.GenerateRead(_sb, $"instance.{member.Name}", member.Type, member.DataFormat);
            }
            else
            {
                // Complex type - nested message
                var typeName = GetClassName(member.Type);
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
            else
            {
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
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
            var derivedClassName = GetClassName(include.Type);

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

        // Body versions for switch case (without continue/break - those are added by GenerateFieldReadCase)
        private void GenerateEnumFieldReadBody(ProtoMemberAttribute member)
        {
            var typeName = GetClassName(member.Type);
            _sb.AppendIndentedLine($"result.{member.Name} = ({typeName})reader.ReadVarInt32();");
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member)
        {
            // TODO: Implement with MapHandler
            _sb.AppendIndentedLine($"// TODO: Map field {member.Name}");
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
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
            else
            {
                // TODO: Implement with CollectionHandler for complex types
                _sb.AppendIndentedLine($"// TODO: Collection field {member.Name}");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
            }
        }

        private void GenerateComplexTypeReadBody(ProtoMemberAttribute member)
        {
            var typeName = GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = new SpanReader(reader.GetSlice(length));");
            _sb.AppendIndentedLine($"result.{member.Name} = Read{typeName}Content(ref nestedReader);");
        }

        // Legacy versions for if-else (with continue)
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
