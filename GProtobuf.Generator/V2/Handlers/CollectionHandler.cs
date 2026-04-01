using GProtobuf.Generator.Attributes;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Helpers;
using GProtobuf.Generator.WireFormat;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Generates code for collections of complex types (List&lt;CustomMessage&gt;).
    /// Reuses existing Write{ClassName}Content and Calculate{ClassName}ContentSize methods.
    /// </summary>
    internal class CollectionHandler
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly TypeRegistry _registry;

        public CollectionHandler(StringBuilderWithIndent sb, TypeRegistry registry)
        {
            _sb = sb;
            _registry = registry;
        }

        private bool IsProtoVarintType(string typeName, out ProtoVarintType varintType, out string valueMember)
        {
            var normalizedTypeName = TypeMapping.NormalizeTypeName(typeName);
            if (_registry.IsProtoVarint(normalizedTypeName))
            {
                varintType = _registry.GetProtoVarintType(normalizedTypeName) ?? ProtoVarintType.UInt32;
                valueMember = _registry.GetProtoVarintValueMember(normalizedTypeName);
                return true;
            }
            varintType = default;
            valueMember = null;
            return false;
        }

        #region Write (Serialization)

        /// <summary>
        /// Generates write code for List&lt;ComplexType&gt;.
        /// For each item: write tag, calculate length, write length, write content.
        /// </summary>
        public void GenerateComplexCollectionWrite(
            int fieldId,
            string sourceVar,
            string elementTypeName,
            string elementClassName,
            string writerClassName = "StreamWriters")
        {
            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var collection = {sourceVar};");
            _sb.AppendIndentedLine("if (collection != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("foreach (var item in collection)");
            _sb.StartNewBlock();

            // Check if element is a simple BCL type
            bool isSimpleBclType = TypeMapping.IsSimpleType(elementTypeName);

            if (isSimpleBclType)
            {
                // Add null validation for KNOWN reference types only (Level200 compatibility)
                // Only validate string and byte[] - simple BCL types are value types (DateTime, Guid, etc.)
                var normalizedType = TypeMapping.NormalizeTypeName(elementTypeName);
                bool isString = normalizedType == "System.String";
                bool isByteArray = normalizedType == "System.Byte[]";

                if (isString || isByteArray)
                {
                    // Extract simple class name for error message (e.g., "System.String" -> "string")
                    var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                    // If still has dots (custom type), extract just the class name
                    var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                    _sb.AppendIndentedLine("if (item == null)");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                    _sb.EndBlock();
                }

                // For simple BCL types (DateTime, Guid, etc.), write tag and use direct write method
                var wireType = TypeMapping.GetWireType(elementTypeName);
                TagCodeHelper.WriteTag(_sb, fieldId, wireType);

                var writeExpr = TypeMapping.GetWriteExpression(elementTypeName, "item", DataFormat.Default, "writer");
                if (writeExpr != null)
                {
                    _sb.AppendIndentedLine($"{writeExpr};");
                }
                else
                {
                    _sb.AppendIndentedLine($"// WARNING: No write expression for {elementTypeName}");
                }
            }
            else if (IsProtoVarintType(elementTypeName, out var writeVarintType, out var writeValueMember))
            {
                // ProtoVarint type - write tag and varint value directly
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.VarInt);
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(writeVarintType);
                _sb.AppendIndentedLine($"writer.{writeMethod}(item.{writeValueMember});");
            }
            else
            {
                // For complex types (custom messages), validate nulls before serialization
                // Use ReferenceEquals to work with both value types (structs) and reference types (classes)
                // For structs, ReferenceEquals will always return false (no null check needed)
                // For classes, ReferenceEquals will return true if null
                var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                // Extract just the class name for error message (e.g., "Namespace.SimpleMessage" -> "SimpleMessage")
                var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                _sb.AppendIndentedLine("if (object.ReferenceEquals(item, null))");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                _sb.EndBlock();

                // For complex types, write tag, calculate length, write length, write content
                TagCodeHelper.WriteTag(_sb, fieldId, WireType.Len);

                // Check if element type is polymorphic (requires dispatcher method with ProtoInclude wrapper)
                // This includes: GenericDevice[] where DeviceBaseV1 has ProtoInclude
                bool isPolymorphic = IsElementTypePolymorphic(elementTypeName);

                // Calculate and write length
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

                // ContentSize handles dispatcher logic for polymorphic types
                var qualifiedSizeCall = GetQualifiedCalculateContentSizeCall(elementTypeName, elementClassName);

                _sb.AppendIndentedLine($"{qualifiedSizeCall}(ref itemCalc, item);");
                _sb.AppendIndentedLine("writer.WriteVarUInt32((uint)itemCalc.Length);");

                // Write content
                var qualifiedWriteCall = isPolymorphic
                    ? GetQualifiedWriteCall(elementTypeName, elementClassName, writerClassName)
                    : GetQualifiedWriteContentCall(elementTypeName, elementClassName, writerClassName);

                _sb.AppendIndentedLine($"{qualifiedWriteCall}(ref writer, item);");
            }

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        #endregion

        #region Read (Deserialization)

        /// <summary>
        /// Generates read code for List&lt;ComplexType&gt;.
        /// Lazy-initializes collection, reads length-prefixed item, adds to collection.
        /// </summary>
        /// <param name="useObjectArrayBuilder">
        /// When true, uses ObjectArrayBuilder pattern (_builder_{fieldName}.Add) instead of List&lt;T&gt; pattern.
        /// ObjectArrayBuilder must be pre-declared and disposed by caller.
        /// </param>
        public void GenerateComplexCollectionRead(
            string targetVar,
            string elementTypeName,
            string elementClassName,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader",
            bool useObjectArrayBuilder = false)
        {
            var shortElementType = TypeMapping.GetGlobalTypeName(elementTypeName);

            // For arrays and IEnumerable, use temp list
            // Arrays can't use Add(), and IEnumerable<T> doesn't have Add() method
            string actualTargetVar = targetVar;
            bool needsTempList = false;
            string prefix = null;
            string fieldName = null;

            // Determine if this is from Populate (instance.) or ReadContent (result.)
            if (targetVar.StartsWith("instance."))
            {
                prefix = "instance.";
                fieldName = targetVar.Substring(prefix.Length);
            }
            else if (targetVar.StartsWith("result."))
            {
                prefix = "result.";
                fieldName = targetVar.Substring(prefix.Length);
            }

            if (fieldName != null)
            {
                if (collectionKind == CollectionKind.Array)
                {
                    // Arrays need temp storage - either _tempList_ or _builder_
                    actualTargetVar = useObjectArrayBuilder ? $"_builder_{fieldName}" : $"_tempList_{fieldName}";
                    needsTempList = !useObjectArrayBuilder; // Only need temp list pattern for List<T>
                }
                else if (collectionKind == CollectionKind.InterfaceCollection && collectionTypeName != null)
                {
                    // Check if it's IEnumerable (not ICollection, IList, etc.)
                    // For IEnumerable collections, we can't call Add() directly, need temp storage
                    if (collectionTypeName.Contains("IEnumerable<") &&
                        !collectionTypeName.Contains("ICollection") &&
                        !collectionTypeName.Contains("IList"))
                    {
                        actualTargetVar = useObjectArrayBuilder ? $"_builder_{fieldName}" : $"_tempList_{fieldName}";
                        needsTempList = !useObjectArrayBuilder;
                    }
                }
            }

            // Check if we're actually using ObjectArrayBuilder (target starts with _builder_)
            bool actuallyUsingObjectBuilder = actualTargetVar.StartsWith("_builder_");

            // For ObjectArrayBuilder, skip lazy init - it's always pre-initialized
            // For all other cases (instance.Property, _tempList_), we need lazy init
            if (!actuallyUsingObjectBuilder)
            {
                // Lazy init collection (List<T> pattern)
                _sb.AppendIndentedLine($"if ({actualTargetVar} == null)");
                _sb.StartNewBlock();

                var initExpr = GenerateCollectionInitialization(shortElementType, collectionKind, collectionTypeName);
                _sb.AppendIndentedLine($"{actualTargetVar} = {initExpr};");

                _sb.EndBlock();
            }

            // Check if element is a simple BCL type (DateTime, Guid, TimeSpan, etc.)
            bool isSimpleBclType = TypeMapping.IsSimpleType(elementTypeName);

            if (isSimpleBclType)
            {
                // For simple BCL types (DateTime, Guid, etc.), use direct read method
                // These types handle their own wire format internally
                var readExpr = TypeMapping.GetReadExpression(elementTypeName, DataFormat.Default, readerVar, "wireType");
                if (readExpr != null)
                {
                    _sb.AppendIndentedLine($"var item = {readExpr};");
                }
                else
                {
                    // Fallback for unsupported simple types
                    _sb.AppendIndentedLine($"// WARNING: No read expression for {elementTypeName}");
                    _sb.AppendIndentedLine($"var item = default({shortElementType});");
                }
            }
            else if (IsProtoVarintType(elementTypeName, out var varintType, out var valueMember))
            {
                // ProtoVarint type - read as simple varint and construct via constructor
                var readMethod = PrimitiveTypeCodeGenerator.GetProtoVarintReadMethod(varintType);
                var globalTypeName = TypeMapping.GetGlobalGenericTypeName(elementTypeName);
                _sb.AppendIndentedLine($"var item = new {globalTypeName}({readerVar}.{readMethod}());");
            }
            else
            {
                // For complex types, read length-prefixed nested message
                _sb.AppendIndentedLine($"var length = {readerVar}.ReadVarInt32();");
                _sb.AppendIndentedLine($"var nestedReader = new SpanReader({readerVar}.GetSlice(length));");

                // Check if element type is polymorphic (requires Read() with ProtoInclude wrapper detection)
                // Polymorphic types: base types with ProtoInclude OR derived types from polymorphic base
                bool isPolymorphic = IsElementTypePolymorphic(elementTypeName);

                // For polymorphic types, use Read{ClassName} (handles ProtoInclude wrapper)
                // For concrete types, use Read{ClassName}Content (reads fields directly)
                var qualifiedCall = isPolymorphic
                    ? GetQualifiedReadCall(elementTypeName, elementClassName)
                    : GetQualifiedReadContentCall(elementTypeName, elementClassName);

                _sb.AppendIndentedLine($"var item = {qualifiedCall}(ref nestedReader);");
            }

            // Add to collection
            _sb.AppendIndentedLine($"{actualTargetVar}.Add(item);");
        }

        private string GenerateCollectionInitialization(string elementType, CollectionKind kind, string collectionTypeName)
        {
            switch (kind)
            {
                case CollectionKind.Array:
                    // Arrays can't be used with Add(), so use List
                    return GetListInit(elementType);

                case CollectionKind.InterfaceCollection:
                    // Interface collections always use System.Collections.Generic implementations
                    if (collectionTypeName != null)
                    {
                        if (TypeHelper.IsHashSetType(collectionTypeName))
                            return GetHashSetInit(elementType);
                        if (TypeHelper.IsListType(collectionTypeName))
                            return GetListInit(elementType);
                    }
                    // Default to List
                    return GetListInit(elementType);

                case CollectionKind.ConcreteCollection:
                    // Check for System.Collections.Generic types specifically (not custom types)
                    if (collectionTypeName != null)
                    {
                        bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                               collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                        bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.List<");

                        if (isSystemHashSet)
                            return GetHashSetInit(elementType);
                        if (isSystemList)
                            return GetListInit(elementType);

                        // Custom collection type - instantiate the actual type
                        return $"new global::{collectionTypeName}()";
                    }
                    // Default to List
                    return GetListInit(elementType);

                case CollectionKind.CustomCollection:
                case CollectionKind.CustomEnumerable:
                    // Custom collection type - instantiate the actual type
                    // (protobuf-net compatible: IEnumerable<T> + Add(T))
                    if (!string.IsNullOrEmpty(collectionTypeName))
                    {
                        return $"new global::{collectionTypeName}()";
                    }
                    // Fallback to List if type is unknown
                    return GetListInit(elementType);

                default:
                    return GetListInit(elementType);
            }
        }

        private static string GetListInit(string elementType)
        {
            return $"new global::System.Collections.Generic.List<{elementType}>()";
        }

        private static string GetHashSetInit(string elementType)
        {
            return $"new global::System.Collections.Generic.HashSet<{elementType}>()";
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates size calculation for List&lt;ComplexType&gt;.
        /// For each item: add tag size, calculate content size, add length varint + content.
        /// </summary>
        public void GenerateComplexCollectionSize(
            int fieldId,
            string sourceVar,
            string elementTypeName,
            string elementClassName,
            string calculatorVar = "calculator")
        {
            _sb.StartNewBlock(); // Scope block to avoid name collisions
            _sb.AppendIndentedLine($"var collection = {sourceVar};");
            _sb.AppendIndentedLine("if (collection != null)");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("foreach (var item in collection)");
            _sb.StartNewBlock();

            // Check if element is a simple BCL type
            bool isSimpleBclType = TypeMapping.IsSimpleType(elementTypeName);

            if (isSimpleBclType)
            {
                // Add null validation for KNOWN reference types only (Level200 compatibility)
                // Only validate string and byte[] - simple BCL types are value types (DateTime, Guid, etc.)
                var normalizedType = TypeMapping.NormalizeTypeName(elementTypeName);
                bool isString = normalizedType == "System.String";
                bool isByteArray = normalizedType == "System.Byte[]";

                if (isString || isByteArray)
                {
                    // Extract simple class name for error message (e.g., "System.String" -> "string")
                    var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                    // If still has dots (custom type), extract just the class name
                    var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                    _sb.AppendIndentedLine("if (item == null)");
                    _sb.StartNewBlock();
                    _sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                    _sb.EndBlock();
                }

                // For simple BCL types (DateTime, Guid, etc.), add tag size and use direct size method
                var wireType = TypeMapping.GetWireType(elementTypeName);
                TagCodeHelper.AddTagSize(_sb, fieldId, wireType, calculatorVar);

                var sizeExpr = TypeMapping.GetSizeExpression(elementTypeName, "item", DataFormat.Default, calculatorVar);
                if (sizeExpr != null)
                {
                    _sb.AppendIndentedLine($"{sizeExpr};");
                }
                else
                {
                    _sb.AppendIndentedLine($"// WARNING: No size expression for {elementTypeName}");
                }
            }
            else if (IsProtoVarintType(elementTypeName, out var sizeVarintType, out var sizeValueMember))
            {
                // ProtoVarint type - add tag size and varint value size
                TagCodeHelper.AddTagSize(_sb, fieldId, WireType.VarInt, calculatorVar);
                var writeMethod = PrimitiveTypeCodeGenerator.GetProtoVarintWriteMethod(sizeVarintType);
                _sb.AppendIndentedLine($"{calculatorVar}.{writeMethod}(item.{sizeValueMember});");
            }
            else
            {
                // Use ReferenceEquals to work with both value types (structs) and reference types (classes)
                // For structs, ReferenceEquals will always return false (no null check needed)
                // For classes, ReferenceEquals will return true if null
                var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                // Extract just the class name for error message (e.g., "Namespace.SimpleMessage" -> "SimpleMessage")
                var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                _sb.AppendIndentedLine("if (object.ReferenceEquals(item, null))");
                _sb.StartNewBlock();
                _sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                _sb.EndBlock();

                // For complex types, add tag size, calculate content size, add length + content
                TagCodeHelper.AddTagSize(_sb, fieldId, WireType.Len, calculatorVar);

                // Check if element type is polymorphic (requires dispatcher method with ProtoInclude wrapper)
                bool isPolymorphic = IsElementTypePolymorphic(elementTypeName);

                // Calculate item content size
                _sb.AppendIndentedLine("var itemCalc = new global::GProtobuf.Core.WriteSizeCalculator();");

                // ContentSize handles dispatcher logic for polymorphic types
                var qualifiedSizeCall = GetQualifiedCalculateContentSizeCall(elementTypeName, elementClassName);

                _sb.AppendIndentedLine($"{qualifiedSizeCall}(ref itemCalc, item);");

                // Add length varint size + content size
                _sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)itemCalc.Length);");
                _sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(itemCalc.Length);");
            }

            _sb.EndBlock(); // foreach
            _sb.EndBlock(); // if
            _sb.EndBlock(); // scope
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Builds fully qualified call to SpanReaders.Read{ClassName}Content method.
        /// </summary>
        private string GetQualifiedReadContentCall(string elementTypeName, string elementClassName)
        {
            var ns = GetTypeNamespace(elementTypeName);
            if (ns == null)
            {
                // No namespace, call directly
                return $"Read{elementClassName}Content";
            }
            return $"global::{ns}.Serialization.SpanReaders.Read{elementClassName}Content";
        }

        /// <summary>
        /// Builds fully qualified call to SpanReaders.Read{ClassName} method (dispatcher with ProtoInclude).
        /// </summary>
        private string GetQualifiedReadCall(string elementTypeName, string elementClassName)
        {
            var ns = GetTypeNamespace(elementTypeName);
            if (ns == null)
            {
                // No namespace, call directly
                return $"Read{elementClassName}";
            }
            return $"global::{ns}.Serialization.SpanReaders.Read{elementClassName}";
        }

        /// <summary>
        /// Builds fully qualified call to StreamWriters/BufferWriters.Write{ClassName} or Write{ClassName}Content method.
        /// Uses WriteX for simple types without callbacks (where WriteXContent is not generated).
        /// </summary>
        private string GetQualifiedWriteContentCall(string elementTypeName, string elementClassName, string writerClassName)
        {
            // Determine write method: use WriteX for simple types without callbacks (WriteXContent not generated),
            // and for unknown cross-project types (WriteX always exists as safe default)
            var typeDef = _registry.GetByFullName(TypeMapping.NormalizeTypeName(elementTypeName));
            bool needsContent = typeDef != null
                && !_registry.IsDerivedType(typeDef.FullName)
                && (typeDef.ProtoIncludes != null && typeDef.ProtoIncludes.Count > 0
                    || (typeDef.BeforeSerializationCallbacks != null && typeDef.BeforeSerializationCallbacks.Count > 0)
                    || (typeDef.AfterSerializationCallbacks != null && typeDef.AfterSerializationCallbacks.Count > 0));
            var methodName = needsContent ? $"Write{elementClassName}Content" : $"Write{elementClassName}";

            var ns = GetTypeNamespace(elementTypeName);
            if (ns == null)
            {
                return methodName;
            }
            return $"global::{ns}.Serialization.{writerClassName}.{methodName}";
        }

        /// <summary>
        /// Builds fully qualified call to SizeCalculators.Calculate{ClassName}ContentSize method.
        /// </summary>
        private string GetQualifiedCalculateContentSizeCall(string elementTypeName, string elementClassName)
        {
            var ns = GetTypeNamespace(elementTypeName);
            if (ns == null)
            {
                return $"SizeCalculators.Calculate{elementClassName}ContentSize";
            }
            return $"global::{ns}.Serialization.SizeCalculators.Calculate{elementClassName}ContentSize";
        }

        /// <summary>
        /// Extracts namespace from full type name.
        /// Returns null if no namespace or if it's a primitive type.
        /// </summary>
        private string GetTypeNamespace(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            // Use TypeRegistry for accurate namespace resolution (handles nested types correctly)
            if (_registry != null)
            {
                var ns = _registry.GetNamespaceForType(fullTypeName);
                if (!string.IsNullOrEmpty(ns))
                {
                    return ns;
                }
            }

            // Fallback: check if typename contains dot (may be incorrect for nested types)
            var dotIndex = fullTypeName.LastIndexOf('.');
            return dotIndex > 0 ? fullTypeName.Substring(0, dotIndex) : null;
        }

        /// <summary>
        /// Checks if collection element type requires dispatcher method (ProtoInclude wrapper).
        /// Returns true if:
        /// 1. Element type itself has ProtoInclude (is base type with derived types)
        /// 2. Element's base type has ProtoInclude (element is derived from polymorphic base)
        ///
        /// Example: GenericDevice[] where GenericDevice : DeviceBaseV1
        /// - DeviceBaseV1 has [ProtoInclude(100, typeof(GenericDevice))]
        /// - Even though field is GenericDevice[] (concrete), protobuf-net Level200 writes ProtoInclude wrapper
        /// - Must use WriteGenericDevice() dispatcher, not WriteGenericDeviceContent()
        /// </summary>
        private bool IsElementTypePolymorphic(string elementTypeName)
        {
            // Check if element type itself has derived types (is polymorphic base)
            if (_registry.GetAllDerivedTypes(elementTypeName).Count > 0)
            {
                return true;
            }

            // Check if element's BASE type is polymorphic
            // This handles the case: GenericDevice[] where DeviceBaseV1 has ProtoInclude
            var baseTypeName = _registry.GetParent(elementTypeName);
            if (baseTypeName != null)
            {
                // Check if parent type has derived types (is polymorphic)
                if (_registry.GetAllDerivedTypes(baseTypeName).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds fully qualified call to StreamWriters/BufferWriters.Write{ClassName} method (dispatcher with ProtoInclude).
        /// </summary>
        private string GetQualifiedWriteCall(string elementTypeName, string elementClassName, string writerClassName)
        {
            var ns = GetTypeNamespace(elementTypeName);
            if (ns == null)
            {
                return $"Write{elementClassName}";
            }
            return $"global::{ns}.Serialization.{writerClassName}.Write{elementClassName}";
        }

        #endregion
    }
}
