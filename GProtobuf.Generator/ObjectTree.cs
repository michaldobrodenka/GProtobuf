using GProtobuf.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator;

public sealed record TypeDefinition(
    bool IsStruct,
    bool IsAbstract,
    string FullName,
    List<ProtoIncludeAttribute> ProtoIncludes, // List of ProtoInclude derived classes
    List<ProtoMemberAttribute> ProtoMembers);

class ObjectTree
{
    private Dictionary<string, List<TypeDefinition>> types = new();

    private Dictionary<string, string> baseClassesForTypes = new();

    private TypeDefinition FindTypeByFullName(string fullName)
    {
        foreach (var typeList in types.Values)
        {
            var type = typeList.FirstOrDefault(t => t.FullName == fullName);
            if (type != null)
                return type;
        }
        return null;
    }

    private void WriteAncestorFields(StringBuilderWithIndent sb, string baseClassName)
    {
        var baseType = FindTypeByFullName(baseClassName);
        if (baseType == null) return;

        // First write ancestor's ancestors (recursively)
        if (baseClassesForTypes.TryGetValue(baseType.FullName, out var grandParentClassName))
        {
            WriteAncestorFields(sb, grandParentClassName);
        }

        // Then write this ancestor's fields
        if (baseType.ProtoMembers != null)
        {
            foreach (var baseMember in baseType.ProtoMembers)
            {
                WriteProtoMember(sb, baseMember);
            }
        }
    }

    private void WriteInheritedFieldsForContent(StringBuilderWithIndent sb, string typeName)
    {
        // Write inherited fields from all ancestors hierarchically
        if (baseClassesForTypes.TryGetValue(typeName, out var baseClassName))
        {
            WriteAncestorFields(sb, baseClassName);
        }
    }

    private string FindUltimateBaseClass(string typeName)
    {
        string currentType = typeName;
        string lastBaseType = currentType;
        
        // Walk up the inheritance chain to find the ultimate base class
        while (baseClassesForTypes.TryGetValue(currentType, out var baseType))
        {
            lastBaseType = baseType;
            currentType = baseType;
        }
        
        return lastBaseType;
    }

    private void WriteAllInheritanceFieldIds(StringBuilderWithIndent sb, string typeName)
    {
        var fieldIds = new List<(int FieldId, string CurrentTypeName)>();
        
        // Collect all field IDs in the inheritance chain
        CollectInheritanceFieldIds(typeName, fieldIds);
        
        // Generate if statements for all field IDs
        foreach (var (fieldId, currentTypeName) in fieldIds)
        {
            sb.AppendIndentedLine($"if (fieldId == {fieldId})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
            sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
            sb.AppendIndentedLine($"var contentResult = Read{GetClassNameFromFullName(currentTypeName)}Content(ref reader1);");
            
            // Add type check and cast if this is not the exact type expected
            if (currentTypeName != typeName)
            {
                sb.AppendIndentedLine($"if (contentResult is global::{typeName} castResult)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"result = castResult;");
                sb.AppendIndentedLine($"continue;");
                sb.EndBlock();
                sb.AppendIndentedLine($"throw new InvalidOperationException($\"Expected type {typeName} but got {{contentResult?.GetType().Name ?? \"null\"}}\");");
            }
            else
            {
                sb.AppendIndentedLine($"result = contentResult;");
                sb.AppendIndentedLine($"continue;");
            }
            
            sb.EndBlock();
            sb.AppendNewLine();
        }
    }

    private void CollectInheritanceFieldIds(string typeName, List<(int FieldId, string CurrentTypeName)> fieldIds)
    {
        // Check if this type has a base class
        if (baseClassesForTypes.TryGetValue(typeName, out var baseClassName))
        {
            // First collect field IDs from parent's hierarchy  
            CollectInheritanceFieldIds(baseClassName, fieldIds);
            
            // Then add the field ID that references current type from its base class
            var baseType = FindTypeByFullName(baseClassName);
            if (baseType != null)
            {
                var protoInclude = baseType.ProtoIncludes.FirstOrDefault(pi => pi.Type == typeName);
                if (protoInclude != null)
                {
                    fieldIds.Add((protoInclude.FieldId, typeName));
                }
            }
        }
    }

    private void WriteAllAncestorProtoIncludes(StringBuilderWithIndent sb, string typeName)
    {
        // Nothing to do here anymore - ProtoInclude handling is now done differently
        // This method is kept for compatibility but will be empty
    }
    

    private void CollectAncestorProtoIncludes(string typeName, List<(int FieldId, string TypeName)> ancestorIncludes)
    {
        if (baseClassesForTypes.TryGetValue(typeName, out var baseClassName))
        {
            // First collect from ancestors (recursive)
            CollectAncestorProtoIncludes(baseClassName, ancestorIncludes);
            
            // Then add this type's ProtoInclude reference from its base class
            var baseType = FindTypeByFullName(baseClassName);
            if (baseType != null)
            {
                var protoInclude = baseType.ProtoIncludes.FirstOrDefault(pi => pi.Type == typeName);
                if (protoInclude != null)
                {
                    ancestorIncludes.Add((protoInclude.FieldId, typeName));
                }
            }
        }
    }

    // This method is no longer used - fields are written directly in the Write methods
    // Kept for potential future use
    private void WriteAllMembersForSerialization(StringBuilderWithIndent sb, TypeDefinition obj)
    {
        // Write own members first
        if (obj.ProtoMembers != null)
        {
            foreach (var protoMember in obj.ProtoMembers)
            {
                WriteProtoMemberSerializer(sb, protoMember);
            }
        }
        
        // Write inherited members from all ancestors
        WriteInheritedMembersForSerialization(sb, obj.FullName);
    }

    private void WriteInheritedMembersForSerialization(StringBuilderWithIndent sb, string typeName)
    {
        if (baseClassesForTypes.TryGetValue(typeName, out var baseClassName))
        {
            var baseType = FindTypeByFullName(baseClassName);
            if (baseType != null)
            {
                // Write ancestor's inherited members first (recursive)
                WriteInheritedMembersForSerialization(sb, baseType.FullName);
                
                // Then write this ancestor's own members
                if (baseType.ProtoMembers != null)
                {
                    foreach (var baseMember in baseType.ProtoMembers)
                    {
                        WriteProtoMemberSerializer(sb, baseMember);
                    }
                }
            }
        }
    }

    // These methods are no longer used after refactoring
    // Kept for reference but can be removed

    public void AddType(string nmspace, string fullName, bool isStruct, bool isAbstract, List<ProtoIncludeAttribute> protoIncludes, List<ProtoMemberAttribute> protoMembers)
    {
        AddType(nmspace, new TypeDefinition(isStruct, isAbstract, fullName, protoIncludes, protoMembers));
    }

    public void AddType(string @namespace, TypeDefinition typeDefinition)
    {
        if (!types.TryGetValue(@namespace, out var typeDefinitions))
        {
            typeDefinitions = [];
            types[@namespace] = typeDefinitions;
        }

        typeDefinitions.Add(typeDefinition);

        if (typeDefinition.ProtoIncludes != null)
        {
            foreach (var protoInclude in typeDefinition.ProtoIncludes)
            {
                if (!baseClassesForTypes.ContainsKey(protoInclude.Type))
                {
                    baseClassesForTypes[protoInclude.Type] = typeDefinition.FullName;
                }
            }
        }
    }

    public IEnumerable<(string FileName, string FileCode)> GenerateCode()
    {
        var sb = new StringBuilderWithIndent();

        foreach (var namespaceWithObjects in types)
        {
            var nmspace = namespaceWithObjects.Key;
            GenerateCode(sb, namespaceWithObjects, nmspace);

            yield return (nmspace + ".Serialization.cs", sb.ToString());
        }
    }

    private void GenerateCode(
        StringBuilderWithIndent sb,
        KeyValuePair<string, List<TypeDefinition>> namespaceWithObjects,
        string nmspace)
    {
        sb.Clear();
        sb.AppendIndentedLine("// <auto-generated/>");
        sb.AppendIndentedLine("using GProtobuf.Core;\r\nusing System;\r\nusing System.Collections.Generic;\r\nusing System.IO;\r\nusing System.Text;\r\n");

        var objects = namespaceWithObjects.Value;

        sb.AppendIndentedLine($"namespace {nmspace}.Serialization");
        sb.StartNewBlock();
        sb.AppendIndentedLine("public static class Deserializers");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static global::{obj.FullName} Deserialize{GetClassNameFromFullName(obj.FullName)}(ReadOnlySpan<byte> data)");
            sb.StartNewBlock();
            sb.AppendIndentedLine("var reader = new SpanReader(data);");
            sb.AppendIndentedLine($"return SpanReaders.Read{GetClassNameFromFullName(obj.FullName)}(ref reader);");
            sb.EndBlock();
            sb.AppendNewLine();
        }

        sb.EndBlock();

        sb.AppendNewLine();

        sb.AppendIndentedLine("public static class Serializers");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Serialize{GetClassNameFromFullName(obj.FullName)}(Stream stream, global::{obj.FullName} obj)");
            sb.StartNewBlock();
            sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream);");
            sb.AppendIndentedLine($"StreamWriters.Write{GetClassNameFromFullName(obj.FullName)}(writer, obj);");
            sb.EndBlock();
            sb.AppendNewLine();
        }

        sb.EndBlock();

        sb.AppendNewLine();

        sb.AppendIndentedLine($"public static class SpanReaders");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static global::{obj.FullName} Read{GetClassNameFromFullName(obj.FullName)}(ref SpanReader reader)");
            sb.StartNewBlock();

            // Check if this type is part of inheritance hierarchy (has ProtoIncludes OR is referenced in base class ProtoInclude)
            bool isPartOfInheritanceHierarchy = obj.ProtoIncludes.Count > 0 || baseClassesForTypes.ContainsKey(obj.FullName);
            
            if (!isPartOfInheritanceHierarchy)
            {
                // Simple case - no inheritance, just read content
                sb.AppendIndentedLine($"return Read{GetClassNameFromFullName(obj.FullName)}Content(ref reader);");
            }
            else
            {
                // Complex case - check for ProtoInclude first
                sb.AppendIndentedLine($"global::{obj.FullName} result = default(global::{obj.FullName});\r\n");
                sb.AppendIndentedLine($"while(!reader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");

                WriteProtoIncludesInDeserializers(sb, obj);

                // In main Read methods, write ONLY ultimate base class fields
                // All other type-specific fields are handled in ReadXContent methods
                if (baseClassesForTypes.ContainsKey(obj.FullName))
                {
                    // This is a derived class - write only ultimate base class fields
                    var ultimateBaseClassName = FindUltimateBaseClass(obj.FullName);
                    var ultimateBaseType = FindTypeByFullName(ultimateBaseClassName);
                    
                    if (ultimateBaseType != null && ultimateBaseType.ProtoMembers != null)
                    {
                        foreach (var baseMember in ultimateBaseType.ProtoMembers)
                        {
                            WriteProtoMember(sb, baseMember);
                        }
                    }
                }
                else if (obj.ProtoMembers != null)
                {
                    // This is a base class - write own members
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        WriteProtoMember(sb, protoMember);
                    }
                }

