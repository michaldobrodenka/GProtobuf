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

    private static void WritePrecomputedTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, 
        WriteTarget writeTarget, string writeTargetShortName)
    {
        var (tagBytes, tagLen) = PrecomputeTagBytes(fieldId, wireType);
        sb.AppendIndentedLine($"// Tag for field {fieldId}, {wireType}");
        // Always use "writer" as the variable name
        string writerVar = "writer";
        if (tagLen == 1)
        {
            sb.AppendIndentedLine($"{writerVar}.WriteSingleByte({tagBytes});");
        }
        else
        {
            sb.AppendIndentedLine($"{writerVar}.WriteBytes(stackalloc byte[] {{ {tagBytes} }});");
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

        // Generate Tuple methods for SpanReaders
        var tupleTypes = new HashSet<string>();
        foreach (var obj in objects)
        {
            if (obj.ProtoMembers != null)
            {
                foreach (var member in obj.ProtoMembers)
                {
                    if (IsTupleType(member.Type))
                    {
                        tupleTypes.Add(member.Type);
                    }
                }
            }
        }

        foreach (var tupleType in tupleTypes)
        {
            var (item1Type, item2Type) = GetTupleTypeArguments(tupleType);
            if (item1Type != null && item2Type != null)
            {
                var methodName = SanitizeTypeNameForMethod(tupleType);
                
                // Generate ReadTuple method
                sb.AppendIndentedLine($"public static {tupleType} Read{methodName}(ref SpanReader reader)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{item1Type} item1 = default({item1Type});");
                sb.AppendIndentedLine($"{item2Type} item2 = default({item2Type});");
                sb.AppendIndentedLine("");
                sb.AppendIndentedLine("while (!reader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine("var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();");
                sb.AppendIndentedLine("");
                sb.AppendIndentedLine("if (fieldId == 1)");
                sb.StartNewBlock();
                
                // Read Item1 based on type
                if (item1Type == "System.Double" || item1Type == "double")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadDouble(wireType);");
                }
                else if (item1Type == "System.Single" || item1Type == "float")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadFloat(wireType);");
                }
                else if (item1Type == "System.Int32" || item1Type == "int")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadVarInt32();");
                }
                else if (item1Type == "System.Int64" || item1Type == "long")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadVarInt64();");
                }
                else if (item1Type == "System.String" || item1Type == "string")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadString(wireType);");
                }
                else if (item1Type == "System.Boolean" || item1Type == "bool")
                {
                    sb.AppendIndentedLine("item1 = reader.ReadBool();");
                }
                else if (item1Type == "System.Char" || item1Type == "char")
                {
                    sb.AppendIndentedLine("item1 = (char)reader.ReadVarInt32();");
                }
                else if (item1Type.Contains("Enum")) // Simple check for enum types
                {
                    sb.AppendIndentedLine($"item1 = ({item1Type})reader.ReadVarInt32();");
                }
                else
                {
                    // Complex type
                    sb.AppendIndentedLine("var length1 = reader.ReadVarInt32();");
                    sb.AppendIndentedLine("var reader1 = new SpanReader(reader.GetSlice(length1));");
                    sb.AppendIndentedLine($"item1 = Read{SanitizeTypeNameForMethod(item1Type)}(ref reader1);");
                }
                
                sb.AppendIndentedLine("continue;");
                sb.EndBlock();
                sb.AppendIndentedLine("");
                sb.AppendIndentedLine("if (fieldId == 2)");
                sb.StartNewBlock();
                
                // Read Item2 based on type
                if (item2Type == "System.Double" || item2Type == "double")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadDouble(wireType);");
                }
                else if (item2Type == "System.Single" || item2Type == "float")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadFloat(wireType);");
                }
                else if (item2Type == "System.Int32" || item2Type == "int")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadVarInt32();");
                }
                else if (item2Type == "System.Int64" || item2Type == "long")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadVarInt64();");
                }
                else if (item2Type == "System.String" || item2Type == "string")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadString(wireType);");
                }
                else if (item2Type == "System.Boolean" || item2Type == "bool")
                {
                    sb.AppendIndentedLine("item2 = reader.ReadBool();");
                }
                else if (item2Type == "System.Char" || item2Type == "char")
                {
                    sb.AppendIndentedLine("item2 = (char)reader.ReadVarInt32();");
                }
                else if (item2Type.Contains("Enum")) // Simple check for enum types
                {
                    sb.AppendIndentedLine($"item2 = ({item2Type})reader.ReadVarInt32();");
                }
                else
                {
                    // Complex type
                    sb.AppendIndentedLine("var length2 = reader.ReadVarInt32();");
                    sb.AppendIndentedLine("var reader2 = new SpanReader(reader.GetSlice(length2));");
                    sb.AppendIndentedLine($"item2 = Read{SanitizeTypeNameForMethod(item2Type)}(ref reader2);");
                }
                
                sb.AppendIndentedLine("continue;");
                sb.EndBlock();
                sb.AppendIndentedLine("");
                sb.AppendIndentedLine("// default");
                sb.AppendIndentedLine("reader.SkipField(wireType);");
                sb.EndBlock();
                sb.AppendIndentedLine("");
                sb.AppendIndentedLine($"return new {tupleType}(item1, item2);");
                sb.EndBlock();
                sb.AppendNewLine();
            }
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

        // Generate Tuple size calculation methods
        var tupleTypesForSize = new HashSet<string>();
        foreach (var obj in objects)
        {
            if (obj.ProtoMembers != null)
            {
                foreach (var member in obj.ProtoMembers)
                {
                    if (IsTupleType(member.Type))
                    {
                        tupleTypesForSize.Add(member.Type);
                    }
                }
            }
        }

        foreach (var tupleType in tupleTypesForSize)
        {
            var (item1Type, item2Type) = GetTupleTypeArguments(tupleType);
            if (item1Type != null && item2Type != null)
            {
                var methodName = SanitizeTypeNameForMethod(tupleType);
                
                sb.AppendIndentedLine($"public static void Calculate{methodName}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {tupleType} tuple)");
                sb.StartNewBlock();
                
                // Calculate Item1 size (field 1)
                if (item1Type == "System.Double" || item1Type == "double")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine("calculator.WriteDouble(tuple.Item1);");
                }
                else if (item1Type == "System.Single" || item1Type == "float")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.Fixed32b);
                    sb.AppendIndentedLine("calculator.WriteFloat(tuple.Item1);");
                }
                else if (item1Type == "System.Int32" || item1Type == "int")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32(tuple.Item1);");
                }
                else if (item1Type == "System.Int64" || item1Type == "long")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt64(tuple.Item1);");
                }
                else if (item1Type == "System.String" || item1Type == "string")
                {
                    if (sb != null)
                    {
                        sb.AppendIndentedLine("if (tuple.Item1 != null)");
                        sb.StartNewBlock();
                        WritePrecomputedTagForCalculator(sb, 1, WireType.Len);
                        sb.AppendIndentedLine("calculator.WriteString(tuple.Item1);");
                        sb.EndBlock();
                    }
                }
                else if (item1Type == "System.Boolean" || item1Type == "bool")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteBool(tuple.Item1);");
                }
                else if (item1Type == "System.Char" || item1Type == "char")
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32((int)tuple.Item1);");
                }
                else if (item1Type.Contains("Enum")) // Simple check for enum types
                {
                    WritePrecomputedTagForCalculator(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32((int)tuple.Item1);");
                }
                else
                {
                    // Complex type
                    sb.AppendIndentedLine("if (tuple.Item1 != null)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, 1, WireType.Len);
                    sb.AppendIndentedLine("var lengthBefore1 = calculator.Length;");
                    sb.AppendIndentedLine($"Calculate{SanitizeTypeNameForMethod(item1Type)}Size(ref calculator, tuple.Item1);");
                    sb.AppendIndentedLine("var contentLength1 = calculator.Length - lengthBefore1;");
                    sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)contentLength1);");
                    sb.EndBlock();
                }
                
                // Calculate Item2 size (field 2)
                if (item2Type == "System.Double" || item2Type == "double")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine("calculator.WriteDouble(tuple.Item2);");
                }
                else if (item2Type == "System.Single" || item2Type == "float")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.Fixed32b);
                    sb.AppendIndentedLine("calculator.WriteFloat(tuple.Item2);");
                }
                else if (item2Type == "System.Int32" || item2Type == "int")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32(tuple.Item2);");
                }
                else if (item2Type == "System.Int64" || item2Type == "long")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt64(tuple.Item2);");
                }
                else if (item2Type == "System.String" || item2Type == "string")
                {
                    if (sb != null)
                    {
                        sb.AppendIndentedLine("if (tuple.Item2 != null)");
                        sb.StartNewBlock();
                        WritePrecomputedTagForCalculator(sb, 2, WireType.Len);
                        sb.AppendIndentedLine("calculator.WriteString(tuple.Item2);");
                        sb.EndBlock();
                    }
                }
                else if (item2Type == "System.Boolean" || item2Type == "bool")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteBool(tuple.Item2);");
                }
                else if (item2Type == "System.Char" || item2Type == "char")
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32((int)tuple.Item2);");
                }
                else if (item2Type.Contains("Enum")) // Simple check for enum types
                {
                    WritePrecomputedTagForCalculator(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("calculator.WriteVarInt32((int)tuple.Item2);");
                }
                else
                {
                    // Complex type
                    sb.AppendIndentedLine("if (tuple.Item2 != null)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, 2, WireType.Len);
                    sb.AppendIndentedLine("var lengthBefore2 = calculator.Length;");
                    sb.AppendIndentedLine($"Calculate{SanitizeTypeNameForMethod(item2Type)}Size(ref calculator, tuple.Item2);");
                    sb.AppendIndentedLine("var contentLength2 = calculator.Length - lengthBefore2;");
                    sb.AppendIndentedLine("calculator.WriteVarUInt32((uint)contentLength2);");
                    sb.EndBlock();
                }
                
                sb.EndBlock();
                sb.AppendNewLine();
            }
        }

        sb.EndBlock(); // Close SizeCalculators
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

        // Generate Tuple write methods
        var tupleTypes = new HashSet<string>();
        foreach (var obj in objects)
        {
            if (obj.ProtoMembers != null)
            {
                foreach (var member in obj.ProtoMembers)
                {
                    if (IsTupleType(member.Type))
                    {
                        tupleTypes.Add(member.Type);
                    }
                }
            }
        }

        foreach (var tupleType in tupleTypes)
        {
            var (item1Type, item2Type) = GetTupleTypeArguments(tupleType);
            if (item1Type != null && item2Type != null)
            {
                var methodName = SanitizeTypeNameForMethod(tupleType);
                
                sb.AppendIndentedLine($"public static void Write{methodName}(ref global::GProtobuf.Core.{shortName}Writer writer, {tupleType} tuple)");
                sb.StartNewBlock();
                
                // Write Item1 (field 1)
                if (item1Type == "System.Double" || item1Type == "double")
                {
                    WritePrecomputedTag(sb, 1, WireType.Fixed64b);
                    sb.AppendIndentedLine("writer.WriteDouble(tuple.Item1);");
                }
                else if (item1Type == "System.Single" || item1Type == "float")
                {
                    WritePrecomputedTag(sb, 1, WireType.Fixed32b);
                    sb.AppendIndentedLine("writer.WriteFloat(tuple.Item1);");
                }
                else if (item1Type == "System.Int32" || item1Type == "int")
                {
                    WritePrecomputedTag(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32(tuple.Item1);");
                }
                else if (item1Type == "System.Int64" || item1Type == "long")
                {
                    WritePrecomputedTag(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt64(tuple.Item1);");
                }
                else if (item1Type == "System.String" || item1Type == "string")
                {
                    WritePrecomputedTag(sb, 1, WireType.Len);
                    sb.AppendIndentedLine("writer.WriteString(tuple.Item1);");
                }
                else if (item1Type == "System.Boolean" || item1Type == "bool")
                {
                    WritePrecomputedTag(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteBool(tuple.Item1);");
                }
                else if (item1Type == "System.Char" || item1Type == "char")
                {
                    WritePrecomputedTag(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32((int)tuple.Item1);");
                }
                else if (item1Type.Contains("Enum")) // Simple check for enum types
                {
                    WritePrecomputedTag(sb, 1, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32((int)tuple.Item1);");
                }
                else
                {
                    // Complex type
                    WritePrecomputedTag(sb, 1, WireType.Len);
                    sb.AppendIndentedLine("var calc1 = new global::GProtobuf.Core.WriteSizeCalculator();");
                    sb.AppendIndentedLine($"SizeCalculators.Calculate{SanitizeTypeNameForMethod(item1Type)}Size(ref calc1, tuple.Item1);");
                    sb.AppendIndentedLine("writer.WriteVarUInt32((uint)calc1.Length);");
                    sb.AppendIndentedLine($"{shortName}Writers.Write{SanitizeTypeNameForMethod(item1Type)}(ref writer, tuple.Item1);");
                }
                
                // Write Item2 (field 2)
                if (item2Type == "System.Double" || item2Type == "double")
                {
                    WritePrecomputedTag(sb, 2, WireType.Fixed64b);
                    sb.AppendIndentedLine("writer.WriteDouble(tuple.Item2);");
                }
                else if (item2Type == "System.Single" || item2Type == "float")
                {
                    WritePrecomputedTag(sb, 2, WireType.Fixed32b);
                    sb.AppendIndentedLine("writer.WriteFloat(tuple.Item2);");
                }
                else if (item2Type == "System.Int32" || item2Type == "int")
                {
                    WritePrecomputedTag(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32(tuple.Item2);");
                }
                else if (item2Type == "System.Int64" || item2Type == "long")
                {
                    WritePrecomputedTag(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt64(tuple.Item2);");
                }
                else if (item2Type == "System.String" || item2Type == "string")
                {
                    WritePrecomputedTag(sb, 2, WireType.Len);
                    sb.AppendIndentedLine("writer.WriteString(tuple.Item2);");
                }
                else if (item2Type == "System.Boolean" || item2Type == "bool")
                {
                    WritePrecomputedTag(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteBool(tuple.Item2);");
                }
                else if (item2Type == "System.Char" || item2Type == "char")
                {
                    WritePrecomputedTag(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32((int)tuple.Item2);");
                }
                else if (item2Type.Contains("Enum")) // Simple check for enum types
                {
                    WritePrecomputedTag(sb, 2, WireType.VarInt);
                    sb.AppendIndentedLine("writer.WriteVarInt32((int)tuple.Item2);");
                }
                else
                {
                    // Complex type
                    WritePrecomputedTag(sb, 2, WireType.Len);
                    sb.AppendIndentedLine("var calc2 = new global::GProtobuf.Core.WriteSizeCalculator();");
                    sb.AppendIndentedLine($"SizeCalculators.Calculate{SanitizeTypeNameForMethod(item2Type)}Size(ref calc2, tuple.Item2);");
                    sb.AppendIndentedLine("writer.WriteVarUInt32((uint)calc2.Length);");
                    sb.AppendIndentedLine($"{shortName}Writers.Write{SanitizeTypeNameForMethod(item2Type)}(ref writer, tuple.Item2);");
                }
                
                sb.EndBlock();
                sb.AppendNewLine();
            }
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

            case "char":
            case "Char":
            case "System.Char":
                sb.AppendIndentedLine($"result.{protoMember.Name} = (char)reader.ReadVarUInt32();");
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

            case "System.Char[]":
            case "Char[]":
            case "char[]":
                if (protoMember.IsPacked)
                {
                    sb.AppendIndentedLine($"result.{protoMember.Name} = reader.ReadPackedVarIntCharArray();");
                }
                else
                {
                    sb.AppendIndentedLine($"List<char> resultList = new();");
                    sb.AppendIndentedLine($"var wireType1 = wireType;");
                    sb.AppendIndentedLine($"var fieldId1 = fieldId;");
                    sb.AppendNewLine();
                    sb.AppendIndentedLine($"while (!reader.IsEnd)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var number = (char)reader.ReadVarInt32();");
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

            default:

                sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                sb.AppendIndentedLine($"result.{protoMember.Name} = global::{protoMember.Namespace}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(protoMember.Type)}(ref reader1);");
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

            case "char":
            case "Char":
            case "System.Char":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){objectName}.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != '\\0')");
                    sb.StartNewBlock();
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){objectName}.{protoMember.Name});");
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
                    var guidVarName = $"guidBytes_{protoMember.Name}_{protoMember.FieldId}";
                    sb.AppendIndentedLine($"var {guidVarName} = {objectName}.{protoMember.Name}.Value.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64({guidVarName}, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64({guidVarName}, 8);");
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
                    var guidVarName = $"guidBytes_{protoMember.Name}_{protoMember.FieldId}";
                    sb.AppendIndentedLine($"var {guidVarName} = {objectName}.{protoMember.Name}.ToByteArray();");
                    sb.AppendIndentedLine($"var low = System.BitConverter.ToUInt64({guidVarName}, 0);");
                    sb.AppendIndentedLine($"var high = System.BitConverter.ToUInt64({guidVarName}, 8);");
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

            case "System.Char[]":
            case "Char[]":
            case "char[]":
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"var packedSize = Utils.GetVarintPackedCollectionSizeInt32({objectName}.{protoMember.Name}.Select(c => (int)c).ToArray());");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedSize);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarUInt32((uint)v); }}");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in {objectName}.{protoMember.Name}) {{ writer.WriteVarInt32(tagAndWire); writer.WriteVarUInt32((uint)v); }}");
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if ({objectName}.{protoMember.Name} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var calculator{protoMember.FieldId} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{SanitizeTypeNameForMethod(protoMember.Type)}Size(ref calculator{protoMember.FieldId}, {objectName}.{protoMember.Name});");
                WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator{protoMember.FieldId}.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{SanitizeTypeNameForMethod(protoMember.Type)}(ref writer, {objectName}.{protoMember.Name});");
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

            case "char":
            case "Char":
            case "System.Char":
                if (isNullable)
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name}.HasValue)");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)obj.{protoMember.Name}.Value);");
                    sb.EndBlock();
                }
                else
                {
                    sb.AppendIndentedLine($"if (obj.{protoMember.Name} != '\\0')");
                    sb.StartNewBlock();
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.VarInt);
                    sb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)obj.{protoMember.Name});");
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

            case "System.Char[]":
            case "Char[]":
            case "char[]":
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                if (protoMember.IsPacked)
                {
                    WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                    sb.AppendIndentedLine($"calculator.WritePackedInt32Array(obj.{protoMember.Name}.Select(c => (int)c).ToArray());");
                }
                else
                {
                    sb.AppendIndentedLine($"var tagAndWire = Utils.GetTagAndWireType({protoMember.FieldId}, WireType.VarInt);");
                    sb.AppendIndentedLine($"foreach(var v in obj.{protoMember.Name}) {{ calculator.WriteVarInt32(tagAndWire); calculator.WriteVarUInt32((uint)v); }}");
                }
                sb.EndBlock();
                break;

            default:
                sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
                sb.StartNewBlock();
                WritePrecomputedTagForCalculator(sb, protoMember.FieldId, WireType.Len);
                sb.AppendIndentedLine($"var lengthBefore{protoMember.FieldId} = calculator.Length;");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{SanitizeTypeNameForMethod(protoMember.Type)}Size(ref calculator, obj.{protoMember.Name});");
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

        // Handle generic types - don't split inside angle brackets
        int genericStart = fullTypeName.IndexOf('<');
        if (genericStart > 0)
        {
            // Find the last dot before the generic parameters
            string beforeGeneric = fullTypeName.Substring(0, genericStart);
            int lastDot = beforeGeneric.LastIndexOf('.');
            if (lastDot >= 0)
            {
                // Return everything after the last dot (including generic parameters)
                return fullTypeName.Substring(lastDot + 1);
            }
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
            "char" or "System.Char" => true,  // char is serialized as varint
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
            "byte" or "System.Byte" or "Byte" => true,  // byte is primitive for map keys
            _ => IsPrimitiveArrayType(typeName) // reuse the logic for numeric primitives
        };
    }

    private static bool IsTupleType(string typeName)
    {
        return typeName != null && (typeName.StartsWith("System.Tuple<") || typeName.StartsWith("Tuple<"));
    }

    private static (string, string) GetTupleTypeArguments(string typeName)
    {
        var genericStart = typeName.IndexOf('<');
        var genericEnd = typeName.LastIndexOf('>');
        if (genericStart > 0 && genericEnd > genericStart)
        {
            var genericArgs = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);
            var argParts = SplitGenericArguments(genericArgs);
            if (argParts.Count == 2)
            {
                return (argParts[0].Trim(), argParts[1].Trim());
            }
        }
        return (null, null);
    }

    private static List<string> SplitGenericArguments(string genericArgs)
    {
        var result = new List<string>();
        var depth = 0;
        var currentArg = new System.Text.StringBuilder();
        
        for (int i = 0; i < genericArgs.Length; i++)
        {
            char c = genericArgs[i];
            if (c == '<')
            {
                depth++;
                currentArg.Append(c);
            }
            else if (c == '>')
            {
                depth--;
                currentArg.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                result.Add(currentArg.ToString().Trim());
                currentArg.Clear();
            }
            else
            {
                currentArg.Append(c);
            }
        }
        
        if (currentArg.Length > 0)
        {
            result.Add(currentArg.ToString().Trim());
        }
        
        return result;
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
        else if (elementType.Contains("KeyValuePair<"))
        {
            // Special handling for List<KeyValuePair<K,V>> - deserialize as map entries
            // Extract key and value types from KeyValuePair<K,V>
            var genericStart = elementType.IndexOf('<');
            var genericEnd = elementType.LastIndexOf('>');
            if (genericStart > 0 && genericEnd > genericStart)
            {
                var genericArgs = elementType.Substring(genericStart + 1, genericEnd - genericStart - 1);
                var commaIndex = FindTopLevelComma(genericArgs);
                if (commaIndex > 0)
                {
                    var keyType = genericArgs.Substring(0, commaIndex).Trim();
                    var valueType = genericArgs.Substring(commaIndex + 1).Trim();
                    
                    // Read as a map entry with key (field 1) and value (field 2)
                    sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                    sb.AppendIndentedLine($"var entryReader = new SpanReader(reader.GetSlice(length));");
                    sb.AppendIndentedLine($"{keyType} key = default({keyType});");
                    sb.AppendIndentedLine($"{valueType} value = default({valueType});");
                    
                    sb.AppendIndentedLine($"while (!entryReader.IsEnd)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var (entryWireType, entryFieldId) = entryReader.ReadWireTypeAndFieldId();");
                    
                    // Read key (field 1)
                    sb.AppendIndentedLine($"if (entryFieldId == 1)");
                    sb.StartNewBlock();
                    
                    // Simple inline deserialization for key
                    var keyTypeName = GetSimpleTypeName(keyType);
                    if (keyTypeName == "long" || keyTypeName == "Int64")
                    {
                        sb.AppendIndentedLine($"key = entryReader.ReadVarInt64();");
                    }
                    else if (keyTypeName == "int" || keyTypeName == "Int32")
                    {
                        sb.AppendIndentedLine($"key = entryReader.ReadVarInt32();");
                    }
                    else if (keyTypeName == "char" || keyTypeName == "Char")
                    {
                        sb.AppendIndentedLine($"key = (char)entryReader.ReadVarUInt32();");
                    }
                    else if (keyTypeName == "string" || keyTypeName == "String")
                    {
                        sb.AppendIndentedLine($"key = entryReader.ReadString(entryWireType);");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var keyLen = entryReader.ReadVarInt32();");
                        sb.AppendIndentedLine($"var keyReader = new SpanReader(entryReader.GetSlice(keyLen));");
                        
                        // Special handling for Tuple types - use current namespace instead of System namespace
                        var keyTypeSimpleName = GetSimpleTypeName(keyType);
                        if (keyTypeSimpleName.StartsWith("Tuple<") || keyTypeSimpleName.StartsWith("System.Tuple<"))
                        {
                            sb.AppendIndentedLine($"key = SpanReaders.Read{SanitizeTypeNameForMethod(keyType)}(ref keyReader);");
                        }
                        else
                        {
                            sb.AppendIndentedLine($"key = global::{GetNamespaceFromType(keyType)}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(keyType)}(ref keyReader);");
                        }
                    }
                    
                    sb.AppendIndentedLine($"continue;");
                    sb.EndBlock();
                    
                    // Read value (field 2)
                    sb.AppendIndentedLine($"if (entryFieldId == 2)");
                    sb.StartNewBlock();
                    
                    // Simple inline deserialization for value
                    var valueTypeName = GetSimpleTypeName(valueType);
                    if (valueTypeName == "long" || valueTypeName == "Int64")
                    {
                        sb.AppendIndentedLine($"value = entryReader.ReadVarInt64();");
                    }
                    else if (valueTypeName == "int" || valueTypeName == "Int32")
                    {
                        sb.AppendIndentedLine($"value = entryReader.ReadVarInt32();");
                    }
                    else if (valueTypeName == "char" || valueTypeName == "Char")
                    {
                        sb.AppendIndentedLine($"value = (char)entryReader.ReadVarUInt32();");
                    }
                    else if (valueTypeName == "string" || valueTypeName == "String")
                    {
                        sb.AppendIndentedLine($"value = entryReader.ReadString(entryWireType);");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"var valueLen = entryReader.ReadVarInt32();");
                        sb.AppendIndentedLine($"var valueReader = new SpanReader(entryReader.GetSlice(valueLen));");
                        
                        // Special handling for Tuple types - use current namespace instead of System namespace
                        var valueTypeSimpleName = GetSimpleTypeName(valueType);
                        if (valueTypeSimpleName.StartsWith("Tuple<") || valueTypeSimpleName.StartsWith("System.Tuple<"))
                        {
                            sb.AppendIndentedLine($"value = SpanReaders.Read{SanitizeTypeNameForMethod(valueType)}(ref valueReader);");
                        }
                        else
                        {
                            sb.AppendIndentedLine($"value = global::{GetNamespaceFromType(valueType)}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(valueType)}(ref valueReader);");
                        }
                    }
                    
                    sb.AppendIndentedLine($"continue;");
                    sb.EndBlock();
                    
                    sb.AppendIndentedLine($"entryReader.SkipField(entryWireType);");
                    sb.EndBlock();
                    
                    sb.AppendIndentedLine($"var item = new global::System.Collections.Generic.KeyValuePair<{keyType}, {valueType}>(key, value);");
                    sb.AppendIndentedLine($"resultCollector.Add(item);");
                }
            }
        }
        else
        {
            // Custom message types - deserialize nested messages
            sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
            sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
            sb.AppendIndentedLine($"var item = global::{protoMember.Namespace}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(elementType)}(ref reader1);");
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
        
        // Create a virtual TypeDefinition for the map entry message
        // Map entries are messages with field 1 (key) and field 2 (value)
        var mapEntryType = CreateMapEntryTypeDefinition(keyType, valueType, protoMember.MapKeyIsEnum, protoMember.MapValueIsEnum);
        
        // Create dictionary to collect map entries
        var dictType = GetDictionaryCreationType(mapType, keyType, valueType);
        sb.AppendIndentedLine($"var mapDict = new {dictType}();");
        sb.AppendIndentedLine($"var wireType1 = wireType;");
        sb.AppendIndentedLine($"var fieldId1 = fieldId;");
        
        // Loop through all map entries (wire type is always Len for map entries)
        sb.AppendIndentedLine($"while (fieldId1 == fieldId && wireType1 == WireType.Len)");
        sb.StartNewBlock();
        
        // Read the map entry message length
        sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
        sb.AppendIndentedLine($"var entryReader = new SpanReader(reader.GetSlice(length));");
        
        // Initialize key/value variables
        sb.AppendIndentedLine($"{keyType} key = default({keyType});");
        sb.AppendIndentedLine($"{valueType} value = default({valueType});");
        
        // Generate the map entry deserializer using the unified approach
        GenerateMapEntryDeserializer(sb, mapEntryType, "key", "value", "entryReader");
        
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

    private static TypeDefinition CreateMapEntryTypeDefinition(string keyType, string valueType, bool keyIsEnum = false, bool valueIsEnum = false)
    {
        // Create ProtoMember for key (field 1)
        var keyMember = new ProtoMemberAttribute(1)
        {
            Name = "Key",
            Type = keyType,
            DataFormat = DataFormat.Default,
            IsPacked = false,
            IsRequired = false,
            IsEnum = keyIsEnum,
            Namespace = GetNamespaceFromType(keyType)
        };
        
        // Handle special cases for key collections (arrays)
        if (keyType.EndsWith("[]"))
        {
            keyMember.IsCollection = true;
            keyMember.CollectionElementType = keyType.Substring(0, keyType.Length - 2);
            keyMember.CollectionKind = CollectionKind.Array;
            
            // For primitive array keys (except byte[] which uses raw bytes and strings), use packed encoding
            var elementType = keyMember.CollectionElementType;
            var simpleType = GetSimpleTypeName(elementType);
            if (IsPrimitiveType(simpleType) && 
                simpleType != "byte" && simpleType != "Byte" && simpleType != "System.Byte" &&
                simpleType != "string" && simpleType != "String" && simpleType != "System.String")
            {
                keyMember.IsPacked = true;
            }
        }
        // Handle generic collections as keys (List<>, HashSet<>, etc.) - BUT NOT Tuples
        else if ((keyType.Contains("List<") || keyType.Contains("HashSet<") || keyType.Contains("<")) && 
                 !keyType.StartsWith("Tuple<") && !keyType.StartsWith("System.Tuple<"))
        {
            keyMember.IsCollection = true;
            var genericStart = keyType.IndexOf('<');
            var genericEnd = keyType.LastIndexOf('>');
            if (genericStart > 0 && genericEnd > genericStart)
            {
                keyMember.CollectionElementType = keyType.Substring(genericStart + 1, genericEnd - genericStart - 1);
                
                // Check if it's a numeric primitive for packed encoding (NOT string!)
                var simpleType = GetSimpleTypeName(keyMember.CollectionElementType);
                bool isNumericPrimitive = simpleType switch
                {
                    "int" or "Int32" or "int32" => true,
                    "long" or "Int64" or "int64" => true,
                    "uint" or "UInt32" or "uint32" => true,
                    "ulong" or "UInt64" or "uint64" => true,
                    "float" or "Single" => true,
                    "double" or "Double" => true,
                    "bool" or "Boolean" => true,
                    "byte" or "Byte" => true,
                    "sbyte" or "SByte" => true,
                    "short" or "Int16" => true,
                    "ushort" or "UInt16" => true,
                    _ => false
                };
                
                if (isNumericPrimitive)
                {
                    keyMember.IsPacked = true;
                }
            }
            keyMember.CollectionKind = CollectionKind.ConcreteCollection;
        }
        
        // Create ProtoMember for value (field 2)
        var valueMember = new ProtoMemberAttribute(2)
        {
            Name = "Value", 
            Type = valueType,
            DataFormat = DataFormat.Default,
            IsPacked = false,
            IsRequired = false,
            IsEnum = valueIsEnum,
            Namespace = GetNamespaceFromType(valueType)
        };
        
        // Handle special cases for value collections
        if (valueType.EndsWith("[]"))
        {
            valueMember.IsCollection = true;
            valueMember.CollectionElementType = valueType.Substring(0, valueType.Length - 2);
            valueMember.CollectionKind = CollectionKind.Array;
            
            // For primitive array values (except string arrays), use packed encoding
            var elementType = valueMember.CollectionElementType;
            var simpleType = GetSimpleTypeName(elementType);
            if (IsPrimitiveType(simpleType) && 
                simpleType != "string" && simpleType != "String" && simpleType != "System.String")
            {
                valueMember.IsPacked = true;
            }
        }
        else if (valueType.Contains("List<") || valueType.Contains("HashSet<"))
        {
            valueMember.IsCollection = true;
            var genericStart = valueType.IndexOf('<');
            var genericEnd = valueType.LastIndexOf('>');
            if (genericStart > 0 && genericEnd > genericStart)
            {
                valueMember.CollectionElementType = valueType.Substring(genericStart + 1, genericEnd - genericStart - 1);
                
                // For primitive collections (except strings), use packed encoding
                var simpleType = GetSimpleTypeName(valueMember.CollectionElementType);
                if (IsPrimitiveType(simpleType) && 
                    simpleType != "string" && simpleType != "String" && simpleType != "System.String")
                {
                    valueMember.IsPacked = true;
                }
            }
            valueMember.CollectionKind = CollectionKind.ConcreteCollection;
        }
        
        var mapEntryType = new TypeDefinition(
            IsStruct: false,
            IsAbstract: false, 
            FullName: "MapEntry",
            ProtoIncludes: new List<ProtoIncludeAttribute>(),
            ProtoMembers: new List<ProtoMemberAttribute> { keyMember, valueMember }
        );
        
        return mapEntryType;
    }
    
    private static void GenerateMapEntryDeserializer(StringBuilderWithIndent sb, TypeDefinition mapEntryType, 
        string keyVar, string valueVar, string readerVar)
    {
        // Check if key is a collection (array)
        var keyMember = mapEntryType.ProtoMembers.FirstOrDefault(m => m.FieldId == 1);
        bool isKeyCollection = keyMember != null && keyMember.IsCollection;
        bool isKeyPrimitiveCollection = false;
        
        if (isKeyCollection && keyMember.CollectionElementType != null)
        {
            var elementTypeName = GetSimpleTypeName(keyMember.CollectionElementType);
            isKeyPrimitiveCollection = IsPrimitiveType(elementTypeName) && elementTypeName != "string" && elementTypeName != "System.String";
            
            // Initialize collector for the key collection
            if (isKeyPrimitiveCollection)
            {
                // For primitive types, need to handle proper type names
                var managedType = GetManagedTypeName(keyMember.CollectionElementType);
                sb.AppendIndentedLine($"using UnmanagedCollectionCollector<{managedType}> keyCollector = new(stackalloc {managedType}[256 / sizeof({managedType})], 1024);");
            }
            else
            {
                sb.AppendIndentedLine($"var keyList = new List<{keyMember.CollectionElementType}>();");
            }
        }
        
        // Check if value is a collection
        var valueMember = mapEntryType.ProtoMembers.FirstOrDefault(m => m.FieldId == 2);
        bool isValueCollection = valueMember != null && valueMember.IsCollection;
        bool isPrimitiveCollection = false;
        
        if (isValueCollection && valueMember.CollectionElementType != null)
        {
            var elementTypeName = GetSimpleTypeName(valueMember.CollectionElementType);
            isPrimitiveCollection = IsPrimitiveType(elementTypeName) && elementTypeName != "string" && elementTypeName != "System.String";
            
            // Initialize collector for the collection
            if (isPrimitiveCollection)
            {
                // Use UnmanagedCollectionCollector for primitive types
                sb.AppendIndentedLine($"using UnmanagedCollectionCollector<{valueMember.CollectionElementType}> valueCollector = new(stackalloc {valueMember.CollectionElementType}[256 / sizeof({valueMember.CollectionElementType})], 1024);");
            }
            else
            {
                // Use List for managed types (including strings)
                sb.AppendIndentedLine($"var valueList = new List<{valueMember.CollectionElementType}>();");
            }
        }
        
        // Read the map entry content using standard message reading logic
        sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
        sb.StartNewBlock();
        sb.AppendIndentedLine($"var (entryWireType, entryFieldId) = {readerVar}.ReadWireTypeAndFieldId();");
        
        // Process field 1 (key)
        if (keyMember != null)
        {
            sb.AppendIndentedLine($"if (entryFieldId == 1)");
            sb.StartNewBlock();
            
            if (isKeyCollection)
            {
                var elementType = keyMember.CollectionElementType;
                var elementTypeName = GetSimpleTypeName(elementType);
                
                // Special handling for byte arrays - read as single Len field
                if (elementTypeName == "byte" || elementTypeName == "Byte" || elementTypeName == "System.Byte")
                {
                    sb.AppendIndentedLine($"// Read byte array as single Len field");
                    sb.AppendIndentedLine($"if (entryWireType == WireType.Len)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var byteLen = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var bytes = {readerVar}.GetSlice(byteLen).ToArray();");
                    sb.AppendIndentedLine($"foreach (var b in bytes)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"keyCollector.Add(b);");
                    sb.EndBlock();
                    sb.EndBlock();
                }
                else if (isKeyPrimitiveCollection)
                {
                    // For primitive arrays (except bytes), handle both packed and repeated formats
                    sb.AppendIndentedLine($"if (entryWireType == WireType.Len)");
                    sb.StartNewBlock();
                    // Packed format - read all values from a single length-prefixed field
                    sb.AppendIndentedLine($"var packedLength = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var packedReader = new SpanReader({readerVar}.GetSlice(packedLength));");
                    sb.AppendIndentedLine($"while (!packedReader.IsEnd)");
                    sb.StartNewBlock();
                    string readMethod = GetPrimitiveReadMethodWithoutWireType(elementTypeName, keyMember.DataFormat);
                    sb.AppendIndentedLine($"keyCollector.Add(packedReader.{readMethod});");
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"else");
                    sb.StartNewBlock();
                    // Repeated format - single value with this field ID
                    string readMethodRepeated = GetPrimitiveReadMethod(elementTypeName, keyMember.DataFormat, "entryWireType");
                    sb.AppendIndentedLine($"keyCollector.Add({readerVar}.{readMethodRepeated});");
                    sb.EndBlock();
                }
                else if (elementTypeName == "string" || elementTypeName == "System.String")
                {
                    // String collections
                    sb.AppendIndentedLine($"var strLen = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var str = System.Text.Encoding.UTF8.GetString({readerVar}.GetSlice(strLen));");
                    sb.AppendIndentedLine($"keyList.Add(str);");
                }
                else
                {
                    // Complex type collections
                    sb.AppendIndentedLine($"var len = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var subReader = new SpanReader({readerVar}.GetSlice(len));");
                    // Special handling for Tuple types - use current namespace instead of System namespace
                    var keyElementTypeSimpleName = GetSimpleTypeName(elementType);
                    if (keyElementTypeSimpleName.StartsWith("Tuple<") || keyElementTypeSimpleName.StartsWith("System.Tuple<"))
                    {
                        sb.AppendIndentedLine($"keyList.Add(SpanReaders.Read{SanitizeTypeNameForMethod(elementType)}(ref subReader));");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"keyList.Add(global::{GetNamespaceFromType(elementType)}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(elementType)}(ref subReader));");
                    }
                }
            }
            else
            {
                // Non-collection key
                GenerateSimpleFieldDeserializer(sb, keyMember, keyVar, readerVar, "entryWireType");
            }
            
            sb.AppendIndentedLine($"continue;");
            sb.EndBlock();
        }
        
        // Process field 2 (value)
        if (valueMember != null)
        {
            sb.AppendIndentedLine($"if (entryFieldId == 2)");
            sb.StartNewBlock();
            
            if (isValueCollection)
            {
                var elementType = valueMember.CollectionElementType;
                var elementTypeName = GetSimpleTypeName(elementType);
                
                if (isPrimitiveCollection)
                {
                    // Handle both packed and repeated formats for primitive collections
                    sb.AppendIndentedLine($"if (entryWireType == WireType.Len)");
                    sb.StartNewBlock();
                    // Packed format - read all values from a single length-prefixed field
                    sb.AppendIndentedLine($"var packedLength = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var packedReader = new SpanReader({readerVar}.GetSlice(packedLength));");
                    sb.AppendIndentedLine($"while (!packedReader.IsEnd)");
                    sb.StartNewBlock();
                    string readMethod = GetPrimitiveReadMethodWithoutWireType(elementTypeName, valueMember.DataFormat);
                    // Special handling for types that need casting
                    if (elementTypeName == "byte" || elementTypeName == "System.Byte" || elementTypeName == "Byte")
                    {
                        sb.AppendIndentedLine($"valueCollector.Add((byte)packedReader.ReadVarUInt32());");
                    }
                    else if (elementTypeName == "sbyte" || elementTypeName == "System.SByte" || elementTypeName == "SByte")
                    {
                        if (valueMember.DataFormat == DataFormat.ZigZag)
                            sb.AppendIndentedLine($"valueCollector.Add((sbyte)packedReader.ReadZigZagVarInt32());");
                        else
                            sb.AppendIndentedLine($"valueCollector.Add((sbyte)packedReader.ReadVarInt32());");
                    }
                    else if (elementTypeName == "short" || elementTypeName == "System.Int16" || elementTypeName == "Int16")
                    {
                        if (valueMember.DataFormat == DataFormat.ZigZag)
                            sb.AppendIndentedLine($"valueCollector.Add((short)packedReader.ReadZigZagVarInt32());");
                        else
                            sb.AppendIndentedLine($"valueCollector.Add((short)packedReader.ReadVarInt32());");
                    }
                    else if (elementTypeName == "ushort" || elementTypeName == "System.UInt16" || elementTypeName == "UInt16")
                    {
                        sb.AppendIndentedLine($"valueCollector.Add((ushort)packedReader.ReadVarUInt32());");
                    }
                    else if (elementTypeName == "char" || elementTypeName == "System.Char" || elementTypeName == "Char")
                    {
                        sb.AppendIndentedLine($"valueCollector.Add((char)packedReader.ReadVarUInt32());");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"valueCollector.Add(packedReader.{readMethod});");
                    }
                    sb.EndBlock();
                    sb.EndBlock();
                    sb.AppendIndentedLine($"else");
                    sb.StartNewBlock();
                    // Repeated format - single value with this field ID
                    string readMethodRepeated = GetPrimitiveReadMethod(elementTypeName, valueMember.DataFormat, "entryWireType");
                    sb.AppendIndentedLine($"valueCollector.Add({readerVar}.{readMethodRepeated});");
                    sb.EndBlock();
                }
                else if (elementTypeName == "string" || elementTypeName == "System.String")
                {
                    // String collections - always repeated, not packed
                    sb.AppendIndentedLine($"var strLen = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var str = System.Text.Encoding.UTF8.GetString({readerVar}.GetSlice(strLen));");
                    sb.AppendIndentedLine($"valueList.Add(str);");
                }
                else
                {
                    // Complex type collections - read as sub-message
                    sb.AppendIndentedLine($"var len = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var subReader = new SpanReader({readerVar}.GetSlice(len));");
                    // Special handling for Tuple types - use current namespace instead of System namespace  
                    var valueElementTypeSimpleName = GetSimpleTypeName(elementType);
                    if (valueElementTypeSimpleName.StartsWith("Tuple<") || valueElementTypeSimpleName.StartsWith("System.Tuple<"))
                    {
                        sb.AppendIndentedLine($"valueList.Add(SpanReaders.Read{SanitizeTypeNameForMethod(elementType)}(ref subReader));");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"valueList.Add(global::{GetNamespaceFromType(elementType)}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(elementType)}(ref subReader));");
                    }
                }
            }
            else
            {
                // Non-collection value - use standard field deserialization
                GenerateSimpleFieldDeserializer(sb, valueMember, valueVar, readerVar, "entryWireType");
            }
            
            sb.AppendIndentedLine($"continue;");
            sb.EndBlock();
        }
        
        // Skip unknown fields
        sb.AppendIndentedLine($"{readerVar}.SkipField(entryWireType);");
        sb.EndBlock();
        
        // After reading all fields, convert collections to final type
        if (isKeyCollection)
        {
            var elementType = keyMember.CollectionElementType;
            
            if (isKeyPrimitiveCollection)
            {
                // Convert from collector to final collection type
                if (keyMember.CollectionKind == CollectionKind.Array)
                {
                    sb.AppendIndentedLine($"{keyVar} = keyCollector.ToArray();");
                }
                else if (keyMember.Type.Contains("HashSet<"))
                {
                    sb.AppendIndentedLine($"{keyVar} = new HashSet<{elementType}>(keyCollector.ToArray());");
                }
                else if (keyMember.Type.Contains("List<"))
                {
                    sb.AppendIndentedLine($"{keyVar} = new List<{elementType}>(keyCollector.ToArray());");
                }
            }
            else
            {
                // Convert from List to final collection type
                if (keyMember.CollectionKind == CollectionKind.Array)
                {
                    sb.AppendIndentedLine($"{keyVar} = keyList.ToArray();");
                }
                else if (keyMember.Type.Contains("HashSet<"))
                {
                    sb.AppendIndentedLine($"{keyVar} = new HashSet<{elementType}>(keyList);");
                }
                else
                {
                    sb.AppendIndentedLine($"{keyVar} = keyList;");
                }
            }
        }
        
        if (isValueCollection)
        {
            var elementType = valueMember.CollectionElementType;
            
            if (isPrimitiveCollection)
            {
                // Convert from collector to final collection type
                if (valueMember.CollectionKind == CollectionKind.Array)
                {
                    sb.AppendIndentedLine($"{valueVar} = valueCollector.ToArray();");
                }
                else if (valueMember.Type.Contains("HashSet<"))
                {
                    sb.AppendIndentedLine($"{valueVar} = new HashSet<{elementType}>(valueCollector.ToArray());");
                }
                else if (valueMember.Type.Contains("List<"))
                {
                    sb.AppendIndentedLine($"{valueVar} = new List<{elementType}>(valueCollector.ToArray());");
                }
            }
            else
            {
                // Convert from List to final collection type
                if (valueMember.CollectionKind == CollectionKind.Array)
                {
                    sb.AppendIndentedLine($"{valueVar} = valueList.ToArray();");
                }
                else if (valueMember.Type.Contains("HashSet<"))
                {
                    sb.AppendIndentedLine($"{valueVar} = new HashSet<{elementType}>(valueList);");
                }
                else
                {
                    sb.AppendIndentedLine($"{valueVar} = valueList;");
                }
            }
        }
    }

    private static WireType ParseWireType(string wireTypeStr)
    {
        switch (wireTypeStr)
        {
            case "VarInt": return WireType.VarInt;
            case "Fixed64b": return WireType.Fixed64b;
            case "Len": return WireType.Len;
            case "Fixed32b": return WireType.Fixed32b;
            default: return WireType.VarInt;
        }
    }

    private static string GetPrimitiveReadMethodWithoutWireType(string typeName, DataFormat dataFormat)
    {
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                if (dataFormat == DataFormat.FixedSize)
                    return "ReadFixedInt32()";
                else if (dataFormat == DataFormat.ZigZag)
                    return "ReadZigZagVarInt32()";
                else
                    return "ReadVarInt32()";
            case "long":
            case "System.Int64":
            case "Int64":
                if (dataFormat == DataFormat.FixedSize)
                    return "ReadFixedInt64()";
                else if (dataFormat == DataFormat.ZigZag)
                    return "ReadZigZagVarInt64()";
                else
                    return "ReadVarInt64()";
            case "bool":
            case "System.Boolean":
            case "Boolean":
                return "ReadBool(WireType.VarInt)";
            case "byte":
            case "System.Byte":
            case "Byte":
                return "(byte)ReadVarUInt32()";
            case "sbyte":
            case "System.SByte":
            case "SByte":
                if (dataFormat == DataFormat.ZigZag)
                    return "(sbyte)ReadZigZagVarInt32()";
                else
                    return "(sbyte)ReadVarInt32()";
            case "short":
            case "System.Int16":
            case "Int16":
                if (dataFormat == DataFormat.ZigZag)
                    return "(short)ReadZigZagVarInt32()";
                else
                    return "(short)ReadVarInt32()";
            case "ushort":
            case "System.UInt16":
            case "UInt16":
                return "(ushort)ReadVarUInt32()";
            case "uint":
            case "System.UInt32":
            case "UInt32":
                return "ReadVarUInt32()";
            case "ulong":
            case "System.UInt64":
            case "UInt64":
                return "ReadVarUInt64()";
            case "char":
            case "System.Char":
            case "Char":
                return "(char)ReadVarUInt32()";
            case "float":
            case "System.Single":
            case "Single":
                return "ReadFixedFloat()";
            case "double":
            case "System.Double":
            case "Double":
                return "ReadFixedDouble()";
            default:
                return "ReadVarInt32()";
        }
    }

    private static void GenerateSimpleFieldDeserializer(StringBuilderWithIndent sb, ProtoMemberAttribute member, 
        string targetVar, string readerVar, string wireTypeVar)
    {
        var typeName = GetClassNameFromFullName(member.Type);
        
        switch (typeName)
        {
            case "string":
            case "System.String":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadString({wireTypeVar});");
                break;
            case "int":
            case "System.Int32":
            case "Int32":
                if (member.DataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFixedInt32();");
                else if (member.DataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadZigZagVarInt32();");
                else
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadVarInt32();");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                if (member.DataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFixedInt64();");
                else if (member.DataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadZigZagVarInt64();");
                else
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadVarInt64();");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadBool({wireTypeVar});");
                break;
            case "byte":
            case "System.Byte":
            case "Byte":
                sb.AppendIndentedLine($"{targetVar} = (byte){readerVar}.ReadVarUInt32();");
                break;
            case "char":
            case "System.Char":
            case "Char":
                sb.AppendIndentedLine($"{targetVar} = (char){readerVar}.ReadVarUInt32();");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFloat({wireTypeVar});");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadDouble({wireTypeVar});");
                break;
            case "Guid":
            case "System.Guid":
                // Read Guid as BCL format (2 fixed64 fields)
                sb.AppendIndentedLine($"var guidLength = {readerVar}.ReadVarInt32();");
                sb.AppendIndentedLine($"var guidReader = new SpanReader({readerVar}.GetSlice(guidLength));");
                sb.AppendIndentedLine($"ulong guidLow = 0, guidHigh = 0;");
                sb.AppendIndentedLine($"while (!guidReader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var guidFieldInfo = guidReader.ReadWireTypeAndFieldId();");
                sb.AppendIndentedLine($"switch (guidFieldInfo.fieldId)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"case 1: guidLow = guidReader.ReadFixed64(); break;");
                sb.AppendIndentedLine($"case 2: guidHigh = guidReader.ReadFixed64(); break;");
                sb.AppendIndentedLine($"default: guidReader.SkipField(guidFieldInfo.wireType); break;");
                sb.EndBlock();
                sb.EndBlock();
                sb.AppendIndentedLine($"// Convert back to Guid");
                sb.AppendIndentedLine($"Span<byte> guidBytes = stackalloc byte[16];");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes, guidLow);");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes.Slice(8), guidHigh);");
                sb.AppendIndentedLine($"{targetVar} = new Guid(guidBytes);");
                break;
            default:
                if (member.IsEnum)
                {
                    sb.AppendIndentedLine($"{targetVar} = ({typeName}){readerVar}.ReadVarInt32();");
                }
                else
                {
                    // For complex types, deserialize as message
                    sb.AppendIndentedLine($"var fieldLength = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var fieldReader{targetVar} = new SpanReader({readerVar}.GetSlice(fieldLength));");
                    
                    // Special handling for Tuple types - use current namespace instead of System namespace
                    var typeNameSimple = GetSimpleTypeName(typeName);
                    if (typeNameSimple.StartsWith("Tuple<") || typeNameSimple.StartsWith("System.Tuple<"))
                    {
                        sb.AppendIndentedLine($"{targetVar} = SpanReaders.Read{SanitizeTypeNameForMethod(typeName)}(ref fieldReader{targetVar});");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"{targetVar} = global::{member.Namespace}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(typeName)}(ref fieldReader{targetVar});");
                    }
                }
                break;
        }
    }

    private static string GetPrimitiveReadMethod(string typeName, DataFormat dataFormat, string wireTypeVar)
    {
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                if (dataFormat == DataFormat.FixedSize)
                    return "ReadFixedInt32()";
                else if (dataFormat == DataFormat.ZigZag)
                    return "ReadZigZagVarInt32()";
                else
                    return "ReadVarInt32()";
            case "long":
            case "System.Int64":
            case "Int64":
                if (dataFormat == DataFormat.FixedSize)
                    return "ReadFixedInt64()";
                else if (dataFormat == DataFormat.ZigZag)
                    return "ReadZigZagVarInt64()";
                else
                    return "ReadVarInt64()";
            case "bool":
            case "System.Boolean":
            case "Boolean":
                return $"ReadBool({wireTypeVar})";
            case "float":
            case "System.Single":
            case "Single":
                return $"ReadFloat({wireTypeVar})";
            case "double":
            case "System.Double":
            case "Double":
                return $"ReadDouble({wireTypeVar})";
            case "byte":
            case "System.Byte":
            case "Byte":
                return $"ReadByte({wireTypeVar})";
            case "char":
            case "System.Char":
            case "Char":
                return $"(char)ReadVarUInt32()";
            default:
                return $"ReadVarInt32()";
        }
    }
    
    private static void GenerateFieldDeserializer(StringBuilderWithIndent sb, ProtoMemberAttribute member, 
        string targetVar, string readerVar)
    {
        var typeName = GetClassNameFromFullName(member.Type);
        
        // Handle arrays
        if (member.IsCollection && member.CollectionKind == CollectionKind.Array)
        {
            GenerateMapValueArrayDeserializer(sb, targetVar, member.CollectionElementType);
            return;
        }
        
        // Handle collections (non-string)
        if (member.IsCollection && member.CollectionKind == CollectionKind.ConcreteCollection)
        {
            GenerateMapValueCollectionDeserializer(sb, targetVar, member.Type);
            return;
        }
        
        // Handle primitives and messages
        switch (typeName)
        {
            case "string":
            case "System.String":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadString(entryWireType);");
                break;
            case "int":
            case "System.Int32":
            case "Int32":
                if (member.DataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFixedInt32();");
                else if (member.DataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadZigZagVarInt32();");
                else
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadVarInt32();");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                if (member.DataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFixedInt64();");
                else if (member.DataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadZigZagVarInt64();");
                else
                    sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadVarInt64();");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadBool(entryWireType);");
                break;
            case "char":
            case "System.Char":
            case "Char":
                sb.AppendIndentedLine($"{targetVar} = (char){readerVar}.ReadVarUInt32();");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadFloat(entryWireType);");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{targetVar} = {readerVar}.ReadDouble(entryWireType);");
                break;
            case "byte":
            case "System.Byte":
            case "Byte":
                sb.AppendIndentedLine($"{targetVar} = (byte){readerVar}.ReadVarUInt32();");
                break;
            case "Guid":
            case "System.Guid":
                // Read Guid as protobuf-net format (2 fixed64 fields)
                sb.AppendIndentedLine($"var guidLength = {readerVar}.ReadVarInt32();");
                sb.AppendIndentedLine($"var guidReader = new SpanReader({readerVar}.GetSlice(guidLength));");
                sb.AppendIndentedLine($"ulong guidLow = 0, guidHigh = 0;");
                sb.AppendIndentedLine($"while (!guidReader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var guidFieldInfo = guidReader.ReadWireTypeAndFieldId();");
                sb.AppendIndentedLine($"switch (guidFieldInfo.fieldId)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"case 1: guidLow = guidReader.ReadFixed64(); break;");
                sb.AppendIndentedLine($"case 2: guidHigh = guidReader.ReadFixed64(); break;");
                sb.AppendIndentedLine($"default: guidReader.SkipField(guidFieldInfo.wireType); break;");
                sb.EndBlock();
                sb.EndBlock();
                sb.AppendIndentedLine($"// Convert back to Guid");
                sb.AppendIndentedLine($"Span<byte> guidBytes = stackalloc byte[16];");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes, guidLow);");
                sb.AppendIndentedLine($"System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(guidBytes.Slice(8), guidHigh);");
                sb.AppendIndentedLine($"{targetVar} = new Guid(guidBytes);");
                break;
            default:
                if (member.IsEnum)
                {
                    sb.AppendIndentedLine($"{targetVar} = ({typeName}){readerVar}.ReadVarInt32();");
                }
                else
                {
                    // For complex types, deserialize as message
                    sb.AppendIndentedLine($"var fieldLength = {readerVar}.ReadVarInt32();");
                    sb.AppendIndentedLine($"var fieldReader{targetVar} = new SpanReader({readerVar}.GetSlice(fieldLength));");
                    
                    // Special handling for Tuple types - use current namespace instead of System namespace
                    var typeNameSimple = GetSimpleTypeName(typeName);
                    if (typeNameSimple.StartsWith("Tuple<") || typeNameSimple.StartsWith("System.Tuple<"))
                    {
                        sb.AppendIndentedLine($"{targetVar} = SpanReaders.Read{SanitizeTypeNameForMethod(typeName)}(ref fieldReader{targetVar});");
                    }
                    else
                    {
                        sb.AppendIndentedLine($"{targetVar} = global::{member.Namespace}.Serialization.SpanReaders.Read{SanitizeTypeNameForMethod(typeName)}(ref fieldReader{targetVar});");
                    }
                }
                break;
        }
    }

    private static void GenerateMapEntrySerializer(StringBuilderWithIndent sb, TypeDefinition mapEntryType,
        string keyVar, string valueVar, string calculatorVar, WriteTarget writeTarget, 
        string writeTargetShortName, bool isSizeCalculation)
    {
        // Check if value is a collection that needs packed encoding
        var valueMember = mapEntryType.ProtoMembers.FirstOrDefault(m => m.FieldId == 2);
        bool isValueCollection = valueMember != null && valueMember.IsCollection;
        bool usePacked = valueMember != null && valueMember.IsPacked;
        
        if (isSizeCalculation)
        {
            // We're calculating size, use the calculator
            var keyMember = mapEntryType.ProtoMembers.FirstOrDefault(m => m.FieldId == 1);
            if (keyMember != null)
            {
                GenerateFieldSizeCalculation(sb, keyMember, keyVar, calculatorVar);
            }
            
            if (valueMember != null)
            {
                GenerateFieldSizeCalculation(sb, valueMember, valueVar, calculatorVar);
            }
        }
        else
        {
            // We're writing actual data
            string writerVar = "writer";
            
            var keyMember = mapEntryType.ProtoMembers.FirstOrDefault(m => m.FieldId == 1);
            if (keyMember != null)
            {
                // Write key with field ID 1
                // Note: WriteMapKey already writes the tag, so we don't call WritePrecomputedTag here
                WriteMapKey(sb, keyMember.Type, keyVar, writeTarget, writeTargetShortName, keyMember.IsEnum);
            }
            
            if (valueMember != null)
            {
                if (usePacked && valueMember.CollectionElementType != null)
                {
                    // Write packed primitive collection
                    var elementType = valueMember.CollectionElementType;
                    var elementTypeName = GetSimpleTypeName(elementType);
                    
                    // Write field 2 tag for packed data
                    WritePrecomputedTag(sb, 2, WireType.Len, writeTarget, writeTargetShortName);
                    
                    // Calculate and write packed size
                    sb.AppendIndentedLine($"var packedWriter = new WriteSizeCalculator();");
                    sb.AppendIndentedLine($"foreach (var item in {valueVar})");
                    sb.StartNewBlock();
                    AddPrimitiveSizeCalculation(sb, elementTypeName, "item", valueMember.DataFormat, "packedWriter");
                    sb.EndBlock();
                    sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint)packedWriter.Length);");
                    
                    // Write packed values
                    sb.AppendIndentedLine($"foreach (var item in {valueVar})");
                    sb.StartNewBlock();
                    WritePrimitiveValueWithoutTag(sb, elementTypeName, "item", valueMember.DataFormat, writeTarget, writeTargetShortName);
                    sb.EndBlock();
                }
                else
                {
                    // Note: WriteMapValue already writes the tag
                    WriteMapValue(sb, valueMember.Type, valueVar, writeTarget, writeTargetShortName, valueMember.IsEnum);
                }
            }
        }
    }

    private static void WritePrimitiveValueWithoutTag(StringBuilderWithIndent sb, string typeName, string varName, 
        DataFormat dataFormat, WriteTarget writeTarget, string writeTargetShortName)
    {
        // Always use "writer" as the variable name
        string writerVar = "writer";
        
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{writerVar}.WriteFixed32((uint){varName});");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{writerVar}.WriteZigZagVarInt32({varName});");
                else
                    sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint){varName});");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{writerVar}.WriteFixed64((ulong){varName});");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{writerVar}.WriteZigZagVarInt64({varName});");
                else
                    sb.AppendIndentedLine($"{writerVar}.WriteVarUInt64((ulong){varName});");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{writerVar}.WriteBool({varName});");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{writerVar}.WriteFixed32(BitConverter.SingleToUInt32Bits({varName}));");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{writerVar}.WriteFixed64(BitConverter.DoubleToUInt64Bits({varName}));");
                break;
            case "byte":
            case "System.Byte":
            case "Byte":
                sb.AppendIndentedLine($"{writerVar}.WriteSingleByte({varName});");
                break;
            case "char":
            case "System.Char":
            case "Char":
                sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint){varName});");
                break;
            default:
                sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint){varName});");
                break;
        }
    }
    
    private static void GenerateFieldSizeCalculation(StringBuilderWithIndent sb, ProtoMemberAttribute member, 
        string sourceVar, string calculator)
    {
        var typeName = GetClassNameFromFullName(member.Type);
        
        // Handle collections
        if (member.IsCollection && member.CollectionElementType != null)
        {
            var elementType = member.CollectionElementType;
            var elementTypeName = GetSimpleTypeName(elementType);
            
            // Special case for byte arrays - treat as single Len field with raw bytes
            if ((elementTypeName == "byte" || elementTypeName == "Byte" || elementTypeName == "System.Byte") && 
                member.Type.EndsWith("[]"))
            {
                sb.AppendIndentedLine($"if ({sourceVar} != null)");
                sb.StartNewBlock();
                
                // Add field tag for Len wire type
                var byteArrayTagValue = (member.FieldId << 3) | GetWireTypeValue(WireType.Len);
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({byteArrayTagValue}u); // Field {member.FieldId} tag");
                
                // Add length and raw bytes
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){sourceVar}.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength({sourceVar}.Length);");
                
                sb.EndBlock();
                return;
            }
            
            // Check if we should use packed encoding
            if (member.IsPacked && IsPrimitiveType(elementTypeName))
            {
                // Use packed encoding for primitive arrays
                sb.AppendIndentedLine($"if ({sourceVar} != null)");
                sb.StartNewBlock();
                
                // Add field tag for packed data
                var packedTagValue = (member.FieldId << 3) | GetWireTypeValue(WireType.Len);
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({packedTagValue}u); // Field {member.FieldId} tag (packed)");
                
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementTypeName, "item", member.DataFormat, "packedCalc");
                sb.EndBlock();
                
                // Add packed length and data size
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)packedCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(packedCalc.Length);");
                
                sb.EndBlock();
                return;
            }
            
            // For non-packed collections, use repeated fields
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();
            
            // Add tag for each element
            if (elementTypeName == "string" || elementTypeName == "System.String")
            {
                var strTagValue = (member.FieldId << 3) | GetWireTypeValue(WireType.Len);
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({strTagValue}u); // Field {member.FieldId} tag");
                sb.AppendIndentedLine($"{calculator}.WriteString(item);");
            }
            else if (IsPrimitiveType(elementTypeName))
            {
                var primWireType = GetWireTypeForPrimitive(elementTypeName, member.DataFormat);
                var primTagValue = (member.FieldId << 3) | GetWireTypeValue(primWireType);
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({primTagValue}u); // Field {member.FieldId} tag");
                AddPrimitiveSizeCalculation(sb, elementTypeName, "item", member.DataFormat, calculator);
            }
            else
            {
                // Complex type - calculate size as sub-message
                var complexTagValue = (member.FieldId << 3) | GetWireTypeValue(WireType.Len);
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({complexTagValue}u); // Field {member.FieldId} tag");
                sb.AppendIndentedLine($"var itemCalc = new WriteSizeCalculator();");
                var sanitizedType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedType}Size(ref itemCalc, item);");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)itemCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(itemCalc.Length);");
            }
            
            sb.EndBlock();
            return;
        }
        
        // For non-collections, add single tag and value size
        var wireType = GetWireTypeForField(member);
        var tagValue = (member.FieldId << 3) | GetWireTypeValue(wireType);
        sb.AppendIndentedLine($"{calculator}.WriteVarUInt32({tagValue}u); // Field {member.FieldId} tag");
        
        // Check if field is enum - handle as varint
        if (member.IsEnum)
        {
            // Enums are serialized as varints (cast to int)
            sb.AppendIndentedLine($"{calculator}.WriteVarInt32((int){sourceVar});");
        }
        else
        {
            // Add value size (without tag since we already added it)
            WriteMapKeySize(sb, member.Type, sourceVar, calculator);
        }
    }

    private static int GetWireTypeValue(WireType wireType)
    {
        switch (wireType)
        {
            case WireType.VarInt: return 0;
            case WireType.Fixed64b: return 1;
            case WireType.Len: return 2;
            case WireType.Fixed32b: return 5;
            default: return 0;
        }
    }

    private static void AddPrimitiveSizeCalculation(StringBuilderWithIndent sb, string typeName, string varName, 
        DataFormat dataFormat, string calculator)
    {
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{calculator}.AddByteLength(4);");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{calculator}.WriteZigZagVarInt32({varName});");
                else
                    sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){varName});");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"{calculator}.AddByteLength(8);");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"{calculator}.WriteZigZagVarInt64({varName});");
                else
                    sb.AppendIndentedLine($"{calculator}.WriteVarUInt64((ulong){varName});");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"{calculator}.WriteBool({varName});");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(4);");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(8);");
                break;
            case "byte":
            case "System.Byte":
            case "Byte":
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1);");
                break;
            default:
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){varName});");
                break;
        }
    }
    
    private static int GetWireTypeValue(string wireType)
    {
        switch (wireType)
        {
            case "VarInt": return 0;
            case "Fixed64b": return 1;
            case "Len": return 2;
            case "Fixed32b": return 5;
            default: return 0;
        }
    }
    
    private static void GenerateFieldSerializer(StringBuilderWithIndent sb, ProtoMemberAttribute member,
        string sourceVar, WriteTarget writeTarget, string writeTargetShortName)
    {
        var typeName = GetClassNameFromFullName(member.Type);
        
        // Handle collections as repeated fields
        if (member.IsCollection && member.CollectionElementType != null)
        {
            var elementType = member.CollectionElementType;
            var elementTypeName = GetSimpleTypeName(elementType);
            
            // Write each element as a separate field with the same field ID
            sb.AppendIndentedLine($"foreach (var item in {sourceVar})");
            sb.StartNewBlock();
            
            // Write the tag for field 2 with appropriate wire type
            if (elementTypeName == "string" || elementTypeName == "System.String")
            {
                WritePrecomputedTag(sb, member.FieldId, WireType.Len);
                sb.AppendIndentedLine($"writer.WriteString(item);");
            }
            else if (IsPrimitiveType(elementTypeName))
            {
                // Determine wire type for primitive
                var wireType = GetWireTypeForPrimitive(elementTypeName, member.DataFormat);
                WritePrecomputedTag(sb, member.FieldId, wireType);
                WritePrimitiveValue(sb, elementTypeName, "item", member.DataFormat, writeTarget, writeTargetShortName);
            }
            else
            {
                // Complex type - write as sub-message
                WritePrecomputedTag(sb, member.FieldId, WireType.Len);
                sb.AppendIndentedLine($"var itemWriter = new WriteSizeCalculator();");
                sb.AppendIndentedLine($"global::{GetNamespaceFromType(elementType)}.Serialization.Serializers.Serialize{GetClassNameFromFullName(elementType)}(item, ref itemWriter);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)itemWriter.Length);");
                sb.AppendIndentedLine($"global::{GetNamespaceFromType(elementType)}.Serialization.Serializers.Serialize{GetClassNameFromFullName(elementType)}(item, ref writer);");
            }
            
            sb.EndBlock();
            return;
        }
        
        // For non-collections, use the existing logic
        if (member.FieldId == 1)
        {
            // Field 1 is always the key
            WriteMapKey(sb, member.Type, sourceVar, writeTarget, writeTargetShortName);
        }
        else if (member.FieldId == 2)
        {
            // Field 2 is always the value (non-collection)
            WriteMapValue(sb, member.Type, sourceVar, writeTarget, writeTargetShortName);
        }
        else
        {
            // This shouldn't happen for map entries
            throw new InvalidOperationException($"Unexpected field ID {member.FieldId} in map entry");
        }
    }

    private static WireType GetWireTypeForPrimitive(string typeName, DataFormat dataFormat)
    {
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                return dataFormat == DataFormat.FixedSize ? WireType.Fixed32b : WireType.VarInt;
            case "long":
            case "System.Int64":
            case "Int64":
                return dataFormat == DataFormat.FixedSize ? WireType.Fixed64b : WireType.VarInt;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                return WireType.VarInt;
            case "float":
            case "System.Single":
            case "Single":
                return WireType.Fixed32b;
            case "double":
            case "System.Double":
            case "Double":
                return WireType.Fixed64b;
            default:
                return WireType.VarInt;
        }
    }

    private static void WritePrimitiveValue(StringBuilderWithIndent sb, string typeName, string varName, 
        DataFormat dataFormat, WriteTarget writeTarget, string writeTargetShortName)
    {
        switch (typeName)
        {
            case "int":
            case "System.Int32":
            case "Int32":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"writer.WriteFixed32((uint){varName});");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"writer.WriteZigZagVarInt32({varName});");
                else
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){varName});");
                break;
            case "long":
            case "System.Int64":
            case "Int64":
                if (dataFormat == DataFormat.FixedSize)
                    sb.AppendIndentedLine($"writer.WriteFixed64((ulong){varName});");
                else if (dataFormat == DataFormat.ZigZag)
                    sb.AppendIndentedLine($"writer.WriteZigZagVarInt64({varName});");
                else
                    sb.AppendIndentedLine($"writer.WriteVarUInt64((ulong){varName});");
                break;
            case "bool":
            case "System.Boolean":
            case "Boolean":
                sb.AppendIndentedLine($"writer.WriteBool({varName});");
                break;
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"writer.WriteFixed32(BitConverter.SingleToUInt32Bits({varName}));");
                break;
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"writer.WriteFixed64(BitConverter.DoubleToUInt64Bits({varName}));");
                break;
            default:
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){varName});");
                break;
        }
    }
    
    private static string GetWireTypeForField(ProtoMemberAttribute member)
    {
        var typeName = GetClassNameFromFullName(member.Type);
        
        // Collections always use Len wire type
        if (member.IsCollection)
        {
            return "Len";
        }
        
        // Check primitive types
        switch (typeName)
        {
            case "string":
            case "System.String":
            case "byte[]":
            case "System.Byte[]":
                return "Len";
                
            case "bool":
            case "System.Boolean":
            case "int":
            case "System.Int32":
            case "long":
            case "System.Int64":
            case "uint":
            case "System.UInt32":
            case "ulong":
            case "System.UInt64":
            case "sbyte":
            case "System.SByte":
            case "short":
            case "System.Int16":
            case "ushort":
            case "System.UInt16":
                if (member.DataFormat == DataFormat.FixedSize)
                {
                    if (typeName.Contains("64"))
                        return "Fixed64b";
                    else
                        return "Fixed32b";
                }
                return "VarInt";
                
            case "float":
            case "System.Single":
                return "Fixed32b";
                
            case "double":
            case "System.Double":
                return "Fixed64b";
                
            default:
                // Complex types and enums
                if (member.IsEnum)
                    return "VarInt";
                return "Len";
        }
    }
    
    private static bool IsEnumType(string typeName)
    {
        // This would need to check against the actual enum types in the codebase
        // For now, return false as a placeholder
        return false;
    }
    
    /// <summary>
    /// Writes the reader logic for map key/value fields
    /// </summary>
    /// <summary>
    /// Generates deserialization code for array values in maps
    /// </summary>
    private static void GenerateMapValueArrayDeserializer(StringBuilderWithIndent sb, string varName, string elementType)
    {
        var typeName = GetClassNameFromFullName(elementType);
        
        // For string arrays, each string comes as a separate field 2 with Len wire type
        if (typeName == "string" || typeName == "System.String")
        {
            // String array handling is done in WriteMapProtoMember directly
            // This method should not be called for string arrays anymore
            return;
        }
        
        // For non-string arrays, they are packed in a single field
        sb.AppendIndentedLine($"// Deserialize array value");
        sb.AppendIndentedLine($"var fieldLength = entryReader.ReadVarInt32();");
        sb.AppendIndentedLine($"var fieldReader = new SpanReader(entryReader.GetSlice(fieldLength));");
        
        // Use list to collect items
        sb.AppendIndentedLine($"var itemsList = new List<{elementType}>();");
        sb.AppendIndentedLine($"while (!fieldReader.IsEnd)");
        sb.StartNewBlock();
        
        if (typeName == "double" || typeName == "System.Double" || typeName == "Double")
        {
            // Double uses fixed 64-bit encoding
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.ReadFixedDouble());");
        }
        else if (typeName == "float" || typeName == "System.Single" || typeName == "Single")
        {
            // Float uses fixed 32-bit encoding
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.ReadFixedFloat());");
        }
        else if (IsPrimitiveType(typeName))
        {
            // For other primitive types, read directly
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.Read{GetReaderMethodName(typeName)}());");
        }
        else
        {
            // For complex types, read as sub-message
            sb.AppendIndentedLine($"var len = fieldReader.ReadVarInt32();");
            sb.AppendIndentedLine($"var subReader = new SpanReader(fieldReader.GetSlice(len));");
            sb.AppendIndentedLine($"itemsList.Add(global::{GetNamespaceFromType(elementType)}.Serialization.SpanReaders.Read{typeName}(ref subReader));");
        }
        
        sb.EndBlock();
        
        // Convert to array
        sb.AppendIndentedLine($"{varName} = itemsList.ToArray();");
    }
    
    /// <summary>
    /// Generates deserialization code for collection values in maps
    /// </summary>
    private static void GenerateMapValueCollectionDeserializer(StringBuilderWithIndent sb, string varName, string collectionType)
    {
        // Extract element type from collection
        string elementType = null;
        bool isHashSet = false;
        
        if (collectionType.StartsWith("HashSet<") || collectionType.StartsWith("System.Collections.Generic.HashSet<"))
        {
            isHashSet = true;
            var start = collectionType.IndexOf('<') + 1;
            var end = collectionType.LastIndexOf('>');
            elementType = collectionType.Substring(start, end - start);
        }
        else if (collectionType.StartsWith("List<") || collectionType.StartsWith("System.Collections.Generic.List<"))
        {
            var start = collectionType.IndexOf('<') + 1;
            var end = collectionType.LastIndexOf('>');
            elementType = collectionType.Substring(start, end - start);
        }
        
        var typeName = GetClassNameFromFullName(elementType);
        
        // For string collections, each string comes as a separate field 2 with Len wire type
        // So we DON'T read them here - they're handled in the WriteMapProtoMember
        if (typeName == "string" || typeName == "System.String")
        {
            // String collection handling is done in WriteMapProtoMember directly
            // This method should not be called for string collections anymore
            return;
        }
        
        // For non-string collections, they are packed in a single field
        sb.AppendIndentedLine($"// Deserialize collection value");
        sb.AppendIndentedLine($"var fieldLength = entryReader.ReadVarInt32();");
        sb.AppendIndentedLine($"var fieldReader = new SpanReader(entryReader.GetSlice(fieldLength));");
        
        // Use list to collect items
        sb.AppendIndentedLine($"var itemsList = new List<{elementType}>();");
        sb.AppendIndentedLine($"while (!fieldReader.IsEnd)");
        sb.StartNewBlock();
        
        if (typeName == "double" || typeName == "System.Double" || typeName == "Double")
        {
            // Double uses fixed 64-bit encoding
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.ReadFixedDouble());");
        }
        else if (typeName == "float" || typeName == "System.Single" || typeName == "Single")
        {
            // Float uses fixed 32-bit encoding
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.ReadFixedFloat());");
        }
        else if (IsPrimitiveType(typeName))
        {
            // For other primitive types, read directly without wire type
            sb.AppendIndentedLine($"itemsList.Add(fieldReader.Read{GetReaderMethodName(typeName)}());");
        }
        else
        {
            // For complex types, read as sub-message
            sb.AppendIndentedLine($"var len = fieldReader.ReadVarInt32();");
            sb.AppendIndentedLine($"var subReader = new SpanReader(fieldReader.GetSlice(len));");
            sb.AppendIndentedLine($"itemsList.Add(global::{GetNamespaceFromType(elementType)}.Serialization.SpanReaders.Read{typeName}(ref subReader));");
        }
        
        sb.EndBlock();
        
        // Convert to appropriate collection type
        if (isHashSet)
        {
            sb.AppendIndentedLine($"{varName} = new HashSet<{elementType}>(itemsList);");
        }
        else
        {
            sb.AppendIndentedLine($"{varName} = itemsList;");
        }
    }
    
    private static string GetReaderMethodName(string typeName)
    {
        return typeName switch
        {
            "int" or "System.Int32" or "Int32" => "VarInt32",
            "long" or "System.Int64" or "Int64" => "VarInt64",
            "float" or "System.Single" or "Single" => "Float",
            "double" or "System.Double" or "Double" => "Double",
            "bool" or "System.Boolean" or "Boolean" => "Bool",
            "byte" or "System.Byte" or "Byte" => "Byte",
            "sbyte" or "System.SByte" or "SByte" => "SByte",
            "short" or "System.Int16" or "Int16" => "Int16",
            "ushort" or "System.UInt16" or "UInt16" => "UInt16",
            "uint" or "System.UInt32" or "UInt32" => "VarUInt32",
            "ulong" or "System.UInt64" or "UInt64" => "VarUInt64",
            _ => throw new NotSupportedException($"Unknown primitive type: {typeName}")
        };
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
        else if (elementType.Contains("KeyValuePair<"))
        {
            // Special handling for List<KeyValuePair<K,V>> - treat as map entries
            WriteOptimizedLoop(sb, protoMember, objectName, (loopSb) => {
                // Extract key and value types from KeyValuePair<K,V>
                var genericStart = elementType.IndexOf('<');
                var genericEnd = elementType.LastIndexOf('>');
                if (genericStart > 0 && genericEnd > genericStart)
                {
                    var genericArgs = elementType.Substring(genericStart + 1, genericEnd - genericStart - 1);
                    var commaIndex = FindTopLevelComma(genericArgs);
                    if (commaIndex > 0)
                    {
                        var keyType = genericArgs.Substring(0, commaIndex).Trim();
                        var valueType = genericArgs.Substring(commaIndex + 1).Trim();
                        
                        // Write as a map entry with key (field 1) and value (field 2)
                        loopSb.AppendIndentedLine($"var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                        
                        // Calculate key size with field tag
                        loopSb.AppendIndentedLine($"entryCalc.WriteVarUInt32(10u); // Field 1 tag");
                        WriteMapKeySize(loopSb, keyType, "item.Key", "entryCalc");
                        
                        // Calculate value size with field tag
                        loopSb.AppendIndentedLine($"entryCalc.WriteVarUInt32(18u); // Field 2 tag");
                        WriteMapValueSize(loopSb, valueType, "item.Value", "entryCalc");
                        
                        // Write the entry
                        WritePrecomputedTag(loopSb, protoMember.FieldId, WireType.Len);
                        loopSb.AppendIndentedLine($"writer.WriteVarUInt32((uint)entryCalc.Length);");
                        
                        // Write key
                        WriteMapKey(loopSb, keyType, "item.Key", writeTarget, writeTargetShortName);
                        
                        // Write value
                        WriteMapValue(loopSb, valueType, "item.Value", writeTarget, writeTargetShortName);
                    }
                }
            });
        }
        else
        {
            // Custom message types - each message gets its own tag + length + serialized content
            WriteOptimizedLoop(sb, protoMember, objectName, (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                loopSb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref calculator, item);");
                WritePrecomputedTag(loopSb, protoMember.FieldId, WireType.Len);
                loopSb.AppendIndentedLine($"writer.WriteVarUInt32((uint)calculator.Length);");
                loopSb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedElementType}(ref writer, item);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {sanitizedElementType} was null; this might be as contents in a list/array\");");
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
        else if (elementType.Contains("KeyValuePair<"))
        {
            // Special handling for List<KeyValuePair<K,V>> - treat as map entries
            WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                // Extract key and value types from KeyValuePair<K,V>
                var genericStart = elementType.IndexOf('<');
                var genericEnd = elementType.LastIndexOf('>');
                if (genericStart > 0 && genericEnd > genericStart)
                {
                    var genericArgs = elementType.Substring(genericStart + 1, genericEnd - genericStart - 1);
                    var commaIndex = FindTopLevelComma(genericArgs);
                    if (commaIndex > 0)
                    {
                        var keyType = genericArgs.Substring(0, commaIndex).Trim();
                        var valueType = genericArgs.Substring(commaIndex + 1).Trim();
                        
                        // Calculate size as a map entry with key (field 1) and value (field 2)
                        WritePrecomputedTagForCalculator(loopSb, protoMember.FieldId, WireType.Len);
                        loopSb.AppendIndentedLine($"var entryCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                        
                        // Calculate key size
                        loopSb.AppendIndentedLine($"entryCalc.WriteVarUInt32(10u); // Field 1 tag");
                        WriteMapKeySize(loopSb, keyType, "item.Key", "entryCalc");
                        
                        // Calculate value size
                        loopSb.AppendIndentedLine($"entryCalc.WriteVarUInt32(18u); // Field 2 tag");
                        WriteMapValueSize(loopSb, valueType, "item.Value", "entryCalc");
                        
                        loopSb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)entryCalc.Length);");
                        loopSb.AppendIndentedLine($"calculator.AddByteLength(entryCalc.Length);");
                    }
                }
            });
        }
        else
        {
            // Custom message types - each message gets tag + length + content
            WriteOptimizedLoop(sb, protoMember, "obj", (loopSb) => {
                loopSb.AppendIndentedLine($"if (item != null)");
                loopSb.StartNewBlock();
                WritePrecomputedTagForCalculator(loopSb, protoMember.FieldId, WireType.Len);
                loopSb.AppendIndentedLine($"var itemCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                loopSb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref itemCalculator, item);");
                loopSb.AppendIndentedLine($"calculator.WriteVarUInt32((uint)itemCalculator.Length);");
                loopSb.AppendIndentedLine($"calculator.AddByteLength(itemCalculator.Length);");
                loopSb.EndBlock();
                loopSb.AppendIndentedLine($"else");
                loopSb.StartNewBlock();
                loopSb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {sanitizedElementType} was null; this might be as contents in a list/array\");");
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
        sb.AppendIndentedLine($"if (obj.{protoMember.Name} != null)");
        sb.StartNewBlock();
        
        // Create a virtual TypeDefinition for the map entry message
        var mapEntryType = CreateMapEntryTypeDefinition(protoMember.MapKeyType, protoMember.MapValueType, protoMember.MapKeyIsEnum, protoMember.MapValueIsEnum);
        
        // Calculate size for each map entry
        sb.AppendIndentedLine($"foreach (var kvp in obj.{protoMember.Name})");
        sb.StartNewBlock();
        
        // Each entry is a message with field 1 (key) and field 2 (value)
        sb.AppendIndentedLine($"var entryCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
        
        // Generate size calculation for map entry fields using unified approach
        GenerateMapEntrySerializer(sb, mapEntryType, "kvp.Key", "kvp.Value", 
                                  "entryCalculator", WriteTarget.Stream, "stream", true);
        
        // Add the entry tag and length-delimited size
        var tagValue = (protoMember.FieldId << 3) | 2; // wire type Len = 2
        sb.AppendIndentedLine($"calculator.WriteVarUInt32({tagValue}u); // Field {protoMember.FieldId} tag");
        sb.AppendIndentedLine($"calculator.WriteVarInt32(entryCalculator.Length);");
        sb.AppendIndentedLine($"calculator.AddByteLength(entryCalculator.Length);");
        
        sb.EndBlock(); // end foreach
        sb.EndBlock(); // end if not null
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
            case "char":
            case "System.Char":
            case "Char":
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
        
        // Check if it's an array type
        if (fieldType.EndsWith("[]"))
        {
            var elementType = fieldType.Substring(0, fieldType.Length - 2);
            sb.AppendIndentedLine($"var arrayCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            
            sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
            sb.StartNewBlock();
            
            if (IsPrimitiveType(GetSimpleTypeName(elementType)) || elementType == "string" || elementType == "System.String")
            {
                WriteElementSizeCalculation(sb, elementType, "item", "arrayCalc");
            }
            else
            {
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref arrayCalc, item);");
            }
            sb.EndBlock();
            
            sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)arrayCalc.Length);");
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(arrayCalc.Length);");
            return;
        }
        
        // Check if it's a generic collection type
        if (typeName.Contains("<"))
        {
            var genericStart = typeName.IndexOf('<');
            var genericEnd = typeName.LastIndexOf('>');
            var baseType = typeName.Substring(0, genericStart);
            var elementType = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);
            
            sb.AppendIndentedLine($"var collectionCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
            
            sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
            sb.StartNewBlock();
            
            if (IsPrimitiveType(GetSimpleTypeName(elementType)) || elementType == "string" || elementType == "System.String")
            {
                WriteElementSizeCalculation(sb, elementType, "item", "collectionCalc");
            }
            else
            {
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref collectionCalc, item);");
            }
            sb.EndBlock();
            
            sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)collectionCalc.Length);");
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(collectionCalc.Length);");
            return;
        }
        
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
            case "char":
            case "System.Char":
            case "Char":
                sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint){valueAccess});");
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
                var sanitizedTypeName = SanitizeTypeNameForMethod(fieldType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref valueCalc, {valueAccess});");
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
            "char" or "System.Char" => "reader.ReadPackedVarIntCharArray()",
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
        
        // Create a virtual TypeDefinition for the map entry message
        var mapEntryType = CreateMapEntryTypeDefinition(protoMember.MapKeyType, protoMember.MapValueType, protoMember.MapKeyIsEnum, protoMember.MapValueIsEnum);
        
        // Maps are serialized as repeated message entries
        // Each entry has field 1 for key and field 2 for value
        sb.AppendIndentedLine($"foreach (var kvp in {objectName}.{protoMember.Name})");
        sb.StartNewBlock();
        
        // Skip entries with null values for reference types (protobuf standard)
        var valueType = protoMember.MapValueType;
        var valueTypeName = GetClassNameFromFullName(valueType);
        if (!IsPrimitiveType(valueTypeName) && !valueType.EndsWith("[]"))
        {
            // For reference types (including string), skip if value is null
            sb.AppendIndentedLine($"if (kvp.Value == null) continue;");
        }
        
        // Calculate entry size using the unified approach
        sb.AppendIndentedLine($"var entryCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
        
        // Generate size calculation for map entry fields
        GenerateMapEntrySerializer(sb, mapEntryType, "kvp.Key", "kvp.Value", 
                                  "entryCalculator", writeTarget, writeTargetShortName, true);
        
        // Write the entry tag and length
        WritePrecomputedTag(sb, protoMember.FieldId, WireType.Len, writeTarget, writeTargetShortName);
        sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)entryCalculator.Length);");
        
        // Write the actual key and value
        GenerateMapEntrySerializer(sb, mapEntryType, "kvp.Key", "kvp.Value", 
                                  null, writeTarget, writeTargetShortName, false);
        
        sb.EndBlock(); // end foreach
        sb.EndBlock(); // end if not null
    }
    
    private static void WriteMapKeySize(StringBuilderWithIndent sb, string keyType, string keyAccess, string calculator)
    {
        // Key is always field 1
        var simpleType = GetSimpleTypeName(keyType);
        
        // Check if it's an array type
        if (simpleType.EndsWith("[]"))
        {
            var elementType = simpleType.Substring(0, simpleType.Length - 2);
            
            // Special case for byte arrays - single Len field with raw bytes (no tag, already added by caller)
            if (elementType == "byte" || elementType == "System.Byte" || elementType == "Byte")
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyAccess}.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength({keyAccess}.Length);");
                sb.EndBlock();
                return;
            }
            
            // For int arrays, use packed encoding size calculation
            if (elementType == "int" || elementType == "Int32" || elementType == "int32" || 
                elementType == "long" || elementType == "Int64" || elementType == "int64" ||
                elementType == "uint" || elementType == "UInt32" || elementType == "uint32" ||
                elementType == "ulong" || elementType == "UInt64" || elementType == "uint64")
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementType, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                // Add length varint + packed content
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)packedCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(packedCalc.Length);");
                sb.EndBlock();
                return;
            }
            
            // For other array keys (strings, custom types), calculate size of all elements as repeated fields
            sb.AppendIndentedLine($"if ({keyAccess} != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"foreach (var keyItem in {keyAccess})");
            sb.StartNewBlock();
            
            // Check if element is string
            if (elementType == "string" || elementType == "System.String" || elementType == "String")
            {
                // For strings
                sb.AppendIndentedLine($"if (keyItem != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // field 1, Len tag");
                sb.AppendIndentedLine($"{calculator}.WriteString(keyItem);");
                sb.EndBlock();
            }
            else
            {
                // For custom types
                sb.AppendIndentedLine($"if (keyItem != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // field 1, Len tag");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                var keyItemCalcName = $"keyItemCalc{sanitizedElementType}";
                sb.AppendIndentedLine($"var {keyItemCalcName} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref {keyItemCalcName}, keyItem);");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyItemCalcName}.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength({keyItemCalcName}.Length);");
                sb.EndBlock();
            }
            
            sb.EndBlock();
            sb.EndBlock();
            return;
        }
        
        // Check if it's a generic collection type (List<T>, HashSet<T>, etc.) - BUT NOT Tuples
        if (simpleType.Contains("<") && !simpleType.StartsWith("Tuple<") && !simpleType.StartsWith("System.Tuple<"))
        {
            var genericStart = simpleType.IndexOf('<');
            var genericEnd = simpleType.LastIndexOf('>');
            var baseType = simpleType.Substring(0, genericStart);
            var elementType = simpleType.Substring(genericStart + 1, genericEnd - genericStart - 1);
            
            // Check if it's a numeric primitive for packed encoding (NOT string!)
            bool isNumericPrimitive = elementType switch
            {
                "int" or "Int32" or "int32" => true,
                "long" or "Int64" or "int64" => true,
                "uint" or "UInt32" or "uint32" => true,
                "ulong" or "UInt64" or "uint64" => true,
                "float" or "Single" => true,
                "double" or "Double" => true,
                "bool" or "Boolean" => true,
                "byte" or "Byte" => true,
                "sbyte" or "SByte" => true,
                "short" or "Int16" => true,
                "ushort" or "UInt16" => true,
                _ => false
            };
            
            // For numeric primitive collections, use packed encoding
            if (isNumericPrimitive)
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementType, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                // Add length varint + packed content
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)packedCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(packedCalc.Length);");
                sb.EndBlock();
            }
            else
            {
                // For collections with strings or custom types, calculate size of all elements as repeated fields
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"foreach (var keyItem in {keyAccess})");
                sb.StartNewBlock();
                
                if (elementType == "string" || elementType == "System.String" || elementType == "String")
                {
                    // For strings
                    sb.AppendIndentedLine($"if (keyItem != null)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // field 1, Len tag");
                    sb.AppendIndentedLine($"{calculator}.WriteString(keyItem);");
                    sb.EndBlock();
                }
                else
                {
                    // For custom types
                    sb.AppendIndentedLine($"if (keyItem != null)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"{calculator}.AddByteLength(1); // field 1, Len tag");
                    var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                    var keyItemCalcName = $"keyItemCalc{sanitizedElementType}";
                    sb.AppendIndentedLine($"var {keyItemCalcName} = new global::GProtobuf.Core.WriteSizeCalculator();");
                    sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref {keyItemCalcName}, keyItem);");
                    sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyItemCalcName}.Length);");
                    sb.AppendIndentedLine($"{calculator}.AddByteLength({keyItemCalcName}.Length);");
                    sb.EndBlock();
                }
                
                sb.EndBlock();
                sb.EndBlock();
            }
            return;
        }
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.WriteString({keyAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt32({keyAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt64({keyAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"{calculator}.WriteBool({keyAccess});");
                break;
                
            case "byte":
            case "Byte":
            case "System.Byte":
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyAccess});");
                break;
                
            case "char":
            case "Char":
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyAccess});");
                break;
                
            case "uint":
            case "UInt32":
                sb.AppendIndentedLine($"{calculator}.WriteUInt32({keyAccess});");
                break;
                
            case "ulong":
            case "UInt64":
                sb.AppendIndentedLine($"{calculator}.WriteUInt64({keyAccess});");
                break;
                
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"{calculator}.WriteDouble({keyAccess});");
                break;
                
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"{calculator}.WriteFloat({keyAccess});");
                break;
                
            case "Guid":
            case "System.Guid":
                // BCL Guid format: 2 fixed64 fields = 2*(1+8) = 18 bytes
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32(18u);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(18);");
                break;
                
            default:
                // For complex types (custom classes)
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                var sanitizedTypeName = SanitizeTypeNameForMethod(keyType);
                var keyCalcName = $"keyCalc{sanitizedTypeName}";
                sb.AppendIndentedLine($"var {keyCalcName} = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref {keyCalcName}, {keyAccess});");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){keyCalcName}.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength({keyCalcName}.Length);");
                sb.EndBlock();
                break;
        }
    }
    
    private static void WriteMapValueSize(StringBuilderWithIndent sb, string valueType, string valueAccess, string calculator)
    {
        // Value is always field 2
        var simpleType = GetSimpleTypeName(valueType);
        
        // Check if it's an array type
        if (valueType.EndsWith("[]"))
        {
            var elementType = valueType.Substring(0, valueType.Length - 2);
            var elementTypeName = GetSimpleTypeName(elementType);
            
            sb.AppendIndentedLine($"if ({valueAccess} != null)");
            sb.StartNewBlock();
            
            if (elementTypeName == "string" || elementTypeName == "System.String")
            {
                // For string arrays, each string needs its own tag - but that's handled in GenerateFieldSizeCalculation
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                // Tag is already added in GenerateFieldSizeCalculation for string collections
                sb.AppendIndentedLine($"{calculator}.WriteString(item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            else if (IsPrimitiveType(elementTypeName))
            {
                // For primitive arrays, we can pack them
                sb.AppendIndentedLine($"var arrayCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                WriteElementSizeCalculation(sb, elementType, "item", "arrayCalc");
                sb.EndBlock();
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)arrayCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(arrayCalc.Length);");
            }
            else
            {
                // For custom type arrays, each needs its own tag and size
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                // Tag already added in GenerateFieldSizeCalculation
                sb.AppendIndentedLine($"var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref itemCalc, item);");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)itemCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(itemCalc.Length);");
                sb.EndBlock();
                sb.EndBlock();
            }
            
            sb.EndBlock();
            return;
        }
        
        // Check if it's a collection type (HashSet, List, etc.)
        if (simpleType.Contains("<"))
        {
            var genericStart = simpleType.IndexOf('<');
            var genericEnd = simpleType.LastIndexOf('>');
            var baseType = simpleType.Substring(0, genericStart);
            var elementType = simpleType.Substring(genericStart + 1, genericEnd - genericStart - 1);
            var elementTypeName = GetSimpleTypeName(elementType);
            
            sb.AppendIndentedLine($"if ({valueAccess} != null)");
            sb.StartNewBlock();
            
            if (elementTypeName == "string" || elementTypeName == "System.String")
            {
                // For string collections, each string needs its own tag
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.WriteString(item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            else if (IsPrimitiveType(elementTypeName))
            {
                // For primitive collections, we can pack them
                sb.AppendIndentedLine($"var collectionCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                WriteElementSizeCalculation(sb, elementType, "item", "collectionCalc");
                sb.EndBlock();
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)collectionCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(collectionCalc.Length);");
            }
            else
            {
                // For custom type collections (List<CustomNested>, etc.), each needs its own tag and size - same as arrays
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                // Tag already added in GenerateFieldSizeCalculation
                sb.AppendIndentedLine($"var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref itemCalc, item);");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)itemCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(itemCalc.Length);");
                sb.EndBlock();
                sb.EndBlock();
            }
            
            sb.EndBlock();
            return;
        }
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{calculator}.WriteString({valueAccess});");
                sb.EndBlock();
                break;
                
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt32({valueAccess});");
                break;
                
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt64({valueAccess});");
                break;
                
            case "bool":
                sb.AppendIndentedLine($"{calculator}.WriteBool({valueAccess});");
                break;
                
            case "byte":
            case "Byte":
            case "System.Byte":
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){valueAccess});");
                break;
                
            case "char":
            case "Char":
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){valueAccess});");
                break;
                
            case "float":
                sb.AppendIndentedLine($"{calculator}.WriteFloat({valueAccess});");
                break;
                
            case "double":
                sb.AppendIndentedLine($"{calculator}.WriteDouble({valueAccess});");
                break;
                
            case "Guid":
            case "System.Guid":
                // Guid: 2 fixed64 fields = (1+8)+(1+8) = 18 bytes
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32(18u);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(18);");
                break;
                
            default:
                // For complex types, we need to calculate their size
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var valueCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedTypeName = SanitizeTypeNameForMethod(valueType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref valueCalc, {valueAccess});");
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint)valueCalc.Length);");
                sb.AppendIndentedLine($"{calculator}.AddByteLength(valueCalc.Length);");
                sb.EndBlock();
                break;
        }
    }
    
    private static void WriteElementSizeCalculation(StringBuilderWithIndent sb, string elementType, string elementAccess, string calculator)
    {
        var simpleType = GetSimpleTypeName(elementType);
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({elementAccess} != null)");
                sb.AppendIndentedLine($"    {calculator}.WriteString({elementAccess});");
                break;
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt32({elementAccess});");
                break;
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"{calculator}.WriteVarInt64({elementAccess});");
                break;
            case "bool":
                sb.AppendIndentedLine($"{calculator}.WriteBool({elementAccess});");
                break;
            case "float":
                sb.AppendIndentedLine($"{calculator}.WriteFloat({elementAccess});");
                break;
            case "double":
                sb.AppendIndentedLine($"{calculator}.WriteDouble({elementAccess});");
                break;
            case "byte":
                sb.AppendIndentedLine($"{calculator}.WriteByte({elementAccess});");
                break;
            case "char":
            case "Char":
                sb.AppendIndentedLine($"{calculator}.WriteVarUInt32((uint){elementAccess});");
                break;
            default:
                var sanitizedTypeName = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref {calculator}, {elementAccess});");
                break;
        }
    }
    
    private static void WriteMapKey(StringBuilderWithIndent sb, string keyType, string keyAccess, WriteTarget writeTarget, string writeTargetShortName, bool isEnum = false)
    {
        // Check if it's an enum first
        if (isEnum)
        {
            sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
            sb.AppendIndentedLine($"writer.WriteVarInt32((int){keyAccess});");
            return;
        }
        
        var simpleType = GetSimpleTypeName(keyType);
        
        // Check if it's an array type
        if (simpleType.EndsWith("[]"))
        {
            var elementType = simpleType.Substring(0, simpleType.Length - 2);
            
            // Special case for byte arrays - write as single Len field with raw bytes
            if (elementType == "byte" || elementType == "System.Byte" || elementType == "Byte")
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){keyAccess}.Length);");
                sb.AppendIndentedLine($"writer.WriteBytes({keyAccess});");
                sb.EndBlock();
                return;
            }
            
            // For int arrays, use packed encoding for protobuf-net compatibility
            if (elementType == "int" || elementType == "Int32" || elementType == "int32" || 
                elementType == "long" || elementType == "Int64" || elementType == "int64" ||
                elementType == "uint" || elementType == "UInt32" || elementType == "uint32" ||
                elementType == "ulong" || elementType == "UInt64" || elementType == "uint64")
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len (packed)");
                
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementType, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                
                // Write packed length
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedCalc.Length);");
                
                // Write packed values
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                WritePrimitiveValueWithoutTag(sb, elementType, "item", DataFormat.Default, writeTarget, writeTargetShortName);
                sb.EndBlock();
                sb.EndBlock();
                return;
            }
            
            // For other array keys (strings, custom types), treat them as repeated fields within the map entry
            sb.AppendIndentedLine($"if ({keyAccess} != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"// Array key - write as repeated field 1");
            sb.AppendIndentedLine($"foreach (var keyItem in {keyAccess})");
            sb.StartNewBlock();
            
            // Check if element is string
            if (elementType == "string" || elementType == "System.String" || elementType == "String")
            {
                // For strings
                sb.AppendIndentedLine($"if (keyItem != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                sb.AppendIndentedLine($"writer.WriteString(keyItem);");
                sb.EndBlock();
            }
            else
            {
                // For custom types
                sb.AppendIndentedLine($"if (keyItem != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                sb.AppendIndentedLine($"var keyItemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref keyItemCalc, keyItem);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)keyItemCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedElementType}(ref writer, keyItem);");
                sb.EndBlock();
            }
            
            sb.EndBlock();
            sb.EndBlock();
            return;
        }
        
        // Check if it's a generic collection type (List<T>, HashSet<T>, etc.) - BUT NOT Tuples
        if (simpleType.Contains("<") && !simpleType.StartsWith("Tuple<") && !simpleType.StartsWith("System.Tuple<"))
        {
            var genericStart = simpleType.IndexOf('<');
            var genericEnd = simpleType.LastIndexOf('>');
            var baseType = simpleType.Substring(0, genericStart);
            var elementType = simpleType.Substring(genericStart + 1, genericEnd - genericStart - 1);
            
            // Check if it's a numeric primitive for packed encoding (NOT string!)
            bool isNumericPrimitive = elementType switch
            {
                "int" or "Int32" or "int32" => true,
                "long" or "Int64" or "int64" => true,
                "uint" or "UInt32" or "uint32" => true,
                "ulong" or "UInt64" or "uint64" => true,
                "float" or "Single" => true,
                "double" or "Double" => true,
                "bool" or "Boolean" => true,
                "byte" or "Byte" => true,
                "sbyte" or "SByte" => true,
                "short" or "Int16" => true,
                "ushort" or "UInt16" => true,
                _ => false
            };
            
            // For numeric primitive collections, use packed encoding
            if (isNumericPrimitive)
            {
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len (packed)");
                
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementType, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                
                // Write packed length
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedCalc.Length);");
                
                // Write packed values
                sb.AppendIndentedLine($"foreach (var item in {keyAccess})");
                sb.StartNewBlock();
                WritePrimitiveValueWithoutTag(sb, elementType, "item", DataFormat.Default, writeTarget, writeTargetShortName);
                sb.EndBlock();
                sb.EndBlock();
            }
            else
            {
                // For collections with strings or custom types, treat them as repeated fields
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"// Collection key - write as repeated field 1");
                sb.AppendIndentedLine($"foreach (var keyItem in {keyAccess})");
                sb.StartNewBlock();
                
                if (elementType == "string" || elementType == "System.String" || elementType == "String")
                {
                    // For strings
                    sb.AppendIndentedLine($"if (keyItem != null)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                    sb.AppendIndentedLine($"writer.WriteString(keyItem);");
                    sb.EndBlock();
                }
                else
                {
                    // For custom types
                    sb.AppendIndentedLine($"if (keyItem != null)");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                    sb.AppendIndentedLine($"var keyItemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                    var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                    sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref keyItemCalc, keyItem);");
                    sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)keyItemCalc.Length);");
                    sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedElementType}(ref writer, keyItem);");
                    sb.EndBlock();
                }
                
                sb.EndBlock();
                sb.EndBlock();
            }
            return;
        }
        
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
                
            case "byte":
            case "Byte":
            case "System.Byte":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){keyAccess});");
                break;
                
            case "char":
            case "Char":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x08); // field 1, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){keyAccess});");
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
                
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x09); // field 1, Fixed64");
                sb.AppendIndentedLine($"writer.WriteDouble({keyAccess});");
                break;
                
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0D); // field 1, Fixed32");
                sb.AppendIndentedLine($"writer.WriteFloat({keyAccess});");
                break;
                
            case "Guid":
            case "System.Guid":
                // Guid is written as message with 2 fixed64 fields (protobuf-net format)
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // Guid: 2 fixed64 fields = (1+8)+(1+8) = 18 bytes");
                sb.AppendIndentedLine($"// Convert Guid to protobuf-net format (2 fixed64 fields)");
                // Create unique variable name based on keyAccess
                var guidVarName = keyAccess.Replace(".", "_").Replace("[", "_").Replace("]", "_").Replace("(", "_").Replace(")", "_") + "_guidBytes";
                sb.AppendIndentedLine($"var {guidVarName} = {keyAccess}.ToByteArray();");
                sb.AppendIndentedLine($"// Field 1: first 8 bytes (fixed64)");
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x09); // field 1, Fixed64");
                sb.AppendIndentedLine($"writer.WriteFixed64(System.BitConverter.ToUInt64({guidVarName}, 0));");
                sb.AppendIndentedLine($"// Field 2: last 8 bytes (fixed64)");
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x11); // field 2, Fixed64");
                sb.AppendIndentedLine($"writer.WriteFixed64(System.BitConverter.ToUInt64({guidVarName}, 8));");
                break;
                
            default:
                // For complex types (custom classes)
                sb.AppendIndentedLine($"if ({keyAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x0A); // field 1, Len");
                sb.AppendIndentedLine($"var keyCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedTypeName = SanitizeTypeNameForMethod(keyType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref keyCalc, {keyAccess});");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)keyCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedTypeName}(ref writer, {keyAccess});");
                sb.EndBlock();
                break;
        }
    }
    
    private static void WriteMapValue(StringBuilderWithIndent sb, string valueType, string valueAccess, WriteTarget writeTarget, string writeTargetShortName, bool isEnum = false)
    {
        // Check if it's an enum first
        if (isEnum)
        {
            sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
            sb.AppendIndentedLine($"writer.WriteVarInt32((int){valueAccess});");
            return;
        }
        
        var simpleType = GetSimpleTypeName(valueType);
        
        // Check if it's an array type
        if (valueType.EndsWith("[]"))
        {
            var elementType = valueType.Substring(0, valueType.Length - 2);
            var elementTypeName = GetSimpleTypeName(elementType);
            
            sb.AppendIndentedLine($"if ({valueAccess} != null)");
            sb.StartNewBlock();
            
            // For string arrays, each string needs its own tag
            if (elementTypeName == "string" || elementTypeName == "System.String")
            {
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"writer.WriteString(item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            else if (IsPrimitiveType(elementTypeName))
            {
                // For primitive arrays, write as packed
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementTypeName, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedCalc.Length);");
                
                // Write packed data
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                WritePrimitiveValueWithoutTag(sb, elementTypeName, "item", DataFormat.Default, writeTarget, writeTargetShortName);
                sb.EndBlock();
            }
            else
            {
                // Complex types - write as repeated fields
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref itemCalc, item);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)itemCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedElementType}(ref writer, item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            
            sb.EndBlock();
            return;
        }
        
        // Check if it's a generic collection type (List<T>, HashSet<T>, etc.) - BUT NOT Tuples
        if (simpleType.Contains("<") && !simpleType.StartsWith("Tuple<") && !simpleType.StartsWith("System.Tuple<"))
        {
            var genericStart = simpleType.IndexOf('<');
            var genericEnd = simpleType.LastIndexOf('>');
            var baseType = simpleType.Substring(0, genericStart);
            var elementType = simpleType.Substring(genericStart + 1, genericEnd - genericStart - 1);
            
            sb.AppendIndentedLine($"if ({valueAccess} != null)");
            sb.StartNewBlock();
            
            // Check if it's a numeric primitive for packed encoding (NOT string!)
            bool isNumericPrimitive = elementType switch
            {
                "int" or "Int32" or "int32" => true,
                "long" or "Int64" or "int64" => true,
                "uint" or "UInt32" or "uint32" => true,
                "ulong" or "UInt64" or "uint64" => true,
                "float" or "Single" => true,
                "double" or "Double" => true,
                "bool" or "Boolean" => true,
                "byte" or "Byte" => true,
                "sbyte" or "SByte" => true,
                "short" or "Int16" => true,
                "ushort" or "UInt16" => true,
                _ => false
            };
            
            if (isNumericPrimitive)
            {
                // For primitive arrays, write as packed
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len (packed)");
                
                // Calculate packed size
                sb.AppendIndentedLine($"var packedCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                AddPrimitiveSizeCalculation(sb, elementType, "item", DataFormat.Default, "packedCalc");
                sb.EndBlock();
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)packedCalc.Length);");
                
                // Write packed data
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                WritePrimitiveValueWithoutTag(sb, elementType, "item", DataFormat.Default, writeTarget, writeTargetShortName);
                sb.EndBlock();
            }
            else if (elementType == "string" || elementType == "System.String" || elementType == "String")
            {
                // For string collections, each string needs its own tag
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"writer.WriteString(item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            else
            {
                // Complex types - write as repeated fields
                sb.AppendIndentedLine($"foreach (var item in {valueAccess})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"if (item != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedElementType = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedElementType}Size(ref itemCalc, item);");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)itemCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedElementType}(ref writer, item);");
                sb.EndBlock();
                sb.EndBlock();
            }
            
            sb.EndBlock();
            return;
        }
        
        // Value is field 2, wire type depends on the type
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
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
                
            case "byte":
            case "Byte":
            case "System.Byte":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){valueAccess});");
                break;
                
            case "char":
            case "Char":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){valueAccess});");
                break;
                
            case "uint":
            case "UInt32":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteUInt32({valueAccess});");
                break;
                
            case "ulong":
            case "UInt64":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x10); // field 2, VarInt");
                sb.AppendIndentedLine($"writer.WriteUInt64({valueAccess});");
                break;
                
            case "double":
            case "System.Double":
            case "Double":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x11); // field 2, Fixed64");
                sb.AppendIndentedLine($"writer.WriteDouble({valueAccess});");
                break;
                
            case "float":
            case "System.Single":
            case "Single":
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x15); // field 2, Fixed32");
                sb.AppendIndentedLine($"writer.WriteFloat({valueAccess});");
                break;
                
            case "Guid":
            case "System.Guid":
                // Guid is written as message with 2 fixed64 fields (protobuf-net format)
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"writer.WriteVarUInt32(18u); // Guid: 2 fixed64 fields = (1+8)+(1+8) = 18 bytes");
                sb.AppendIndentedLine($"// Convert Guid to protobuf-net format (2 fixed64 fields)");
                // Create unique variable name based on valueAccess
                var guidVarName = valueAccess.Replace(".", "_").Replace("[", "_").Replace("]", "_").Replace("(", "_").Replace(")", "_") + "_guidBytes";
                sb.AppendIndentedLine($"var {guidVarName} = {valueAccess}.ToByteArray();");
                sb.AppendIndentedLine($"// Field 1: first 8 bytes (fixed64)");
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x09); // field 1, Fixed64");
                sb.AppendIndentedLine($"writer.WriteFixed64(System.BitConverter.ToUInt64({guidVarName}, 0));");
                sb.AppendIndentedLine($"// Field 2: last 8 bytes (fixed64)");
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x11); // field 2, Fixed64");
                sb.AppendIndentedLine($"writer.WriteFixed64(System.BitConverter.ToUInt64({guidVarName}, 8));");
                break;
                
            default:
                // For complex types (custom classes)
                sb.AppendIndentedLine($"if ({valueAccess} != null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"writer.WriteSingleByte(0x12); // field 2, Len");
                sb.AppendIndentedLine($"var valueCalc = new global::GProtobuf.Core.WriteSizeCalculator();");
                var sanitizedTypeName = SanitizeTypeNameForMethod(valueType);
                sb.AppendIndentedLine($"SizeCalculators.Calculate{sanitizedTypeName}Size(ref valueCalc, {valueAccess});");
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint)valueCalc.Length);");
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedTypeName}(ref writer, {valueAccess});");
                sb.EndBlock();
                break;
        }
    }
    
    private static void WriteElementToWriter(StringBuilderWithIndent sb, string elementType, string elementAccess, string writeTargetShortName)
    {
        var simpleType = GetSimpleTypeName(elementType);
        
        switch (simpleType)
        {
            case "string":
                sb.AppendIndentedLine($"if ({elementAccess} != null)");
                sb.AppendIndentedLine($"    writer.WriteString({elementAccess});");
                break;
            case "int":
            case "Int32":
                sb.AppendIndentedLine($"writer.WriteVarInt32({elementAccess});");
                break;
            case "long":
            case "Int64":
                sb.AppendIndentedLine($"writer.WriteVarInt64({elementAccess});");
                break;
            case "bool":
                sb.AppendIndentedLine($"writer.WriteBool({elementAccess});");
                break;
            case "float":
                sb.AppendIndentedLine($"writer.WriteFloat({elementAccess});");
                break;
            case "double":
                sb.AppendIndentedLine($"writer.WriteDouble({elementAccess});");
                break;
            case "byte":
                sb.AppendIndentedLine($"writer.WriteByte({elementAccess});");
                break;
            case "char":
            case "Char":
                sb.AppendIndentedLine($"writer.WriteVarUInt32((uint){elementAccess});");
                break;
            default:
                var sanitizedTypeName = SanitizeTypeNameForMethod(elementType);
                sb.AppendIndentedLine($"{writeTargetShortName}Writers.Write{sanitizedTypeName}(ref writer, {elementAccess});");
                break;
        }
    }
    
    /// <summary>
    /// Finds the position of the top-level comma in a generic type argument list
    /// </summary>
    private static int FindTopLevelComma(string genericArgs)
    {
        int depth = 0;
        for (int i = 0; i < genericArgs.Length; i++)
        {
            char c = genericArgs[i];
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;
            else if (c == ',' && depth == 0)
                return i;
        }
        return -1;
    }

    private static string GetSimpleTypeName(string fullTypeName)
    {
        // Handle generic types - don't split inside angle brackets
        int genericStart = fullTypeName.IndexOf('<');
        if (genericStart > 0)
        {
            // Find the last dot before the generic part
            string beforeGeneric = fullTypeName.Substring(0, genericStart);
            int lastDot = beforeGeneric.LastIndexOf('.');
            if (lastDot >= 0)
            {
                // Return everything after the last dot (including the generic part)
                return fullTypeName.Substring(lastDot + 1);
            }
        }
        else
        {
            // Non-generic type - find the last dot
            var lastDot = fullTypeName.LastIndexOf('.');
            if (lastDot >= 0)
                return fullTypeName.Substring(lastDot + 1);
        }
        
        return fullTypeName;
    }

    /// <summary>
    /// Gets the managed type name for primitive types (e.g., "int" -> "int", "Int32" -> "int")
    /// </summary>
    private static string GetManagedTypeName(string typeName)
    {
        var simpleType = GetSimpleTypeName(typeName);
        return simpleType switch
        {
            "Int32" or "System.Int32" => "int",
            "Int64" or "System.Int64" => "long",
            "Int16" or "System.Int16" => "short",
            "Byte" or "System.Byte" => "byte",
            "SByte" or "System.SByte" => "sbyte",
            "UInt32" or "System.UInt32" => "uint",
            "UInt64" or "System.UInt64" => "ulong",
            "UInt16" or "System.UInt16" => "ushort",
            "Single" or "System.Single" => "float",
            "Double" or "System.Double" => "double",
            "Boolean" or "System.Boolean" => "bool",
            "String" or "System.String" => "string",
            _ => simpleType // Return as-is for non-primitive types
        };
    }

    /// <summary>
    /// Sanitizes a type name to be used as part of a method name.
    /// Replaces special characters like <>, [], etc. with readable names.
    /// </summary>
    private static string SanitizeTypeNameForMethod(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;
        
        // Get simple type name without namespace
        var simpleName = GetSimpleTypeName(typeName);
        
        // Handle arrays
        if (simpleName.EndsWith("[]"))
        {
            var elementType = simpleName.Substring(0, simpleName.Length - 2);
            return SanitizeTypeNameForMethod(elementType) + "Array";
        }
        
        // Special handling for System.Tuple
        if (typeName.StartsWith("System.Tuple<") || typeName.StartsWith("Tuple<"))
        {
            // Extract the generic arguments
            var genericStart = typeName.IndexOf('<');
            var genericEnd = typeName.LastIndexOf('>');
            if (genericStart > 0 && genericEnd > genericStart)
            {
                var genericArgs = typeName.Substring(genericStart + 1, genericEnd - genericStart - 1);
                
                // Parse generic arguments
                var sanitizedArgs = new List<string>();
                var depth = 0;
                var currentArg = new System.Text.StringBuilder();
                
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    char c = genericArgs[i];
                    if (c == '<')
                    {
                        depth++;
                        currentArg.Append(c);
                    }
                    else if (c == '>')
                    {
                        depth--;
                        currentArg.Append(c);
                    }
                    else if (c == ',' && depth == 0)
                    {
                        sanitizedArgs.Add(SanitizeTypeNameForMethod(currentArg.ToString().Trim()));
                        currentArg.Clear();
                    }
                    else
                    {
                        currentArg.Append(c);
                    }
                }
                
                if (currentArg.Length > 0)
                {
                    sanitizedArgs.Add(SanitizeTypeNameForMethod(currentArg.ToString().Trim()));
                }
                
                // Create method-friendly name for Tuple
                return "TupleOf" + string.Join("And", sanitizedArgs);
            }
        }
        
        // Handle other generics
        var genericStartIdx = simpleName.IndexOf('<');
        if (genericStartIdx > 0)
        {
            var genericEndIdx = simpleName.LastIndexOf('>');
            if (genericEndIdx > genericStartIdx)
            {
                var baseType = simpleName.Substring(0, genericStartIdx);
                var genericArgs = simpleName.Substring(genericStartIdx + 1, genericEndIdx - genericStartIdx - 1);
                
                // Handle nested generics by recursively sanitizing
                var sanitizedArgs = new List<string>();
                var depth = 0;
                var currentArg = new System.Text.StringBuilder();
                
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    char c = genericArgs[i];
                    if (c == '<')
                    {
                        depth++;
                        currentArg.Append(c);
                    }
                    else if (c == '>')
                    {
                        depth--;
                        currentArg.Append(c);
                    }
                    else if (c == ',' && depth == 0)
                    {
                        sanitizedArgs.Add(SanitizeTypeNameForMethod(currentArg.ToString().Trim()));
                        currentArg.Clear();
                    }
                    else
                    {
                        currentArg.Append(c);
                    }
                }
                
                if (currentArg.Length > 0)
                {
                    sanitizedArgs.Add(SanitizeTypeNameForMethod(currentArg.ToString().Trim()));
                }
                
                // Create method-friendly name
                return baseType + "Of" + string.Join("And", sanitizedArgs);
            }
        }
        
        // Handle primitive type aliases - ensure proper casing for method names
        return simpleName switch
        {
            "string" or "System.String" => "String",
            "int" or "System.Int32" => "Int",
            "long" or "System.Int64" => "Long",
            "float" or "System.Single" => "Float",
            "double" or "System.Double" => "Double",
            "bool" or "System.Boolean" => "Bool",
            "byte" or "System.Byte" => "Byte",
            "sbyte" or "System.SByte" => "SByte",
            "short" or "System.Int16" => "Short",
            "ushort" or "System.UInt16" => "UShort",
            "uint" or "System.UInt32" => "UInt",
            "ulong" or "System.UInt64" => "ULong",
            _ => simpleName
        };
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
            "char" or "System.Char" => "foreach(var item in tempArray) writer.WriteVarUInt32((uint)item)",
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
            "char" or "System.Char" => "writer.WriteVarUInt32((uint)item)",
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
            "char" or "System.Char" => "foreach(var item in tempArray) calculator.WriteVarUInt32((uint)item)",
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
            "char" or "System.Char" => "calculator.WriteVarUInt32((uint)item)",
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
            "char" or "System.Char" => "(char)reader.ReadVarUInt32()",
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