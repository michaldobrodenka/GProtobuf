using GProtobuf.Generator.V2.Handlers.Core;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
{
    /// <summary>
    /// Centralized logic for analyzing and categorizing Tuple item types.
    /// Eliminates code duplication across Read/Write/Size operations.
    /// </summary>
    internal class TupleItemTypeHandler
    {
        private readonly TypeRegistry _registry;

        public TupleItemTypeHandler(TypeRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Analyzes a type and returns comprehensive information about how to handle it.
        /// </summary>
        public TypeHandlingInfo AnalyzeType(string itemType)
        {
            if (TypeHelper.IsNullableType(itemType))
            {
                var underlyingType = TypeHelper.GetNullableUnderlyingType(itemType);
                // Normalize for consistency (e.g., "byte" -> "System.Byte")
                var normalizedUnderlying = TypeMapping.NormalizeTypeName(underlyingType);
                return new TypeHandlingInfo
                {
                    IsNullable = true,
                    OriginalType = itemType,
                    UnderlyingType = normalizedUnderlying,
                    Category = GetTypeCategory(underlyingType)
                };
            }

            // Normalize for consistency (e.g., "byte" -> "System.Byte")
            var normalizedType = TypeMapping.NormalizeTypeName(itemType);
            return new TypeHandlingInfo
            {
                IsNullable = false,
                OriginalType = itemType,
                UnderlyingType = normalizedType,
                Category = GetTypeCategory(itemType)
            };
        }

        /// <summary>
        /// Determines the category of a type (Primitive, Enum, or Complex).
        /// </summary>
        private TypeCategory GetTypeCategory(string typeName)
        {
            // Normalize type name first (e.g., "byte" -> "System.Byte")
            var normalizedTypeName = TypeMapping.NormalizeTypeName(typeName);

            // Check TypeMapping first (handles primitives, string, byte[], Guid, etc.)
            // Use GetReadExpression as indicator that TypeMapping can handle this type
            var readExpr = TypeMapping.GetReadExpression(normalizedTypeName, DataFormat.Default, "reader", "wireType");
            if (readExpr != null)
                return TypeCategory.Primitive;

            // Check if it's an enum (use original type name for enum check)
            if (_registry.IsEnum(typeName) || _registry.IsEnum(normalizedTypeName))
                return TypeCategory.Enum;

            // Otherwise it's a complex type
            return TypeCategory.Complex;
        }
    }

    /// <summary>
    /// Comprehensive information about a Tuple item type.
    /// </summary>
    internal class TypeHandlingInfo
    {
        /// <summary>
        /// Whether the type is Nullable&lt;T&gt; or T?
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// Original type name as declared (e.g., "int?", "Nullable&lt;int&gt;")
        /// </summary>
        public string OriginalType { get; set; }

        /// <summary>
        /// Underlying type (for Nullable&lt;int&gt; this is "int")
        /// </summary>
        public string UnderlyingType { get; set; }

        /// <summary>
        /// Category of the underlying type
        /// </summary>
        public TypeCategory Category { get; set; }
    }

    /// <summary>
    /// Categories of types for code generation.
    /// </summary>
    internal enum TypeCategory
    {
        /// <summary>
        /// Primitive types handled by TypeMapping (int, string, byte[], Guid, etc.)
        /// </summary>
        Primitive,

        /// <summary>
        /// Enum types
        /// </summary>
        Enum,

        /// <summary>
        /// Complex/custom types (classes, structs)
        /// </summary>
        Complex
    }
}