                sb.AppendIndentedLine($"// default");
                sb.AppendIndentedLine($"reader.SkipField(wireType);");
                sb.EndBlock();
                sb.AppendIndentedLine($"return result;");
            }
            sb.EndBlock();
            sb.AppendNewLine();
        }

        sb.AppendNewLine();

        // Generate Content reading methods
        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static global::{obj.FullName} Read{GetClassNameFromFullName(obj.FullName)}Content(ref SpanReader reader)");
            sb.StartNewBlock();

            // Check if this type is part of inheritance hierarchy (has ProtoIncludes OR is referenced in base class ProtoInclude)
            bool isPartOfInheritanceHierarchy = obj.ProtoIncludes.Count > 0 || baseClassesForTypes.ContainsKey(obj.FullName);
            
            if (!isPartOfInheritanceHierarchy)
            {
                // Simple case - no inheritance, just create and fill instance
                sb.AppendIndentedLine($"global::{obj.FullName} result = new global::{obj.FullName}();\r\n");
                sb.AppendIndentedLine($"while(!reader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");

                if (obj.ProtoMembers != null)
                {
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        WriteProtoMember(sb, protoMember);
                    }
                }


                sb.AppendIndentedLine($"// default");
                sb.AppendIndentedLine($"reader.SkipField(wireType);");
                sb.EndBlock();
                sb.AppendIndentedLine($"return result;");
            }
            else
            {
                // Complex case - check for ProtoInclude first and determine correct type
                // For non-abstract types, create instance immediately to handle cases with no type-specific fields
                if (obj.IsAbstract)
                {
                    sb.AppendIndentedLine($"global::{obj.FullName} result = default(global::{obj.FullName});\r\n");
                }
                else
                {
                    sb.AppendIndentedLine($"global::{obj.FullName} result = new global::{obj.FullName}();\r\n");
                }
                
                sb.AppendIndentedLine($"while(!reader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");

                // Write own ProtoIncludes first (derived classes)
                if (obj.ProtoIncludes.Count > 0)
                {
                    foreach (var protoInclude in obj.ProtoIncludes)
                    {
                        sb.AppendIndentedLine($"if (fieldId == {protoInclude.FieldId})");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                        sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                        sb.AppendIndentedLine($"result = Read{GetClassNameFromFullName(protoInclude.Type)}Content(ref reader1);");
                        sb.AppendIndentedLine($"continue;");
                        sb.EndBlock();
                        sb.AppendNewLine();
                    }
                }

                // Only for abstract types, check if instance was created via ProtoInclude
                if (obj.IsAbstract)
                {
                    sb.AppendIndentedLine($"if (result == null)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"throw new InvalidOperationException($\"ProtoInclude field must be first for abstract type {obj.FullName}. Is {{fieldId}} defined in ProtoInclude attributes?\");");
                    sb.EndBlock();
                    sb.AppendNewLine();
                }

                // Write fields specific to this type
                if (obj.ProtoMembers != null)
                {
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        WriteProtoMember(sb, protoMember);
                    }
                }


                sb.AppendIndentedLine($"// default");
                sb.AppendIndentedLine($"reader.SkipField(wireType);");
                sb.EndBlock();
                sb.AppendIndentedLine($"return result;");
            }
            
            sb.EndBlock();
            sb.AppendNewLine();
        }

        sb.EndBlock();

        sb.AppendNewLine();

        sb.AppendIndentedLine("public static class StreamWriters");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Write{GetClassNameFromFullName(obj.FullName)}(global::GProtobuf.Core.StreamWriter writer, global::{obj.FullName} instance)");
            sb.StartNewBlock();

            // Check if this type is derived (has a base class)
            if (baseClassesForTypes.ContainsKey(obj.FullName))
            {
                // For derived types like C, we need special handling
                // For C: write tag 5 (B wrapper) containing [tag 10 (C wrapper) with length 0, B fields], then A fields
                
                // Build inheritance chain from base to this type
                var inheritanceChain = new List<string>();
                var currentType = obj.FullName;
                while (baseClassesForTypes.TryGetValue(currentType, out var baseType))
                {
                    inheritanceChain.Insert(0, currentType);
                    currentType = baseType;
                }
                inheritanceChain.Insert(0, currentType); // Add the base type
                
                // For a 3-level hierarchy A <- B <- C:
                // inheritanceChain = ["Model.A", "Model.B", "Model.C"]
                
                if (inheritanceChain.Count == 3)
                {
                    // Special case for 3-level hierarchy (A <- B <- C)
                    var classA = inheritanceChain[0];
                    var classB = inheritanceChain[1]; 
                    var classC = inheritanceChain[2];
                    
                    var typeA = FindTypeByFullName(classA);
                    var typeB = FindTypeByFullName(classB);
                    
                    var protoIncludeAtoB = typeA?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classB);
                    var protoIncludeBtoC = typeB?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classC);
                    
                    if (protoIncludeAtoB != null && protoIncludeBtoC != null)
                    {
                        sb.AppendNewLine();
                        // Write tag 5 (B wrapper)
                        sb.AppendIndentedLine($"writer.WriteTag({protoIncludeAtoB.FieldId}, WireType.Len);");
                        
                        // Calculate B wrapper content size inline (same as CalculateCSize)
                        sb.AppendIndentedLine($"var calculator{protoIncludeAtoB.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.WriteTag({protoIncludeBtoC.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"var tempCalculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref tempCalculatorC, instance);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.WriteVarUInt32((uint)tempCalculatorC.Length);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.AddByteLength(tempCalculatorC.Length);");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classB)}ContentSize(ref calculator{protoIncludeAtoB.FieldId}, instance);");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{protoIncludeAtoB.FieldId}.Length);");
                        
                        // Write the actual B wrapper content inline (Tag10 + C fields + B fields)
                        sb.AppendIndentedLine($"writer.WriteTag({protoIncludeBtoC.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"var calculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref calculatorC, instance);");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculatorC.Length);");
                        
                        // Write C fields  
                        var typeC = FindTypeByFullName(classC);
                        if (typeC?.ProtoMembers != null)
                        {
                            foreach (var member in typeC.ProtoMembers)
                            {
                                WriteProtoMemberSerializer(sb, member);
                            }
                        }
                        
                        // Write B fields
                        if (typeB?.ProtoMembers != null)
                        {
                            foreach (var member in typeB.ProtoMembers)
                            {
                                WriteProtoMemberSerializer(sb, member);
                            }
                        }
                    }
                    
                    // Write A fields (outside the wrapper)
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member);
                        }
                    }
                }
                else if (inheritanceChain.Count == 2)
                {
                    // Simple 2-level hierarchy (A <- B)
                    var classA = inheritanceChain[0];
                    var classB = inheritanceChain[1];
                    
                    var typeA = FindTypeByFullName(classA);
                    var protoInclude = typeA?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classB);
                    
                    if (protoInclude != null)
                    {
                        sb.AppendNewLine();
                        sb.AppendIndentedLine($"writer.WriteTag({protoInclude.FieldId}, WireType.Len);");
                        // Check if this type has its own fields
                        if (obj.ProtoMembers != null && obj.ProtoMembers.Count > 0)
                        {
                            // Calculate size of own fields
                            sb.AppendIndentedLine($"var calculatorContent = new global::GProtobuf.Core.WriteSizeCalculator();");
                            sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(obj.FullName)}ContentSize(ref calculatorContent, instance);");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculatorContent.Length);");
                        }
                        else
                        {
                            sb.AppendIndentedLine($"writer.WriteVarint32(0); // No fields inside final ProtoInclude");
                        }
                    }
                    
                    // Write own fields (B fields) into the wrapper
                    if (obj.ProtoMembers != null)
                    {
                        foreach (var member in obj.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member);
                        }
                    }
                    
                    // Write A fields (inherited) outside the wrapper
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member);
                        }
                    }
                }
                else
                {
                    // Generic case for deeper hierarchies - TODO: implement if needed
                    sb.AppendIndentedLine($"// TODO: Handle hierarchy deeper than 3 levels");
                }
            }
            else
            {
                // Base type or non-inherited type
                
                // Write own ProtoIncludes (for switch on derived types)
                if (obj.ProtoIncludes.Count > 0)
                {
                    sb.AppendNewLine();
                    sb.AppendIndentedLine("switch (instance)");
                    sb.StartNewBlock();
                    foreach (var include in obj.ProtoIncludes)
                    {
                        var className = GetClassNameFromFullName(include.Type);
                        sb.AppendIndentedLine($"case global::{include.Type} obj1:");
                        sb.IncreaseIndent();
                        sb.AppendIndentedLine($"var calculator{include.FieldId}_{className} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        // Calculate size of content that will be written inline
                        
                        // For nested ProtoIncludes (like C within B)
                        var typeForSizeCalc = FindTypeByFullName(include.Type);
                        if (typeForSizeCalc?.ProtoIncludes != null && typeForSizeCalc.ProtoIncludes.Count > 0)
                        {
                            foreach (var bInclude in typeForSizeCalc.ProtoIncludes)
                            {
                                var cClassName = GetClassNameFromFullName(bInclude.Type);
                                sb.AppendIndentedLine($"if (obj1 is global::{bInclude.Type} objC{bInclude.FieldId})");
                                sb.StartNewBlock();
                                sb.AppendIndentedLine($"calculator{include.FieldId}_{className}.WriteTag({bInclude.FieldId}, WireType.Len);");
                                sb.AppendIndentedLine($"var tempCalcC{bInclude.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                                sb.AppendIndentedLine($"SizeCalculators.Calculate{cClassName}ContentSize(ref tempCalcC{bInclude.FieldId}, objC{bInclude.FieldId});");
                                sb.AppendIndentedLine($"calculator{include.FieldId}_{className}.WriteVarUInt32((uint)tempCalcC{bInclude.FieldId}.Length);");
                                sb.AppendIndentedLine($"calculator{include.FieldId}_{className}.AddByteLength(tempCalcC{bInclude.FieldId}.Length);");
                                sb.EndBlock();
                            }
                        }
                        
                        // Calculate B's own fields and A's inherited fields
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref calculator{include.FieldId}_{className}, obj1);");
                        
                        sb.AppendIndentedLine($"writer.WriteTag({include.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{include.FieldId}_{className}.Length);");
                        // Inline write B content to avoid duplicate wrapper  
                        sb.AppendIndentedLine($"// Write B wrapper content inline");
                        sb.AppendIndentedLine($"switch (obj1)");
                        sb.StartNewBlock();
                        
                        // Check if B has ProtoIncludes (for C)
                        var typeB = FindTypeByFullName(include.Type);
                        if (typeB?.ProtoIncludes != null && typeB.ProtoIncludes.Count > 0)
                        {
                            foreach (var bInclude in typeB.ProtoIncludes)
                            {
                                var cClassName = GetClassNameFromFullName(bInclude.Type);
                                sb.AppendIndentedLine($"case global::{bInclude.Type} objC:");
                                sb.IncreaseIndent();
                                
                                sb.AppendIndentedLine($"writer.WriteTag({bInclude.FieldId}, WireType.Len);");
                                sb.AppendIndentedLine($"var calculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                                sb.AppendIndentedLine($"SizeCalculators.Calculate{cClassName}ContentSize(ref calculatorC, objC);");
                                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculatorC.Length);");
                                
                                // Write C fields using objC (not obj)
                                var typeC = FindTypeByFullName(bInclude.Type);
                                if (typeC?.ProtoMembers != null)
                                {
                                    foreach (var cMember in typeC.ProtoMembers)
                                    {
                                        WriteProtoMemberSerializerWithObject(sb, cMember, "objC");
                                    }
                                }
                                
                                sb.AppendIndentedLine("break;");
                                sb.DecreaseIndent();
                            }
                        }
                        
                        sb.EndBlock();
                        
                        // Write B fields using obj1 (not obj)  
                        if (typeB?.ProtoMembers != null)
                        {
                            foreach (var bMember in typeB.ProtoMembers)
                            {
                                WriteProtoMemberSerializerWithObject(sb, bMember, "obj1");
                            }
                        }
                        sb.AppendIndentedLine("break;");
                        sb.DecreaseIndent();
                    }
                    sb.EndBlock();
                }
                
                // Write own fields (only for base types, not derived types)
                // Derived types write their own fields inside the wrapper
                if (obj.ProtoMembers != null && !baseClassesForTypes.ContainsKey(obj.FullName))
                {
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        WriteProtoMemberSerializer(sb, protoMember);
                    }
                }
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }

        // Add Guid helper method for StreamWriters
        sb.AppendNewLine();
        sb.AppendIndentedLine("public static void WriteGuid(global::GProtobuf.Core.StreamWriter writer, Guid value)");
        sb.StartNewBlock();
        sb.AppendIndentedLine("writer.WriteGuid(value);");
        sb.EndBlock();

        sb.EndBlock();

        sb.AppendNewLine();

        // Generate SizeCalculators
        sb.AppendIndentedLine("public static class SizeCalculators");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Calculate{GetClassNameFromFullName(obj.FullName)}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{obj.FullName} obj)");
            sb.StartNewBlock();

            // For derived types, calculate ProtoInclude wrapper sizes
            if (baseClassesForTypes.ContainsKey(obj.FullName))
            {
                // Build inheritance chain from base to this type
                var inheritanceChain = new List<string>();
                var currentType = obj.FullName;
                while (baseClassesForTypes.TryGetValue(currentType, out var baseType))
                {
                    inheritanceChain.Insert(0, currentType);
                    currentType = baseType;
                }
                inheritanceChain.Insert(0, currentType); // Add the base type
                
                if (inheritanceChain.Count == 3)
                {
                    // Special case for 3-level hierarchy (A <- B <- C)
                    var classA = inheritanceChain[0];
                    var classB = inheritanceChain[1];
                    var classC = inheritanceChain[2];
                    
                    var typeA = FindTypeByFullName(classA);
                    var typeB = FindTypeByFullName(classB);
                    
                    var protoIncludeAtoB = typeA?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classB);
                    var protoIncludeBtoC = typeB?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classC);
                    
                    if (protoIncludeAtoB != null && protoIncludeBtoC != null)
                    {
                        sb.AppendNewLine();
                        // Calculate tag 5 (B wrapper) size
                        sb.AppendIndentedLine($"calculator.WriteTag({protoIncludeAtoB.FieldId}, WireType.Len);");
                        
                        // Calculate B wrapper content inline (Tag10 + C size + C content + B content)
                        sb.AppendIndentedLine($"var tempCalc{protoIncludeAtoB.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"tempCalc{protoIncludeAtoB.FieldId}.WriteTag({protoIncludeBtoC.FieldId}, WireType.Len);");
                        
                        sb.AppendIndentedLine($"var tempCalcC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"Calculate{GetClassNameFromFullName(classC)}ContentSize(ref tempCalcC, obj);");
                        sb.AppendIndentedLine($"tempCalc{protoIncludeAtoB.FieldId}.WriteVarUInt32((uint)tempCalcC.Length);");
                        sb.AppendIndentedLine($"tempCalc{protoIncludeAtoB.FieldId}.AddByteLength(tempCalcC.Length);");
                        
                        sb.AppendIndentedLine($"Calculate{GetClassNameFromFullName(classB)}ContentSize(ref tempCalc{protoIncludeAtoB.FieldId}, obj);");
                        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempCalc{protoIncludeAtoB.FieldId}.Length);");
                    }
                    
                    // Add A fields to size
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSizeCalculator(sb, member);
                        }
                    }
                }
                else if (inheritanceChain.Count == 2)
                {
                    // Simple 2-level hierarchy (A <- B)
                    var classA = inheritanceChain[0];
                    var classB = inheritanceChain[1];
                    
                    var typeA = FindTypeByFullName(classA);
                    var protoInclude = typeA?.ProtoIncludes.FirstOrDefault(pi => pi.Type == classB);
                    
                    if (protoInclude != null)
                    {
                        sb.AppendNewLine();
                        sb.AppendIndentedLine($"calculator.WriteTag({protoInclude.FieldId}, WireType.Len);");
                        // Check if this type has its own fields
                        if (obj.ProtoMembers != null && obj.ProtoMembers.Count > 0)
                        {
                            // Calculate size of own fields
                            sb.AppendIndentedLine($"var tempCalcContent = new global::GProtobuf.Core.WriteSizeCalculator();");
                            sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(obj.FullName)}ContentSize(ref tempCalcContent, obj);");
                            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempCalcContent.Length);");
                        }
                        else
                        {
                            sb.AppendIndentedLine($"calculator.WriteVarint32(0); // No fields inside final ProtoInclude");
                        }
                    }
                    
                    // Add A fields to size
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSizeCalculator(sb, member);
                        }
                    }
                }
                else
                {
                    // Generic case for deeper hierarchies - TODO: implement if needed
                    sb.AppendIndentedLine($"// TODO: Handle hierarchy deeper than 3 levels");
                }
            }

            else
            {
                // Base type or non-inherited type
                
                // Calculate own ProtoIncludes (for switch on derived types)
                if (obj.ProtoIncludes.Count > 0)
                {
                    sb.AppendNewLine();
                    sb.AppendIndentedLine("switch (obj)");
                    sb.StartNewBlock();
                    foreach (var include in obj.ProtoIncludes)
                    {
                        var className = GetClassNameFromFullName(include.Type);
                        sb.AppendIndentedLine($"case global::{include.Type} obj1:");
                        sb.IncreaseIndent();
                        sb.AppendIndentedLine($"calculator.WriteTag({include.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"var lengthBefore{include.FieldId} = calculator.Length;");
                        sb.AppendIndentedLine($"Calculate{className}Size(ref calculator, obj1);");
                        sb.AppendIndentedLine($"var contentLength{include.FieldId} = calculator.Length - lengthBefore{include.FieldId};");
                        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)contentLength{include.FieldId});");
                        sb.AppendIndentedLine("break;");
                        sb.DecreaseIndent();
                    }
                    sb.EndBlock();
                }
                
                // Calculate own fields
                if (obj.ProtoMembers != null)
                {
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        WriteProtoMemberSizeCalculator(sb, protoMember);
                    }
                }
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }

        sb.AppendNewLine();

        // Generate Content size calculators (without ProtoInclude wrapping)
        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Calculate{GetClassNameFromFullName(obj.FullName)}ContentSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, global::{obj.FullName} obj)");
            sb.StartNewBlock();

            // Calculate only content - no ProtoInclude wrapping, just OWN members (NOT inherited)
            // ContentSize methods should only include fields defined in THIS class
            if (obj.ProtoMembers != null)
            {
                foreach (var protoMember in obj.ProtoMembers)
                {
                    WriteProtoMemberSizeCalculator(sb, protoMember);
                }
            }
            
            // DO NOT write inherited members in ContentSize methods
            // They are handled separately in the full Size methods

            sb.EndBlock();
            sb.AppendNewLine();
        }

        // Add Guid helper method for SizeCalculators
        sb.AppendNewLine();
        sb.AppendIndentedLine("public static void CalculateGuidSize(ref global::GProtobuf.Core.WriteSizeCalculator calculator, Guid value)");
        sb.StartNewBlock();
        sb.AppendIndentedLine("calculator.WriteGuid(value);");
        sb.EndBlock();

        sb.EndBlock();
        sb.EndBlock(); // Close namespace
    }

    private void WriteProtoIncludesInDeserializers(StringBuilderWithIndent sb, TypeDefinition obj)
    {
        // Write own ProtoIncludes first (derived classes)
        if (obj.ProtoIncludes.Count > 0)
        {
            foreach (var protoInclude in obj.ProtoIncludes)
            {
                sb.AppendIndentedLine($"if (fieldId == {protoInclude.FieldId})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                sb.AppendIndentedLine($"var contentResult = Read{GetClassNameFromFullName(protoInclude.Type)}Content(ref reader1);");
                sb.AppendIndentedLine($"result = contentResult;");
                sb.AppendIndentedLine($"continue;");
                sb.EndBlock();
                sb.AppendNewLine();
            }
        }

        // Generate all ProtoInclude field IDs in the entire inheritance hierarchy for this type
        WriteAllInheritanceFieldIds(sb, obj.FullName);
        
        sb.AppendIndentedLine($"if (result == null)");
        sb.StartNewBlock();
        // If this type is referenced in base class ProtoInclude, it must have field ID at the beginning
        bool isInBaseProtoInclude = baseClassesForTypes.ContainsKey(obj.FullName);
        if (obj.IsAbstract || isInBaseProtoInclude)
        {
            string typeDescription = obj.IsAbstract ? "abstract type" : "ProtoInclude type";
            sb.AppendIndentedLine($"throw new InvalidOperationException($\"ProtoInclude field must be first for {typeDescription} {obj.FullName}. Is {{fieldId}} defined in ProtoInclude attributes?\");");
        }
        else
        {
            sb.AppendIndentedLine($"result = new global::{obj.FullName}();");
        }
        sb.EndBlock();
        sb.AppendNewLine();
        // todo hybrid binary tree for many proto includes
    }


    private static void WriteProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        sb.AppendIndentedLine($"if (fieldId == {protoMember.FieldId})");
        sb.StartNewBlock();
        
        var typeName = GetClassNameFromFullName(protoMember.Type);
        
        // Check collection types in priority order
        if (IsByteCollectionType(protoMember))
        {
            WriteByteCollectionProtoMember(sb, protoMember);
            return;
        }
        else if (IsPrimitiveCollectionType(protoMember))
        {
            WritePrimitiveCollectionProtoMember(sb, protoMember);
            return;
        }
        else if (IsArrayType(protoMember))
        {
            WriteArrayProtoMember(sb, protoMember);
            return;
        }
        
        switch(typeName)
        {
            case "System.Int32":
            case "Int32":
            case "int":
                switch (protoMember.DataFormat)
                {
                    case DataFormat.FixedSize:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadFixedInt32();");
                        break;

                    case DataFormat.ZigZag:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadZigZagVarInt32();");
                        break;

                    default:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadVarInt32();");
                        break;
                }
                break;

            case "bool":
            case "Boolean":
            case "System.Boolean":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadBool(wireType);");
                break;

            case "byte":
            case "Byte":
            case "System.Byte":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadByte(wireType);");
                break;

            case "sbyte":
            case "SByte":
            case "System.SByte":
                switch (protoMember.DataFormat)
                {
                    case DataFormat.ZigZag:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadSByte(wireType, true);");
                        break;
                    default:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadSByte(wireType, false);");
                        break;
                }
                break;

            case "short":
            case "Int16":
            case "System.Int16":
                switch (protoMember.DataFormat)
                {
                    case DataFormat.ZigZag:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadInt16(wireType, true);");
                        break;
                    default:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadInt16(wireType, false);");
                        break;
                }
                break;

            case "ushort":
            case "UInt16":
            case "System.UInt16":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadUInt16(wireType);");
                break;

            case "uint":
            case "UInt32":
            case "System.UInt32":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadUInt32(wireType);");
                break;

            case "long":
            case "Int64":
            case "System.Int64":
                switch (protoMember.DataFormat)
                {
                    case DataFormat.ZigZag:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadInt64(wireType, true);");
                        break;
                    default:
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadInt64(wireType, false);");
                        break;
                }
                break;

            case "ulong":
            case "UInt64":
            case "System.UInt64":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadUInt64(wireType);");
                break;

            case "float":
            case "Single":
            case "System.Single":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadFloat(wireType);");
                break;

            case "Double":
            case "double":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadDouble(wireType);");
                break;


            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadString(wireType);");
                break;

            case "Guid":
            case "System.Guid":
                sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                sb.AppendIndentedLine($"// Read BCL Guid format (2 fixed64 fields)");
                sb.AppendIndentedLine($"ulong low = 0, high = 0;");
                sb.AppendIndentedLine($"while (!reader1.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var fieldInfo = reader1.ReadWireTypeAndFieldId();");
                sb.AppendIndentedLine($"switch (fieldInfo.fieldId)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"case 1: low = reader1.ReadFixed64(); break;");
                sb.AppendIndentedLine($"case 2: high = reader1.ReadFixed64(); break;");
                sb.AppendIndentedLine($"default: reader1.SkipField(fieldInfo.wireType); break;");
                sb.EndBlock();
                sb.EndBlock();
                sb.AppendIndentedLine($"// Convert back to Guid");
                sb.AppendIndentedLine($"Span<byte> guidBytes = stackalloc byte[16];");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes, low);");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes.Slice(8), high);");
                sb.AppendIndentedLine($"result.{protoMember.Name} = new Guid(guidBytes);");
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadByteArray();");
                break;

            case "System.Int32[]":
            case "Int32[]":
            case "int[]":
                if (protoMember.IsPacked)
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeInt32Array();");
                            break;

                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedVarIntInt32Array(true);");
                            break;

                        default:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedVarIntInt32Array(false);");
                            break;
                    }
                }
                else
                {
                    string int32Reader = null;

                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            int32Reader = String.Format($"reader.ReadFixedInt32()");
                            break;

                        case DataFormat.ZigZag:
                            int32Reader = String.Format($"reader.ReadZigZagVarInt32()");
                            break;

                        default:
                            int32Reader = String.Format($"reader.ReadVarInt32()");
                            break;
                    }

                    sb.AppendIndentedLine($"List<int> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendNewLine();
                    sb.AppendIndentedLine($"while (!reader.IsEnd)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var number = {int32Reader};");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"resultList.Add(number);");
                    sb.AppendIndentedLine($"if (reader.IsEnd) break; // End of buffer, no more data");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadWireTypeAndFieldId();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendNewLine();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.Single[]":
            case "Single[]":
            case "float[]":
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFloatArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"List<float> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Fixed32b)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add(reader.ReadFloat(wireType1));");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.Double[]":
            case "Double[]":
            case "double[]":
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedDoubleArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"List<double> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Fixed64b)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add(reader.ReadDouble(wireType1));");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.Int64[]":
            case "Int64[]":
            case "long[]":
                if (protoMember.IsPacked)
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeInt64Array();");
                            break;

                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedVarIntInt64Array(true);");
                            break;

                        default:
                            sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedVarIntInt64Array(false);");
                            break;
                    }
                }
                else
                {
                    string longReader = null;

                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            longReader = String.Format($"reader.ReadFixedInt64()");
                            break;

                        case DataFormat.ZigZag:
                            longReader = String.Format($"reader.ReadZigZagVarInt64()");
                            break;

                        default:
                            longReader = String.Format($"reader.ReadVarInt64()");
                            break;
                    }

                    sb.AppendIndentedLine($"List<long> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({longReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.Boolean[]":
            case "Boolean[]":
            case "bool[]":
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedBoolArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"List<bool> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.VarInt)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add(reader.ReadBool());");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.SByte[]":
            case "SByte[]":
            case "sbyte[]":
                if (protoMember.IsPacked)
                {
                    bool zigZagFormat = protoMember.DataFormat == DataFormat.ZigZag;
                    sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedSByteArray({zigZagFormat.ToString().ToLower()});");
                }
                else
                {
                    string sbyteReader = protoMember.DataFormat switch
                    {
                        DataFormat.ZigZag => "reader.ReadSByte(wireType1, true)",
                        _ => "reader.ReadSByte(wireType1)"
                    };
                    
                    sb.AppendIndentedLine($"List<sbyte> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.VarInt)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({sbyteReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.Int16[]":
            case "Int16[]":
            case "short[]":
                if (protoMember.IsPacked)
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeInt32ArrayAsInt16();");
                    }
                    else
                    {
                        bool zigZagFormat = protoMember.DataFormat == DataFormat.ZigZag;
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedInt16Array({zigZagFormat.ToString().ToLower()});");
                    }
                }
                else
                {
                    string shortReader = protoMember.DataFormat switch
                    {
                        DataFormat.ZigZag => "reader.ReadInt16(wireType1, true)",
                        DataFormat.FixedSize => "reader.ReadFixedInt32()", // Fixed size uses 32-bit encoding
                        _ => "reader.ReadInt16(wireType1)"
                    };

                    string expectedWireType = protoMember.DataFormat == DataFormat.FixedSize ? "Fixed32b" : "VarInt";
                    
                    sb.AppendIndentedLine($"List<short> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.{expectedWireType})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({shortReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.UInt16[]":
            case "UInt16[]":
            case "ushort[]":
                if (protoMember.IsPacked)
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeUInt32ArrayAsUInt16();");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedUInt16Array();");
                    }
                }
                else
                {
                    string ushortReader = protoMember.DataFormat switch
                    {
                        DataFormat.FixedSize => "reader.ReadFixedUInt32()", // Fixed size uses 32-bit encoding
                        _ => "reader.ReadUInt16(wireType1)"
                    };

                    string expectedWireType = protoMember.DataFormat == DataFormat.FixedSize ? "Fixed32b" : "VarInt";
                    
                    sb.AppendIndentedLine($"List<ushort> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.{expectedWireType})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({ushortReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.UInt32[]":
            case "UInt32[]":
            case "uint[]":
                if (protoMember.IsPacked)
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeUInt32Array();");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedUInt32Array();");
                    }
                }
                else
                {
                    string uintReader = protoMember.DataFormat switch
                    {
                        DataFormat.FixedSize => "reader.ReadFixedUInt32()", // Fixed size uses different wire type
                        _ => "reader.ReadUInt32(wireType1)"
                    };

                    string expectedWireType = protoMember.DataFormat == DataFormat.FixedSize ? "Fixed32b" : "VarInt";
                    
                    sb.AppendIndentedLine($"List<uint> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.{expectedWireType})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({uintReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            case "System.UInt64[]":
            case "UInt64[]":
            case "ulong[]":
                if (protoMember.IsPacked)
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeUInt64Array();");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedUInt64Array();");
                    }
                }
                else
                {
                    string ulongReader = protoMember.DataFormat switch
                    {
                        DataFormat.FixedSize => "reader.ReadFixedUInt64()", // Fixed size uses different wire type
                        _ => "reader.ReadUInt64(wireType1)"
                    };

                    string expectedWireType = protoMember.DataFormat == DataFormat.FixedSize ? "Fixed64b" : "VarInt";
                    
                    sb.AppendIndentedLine($"List<ulong> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.{expectedWireType})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"resultList.Add({ulongReader});");
                    sb.AppendIndentedLine($"if (reader.EndOfData) break;");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
                    sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"reader.Position = p; // rewind");
                    sb.AppendIndentedLine($"break;");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                }
                break;

            default:

                sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                sb.AppendIndentedLine($"result.{protoMember.Name} = global::{protoMember.Namespace}.Serialization.SpanReaders.Read{GetClassNameFromFullName(protoMember.Type)}(ref reader1);");
                break;
        }
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }

    private static void WriteProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        WriteProtoMemberSerializerWithObject(sb, protoMember, "instance");
    }
    
    private static void WriteProtoMemberSerializerWithObject(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName)
    {
        // Check if it's a nullable type
        bool isNullable = protoMember.IsNullable;
        var typeName = GetClassNameFromFullName(protoMember.Type);

        // Check collection types in priority order
        if (IsByteCollectionType(protoMember))
        {
            WriteByteCollectionProtoMemberSerializer(sb, protoMember, objectName);
            return;
        }
        else if (IsPrimitiveCollectionType(protoMember))
        {
            WritePrimitiveCollectionProtoMemberSerializer(sb, protoMember, objectName);
            return;
        }
        else if (IsArrayType(protoMember))
        {
            WriteArrayProtoMemberSerializer(sb, protoMember, objectName);
            return;
        }

        switch (typeName)
        {
            case "System.Int32":
            case "Int32":
            case "int":
                if (isNullable)
                {
                    // Nullable int? - write if not null (even if value is 0)
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"writer.WriteFixedSizeInt32({objectName}.{protoMember.Name}.Value);");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"writer.WriteZigZag32({objectName}.{protoMember.Name}.Value);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"writer.WriteVarint32({objectName}.{protoMember.Name}.Value);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    // Non-nullable int - write if not 0
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"writer.WriteFixedSizeInt32({objectName}.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"writer.WriteZigZag32({objectName}.{protoMember.Name});");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"writer.WriteVarint32({objectName}.{protoMember.Name});");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "bool":
            case "Boolean":
            case "System.Boolean":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteBool({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteBool({objectName}.{protoMember.Name});");
                }
                break;

            case "byte":
            case "Byte":
            case "System.Byte":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteByte({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteByte({objectName}.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "sbyte":
            case "SByte":
            case "System.SByte":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteSByte({objectName}.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteSByte({objectName}.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteSByte({objectName}.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteSByte({objectName}.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "short":
            case "Int16":
            case "System.Int16":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteInt16({objectName}.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteInt16({objectName}.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteInt16({objectName}.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteInt16({objectName}.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "ushort":
            case "UInt16":
            case "System.UInt16":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt16({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt16({objectName}.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "uint":
            case "UInt32":
            case "System.UInt32":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt32({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt32({objectName}.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "long":
            case "Int64":
            case "System.Int64":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteInt64({objectName}.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteInt64({objectName}.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteInt64({objectName}.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteInt64({objectName}.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "ulong":
            case "UInt64":
            case "System.UInt64":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt64({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"writer.WriteUInt64({objectName}.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "Double":
            case "double":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteDouble({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteDouble({objectName}.{protoMember.Name});");
                }
                break;

            case "Single":
            case "single":
            case "float":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"writer.WriteFloat({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"writer.WriteFloat({objectName}.{protoMember.Name});");
                }
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)Encoding.UTF8.GetByteCount({objectName}.{protoMember.Name}));");
                sb.AppendIndentedLine($"writer.WriteString({objectName}.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "Guid":
            case "System.Guid":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // BCL Guid format: 2 fixed64 fields = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Convert Guid to BCL format (2 fixed64 fields)");
                    sb.AppendIndentedLine($"var guidBytes = {objectName}.{protoMember.Name}.Value.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64(guidBytes, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64(guidBytes, 8);");
                    sb.AppendIndentedLine($"// Field 1: low (fixed64)");
                    sb.AppendIndentedLine($"writer.WriteTag(1, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteFixed64(low);");
                    sb.AppendIndentedLine($"// Field 2: high (fixed64)");
                    sb.AppendIndentedLine($"writer.WriteTag(2, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteFixed64(high);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != Guid.Empty)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // BCL Guid format: 2 fixed64 fields = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Convert Guid to BCL format (2 fixed64 fields)");
                    sb.AppendIndentedLine($"var guidBytes = {objectName}.{protoMember.Name}.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64(guidBytes, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64(guidBytes, 8);");
                    sb.AppendIndentedLine($"// Field 1: low (fixed64)");
                    sb.AppendIndentedLine($"writer.WriteTag(1, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteFixed64(low);");
                    sb.AppendIndentedLine($"// Field 2: high (fixed64)");
                    sb.AppendIndentedLine($"writer.WriteTag(2, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"writer.WriteFixed64(high);");
                    sb.EndBlock();
                }
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){objectName}.{protoMember.Name}.Length);");
                sb.AppendIndentedLine($"writer.Stream.Write({objectName}.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "System.Int32[]":
            case "Int32[]":
            case "int[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length << 2));");
                            sb.AppendIndentedLine($"writer.WritePackedFixedSizeIntArray({objectName}.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) writer.WriteZigZag32(v);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) writer.WriteVarint32(v);");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedSizeInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteZigZag32(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteVarint32(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.Single[]":
            case "Single[]":
            case "float[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 4));");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFloat(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFloat(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.Double[]":
            case "Double[]":
            case "double[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 8));");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteDouble(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteDouble(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.Int64[]":
            case "Int64[]":
            case "long[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 8));");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint64(v); }}");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteVarint64(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.Boolean[]":
            case "Boolean[]":
            case "bool[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"var packedSize = Utils.GetBoolPackedCollectionSize({objectName}.{protoMember.Name});");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteBool(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteBool(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.SByte[]":
            case "SByte[]":
            case "sbyte[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    
                    if (protoMember.DataFormat == DataFormat.ZigZag)
                    {
                        sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSizeSByte({objectName}.{protoMember.Name});");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteSByte(v, true); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeSByte({objectName}.{protoMember.Name});");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteSByte(v); }}");
                    }
                }
                else
                {
                    string writeMethod = protoMember.DataFormat == DataFormat.ZigZag ? "writer.WriteSByte(v, true)" : "writer.WriteSByte(v)";
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); {writeMethod}; }}");
                }
                sb.EndBlock();
                break;

            case "System.Int16[]":
            case "Int16[]":
            case "short[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 4)); // 4 bytes per short (fixed32)");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSizeInt16({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteInt16(v, true); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeInt16({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteInt16(v); }}");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteInt16(v, true); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteInt16(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt16[]":
            case "UInt16[]":
            case "ushort[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 4)); // 4 bytes per ushort (fixed32)");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeUInt16({objectName}.{protoMember.Name});");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteUInt16(v); }}");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteUInt16(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt32[]":
            case "UInt32[]":
            case "uint[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 4)); // 4 bytes per uint");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeUInt32({objectName}.{protoMember.Name});");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteUInt32(v); }}");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteUInt32(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt64[]":
            case "UInt64[]":
            case "ulong[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                    
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 8)); // 8 bytes per ulong");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeUInt64({objectName}.{protoMember.Name});");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteUInt64(v); }}");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteUInt64(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var calculator{protoMember.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(protoMember.Type)}Size(ref calculator{protoMember.FieldId}, {objectName}.{protoMember.Name});");
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{protoMember.FieldId}.Length);");
                sb.AppendIndentedLine($"StreamWriters.Write{GetClassNameFromFullName(protoMember.Type)}(writer, {objectName}.{protoMember.Name});");
                sb.EndBlock();
                break;
        }

        sb.AppendNewLine();
    }

    private static void WriteProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        // Check if it's a nullable type
        bool isNullable = protoMember.IsNullable;
        var typeName = GetClassNameFromFullName(protoMember.Type);

        // Check collection types in priority order
        if (IsByteCollectionType(protoMember))
        {
            WriteByteCollectionProtoMemberSizeCalculator(sb, protoMember);
            return;
        }
        else if (IsPrimitiveCollectionType(protoMember))
        {
            WritePrimitiveCollectionProtoMemberSizeCalculator(sb, protoMember);
            return;
        }
        else if (IsArrayType(protoMember))
        {
            WriteArrayProtoMemberSizeCalculator(sb, protoMember);
            return;
        }

        switch (typeName)
        {
            case "System.Int32":
            case "Int32":
            case "int":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"calculator.WriteFixedSizeInt32(obj.{protoMember.Name}.Value);");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteZigZag32(obj.{protoMember.Name}.Value);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteVarint32(obj.{protoMember.Name}.Value);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"calculator.WriteFixedSizeInt32(obj.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteZigZag32(obj.{protoMember.Name});");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteVarint32(obj.{protoMember.Name});");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "bool":
            case "Boolean":
            case "System.Boolean":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteBool(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteBool(obj.{protoMember.Name});");
                }
                break;

            case "byte":
            case "Byte":
            case "System.Byte":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteByte(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteByte(obj.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "sbyte":
            case "SByte":
            case "System.SByte":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteSByte(obj.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteSByte(obj.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteSByte(obj.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteSByte(obj.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "short":
            case "Int16":
            case "System.Int16":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteInt16(obj.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteInt16(obj.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteInt16(obj.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteInt16(obj.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "ushort":
            case "UInt16":
            case "System.UInt16":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt16(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt16(obj.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "uint":
            case "UInt32":
            case "System.UInt32":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt32(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt32(obj.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "long":
            case "Int64":
            case "System.Int64":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteInt64(obj.{protoMember.Name}.Value, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteInt64(obj.{protoMember.Name}.Value, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WriteInt64(obj.{protoMember.Name}, true);");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WriteInt64(obj.{protoMember.Name}, false);");
                            break;
                    }
                    sb.EndBlock();
                }
                break;

            case "ulong":
            case "UInt64":
            case "System.UInt64":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt64(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"calculator.WriteUInt64(obj.{protoMember.Name});");
                    sb.EndBlock();
                }
                break;

            case "Double":
            case "double":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteDouble(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteDouble(obj.{protoMember.Name});");
                }
                break;

            case "Single":
            case "single":
            case "float":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"calculator.WriteFloat(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"calculator.WriteFloat(obj.{protoMember.Name});");
                }
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"calculator.WriteString(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "Guid":
            case "System.Guid":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32(18u); // BCL Guid: 2*(tag+fixed64) = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Field 1: low (tag + fixed64)");
                    sb.AppendIndentedLine($"calculator.WriteTag(1, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.AppendIndentedLine($"// Field 2: high (tag + fixed64)");
                    sb.AppendIndentedLine($"calculator.WriteTag(2, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != Guid.Empty)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32(18u); // BCL Guid: 2*(tag+fixed64) = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Field 1: low (tag + fixed64)");
                    sb.AppendIndentedLine($"calculator.WriteTag(1, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.AppendIndentedLine($"// Field 2: high (tag + fixed64)");
                    sb.AppendIndentedLine($"calculator.WriteTag(2, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.EndBlock();
                }
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"calculator.WriteBytes(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "System.Int32[]":
            case "Int32[]":
            case "int[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"calculator.WritePackedFixedSizeIntArray(obj.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WritePackedZigZagArray(obj.{protoMember.Name});");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WritePackedVarintArray(obj.{protoMember.Name});");
                            break;
                    }
                }
                else
                {
                    // Non-packed arrays - calculate each element separately
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedSizeInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteZigZag32(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteVarint32(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.Single[]":
            case "Single[]":
            case "float[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 4));");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFloat(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFloat(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.Double[]":
            case "Double[]":
            case "double[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 8));");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteDouble(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteDouble(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.Int64[]":
            case "Int64[]":
            case "long[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 8));");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WritePackedZigZagInt64Array(obj.{protoMember.Name});");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WritePackedVarintInt64Array(obj.{protoMember.Name});");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteVarint64(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.Boolean[]":
            case "Boolean[]":
            case "bool[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    sb.AppendIndentedLine($"calculator.WritePackedBoolArray(obj.{protoMember.Name});");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteBool(v); }}");
                }
                sb.EndBlock();
                break;

            case "System.SByte[]":
            case "SByte[]":
            case "sbyte[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    if (protoMember.DataFormat == DataFormat.ZigZag)
                    {
                        sb.AppendIndentedLine($"calculator.WritePackedZigZagSByteArray(obj.{protoMember.Name});");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"calculator.WritePackedSByteArray(obj.{protoMember.Name});");
                    }
                }
                else
                {
                    string writeMethod = protoMember.DataFormat == DataFormat.ZigZag ? "calculator.WriteZigZagSByte(v)" : "calculator.WriteSByte(v)";
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); {writeMethod}; }}");
                }
                sb.EndBlock();
                break;

            case "System.Int16[]":
            case "Int16[]":
            case "short[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 4)); // 4 bytes per short (fixed32)");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"calculator.WritePackedZigZagInt16Array(obj.{protoMember.Name});");
                            break;
                        default:
                            sb.AppendIndentedLine($"calculator.WritePackedInt16Array(obj.{protoMember.Name});");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteZigZagInt16(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteInt16(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt16[]":
            case "UInt16[]":
            case "ushort[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 4)); // 4 bytes per ushort (fixed32)");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"calculator.WritePackedUInt16Array(obj.{protoMember.Name});");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedUInt16(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteUInt16(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt32[]":
            case "UInt32[]":
            case "uint[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 4)); // 4 bytes per uint");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"calculator.WritePackedUInt32Array(obj.{protoMember.Name});");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedUInt32((uint)v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteUInt32(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            case "System.UInt64[]":
            case "UInt64[]":
            case "ulong[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 8)); // 8 bytes per ulong");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"calculator.WritePackedUInt64Array(obj.{protoMember.Name});");
                    }
                }
                else
                {
                    if (protoMember.DataFormat == DataFormat.FixedSize)
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarint32(tagAndWire); calculator.WriteUInt64(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"var lengthBefore{protoMember.FieldId} = calculator.Length;");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(protoMember.Type)}Size(ref calculator, obj.{protoMember.Name});");
                sb.AppendIndentedLine($"var contentLength{protoMember.FieldId} = calculator.Length - lengthBefore{protoMember.FieldId};");
                sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)contentLength{protoMember.FieldId});");
                sb.EndBlock();
                break;
        }

        sb.AppendNewLine();
    }

    static string BoolToSource(bool value)
    {
        return value.ToString().ToLower();
    }

    public static string GetClassNameFromFullName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return string.Empty;

        // Odstránime ? na konci (nullable)
        if (fullTypeName.EndsWith("?"))
        {
            fullTypeName = fullTypeName.Substring(0, fullTypeName.Length - 1);
        }

        // Rozdelíme podľa bodiek a vezmeme poslednú časť
        string[] parts = fullTypeName.Split('.');
        return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
    }
    
    public static string GetNamespaceFromType(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
            return string.Empty;

        // Odstránime ? na konci (nullable)
        if (fullTypeName.EndsWith("?"))
        {
            fullTypeName = fullTypeName.Substring(0, fullTypeName.Length - 1);
        }

        // Rozdelíme podľa bodiek a vezmeme všetky časti okrem poslednej
        string[] parts = fullTypeName.Split('.');
        if (parts.Length <= 1)
            return string.Empty; // No namespace
            
        return string.Join(".", parts.Take(parts.Length - 1));
    }
    
    #region Array Support Helper Methods
    
    /// <summary>
    /// Checks if the given ProtoMember represents a collection type that should use generic collection logic
    /// BUT excludes primitive collections which have their own specialized implementation
    /// </summary>
    private static bool IsArrayType(ProtoMemberAttribute protoMember)
    {
        if (!protoMember.IsCollection)
            return false;
            
        // Get element type name for primitive check
        var elementTypeName = GetClassNameFromFullName(protoMember.CollectionElementType);
        
        // Exclude primitive arrays - they have their own specialized implementation with packed/zigzag/fixed options
        if (IsPrimitiveArrayType(elementTypeName))
            return false;
            
        // Only non-primitive collections (string[], Message[], List<Message>, ICollection<string>) use generic collection logic
        return true;
    }

    /// <summary>
    /// Legacy method - kept for backwards compatibility with string-based type checking
    /// </summary>
    private static bool IsArrayType(string typeName)
    {
        if (!typeName.EndsWith("[]") && !(typeName.StartsWith("List<") && typeName.EndsWith(">")))
            return false;
            
        // Get element type
        var elementType = GetArrayElementType(typeName);
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        // Exclude primitive arrays - they have their own specialized implementation with packed/zigzag/fixed options
        if (IsPrimitiveArrayType(elementTypeName))
            return false;
            
        // Only non-primitive arrays (string[], Message[]) use generic array logic
        return true;
    }
    
    /// <summary>
    /// Checks if the given element type represents a primitive that already has specialized array support
    /// Note: byte is excluded because List&lt;byte&gt; should serialize as byte[] (length-delimited)
    /// </summary>
    private static bool IsPrimitiveArrayType(string elementTypeName)
    {
        return elementTypeName switch
        {
            // These primitive types already have specialized array implementations
            "int" or "System.Int32" => true,
            "long" or "System.Int64" => true,
            "float" or "System.Single" => true,
            "double" or "System.Double" => true,
            "bool" or "System.Boolean" => true,
            "byte" or "System.Byte" => false,  // Special case: byte collections serialize as byte[]
            "sbyte" or "System.SByte" => true,
            "short" or "System.Int16" => true,
            "ushort" or "System.UInt16" => true,
            "uint" or "System.UInt32" => true,
            "ulong" or "System.UInt64" => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if the given ProtoMember represents a byte collection type that should serialize as byte[]
    /// </summary>
    private static bool IsByteCollectionType(ProtoMemberAttribute protoMember)
    {
        if (!protoMember.IsCollection) return false;
        var elementType = GetClassNameFromFullName(protoMember.CollectionElementType);
        return elementType == "byte" || elementType == "System.Byte";
    }

    /// <summary>
    /// Checks if the given ProtoMember represents a primitive collection type (int, float, etc.) 
    /// that should use specialized primitive array logic
    /// </summary>
    private static bool IsPrimitiveCollectionType(ProtoMemberAttribute protoMember)
    {
        if (!protoMember.IsCollection) return false;
        var elementType = GetClassNameFromFullName(protoMember.CollectionElementType);
        return IsPrimitiveArrayType(elementType);
    }
    
    /// <summary>
    /// Gets the element type from an array type name (e.g., "string[]" -> "string", "List<Person>" -> "Person")
    /// </summary>
    private static string GetArrayElementType(string arrayTypeName)
    {
        if (arrayTypeName.EndsWith("[]"))
        {
            return arrayTypeName.Substring(0, arrayTypeName.Length - 2);
        }
        
        if (arrayTypeName.StartsWith("List<") && arrayTypeName.EndsWith(">"))
        {
            return arrayTypeName.Substring(5, arrayTypeName.Length - 6);
        }
        
        return arrayTypeName;
    }
    
    /// <summary>
    /// Checks if the given type is a primitive type that can be handled directly
    /// Note: string is primitive but NOT in specialized array category (no packed/zigzag/fixed options)
    /// </summary>
    private static bool IsPrimitiveType(string typeName)
    {
        return typeName switch
        {
            "string" or "System.String" => true,
            _ => IsPrimitiveArrayType(typeName) // reuse the logic for numeric primitives
        };
    }
    
    /// <summary>
    /// Writes the deserialization logic for array types (string[], Message[], etc.)
    /// </summary>
    private static void WriteArrayProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        // For arrays, we always expect repeated fields (non-packed for length-delimited types)
        sb.AppendIndentedLine($"List<{elementType}> resultList = new();");
        sb.AppendIndentedLine($"var wireType1 = wireType;");
        sb.AppendIndentedLine($"var fieldId1 = fieldId;");
        
        // All length-delimited types (strings, messages) use wire type 2 (Len)
        sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Len)");
        sb.StartNewBlock();
        
        if (elementTypeName == "string" || elementTypeName == "System.String")
        {
            // String arrays - direct string reading
            sb.AppendIndentedLine($"resultList.Add(reader.ReadString(wireType));");
        }
        else if (IsPrimitiveArrayType(elementTypeName))
        {
            // This should NOT happen - primitive arrays should use specialized implementation
            sb.AppendIndentedLine($"throw new InvalidOperationException(\"Primitive array {elementTypeName}[] should use specialized implementation, not generic array logic\");");
        }
        else
        {
            // Custom message types - deserialize nested messages
            sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
            sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
            sb.AppendIndentedLine($"var item = global::{protoMember.Namespace}.Serialization.SpanReaders.Read{GetClassNameFromFullName(elementType)}(ref reader1);");
            sb.AppendIndentedLine($"resultList.Add(item);");
        }
        
        // Standard array reading loop continuation logic
        sb.AppendIndentedLine($"if (reader.EndOfData) break;");
        sb.AppendIndentedLine($"var p = reader.Position;");
        sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
        sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"reader.Position = p; // rewind");
        sb.AppendIndentedLine($"break;");
        sb.EndBlock();
        sb.EndBlock();
        
        // Assign based on collection kind
        switch (protoMember.CollectionKind)
        {
            case CollectionKind.Array:
                sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                break;
            case CollectionKind.InterfaceCollection:
                // ICollection<T>, IList<T>, IEnumerable<T> → assign List<T>
                sb.AppendIndentedLine($"result.{protoMember.Name} = resultList;");
                break;
            case CollectionKind.ConcreteCollection:
                // For concrete types like List<T> or custom collections
                var concreteType = protoMember.Type;
                if (concreteType.StartsWith("List<") || concreteType.StartsWith("System.Collections.Generic.List<") || concreteType == "System.Collections.Generic.List`1")
                {
                    // List<T> → assign directly
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList;");
                }
                else
                {
                    // Custom collection type → create instance and add items via ICollection<T>
                    sb.AppendIndentedLine($"var customCollection = new {concreteType}();");
                    sb.AppendIndentedLine($"var iCollection = (global::System.Collections.Generic.ICollection<{elementType}>)customCollection;");
                    sb.AppendIndentedLine($"foreach (var item in resultList) iCollection.Add(item);");
                    sb.AppendIndentedLine($"result.{protoMember.Name} = customCollection;");
                }
                break;
        }
        
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }
    
    /// <summary>
    /// Writes the serialization logic for array types (string[], Message[], etc.)
    /// </summary>
    private static void WriteArrayProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        // Check if array is not null
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (elementTypeName == "string" || elementTypeName == "System.String")
        {
            // String arrays - each string gets its own tag + length + content
            sb.AppendIndentedLine($"foreach (var item in {objectName}.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"if (item != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)System.Text.Encoding.UTF8.GetByteCount(item));");
            sb.AppendIndentedLine($"writer.WriteString(item);");
            sb.EndBlock();
            sb.AppendIndentedLine($"else");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type string was null; this might be as contents in a list/array\");");
            sb.EndBlock();
            sb.EndBlock();
        }
        else if (IsPrimitiveArrayType(elementTypeName))
        {
            // This should NOT happen - primitive arrays should use specialized implementation
            sb.AppendIndentedLine($"throw new InvalidOperationException(\"Primitive array {elementTypeName}[] should use specialized implementation, not generic array logic\");");
        }
        else
        {
            // Custom message types - each message gets its own tag + length + serialized content
            sb.AppendIndentedLine($"foreach (var item in {objectName}.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"if (item != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(elementType)}Size(ref calculator, item);");
            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator.Length);");
            sb.AppendIndentedLine($"StreamWriters.Write{GetClassNameFromFullName(elementType)}(writer, item);");
            sb.EndBlock();
            sb.AppendIndentedLine($"else");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {GetClassNameFromFullName(elementType)} was null; this might be as contents in a list/array\");");
            sb.EndBlock();
            sb.EndBlock();
        }
        
        sb.EndBlock();
    }
    
    /// <summary>
    /// Writes the size calculation logic for array types (string[], Message[], etc.)
    /// </summary>
    private static void WriteArrayProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        // Check if array is not null
        sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (elementTypeName == "string" || elementTypeName == "System.String")
        {
            // String arrays - each string gets tag + length + content
            sb.AppendIndentedLine($"foreach (var item in obj.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"if (item != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"calculator.WriteString(item);");
            sb.EndBlock();
            sb.AppendIndentedLine($"else");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type string was null; this might be as contents in a list/array\");");
            sb.EndBlock();
            sb.EndBlock();
        }
        else if (IsPrimitiveArrayType(elementTypeName))
        {
            // This should NOT happen - primitive arrays should use specialized implementation
            sb.AppendIndentedLine($"throw new InvalidOperationException(\"Primitive array {elementTypeName}[] should use specialized implementation, not generic array logic\");");
        }
        else
        {
            // Custom message types - each message gets tag + length + content
            sb.AppendIndentedLine($"foreach (var item in obj.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"if (item != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"var itemCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(elementType)}Size(ref itemCalculator, item);");
            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)itemCalculator.Length);");
            sb.AppendIndentedLine($"calculator.AddByteLength(itemCalculator.Length);");
            sb.EndBlock();
            sb.AppendIndentedLine($"else");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {GetClassNameFromFullName(elementType)} was null; this might be as contents in a list/array\");");
            sb.EndBlock();
            sb.EndBlock();
        }
        
        sb.EndBlock();
    }

    /// <summary>
    /// Writes the deserialization logic for byte collection types (List&lt;byte&gt;, ICollection&lt;byte&gt;, etc.)
    /// These are serialized as byte[] (length-delimited) for protobuf-net compatibility
    /// </summary>
    private static void WriteByteCollectionProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        // Deserialize as byte[]
        sb.AppendIndentedLine($"var byteArray = reader.ReadByteArray();");
        
        // Convert to appropriate collection type
        switch (protoMember.CollectionKind)
        {
            case CollectionKind.Array:
                sb.AppendIndentedLine($"result.{protoMember.Name} = byteArray;");
                break;
            case CollectionKind.InterfaceCollection:
                // ICollection<byte>, IList<byte>, IEnumerable<byte> → assign List<byte>
                sb.AppendIndentedLine($"result.{protoMember.Name} = new List<byte>(byteArray);");
                break;
            case CollectionKind.ConcreteCollection:
                var concreteType = protoMember.Type;
                if (concreteType.StartsWith("List<") || concreteType.StartsWith("System.Collections.Generic.List<") || concreteType == "System.Collections.Generic.List`1")
                {
                    // List<byte> → assign List<byte>
                    sb.AppendIndentedLine($"result.{protoMember.Name} = new List<byte>(byteArray);");
                }
                else
                {
                    // Custom collection type → create instance and add items via ICollection<byte>
                    sb.AppendIndentedLine($"var customCollection = new {concreteType}();");
                    sb.AppendIndentedLine($"var iCollection = (global::System.Collections.Generic.ICollection<byte>)customCollection;");
                    sb.AppendIndentedLine($"foreach (var b in byteArray) iCollection.Add(b);");
                    sb.AppendIndentedLine($"result.{protoMember.Name} = customCollection;");
                }
                break;
        }
        
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }

    /// <summary>
    /// Writes the deserialization logic for primitive collection types (List&lt;int&gt;, ICollection&lt;float&gt;, etc.)
    /// These use packed/non-packed arrays with DataFormat support
    /// </summary>
    private static void WritePrimitiveCollectionProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        if (protoMember.IsPacked)
        {
            // Packed reading - use specialized array readers
            var arrayReadMethod = GetPrimitiveArrayReadMethod(elementTypeName, protoMember.DataFormat, true);
            sb.AppendIndentedLine($"var primitiveArray = {arrayReadMethod};");
            
            // Convert to appropriate collection type
            switch (protoMember.CollectionKind)
            {
                case CollectionKind.Array:
                    sb.AppendIndentedLine($"result.{protoMember.Name} = primitiveArray;");
                    break;
                case CollectionKind.InterfaceCollection:
                    // ICollection<T>, IList<T>, IEnumerable<T> → assign List<T>
                    sb.AppendIndentedLine($"result.{protoMember.Name} = new List<{elementType}>(primitiveArray);");
                    break;
                case CollectionKind.ConcreteCollection:
                    var concreteType = protoMember.Type;
                    if (concreteType.StartsWith("List<") || concreteType == "System.Collections.Generic.List`1")
                    {
                        // List<T> → assign List<T>
                        sb.AppendIndentedLine($"result.{protoMember.Name} = new List<{elementType}>(primitiveArray);");
                    }
                    else
                    {
                        // Custom collection type → create instance and add items via ICollection<T>
                        sb.AppendIndentedLine($"var customCollection = new {concreteType}();");
                        sb.AppendIndentedLine($"var iCollection = (global::System.Collections.Generic.ICollection<{elementType}>)customCollection;");
                        sb.AppendIndentedLine($"foreach (var item in primitiveArray) iCollection.Add(item);");
                        sb.AppendIndentedLine($"result.{protoMember.Name} = customCollection;");
                    }
                    break;
            }
        }
        else
        {
            // Non-packed reading - generate loop-based reading similar to existing array logic
            var elementReadMethod = GetPrimitiveElementReadMethod(elementTypeName, protoMember.DataFormat);
            sb.AppendIndentedLine($"List<{elementType}> resultList = new();");
            sb.AppendIndentedLine($"var wireType1 = wireType;");
            sb.AppendIndentedLine($"var fieldId1 = fieldId;");
            sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == {GetExpectedWireType(elementTypeName, protoMember.DataFormat)})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"resultList.Add({elementReadMethod});");
            sb.AppendIndentedLine($"if (reader.EndOfData) break;");
            sb.AppendIndentedLine($"var p = reader.Position;");
            sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
            sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"reader.Position = p; // rewind");
            sb.AppendIndentedLine($"break;");
            sb.EndBlock();
            sb.EndBlock();
            
            // Convert resultList to appropriate collection type  
            switch (protoMember.CollectionKind)
            {
                case CollectionKind.Array:
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList.ToArray();");
                    break;
                case CollectionKind.InterfaceCollection:
                case CollectionKind.ConcreteCollection:
                    // For non-packed, we built List<T> directly, so assign it
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultList;");
                    break;
            }
        }
        
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }

    /// <summary>
    /// Gets the appropriate SpanReader method for reading primitive arrays
    /// </summary>
    private static string GetPrimitiveArrayReadMethod(string elementTypeName, DataFormat dataFormat, bool isPacked)
    {
        if (!isPacked)
        {
            // Non-packed reading - need to generate loop-based reading code
            // This should not be used for this function - non-packed arrays use different logic
            throw new InvalidOperationException($"GetPrimitiveArrayReadMethod should only be called for packed arrays. Use WritePrimitiveCollectionProtoMember for non-packed.");
        }
        
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "reader.ReadPackedFixedSizeInt32Array()",
                DataFormat.ZigZag => "reader.ReadPackedVarIntInt32Array(true)",
                _ => "reader.ReadPackedVarIntInt32Array(false)"
            },
            "long" or "System.Int64" => dataFormat switch  
            {
                DataFormat.FixedSize => "reader.ReadPackedFixedSizeInt64Array()",
                DataFormat.ZigZag => "reader.ReadPackedVarIntInt64Array(true)",
                _ => "reader.ReadPackedVarIntInt64Array(false)"
            },
            "float" or "System.Single" => "reader.ReadPackedFloatArray()",
            "double" or "System.Double" => "reader.ReadPackedDoubleArray()",
            "bool" or "System.Boolean" => "reader.ReadPackedBoolArray()",
            "sbyte" or "System.SByte" => dataFormat == DataFormat.ZigZag 
                ? "reader.ReadPackedSByteArray(true)" 
                : "reader.ReadPackedSByteArray(false)",
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "reader.ReadPackedFixedSizeInt16Array()",
                DataFormat.ZigZag => "reader.ReadPackedInt16Array(true)",
                _ => "reader.ReadPackedInt16Array(false)"
            },
            "ushort" or "System.UInt16" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadPackedFixedSizeUInt16Array()"
                : "reader.ReadPackedUInt16Array()",
            "uint" or "System.UInt32" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadPackedFixedSizeUInt32Array()"
                : "reader.ReadPackedUInt32Array()",
            "ulong" or "System.UInt64" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadPackedFixedSizeUInt64Array()"
                : "reader.ReadPackedUInt64Array()",
            _ => throw new InvalidOperationException($"Unsupported primitive type for collections: {elementTypeName}")
        };
    }

    /// <summary>
    /// Writes the serialization logic for byte collection types
    /// </summary>
    private static void WriteByteCollectionProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName)
    {
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
        
        // Convert collection to byte array and write directly
        if (protoMember.CollectionKind == Core.CollectionKind.Array)
        {
            // Already byte[], write directly
            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){objectName}.{protoMember.Name}.Length);");
            sb.AppendIndentedLine($"writer.WriteBytes({objectName}.{protoMember.Name});");
        }
        else
        {
            // Collection type - use span for zero allocation
            var collectionType = protoMember.Type;
            if (collectionType.Contains("List<") || collectionType.Contains("System.Collections.Generic.List"))
            {
                // List<byte> - use CollectionsMarshal.AsSpan for zero allocation
                sb.AppendIndentedLine($"var byteSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan((List<byte>){objectName}.{protoMember.Name});");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)byteSpan.Length);");
                sb.AppendIndentedLine($"writer.WriteBytes(byteSpan);");
            }
            else
            {
                // Other collection types (ICollection, IList, IEnumerable) - must convert to array
                sb.AppendIndentedLine($"var byteArray = {objectName}.{protoMember.Name}.ToArray();");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)byteArray.Length);");
                sb.AppendIndentedLine($"writer.WriteBytes(byteArray);");
            }
        }
        
        sb.EndBlock();
    }

    /// <summary>
    /// Writes the serialization logic for primitive collection types
    /// </summary>
    private static void WritePrimitiveCollectionProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (protoMember.IsPacked)
        {
            // Packed serialization - iterate directly over collection
            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementWriteMethod = GetPrimitiveElementWriteMethod(elementTypeName, protoMember.DataFormat);
            sb.AppendIndentedLine($"foreach(var item in {objectName}.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementWriteMethod.Replace("writer.", "calculator.")};"); // Calculate size first
            sb.EndBlock();
            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator.Length);");
            sb.AppendIndentedLine($"foreach(var item in {objectName}.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementWriteMethod};"); // Write actual data
            sb.EndBlock();
        }
        else
        {
            // Non-packed serialization - write individual elements
            var elementWriteMethod = GetPrimitiveElementWriteMethod(elementTypeName, protoMember.DataFormat);
            
            // Optimize for float and double arrays with batched writing
            if (elementTypeName == "float" || elementTypeName == "System.Single")
            {
                // Float uses WireType.Fixed32b which is 5
                uint tag = (uint)((protoMember.FieldId << 3) | 5); // Fixed32b = 5
                
                // Precompute VarUInt32 bytes for tag during code generation
                var tagBytes = new List<byte>();
                uint tagValue = tag;
                while (tagValue > 0x7F)
                {
                    tagBytes.Add((byte)((tagValue & 0x7F) | 0x80));
                    tagValue >>= 7;
                }
                tagBytes.Add((byte)tagValue);
                
                string tagBytesString = string.Join(", ", tagBytes.Select(b => $"0x{b:X2}"));
                int tagLength = tagBytes.Count;
                int itemSize = tagLength + 4; // tag + 4 bytes for float
                
                sb.AppendIndentedLine($"// Batched float serialization");
                sb.AppendIndentedLine($"unsafe");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"System.Span<byte> tag{protoMember.FieldId} = stackalloc byte[] {{ {tagBytesString} }};");
                sb.AppendIndentedLine($"System.Span<byte> batch = stackalloc byte[256];");
                sb.AppendIndentedLine($"int used = 0;");
                
                // Use CollectionsMarshal.AsSpan for List<T> for better performance
                if (protoMember.CollectionKind == Core.CollectionKind.ConcreteCollection && 
                    (protoMember.Type.StartsWith("List<") || protoMember.Type.StartsWith("System.Collections.Generic.List<")))
                {
                    sb.AppendIndentedLine($"var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan({objectName}.{protoMember.Name});");
                    sb.AppendIndentedLine($"fixed (byte* pBatch = batch)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"for (int i = 0; i < span.Length; i++)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"if (256 - used < {itemSize})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                    sb.AppendIndentedLine($"used = 0;");
                    sb.EndBlock();
                    sb.AppendIndentedLine($"var value = span[i];");
                    sb.AppendIndentedLine($"byte* dst = pBatch + used;");
                    sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    sb.AppendIndentedLine($"*(uint*)(dst + {tagLength}) = *(uint*)&value;");
                    sb.AppendIndentedLine($"used += {itemSize};");
                    sb.EndBlock();
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"fixed (byte* pBatch = batch)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"foreach (var value in {objectName}.{protoMember.Name})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"if (256 - used < {itemSize})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                    sb.AppendIndentedLine($"used = 0;");
                    sb.EndBlock();
                    sb.AppendIndentedLine($"byte* dst = pBatch + used;");
                    sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    sb.AppendIndentedLine($"*(uint*)(dst + {tagLength}) = *(uint*)&value;");
                    sb.AppendIndentedLine($"used += {itemSize};");
                    sb.EndBlock();
                    sb.EndBlock();
                }
                sb.AppendIndentedLine($"if (used > 0)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                sb.EndBlock();
                sb.EndBlock();
            }
            else if (elementTypeName == "double" || elementTypeName == "System.Double")
            {
                // Double uses WireType.Fixed64b which is 1
                uint tag = (uint)((protoMember.FieldId << 3) | 1); // Fixed64b = 1
                
                // Precompute VarUInt32 bytes for tag during code generation
                var tagBytes = new List<byte>();
                uint tagValue = tag;
                while (tagValue > 0x7F)
                {
                    tagBytes.Add((byte)((tagValue & 0x7F) | 0x80));
                    tagValue >>= 7;
                }
                tagBytes.Add((byte)tagValue);
                
                string tagBytesString = string.Join(", ", tagBytes.Select(b => $"0x{b:X2}"));
                int tagLength = tagBytes.Count;
                int itemSize = tagLength + 8; // tag + 8 bytes for double
                
                sb.AppendIndentedLine($"// Batched double serialization");
                sb.AppendIndentedLine($"unsafe");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"System.Span<byte> tag{protoMember.FieldId} = stackalloc byte[] {{ {tagBytesString} }};");
                sb.AppendIndentedLine($"System.Span<byte> batch = stackalloc byte[256];");
                sb.AppendIndentedLine($"int used = 0;");
                
                // Use CollectionsMarshal.AsSpan for List<T> for better performance
                if (protoMember.CollectionKind == Core.CollectionKind.ConcreteCollection && 
                    (protoMember.Type.StartsWith("List<") || protoMember.Type.StartsWith("System.Collections.Generic.List<")))
                {
                    sb.AppendIndentedLine($"var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan({objectName}.{protoMember.Name});");
                    sb.AppendIndentedLine($"fixed (byte* pBatch = batch)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"for (int i = 0; i < span.Length; i++)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"if (256 - used < {itemSize})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                    sb.AppendIndentedLine($"used = 0;");
                    sb.EndBlock();
                    sb.AppendIndentedLine($"var value = span[i];");
                    sb.AppendIndentedLine($"byte* dst = pBatch + used;");
                    sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    sb.AppendIndentedLine($"*(ulong*)(dst + {tagLength}) = *(ulong*)&value;");
                    sb.AppendIndentedLine($"used += {itemSize};");
                    sb.EndBlock();
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"fixed (byte* pBatch = batch)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"foreach (var value in {objectName}.{protoMember.Name})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"if (256 - used < {itemSize})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                    sb.AppendIndentedLine($"used = 0;");
                    sb.EndBlock();
                    sb.AppendIndentedLine($"byte* dst = pBatch + used;");
                    sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    sb.AppendIndentedLine($"*(ulong*)(dst + {tagLength}) = *(ulong*)&value;");
                    sb.AppendIndentedLine($"used += {itemSize};");
                    sb.EndBlock();
                    sb.EndBlock();
                }
                sb.AppendIndentedLine($"if (used > 0)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteBytes(batch.Slice(0, used));");
                sb.EndBlock();
                sb.EndBlock();
            }
            else
            {
                sb.AppendIndentedLine($"foreach(var item in {objectName}.{protoMember.Name})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, {GetWireTypeForElement(elementTypeName, protoMember.DataFormat)});");
                sb.AppendIndentedLine($"{elementWriteMethod.Replace("item", "item")};");
                sb.EndBlock();
            }
        }
        
        sb.EndBlock();
    }

    /// <summary>
    /// Writes the size calculation logic for byte collection types
    /// </summary>
    private static void WriteByteCollectionProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
        
        if (protoMember.CollectionKind == Core.CollectionKind.Array)
        {
            // Already byte[], calculate directly
            sb.AppendIndentedLine($"calculator.WriteBytes(obj.{protoMember.Name});");
        }
        else
        {
            // Collection type, calculate count and add raw bytes
            var countProperty = GetCollectionCountExpression(protoMember);
            sb.AppendIndentedLine($"var count = obj.{protoMember.Name}.{countProperty};");
            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)count);");
            sb.AppendIndentedLine($"calculator.AddByteLength(count);");
        }
        
        sb.EndBlock();
    }

    /// <summary>
    /// Writes the size calculation logic for primitive collection types
    /// </summary>
    private static void WritePrimitiveCollectionProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (protoMember.IsPacked)
        {
            // Packed size calculation - iterate directly over collection
            sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
            sb.AppendIndentedLine($"var tempCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementSizeMethod = GetPrimitiveElementSizeMethod(elementTypeName, protoMember.DataFormat);
            sb.AppendIndentedLine($"foreach(var item in obj.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementSizeMethod.Replace("calculator.", "tempCalculator.")};");
            sb.EndBlock();
            sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempCalculator.Length);");
            sb.AppendIndentedLine($"calculator.AddByteLength(tempCalculator.Length);");
        }
        else
        {
            // Non-packed size calculation
            var elementSizeMethod = GetPrimitiveElementSizeMethod(elementTypeName, protoMember.DataFormat);
            sb.AppendIndentedLine($"foreach(var item in obj.{protoMember.Name})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, {GetWireTypeForElement(elementTypeName, protoMember.DataFormat)});");
            sb.AppendIndentedLine($"{elementSizeMethod};");
            sb.EndBlock();
        }
        
        sb.EndBlock();
    }

    private static string GetPrimitiveArrayWriteMethod(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WritePackedFixedSizeIntArray(tempArray)",
                DataFormat.ZigZag => "foreach(var item in tempArray) writer.WriteZigZag32(item)",
                _ => "foreach(var item in tempArray) writer.WriteVarint32(item)"
            },
            "float" or "System.Single" => "foreach(var item in tempArray) writer.WriteFloat(item)",
            "double" or "System.Double" => "foreach(var item in tempArray) writer.WriteDouble(item)",
            "bool" or "System.Boolean" => "foreach(var item in tempArray) writer.WriteBool(item)",
            "byte" or "System.Byte" => "foreach(var item in tempArray) writer.WriteByte(item)",
            "sbyte" or "System.SByte" => dataFormat switch
            {
                DataFormat.ZigZag => "foreach(var item in tempArray) writer.WriteSByte(item, true)",
                _ => "foreach(var item in tempArray) writer.WriteSByte(item)"
            },
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) writer.WriteFixedInt32(item)",
                DataFormat.ZigZag => "foreach(var item in tempArray) writer.WriteInt16(item, true)",
                _ => "foreach(var item in tempArray) writer.WriteInt16(item)"
            },
            "ushort" or "System.UInt16" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) writer.WriteFixedUInt32(item)",
                _ => "foreach(var item in tempArray) writer.WriteUInt16(item)"
            },
            "uint" or "System.UInt32" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) writer.WriteFixedUInt32(item)",
                _ => "foreach(var item in tempArray) writer.WriteUInt32(item)"
            },
            "long" or "System.Int64" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) writer.WriteFixedInt64(item)",
                DataFormat.ZigZag => "foreach(var item in tempArray) writer.WriteZigZagVarInt64(item)",
                _ => "foreach(var item in tempArray) writer.WriteVarint64(item)"
            },
            "ulong" or "System.UInt64" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) writer.WriteFixedUInt64(item)",
                _ => "foreach(var item in tempArray) writer.WriteUInt64(item)"
            },
            _ => $"foreach(var item in tempArray) writer.Write{elementTypeName}(item)"
        };
    }

    private static string GetPrimitiveElementWriteMethod(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedSizeInt32(item)",
                DataFormat.ZigZag => "writer.WriteZigZag32(item)",
                _ => "writer.WriteVarint32(item)"
            },
            "float" or "System.Single" => "writer.WriteFloat(item)",
            "double" or "System.Double" => "writer.WriteDouble(item)",
            "bool" or "System.Boolean" => "writer.WriteBool(item)",
            "byte" or "System.Byte" => "writer.WriteByte(item)",
            "sbyte" or "System.SByte" => dataFormat switch
            {
                DataFormat.ZigZag => "writer.WriteSByte(item, true)",
                _ => "writer.WriteSByte(item)"
            },
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedInt32(item)",
                DataFormat.ZigZag => "writer.WriteInt16(item, true)",
                _ => "writer.WriteInt16(item)"
            },
            "ushort" or "System.UInt16" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedUInt32(item)",
                _ => "writer.WriteUInt16(item)"
            },
            "uint" or "System.UInt32" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedUInt32(item)",
                _ => "writer.WriteUInt32(item)"
            },
            "long" or "System.Int64" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedInt64(item)",
                DataFormat.ZigZag => "writer.WriteZigZagVarInt64(item)",
                _ => "writer.WriteVarint64(item)"
            },
            "ulong" or "System.UInt64" => dataFormat switch
            {
                DataFormat.FixedSize => "writer.WriteFixedUInt64(item)",
                _ => "writer.WriteUInt64(item)"
            },
            _ => $"writer.Write{elementTypeName}(item)"
        };
    }

    private static string GetWireTypeForElement(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed32b" : "WireType.VarInt",
            "long" or "System.Int64" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed64b" : "WireType.VarInt",
            "float" or "System.Single" => "WireType.Fixed32b",
            "double" or "System.Double" => "WireType.Fixed64b",
            "bool" or "System.Boolean" => "WireType.VarInt",
            _ => "WireType.VarInt"
        };
    }

    private static string GetPrimitiveArraySizeMethod(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WritePackedFixedSizeIntArray(tempArray)",
                DataFormat.ZigZag => "foreach(var item in tempArray) calculator.WriteZigZag32(item)",
                _ => "foreach(var item in tempArray) calculator.WriteVarint32(item)"
            },
            "float" or "System.Single" => "foreach(var item in tempArray) calculator.WriteFloat(item)",
            "double" or "System.Double" => "foreach(var item in tempArray) calculator.WriteDouble(item)",
            "bool" or "System.Boolean" => "foreach(var item in tempArray) calculator.WriteBool(item)",
            "byte" or "System.Byte" => "foreach(var item in tempArray) calculator.WriteByte(item)",
            "sbyte" or "System.SByte" => dataFormat switch
            {
                DataFormat.ZigZag => "foreach(var item in tempArray) calculator.WriteSByte(item, true)",
                _ => "foreach(var item in tempArray) calculator.WriteSByte(item)"
            },
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) calculator.WriteFixedInt32(item)",
                DataFormat.ZigZag => "foreach(var item in tempArray) calculator.WriteInt16(item, true)",
                _ => "foreach(var item in tempArray) calculator.WriteInt16(item)"
            },
            "ushort" or "System.UInt16" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) calculator.WriteFixedUInt32(item)",
                _ => "foreach(var item in tempArray) calculator.WriteUInt16(item)"
            },
            "uint" or "System.UInt32" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) calculator.WriteFixedUInt32(item)",
                _ => "foreach(var item in tempArray) calculator.WriteUInt32(item)"
            },
            "long" or "System.Int64" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) calculator.WriteFixedInt64(item)",
                DataFormat.ZigZag => "foreach(var item in tempArray) calculator.WriteZigZagVarInt64(item)",
                _ => "foreach(var item in tempArray) calculator.WriteVarint64(item)"
            },
            "ulong" or "System.UInt64" => dataFormat switch
            {
                DataFormat.FixedSize => "foreach(var item in tempArray) calculator.WriteFixedUInt64(item)",
                _ => "foreach(var item in tempArray) calculator.WriteUInt64(item)"
            },
            _ => $"foreach(var item in tempArray) calculator.Write{elementTypeName}(item)"
        };
    }

    private static string GetPrimitiveElementSizeMethod(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedSizeInt32(item)",
                DataFormat.ZigZag => "calculator.WriteZigZag32(item)",
                _ => "calculator.WriteVarint32(item)"
            },
            "float" or "System.Single" => "calculator.WriteFloat(item)",
            "double" or "System.Double" => "calculator.WriteDouble(item)",
            "bool" or "System.Boolean" => "calculator.WriteBool(item)",
            "byte" or "System.Byte" => "calculator.WriteByte(item)",
            "sbyte" or "System.SByte" => dataFormat switch
            {
                DataFormat.ZigZag => "calculator.WriteSByte(item, true)",
                _ => "calculator.WriteSByte(item)"
            },
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedInt32(item)",
                DataFormat.ZigZag => "calculator.WriteInt16(item, true)",
                _ => "calculator.WriteInt16(item)"
            },
            "ushort" or "System.UInt16" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedUInt32(item)",
                _ => "calculator.WriteUInt16(item)"
            },
            "uint" or "System.UInt32" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedUInt32(item)",
                _ => "calculator.WriteUInt32(item)"
            },
            "long" or "System.Int64" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedInt64(item)",
                DataFormat.ZigZag => "calculator.WriteZigZagVarInt64(item)",
                _ => "calculator.WriteVarint64(item)"
            },
            "ulong" or "System.UInt64" => dataFormat switch
            {
                DataFormat.FixedSize => "calculator.WriteFixedUInt64(item)",
                _ => "calculator.WriteUInt64(item)"
            },
            _ => $"calculator.Write{elementTypeName}(item)"
        };
    }
    
    /// <summary>
    /// Returns the appropriate property/method to get collection count
    /// </summary>
    private static string GetCollectionCountExpression(ProtoMemberAttribute protoMember)
    {
        return protoMember.CollectionKind switch
        {
            Core.CollectionKind.Array => "Length",
            Core.CollectionKind.InterfaceCollection when protoMember.Type.Contains("IEnumerable<") && !protoMember.Type.Contains("ICollection<") && !protoMember.Type.Contains("IList<") => "Count()",
            _ => "Count"
        };
    }
    
    /// <summary>
    /// Gets the SpanReader method for reading individual primitive elements
    /// </summary>
    private static string GetPrimitiveElementReadMethod(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat switch
            {
                DataFormat.FixedSize => "reader.ReadFixedInt32()",
                DataFormat.ZigZag => "reader.ReadZigZagVarInt32()",
                _ => "reader.ReadVarInt32()"
            },
            "long" or "System.Int64" => dataFormat switch
            {
                DataFormat.FixedSize => "reader.ReadFixedInt64()",
                DataFormat.ZigZag => "reader.ReadZigZagVarInt64()",
                _ => "reader.ReadVarInt64()"
            },
            "float" or "System.Single" => "reader.ReadFloat(wireType1)",
            "double" or "System.Double" => "reader.ReadDouble(wireType1)",
            "bool" or "System.Boolean" => "reader.ReadBool(wireType1)",
            "byte" or "System.Byte" => "reader.ReadByte(wireType1)",
            "sbyte" or "System.SByte" => dataFormat == DataFormat.ZigZag 
                ? "reader.ReadSByte(wireType1, true)" 
                : "reader.ReadSByte(wireType1)",
            "short" or "System.Int16" => dataFormat switch
            {
                DataFormat.FixedSize => "reader.ReadFixedInt16()",
                DataFormat.ZigZag => "reader.ReadInt16(wireType1, true)",
                _ => "reader.ReadInt16(wireType1)"
            },
            "ushort" or "System.UInt16" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadFixedUInt16()"
                : "reader.ReadUInt16(wireType1)",
            "uint" or "System.UInt32" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadFixedUInt32()"
                : "reader.ReadUInt32(wireType1)",
            "ulong" or "System.UInt64" => dataFormat == DataFormat.FixedSize
                ? "reader.ReadFixedUInt64()"
                : "reader.ReadUInt64(wireType1)",
            _ => throw new InvalidOperationException($"Unsupported primitive type: {elementTypeName}")
        };
    }

    /// <summary>
    /// Gets the expected wire type for a primitive element
    /// </summary>
    private static string GetExpectedWireType(string elementTypeName, DataFormat dataFormat)
    {
        return elementTypeName switch
        {
            "int" or "System.Int32" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed32b" : "WireType.VarInt",
            "long" or "System.Int64" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed64b" : "WireType.VarInt",
            "float" or "System.Single" => "WireType.Fixed32b",
            "double" or "System.Double" => "WireType.Fixed64b",
            "bool" or "System.Boolean" => "WireType.VarInt",
            "byte" or "System.Byte" => "WireType.VarInt",
            "sbyte" or "System.SByte" => "WireType.VarInt",
            "short" or "System.Int16" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed32b" : "WireType.VarInt",
            "ushort" or "System.UInt16" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed32b" : "WireType.VarInt",
            "uint" or "System.UInt32" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed32b" : "WireType.VarInt",
            "ulong" or "System.UInt64" => dataFormat == DataFormat.FixedSize ? "WireType.Fixed64b" : "WireType.VarInt",
            _ => "WireType.VarInt"
        };
    }
    
    #endregion
}