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
            var sb = new StringBuilder();

            //context.AddSource("PropertySummary.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

            foreach (var namespaceWithObjects in types)
            {
                sb.Clear();
                sb.AppendLine("using GProtobuf.Core;\r\nusing System;\r\nusing System.Collections.Generic;\r\n");

                var nmspace = namespaceWithObjects.Key;
                var objects = namespaceWithObjects.Value;

                sb.AppendLine($"namespace {nmspace}.Serialization\r\n{{");

                sb.AppendLine("""
                            public static class Deserializers
                            {
                        """);

                foreach (var obj in objects)
                {
                    sb.AppendLine($$"""
                                public static global::{{obj.FullName}} Deserialize{{GetClassNameFromFullName(obj.FullName)}}(ReadOnlySpan<byte> data)
                                {
                                    var reader = new SpanReader(data);
                                    return SpanReaders.Read{{GetClassNameFromFullName(obj.FullName)}}(ref reader);
                                }

                        """);



                }

                sb.AppendLine("""
                            }

                        """);


                sb.AppendLine($"\tpublic static class SpanReaders\r\n\t{{");

                foreach (var obj in objects)
                {
                    sb.AppendLine($"\t\tpublic static global::{obj.FullName} Read{GetClassNameFromFullName(obj.FullName)}(ref SpanReader reader)");
                    sb.AppendLine($"\t\t{{");
                    
                    if (obj.ProtoIncludes == null || obj.ProtoIncludes.Count == 0)
                        sb.AppendLine($"\t\t\tglobal::{obj.FullName} result = new global::{obj.FullName}();\r\n");
                    else
                        sb.AppendLine($"\t\t\tglobal::{obj.FullName} result = default(global::{obj.FullName});\r\n");

                    sb.AppendLine($"\t\t\twhile(!reader.IsEnd)");
                    sb.AppendLine($"\t\t\t{{");
                    sb.AppendLine($"\t\t\t\tvar (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");

                    if (obj.ProtoIncludes != null)
                    {
                        WriteProtoIncludes(sb, obj);
                    }

                    if (obj.ProtoMembers != null)
                    {
                        foreach(var protoMember in obj.ProtoMembers)
                        {
                            this.WriteProtoMember(sb, protoMember);
                        }
                    }
                    sb.AppendLine($$"""
                                        // default
                                        reader.SkipField(wireType);
                                    }
                            
                                    return result;
                                }
                        """);
                    //sb.AppendLine($"\t\t\t\t// default");
                    //sb.AppendLine($"\t\t\t\treader.SkipField(wireType);");

                    //sb.AppendLine($"\t\t\t}}"); // end of main while loop

                    //sb.AppendLine($"\t\t\treturn result;");
                    //sb.AppendLine($"\t\t}}\r\n");
                }

                sb.AppendLine($"\t}}"); // end of extension class
                sb.AppendLine("}"); // end of namespace

                yield return (nmspace + ".Serialization.cs", sb.ToString());
            }
        }

        private static void WriteProtoIncludes(StringBuilder sb, TypeDefinition obj)
        {
            if (obj.ProtoIncludes.Count > 0)
            {
                foreach (var protoInclude in obj.ProtoIncludes)
                {
                    //protoInclude.Type
                    sb.AppendLine($"\t\t\t\tif (fieldId == {protoInclude.FieldId})");
                    sb.AppendLine($"\t\t\t\t{{");
                    sb.AppendLine($"\t\t\t\t\tvar length = reader.ReadVarInt32();");
                    sb.AppendLine($"\t\t\t\t\tvar reader1 = new SpanReader(reader.GetSlice(length));");
                    //sb.AppendLine($"\t\t\t\t\tresult = reader1.Read{GetClassNameFromFullName(protoInclude.Type)}();");
                    sb.AppendLine($"\t\t\t\t\tresult = global::{protoInclude.Namespace}.Serialization.SpanReaders.Read{GetClassNameFromFullName(protoInclude.Type)}(ref reader1);");

                    sb.AppendLine($"\t\t\t\t\t\tcontinue;");
                    sb.AppendLine($"\t\t\t\t}}\r\n");

                    sb.AppendLine($"\t\t\t\tif (result == null)");
                    sb.AppendLine($"\t\t\t\t\tthrow new InvalidOperationException($\"ProtoInclude field must be first. Is {{fieldId}} defined in ProtoInclude attributes?\");\r\n");
                }

            } // todo hybrid binary tree for many proto includes
        }

        private void WriteProtoMember(StringBuilder sb, ProtoMemberAttribute protoMember)
        {
            sb.AppendLine($"\t\t\t\tif (fieldId == {protoMember.FieldId})");
            sb.AppendLine($"\t\t\t\t{{");
            //sb.AppendLine($"\t\t\treader.SkipField(wireType)");
            //sb.AppendLine($"\t\t\t\tresult.{protoMember.Name} = reader.Read{GetClassNameFromFullName(protoMember.Type)};");
            switch(GetClassNameFromFullName(protoMember.Type))
            {
                case "System.Int32":
                case "Int32":
                case "int":
                    switch (protoMember.DataFormat)
                    {
                        case DataFormat.FixedSize:
                            sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadFixedInt32();");
                            break;

                        case DataFormat.ZigZag:
                            sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadZigZagVarInt32();");
                            break;

                        default:
                            sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadVarInt32();");
                            break;
                    }
                    break;

                case "Double":
                case "double":
                    sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadDouble(wireType);");
                    break;

                case "String":
                case "System.String":
                case "string":
                    sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadString(wireType);");
                    break;

                case "byte[]":
                case "Byte[]":
                case "System.Byte[]":
                    sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadByteArray();");
                    break;

                case "System.Int32[]":
                case "Int32[]":
                case "int[]":
                    if (protoMember.IsPacked)
                    {
                        switch (protoMember.DataFormat)
                        {
                            case DataFormat.FixedSize:
                                sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadPackedFixedSizeInt32Array();");
                                break;

                            case DataFormat.ZigZag:
                                sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadPackedVarIntInt32Array(true);");
                                break;

                            default:
                                sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = reader.ReadPackedVarIntInt32Array(false);");
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

                        sb.AppendLine($$"""
                                            List<int> resultList = new();
                                            var wireType1 = wireType;
                                            var fieldId1 = fieldId;

                                            while (!reader.IsEnd && fieldId == fieldId1)
                                            {
                                                var number = {{int32Reader}}
                                                resultList.Add(number);
                                                (wireType1, fieldId1) = reader.ReadWireTypeAndFieldId();
                        					}

                                            result.{{protoMember.Name}} = resultList.ToArray();
                        """);

                        //sb.AppendLine($"\t\t\t{{");
                        //sb.AppendLine($"\t\t\t\tvar (wireType, fieldId) = reader.ReadWireTypeAndFieldId();\r\n");
                    }
                    break;

                default:

                    sb.AppendLine($"\t\t\t\t\tvar length = reader.ReadVarInt32();");
                    sb.AppendLine($"\t\t\t\t\tvar reader1 = new SpanReader(reader.GetSlice(length));");
                    sb.AppendLine($"\t\t\t\t\tresult.{protoMember.Name} = global::{protoMember.Namespace}.Serialization.SpanReaders.Read{GetClassNameFromFullName(protoMember.Type)}(ref reader1);");
                    break;
            }
            sb.AppendLine($"\t\t\t\t\tcontinue;");
            sb.AppendLine($"\t\t\t\t}}\r\n");
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
