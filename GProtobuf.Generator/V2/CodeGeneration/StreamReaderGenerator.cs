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
    /// Generates StreamReaders class with Read{ClassName} and Read{ClassName}Content methods.
    /// Handles deserialization from StreamReader to object instances.
    ///
    /// DESIGN: StreamReaders methods delegate to SpanReaders internally.
    /// StreamReader.CreateSubReader() returns SpanReader, allowing zero-copy nested parsing.
    ///
    /// Pattern:
    /// - ReadXXX(ref StreamReader reader) - reads entire message from stream
    /// - ReadXXXContent(ref StreamReader reader) - reads message fields from stream
    /// Both internally create SpanReader via CreateSubReader and delegate to SpanReaders.
    /// </summary>
    internal class StreamReaderGenerator : GeneratorBase
    {
        private const string ReaderType = "global::GProtobuf.Core.StreamReader";
        private const string SpanReaderType = "global::GProtobuf.Core.SpanReader";
        private const string ClassName = "StreamReaders";

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry)
            : base(sb, registry)
        {
        }

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : base(sb, registry, virtualMapRegistry)
        {
        }

        public StreamReaderGenerator(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, VirtualTupleTypeRegistry virtualTupleRegistry)
            : base(sb, registry, virtualMapRegistry, virtualTupleRegistry, passRegistryToPrimitiveHandler: true)
        {
        }

        #region Constructor Analysis

        /// <summary>
        /// Analyzes the best constructor strategy for a type.
        /// For readonly structs with readonly fields, finds matching constructor.
        /// </summary>
        private ConstructorMatcher.ConstructorMatchResult AnalyzeConstructorStrategy(TypeDefinition type)
        {
            // If TypeSymbol not available, fallback to old behavior
            if (type.TypeSymbol == null)
            {
                if (type.HasParameterlessConstructor)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseParameterlessConstructor = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else if (!type.IsStruct)
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        UseFormatterServices = true,
                        ParameterMappings = new List<ConstructorMatcher.ParameterMapping>()
                    };
                }
                else
                {
                    return new ConstructorMatcher.ConstructorMatchResult
                    {
                        ErrorMessage = $"Struct '{type.FullName}' requires TypeSymbol for constructor analysis"
                    };
                }
            }

            // Collect ProtoMember field information
            var protoFields = new List<ConstructorMatcher.FieldInfo>();
            if (type.ProtoMembers != null)
            {
                foreach (var protoMember in type.ProtoMembers)
                {
                    // Find corresponding field or property in TypeSymbol
                    var member = type.TypeSymbol.GetMembers(protoMember.Name).FirstOrDefault();

                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = field.Type,
                            IsReadonly = field.IsReadOnly
                        });
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        protoFields.Add(new ConstructorMatcher.FieldInfo
                        {
                            FieldId = protoMember.FieldId,
                            Name = protoMember.Name,
                            Type = property.Type,
                            IsReadonly = property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public
                        });
                    }
                }
            }

            // Use ConstructorMatcher to find best constructor
            return ConstructorMatcher.FindBestConstructor(type.TypeSymbol, protoFields);
        }

        #endregion

        /// <summary>
        /// Generates complete StreamReaders class for all types.
        /// </summary>
        public void GenerateAll(IEnumerable<TypeDefinition> types, string currentNamespace = null)
        {
            _currentNamespace = currentNamespace ?? string.Empty;

            _sb.AppendIndentedLine($"public static class {ClassName}");
            _sb.StartNewBlock();

            foreach (var type in types)
            {
                GenerateReadMethod(type);
                GenerateReadContentMethod(type);
                GeneratePopulateMethod(type);
            }

            // Generate ReadContent methods for ProtoInclude derived types
            var processedTypes = new HashSet<string>(types.Select(t => t.FullName));
            var protoIncludeTypes = CollectUnprocessedProtoIncludeTypes(processedTypes);

            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                var protoIncludeType = _registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    GenerateReadContentMethod(protoIncludeType);
                    processedTypes.Add(protoIncludeTypeName);
                }
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #region Read Method

        /// <summary>
        /// Generates Read{ClassName}(ref StreamReader reader) method.
        /// Entry point for deserialization from Stream.
        /// </summary>
        private void GenerateReadMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}(ref {ReaderType} reader)");
            _sb.StartNewBlock();

            // Simple delegation pattern:
            // For messages without known length prefix, we read field by field
            // For messages with length prefix, we can create a sub-reader

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                // Simple case - no inheritance
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
            else
            {
                // Has inheritance - delegate to SpanReaders after reading full message
                GenerateReadMethodWithInheritance(type, className, nsPrefix);
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateReadMethodWithInheritance(TypeDefinition type, string className, string nsPrefix)
        {
            bool hasProtoInclude = type.ProtoIncludes != null && type.ProtoIncludes.Count > 0;
            bool isProtoIncludeDerived = _registry.IsDerivedType(type.FullName);

            if (isProtoIncludeDerived || hasProtoInclude)
            {
                // Complex inheritance - read remaining data and delegate to SpanReaders
                // SpanReaders handles ProtoInclude fields and base class fields correctly
                _sb.AppendIndentedLine("// Inheritance requires reading all content to determine actual type");
                _sb.AppendIndentedLine("var remainingData = reader.ReadRemainingBytes();");
                _sb.AppendIndentedLine($"var spanReader = new {SpanReaderType}(remainingData);");
                _sb.AppendIndentedLine($"return {nsPrefix}SpanReaders.Read{className}(ref spanReader);");
            }
            else
            {
                _sb.AppendIndentedLine($"return Read{className}Content(ref reader);");
            }
        }

        #endregion

        #region ReadContent Method

        private void GenerateReadContentMethod(TypeDefinition type)
        {
            var className = TypeNameHelper.GetClassName(type.FullName);
            var nsPrefix = GeneratorHelpers.GetNamespacePrefix(_registry.GetNamespaceForType(type.FullName), _currentNamespace);

            _sb.AppendIndentedLine($"public static global::{type.FullName} Read{className}Content(ref {ReaderType} reader)");
            _sb.StartNewBlock();

            if (type.IsEnum)
            {
                _sb.AppendIndentedLine($"return (global::{type.FullName})reader.ReadVarInt32();");
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Enter();");
            _sb.AppendIndentedLine("try");
            _sb.StartNewBlock();

            bool hasInheritance = GeneratorHelpers.HasInheritance(type, _registry);

            if (!hasInheritance)
            {
                GenerateSimpleReadContent(type, className, nsPrefix);
            }
            else
            {
                GenerateReadContentWithInheritance(type, className, nsPrefix);
            }

            _sb.EndBlock();
            _sb.AppendIndentedLine("finally");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("global::GProtobuf.Core.RecursionGuard.Exit();");
            _sb.EndBlock();

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        private void GenerateSimpleReadContent(TypeDefinition type, string className, string nsPrefix)
        {
            // Analyze constructor strategy for readonly struct support
            var constructorStrategy = AnalyzeConstructorStrategy(type);

            if (!constructorStrategy.IsSuccess)
            {
                // Constructor matching failed - generate error comment and skip
                _sb.AppendIndentedLine($"// ERROR: {constructorStrategy.ErrorMessage}");
                _sb.AppendIndentedLine("reader.SkipField(global::GProtobuf.Core.WireType.VarInt); // Skip all fields due to constructor error");
                _sb.AppendIndentedLine($"return default(global::{type.FullName});");
                return;
            }

            bool useConstructor = constructorStrategy.Constructor != null &&
                                 constructorStrategy.ParameterMappings != null &&
                                 constructorStrategy.ParameterMappings.Count > 0;

            if (useConstructor)
            {
                // Generate deserialization with constructor call (for readonly structs)
                _sb.AppendIndentedLine($"// Using constructor with {constructorStrategy.ParameterMappings.Count} parameters");
                GenerateReadContentWithConstructor(type, className, constructorStrategy);
                return;
            }

            GenerateObjectCreation(type, "result");
            _sb.AppendIndentedLine($"Populate{className}(ref reader, result);");
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates read content for types that use constructor with parameters (readonly struct support).
        /// Creates local variables for constructor parameters and calls constructor at the end.
        /// </summary>
        private void GenerateReadContentWithConstructor(
            TypeDefinition type,
            string className,
            ConstructorMatcher.ConstructorMatchResult constructorStrategy)
        {
            var fullTypeName = $"global::{type.FullName}";
            var mappings = constructorStrategy.ParameterMappings!;

            // Declare local variables for constructor parameters
            _sb.AppendIndentedLine("// Local variables for constructor parameters");
            foreach (var mapping in mappings.OrderBy(m => m.ParameterOrdinal))
            {
                // Use fully qualified type name
                var paramTypeName = mapping.FieldType.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
                _sb.AppendIndentedLine($"{paramTypeName} param_{mapping.ParameterName} = default;");
            }
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
                    // Find mapping for this field
                    var mapping = mappings.FirstOrDefault(m => m.FieldName == member.Name);

                    if (mapping != null)
                    {
                        // This field corresponds to a constructor parameter
                        GenerateFieldReadCaseForParameter(member, $"param_{mapping.ParameterName}");
                    }
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

            // Call constructor with parameters
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"// Create instance using constructor");
            var constructorParams = mappings
                .OrderBy(m => m.ParameterOrdinal)
                .Select(m => $"param_{m.ParameterName}")
                .ToList();

            if (constructorParams.Count == 0)
            {
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}();");
            }
            else if (constructorParams.Count <= 3)
            {
                // Inline for short parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}({string.Join(", ", constructorParams)});");
            }
            else
            {
                // Multi-line for long parameter lists
                _sb.AppendIndentedLine($"{fullTypeName} result = new {fullTypeName}(");
                _sb.IncreaseIndent();
                for (int i = 0; i < constructorParams.Count; i++)
                {
                    var comma = i < constructorParams.Count - 1 ? "," : ");";
                    _sb.AppendIndentedLine($"{constructorParams[i]}{comma}");
                }
                _sb.DecreaseIndent();
            }

            _sb.AppendNewLine();
            _sb.AppendIndentedLine("return result;");
        }

        /// <summary>
        /// Generates field read case that assigns to a local variable instead of object field.
        /// Used for constructor-based deserialization.
        /// </summary>
        private void GenerateFieldReadCaseForParameter(ProtoMemberAttribute member, string targetVariable)
        {
            _sb.AppendIndentedLine($"case {member.FieldId}:");
            _sb.IncreaseIndent();

            var normalizedType = TypeMapping.NormalizeTypeName(member.Type);
            var isZigZag = member.DataFormat == DataFormat.ZigZag;

            // Generate read statement based on type
            switch (normalizedType)
            {
                case "System.Int32":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedInt32();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;

                case "System.UInt32":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedUInt32();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;

                case "System.Int64":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedInt64();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;

                case "System.UInt64":
                    if (member.DataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"{targetVariable} = reader.ReadFixedUInt64();");
                    else
                        _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;

                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;

                case "System.Single":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;

                case "System.Double":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;

                case "System.String":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                    break;

                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                    break;

                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;

                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;

                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetVariable} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;

                default:
                    // Check if it's an enum
                    if (_registry != null && (_registry.IsEnum(member.Type) || _registry.IsEnum(normalizedType)))
                    {
                        _sb.AppendIndentedLine($"{targetVariable} = (global::{member.Type})reader.ReadVarInt32();");
                    }
                    else if (member.IsProtoVarint)
                    {
                        // ProtoVarint type
                        ProtoVarintTypeSupport.GenerateRead(_sb, member, targetVariable, "reader");
                    }
                    else
                    {
                        // Complex type - read via SpanReader
                        _sb.AppendIndentedLine("var nestedLength = reader.ReadVarInt32();");
                        _sb.AppendIndentedLine("var nestedReader = reader.CreateSubReader(nestedLength);");

                        var simpleName = Helpers.TypeNameHelper.GetClassName(member.Type);
                        var typeNs = _registry?.GetNamespaceForType(member.Type) ?? string.Empty;
                        var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);

                        _sb.AppendIndentedLine($"{targetVariable} = {typeNsPrefix}SpanReaders.Read{simpleName}Content(ref nestedReader);");
                    }
                    break;
            }

            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();
        }

        private void GenerateReadContentWithInheritance(TypeDefinition type, string className, string nsPrefix)
        {
            if (type.IsAbstract)
            {
                _sb.AppendIndentedLine($"global::{type.FullName} result = default(global::{type.FullName});");
            }
            else
            {
                GenerateObjectCreation(type, "result");
            }
            _sb.AppendNewLine();

            // Track array fields that need temp list
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

            _sb.AppendIndentedLine("while (!reader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var fieldId);");
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("switch (fieldId)");
            _sb.StartNewBlock();

            if (type.ProtoIncludes != null && type.ProtoIncludes.Count > 0)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    GenerateProtoIncludeReadCase(type, include, nsPrefix);
                }
            }

            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    GenerateFieldReadCase(member, nsPrefix);
                }
            }

            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("reader.SkipField(wireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
            _sb.EndBlock();

            // Convert temp lists to arrays if needed
            if (fieldsNeedingTempList != null && fieldsNeedingTempList.Count > 0)
            {
                _sb.AppendNewLine();
                foreach (var member in fieldsNeedingTempList)
                {
                    _sb.AppendIndentedLine($"if (_tempList_{member.Name} != null)");
                    _sb.StartNewBlock();
                    if (member.CollectionKind == CollectionKind.Array)
                    {
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name}.ToArray();");
                    }
                    else
                    {
                        _sb.AppendIndentedLine($"result.{member.Name} = _tempList_{member.Name};");
                    }
                    _sb.EndBlock();
                }
            }

            _sb.AppendIndentedLine("return result;");
        }

        #endregion

        #region Field Generation

        private void GenerateFieldReadCase(ProtoMemberAttribute member, string nsPrefix)
        {
            var category = GeneratorHelpers.GetFieldCategory(member, _primitiveHandler);

            bool needsBraces = category == FieldCategory.Map ||
                              category == FieldCategory.Collection ||
                              category == FieldCategory.ComplexType ||
                              category == FieldCategory.Tuple ||
                              category == FieldCategory.ProtoVarint;

            if (needsBraces)
            {
                _sb.AppendIndentedLine($"case {member.FieldId}: {{");
                _sb.IncreaseIndent();
            }
            else
            {
                _sb.AppendIndentedLine($"case {member.FieldId}:");
                _sb.IncreaseIndent();
            }

            switch (category)
            {
                case FieldCategory.Map:
                    GenerateMapFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Collection:
                    GenerateCollectionFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Enum:
                    _sb.AppendIndentedLine($"result.{member.Name} = (global::{member.Type})reader.ReadVarInt32();");
                    break;
                case FieldCategory.Tuple:
                    GenerateTupleFieldReadBody(member, nsPrefix);
                    break;
                case FieldCategory.Primitive:
                    GeneratePrimitiveFieldReadBody(member);
                    break;
                case FieldCategory.ProtoVarint:
                    ProtoVarintTypeSupport.GenerateRead(_sb, member, $"result.{member.Name}", "reader");
                    break;
                case FieldCategory.Unsupported:
                    _sb.AppendIndentedLine($"// WARNING: Field '{member.Name}' with type '{member.Type}' is unsupported");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
                case FieldCategory.ComplexType:
                    GenerateComplexTypeReadBody(member, nsPrefix);
                    break;
            }

            _sb.AppendIndentedLine("break;");

            if (needsBraces)
            {
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
            }
            else
            {
                _sb.DecreaseIndent();
            }
        }

        private void GeneratePrimitiveFieldReadBody(ProtoMemberAttribute member)
        {
            // Use StreamReaders extension methods which mirror SpanReaders
            var typeName = TypeMapping.NormalizeTypeName(member.Type);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (typeName)
            {
                case "System.Int32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedInt32();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedUInt32();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedInt64();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    if (dataFormat == DataFormat.FixedSize)
                        _sb.AppendIndentedLine($"result.{member.Name} = reader.ReadFixedUInt64();");
                    else
                        _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Int16":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt16(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt16":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt16(ref reader, wireType);");
                    break;
                case "System.Byte":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByte(ref reader, wireType);");
                    break;
                case "System.SByte":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadSByte(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"result.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType);");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader);");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    // Nullable types
                    if (typeName.StartsWith("System.Nullable<"))
                    {
                        var innerType = typeName.Substring("System.Nullable<".Length, typeName.Length - "System.Nullable<".Length - 1);
                        GeneratePrimitiveFieldReadBodyForType(member, innerType);
                    }
                    else
                    {
                        _sb.AppendIndentedLine($"// WARNING: Unknown primitive type '{typeName}'");
                        _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    }
                    break;
            }
        }

        private void GeneratePrimitiveFieldReadBodyForType(ProtoMemberAttribute member, string innerType)
        {
            var normalizedInnerType = TypeMapping.NormalizeTypeName(innerType);
            var dataFormat = member.DataFormat;
            bool isZigZag = dataFormat == DataFormat.ZigZag;

            switch (normalizedInnerType)
            {
                case "System.Int32":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt32(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt32":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt32(ref reader, wireType);");
                    break;
                case "System.Int64":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadInt64(ref reader, wireType, {isZigZag.ToString().ToLower()});");
                    break;
                case "System.UInt64":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadUInt64(ref reader, wireType);");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDouble(ref reader, wireType);");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadFloat(ref reader, wireType);");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadBool(ref reader, wireType);");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"result.{member.Name} = (char)reader.ReadVarUInt32();");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType);");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType);");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"result.{member.Name} = global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unknown nullable inner type '{normalizedInnerType}'");
                    _sb.AppendIndentedLine("reader.SkipField(wireType);");
                    break;
            }
        }

        private void GenerateMapFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // Read map entry as nested message, delegate to SpanReaders
            _sb.AppendIndentedLine("var mapLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var mapReader = reader.CreateSubReader(mapLength);");

            // Get map key/value types
            var keyType = TypeMapping.GetShortTypeName(member.MapKeyType);
            var valueType = TypeMapping.GetShortTypeName(member.MapValueType);

            // Check if target is List<KeyValuePair> vs Dictionary
            bool isKeyValuePairCollection = TypeHelper.IsKeyValuePairCollection(member.Type);
            var dictCreationType = TypeHelper.GetDictionaryCreationType(member.Type, member.MapKeyType, member.MapValueType);

            _sb.AppendIndentedLine($"if (result.{member.Name} == null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"result.{member.Name} = new {dictCreationType}();");
            _sb.EndBlock();

            // Generate inline map entry reading using SpanReader
            _sb.AppendIndentedLine($"{keyType} key = default;");
            _sb.AppendIndentedLine($"{valueType} value = default;");

            _sb.AppendIndentedLine("while (!mapReader.IsEnd)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("mapReader.ReadWireTypeAndFieldId(out var entryWireType, out var entryFieldId);");
            _sb.AppendIndentedLine("switch (entryFieldId)");
            _sb.StartNewBlock();

            // Field 1 = Key
            _sb.AppendIndentedLine("case 1:");
            _sb.IncreaseIndent();
            GenerateMapKeyOrValueRead("key", member.MapKeyType, member.MapKeyEnumUnderlyingType, "mapReader", "entryWireType");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            // Field 2 = Value
            _sb.AppendIndentedLine("case 2:");
            _sb.IncreaseIndent();
            GenerateMapKeyOrValueRead("value", member.MapValueType, member.MapValueEnumUnderlyingType, "mapReader", "entryWireType", nsPrefix);
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.AppendIndentedLine("default:");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine("mapReader.SkipField(entryWireType);");
            _sb.AppendIndentedLine("break;");
            _sb.DecreaseIndent();

            _sb.EndBlock();
            _sb.EndBlock();

            // Add entry to collection
            if (isKeyValuePairCollection)
            {
                _sb.AppendIndentedLine($"result.{member.Name}.Add(new global::System.Collections.Generic.KeyValuePair<{member.MapKeyType}, {member.MapValueType}>(key, value));");
            }
            else
            {
                _sb.AppendIndentedLine($"result.{member.Name}[key] = value;");
            }
        }

        private void GenerateMapKeyOrValueRead(string varName, string typeName, string enumUnderlyingType, string readerVar, string wireTypeVar, string nsPrefix = "")
        {
            var normalizedType = TypeMapping.NormalizeTypeName(typeName);

            // Check if it's an enum
            if (!string.IsNullOrEmpty(enumUnderlyingType) || _registry.IsEnum(typeName) || _registry.IsEnum(normalizedType))
            {
                _sb.AppendIndentedLine($"{varName} = (global::{typeName}){readerVar}.ReadVarInt32();");
                return;
            }

            // Check for array types
            if (typeName.EndsWith("[]"))
            {
                GenerateMapArrayValueRead(varName, typeName, readerVar);
                return;
            }

            // Check for nested Dictionary types BEFORE List/Collection
            // (because IsListType matches any string containing "List<")
            if (TypeHelper.IsDictionaryType(typeName))
            {
                GenerateMapNestedDictionaryValueRead(varName, typeName, readerVar);
                return;
            }

            // Check for List/Collection types (including custom collections like ValueLogTypeHashSet)
            if (TypeHelper.IsListType(typeName) || TypeHelper.IsHashSetType(typeName) ||
                TypeHelper.IsCustomHashSetType(typeName) || TypeHelper.IsCustomListType(typeName))
            {
                GenerateMapCollectionValueRead(varName, typeName, readerVar);
                return;
            }

            // Check for Tuple types - use current namespace's SpanReaders
            if (typeName.StartsWith("System.Tuple<") || typeName.StartsWith("System.ValueTuple<") ||
                typeName.StartsWith("(") || normalizedType.Contains("Tuple<"))
            {
                GenerateMapTupleValueRead(varName, typeName, readerVar);
                return;
            }

            switch (normalizedType)
            {
                case "System.Int32":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadVarInt32();");
                    break;
                case "System.UInt32":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadVarUInt32();");
                    break;
                case "System.Int64":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadVarInt64();");
                    break;
                case "System.UInt64":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadVarUInt64();");
                    break;
                case "System.Int16":
                    _sb.AppendIndentedLine($"{varName} = (short){readerVar}.ReadVarInt32();");
                    break;
                case "System.UInt16":
                    _sb.AppendIndentedLine($"{varName} = (ushort){readerVar}.ReadVarUInt32();");
                    break;
                case "System.Byte":
                    _sb.AppendIndentedLine($"{varName} = (byte){readerVar}.ReadVarUInt32();");
                    break;
                case "System.SByte":
                    _sb.AppendIndentedLine($"{varName} = (sbyte){readerVar}.ReadVarInt32();");
                    break;
                case "System.Char":
                    _sb.AppendIndentedLine($"{varName} = (char){readerVar}.ReadVarUInt32();");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"{varName} = global::GProtobuf.Core.SpanReaders.ReadString(ref {readerVar}, {wireTypeVar});");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadVarInt32() != 0;");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadFixedDouble();");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.ReadFixedFloat();");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine("{");
                    _sb.IncreaseIndent();
                    _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                    _sb.AppendIndentedLine($"{varName} = {readerVar}.GetSlice({varName}Len).ToArray();");
                    _sb.DecreaseIndent();
                    _sb.AppendIndentedLine("}");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{varName} = global::GProtobuf.Core.SpanReaders.ReadGuid(ref {readerVar}, {wireTypeVar});");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{varName} = global::GProtobuf.Core.SpanReaders.ReadDateTime(ref {readerVar}, {wireTypeVar});");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{varName} = global::GProtobuf.Core.SpanReaders.ReadTimeSpan(ref {readerVar}, {wireTypeVar});");
                    break;
                default:
                    // Complex type - use SpanReaders with unique variable names
                    GenerateMapComplexTypeValueRead(varName, typeName, readerVar);
                    break;
            }
        }

        private void GenerateMapArrayValueRead(string varName, string typeName, string readerVar)
        {
            var elementType = typeName.Substring(0, typeName.Length - 2);
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);
            var shortElementType = TypeMapping.GetShortTypeName(elementType);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Try to use optimized packed array read
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, readerVar);
            if (packedReadExpr != null)
            {
                _sb.AppendIndentedLine($"{varName} = {packedReadExpr};");
            }
            else
            {
                // Fallback for types without optimized packed read
                _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var {varName}End = {readerVar}.Position + {varName}Len;");
                _sb.AppendIndentedLine($"var {varName}List = new global::System.Collections.Generic.List<{shortElementType}>();");
                _sb.AppendIndentedLine($"while ({readerVar}.Position < {varName}End)");
                _sb.StartNewBlock();

                var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, readerVar);
                if (elementReadExpr != null)
                {
                    _sb.AppendIndentedLine($"{varName}List.Add({elementReadExpr});");
                }
                else if (normalizedElementType == "System.String")
                {
                    _sb.AppendIndentedLine($"{varName}List.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref {readerVar}, global::GProtobuf.Core.WireType.Len));");
                }
                else
                {
                    _sb.AppendIndentedLine($"// Unsupported array element type: {elementType}");
                }

                _sb.EndBlock();
                _sb.AppendIndentedLine($"{varName} = {varName}List.ToArray();");
            }

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GenerateMapCollectionValueRead(string varName, string typeName, string readerVar)
        {
            // Check for custom collections without generic parameter (e.g., ValueLogTypeHashSet)
            // These implement ICollection<T> but don't have <T> in type name
            bool isCustomNonGenericCollection = (TypeHelper.IsCustomHashSetType(typeName) || TypeHelper.IsCustomListType(typeName)) &&
                                                 !typeName.Contains("<");
            if (isCustomNonGenericCollection)
            {
                // Custom non-generic collections cannot be parsed from type name alone
                // They need metadata from ICollection<T> interface which is not available here
                // Generate a skip with warning
                _sb.AppendIndentedLine("{");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"// WARNING: Custom collection type '{typeName}' without generic parameter cannot be deserialized as Map key/value");
                _sb.AppendIndentedLine($"// Consider using standard HashSet<T> or adding ProtoContract to '{typeName}'");
                _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"_ = {readerVar}.GetSlice({varName}Len); // Skip bytes");
                _sb.AppendIndentedLine($"{varName} = new global::{typeName}();");
                _sb.DecreaseIndent();
                _sb.AppendIndentedLine("}");
                return;
            }

            var elementType = TypeHelper.GetCollectionElementType(typeName);
            var normalizedElementType = TypeMapping.NormalizeTypeName(elementType);
            var shortElementType = TypeMapping.GetShortTypeName(elementType);
            bool isHashSet = TypeHelper.IsHashSetType(typeName);
            bool isCustomCollection = TypeHelper.IsCustomHashSetType(typeName) || TypeHelper.IsCustomListType(typeName);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Try to use optimized packed array read, then convert to collection
            var packedReadExpr = TypeMapping.GetPackedArrayReadExpression(elementType, DataFormat.Default, readerVar);
            if (packedReadExpr != null && !isCustomCollection)
            {
                if (isHashSet)
                {
                    _sb.AppendIndentedLine($"{varName} = new global::System.Collections.Generic.HashSet<{shortElementType}>({packedReadExpr});");
                }
                else
                {
                    _sb.AppendIndentedLine($"{varName} = new global::System.Collections.Generic.List<{shortElementType}>({packedReadExpr});");
                }
            }
            else
            {
                // Fallback for types without optimized packed read
                _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var {varName}End = {readerVar}.Position + {varName}Len;");

                // Determine collection creation type
                string collectionCreationType;
                if (isCustomCollection)
                {
                    collectionCreationType = $"global::{typeName}";
                }
                else if (isHashSet)
                {
                    collectionCreationType = $"global::System.Collections.Generic.HashSet<{shortElementType}>";
                }
                else
                {
                    collectionCreationType = $"global::System.Collections.Generic.List<{shortElementType}>";
                }
                _sb.AppendIndentedLine($"var {varName}Collection = new {collectionCreationType}();");

                _sb.AppendIndentedLine($"while ({readerVar}.Position < {varName}End)");
                _sb.StartNewBlock();

                var elementReadExpr = TypeMapping.GetElementReadExpression(elementType, DataFormat.Default, readerVar);
                if (elementReadExpr != null)
                {
                    _sb.AppendIndentedLine($"{varName}Collection.Add({elementReadExpr});");
                }
                else if (normalizedElementType == "System.String")
                {
                    _sb.AppendIndentedLine($"{varName}Collection.Add(global::GProtobuf.Core.SpanReaders.ReadString(ref {readerVar}, global::GProtobuf.Core.WireType.Len));");
                }
                else
                {
                    _sb.AppendIndentedLine($"// Unsupported collection element type: {elementType}");
                }

                _sb.EndBlock();
                _sb.AppendIndentedLine($"{varName} = {varName}Collection;");
            }

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GenerateMapNestedDictionaryValueRead(string varName, string typeName, string readerVar)
        {
            // For nested dictionaries, delegate to the generated KeyValue reader via SpanReaders
            var (dictKeyType, dictValueType) = TypeHelper.ParseDictionaryTypes(typeName);
            var mapEntryTypeName = VirtualTypeNameGenerator.GetMapEntryTypeName(dictKeyType, dictValueType);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}Reader = new global::GProtobuf.Core.SpanReader({readerVar}.GetSlice({varName}Len));");
            _sb.AppendIndentedLine($"var {varName}Entry = SpanReaders.Read{mapEntryTypeName}(ref {varName}Reader);");
            _sb.AppendIndentedLine($"if ({varName}Entry.success)");
            _sb.StartNewBlock();

            // Use original dictionary type if it's a custom type (ConcurrentDictionary, ListDictionary, etc.)
            string dictCreationType;
            if (TypeHelper.IsCustomDictionaryType(typeName))
            {
                dictCreationType = $"global::{typeName}";
            }
            else
            {
                var keyType = TypeMapping.GetShortTypeName(dictKeyType);
                var valueType = TypeMapping.GetShortTypeName(dictValueType);
                dictCreationType = $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
            }
            _sb.AppendIndentedLine($"{varName} = new {dictCreationType}();");
            _sb.AppendIndentedLine($"{varName}[{varName}Entry.key] = {varName}Entry.value;");
            _sb.EndBlock();
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GenerateMapComplexTypeValueRead(string varName, string typeName, string readerVar)
        {
            var className = TypeNameHelper.GetClassName(typeName);
            var typeNs = _registry.GetNamespaceForType(typeName);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNs, _currentNamespace);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}Reader = new global::GProtobuf.Core.SpanReader({readerVar}.GetSlice({varName}Len));");
            _sb.AppendIndentedLine($"{varName} = {typeNsPrefix}SpanReaders.Read{className}Content(ref {varName}Reader);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GenerateMapTupleValueRead(string varName, string typeName, string readerVar)
        {
            // For tuple types, use the TupleHandler via current namespace's SpanReaders
            // Generate: Read{TupleSafeName}Content(ref reader)
            var tupleSafeName = VirtualTypeNameGenerator.GetSafeTypeName(typeName);

            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();
            _sb.AppendIndentedLine($"var {varName}Len = {readerVar}.ReadVarInt32();");
            _sb.AppendIndentedLine($"var {varName}Reader = new global::GProtobuf.Core.SpanReader({readerVar}.GetSlice({varName}Len));");
            // Use current namespace's SpanReaders for tuple reading
            _sb.AppendIndentedLine($"{varName} = SpanReaders.Read{tupleSafeName}Content(ref {varName}Reader);");
            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        private void GenerateCollectionFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            var normalizedType = TypeMapping.NormalizeTypeName(member.CollectionElementType);
            bool isEnumCollection = _registry != null && (_registry.IsEnum(member.CollectionElementType) || _registry.IsEnum(normalizedType));
            bool isPrimitiveCollection = _primitiveHandler.CanHandleCollection(member.CollectionElementType) || isEnumCollection;

            if (isPrimitiveCollection)
            {
                GeneratePrimitiveCollectionRead(member, isEnumCollection);
            }
            else
            {
                GenerateComplexCollectionRead(member, nsPrefix);
            }
        }

        private void GeneratePrimitiveCollectionRead(ProtoMemberAttribute member, bool isEnumCollection)
        {
            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);

            // Check for custom collection types (ValueLogTypeHashSet, etc.)
            // Note: IList<T>, ICollection<T> are NOT custom collections - they should use List<T>
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);
            bool isInterfaceCollection = normalizedMemberType.Contains("IList<") ||
                                         normalizedMemberType.Contains("ICollection<");
            bool isCustomCollection = !isInterfaceCollection &&
                                      (TypeHelper.IsCustomHashSetType(normalizedMemberType) ||
                                       TypeHelper.IsCustomListType(normalizedMemberType));

            // Determine if we need temp list
            // Custom collections don't use temp list - they implement ICollection and have Add method
            bool needsTempList = !isCustomCollection && (
                member.CollectionKind == CollectionKind.Array ||
                (member.CollectionKind == CollectionKind.InterfaceCollection &&
                 normalizedMemberType.StartsWith("System.Collections.Generic.IEnumerable<")));

            // Determine collection type name for initialization
            string collectionTypeName;
            if (isCustomCollection)
            {
                // Use original custom type
                collectionTypeName = $"global::{member.Type}";
            }
            else
            {
                bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                collectionTypeName = isHashSet
                    ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                    : $"global::System.Collections.Generic.List<{elementType}>";
            }

            string targetCollection = needsTempList ? $"_tempList_{member.Name}" : $"result.{member.Name}";

            if (needsTempList)
            {
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if (result.{member.Name} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"result.{member.Name} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            // Check if element type can be packed (only numeric/bool types can be packed)
            bool isPackable = IsPackableElementType(normalizedElementType) || isEnumCollection;

            if (isPackable)
            {
                // Check if packed
                _sb.AppendIndentedLine("if (wireType == global::GProtobuf.Core.WireType.Len)");
                _sb.StartNewBlock();

                // Packed - read as sub-reader
                _sb.AppendIndentedLine("var packedLength = reader.ReadVarInt32();");
                _sb.AppendIndentedLine("var packedReader = reader.CreateSubReader(packedLength);");
                _sb.AppendIndentedLine("while (!packedReader.IsEnd)");
                _sb.StartNewBlock();

                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})packedReader.ReadVarInt32());");
                }
                else
                {
                    GeneratePackedElementRead(targetCollection, normalizedElementType, member.DataFormat, "packedReader");
                }

                _sb.EndBlock();
                _sb.EndBlock();

                _sb.AppendIndentedLine("else");
                _sb.StartNewBlock();

                // Non-packed - single element
                if (isEnumCollection)
                {
                    _sb.AppendIndentedLine($"{targetCollection}.Add((global::{member.CollectionElementType})reader.ReadVarInt32());");
                }
                else
                {
                    GenerateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
                }

                _sb.EndBlock();
            }
            else
            {
                // Non-packable types (string, bytes) - just read directly
                GenerateSingleElementRead(targetCollection, normalizedElementType, member.DataFormat);
            }
        }

        /// <summary>
        /// Determines if an element type can be packed in protobuf wire format.
        /// Only numeric types and booleans can be packed. Strings, bytes, and messages cannot.
        /// </summary>
        private static bool IsPackableElementType(string normalizedElementType)
        {
            return normalizedElementType switch
            {
                "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => true,
                "System.Int16" or "System.UInt16" or "System.Byte" or "System.SByte" => true,
                "System.Double" or "System.Single" => true,
                "System.Boolean" => true,
                "System.Char" => true,
                _ => false // strings, bytes, messages, etc. cannot be packed
            };
        }

        private void GeneratePackedElementRead(string targetCollection, string elementType, DataFormat dataFormat, string readerVar)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add({readerVar}.ReadVarInt32() != 0);");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported packed element type: {elementType}");
                    break;
            }
        }

        private void GenerateSingleElementRead(string targetCollection, string elementType, DataFormat dataFormat)
        {
            bool isZigZag = dataFormat == DataFormat.ZigZag;
            bool isFixedSize = dataFormat == DataFormat.FixedSize;

            switch (elementType)
            {
                case "System.Int32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt32());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32());");
                    break;
                case "System.UInt32":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt32());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt32());");
                    break;
                case "System.Int64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedInt64());");
                    else if (isZigZag)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadZigZagVarInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt64());");
                    break;
                case "System.UInt64":
                    if (isFixedSize)
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedUInt64());");
                    else
                        _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarUInt64());");
                    break;
                case "System.Double":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedDouble());");
                    break;
                case "System.Single":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadFixedFloat());");
                    break;
                case "System.Boolean":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(reader.ReadVarInt32() != 0);");
                    break;
                case "System.String":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadString(ref reader, wireType));");
                    break;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader));");
                    break;
                case "System.Guid":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadGuid(ref reader, wireType));");
                    break;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, wireType));");
                    break;
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, wireType));");
                    break;
                default:
                    _sb.AppendIndentedLine($"// WARNING: Unsupported single element type: {elementType}");
                    break;
            }
        }

        private void GenerateComplexCollectionRead(ProtoMemberAttribute member, string nsPrefix)
        {
            var elementType = TypeMapping.GetShortTypeName(member.CollectionElementType);
            var elementClassName = TypeNameHelper.GetClassName(member.CollectionElementType);
            var normalizedElementType = TypeMapping.NormalizeTypeName(member.CollectionElementType);

            // Check for custom collection types (ValueLogTypeHashSet, etc.)
            // Note: IList<T>, ICollection<T> are NOT custom collections - they should use List<T>
            var normalizedMemberType = TypeMapping.NormalizeTypeName(member.Type);
            bool isInterfaceCollection = normalizedMemberType.Contains("IList<") ||
                                         normalizedMemberType.Contains("ICollection<");
            bool isCustomCollection = !isInterfaceCollection &&
                                      (TypeHelper.IsCustomHashSetType(normalizedMemberType) ||
                                       TypeHelper.IsCustomListType(normalizedMemberType));

            // Custom collections don't use temp list - they implement ICollection and have Add method
            bool needsTempList = !isCustomCollection && (
                member.CollectionKind == CollectionKind.Array ||
                (member.CollectionKind == CollectionKind.InterfaceCollection &&
                 normalizedMemberType.StartsWith("System.Collections.Generic.IEnumerable<")));

            // Determine collection type name for initialization
            string collectionTypeName;
            if (isCustomCollection)
            {
                // Use original custom type
                collectionTypeName = $"global::{member.Type}";
            }
            else
            {
                bool isHashSet = TypeHelper.IsHashSetType(normalizedMemberType);
                collectionTypeName = isHashSet
                    ? $"global::System.Collections.Generic.HashSet<{elementType}>"
                    : $"global::System.Collections.Generic.List<{elementType}>";
            }

            string targetCollection = needsTempList ? $"_tempList_{member.Name}" : $"result.{member.Name}";

            if (needsTempList)
            {
                _sb.AppendIndentedLine($"if ({targetCollection} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"{targetCollection} = new global::System.Collections.Generic.List<{elementType}>();");
                _sb.EndBlock();
            }
            else
            {
                _sb.AppendIndentedLine($"if (result.{member.Name} == null)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"result.{member.Name} = new {collectionTypeName}();");
                _sb.EndBlock();
            }

            // Handle special types (DateTime, Guid, TimeSpan, byte[]) that have predefined readers in GProtobuf.Core
            // These don't have generated ReadXxxContent methods
            switch (normalizedElementType)
            {
                case "System.DateTime":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadDateTime(ref reader, global::GProtobuf.Core.WireType.Len));");
                    return;
                case "System.TimeSpan":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadTimeSpan(ref reader, global::GProtobuf.Core.WireType.Len));");
                    return;
                case "System.Byte[]":
                    _sb.AppendIndentedLine($"{targetCollection}.Add(global::GProtobuf.Core.StreamReaders.ReadByteArray(ref reader));");
                    return;
            }

            // Read nested message via CreateSubReader -> SpanReader
            _sb.AppendIndentedLine("var itemLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var itemReader = reader.CreateSubReader(itemLength);");

            // Handle tuple types - use current namespace's SpanReaders (not System.Serialization)
            if (TupleHandler.IsTupleType(member.CollectionElementType))
            {
                var tupleSafeName = VirtualTypeNameGenerator.GetSafeTypeName(member.CollectionElementType);
                _sb.AppendIndentedLine($"{targetCollection}.Add(SpanReaders.Read{tupleSafeName}Content(ref itemReader));");
                return;
            }

            var elementNs = _registry.GetNamespaceForType(member.CollectionElementType);
            var elementNsPrefix = GeneratorHelpers.GetNamespacePrefix(elementNs, _currentNamespace);

            _sb.AppendIndentedLine($"{targetCollection}.Add({elementNsPrefix}SpanReaders.Read{elementClassName}Content(ref itemReader));");
        }

        private void GenerateTupleFieldReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            // For tuples, read length, create sub-reader, and call SpanReaders directly
            // (Don't use TupleHandler.GenerateTupleRead as it adds another level of length reading)
            var itemTypes = TupleHandler.ParseTupleTypes(member.Type);
            var tupleInfo = _virtualTupleRegistry.Register(member.Type, itemTypes);

            _sb.AppendIndentedLine("var tupleLength = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var tupleReader = reader.CreateSubReader(tupleLength);");
            _sb.AppendIndentedLine($"result.{member.Name} = SpanReaders.Read{tupleInfo.SafeName}Content(ref tupleReader);");
        }

        private void GenerateComplexTypeReadBody(ProtoMemberAttribute member, string nsPrefix)
        {
            var typeName = TypeNameHelper.GetClassName(member.Type);
            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = reader.CreateSubReader(length);");

            var typeNamespace = _registry.GetNamespaceForType(member.Type);
            var typeNsPrefix = GeneratorHelpers.GetNamespacePrefix(typeNamespace, _currentNamespace);

            // Use SpanReaders for nested content (CreateSubReader returns SpanReader)
            _sb.AppendIndentedLine($"result.{member.Name} = {typeNsPrefix}SpanReaders.Read{typeName}Content(ref nestedReader);");
        }

        private void GenerateProtoIncludeReadCase(TypeDefinition parentType, ProtoIncludeAttribute include, string nsPrefix)
        {
            var derivedClassName = TypeNameHelper.GetClassName(include.Type);
            var derivedNs = _registry.GetNamespaceForType(include.Type);
            var derivedNsPrefix = GeneratorHelpers.GetNamespacePrefix(derivedNs, _currentNamespace);

            _sb.AppendIndentedLine($"case {include.FieldId}: {{");
            _sb.IncreaseIndent();

            _sb.AppendIndentedLine("var length = reader.ReadVarInt32();");
            _sb.AppendIndentedLine("var nestedReader = reader.CreateSubReader(length);");

            _sb.AppendIndentedLine($"var oldResult = result;");

            // Use SpanReaders for derived content reading
            _sb.AppendIndentedLine($"result = {derivedNsPrefix}SpanReaders.Read{derivedClassName}Content(ref nestedReader);");

            _sb.AppendIndentedLine("if (oldResult != null)");
            _sb.StartNewBlock();
            if (parentType.ProtoMembers != null)
            {
                foreach (var member in parentType.ProtoMembers)
                {
                    _sb.AppendIndentedLine($"result.{member.Name} = oldResult.{member.Name};");
                }
            }
            _sb.EndBlock();

            _sb.AppendIndentedLine("continue;");

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        #endregion

        #region Populate Method

        /// <summary>
        /// Generates Populate{ClassName}(ref StreamReader reader, T instance) method.
        /// Delegates to SpanReaders.Populate by reading all remaining bytes.
        /// </summary>
        private void GeneratePopulateMethod(TypeDefinition type)
        {
            // Skip abstract types - can't populate them directly
            if (type.IsAbstract)
                return;

            var className = TypeNameHelper.GetClassName(type.FullName);

            // Check if this is a readonly struct with readonly fields
            bool isReadonlyStruct = false;
            if (type.IsStruct && type.TypeSymbol != null)
            {
                bool hasReadonlyFields = type.ProtoMembers?.Any(m =>
                {
                    var member = type.TypeSymbol.GetMembers(m.Name).FirstOrDefault();
                    if (member is Microsoft.CodeAnalysis.IFieldSymbol field)
                    {
                        return field.IsReadOnly;
                    }
                    else if (member is Microsoft.CodeAnalysis.IPropertySymbol property)
                    {
                        return property.SetMethod == null || property.SetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public;
                    }
                    return false;
                }) ?? false;

                isReadonlyStruct = hasReadonlyFields;
            }

            // Generate Populate method signature
            _sb.AppendIndentedLine($"public static void Populate{className}(ref {ReaderType} reader, global::{type.FullName} instance)");
            _sb.StartNewBlock();

            if (isReadonlyStruct)
            {
                // For readonly structs, Populate method is a no-op
                _sb.AppendIndentedLine("// Readonly struct - fields cannot be modified after construction");
                _sb.AppendIndentedLine("// This method consumes the reader but does not modify the instance");
                _sb.AppendIndentedLine("while (!reader.IsEnd)");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine("reader.ReadWireTypeAndFieldId(out var wireType, out var _);");
                _sb.AppendIndentedLine("reader.SkipField(wireType);");
                _sb.EndBlock();
                _sb.EndBlock();
                _sb.AppendNewLine();
                return;
            }

            // Populate implementation - delegate to SpanReaders by reading remaining data
            // Read all remaining data into buffer and use SpanReaders.Populate
            _sb.AppendIndentedLine("// Read remaining data and delegate to SpanReaders.Populate");
            _sb.AppendIndentedLine("var remainingData = reader.ReadRemainingBytes();");
            _sb.AppendIndentedLine("var spanReader = new SpanReader(remainingData);");
            _sb.AppendIndentedLine($"SpanReaders.Populate{className}(ref spanReader, instance);");

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        #endregion

        #region Helpers

        private void GenerateObjectCreation(TypeDefinition type, string variableName = "result")
        {
            var fullTypeName = $"global::{type.FullName}";

            if (type.HasParameterlessConstructor)
            {
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = new {fullTypeName}();");
            }
            else
            {
                _sb.AppendIndentedLine($"{fullTypeName} {variableName} = ({fullTypeName})");
                _sb.IncreaseIndent();
                _sb.AppendIndentedLine($"System.Runtime.Serialization.FormatterServices.GetUninitializedObject(");
                _sb.AppendIndentedLine($"    typeof({fullTypeName}));");
                _sb.DecreaseIndent();
            }
        }

        #endregion
    }
}
