using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using GProtobuf.Generator.V2;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GProtobuf.Generator;

[Generator]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
//#if DEBUG
//        if (!Debugger.IsAttached)
//        {
//            Debugger.Launch();
//        }
//#endif


        // Collect all enums from compilation
        var enumTypesProvider = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var enumTypes = new HashSet<string>();

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = syntaxTree.GetRoot();

                    var enumDeclarations = root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax>();

                    foreach (var enumDecl in enumDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(enumDecl);
                        if (symbol != null)
                        {
                            // Store fully qualified name
                            enumTypes.Add(symbol.ToDisplayString());
                        }
                    }
                }

                return enumTypes;
            });

        // Pipeline 1: Types with [ProtoContract]
        var protoContractPipeline = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "ProtoBuf.ProtoContractAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax,
            transform: static (syntaxContext, _) =>
            {
                var typeWithAttribute = (syntaxContext.TargetSymbol as INamedTypeSymbol)!;
                var namespaceName = syntaxContext.TargetSymbol.ContainingNamespace.ToDisplayString();
                var protoIncludes = GetProtoIncludeAttributes(typeWithAttribute);
                var protoMembers = GetProtoMemberAttributes(typeWithAttribute);
                var customBufferMembers = GetCustomBufferMembers(typeWithAttribute);
                var hasParameterlessConstructor = HasParameterlessConstructor(typeWithAttribute);
                var baseClass = GetBaseClass(typeWithAttribute);
                var enableRecursionGuard = GetEnableRecursionGuard(typeWithAttribute);

                // Check if this is a custom collection type (implements IEnumerable<T> + Add(T))
                // Only treat as custom collection if there are no ProtoMember fields
                // (if there are ProtoMembers, serialize as regular class with fields)
                bool isCustomCollection = false;
                string customCollectionElementType = null;
                if ((protoMembers == null || protoMembers.Count == 0) &&
                    (customBufferMembers == null || customBufferMembers.Count == 0))
                {
                    var collectionInfo = AnalyzeNonGenericCollection(typeWithAttribute);
                    if (collectionInfo.IsCollection)
                    {
                        isCustomCollection = true;
                        customCollectionElementType = collectionInfo.ElementType;
                    }
                }

                var typeDefinition = new TypeDefinition(
                    IsStruct: typeWithAttribute.TypeKind == Microsoft.CodeAnalysis.TypeKind.Struct,
                    IsAbstract: typeWithAttribute.IsAbstract,
                    IsEnum: typeWithAttribute.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum,
                    typeWithAttribute.ToDisplayString(),
                    protoIncludes,
                    protoMembers,
                    hasParameterlessConstructor,
                    TypeSymbol: typeWithAttribute,
                    BaseClass: baseClass,
                    CustomBufferMembers: customBufferMembers,
                    EnableRecursionGuard: enableRecursionGuard,
                    IsCustomCollection: isCustomCollection,
                    CustomCollectionElementType: customCollectionElementType);

                return (namespaceName, typeDefinition);
            });

        // Pipeline 2: Types with [ProtoInclude] but WITHOUT [ProtoContract]
        // This matches protobuf-net behavior where base classes with ProtoInclude don't need ProtoContract
        var protoIncludePipeline = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "ProtoBuf.ProtoIncludeAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax or StructDeclarationSyntax,
            transform: (syntaxContext, _) =>
            {
                var typeWithAttribute = (syntaxContext.TargetSymbol as INamedTypeSymbol)!;

                // Skip if already has ProtoContract (will be handled by first pipeline)
                if (typeWithAttribute.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoContractAttribute"))
                {
                    return ((string)null, (TypeDefinition)null)!;
                }

                var namespaceName = syntaxContext.TargetSymbol.ContainingNamespace.ToDisplayString();
                var protoIncludes = GetProtoIncludeAttributes(typeWithAttribute);
                var protoMembers = GetProtoMemberAttributes(typeWithAttribute);
                var customBufferMembers = GetCustomBufferMembers(typeWithAttribute);
                var hasParameterlessConstructor = HasParameterlessConstructor(typeWithAttribute);
                var baseClass = GetBaseClass(typeWithAttribute);

                // Check if this is a custom collection type
                bool isCustomCollection = false;
                string customCollectionElementType = null;
                if ((protoMembers == null || protoMembers.Count == 0) &&
                    (customBufferMembers == null || customBufferMembers.Count == 0))
                {
                    var collectionInfo = AnalyzeNonGenericCollection(typeWithAttribute);
                    if (collectionInfo.IsCollection)
                    {
                        isCustomCollection = true;
                        customCollectionElementType = collectionInfo.ElementType;
                    }
                }

                var typeDefinition = new TypeDefinition(
                    IsStruct: typeWithAttribute.TypeKind == Microsoft.CodeAnalysis.TypeKind.Struct,
                    IsAbstract: typeWithAttribute.IsAbstract,
                    IsEnum: typeWithAttribute.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum,
                    typeWithAttribute.ToDisplayString(),
                    protoIncludes,
                    protoMembers,
                    hasParameterlessConstructor,
                    TypeSymbol: typeWithAttribute,
                    BaseClass: baseClass,
                    CustomBufferMembers: customBufferMembers,
                    EnableRecursionGuard: false,
                    IsCustomCollection: isCustomCollection,
                    CustomCollectionElementType: customCollectionElementType);

                return (namespaceName, typeDefinition);
            });

      // Pipeline 3: Types specified via [assembly: GenerateSerializer(typeof(...))]
      // For standalone serialization of List<T>, T[], Dictionary<K,V>, primitives
      // NOTE: ForAttributeWithMetadataName doesn't work for assembly-level attributes,
      // so we extract them from the compilation directly
      var standaloneTypesPipeline = context.CompilationProvider
                .Select((compilation, ct) =>
                {
                    var result = new List<ITypeSymbol>();
                    var generateSerializerAttr = compilation.GetTypeByMetadataName("GProtobuf.Core.GenerateSerializerAttribute");
                    if (generateSerializerAttr == null)
                        return result.ToImmutableArray();

                    foreach (var attr in compilation.Assembly.GetAttributes())
                    {
                        if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, generateSerializerAttr))
                        {
                            if (attr.ConstructorArguments.Length > 0 &&
                                attr.ConstructorArguments[0].Value is ITypeSymbol typeSymbol)
                            {
                                result.Add(typeSymbol);
                            }
                        }
                    }
                    return result.ToImmutableArray();
                });

        // Pipeline 4: Generator options from [assembly: GProtobufOptions(...)]
        // Controls which code generators are enabled (SpanReader, StreamReader, StreamWriter, BufferWriter)
        var optionsPipeline = context.CompilationProvider
            .Select((compilation, ct) =>
            {
                var gprotobufOptionsAttr = compilation.GetTypeByMetadataName("GProtobuf.Core.GProtobufOptionsAttribute");
                if (gprotobufOptionsAttr == null)
                    return GeneratorOptions.Default;

                foreach (var attr in compilation.Assembly.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, gprotobufOptionsAttr))
                    {
                        // Parse named arguments
                        bool generateSpanReader = true;
                        bool generateStreamReader = true;
                        bool generateStreamWriter = true;
                        bool generateBufferWriter = true;
                        bool generateOnePassStreamWriter = false;
                        bool generateStackBufferWriter = true;
                        bool useStringPooling = false;

                        foreach (var namedArg in attr.NamedArguments)
                        {
                            switch (namedArg.Key)
                            {
                                case "GenerateSpanReader":
                                    generateSpanReader = namedArg.Value.Value is bool v1 && v1;
                                    break;
                                case "GenerateStreamReader":
                                    generateStreamReader = namedArg.Value.Value is bool v2 && v2;
                                    break;
                                case "GenerateStreamWriter":
                                    generateStreamWriter = namedArg.Value.Value is bool v3 && v3;
                                    break;
                                case "GenerateBufferWriter":
                                    generateBufferWriter = namedArg.Value.Value is bool v4 && v4;
                                    break;
                                case "GenerateOnePassStreamWriter":
                                    generateOnePassStreamWriter = namedArg.Value.Value is bool v5 && v5;
                                    break;
                                case "GenerateStackBufferWriter":
                                    generateStackBufferWriter = namedArg.Value.Value is bool v6 && v6;
                                    break;
                                case "UseStringPooling":
                                    useStringPooling = namedArg.Value.Value is bool v7 && v7;
                                    break;
                            }
                        }

                        return new GeneratorOptions
                        {
                            GenerateSpanReader = generateSpanReader,
                            GenerateStreamReader = generateStreamReader,
                            GenerateStreamWriter = generateStreamWriter,
                            GenerateBufferWriter = generateBufferWriter,
                            GenerateOnePassStreamWriter = generateOnePassStreamWriter,
                            GenerateStackBufferWriter = generateStackBufferWriter,
                            UseStringPooling = useStringPooling
                        };
                    }
                }

                return GeneratorOptions.Default;
            });

        // Combine ProtoContract and ProtoInclude pipelines
        var combinedPipeline = protoContractPipeline
            .Collect()
            .Combine(protoIncludePipeline.Collect())
            .Select(static (pair, _) =>
            {
                var combined = new List<(string namespaceName, TypeDefinition typeDefinition)>();
                combined.AddRange(pair.Left);
                combined.AddRange(pair.Right.Where(x => x.Item1 != null && x.Item2 != null));
                return combined.ToImmutableArray();
            });

        context.RegisterSourceOutput(
            combinedPipeline
                .Combine(enumTypesProvider)
                .Combine(standaloneTypesPipeline)
                .Combine(optionsPipeline)
                .Combine(context.CompilationProvider),
            static (context, provider) =>
            {
                var typeDefinitions = provider.Left.Left.Left.Left; // ProtoContract + ProtoInclude types
                var enumTypes = provider.Left.Left.Left.Right;
                var standaloneTypes = provider.Left.Left.Right; // Types from [GenerateSerializer]
                var options = provider.Left.Right; // Generator options from [GProtobufOptions]
                var compilation = provider.Right;


                try
                {
                    // Report diagnostic that generator is starting
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GPROTO001",
                            "GProtobuf Generator Started",
                            "GProtobuf generator started with {0} enum types, {1} type definitions, {2} standalone types. Options: SpanReader={3}, StreamReader={4}, StreamWriter={5}, BufferWriter={6}, OnePassStreamWriter={7}",
                            "GProtobuf",
                            DiagnosticSeverity.Info,
                            true),
                        Location.None,
                        enumTypes.Count,
                        typeDefinitions.Count(),
                        standaloneTypes.Length,
                        options.GenerateSpanReader,
                        options.GenerateStreamReader,
                        options.GenerateStreamWriter,
                        options.GenerateBufferWriter,
                        options.GenerateOnePassStreamWriter));

                    var objectTree = new ObjectTreeV2(enumTypes, compilation, standaloneTypes, options);
                    foreach (var (namespaceName, typeDefinition) in typeDefinitions)
                    {
                        objectTree.AddType(namespaceName, typeDefinition);
                    }

                    var codeFiles = objectTree.GenerateCode();
                    int filesGenerated = 0;
                    foreach(var f in codeFiles)
                    {
                        context.AddSource(f.FileName, f.FileCode);
                        filesGenerated++;
                    }

                    // Report success
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GPROTO002",
                            "GProtobuf Generator Success",
                            "GProtobuf generator completed successfully, generated {0} files",
                            "GProtobuf",
                            DiagnosticSeverity.Info,
                            true),
                        Location.None,
                        filesGenerated));
                }
                catch (System.Exception ex)
                {
                    // Report ALL exceptions
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "GPROTO999",
                            "GProtobuf Generator Error",
                            "GProtobuf generator failed: {0}. Stack: {1}",
                            "GProtobuf",
                            DiagnosticSeverity.Error,
                            true),
                        Location.None,
                        ex.Message,
                        ex.StackTrace));
                    throw;
                }
            });
    }
    
    private static List<ProtoMemberAttribute> GetProtoMemberAttributes(INamedTypeSymbol typeSymbol)
    {
        var result = new List<ProtoMemberAttribute>();

        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            // Skipni properties bez set metódy
            if (property.SetMethod == null)
                continue;

            // Prechádzame všetky atribúty property
            foreach (var attribute in property.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString().Contains("ProtoMemberAttribute") ?? false)
                {
                    // Získame FieldId z konštruktora
                    int fieldId = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int id
                        ? id
                        : 0;

                    var propertyType = property.Type.ToDisplayString();
                    var propertyName = property.Name;
                    var nmspace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                    // Detect if this is a nullable value type (Nullable<T>)
                    bool isNullable = property.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;

                    // Analyze collection information
                    var collectionInfo = AnalyzeCollectionType(property.Type);

                    // Analyze map/dictionary information
                    var (isMap, keyType, valueType, keyIsEnum, keyEnumType, valueIsEnum, valueEnumType) = AnalyzeMapType(property.Type);

                    // Check if type is enum
                    bool isEnum = false;
                    string enumUnderlyingType = null;
                    
                    // Handle nullable enum types
                    var checkType = property.Type;
                    if (isNullable && property.Type is INamedTypeSymbol nullableType && nullableType.TypeArguments.Length == 1)
                    {
                        checkType = nullableType.TypeArguments[0];
                    }
                    
                    if (checkType.TypeKind == TypeKind.Enum)
                    {
                        isEnum = true;
                        var enumType = (INamedTypeSymbol)checkType;
                        var rawUnderlyingType = enumType.EnumUnderlyingType?.ToDisplayString() ?? "System.Int32";
                        enumUnderlyingType = TypeMapping.NormalizeTypeName(rawUnderlyingType);
                    }

                    // Check if type is marked with [ProtoVarint]
                    var protoVarintInfo = GetProtoVarintInfo(checkType);

                    // Check for ProtoVarint validation errors
                    if (protoVarintInfo != null && !protoVarintInfo.IsValid)
                    {
                        throw new System.Exception($"ProtoVarint validation error for property '{propertyName}' in type '{typeSymbol.Name}': {protoVarintInfo.ValidationError}");
                    }

                    // Vytvoríme inštanciu s FieldId
                    var protoMember = new ProtoMemberAttribute(fieldId)
                    {
                        Name = propertyName,
                        Type = propertyType,
                        Namespace = nmspace,
                        Interfaces = property.Type.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
                        IsNullable = isNullable,
                        IsCollection = collectionInfo.IsCollection,
                        CollectionElementType = collectionInfo.ElementType != null ? TypeMapping.NormalizeTypeName(collectionInfo.ElementType) : null,
                        CollectionKind = collectionInfo.Kind,
                        IsMap = isMap,
                        MapKeyType = keyType,
                        MapValueType = valueType,
                        MapKeyIsEnum = keyIsEnum,
                        MapKeyEnumUnderlyingType = keyEnumType,
                        MapValueIsEnum = valueIsEnum,
                        MapValueEnumUnderlyingType = valueEnumType,
                        IsEnum = isEnum,
                        EnumUnderlyingType = enumUnderlyingType,
                        // ProtoVarint properties (only set if valid)
                        IsProtoVarint = protoVarintInfo?.IsValid ?? false,
                        ProtoVarintType = protoVarintInfo?.VarintType ?? ProtoVarintType.UInt32,
                        ProtoVarintValueMember = protoVarintInfo?.ValueMemberName,
                        ProtoVarintValueIsProperty = protoVarintInfo?.IsValueProperty ?? false,
                    };

                    // Spracujeme voliteľné NamedArguments
                    foreach (var argument in attribute.NamedArguments)
                    {
                        switch (argument.Key)
                        {
                            case nameof(protoMember.IsPacked):
                                protoMember.IsPacked = argument.Value.Value is bool isPacked && isPacked;
                                break;

                            case nameof(protoMember.IsRequired):
                                protoMember.IsRequired = argument.Value.Value is bool isRequired && isRequired;
                                break;

                            case nameof(protoMember.DataFormat):
                                protoMember.DataFormat = argument.Value.Value is int dataFormat
                                    ? (DataFormat)dataFormat
                                    : DataFormat.Default;
                                break;
                        }
                    }

                    // Pridáme do výsledku
                    result.Add(protoMember);
                    break; // only one is allowed
                }
            }
        }

        // Also collect fields with [ProtoMember] attributes (for readonly struct fields)
        foreach (var field in typeSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            // Skip static, const fields
            if (field.IsStatic || field.IsConst)
                continue;

            // Process field attributes
            foreach (var attribute in field.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString().Contains("ProtoMemberAttribute") ?? false)
                {
                    // Get FieldId from constructor
                    int fieldId = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int id
                        ? id
                        : 0;

                    var fieldType = field.Type.ToDisplayString();
                    var fieldName = field.Name;
                    var nmspace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                    // Detect if this is a nullable value type (Nullable<T>)
                    bool isNullable = field.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;

                    // Analyze collection information
                    var collectionInfo = AnalyzeCollectionType(field.Type);

                    // Analyze map/dictionary information
                    var (isMap, keyType, valueType, keyIsEnum, keyEnumType, valueIsEnum, valueEnumType) = AnalyzeMapType(field.Type);

                    // Check if type is enum
                    bool isEnum = false;
                    string enumUnderlyingType = null;

                    // Handle nullable enum types
                    var checkType = field.Type;
                    if (isNullable && field.Type is INamedTypeSymbol nullableType && nullableType.TypeArguments.Length == 1)
                    {
                        checkType = nullableType.TypeArguments[0];
                    }

                    if (checkType.TypeKind == TypeKind.Enum)
                    {
                        isEnum = true;
                        var enumType = (INamedTypeSymbol)checkType;
                        var rawUnderlyingType = enumType.EnumUnderlyingType?.ToDisplayString() ?? "System.Int32";
                        enumUnderlyingType = TypeMapping.NormalizeTypeName(rawUnderlyingType);
                    }

                    // Check if type is marked with [ProtoVarint]
                    var protoVarintInfo = GetProtoVarintInfo(checkType);

                    // Check for ProtoVarint validation errors
                    if (protoVarintInfo != null && !protoVarintInfo.IsValid)
                    {
                        throw new System.Exception($"ProtoVarint validation error for field '{fieldName}' in type '{typeSymbol.Name}': {protoVarintInfo.ValidationError}");
                    }

                    // Create ProtoMemberAttribute instance
                    var protoMember = new ProtoMemberAttribute(fieldId)
                    {
                        Name = fieldName,
                        Type = fieldType,
                        Namespace = nmspace,
                        Interfaces = field.Type.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
                        IsNullable = isNullable,
                        IsCollection = collectionInfo.IsCollection,
                        CollectionElementType = collectionInfo.ElementType != null ? TypeMapping.NormalizeTypeName(collectionInfo.ElementType) : null,
                        CollectionKind = collectionInfo.Kind,
                        IsMap = isMap,
                        MapKeyType = keyType,
                        MapValueType = valueType,
                        MapKeyIsEnum = keyIsEnum,
                        MapKeyEnumUnderlyingType = keyEnumType,
                        MapValueIsEnum = valueIsEnum,
                        MapValueEnumUnderlyingType = valueEnumType,
                        IsEnum = isEnum,
                        EnumUnderlyingType = enumUnderlyingType,
                        // ProtoVarint properties (only set if valid)
                        IsProtoVarint = protoVarintInfo?.IsValid ?? false,
                        ProtoVarintType = protoVarintInfo?.VarintType ?? ProtoVarintType.UInt32,
                        ProtoVarintValueMember = protoVarintInfo?.ValueMemberName,
                        ProtoVarintValueIsProperty = protoVarintInfo?.IsValueProperty ?? false,
                    };

                    // Process optional NamedArguments
                    foreach (var argument in attribute.NamedArguments)
                    {
                        switch (argument.Key)
                        {
                            case nameof(protoMember.IsPacked):
                                protoMember.IsPacked = argument.Value.Value is bool isPacked && isPacked;
                                break;

                            case nameof(protoMember.IsRequired):
                                protoMember.IsRequired = argument.Value.Value is bool isRequired && isRequired;
                                break;

                            case nameof(protoMember.DataFormat):
                                protoMember.DataFormat = argument.Value.Value is int dataFormat
                                    ? (DataFormat)dataFormat
                                    : DataFormat.Default;
                                break;
                        }
                    }

                    // Add to result
                    result.Add(protoMember);
                    break; // only one ProtoMember attribute allowed
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Analyzes a type to check if it's marked with [ProtoVarint] and extracts the relevant information.
    /// </summary>
    /// <returns>ProtoVarintInfo if the type is a ProtoVarint, null otherwise.</returns>
    private static ProtoVarintInfo GetProtoVarintInfo(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return null;

        // Check for [ProtoVarint] attribute
        var protoVarintAttr = namedType.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "ProtoVarintAttribute");

        if (protoVarintAttr == null)
            return null;

        var typeName = namedType.ToDisplayString();

        // Get the varint type from the attribute (default is UInt32 = 0)
        var varintType = ProtoVarintType.UInt32;
        if (protoVarintAttr.ConstructorArguments.Length > 0 &&
            protoVarintAttr.ConstructorArguments[0].Value is int typeValue)
        {
            varintType = (ProtoVarintType)typeValue;
        }

        // Find ALL [ProtoVarintValue] methods, properties, or fields
        var valueMembers = new List<(string Name, bool IsProperty, string ReturnType)>();
        foreach (var member in namedType.GetMembers())
        {
            var hasValueAttr = member.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "ProtoVarintValueAttribute");

            if (hasValueAttr)
            {
                if (member is IPropertySymbol prop)
                {
                    valueMembers.Add((prop.Name, true, prop.Type.ToDisplayString()));
                }
                else if (member is IFieldSymbol field)
                {
                    // Fields are accessed like properties (no parentheses)
                    valueMembers.Add((field.Name, true, field.Type.ToDisplayString()));
                }
                else if (member is IMethodSymbol method && method.Parameters.Length == 0)
                {
                    valueMembers.Add((method.Name, false, method.ReturnType.ToDisplayString()));
                }
                else if (member is IMethodSymbol methodWithParams)
                {
                    // Method with parameters - invalid
                    return new ProtoVarintInfo
                    {
                        TypeName = typeName,
                        VarintType = varintType,
                        ValidationError = $"[ProtoVarint] type '{typeName}': [ProtoVarintValue] method '{methodWithParams.Name}' must have no parameters"
                    };
                }
            }
        }

        // Validate: exactly one [ProtoVarintValue] member
        if (valueMembers.Count == 0)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': missing [ProtoVarintValue] method or property"
            };
        }
        if (valueMembers.Count > 1)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': found {valueMembers.Count} [ProtoVarintValue] members, expected exactly 1"
            };
        }

        var valueMember = valueMembers[0];

        // Find ALL constructors with [ProtoVarintConstructor]
        var markedConstructors = new List<(int ParamCount, string ParamType)>();
        foreach (var ctor in namedType.Constructors)
        {
            var hasCtor = ctor.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "ProtoVarintConstructorAttribute");

            if (hasCtor)
            {
                var paramType = ctor.Parameters.Length == 1
                    ? ctor.Parameters[0].Type.ToDisplayString()
                    : null;
                markedConstructors.Add((ctor.Parameters.Length, paramType));
            }
        }

        // Validate: exactly one [ProtoVarintConstructor]
        if (markedConstructors.Count == 0)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': missing [ProtoVarintConstructor] on a constructor"
            };
        }
        if (markedConstructors.Count > 1)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': found {markedConstructors.Count} [ProtoVarintConstructor] constructors, expected exactly 1"
            };
        }

        var ctorInfo = markedConstructors[0];

        // Validate: constructor must have exactly 1 parameter
        if (ctorInfo.ParamCount != 1)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': [ProtoVarintConstructor] must have exactly 1 parameter, found {ctorInfo.ParamCount}"
            };
        }

        // Validate: value return type matches constructor parameter type
        var normalizedValueType = NormalizeVarintTypeName(valueMember.ReturnType);
        var normalizedCtorType = NormalizeVarintTypeName(ctorInfo.ParamType);
        if (normalizedValueType != normalizedCtorType)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': [ProtoVarintValue] return type '{valueMember.ReturnType}' doesn't match [ProtoVarintConstructor] parameter type '{ctorInfo.ParamType}'"
            };
        }

        // Validate: value type matches declared ProtoVarintType
        var expectedType = GetExpectedTypeForVarintType(varintType);
        if (normalizedValueType != expectedType)
        {
            return new ProtoVarintInfo
            {
                TypeName = typeName,
                VarintType = varintType,
                ValidationError = $"[ProtoVarint] type '{typeName}': [ProtoVarintValue] return type '{valueMember.ReturnType}' doesn't match declared ProtoVarintType.{varintType} (expected '{expectedType}')"
            };
        }

        return new ProtoVarintInfo
        {
            TypeName = typeName,
            VarintType = varintType,
            ValueMemberName = valueMember.Name,
            IsValueProperty = valueMember.IsProperty,
            ConstructorParameterCount = 1,
            ValueReturnType = normalizedValueType,
            ConstructorParameterType = normalizedCtorType,
            ValidationError = null
        };
    }

    /// <summary>
    /// Normalizes type names for comparison (e.g., "System.UInt32" -> "uint").
    /// </summary>
    private static string NormalizeVarintTypeName(string typeName)
    {
        return typeName switch
        {
            "System.UInt32" or "uint" => "uint",
            "System.Int32" or "int" => "int",
            "System.UInt64" or "ulong" => "ulong",
            "System.Int64" or "long" => "long",
            _ => typeName
        };
    }

    /// <summary>
    /// Gets the expected C# type for a ProtoVarintType.
    /// </summary>
    private static string GetExpectedTypeForVarintType(ProtoVarintType varintType)
    {
        return varintType switch
        {
            ProtoVarintType.UInt32 => "uint",
            ProtoVarintType.Int32 => "int",
            ProtoVarintType.SInt32 => "int",  // ZigZag uses signed int
            ProtoVarintType.UInt64 => "ulong",
            ProtoVarintType.Int64 => "long",
            ProtoVarintType.SInt64 => "long",  // ZigZag uses signed long
            _ => "uint"
        };
    }

    /// <summary>
    /// Analyzes methods in the type for custom buffer serialization attributes.
    /// Collects [ProtoMemberBufferSize], [ProtoMemberBufferFill], and [ProtoMemberBufferRead] methods
    /// and groups them by field ID.
    /// </summary>
    private static List<CustomBufferMember> GetCustomBufferMembers(INamedTypeSymbol typeSymbol)
    {
        var bufferSizeMethods = new Dictionary<int, string>();
        var bufferFillMethods = new Dictionary<int, string>();
        var bufferReadMethods = new Dictionary<int, string>();

        // Iterate through all methods in the type
        foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            // Skip static methods, constructors, property accessors
            if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                continue;

            foreach (var attribute in method.GetAttributes())
            {
                var attrName = attribute.AttributeClass?.ToDisplayString();

                // Check for new ProtoBufferAttribute
                if (attrName?.Contains("ProtoBufferAttribute") == true)
                {
                    if (attribute.ConstructorArguments.Length >= 2 &&
                        attribute.ConstructorArguments[0].Value is int fieldId &&
                        attribute.ConstructorArguments[1].Value is int operationValue)
                    {
                        // ProtoBufferOperation enum: GetSize=0, Write=1, Read=2
                        switch (operationValue)
                        {
                            case 0: // GetSize
                                // Validate method signature: must return int and take no parameters
                                if (method.ReturnType.SpecialType == SpecialType.System_Int32 &&
                                    method.Parameters.Length == 0)
                                {
                                    bufferSizeMethods[fieldId] = method.Name;
                                }
                                break;
                            case 1: // Write
                                // Validate method signature: must return void and take Span<byte>
                                if (method.ReturnsVoid &&
                                    method.Parameters.Length == 1 &&
                                    method.Parameters[0].Type.ToDisplayString().Contains("Span<byte>"))
                                {
                                    bufferFillMethods[fieldId] = method.Name;
                                }
                                break;
                            case 2: // Read
                                // Validate method signature: must return void and take ReadOnlySpan<byte>
                                if (method.ReturnsVoid &&
                                    method.Parameters.Length == 1 &&
                                    method.Parameters[0].Type.ToDisplayString().Contains("ReadOnlySpan<byte>"))
                                {
                                    bufferReadMethods[fieldId] = method.Name;
                                }
                                break;
                        }
                    }
                }
            }
        }

        // Combine all field IDs from all three dictionaries
        var allFieldIds = bufferSizeMethods.Keys
            .Union(bufferFillMethods.Keys)
            .Union(bufferReadMethods.Keys)
            .Distinct()
            .OrderBy(id => id);

        var result = new List<CustomBufferMember>();

        foreach (var fieldId in allFieldIds)
        {
            // Only create CustomBufferMember if we have at least Size and Fill methods
            if (bufferSizeMethods.TryGetValue(fieldId, out var sizeMethod) &&
                bufferFillMethods.TryGetValue(fieldId, out var fillMethod))
            {
                bufferReadMethods.TryGetValue(fieldId, out var readMethod);

                result.Add(new CustomBufferMember
                {
                    FieldId = fieldId,
                    SizeMethodName = sizeMethod,
                    FillMethodName = fillMethod,
                    ReadMethodName = readMethod
                });
            }
        }

        return result;
    }

    private static List<ProtoIncludeAttribute> GetProtoIncludeAttributes(INamedTypeSymbol typeSymbol)
    {
        var result = new List<ProtoIncludeAttribute>();

        // Prejdeme všetky atribúty na danej triede
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            // Skontrolujeme, či ide o atribút typu ProtoInclude
            if (!(attribute.AttributeClass?.ToDisplayString().Contains("ProtoIncludeAttribute") ?? false))
                continue;
            
            // Získame argumenty atribútu
            int tag = 0;
            string? typeName = null;
            string? typeNamespace = null;

            // Prejdeme argumenty atribútu (pozícia závisí od konštruktora)
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Type?.SpecialType == SpecialType.System_Int32)
                {
                    tag = (int)argument.Value!;
                }
                else if (argument.Type?.ToDisplayString() == "System.Type")
                {
                    typeName = argument.Value?.ToString();
                    typeNamespace = (argument.Value as INamedTypeSymbol)?.ContainingNamespace.ToDisplayString();
                }
            }

            if (typeName is null || typeNamespace is null)
                continue;

            result.Add(new ProtoIncludeAttribute(tag, typeName, typeNamespace));
        }

        return result;
    }

    /// <summary>
    /// Analyzes a type symbol to determine if it's a collection and extract collection metadata
    /// </summary>
    private static (bool IsCollection, string ElementType, CollectionKind Kind) AnalyzeCollectionType(ITypeSymbol typeSymbol)
    {
        // Check if it's an array type
        if (typeSymbol.TypeKind == TypeKind.Array)
        {
            var arrayType = (IArrayTypeSymbol)typeSymbol;

            // byte[] is a primitive type (serialized as length-delimited bytes), not a collection
            if (arrayType.ElementType.SpecialType == SpecialType.System_Byte)
            {
                return (false, null, CollectionKind.None);
            }

            return (true, arrayType.ElementType.ToDisplayString(), CollectionKind.Array);
        }

        // Check if it's a generic type
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            var elementType = namedType.TypeArguments[0].ToDisplayString();
            var typeDisplayString = namedType.ToDisplayString();

            // Check for specific collection types
            if (IsInterfaceCollectionType(namedType))
            {
                return (true, elementType, CollectionKind.InterfaceCollection);
            }

            if (IsConcreteCollectionType(namedType))
            {
                return (true, elementType, CollectionKind.ConcreteCollection);
            }
        }

        // Check for non-generic types that implement IEnumerable<T> + Add(T) method
        // (protobuf-net compatible collections without [ProtoContract])
        if (typeSymbol is INamedTypeSymbol nonGenericType)
        {
            var customCollectionInfo = AnalyzeNonGenericCollection(nonGenericType);
            if (customCollectionInfo.IsCollection)
            {
                return customCollectionInfo;
            }
        }

        return (false, null, CollectionKind.None);
    }

    /// <summary>
    /// Checks if the type is an interface collection type (ICollection&lt;T&gt;, IList&lt;T&gt;, IEnumerable&lt;T&gt;)
    /// </summary>
    private static bool IsInterfaceCollectionType(INamedTypeSymbol namedType)
    {
        var typeDisplayString = namedType.OriginalDefinition.ToDisplayString();
        
        return typeDisplayString == "System.Collections.Generic.ICollection<T>" ||
               typeDisplayString == "System.Collections.Generic.IList<T>" ||
               typeDisplayString == "System.Collections.Generic.IEnumerable<T>";
    }

    /// <summary>
    /// Checks if the type is a concrete collection type that implements ICollection&lt;T&gt;
    /// </summary>
    private static bool IsConcreteCollectionType(INamedTypeSymbol namedType)
    {
        // Check if it implements ICollection<T>
        return namedType.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");
    }

    /// <summary>
    /// Analyzes non-generic types that implement IEnumerable&lt;T&gt; and have Add(T) method
    /// (protobuf-net compatible collections like ValueLogTypeHashSet)
    /// </summary>
    private static (bool IsCollection, string ElementType, CollectionKind Kind) AnalyzeNonGenericCollection(INamedTypeSymbol typeSymbol)
    {
        // Find IEnumerable<T> interface
        var enumerableInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            i.IsGenericType &&
            i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>");

        if (enumerableInterface == null)
        {
            return (false, null, CollectionKind.None);
        }

        // Get element type from IEnumerable<T>
        var elementType = enumerableInterface.TypeArguments[0].ToDisplayString();

        // Check if type has Add(T) method with matching parameter type
        var hasAddMethod = typeSymbol.GetMembers("Add")
            .OfType<IMethodSymbol>()
            .Any(m =>
                m.Parameters.Length == 1 &&
                m.Parameters[0].Type.ToDisplayString() == elementType);

        if (!hasAddMethod)
        {
            return (false, null, CollectionKind.None);
        }

        // This is a protobuf-net compatible collection!
        // Determine kind based on whether it implements ICollection<T>
        var implementsICollection = typeSymbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");

        if (implementsICollection)
        {
            return (true, elementType, CollectionKind.CustomCollection);
        }

        return (true, elementType, CollectionKind.CustomEnumerable);
    }

    /// <summary>
    /// Checks if a type string represents a collection type (array, List, HashSet, etc.)
    /// </summary>
    private static bool IsCollectionType(string typeName)
    {
        return typeName.EndsWith("[]") || 
               typeName.Contains("List<") || 
               typeName.Contains("HashSet<") || 
               typeName.Contains("IList<") || 
               typeName.Contains("ICollection<") || 
               typeName.Contains("IEnumerable<") ||
               typeName.Contains("System.Collections.Generic.List<") ||
               typeName.Contains("System.Collections.Generic.HashSet<") ||
               typeName.Contains("System.Collections.Generic.IList<") ||
               typeName.Contains("System.Collections.Generic.ICollection<") ||
               typeName.Contains("System.Collections.Generic.IEnumerable<");
    }
    
    /// <summary>
    /// Analyzes if the type is a dictionary/map type and extracts key/value types
    /// </summary>
    private static (bool isMap, string keyType, string valueType, bool keyIsEnum, string keyEnumType, bool valueIsEnum, string valueEnumType) AnalyzeMapType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return (false, null, null, false, null, false, null);

        // Check for Dictionary<TKey, TValue>
        if (namedType.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
            namedType.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.SortedDictionary<TKey, TValue>")
        {
            if (namedType.TypeArguments.Length == 2)
            {
                var keyTypeSymbol = namedType.TypeArguments[0];
                var valueTypeSymbol = namedType.TypeArguments[1];

                var keyType = keyTypeSymbol.ToDisplayString();
                var valueType = valueTypeSymbol.ToDisplayString();

                // Check if key is enum
                bool keyIsEnum = keyTypeSymbol.TypeKind == TypeKind.Enum;
                string keyEnumUnderlyingType = keyIsEnum ? TypeMapping.NormalizeTypeName(((INamedTypeSymbol)keyTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32") : null;

                // Check if value is enum
                bool valueIsEnum = valueTypeSymbol.TypeKind == TypeKind.Enum;
                string valueEnumUnderlyingType = valueIsEnum ? TypeMapping.NormalizeTypeName(((INamedTypeSymbol)valueTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32") : null;
                
                // Arrays as keys are supported in Protocol Buffers - they're treated as byte arrays or repeated fields
                // Remove the check that prevented collections as keys
                return (true, keyType, valueType, keyIsEnum, keyEnumUnderlyingType, valueIsEnum, valueEnumUnderlyingType);
            }
        }
        
        // Check for IDictionary<TKey, TValue> interface
        var dictionaryInterface = namedType.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");

        if (dictionaryInterface != null && dictionaryInterface.TypeArguments.Length == 2)
        {
            var keyTypeSymbol = dictionaryInterface.TypeArguments[0];
            var valueTypeSymbol = dictionaryInterface.TypeArguments[1];

            var keyType = keyTypeSymbol.ToDisplayString();
            var valueType = valueTypeSymbol.ToDisplayString();

            // Check if key is enum
            bool keyIsEnum = keyTypeSymbol.TypeKind == TypeKind.Enum;
            string keyEnumUnderlyingType = keyIsEnum ? ((INamedTypeSymbol)keyTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32" : null;

            // Check if value is enum
            bool valueIsEnum = valueTypeSymbol.TypeKind == TypeKind.Enum;
            string valueEnumUnderlyingType = valueIsEnum ? ((INamedTypeSymbol)valueTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32" : null;

            // Arrays as keys are supported
            return (true, keyType, valueType, keyIsEnum, keyEnumUnderlyingType, valueIsEnum, valueEnumUnderlyingType);
        }

        // Check for List<KeyValuePair<TKey, TValue>> or ICollection<KeyValuePair<TKey, TValue>>
        // These are treated as map types in protobuf-net
        // ToDo: revisit this implementation later
        if (namedType.TypeArguments.Length == 1)
        {
            var elementType = namedType.TypeArguments[0] as INamedTypeSymbol;
            if (elementType?.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>"
                && elementType.TypeArguments.Length == 2)
            {
                var keyTypeSymbol = elementType.TypeArguments[0];
                var valueTypeSymbol = elementType.TypeArguments[1];

                var keyType = keyTypeSymbol.ToDisplayString();
                var valueType = valueTypeSymbol.ToDisplayString();

                // Check if key is enum
                bool keyIsEnum = keyTypeSymbol.TypeKind == TypeKind.Enum;
                string keyEnumUnderlyingType = keyIsEnum ? ((INamedTypeSymbol)keyTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32" : null;

                // Check if value is enum
                bool valueIsEnum = valueTypeSymbol.TypeKind == TypeKind.Enum;
                string valueEnumUnderlyingType = valueIsEnum ? ((INamedTypeSymbol)valueTypeSymbol).EnumUnderlyingType?.ToDisplayString() ?? "System.Int32" : null;

                return (true, keyType, valueType, keyIsEnum, keyEnumUnderlyingType, valueIsEnum, valueEnumUnderlyingType);
            }
        }

        return (false, null, null, false, null, false, null);
    }

    /// <summary>
    /// Extracts the base class full name for flat inheritance tracking.
    /// Returns null if:
    /// - No base class (type inherits from System.Object directly)
    /// - Base class is System.Object
    /// - Base class is System.ValueType (for structs)
    /// </summary>
    private static string? GetBaseClass(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;

        // No base type or base is System.Object or System.ValueType
        if (baseType == null ||
            baseType.SpecialType == SpecialType.System_Object ||
            baseType.SpecialType == SpecialType.System_ValueType)
        {
            return null;
        }

        return baseType.ToDisplayString();
    }

    /// <summary>
    /// Determines if a type has a parameterless constructor (explicit or implicit).
    /// </summary>
    private static bool HasParameterlessConstructor(INamedTypeSymbol typeSymbol)
    {
        // Get all instance constructors (non-static)
        var instanceConstructors = typeSymbol.Constructors
            .Where(c => !c.IsStatic)
            .ToList();

        // Check for explicit parameterless constructor
        foreach (var ctor in instanceConstructors)
        {
            if (ctor.Parameters.Length == 0)
            {
                return true;
            }
        }

        // If no explicit constructors are defined (only implicit exists),
        // the type has an implicit parameterless constructor
        var explicitConstructors = instanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToList();
        if (explicitConstructors.Count == 0)
        {
            return true; // Implicit parameterless constructor
        }

        // Has explicit constructors but none are parameterless
        return false;
    }

    /// <summary>
    /// Extracts EnableRecursionGuard value from [ProtoContract] attribute.
    /// </summary>
    private static bool GetEnableRecursionGuard(INamedTypeSymbol typeSymbol)
    {
        var attr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoContractAttribute");

        if (attr == null) return false;

        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == "EnableRecursionGuard" && arg.Value.Value is bool value)
                return value;
        }
        return false;
    }
}