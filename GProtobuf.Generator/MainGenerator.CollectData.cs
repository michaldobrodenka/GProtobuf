using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;


namespace GProtobuf.Generator
{
    [Generator]
    public partial class MainGenerator : ISourceGenerator
    {
        private ObjectTree objectTree = new ObjectTree();

        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext context)
        {
            Compilation compilation = context.Compilation;

#if DEBUG
            //if (!Debugger.IsAttached)
            //{
            //    Debugger.Launch();
            //}
#endif
            // Prejdeme všetky referencované assembly
            foreach (var reference in compilation.References)
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                //if (assemblySymbol == null) continue;
                // Ignorujeme assembly, ktoré nemajú zdrojový kód
                if (assemblySymbol == null) continue;

                //// Ignorujeme assembly, ktoré pochádzajú z metadát (t.j. nemajú zdrojový kód v solution)
                //if (assemblySymbol.Locations.All(loc => loc.IsInMetadata))
                //    continue;

                // Prechádzame cez všetky triedy a štruktúry v assembly
                foreach (var type in GetAllTypes(assemblySymbol.GlobalNamespace))
                {
                    if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                        continue;

                    // Overíme, či trieda/štruktúra má atribút [ProtoContract]
                    if (!HasProtoContractAttribute(type))
                        continue;

                    bool isStruct = type.TypeKind == TypeKind.Struct;
                    var nmspace = type.ContainingNamespace?.ToDisplayString() ?? String.Empty;

                    var protoIncludes = GetProtoIncludeAttributes(type);

                    //sb.AppendLine($"        // Properties of {type.ToDisplayString()}");

                    var protoMembers = GetProtoMemberAttributes(type);

                    objectTree.AddType(nmspace, type.ToDisplayString(), isStruct, protoIncludes, protoMembers);
                }
            }

            //var sb = new StringBuilder();

            //sb.AppendLine("namespace Generated");
            //sb.AppendLine("{");
            //sb.AppendLine("    public static class PropertySummary");
            //sb.AppendLine("    {");

            //sb.AppendLine("    }");
            //sb.AppendLine("}");

            //context.AddSource("PropertySummary.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

            var codeFiles = objectTree.GenerateCode();
            foreach(var f in codeFiles)
            {
                context.AddSource(f.fileName, f.fileCode);
            }
            //foreach(var codeFile in codeFiles)
            //{
            //    var fileName = codeFile.Key;
            //    var fileContent = codeFile.Value;

            //    context.AddSource(fileName, fileContent);
            //}
        }

        /// <summary>
        /// Zistí, či trieda/štruktúra má atribút [ProtoContract]
        /// </summary>
        private static bool HasProtoContractAttribute(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.GetAttributes()
                .Any(attr => attr.AttributeClass?.ToDisplayString().Contains("ProtoContract") ?? false );
        }

        private static List<global::GProtobuf.Core.ProtoMemberAttribute> GetProtoMemberAttributes(INamedTypeSymbol typeSymbol)
        {
            var result = new List<global::GProtobuf.Core.ProtoMemberAttribute>();

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
                        var nmspace = property.Type.ContainingNamespace?.ToDisplayString();

                        // Vytvoríme inštanciu s FieldId
                        var protoMember = new global::GProtobuf.Core.ProtoMemberAttribute(fieldId)
                        {
                            Name = propertyName,
                            Type = propertyType,
                            Namespace = nmspace,
                            Interfaces = property.Type.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
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
                                        ? (global::GProtobuf.Core.DataFormat)dataFormat
                                        : global::GProtobuf.Core.DataFormat.Default;
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

        /// <summary>
        /// Zistí, či trieda/štruktúra má atribút [ProtoContract]
        /// </summary>
        private static List<global::GProtobuf.Core.ProtoIncludeAttribute> GetProtoIncludeAttributes(INamedTypeSymbol typeSymbol)
        {
            var result = new List<global::GProtobuf.Core.ProtoIncludeAttribute>();

            // Prejdeme všetky atribúty na danej triede
            foreach (var attribute in typeSymbol.GetAttributes())
            {
                // Skontrolujeme, či ide o atribút typu ProtoInclude
                if (attribute.AttributeClass?.ToDisplayString().Contains("ProtoIncludeAttribute") ?? false)
                {
                    // Získame argumenty atribútu
                    int tag = 0;
                    string typeName = String.Empty;
                    string typeNamespace = String.Empty;

                    // Prejdeme argumenty atribútu (pozícia závisí od konštruktora)
                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        if (argument.Type?.SpecialType == SpecialType.System_Int32)
                        {
                            tag = (int)argument.Value!;
                        }
                        else if (argument.Type.ToDisplayString() == "System.Type")
                        {
                            typeName = argument.Value?.ToString() ?? string.Empty;
                            typeNamespace = (argument.Value as INamedTypeSymbol)?.ContainingNamespace.ToDisplayString();
                        }
                    }

                    // Vytvoríme inštanciu atribútu a pridáme do zoznamu
                    result.Add(new global::GProtobuf.Core.ProtoIncludeAttribute(tag, typeName, typeNamespace));
                }
            }

            return result;
        }

        /// <summary>
        /// Rekurzívne prechádza všetky typy v namespace
        /// </summary>
        private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
                yield return type;

            foreach (var subNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(subNamespace))
                    yield return type;
            }
        }
    }
}
