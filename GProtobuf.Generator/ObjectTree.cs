using System;
using System.Collections.Generic;
using System.Linq;
using GProtobuf.Core;

namespace GProtobuf.Generator;

// Local WireType enum matching GProtobuf.Core.WireType
internal enum WireType
{
    VarInt = 0,
    Fixed64b = 1,
    Len = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32b = 5
}

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

    // Helper method to precompute tag bytes at code generation time
    private static (string bytesString, int byteCount) PrecomputeTagBytes(int fieldId, WireType wireType)
    {
        uint tag = (uint)((fieldId << 3) | (int)wireType);
        var tagBytes = new List<byte>();
        
        // Encode as varint
        while (tag > 0x7F)
        {
            tagBytes.Add((byte)((tag & 0x7F) | 0x80));
            tag >>= 7;
        }
        tagBytes.Add((byte)tag);
        
        // Format as C# byte array literal
        string bytesString = string.Join(", ", tagBytes.Select(b => $"0x{b:X2}"));
        return (bytesString, tagBytes.Count);
    }
    
    // Helper to generate optimized tag write code
    private static void WritePrecomputedTag(StringBuilderWithIndent sb, int fieldId, WireType wireType)
    {
        var (tagBytes, tagLen) = PrecomputeTagBytes(fieldId, wireType);
        sb.AppendIndentedLine($"// Tag for field {fieldId}, {wireType}");
        if (tagLen == 1)
        {
            sb.AppendIndentedLine($"writer.WriteSingleByte({tagBytes});");
        }
        else
        {
            sb.AppendIndentedLine($"writer.WriteBytes(stackalloc byte[] {{ {tagBytes} }});");
        }
    }
    
    // Helper to generate optimized tag calculation for size calculator
    private static void WritePrecomputedTagForCalculator(StringBuilderWithIndent sb, int fieldId, WireType wireType)
    {
        var (tagBytes, tagLen) = PrecomputeTagBytes(fieldId, wireType);
        sb.AppendIndentedLine($"// Tag for field {fieldId}, {wireType}");
        sb.AppendIndentedLine($"calculator.AddByteLength({tagLen}); // Precomputed tag bytes: {tagBytes}");
    }
    
    // Helper to generate optimized tag calculation for named size calculator (e.g., entryCalculator)
    private static void WritePrecomputedTagForCalculatorWithName(StringBuilderWithIndent sb, string calculatorName, int fieldId, WireType wireType)
    {
        var (tagBytes, tagLen) = PrecomputeTagBytes(fieldId, wireType);
        sb.AppendIndentedLine($"// Tag for field {fieldId}, {wireType}");
        sb.AppendIndentedLine($"{calculatorName}.AddByteLength({tagLen}); // Precomputed tag bytes: {tagBytes}");
    }

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

    //// This method is no longer used - fields are written directly in the Write methods
    //// Kept for potential future use
    //private void WriteAllMembersForSerialization(StringBuilderWithIndent sb, TypeDefinition obj)
    //{
    //    // Write own members first
    //    if (obj.ProtoMembers != null)
    //    {
    //        foreach (var protoMember in obj.ProtoMembers)
    //        {
    //            WriteProtoMemberSerializer(sb, protoMember);
    //        }
    //    }
        
    //    // Write inherited members from all ancestors
    //    WriteInheritedMembersForSerialization(sb, obj.FullName);
    //}

    //private void WriteInheritedMembersForSerialization(StringBuilderWithIndent sb, string typeName)
    //{
    //    if (baseClassesForTypes.TryGetValue(typeName, out var baseClassName))
    //    {
    //        var baseType = FindTypeByFullName(baseClassName);
    //        if (baseType != null)
    //        {
    //            // Write ancestor's inherited members first (recursive)
    //            WriteInheritedMembersForSerialization(sb, baseType.FullName);
                
    //            // Then write this ancestor's own members
    //            if (baseType.ProtoMembers != null)
    //            {
    //                foreach (var baseMember in baseType.ProtoMembers)
    //                {
    //                    WriteProtoMemberSerializer(sb, baseMember);
    //                }
    //            }
    //        }
    //    }
    //}

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
        sb.AppendIndentedLine("using GProtobuf.Core;\r\nusing System;\r\nusing System.Collections.Generic;\r\nusing System.IO;\r\nusing System.Buffers;\r\nusing System.Text;\r\n");

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
            sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.StreamWriter(stream, stackalloc byte[256]);");
            sb.AppendIndentedLine($"StreamWriters.Write{GetClassNameFromFullName(obj.FullName)}(ref writer, obj);");
            sb.AppendIndentedLine("writer.Flush();");
            sb.EndBlock();
            sb.AppendNewLine();
        }

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Serialize{GetClassNameFromFullName(obj.FullName)}(IBufferWriter<byte> buffer, global::{obj.FullName} obj)");
            sb.StartNewBlock();
            sb.AppendIndentedLine("var writer = new global::GProtobuf.Core.BufferWriter(buffer);");
            sb.AppendIndentedLine($"BufferWriters.Write{GetClassNameFromFullName(obj.FullName)}(ref writer, obj);");
            sb.AppendIndentedLine("writer.Flush();");
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

        GenerateWriters(sb, objects, WriteTarget.Stream);
        GenerateWriters(sb, objects, WriteTarget.IBufferWriter);

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
                        WritePrecomputedTagForCalculator(sb, protoIncludeAtoB.FieldId, WireType.Len);

                        // Calculate B wrapper content inline (Tag10 + C size + C content + B content)
                        sb.AppendIndentedLine($"var tempCalc{protoIncludeAtoB.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        WritePrecomputedTagForCalculatorWithName(sb, $"tempCalc{protoIncludeAtoB.FieldId}", protoIncludeBtoC.FieldId, WireType.Len);

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
                        WritePrecomputedTagForCalculator(sb, protoInclude.FieldId, WireType.Len);
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
                            sb.AppendIndentedLine($"calculator.WriteVarInt32(0); // No fields inside final ProtoInclude");
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
                        WritePrecomputedTagForCalculator(sb, include.FieldId, WireType.Len);
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


        sb.EndBlock();
        sb.EndBlock(); // Close namespace
    }

    private enum WriteTarget
    { 
        Stream,
        IBufferWriter
    }

    private void GenerateWriters(StringBuilderWithIndent sb, List<TypeDefinition> objects, WriteTarget writeTarget)
    {
        string shortName = null;

        switch (writeTarget)
        {
            case WriteTarget.Stream:
                shortName = "Stream";
                break;
            case WriteTarget.IBufferWriter:
                shortName = "Buffer";
                break;
        }


        sb.AppendIndentedLine($"public static class {shortName}Writers");
        sb.StartNewBlock();

        foreach (var obj in objects)
        {
            sb.AppendIndentedLine($"public static void Write{GetClassNameFromFullName(obj.FullName)}(ref global::GProtobuf.Core.{shortName}Writer writer, global::{obj.FullName} instance)");
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
                        WritePrecomputedTag(sb, protoIncludeAtoB.FieldId, WireType.Len);

                        // Calculate B wrapper content size inline (same as CalculateCSize)
                        sb.AppendIndentedLine($"var calculator{protoIncludeAtoB.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                        WritePrecomputedTagForCalculatorWithName(sb, $"calculator{protoIncludeAtoB.FieldId}", protoIncludeBtoC.FieldId, WireType.Len);
                        sb.AppendIndentedLine($"var tempCalculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref tempCalculatorC, instance);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.WriteVarUInt32((uint)tempCalculatorC.Length);");
                        sb.AppendIndentedLine($"calculator{protoIncludeAtoB.FieldId}.AddByteLength(tempCalculatorC.Length);");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classB)}ContentSize(ref calculator{protoIncludeAtoB.FieldId}, instance);");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{protoIncludeAtoB.FieldId}.Length);");

                        // Write the actual B wrapper content inline (Tag10 + C fields + B fields)
                        WritePrecomputedTag(sb, protoIncludeBtoC.FieldId, WireType.Len);
                        sb.AppendIndentedLine($"var calculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(classC)}ContentSize(ref calculatorC, instance);");
                        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculatorC.Length);");

                        // Write C fields  
                        var typeC = FindTypeByFullName(classC);
                        if (typeC?.ProtoMembers != null)
                        {
                            foreach (var member in typeC.ProtoMembers)
                            {
                                WriteProtoMemberSerializer(sb, member, writeTarget, shortName);
                            }
                        }

                        // Write B fields
                        if (typeB?.ProtoMembers != null)
                        {
                            foreach (var member in typeB.ProtoMembers)
                            {
                                WriteProtoMemberSerializer(sb, member, writeTarget, shortName);
                            }
                        }
                    }

                    // Write A fields (outside the wrapper)
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member, writeTarget, shortName);
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
                        WritePrecomputedTag(sb, protoInclude.FieldId, WireType.Len);
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
                            sb.AppendIndentedLine($"writer.WriteVarInt32(0); // No fields inside final ProtoInclude");
                        }
                    }

                    // Write own fields (B fields) into the wrapper
                    if (obj.ProtoMembers != null)
                    {
                        foreach (var member in obj.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member, writeTarget, shortName);
                        }
                    }

                    // Write A fields (inherited) outside the wrapper
                    if (typeA?.ProtoMembers != null)
                    {
                        foreach (var member in typeA.ProtoMembers)
                        {
                            WriteProtoMemberSerializer(sb, member, writeTarget, shortName);
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
                                WritePrecomputedTagForCalculatorWithName(sb, $"calculator{include.FieldId}_{className}", bInclude.FieldId, WireType.Len);
                                sb.AppendIndentedLine($"var tempCalcC{bInclude.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                                sb.AppendIndentedLine($"SizeCalculators.Calculate{cClassName}ContentSize(ref tempCalcC{bInclude.FieldId}, objC{bInclude.FieldId});");
                                sb.AppendIndentedLine($"calculator{include.FieldId}_{className}.WriteVarUInt32((uint)tempCalcC{bInclude.FieldId}.Length);");
                                sb.AppendIndentedLine($"calculator{include.FieldId}_{className}.AddByteLength(tempCalcC{bInclude.FieldId}.Length);");
                                sb.EndBlock();
                            }
                        }

                        // Calculate B's own fields and A's inherited fields
                        sb.AppendIndentedLine($"SizeCalculators.Calculate{className}ContentSize(ref calculator{include.FieldId}_{className}, obj1);");

                        WritePrecomputedTag(sb, include.FieldId, WireType.Len);
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

                                WritePrecomputedTag(sb, bInclude.FieldId, WireType.Len);
                                sb.AppendIndentedLine($"var calculatorC = new global::GProtobuf.Core.WriteSizeCalculator();");
                                sb.AppendIndentedLine($"SizeCalculators.Calculate{cClassName}ContentSize(ref calculatorC, objC);");
                                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculatorC.Length);");

                                // Write C fields using objC (not obj)
                                var typeC = FindTypeByFullName(bInclude.Type);
                                if (typeC?.ProtoMembers != null)
                                {
                                    foreach (var cMember in typeC.ProtoMembers)
                                    {
                                        WriteProtoMemberSerializerWithObject(sb, cMember, "objC", writeTarget, shortName);
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
                                WriteProtoMemberSerializerWithObject(sb, bMember, "obj1", writeTarget, shortName);
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
                        WriteProtoMemberSerializer(sb, protoMember, writeTarget, shortName);
                    }
                }
            }

            sb.EndBlock();
            sb.AppendNewLine();
        }


        sb.EndBlock();

        sb.AppendNewLine();
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
        if (protoMember.IsMap)
        {
            WriteMapProtoMember(sb, protoMember);
            return;
        }
        else if (IsByteCollectionType(protoMember))
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
        else if (protoMember.IsEnum)
        {
            // Handle enum types - serialize as varint32 (cast to int)
            sb.AppendIndentedLine($"result.{protoMember.Name} = ({typeName})reader.ReadVarInt32();");
            sb.AppendIndentedLine($"continue;");
            sb.EndBlock();
            sb.AppendNewLine();
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

            case "TimeSpan":
            case "System.TimeSpan":
                sb.AppendIndentedLine($"result.{protoMember.Name} = new TimeSpan((long)reader.ReadUInt64(wireType));");
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

    private static void WriteProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, WriteTarget writeTarget, string writeTargetShortName)
    {
        WriteProtoMemberSerializerWithObject(sb, protoMember, "instance", writeTarget, writeTargetShortName);
    }
    
    private static void WriteProtoMemberSerializerWithObject(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName, WriteTarget writeTarget, string writeTargetShortName)
    {
        // Check if it's a nullable type
        bool isNullable = protoMember.IsNullable;
        var typeName = GetClassNameFromFullName(protoMember.Type);

        // Check if it's a map type first
        if (protoMember.IsMap)
        {
            WriteMapProtoMemberSerializer(sb, protoMember, objectName, writeTarget, writeTargetShortName);
            return;
        }
        // Check collection types in priority order
        else if (IsByteCollectionType(protoMember))
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
            WriteArrayProtoMemberSerializer(sb, protoMember, objectName, writeTarget, writeTargetShortName);
            return;
        }
        else if (protoMember.IsEnum)
        {
            // Handle enum types - serialize as varint32 (cast to int)
            if (isNullable)
            {
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                sb.StartNewBlock();
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"writer.WriteVarInt32((int){objectName}.{protoMember.Name}.Value);");
                sb.EndBlock();
            }
            else
            {
                sb.AppendIndentedLine($"if ((int){objectName}.{protoMember.Name} != 0)");
                sb.StartNewBlock();
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"writer.WriteVarInt32((int){objectName}.{protoMember.Name});");
                sb.EndBlock();
            }
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
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed32b);
                            sb.AppendIndentedLine($"writer.WriteFixedSizeInt32({objectName}.{protoMember.Name}.Value);");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"writer.WriteZigZag32({objectName}.{protoMember.Name}.Value);");
                            break;
                        default:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"writer.WriteVarInt32({objectName}.{protoMember.Name}.Value);");
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
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed32b);
                            sb.AppendIndentedLine($"writer.WriteFixedSizeInt32({objectName}.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"writer.WriteZigZag32({objectName}.{protoMember.Name});");
                            break;
                        default:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"writer.WriteVarInt32({objectName}.{protoMember.Name});");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteBool({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteByte({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteUInt16({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteUInt32({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteUInt64({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed64b);
                    sb.AppendIndentedLine($"writer.WriteDouble({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed64b);
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed32b);
                    sb.AppendIndentedLine($"writer.WriteFloat({objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Fixed32b);
                    sb.AppendIndentedLine($"writer.WriteFloat({objectName}.{protoMember.Name});");
                }
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                //sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)Encoding.UTF8.GetByteCount({objectName}.{protoMember.Name}));");
                sb.AppendIndentedLine($"writer.WriteString({objectName}.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "Guid":
            case "System.Guid":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // BCL Guid format: 2 fixed64 fields = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Convert Guid to BCL format (2 fixed64 fields)");
                    sb.AppendIndentedLine($"var guidBytes = {objectName}.{protoMember.Name}.Value.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64(guidBytes, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64(guidBytes, 8);");
                    sb.AppendIndentedLine($"// Field 1: low (fixed64)");
                    WritePrecomputedTag(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine($"writer.WriteFixed64(low);");
                    sb.AppendIndentedLine($"// Field 2: high (fixed64)");
                    WritePrecomputedTag(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine($"writer.WriteFixed64(high);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != Guid.Empty)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // BCL Guid format: 2 fixed64 fields = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Convert Guid to BCL format (2 fixed64 fields)");
                    sb.AppendIndentedLine($"var guidBytes = {objectName}.{protoMember.Name}.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64(guidBytes, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64(guidBytes, 8);");
                    sb.AppendIndentedLine($"// Field 1: low (fixed64)");
                    WritePrecomputedTag(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine($"writer.WriteFixed64(low);");
                    sb.AppendIndentedLine($"// Field 2: high (fixed64)");
                    WritePrecomputedTag(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine($"writer.WriteFixed64(high);");
                    sb.EndBlock();
                }
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){objectName}.{protoMember.Name}.Length);");
                sb.AppendIndentedLine($"writer.Stream.Write({objectName}.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "TimeSpan":
            case "System.TimeSpan":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteUInt64((ulong){objectName}.{protoMember.Name}.Value.Ticks);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != TimeSpan.Zero)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteUInt64((ulong){objectName}.{protoMember.Name}.Ticks);");
                    sb.EndBlock();
                }
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
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length << 2));");
                            sb.AppendIndentedLine($"writer.WritePackedFixedSizeIntArray({objectName}.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) writer.WriteZigZag32(v);");
                            break;
                        default:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) writer.WriteVarInt32(v);");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedSizeInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteZigZag32(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteVarInt32(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 4));");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFloat(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFloat(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 8));");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteDouble(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteDouble(v); }}");
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
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.Length * 8));");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"var packedSize = Utils.GetZigZagPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                            sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSize({objectName}.{protoMember.Name});");
                            sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt64(v); }}");
                            break;
                    }
                }
                else
                {
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteVarInt64(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"var packedSize = Utils.GetBoolPackedCollectionSize({objectName}.{protoMember.Name});");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteBool(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteBool(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    
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
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); {writeMethod}; }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    
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
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteInt16(v, true); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteInt16(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    
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
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteUInt16(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    
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
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedUInt32(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteUInt32(v); }}");
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
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    
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
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteUInt64(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var calculator{protoMember.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(protoMember.Type)}Size(ref calculator{protoMember.FieldId}, {objectName}.{protoMember.Name});");
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{protoMember.FieldId}.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{GetClassNameFromFullName(protoMember.Type)}(ref writer, {objectName}.{protoMember.Name});");
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
        if (protoMember.IsMap)
        {
            WriteMapProtoMemberSizeCalculator(sb, protoMember);
            return;
        }
        else if (IsByteCollectionType(protoMember))
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
        else if (protoMember.IsEnum)
        {
            // Handle enum types - serialize as varint32 (cast to int)
            if (isNullable)
            {
                sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"calculator.WriteVarInt32((int)obj.{protoMember.Name}.Value);");
                sb.EndBlock();
            }
            else
            {
                sb.AppendIndentedLine($"if ((int)obj.{protoMember.Name} != 0)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                sb.AppendIndentedLine($"calculator.WriteVarInt32((int)obj.{protoMember.Name});");
                sb.EndBlock();
            }
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
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed32b);
                            sb.AppendIndentedLine($"calculator.WriteFixedSizeInt32(obj.{protoMember.Name}.Value);");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"calculator.WriteZigZag32(obj.{protoMember.Name}.Value);");
                            break;
                        default:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"calculator.WriteVarInt32(obj.{protoMember.Name}.Value);");
                            break;
                    }
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed32b);
                            sb.AppendIndentedLine($"calculator.WriteFixedSizeInt32(obj.{protoMember.Name});");
                            break;
                        case DataFormat.ZigZag:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"calculator.WriteZigZag32(obj.{protoMember.Name});");
                            break;
                        default:
                            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                            sb.AppendIndentedLine($"calculator.WriteVarInt32(obj.{protoMember.Name});");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteBool(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteByte(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteUInt16(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteUInt32(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteUInt64(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != 0)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed64b);
                    sb.AppendIndentedLine($"calculator.WriteDouble(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed64b);
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed32b);
                    sb.AppendIndentedLine($"calculator.WriteFloat(obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Fixed32b);
                    sb.AppendIndentedLine($"calculator.WriteFloat(obj.{protoMember.Name});");
                }
                break;

            case "String":
            case "System.String":
            case "string":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"calculator.WriteString(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "Guid":
            case "System.Guid":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32(18u); // BCL Guid: 2*(tag+fixed64) = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Field 1: low (tag + fixed64)");
                    WritePrecomputedTagForCalculator(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.AppendIndentedLine($"// Field 2: high (tag + fixed64)");
                    WritePrecomputedTagForCalculator(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != Guid.Empty)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32(18u); // BCL Guid: 2*(tag+fixed64) = 2*(1+8) = 18 bytes");
                    sb.AppendIndentedLine($"// Field 1: low (tag + fixed64)");
                    WritePrecomputedTagForCalculator(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.AppendIndentedLine($"// Field 2: high (tag + fixed64)");
                    WritePrecomputedTagForCalculator(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine($"calculator.WriteFixed64(0ul); // placeholder");
                    sb.EndBlock();
                }
                break;

            case "byte[]":
            case "Byte[]":
            case "System.Byte[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"calculator.WriteBytes(obj.{protoMember.Name});");
                sb.EndBlock();
                break;

            case "TimeSpan":
            case "System.TimeSpan":
                if (protoMember.IsNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteUInt64((ulong)obj.{protoMember.Name}.Value.Ticks);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != TimeSpan.Zero)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteUInt64((ulong)obj.{protoMember.Name}.Ticks);");
                    sb.EndBlock();
                }
                break;

            case "System.Int32[]":
            case "Int32[]":
            case "int[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedSizeInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteZigZag32(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteVarInt32(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 4));");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteFloat(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed32b);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFloat(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)(obj.{protoMember.Name}.Length * 8));");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteDouble(v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.Fixed64b);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteDouble(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedInt64(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteZigZagVarInt64(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteVarInt64(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WritePackedBoolArray(obj.{protoMember.Name});");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteBool(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); {writeMethod}; }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedInt32(v); }}");
                            break;
                        case DataFormat.ZigZag:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteZigZagInt16(v); }}");
                            break;
                        default:
                            sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                            sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteInt16(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedUInt16(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteUInt16(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedUInt32((uint)v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteUInt32(v); }}");
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
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteFixedUInt64(v); }}");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                        sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteUInt64(v); }}");
                    }
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
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
        // Use ClassCollectionCollector for reference types (string, custom messages)
        sb.AppendIndentedLine($"using var resultCollector = new global::GProtobuf.Core.ClassCollectionCollector<{elementType}>();");
        sb.AppendIndentedLine($"var wireType1 = wireType;");
        sb.AppendIndentedLine($"var fieldId1 = fieldId;");
        
        // All length-delimited types (strings, messages) use wire type 2 (Len)
        sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Len)");
        sb.StartNewBlock();
        
        if (elementTypeName == "string" || elementTypeName == "System.String")
        {
            // String arrays - direct string reading
            sb.AppendIndentedLine($"resultCollector.Add(reader.ReadString(wireType));");
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
            sb.AppendIndentedLine($"resultCollector.Add(item);");
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
                sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToArray();");
                break;
            case CollectionKind.InterfaceCollection:
                // ICollection<T>, IList<T>, IEnumerable<T> → assign List<T>
                sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToList();");
                break;
            case CollectionKind.ConcreteCollection:
                // For concrete types like List<T>, HashSet<T> or custom collections
                var concreteType = protoMember.Type;
                if (concreteType.StartsWith("List<") || concreteType.StartsWith("System.Collections.Generic.List<") || concreteType == "System.Collections.Generic.List`1")
                {
                    // List<T> → assign directly
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToList();");
                }
                else if (concreteType.StartsWith("HashSet<") || concreteType.StartsWith("System.Collections.Generic.HashSet<"))
                {
                    // HashSet<T> → create HashSet from items
                    sb.AppendIndentedLine($"result.{protoMember.Name} = new global::System.Collections.Generic.HashSet<{elementType}>(resultCollector.ToArray());");
                }
                else
                {
                    // Custom collection type → create instance and add items via ICollection<T>
                    sb.AppendIndentedLine($"var customCollection = new {concreteType}();");
                    sb.AppendIndentedLine($"var iCollection = (global::System.Collections.Generic.ICollection<{elementType}>)customCollection;");
                    sb.AppendIndentedLine($"var items = resultCollector.ToArray();");
                    sb.AppendIndentedLine($"foreach (var item in items) iCollection.Add(item);");
                    sb.AppendIndentedLine($"result.{protoMember.Name} = customCollection;");
                }
                break;
        }
        
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }
    
    /// <summary>
    /// Writes the deserialization logic for map/dictionary types
    /// </summary>
    private static void WriteMapProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var keyType = protoMember.MapKeyType;
        var valueType = protoMember.MapValueType;
        var mapType = protoMember.Type;
        
        // Create dictionary to collect map entries
        // Use the actual type if it's a concrete dictionary type, otherwise use Dictionary<K,V>
        var dictType = GetDictionaryCreationType(mapType, keyType, valueType);
        sb.AppendIndentedLine($"var mapDict = new {dictType}();");
        sb.AppendIndentedLine($"var wireType1 = wireType;");
        sb.AppendIndentedLine($"var fieldId1 = fieldId;");
        
        // Loop through all map entries (wire type is always Len for map entries)
        sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Len)");
        sb.StartNewBlock();
        
        // Read the map entry message
        sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
        sb.AppendIndentedLine($"var entryReader = new SpanReader(reader.GetSlice(length));");
        
        // Initialize key/value variables
        sb.AppendIndentedLine($"{keyType} key = default({keyType});");
        sb.AppendIndentedLine($"{valueType} value = default({valueType});");
        
        // Read the map entry content (field 1 = key, field 2 = value)
        sb.AppendIndentedLine($"while (!entryReader.IsEnd)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"var (entryWireType, entryFieldId) = entryReader.ReadWireTypeAndFieldId();");
        
        // Read key (field 1)
        sb.AppendIndentedLine($"if (entryFieldId == 1)");
        sb.StartNewBlock();
        WriteMapFieldReader(sb, "key", keyType);
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        
        // Read value (field 2)
        sb.AppendIndentedLine($"if (entryFieldId == 2)");
        sb.StartNewBlock();
        WriteMapFieldReader(sb, "value", valueType);
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        
        // Skip unknown fields
        sb.AppendIndentedLine($"entryReader.SkipField(entryWireType);");
        sb.EndBlock();
        
        // Add to dictionary
        sb.AppendIndentedLine($"mapDict[key] = value;");
        
        // Standard map reading loop continuation logic
        sb.AppendIndentedLine($"if (reader.EndOfData) break;");
        sb.AppendIndentedLine($"var p = reader.Position;");
        sb.AppendIndentedLine($"(wireType1, fieldId1) = reader.ReadKey();");
        sb.AppendIndentedLine($"if (fieldId1 != fieldId)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"reader.Position = p; // rewind");
        sb.AppendIndentedLine($"break;");
        sb.EndBlock();
        sb.EndBlock();
        
        // Assign the dictionary to the property based on type
        if (mapType.Contains("KeyValuePair<"))
        {
            // For List<KeyValuePair<K,V>> or ICollection<KeyValuePair<K,V>>, convert dictionary to list
            sb.AppendIndentedLine($"result.{protoMember.Name} = mapDict.ToList();");
        }
        else
        {
            // For all dictionary types (Dictionary<K,V>, IDictionary<K,V>, DerivedDictionary, etc.)
            // mapDict is already created with the correct type, so direct assignment works
            sb.AppendIndentedLine($"result.{protoMember.Name} = mapDict;");
        }
        
        sb.AppendIndentedLine($"continue;");
        sb.EndBlock();
        sb.AppendNewLine();
    }
    
    /// <summary>
    /// Writes the reader logic for map key/value fields
    /// </summary>
    private static void WriteMapFieldReader(StringBuilderWithIndent sb, string varName, string fieldType)
    {
        var typeName = GetClassNameFromFullName(fieldType);
        
        switch (typeName)
        {
            case "string":
            case "System.String":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadString(entryWireType);");
                break;
            case "int":
            case "System.Int32":
            case "Int32":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadVarInt32();");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadVarInt64();");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadBool(entryWireType);");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadFloat(entryWireType);");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{varName} = entryReader.ReadDouble(entryWireType);");
                break;
            default:
                // For complex types, deserialize as message
                sb.AppendIndentedLine($"var fieldLength = entryReader.ReadVarInt32();");
                sb.AppendIndentedLine($"var fieldReader = new SpanReader(entryReader.GetSlice(fieldLength));");
                sb.AppendIndentedLine($"{varName} = global::{GetNamespaceFromType(fieldType)}.Serialization.SpanReaders.Read{typeName}(ref fieldReader);");
                break;
        }
    }
    
    /// <summary>
    /// Check if collection type should use for instead of foreach for better performance
    /// </summary>
    private static bool ShouldUseForLoop(ProtoMemberAttribute protoMember)
    {
        var collectionType = protoMember.Type;
        return collectionType.StartsWith("System.Collections.Generic.List<") ||
               collectionType.StartsWith("List<") ||
               collectionType.EndsWith("[]");
    }
    
    /// <summary>
    /// Writes optimized loop (for vs foreach) based on collection type
    /// </summary>
    private static void WriteOptimizedLoop(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName, System.Action<StringBuilderWithIndent> writeLoopBody)
    {
        if (ShouldUseForLoop(protoMember))
        {
            string lengthProperty = protoMember.Type.EndsWith("[]") ? "Length" : "Count";
            sb.AppendIndentedLine($"for (int i = 0; i < {objectName}.{protoMember.Name}.{lengthProperty}; i++)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"var item = {objectName}.{protoMember.Name}[i];");
            writeLoopBody(sb);
            sb.EndBlock();
        }
        else
        {
            sb.AppendIndentedLine($"foreach (var item in {objectName}.{protoMember.Name})");
            sb.StartNewBlock();
            writeLoopBody(sb);
            sb.EndBlock();
        }
    }

    /// <summary>
    /// Writes the serialization logic for array types (string[], Message[], etc.)
    /// </summary>
    private static void WriteArrayProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName, WriteTarget writeTarget, string writeTargetShortName)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        // Check if array is not null
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (elementTypeName == "string" || elementTypeName == "System.String")
        {
            // String arrays - each string gets its own tag + length + content
            WriteOptimizedLoop(sb, protoMember, objectName, (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                WritePrecomputedTag(loopSb, protoMember.FieldId, WireType.Len);
                //loopSb.AppendIndentedLine($"writer.WriteVarUInt32((uint)System.Text.Encoding.UTF8.GetByteCount(item));");
                loopSb.AppendIndentedLine($"writer.WriteString(item);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type string was null; this might be as contents in a list/array\");");
                loopSb.EndBlock();
            });
        }
        else if (IsPrimitiveArrayType(elementTypeName))
        {
            // This should NOT happen - primitive arrays should use specialized implementation
            sb.AppendIndentedLine($"throw new InvalidOperationException(\"Primitive array {elementTypeName}[] should use specialized implementation, not generic array logic\");");
        }
        else
        {
            // Custom message types - each message gets its own tag + length + serialized content
            WriteOptimizedLoop(sb, protoMember, objectName, (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                loopSb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(elementType)}Size(ref calculator, item);");
                WritePrecomputedTag(loopSb, protoMember.FieldId, WireType.Len);
                loopSb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator.Length);");
                loopSb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{GetClassNameFromFullName(elementType)}(ref writer, item);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {GetClassNameFromFullName(elementType)} was null; this might be as contents in a list/array\");");
                loopSb.EndBlock();
            });
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
            WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                WritePrecomputedTagForCalculator(loopSb, protoMember.FieldId, WireType.Len);
                loopSb.AppendIndentedLine($"calculator.WriteString(item);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type string was null; this might be as contents in a list/array\");");
                loopSb.EndBlock();
            });
        }
        else if (IsPrimitiveArrayType(elementTypeName))
        {
            // This should NOT happen - primitive arrays should use specialized implementation
            sb.AppendIndentedLine($"throw new InvalidOperationException(\"Primitive array {elementTypeName}[] should use specialized implementation, not generic array logic\");");
        }
        else
        {
            // Custom message types - each message gets tag + length + content
            WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                WritePrecomputedTagForCalculator(loopSb, protoMember.FieldId, WireType.Len);
                loopSb.AppendIndentedLine($"var itemCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                loopSb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(elementType)}Size(ref itemCalculator, item);");
                loopSb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)itemCalculator.Length);");
                loopSb.AppendIndentedLine($"calculator.AddByteLength(itemCalculator.Length);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {GetClassNameFromFullName(elementType)} was null; this might be as contents in a list/array\");");
                loopSb.EndBlock();
            });
        }
        
        sb.EndBlock();
    }

    /// <summary>
    /// Writes the size calculation logic for map/dictionary types
    /// </summary>
    private static void WriteMapProtoMemberSizeCalculator(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
    {
        var keyType = protoMember.MapKeyType;
        var valueType = protoMember.MapValueType;
        
        // Check if map is not null
        sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        // Iterate through each map entry
        sb.AppendIndentedLine($"foreach (var entry in obj.{protoMember.Name})");
        sb.StartNewBlock();
        
        // Calculate size for the map entry message (tag + length + entry content)
        WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
        sb.AppendIndentedLine($"var entryCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
        
        // Calculate key size (field 1)
        var keyWireTypeStr = GetWireTypeForMapField(keyType);
        var keyWireType = keyWireTypeStr switch
        {
            "WireType.VarInt" => WireType.VarInt,
            "WireType.Fixed32b" => WireType.Fixed32b,
            "WireType.Fixed64b" => WireType.Fixed64b,
            "WireType.Len" => WireType.Len,
            _ => WireType.VarInt
        };
        WritePrecomputedTagForCalculatorWithName(sb, "entryCalculator", 1, keyWireType);
        WriteMapFieldSizeCalculation(sb, "entry.Key", keyType, "entryCalculator");
        
        // Calculate value size (field 2)
        var valueWireTypeStr = GetWireTypeForMapField(valueType);
        var valueWireType = valueWireTypeStr switch
        {
            "WireType.VarInt" => WireType.VarInt,
            "WireType.Fixed32b" => WireType.Fixed32b,
            "WireType.Fixed64b" => WireType.Fixed64b,
            "WireType.Len" => WireType.Len,
            _ => WireType.VarInt
        };
        WritePrecomputedTagForCalculatorWithName(sb, "entryCalculator", 2, valueWireType);
        WriteMapFieldSizeCalculation(sb, "entry.Value", valueType, "entryCalculator");
        
        // Write entry length and add to total size
        sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)entryCalculator.Length);");
        sb.AppendIndentedLine($"calculator.AddByteLength(entryCalculator.Length);");
        
        sb.EndBlock();
        sb.EndBlock();
    }
    
    /// <summary>
    /// Gets the wire type for a map field based on the field type
    /// </summary>
    private static string GetWireTypeForMapField(string fieldType)
    {
        var typeName = GetClassNameFromFullName(fieldType);
        
        switch (typeName)
        {
            case "string":
            case "System.String":
                return "WireType.Len";
            case "int":
            case "System.Int32":
            case "Int32":
            case "long":
            case "System.Int64":
            case "Int64":
            case "bool":
            case "System.Boolean":
            case "Boolean":
                return "WireType.VarInt";
            case "float":
            case "System.Single":
            case "Single":
                return "WireType.Fixed32b";
            case "double":
            case "System.Double":
            case "Double":
                return "WireType.Fixed64b";
            default:
                // Complex types are length-delimited
                return "WireType.Len";
        }
    }
    
    /// <summary>
    /// Writes the size calculation for a map key/value field
    /// </summary>
    private static void WriteMapFieldSizeCalculation(StringBuilderWithIndent sb, string valueAccess, string fieldType, string calculatorVar)
    {
        var typeName = GetClassNameFromFullName(fieldType);
        
        switch (typeName)
        {
            case "string":
            case "System.String":
                sb.AppendIndentedLine($"{calculatorVar}.WriteString({valueAccess});");
                break;
            case "int":
            case "System.Int32":
            case "Int32":
                sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32({valueAccess});");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt64({valueAccess});");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{calculatorVar}.WriteBool({valueAccess});");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{calculatorVar}.WriteFloat({valueAccess});");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{calculatorVar}.WriteDouble({valueAccess});");
                break;
            default:
                // For complex types, calculate nested message size
                sb.AppendIndentedLine($"var valueCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(fieldType)}Size(ref valueCalc, {valueAccess});");
                sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)valueCalc.Length);");
                sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(valueCalc.Length);");
                break;
        }
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
                else if (concreteType.StartsWith("HashSet<") || concreteType.StartsWith("System.Collections.Generic.HashSet<"))
                {
                    // HashSet<byte> → create HashSet from byte array
                    sb.AppendIndentedLine($"result.{protoMember.Name} = new global::System.Collections.Generic.HashSet<byte>(byteArray);");
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
            // Check if we can use specialized List reader methods for common cases
            bool canUseDirectListReader = (protoMember.CollectionKind == CollectionKind.ConcreteCollection && 
                                          (protoMember.Type.StartsWith("List<") || 
                                           protoMember.Type.StartsWith("System.Collections.Generic.List<") ||
                                           protoMember.Type == "System.Collections.Generic.List`1")) ||
                                          protoMember.CollectionKind == CollectionKind.InterfaceCollection;

            if (canUseDirectListReader && elementTypeName == "int" && protoMember.DataFormat == DataFormat.FixedSize)
            {
                sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedFixedSizeInt32List();");
            }
            else
            {
                // Fallback to array-based approach
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
                        else if (concreteType.StartsWith("HashSet<") || concreteType.StartsWith("System.Collections.Generic.HashSet<"))
                        {
                            // HashSet<T> → create HashSet from array
                            sb.AppendIndentedLine($"result.{protoMember.Name} = new global::System.Collections.Generic.HashSet<{elementType}>(primitiveArray);");
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
        }
        else
        {
            // Non-packed reading - generate loop-based reading similar to existing array logic
            var elementReadMethod = GetPrimitiveElementReadMethod(elementTypeName, protoMember.DataFormat);
            //sb.AppendIndentedLine($"List<{elementType}> resultList = new(1);");
            sb.AppendIndentedLine($"using UnmanagedCollectionCollector<{elementType}> resultCollector = new(stackalloc {elementType}[256 / sizeof({elementType})], 1024);");
            sb.AppendIndentedLine($"var wireType1 = wireType;");
            sb.AppendIndentedLine($"var fieldId1 = fieldId;");
            sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == {GetExpectedWireType(elementTypeName, protoMember.DataFormat)})");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"resultCollector.Add({elementReadMethod});");
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
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToArray();");
                    break;
                case CollectionKind.InterfaceCollection:
                    sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToList();");
                    break;
                case CollectionKind.ConcreteCollection:
                    var concreteType = protoMember.Type;
                    if (concreteType.StartsWith("HashSet<") || concreteType.StartsWith("System.Collections.Generic.HashSet<"))
                    {
                        // HashSet<T> → create HashSet from items
                        sb.AppendIndentedLine($"result.{protoMember.Name} = new global::System.Collections.Generic.HashSet<{elementType}>(resultCollector.ToArray());");
                    }
                    else
                    {
                        // Default to List<T> for other concrete collections
                        sb.AppendIndentedLine($"result.{protoMember.Name} = resultCollector.ToList();");
                    }
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
        WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
        
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
    private static void WriteMapProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName, WriteTarget writeTarget, string writeTargetShortName)
    {
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        // Maps are serialized as repeated message entries
        // Each entry has field 1 for key and field 2 for value
        sb.AppendIndentedLine($"foreach (var kvp in {objectName}.{protoMember.Name})");
        sb.StartNewBlock();
        
        // Calculate entry size
        sb.AppendIndentedLine($"var entryCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
        
        // Add key (field 1)
        WriteMapKeySize(sb, protoMember.MapKeyType, "kvp.Key", "entryCalculator");
        
        // Add value (field 2)  
        WriteMapValueSize(sb, protoMember.MapValueType, "kvp.Value", "entryCalculator");
        
        // Write the entry tag and length
        WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)entryCalculator.Length);");
        
        // Write the actual key and value
        WriteMapKey(sb, protoMember.MapKeyType, "kvp.Key");
        WriteMapValue(sb, protoMember.MapValueType, "kvp.Value", writeTarget, writeTargetShortName);
        
        sb.EndBlock(); // end foreach
        sb.EndBlock(); // end if not null
    }
    
    private static void WriteMapKeySize(StringBuilderWithIndent sb, string keyType, string keyAccess, string calculator)
    {
        // Key is always field 1
        var simpleType = GetSimpleTypeName(keyType);
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteString({keyAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteVarInt32({keyAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteVarInt64({keyAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteBool({keyAccess});");
                break;
                
            case "uint":
            case "UInt32":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteUInt32({keyAccess});");
                break;
                
            case "ulong":
            case "UInt64":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 1");
                sb.AppendIndentedLine($"{calculator}.WriteUInt64({keyAccess});");
                break;
        }
    }
    
    private static void WriteMapValueSize(StringBuilderWithIndent sb, string valueType, string valueAccess, string calculator)
    {
        // Value is always field 2
        var simpleType = GetSimpleTypeName(valueType);
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2");
                sb.AppendIndentedLine($"{calculator}.WriteString({valueAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2");
                sb.AppendIndentedLine($"{calculator}.WriteVarInt32({valueAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2");
                sb.AppendIndentedLine($"{calculator}.WriteVarInt64({valueAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2");
                sb.AppendIndentedLine($"{calculator}.WriteBool({valueAccess});");
                break;
                
            case "float":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2, Fixed32b");
                sb.AppendIndentedLine($"{calculator}.WriteFloat({valueAccess});");
                break;
                
            case "double":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2, Fixed64b");
                sb.AppendIndentedLine($"{calculator}.WriteDouble({valueAccess});");
                break;
                
            default:
                // For complex types, we need to calculate their size
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // tag for field 2");
                sb.AppendIndentedLine($"var valueCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(valueType)}Size(ref valueCalc, {valueAccess});");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)valueCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(valueCalc.Length);");
                sb.EndBlock();
                break;
        }
    }
    
    private static void WriteMapKey(StringBuilderWithIndent sb, string keyType, string keyAccess)
    {
        var simpleType = GetSimpleTypeName(keyType);
        
        // Key is field 1, wire type depends on the type
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                //sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)System.Text.Encoding.UTF8.GetByteCount({keyAccess}));");
                sb.AppendIndentedLine($"writer.WriteString({keyAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarInt32({keyAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarInt64({keyAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteBool({keyAccess});");
                break;
                
            case "uint":
            case "UInt32":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteUInt32({keyAccess});");
                break;
                
            case "ulong":
            case "UInt64":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteUInt64({keyAccess});");
                break;
        }
    }
    
    private static void WriteMapValue(StringBuilderWithIndent sb, string valueType, string valueAccess, WriteTarget writeTarget, string writeTargetShortName)
    {
        var simpleType = GetSimpleTypeName(valueType);
        
        // Value is field 2, wire type depends on the type
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                //sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)System.Text.Encoding.UTF8.GetByteCount({valueAccess}));");
                sb.AppendIndentedLine($"writer.WriteString({valueAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarInt32({valueAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarInt64({valueAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteBool({valueAccess});");
                break;
                
            case "float":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x15); // field 2, Fixed32b");
                sb.AppendIndentedLine($"writer.WriteFloat({valueAccess});");
                break;
                
            case "double":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x11); // field 2, Fixed64b");
                sb.AppendIndentedLine($"writer.WriteDouble({valueAccess});");
                break;
                
            default:
                // For complex types
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"var valueCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{GetClassNameFromFullName(valueType)}Size(ref valueCalc, {valueAccess});");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)valueCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{GetClassNameFromFullName(valueType)}(ref writer, {valueAccess});");
                sb.EndBlock();
                break;
        }
    }
    
    private static string GetSimpleTypeName(string fullTypeName)
    {
        // Remove namespace and get just the type name
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
    }

    private static void WritePrimitiveCollectionProtoMemberSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember, string objectName)
    {
        var elementType = protoMember.CollectionElementType;
        var elementTypeName = GetClassNameFromFullName(elementType);
        
        sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        if (protoMember.IsPacked)
        {
            // Packed serialization - iterate directly over collection
            WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
            
            // Optimize for fixed-size int32: length = 4 * count
            if (elementTypeName == "int" && protoMember.DataFormat == DataFormat.FixedSize)
            {
                // Use appropriate count/length property and batch write method based on collection type
                string countProperty = protoMember.CollectionKind == CollectionKind.Array ? "Length" : "Count";
                string batchWriteMethod = protoMember.CollectionKind == CollectionKind.Array 
                    ? "WritePackedFixedSizeIntArray" 
                    : "WritePackedFixedSizeIntList";
                    
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)({objectName}.{protoMember.Name}.{countProperty} * 4));");
                sb.AppendIndentedLine($"writer.{batchWriteMethod}({objectName}.{protoMember.Name});");
            }
            else
            {
                // General case: use calculator
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
                
                // For single-byte tags, use a simple byte variable instead of Span
                if (tagLength == 1)
                {
                    sb.AppendIndentedLine($"byte tag{protoMember.FieldId} = {tagBytesString}; // Single byte tag");
                }
                else
                {
                    sb.AppendIndentedLine($"System.Span<byte> tag{protoMember.FieldId} = stackalloc byte[] {{ {tagBytesString} }};");
                }
                
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
                    
                    // Optimize for single-byte tags
                    if (tagLength == 1)
                    {
                        sb.AppendIndentedLine($"*dst = tag{protoMember.FieldId}; // Direct byte assignment");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    }
                    
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
                    
                    // Optimize for single-byte tags
                    if (tagLength == 1)
                    {
                        sb.AppendIndentedLine($"*dst = tag{protoMember.FieldId}; // Direct byte assignment");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    }
                    
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
                
                // For single-byte tags, use a simple byte variable instead of Span
                if (tagLength == 1)
                {
                    sb.AppendIndentedLine($"byte tag{protoMember.FieldId} = {tagBytesString}; // Single byte tag");
                }
                else
                {
                    sb.AppendIndentedLine($"System.Span<byte> tag{protoMember.FieldId} = stackalloc byte[] {{ {tagBytesString} }};");
                }
                
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
                    
                    // Optimize for single-byte tags
                    if (tagLength == 1)
                    {
                        sb.AppendIndentedLine($"*dst = tag{protoMember.FieldId}; // Direct byte assignment");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    }
                    
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
                    
                    // Optimize for single-byte tags
                    if (tagLength == 1)
                    {
                        sb.AppendIndentedLine($"*dst = tag{protoMember.FieldId}; // Direct byte assignment");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"tag{protoMember.FieldId}.CopyTo(new System.Span<byte>(dst, {tagLength}));");
                    }
                    
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
                // Get wire type at code generation time
                var wireTypeStr = GetWireTypeForElement(elementTypeName, protoMember.DataFormat);
                // Parse the wire type enum value
                var wireType = wireTypeStr.Replace("WireType.", "") switch
                {
                    "VarInt" => WireType.VarInt,
                    "Fixed32b" => WireType.Fixed32b,
                    "Fixed64b" => WireType.Fixed64b,
                    "Len" => WireType.Len,
                    _ => WireType.VarInt
                };
                WritePrecomputedTag(sb, protoMember.FieldId, wireType);
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
        WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
        
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
            WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
            
            // Optimize for fixed-size int32: length = 4 * count
            if (elementTypeName == "int" && protoMember.DataFormat == DataFormat.FixedSize)
            {
                // Use appropriate count/length property based on collection type
                string countProperty = protoMember.CollectionKind == CollectionKind.Array ? "Length" : "Count";
                sb.AppendIndentedLine($"var tempLength = obj.{protoMember.Name}.{countProperty} * 4;");
                sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempLength);");
                sb.AppendIndentedLine($"calculator.AddByteLength(tempLength);");
            }
            else
            {
                // General case: use temporary calculator
                sb.AppendIndentedLine($"var tempCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                var elementSizeMethod = GetPrimitiveElementSizeMethod(elementTypeName, protoMember.DataFormat);
                WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                    loopSb.AppendIndentedLine($"{elementSizeMethod.Replace("calculator.", "tempCalculator.")};");
                });
                sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)tempCalculator.Length);");
                sb.AppendIndentedLine($"calculator.AddByteLength(tempCalculator.Length);");
            }
        }
        else
        {
            // Non-packed size calculation
            var elementSizeMethod = GetPrimitiveElementSizeMethod(elementTypeName, protoMember.DataFormat);
            WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                var wireTypeStr = GetWireTypeForElement(elementTypeName, protoMember.DataFormat);
                var wireType = wireTypeStr switch
                {
                    "WireType.VarInt" => WireType.VarInt,
                    "WireType.Fixed32b" => WireType.Fixed32b,
                    "WireType.Fixed64b" => WireType.Fixed64b,
                    "WireType.Len" => WireType.Len,
                    _ => WireType.VarInt
                };
                WritePrecomputedTagForCalculator(loopSb, protoMember.FieldId, wireType);
                loopSb.AppendIndentedLine($"{elementSizeMethod};");
            });
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
                _ => "foreach(var item in tempArray) writer.WriteVarInt32(item)"
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
                _ => "foreach(var item in tempArray) writer.WriteVarInt64(item)"
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
                _ => "writer.WriteVarInt32(item)"
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
                _ => "writer.WriteVarInt64(item)"
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
                _ => "foreach(var item in tempArray) calculator.WriteVarInt32(item)"
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
                _ => "foreach(var item in tempArray) calculator.WriteVarInt64(item)"
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
                _ => "calculator.WriteVarInt32(item)"
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
                _ => "calculator.WriteVarInt64(item)"
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
    
    /// <summary>
    /// Determines the correct type to create for a dictionary/map based on the target property type
    /// </summary>
    private static string GetDictionaryCreationType(string mapType, string keyType, string valueType)
    {
        // For List<KeyValuePair<K,V>> we need to use Dictionary<K,V> for intermediate storage
        if (mapType.Contains("KeyValuePair<"))
        {
            return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        }
        
        // For interface types (IDictionary<K,V>), use Dictionary<K,V>
        if (mapType.Contains("IDictionary<") || mapType.StartsWith("System.Collections.Generic.IDictionary<"))
        {
            return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        }
        
        // For concrete Dictionary<K,V>, use Dictionary<K,V>
        if (mapType.Contains("Dictionary<") && !IsCustomDictionaryType(mapType))
        {
            return $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        }
        
        // For custom derived dictionary types, use the full qualified type name
        // This handles cases like DerivedDictionary : Dictionary<long, string>
        return $"global::{mapType}";
    }
    
    /// <summary>
    /// Checks if the type is a custom dictionary type (derived from Dictionary)
    /// </summary>
    private static bool IsCustomDictionaryType(string mapType)
    {
        // If it's not the standard Dictionary<K,V> type name pattern, it's likely a custom type
        // Standard patterns: "Dictionary<", "System.Collections.Generic.Dictionary<"
        return !mapType.StartsWith("System.Collections.Generic.Dictionary<") && 
               !mapType.StartsWith("Dictionary<") &&
               mapType.Contains("Dictionary");
    }

    #endregion
}