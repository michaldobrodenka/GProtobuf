using GProtobuf.Core;
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;

namespace GProtobuf.Generator
{
    class TypeDefinition
    {
        public bool IsStruct;
        public string FullName;
        public List<ProtoIncludeAttribute> ProtoIncludes;
        public List<ProtoMemberAttribute> ProtoMembers;
    }

    class ObjectTree
    {
        private Dictionary<string, List<TypeDefinition>> types;

        public void AddType(string nmspace, string fullName, bool isStruct, List<ProtoIncludeAttribute> protoIncludes, List<ProtoMemberAttribute> protoMembers)
        {
            types ??= new();

            if (!this.types.TryGetValue(nmspace, out var typeDefinitions))
            {
                typeDefinitions = new List<TypeDefinition>();
                this.types[nmspace] = typeDefinitions;
            }

            typeDefinitions.Add(new TypeDefinition() { FullName = fullName, IsStruct = isStruct, ProtoIncludes = protoIncludes, ProtoMembers = protoMembers });
        }


        public IEnumerable<(string fileName, string fileCode)> GenerateCode()
        {
            var sb = new StringBuilderWithIndent();

            //context.AddSource("PropertySummary.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

            foreach (var namespaceWithObjects in types)
            {
                var nmspace = namespaceWithObjects.Key;
                GenerateDeserializers(sb, namespaceWithObjects, nmspace);

                yield return (nmspace + ".Serialization.cs", sb.ToString());
            }
        }

        private void GenerateDeserializers(StringBuilderWithIndent sb, KeyValuePair<string, List<TypeDefinition>> namespaceWithObjects, string nmspace)
        {
            sb.Clear();
            sb.AppendIndentedLine("using GProtobuf.Core;\r\nusing System;\r\nusing System.Collections.Generic;\r\n");

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

            sb.AppendIndentedLine($"public static class SpanReaders");
            sb.StartNewBlock();

            foreach (var obj in objects)
            {
                sb.AppendIndentedLine($"public static global::{obj.FullName} Read{GetClassNameFromFullName(obj.FullName)}(ref SpanReader reader)");
                sb.StartNewBlock();

                if (obj.ProtoIncludes == null || obj.ProtoIncludes.Count == 0)
                    sb.AppendIndentedLine($"global::{obj.FullName} result = new global::{obj.FullName}();\r\n");
                else
                    sb.AppendIndentedLine($"global::{obj.FullName} result = default(global::{obj.FullName});\r\n");

                sb.AppendIndentedLine($"while(!reader.IsEnd)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"var (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");

                if (obj.ProtoIncludes != null)
                {
                    WriteProtoIncludesInDeserializers(sb, obj);
                }

                if (obj.ProtoMembers != null)
                {
                    foreach (var protoMember in obj.ProtoMembers)
                    {
                        this.WriteProtoMember(sb, protoMember);
                    }
                }
                sb.AppendIndentedLine($"// default");
                sb.AppendIndentedLine($"reader.SkipField(wireType);");
                sb.EndBlock();// end of main while loop

                sb.AppendNewLine();
                sb.AppendIndentedLine($"return result;");
                sb.EndBlock();
                sb.AppendNewLine();
            }

            sb.EndBlock();
            sb.EndBlock();
        }

        private static void WriteProtoIncludesInDeserializers(StringBuilderWithIndent sb, TypeDefinition obj)
        {
            if (obj.ProtoIncludes.Count > 0)
            {
                foreach (var protoInclude in obj.ProtoIncludes)
                {
                    //protoInclude.Type
                    sb.AppendIndentedLine($"if (fieldId == {protoInclude.FieldId})");
                    sb.StartNewBlock();
                    sb.AppendIndentedLine($"var length = reader.ReadVarInt32();");
                    sb.AppendIndentedLine($"var reader1 = new SpanReader(reader.GetSlice(length));");
                    //sb.AppendLine($"\t\t\t\t\tresult = reader1.Read{GetClassNameFromFullName(protoInclude.Type)}();");
                    sb.AppendIndentedLine($"result = global::{protoInclude.Namespace}.Serialization.SpanReaders.Read{GetClassNameFromFullName(protoInclude.Type)}(ref reader1);");

                    sb.AppendIndentedLine($"continue;");
                    //sb.AppendIndentedLine($"\t\t\t\t}}\r\n");
                    sb.EndBlock();
                    sb.AppendNewLine();

                    sb.AppendIndentedLine($"if (result == null)");
                    sb.AppendIndentedLine($"throw new InvalidOperationException($\"ProtoInclude field must be first. Is {{fieldId}} defined in ProtoInclude attributes?\");\r\n");
                }

            } // todo hybrid binary tree for many proto includes
        }

        private void WriteProtoMember(StringBuilderWithIndent sb, ProtoMemberAttribute protoMember)
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
}
