using System;
using System.Collections.Generic;
using System.Text;

namespace GProtobuf.Generator.V2
{
    public class ObjectTreeV2 // Replace ObjectTree afterwards
    {
    //    private readonly TypeRegistry _registry = new();

    //    public void AddType(string @namespace, TypeDefinition type)
    //    {
    //        _registry.Register(@namespace, type);
    //    }

    //    public IEnumerable<(string FileName, string FileCode)> GenerateCode()
    //    {
    //        foreach (var ns in _registry.GetAllNamespaces())
    //        {
    //            var sb = new StringBuilderWithIndent();
    //            var types = _registry.GetByNamespace(ns).ToList();

    //            WriteHeader(sb, ns);

    //            // Generate all code sections
    //            new DeserializerGenerator(_registry, sb).GenerateAll(types);
    //            new SerializerGenerator(_registry, sb).GenerateAll(types);
    //            new SpanReaderGenerator(_registry, sb).GenerateAll(types);
    //            new StreamWriterGenerator(_registry, sb).GenerateAll(types);
    //            new BufferWriterGenerator(_registry, sb).GenerateAll(types);
    //            new SizeCalculatorGenerator(_registry, sb).GenerateAll(types);

    //            WriteFooter(sb);

    //            yield return ($"{ns}.Serialization.cs", sb.ToString());
    //        }
    //    }
    }
}
