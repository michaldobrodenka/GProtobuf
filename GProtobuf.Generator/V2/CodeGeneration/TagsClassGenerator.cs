using System.Collections.Generic;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates static Tags class with ReadOnlySpan properties for multi-byte tags.
    /// This avoids stackalloc on every write for tags that require more than 1 byte.
    /// </summary>
    internal class TagsGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly HashSet<(int fieldId, WireType wireType)> _collectedTags = new HashSet<(int, WireType)>();

        public TagsGenerator(StringBuilderWithIndent sb)
        {
            _sb = sb;
        }

        /// <summary>
        /// Collects all multi-byte tags from all types.
        /// </summary>
        public void CollectTags(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                CollectFromType(type);
            }
        }

        /// <summary>
        /// Collects tags from virtual map entry types.
        /// Map entries have 2 fields: Key (fieldId=1) and Value (fieldId=2).
        /// </summary>
        public void CollectTagsFromMapEntries(System.Collections.Generic.IEnumerable<Handlers.VirtualTypes.VirtualMapEntryInfo> mapEntries)
        {
            foreach (var mapEntry in mapEntries)
            {
                // Field 1: Key
                var keyWireType = TypeMapping.GetWireType(mapEntry.KeyType, DataFormat.Default);
                CollectTag(1, keyWireType);

                // Field 2: Value
                var valueWireType = TypeMapping.GetWireType(mapEntry.ValueType, DataFormat.Default);
                CollectTag(2, valueWireType);
            }
        }

        /// <summary>
        /// Collects tags from virtual tuple types.
        /// </summary>
        public void CollectTagsFromTuples(System.Collections.Generic.IEnumerable<Handlers.VirtualTypes.TupleTypeInfo> tuples)
        {
            foreach (var tuple in tuples)
            {
                for (int i = 0; i < tuple.ItemTypes.Count; i++)
                {
                    var itemType = tuple.ItemTypes[i]; // itemType is already a string
                    var wireType = TypeMapping.GetWireType(itemType, DataFormat.Default);
                    CollectTag(i + 1, wireType); // Field IDs start at 1
                }
            }
        }

        /// <summary>
        /// Collects tags from ProtoInclude types that will have WriteContent methods generated.
        /// This ensures tags are available when writing cross-namespace ProtoInclude types.
        /// </summary>
        public void CollectTagsFromProtoIncludes(IEnumerable<TypeDefinition> types, TypeRegistry registry)
        {
            var processedTypes = new HashSet<string>();
            var protoIncludeTypes = new HashSet<string>();

            // Collect all ProtoInclude types from the given types
            foreach (var type in types)
            {
                if (type.ProtoIncludes != null)
                {
                    foreach (var include in type.ProtoIncludes)
                    {
                        protoIncludeTypes.Add(include.Type);
                    }
                }
            }

            // Also collect ProtoIncludes from ALL registered types to match StreamWriterGenerator logic
            foreach (var registeredType in registry.GetAllTypes())
            {
                if (registeredType.ProtoIncludes != null)
                {
                    foreach (var include in registeredType.ProtoIncludes)
                    {
                        protoIncludeTypes.Add(include.Type);
                    }
                }
            }

            // Collect tags from each ProtoInclude type
            foreach (var protoIncludeTypeName in protoIncludeTypes)
            {
                if (processedTypes.Contains(protoIncludeTypeName))
                    continue;

                processedTypes.Add(protoIncludeTypeName);

                var protoIncludeType = registry.GetByFullName(protoIncludeTypeName);
                if (protoIncludeType != null)
                {
                    CollectFromType(protoIncludeType);
                }
            }
        }

        private void CollectFromType(TypeDefinition type)
        {
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    if (member.IsCollection)
                    {
                        // Level200: Primitives MUST use packed encoding by default
                        bool shouldBePacked = member.IsPacked || TypeMapping.ShouldBePackedByDefault(member.CollectionElementType);

                        // Packed collections use WireType.Len
                        if (shouldBePacked)
                        {
                            CollectTag(member.FieldId, WireType.Len);
                        }
                        else
                        {
                            // Non-packed collections use element's wire type
                            var elementWireType = TypeMapping.GetWireType(member.CollectionElementType, member.DataFormat);
                            CollectTag(member.FieldId, elementWireType);
                        }
                    }
                    else
                    {
                        // Regular field
                        var wireType = TypeMapping.GetWireType(member.Type, member.DataFormat);
                        CollectTag(member.FieldId, wireType);
                    }
                }
            }

            if (type.ProtoIncludes != null)
            {
                foreach (var include in type.ProtoIncludes)
                {
                    CollectTag(include.FieldId, WireType.Len);
                }
            }
        }

        private void CollectTag(int fieldId, WireType wireType)
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            if (byteCount > 1)
            {
                _collectedTags.Add((fieldId, wireType));
            }
        }

        /// <summary>
        /// Generates static Tags class with all collected multi-byte tags.
        /// </summary>
        public void Generate()
        {
            if (_collectedTags.Count == 0)
                return;

            _sb.AppendIndentedLine("internal static class Tags");
            _sb.StartNewBlock();

            foreach (var (fieldId, wireType) in _collectedTags)
            {
                var (bytesString, _) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
                var propertyName = GetTagPropertyName(fieldId, wireType);

                _sb.AppendIndentedLine($"public static ReadOnlySpan<byte> {propertyName} => new byte[] {{ {bytesString} }};");
            }

            _sb.EndBlock();
            _sb.AppendNewLine();
        }

        /// <summary>
        /// Gets the property name for a tag.
        /// </summary>
        public static string GetTagPropertyName(int fieldId, WireType wireType)
        {
            return $"Tag_{fieldId}_{wireType}";
        }

        /// <summary>
        /// Checks if a tag requires multiple bytes.
        /// </summary>
        public static bool IsMultiByteTag(int fieldId, WireType wireType)
        {
            var (_, byteCount) = TypeMapping.PrecomputeTagBytes(fieldId, wireType);
            return byteCount > 1;
        }
    }
}
