using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GProtobuf.Generator;

[Generator]
public sealed class SerializerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
#endif

        var pipeline = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "ProtoBuf.ProtoContractAttribute",
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (syntaxContext, _) =>
            {
                var typeWithAttribute = (syntaxContext.TargetSymbol as INamedTypeSymbol)!;
                var namespaceName = syntaxContext.TargetSymbol.ContainingNamespace.ToDisplayString();
                var protoIncludes = GetProtoIncludeAttributes(typeWithAttribute);
                var protoMembers = GetProtoMemberAttributes(typeWithAttribute);
                var typeDefinition = new TypeDefinition(
                    IsStruct: false, // todo implement support for structs
                    IsAbstract: typeWithAttribute.IsAbstract,
                    typeWithAttribute.ToDisplayString(),
                    protoIncludes,
                    protoMembers);
                
                return (namespaceName, typeDefinition);
            });
        
        context.RegisterSourceOutput(
            pipeline.Collect(),
            static (context, typeDefinitions) =>
            {
                var objectTree = new ObjectTree();
                foreach (var (namespaceName, typeDefinition) in typeDefinitions)
                {
                    objectTree.AddType(namespaceName, typeDefinition);
                }

                var codeFiles = objectTree.GenerateCode();
                foreach(var f in codeFiles)
                {
                    context.AddSource(f.FileName, f.FileCode);
                }
            });
    }
    
    private static List<Core.ProtoMemberAttribute> GetProtoMemberAttributes(INamedTypeSymbol typeSymbol)
    {
        var result = new List<Core.ProtoMemberAttribute>();

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
                    var nmspace = property.Type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    
                    // Detect if this is a nullable value type (Nullable<T>)
                    bool isNullable = property.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;

                    // Vytvoríme inštanciu s FieldId
                    var protoMember = new Core.ProtoMemberAttribute(fieldId)
                    {
                        Name = propertyName,
                        Type = propertyType,
                        Namespace = nmspace,
                        Interfaces = property.Type.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
                        IsNullable = isNullable,
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
                                    ? (Core.DataFormat)dataFormat
                                    : Core.DataFormat.Default;
                                break;
                        }
                    }

                    // Pridáme do výsledku
                    result.Add(protoMember);
                    break; // only one is allowed
                }
            }
        }

        return result;
    }

    private static List<Core.ProtoIncludeAttribute> GetProtoIncludeAttributes(INamedTypeSymbol typeSymbol)
    {
        var result = new List<Core.ProtoIncludeAttribute>();

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

            result.Add(new Core.ProtoIncludeAttribute(tag, typeName, typeNamespace));
        }

        return result;
    }
}