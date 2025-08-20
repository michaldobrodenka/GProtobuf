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
            sb.AppendIndentedLine($"public static void Write{GetClassNameFromFullName(obj.FullName)}(global::GProtobuf.Core.StreamWriter writer, global::{obj.FullName} obj)");
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
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref tempCalculatorC, obj);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.WriteVarint32((int)tempCalculatorC.Length);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.WriteRawBytes(new byte[tempCalculatorC.Length]);");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classB)}ContentSize(ref calculator{protoIncludeAtoB.FieldId}, obj);");
                        sb.AppendIndentedLine($"writer.WriteVarint32((int)calculator{protoIncludeAtoB.FieldId}.Length);");
                        
                        // Write the actual B wrapper content inline (Tag10 + C fields + B fields)
                        sb.AppendIndentedLine($"writer.WriteTag({protoIncludeBtoC.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"var calculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref calculatorC, obj);");
                        sb.AppendIndentedLine($"writer.WriteVarint32((int)calculatorC.Length);");
                        
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
                        sb.AppendIndentedLine($"writer.WriteVarint32(0); // No fields inside final ProtoInclude");
                    }
                    
                    // Write A fields
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
                    sb.AppendIndentedLine("switch (obj)");
                    sb.StartNewBlock();
                    foreach (var include in obj.ProtoIncludes)
                    {
                        var className = GetClassNameFromFullName(include.Type);
                        sb.AppendIndentedLine($"case global::{include.Type} obj1:");
                        sb.IncreaseIndent();
                        sb.AppendIndentedLine($"var calculator{include.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{className}Size(ref calculator{include.FieldId}, obj1);");
                        sb.AppendIndentedLine($"writer.WriteTag({include.FieldId}, WireType.Len);");
                        sb.AppendIndentedLine($"writer.WriteVarint32((int)calculator{include.FieldId}.Length);");
                        sb.AppendIndentedLine($"Write{className}(writer, obj1);");
                        sb.AppendIndentedLine("break;");
                        sb.DecreaseIndent();
                    }
                    sb.EndBlock();
                }
                
                // Write own fields
                if (obj.ProtoMembers != null)
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
                        sb.AppendIndentedLine($"tempCalc{protoIncludeAtoB.FieldId}.WriteVarint32((uint)tempCalcC.Length);");
                        sb.AppendIndentedLine($"tempCalc{protoIncludeAtoB.FieldId}.WriteRawBytes(new byte[tempCalcC.Length]);");
                        
                        sb.AppendIndentedLine($"Calculate{GetClassNameFromFullName(classB)}ContentSize(ref tempCalc{protoIncludeAtoB.FieldId}, obj);");
                        sb.AppendIndentedLine($"calculator.WriteVarint32((uint)tempCalc{protoIncludeAtoB.FieldId}.Length);");
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
                        sb.AppendIndentedLine($"calculator.WriteVarint32(0); // No fields inside final ProtoInclude");
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
                        sb.AppendIndentedLine($"calculator.WriteVarint32((uint)contentLength{include.FieldId});");
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
        switch(GetClassNameFromFullName(protoMember.Type))
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

            case "Double":
            case "double":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadDouble(wireType);");
                break;

            case "Single":
            case "single":
            case "float":
                sb.AppendIndentedLine($"result.{protoMember.Name} = (float)reader.ReadDouble(wireType);");
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadString(wireType);");
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
                            int32Reader = String.Format($"reader.ReadFixedInt32();");
                            break;

                        case DataFormat.ZigZag:
                            int32Reader = String.Format($"reader.ReadZigZagVarInt32();");
                            break;

                        default:
                            int32Reader = String.Format($"reader.ReadVarInt32();");
                            break;
                    }

                    sb.AppendIndentedLine($"List<int> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendNewLine();
                    sb.AppendIndentedLine($"while (!reader.IsEnd)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var number = {int32Reader}");
                    sb.AppendIndentedLine($"var p = reader.Position;");
                    sb.AppendIndentedLine($"resultList.Add(number);");
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
        var typeName = GetClassNameFromFullName(protoMember.Type);

        switch (typeName)
        {
            case "System.Int32":
            case "Int32":
            case "int":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                sb.StartNewBlock();
                switch (protoMember.DataFormat)
                {
                    case DataFormat.FixedSize:
                        sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                        sb.AppendIndentedLine($"writer.WriteFixedSizeInt32(obj.{protoMember.Name});");
                        break;
                    case DataFormat.ZigZag:
                        sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"writer.WriteZigZag32(obj.{protoMember.Name});");
                        break;
                    default:
                        sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"writer.WriteVarint32(obj.{protoMember.Name});");
                        break;
                }
                sb.EndBlock();
                break;

            case "Double":
            case "double":
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                sb.AppendIndentedLine($"writer.WriteDouble(obj.{protoMember.Name});");
                break;

            case "Single":
            case "single":
            case "float":
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                sb.AppendIndentedLine($"writer.WriteFloat(obj.{protoMember.Name});");
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarint32((uint)Encoding.UTF8.GetByteCount(obj.{protoMember.Name}));");
                sb.AppendIndentedLine($"writer.WriteString(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarint32(obj.{protoMember.Name}.Length);");
                sb.AppendIndentedLine($"writer.Stream.Write(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "System.Int32[]":
            case "Int32[]":
            case "int[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"writer.WriteVarint32(obj.{protoMember.Name}.Length << 2);");
                            sb.AppendIndentedLine($"writer.WritePackedFixedSizeIntArray(obj.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize(obj.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarint32(packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) writer.WriteZigZag32(v);");
                            break;
                        default:
                            sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize(obj.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarint32(packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) writer.WriteVarint32(v);");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteFixedSizeInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteZigZag32(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ writer.WriteVarint32(tagAndWire); writer.WriteVarint32(v); }}");
                            break;
                    }
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var calculator{protoMember.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(protoMember.Type)}Size(ref calculator{protoMember.FieldId}, obj.{protoMember.Name});");
                sb.AppendIndentedLine($"writer.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"writer.WriteVarint32((int)calculator{protoMember.FieldId}.Length);");
                sb.AppendIndentedLine($"StreamWriters.Write{GetClassNameFromFullName(protoMember.Type)}(writer, obj.{protoMember.Name});");
                sb.EndBlock();
                break;
        }

        sb.AppendNewLine();
    }

    private static void WriteProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var typeName = GetClassNameFromFullName(protoMember.Type);

        switch (typeName)
        {
            case "System.Int32":
            case "Int32":
            case "int":
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
                break;

            case "Double":
            case "double":
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed64b);");
                sb.AppendIndentedLine($"calculator.WriteDouble(obj.{protoMember.Name});");
                break;

            case "Single":
            case "single":
            case "float":
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Fixed32b);");
                sb.AppendIndentedLine($"calculator.WriteFloat(obj.{protoMember.Name});");
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

            default:
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"calculator.WriteTag({protoMember.FieldId}, WireType.Len);");
                sb.AppendIndentedLine($"var lengthBefore{protoMember.FieldId} = calculator.Length;");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(protoMember.Type)}Size(ref calculator, obj.{protoMember.Name});");
                sb.AppendIndentedLine($"var contentLength{protoMember.FieldId} = calculator.Length - lengthBefore{protoMember.FieldId};");
                sb.AppendIndentedLine($"calculator.WriteVarint32((uint)contentLength{protoMember.FieldId});");
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
}