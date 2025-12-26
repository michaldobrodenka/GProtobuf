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

        private void CollectFromType(TypeDefinition type)
        {
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    if (member.IsCollection)
                    {
                        // Packed collections use WireType.Len
                        if (member.IsPacked)
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
