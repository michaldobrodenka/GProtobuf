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
//#if DEBUG
//        if (!Debugger.IsAttached)
//        {
//            Debugger.Launch();
//        }
//#endif

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
                    var nmspace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    
                    // Detect if this is a nullable value type (Nullable<T>)
                    bool isNullable = property.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;

                    // Analyze collection information
                    var collectionInfo = AnalyzeCollectionType(property.Type);
                    
                    // Analyze map/dictionary information
                    var (isMap, keyType, valueType) = AnalyzeMapType(property.Type);

                    // Vytvoríme inštanciu s FieldId
                    var protoMember = new Core.ProtoMemberAttribute(fieldId)
                    {
                        Name = propertyName,
                        Type = propertyType,
                        Namespace = nmspace,
                        Interfaces = property.Type.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
                        IsNullable = isNullable,
                        IsCollection = collectionInfo.IsCollection,
                        CollectionElementType = collectionInfo.ElementType,
                        CollectionKind = collectionInfo.Kind,
                        IsMap = isMap,
                        MapKeyType = keyType,
                        MapValueType = valueType,
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

    /// <summary>
    /// Analyzes a type symbol to determine if it's a collection and extract collection metadata
    /// </summary>
    private static (bool IsCollection, string ElementType, Core.CollectionKind Kind) AnalyzeCollectionType(ITypeSymbol typeSymbol)
    {
        // Check if it's an array type
        if (typeSymbol.TypeKind == TypeKind.Array)
        {
            var arrayType = (IArrayTypeSymbol)typeSymbol;
            return (true, arrayType.ElementType.ToDisplayString(), Core.CollectionKind.Array);
        }

        // Check if it's a generic type
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            var elementType = namedType.TypeArguments[0].ToDisplayString();
            var typeDisplayString = namedType.ToDisplayString();

            // Check for specific collection types
            if (IsInterfaceCollectionType(namedType))
            {
                return (true, elementType, Core.CollectionKind.InterfaceCollection);
            }
            
            if (IsConcreteCollectionType(namedType))
            {
                return (true, elementType, Core.CollectionKind.ConcreteCollection);
            }
        }

        return (false, null, Core.CollectionKind.None);
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
    /// Analyzes if the type is a dictionary/map type and extracts key/value types
    /// </summary>
    private static (bool isMap, string keyType, string valueType) AnalyzeMapType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return (false, null, null);
        
        // Check for Dictionary<TKey, TValue>
        if (namedType.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
            namedType.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.SortedDictionary<TKey, TValue>")
        {
            if (namedType.TypeArguments.Length == 2)
            {
                return (true, namedType.TypeArguments[0].ToDisplayString(), namedType.TypeArguments[1].ToDisplayString());
            }
        }
        
        // Check for IDictionary<TKey, TValue> interface
        var dictionaryInterface = namedType.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>");
        
        if (dictionaryInterface != null && dictionaryInterface.TypeArguments.Length == 2)
        {
            return (true, dictionaryInterface.TypeArguments[0].ToDisplayString(), dictionaryInterface.TypeArguments[1].ToDisplayString());
        }
        
        // Check for ICollection<KeyValuePair<TKey, TValue>>
        var collectionInterface = namedType.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.ICollection<T>" &&
            i.TypeArguments.Length == 1 &&
            i.TypeArguments[0] is INamedTypeSymbol elementType &&
            elementType.OriginalDefinition?.ToDisplayString() == "System.Collections.Generic.KeyValuePair<TKey, TValue>");
        
        if (collectionInterface?.TypeArguments[0] is INamedTypeSymbol kvpType && kvpType.TypeArguments.Length == 2)
        {
            return (true, kvpType.TypeArguments[0].ToDisplayString(), kvpType.TypeArguments[1].ToDisplayString());
        }
        
        return (false, null, null);
    }
}