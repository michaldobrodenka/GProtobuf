using System;
using GProtobuf.Generator.V2.Handlers.Core;

namespace GProtobuf.Generator.V2.Handlers
{
    /// <summary>
    /// Handles code generation for primitive types (int, long, float, string, etc.)
    /// Generates deserialization, serialization, and size calculation code.
    /// </summary>
    internal class PrimitiveHandler
    {
        private readonly TypeRegistry _registry;

        public PrimitiveHandler(TypeRegistry registry = null)
        {
            _registry = registry;
        }

        #region Type Classification

        /// <summary>
        /// Checks if this handler can handle the given type (single value, not collection).
        /// </summary>
        public bool CanHandle(string typeName)
        {
            return TypeMapping.IsSimpleType(typeName);
        }

        /// <summary>
        /// Checks if this handler can handle primitive collections (packed/non-packed arrays).
        /// </summary>
        public bool CanHandleCollection(string elementTypeName)
        {
            return TypeMapping.IsNonPackedArrayType(elementTypeName);
        }

        /// <summary>
        /// Checks if the given type is an enum.
        /// </summary>
        private bool IsEnumType(string typeName)
        {
            if (_registry == null) return false;
            var normalized = TypeMapping.NormalizeTypeName(typeName);
            return _registry.IsEnum(typeName) || _registry.IsEnum(normalized);
        }

        #endregion

        #region Deserialization

        /// <summary>
        /// Generates read code for a single primitive value.
        /// </summary>
        public void GenerateRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string typeName,
            DataFormat format,
            string readerVar = "reader",
            string wireTypeVar = "wireType")
        {
            var readExpr = TypeMapping.GetReadExpression(typeName, format, readerVar, wireTypeVar);
            if (readExpr != null)
            {
                sb.AppendIndentedLine($"{targetVar} = {readExpr};");
            }
        }

        /// <summary>
        /// Generates read code for packed primitive array.
        /// Returns the array directly.
        /// </summary>
        public void GeneratePackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            string readerVar = "reader")
        {
            GeneratePackedArrayRead(sb, targetVar, elementTypeName, format, CollectionKind.Array, null, readerVar);
        }

        /// <summary>
        /// Generates read code for packed primitive array with collection kind support.
        /// </summary>
        public void GeneratePackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader")
        {
            var readExpr = TypeMapping.GetPackedArrayReadExpression(elementTypeName, format, readerVar);
            if (readExpr != null)
            {
                // For custom collections, generate separate loop (no constructor with array parameter)
                if (collectionKind == CollectionKind.ConcreteCollection && !string.IsNullOrEmpty(collectionTypeName))
                {
                    bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                    bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                       collectionTypeName.StartsWith("System.Collections.Generic.List<");

                    if (!isSystemHashSet && !isSystemList)
                    {
                        // Custom collection - read to temp array first, then create instance and add elements
                        sb.AppendIndentedLine($"var tempArray = {readExpr};");
                        sb.AppendIndentedLine($"{targetVar} = new global::{collectionTypeName}();");
                        sb.AppendIndentedLine($"foreach (var item in tempArray) {targetVar}.Add(item);");
                    }
                    else
                    {
                        var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, readExpr);
                        sb.AppendIndentedLine(assignment);
                    }
                }
                else
                {
                    var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, readExpr);
                    sb.AppendIndentedLine(assignment);
                }
            }
        }

        /// <summary>
        /// Generates read code for non-packed repeated primitive field.
        /// Uses UnmanagedArrayBuilder for efficiency (unified byte[] pool).
        /// </summary>
        public void GenerateNonPackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string readerVar = "reader")
        {
            GenerateNonPackedArrayRead(sb, targetVar, elementTypeName, format, fieldId, CollectionKind.Array, null, readerVar);
        }

        /// <summary>
        /// Generates read code for non-packed repeated primitive field with collection kind support.
        /// </summary>
        public void GenerateNonPackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            CollectionKind collectionKind,
            string collectionTypeName,
            string readerVar = "reader",
            string wireTypeVar = "wireType",
            string fieldIdVar = "fieldId")
        {
            var normalized = TypeMapping.NormalizeTypeName(elementTypeName);
            var shortType = TypeMapping.GetShortTypeName(elementTypeName);

            // Check if element type is enum
            bool isEnum = IsEnumType(elementTypeName);

            // For enums, use cast from ReadVarInt32, otherwise use TypeMapping
            var elementReadExpr = isEnum
                ? $"({shortType}){readerVar}.ReadVarInt32()"
                : TypeMapping.GetElementReadExpression(elementTypeName, format, readerVar);

            var expectedWireType = isEnum ? "WireType.VarInt" : TypeMapping.GetWireTypeString(elementTypeName, format);

            // Generate unique loop variable names to avoid conflicts with outer scope
            var wireTypeLoopVar = wireTypeVar + "_loop";
            var fieldIdLoopVar = fieldIdVar + "_loop";

            // For managed types (string, Guid, TimeSpan), use List<T>
            if (normalized == "System.String" || normalized == "System.Guid" || normalized == "System.TimeSpan")
            {
                sb.AppendIndentedLine($"var tempList = new global::System.Collections.Generic.List<{shortType}>();");
                sb.AppendIndentedLine($"var {wireTypeLoopVar} = {wireTypeVar};");
                sb.AppendIndentedLine($"var {fieldIdLoopVar} = {fieldIdVar};");
                sb.AppendIndentedLine($"while ({fieldIdLoopVar} == {fieldId} && {wireTypeLoopVar} == {expectedWireType})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"tempList.Add({elementReadExpr});");
                sb.AppendIndentedLine($"// Level200: Check EndOfData BEFORE peek to avoid reading past buffer");
                sb.AppendIndentedLine($"if ({readerVar}.IsEnd) break;");
                sb.AppendIndentedLine($"var p = {readerVar}.Position;");
                sb.AppendIndentedLine($"({wireTypeLoopVar}, {fieldIdLoopVar}) = {readerVar}.ReadKey();");
                sb.AppendIndentedLine($"if ({fieldIdLoopVar} != {fieldId})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{readerVar}.Position = p; // rewind");
                sb.AppendIndentedLine($"break;");
                sb.EndBlock();
                sb.EndBlock();

                // Generate assignment based on collection kind
                // For custom collections, generate separate loop (no constructor with array parameter)
                if (collectionKind == CollectionKind.ConcreteCollection && !string.IsNullOrEmpty(collectionTypeName))
                {
                    bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                    bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                       collectionTypeName.StartsWith("System.Collections.Generic.List<");

                    if (!isSystemHashSet && !isSystemList)
                    {
                        // Custom collection - create empty instance and add elements via loop
                        sb.AppendIndentedLine($"{targetVar} = new global::{collectionTypeName}();");
                        sb.AppendIndentedLine($"foreach (var item in tempList) {targetVar}.Add(item);");
                    }
                    else if (isSystemList)
                    {
                        // tempList is already a List<T>, assign directly
                        sb.AppendIndentedLine($"{targetVar} = tempList;");
                    }
                    else
                    {
                        // HashSet case
                        var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, "tempList.ToArray()");
                        sb.AppendIndentedLine(assignment);
                    }
                }
                else if (collectionKind == CollectionKind.InterfaceCollection)
                {
                    // Interface collections (IList<T>, ICollection<T>, IEnumerable<T>) - tempList is already List<T>
                    sb.AppendIndentedLine($"{targetVar} = tempList;");
                }
                else
                {
                    var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, "tempList.ToArray()");
                    sb.AppendIndentedLine(assignment);
                }
            }
            else
            {
                // For unmanaged types, use UnmanagedArrayBuilder (unified byte[] pool)
                sb.AppendIndentedLine($"using var resultCollector = new global::GProtobuf.Core.UnmanagedArrayBuilder<{shortType}>(stackalloc {shortType}[256 / sizeof({shortType})], 1024);");
                sb.AppendIndentedLine($"var {wireTypeLoopVar} = {wireTypeVar};");
                sb.AppendIndentedLine($"var {fieldIdLoopVar} = {fieldIdVar};");
                sb.AppendIndentedLine($"while ({fieldIdLoopVar} == {fieldId} && {wireTypeLoopVar} == {expectedWireType})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"resultCollector.Add({elementReadExpr});");
                sb.AppendIndentedLine($"// Level200: Check IsEnd BEFORE peek to avoid reading past buffer");
                sb.AppendIndentedLine($"if ({readerVar}.IsEnd) break;");
                sb.AppendIndentedLine($"var p = {readerVar}.Position;");
                sb.AppendIndentedLine($"({wireTypeLoopVar}, {fieldIdLoopVar}) = {readerVar}.ReadKey();");
                sb.AppendIndentedLine($"if ({fieldIdLoopVar} != {fieldId})");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"{readerVar}.Position = p; // rewind");
                sb.AppendIndentedLine($"break;");
                sb.EndBlock();
                sb.EndBlock();

                // Generate assignment based on collection kind
                // For custom collections, generate separate loop (no constructor with array parameter)
                if (collectionKind == CollectionKind.ConcreteCollection && !string.IsNullOrEmpty(collectionTypeName))
                {
                    bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                    bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                       collectionTypeName.StartsWith("System.Collections.Generic.List<");

                    if (!isSystemHashSet && !isSystemList)
                    {
                        // Custom collection - create empty instance and add elements via loop
                        sb.AppendIndentedLine($"{targetVar} = new global::{collectionTypeName}();");
                        sb.AppendIndentedLine($"foreach (var item in resultCollector.ToArray()) {targetVar}.Add(item);");
                    }
                    else if (isSystemList)
                    {
                        // Use ToList() directly - avoids intermediate array allocation
                        sb.AppendIndentedLine($"{targetVar} = resultCollector.ToList();");
                    }
                    else
                    {
                        // HashSet case
                        var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, "resultCollector.ToArray()");
                        sb.AppendIndentedLine(assignment);
                    }
                }
                else if (collectionKind == CollectionKind.InterfaceCollection)
                {
                    // Interface collections (IList<T>, ICollection<T>, IEnumerable<T>) use List<T> internally
                    // Use ToList() directly - avoids intermediate array allocation
                    sb.AppendIndentedLine($"{targetVar} = resultCollector.ToList();");
                }
                else
                {
                    var assignment = GenerateCollectionAssignment(targetVar, elementTypeName, collectionKind, collectionTypeName, "resultCollector.ToArray()");
                    sb.AppendIndentedLine(assignment);
                }
            }
        }

        /// <summary>
        /// Generates dual-mode packed array read (Level200 compliance).
        /// Handles BOTH packed (WireType.Len) and unpacked (primitive WireType) with MERGE semantics.
        /// Supports duplicate field numbers by merging into existing collection.
        /// </summary>
        /// <remarks>
        /// Level200 requirement: Must accept both packed and unpacked encoding for repeated primitives.
        /// Packed: [tag=Len][length][val1][val2][val3]
        /// Unpacked: [tag=VarInt][val1] (backward compatibility, single element MERGE)
        ///
        /// MERGE behavior: Multiple packed blocks with same field number concatenate values.
        /// Example: [field 1 packed: [1,2,3]] [field 1 packed: [4,5]] → result = [1,2,3,4,5]
        /// </remarks>
        public void GenerateDualModePackedArrayRead(
            StringBuilderWithIndent sb,
            string targetVar,
            string elementTypeName,
            DataFormat format,
            CollectionKind collectionKind,
            string collectionTypeName,
            string wireTypeVar = "wireType",
            string readerVar = "reader",
            bool useStreamLimits = false)
        {
            var normalized = TypeMapping.NormalizeTypeName(elementTypeName);
            var shortType = TypeMapping.GetShortTypeName(elementTypeName);

            // Check if element type is enum
            bool isEnum = IsEnumType(elementTypeName);

            // For enums, use cast from ReadVarInt32, otherwise use TypeMapping
            var elementReadExpr = isEnum
                ? $"({shortType}){readerVar}.ReadVarInt32()"
                : TypeMapping.GetElementReadExpression(elementTypeName, format, readerVar);

            var packedWireType = "WireType.Len";
            var unpackedWireType = isEnum ? "WireType.VarInt" : TypeMapping.GetWireTypeString(elementTypeName, format);

            // Determine if we can add directly to collection (List, ICollection, IList, CustomCollection) or need tempList (Array, HashSet, IEnumerable)
            // IEnumerable is read-only, so we need tempList for it
            // CustomCollection and CustomEnumerable implement IEnumerable<T> + Add(T), so they support direct addition
            bool isListLike = (collectionKind == CollectionKind.ConcreteCollection &&
                              collectionTypeName != null &&
                              (collectionTypeName.Contains("List<") || collectionTypeName.Contains("System.Collections.Generic.List<"))) ||
                             (collectionKind == CollectionKind.InterfaceCollection &&
                              collectionTypeName != null &&
                              !collectionTypeName.Contains("IEnumerable<") && !collectionTypeName.Contains("System.Collections.Generic.IEnumerable<")) ||
                             collectionKind == CollectionKind.CustomCollection ||
                             collectionKind == CollectionKind.CustomEnumerable;

            // Generate wire type check for dual mode
            sb.AppendIndentedLine($"if ({wireTypeVar} == {packedWireType})");
            sb.StartNewBlock();
            sb.AppendIndentedLine("// Packed encoding (Level200)");

            // Initialize collection if needed (MERGE semantics)
            GenerateCollectionInitialization(sb, targetVar, shortType, collectionKind, collectionTypeName);

            sb.AppendIndentedLine($"int length = {readerVar}.ReadVarInt32();");

            if (useStreamLimits)
            {
                // StreamReader: Use PushLimit/PopLimit for proper limit handling
                sb.AppendIndentedLine($"var packedOldLimit = {readerVar}.PushLimit(length);");
                sb.AppendIndentedLine($"while (!{readerVar}.IsEnd)");
            }
            else
            {
                // SpanReader: Use Position + length for end detection
                sb.AppendIndentedLine($"int endPos = {readerVar}.Position + length;");
                sb.AppendIndentedLine($"while ({readerVar}.Position < endPos)");
            }
            sb.StartNewBlock();

            if (isListLike)
            {
                sb.AppendIndentedLine($"{targetVar}.Add({elementReadExpr});");
            }
            else
            {
                // Use temp list for Array/HashSet
                sb.AppendIndentedLine($"tempList.Add({elementReadExpr});");
            }

            sb.EndBlock();

            if (useStreamLimits)
            {
                sb.AppendIndentedLine($"{readerVar}.PopLimit(packedOldLimit);");
            }

            // Convert temp list to final collection type if needed (for Array/HashSet)
            if (!isListLike)
            {
                GenerateCollectionMerge(sb, targetVar, shortType, collectionKind, collectionTypeName);
            }

            sb.EndBlock();
            sb.AppendIndentedLine($"else if ({wireTypeVar} == {unpackedWireType})");
            sb.StartNewBlock();
            sb.AppendIndentedLine("// Unpacked encoding (backward compatibility, single element)");

            // Initialize collection if needed (MERGE semantics)
            GenerateCollectionInitialization(sb, targetVar, shortType, collectionKind, collectionTypeName);

            // Add single element
            if (isListLike)
            {
                sb.AppendIndentedLine($"{targetVar}.Add({elementReadExpr});");
            }
            else
            {
                sb.AppendIndentedLine($"tempList.Add({elementReadExpr});");
                GenerateCollectionMerge(sb, targetVar, shortType, collectionKind, collectionTypeName);
            }

            sb.EndBlock();
            sb.AppendIndentedLine("else");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"throw new global::System.InvalidOperationException($\"Invalid wire type {{{wireTypeVar}}} for repeated primitive field. Expected {packedWireType} (packed) or {unpackedWireType} (unpacked).\");");
            sb.EndBlock();
        }

        /// <summary>
        /// Generates collection initialization code (null-coalescing assignment for MERGE).
        /// </summary>
        private void GenerateCollectionInitialization(
            StringBuilderWithIndent sb,
            string targetVar,
            string shortElementType,
            CollectionKind collectionKind,
            string collectionTypeName)
        {
            switch (collectionKind)
            {
                case CollectionKind.Array:
                    // Array requires temp list for MERGE
                    sb.AppendIndentedLine($"var tempList = {targetVar} != null ? new global::System.Collections.Generic.List<{shortElementType}>({targetVar}) : new global::System.Collections.Generic.List<{shortElementType}>();");
                    break;

                case CollectionKind.InterfaceCollection:
                    // IEnumerable is read-only, needs tempList. ICollection/IList can be initialized as List
                    if (collectionTypeName != null &&
                        (collectionTypeName.Contains("IEnumerable<") || collectionTypeName.Contains("System.Collections.Generic.IEnumerable<")))
                    {
                        // IEnumerable requires temp list (read-only interface)
                        sb.AppendIndentedLine($"var tempList = {targetVar} != null ? new global::System.Collections.Generic.List<{shortElementType}>({targetVar}) : new global::System.Collections.Generic.List<{shortElementType}>();");
                    }
                    else
                    {
                        // ICollection, IList - initialize as List for MERGE
                        sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
                    }
                    break;

                case CollectionKind.ConcreteCollection:
                    if (collectionTypeName != null)
                    {
                        // Check for System.Collections.Generic.HashSet specifically (not custom HashSet types)
                        bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                               collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                        bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.List<");

                        if (isSystemHashSet)
                        {
                            // System HashSet requires temp list for MERGE
                            sb.AppendIndentedLine($"var tempList = {targetVar} != null ? new global::System.Collections.Generic.List<{shortElementType}>({targetVar}) : new global::System.Collections.Generic.List<{shortElementType}>();");
                        }
                        else if (isSystemList)
                        {
                            // System List can MERGE directly
                            sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
                        }
                        else
                        {
                            // Fallback: custom collection type - use temp list for packed arrays
                            sb.AppendIndentedLine($"var tempList = {targetVar} != null ? new global::System.Collections.Generic.List<{shortElementType}>({targetVar}) : new global::System.Collections.Generic.List<{shortElementType}>();");
                        }
                    }
                    else
                    {
                        sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
                    }
                    break;

                case CollectionKind.CustomCollection:
                case CollectionKind.CustomEnumerable:
                    // Custom collection types (e.g., CustomIntHashSet, CustomHashSet<T>)
                    // Initialize the actual custom type directly for MERGE semantics
                    if (!string.IsNullOrEmpty(collectionTypeName))
                    {
                        sb.AppendIndentedLine($"{targetVar} ??= new global::{collectionTypeName}();");
                    }
                    else
                    {
                        // Fallback to List if type is unknown
                        sb.AppendIndentedLine($"{targetVar} ??= new global::System.Collections.Generic.List<{shortElementType}>();");
                    }
                    break;
            }
        }

        /// <summary>
        /// Generates collection merge code (converts temp list and merges with existing collection).
        /// </summary>
        private void GenerateCollectionMerge(
            StringBuilderWithIndent sb,
            string targetVar,
            string shortElementType,
            CollectionKind collectionKind,
            string collectionTypeName)
        {
            switch (collectionKind)
            {
                case CollectionKind.Array:
                    sb.AppendIndentedLine($"{targetVar} = tempList.ToArray();");
                    break;

                case CollectionKind.InterfaceCollection:
                    // IEnumerable is read-only, convert from tempList
                    if (collectionTypeName != null &&
                        (collectionTypeName.Contains("IEnumerable<") || collectionTypeName.Contains("System.Collections.Generic.IEnumerable<")))
                    {
                        sb.AppendIndentedLine($"{targetVar} = tempList;");
                    }
                    break;

                case CollectionKind.ConcreteCollection:
                    if (collectionTypeName != null)
                    {
                        // Check for System.Collections.Generic.HashSet specifically (not custom HashSet types)
                        bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                               collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");

                        if (isSystemHashSet)
                        {
                            // System HashSet - create from tempList
                            sb.AppendIndentedLine($"if ({targetVar} == null)");
                            sb.StartNewBlock();
                            sb.AppendIndentedLine($"{targetVar} = new global::System.Collections.Generic.HashSet<{shortElementType}>(tempList);");
                            sb.EndBlock();
                            sb.AppendIndentedLine("else");
                            sb.StartNewBlock();
                            sb.AppendIndentedLine($"foreach (var item in tempList) {targetVar}.Add(item);");
                            sb.EndBlock();
                        }
                        else
                        {
                            // Custom collection type - instantiate custom type and add items from tempList
                            sb.AppendIndentedLine($"if ({targetVar} == null)");
                            sb.StartNewBlock();
                            sb.AppendIndentedLine($"{targetVar} = new global::{collectionTypeName}();");
                            sb.EndBlock();
                            sb.AppendIndentedLine($"foreach (var item in tempList) {targetVar}.Add(item);");
                        }
                    }
                    break;

                case CollectionKind.CustomCollection:
                case CollectionKind.CustomEnumerable:
                    // Custom collection types - instantiate custom type and add items from tempList
                    if (!string.IsNullOrEmpty(collectionTypeName))
                    {
                        sb.AppendIndentedLine($"if ({targetVar} == null)");
                        sb.StartNewBlock();
                        sb.AppendIndentedLine($"{targetVar} = new global::{collectionTypeName}();");
                        sb.EndBlock();
                        sb.AppendIndentedLine($"foreach (var item in tempList) {targetVar}.Add(item);");
                    }
                    break;
            }
        }

        /// <summary>
        /// Generates the correct collection assignment based on CollectionKind.
        /// </summary>
        private string GenerateCollectionAssignment(
            string targetVar,
            string elementTypeName,
            CollectionKind collectionKind,
            string collectionTypeName,
            string arrayExpr)
        {
            var shortElementType = TypeMapping.GetShortTypeName(elementTypeName);

            switch (collectionKind)
            {
                case CollectionKind.Array:
                    return $"{targetVar} = {arrayExpr};";

                case CollectionKind.CustomCollection:
                case CollectionKind.CustomEnumerable:
                    // Custom collection types (e.g., CustomIntHashSet, CustomHashSet<T>)
                    // Create instance of the custom type directly
                    if (!string.IsNullOrEmpty(collectionTypeName))
                    {
                        return $"{targetVar} = new global::{collectionTypeName}({arrayExpr});";
                    }
                    // Fallback if no collection type name provided
                    return $"{targetVar} = {arrayExpr};";

                case CollectionKind.InterfaceCollection:
                    // Interface collections always use List as implementation
                    return $"{targetVar} = new global::System.Collections.Generic.List<{shortElementType}>({arrayExpr});";

                case CollectionKind.ConcreteCollection:
                    // Check for specific System collection types (not custom types)
                    if (collectionTypeName != null)
                    {
                        bool isSystemHashSet = collectionTypeName == "System.Collections.Generic.HashSet" ||
                                               collectionTypeName.StartsWith("System.Collections.Generic.HashSet<");
                        bool isSystemList = collectionTypeName == "System.Collections.Generic.List" ||
                                           collectionTypeName.StartsWith("System.Collections.Generic.List<");

                        if (isSystemHashSet)
                        {
                            return $"{targetVar} = new global::System.Collections.Generic.HashSet<{shortElementType}>({arrayExpr});";
                        }
                        else if (isSystemList)
                        {
                            return $"{targetVar} = new global::System.Collections.Generic.List<{shortElementType}>({arrayExpr});";
                        }
                        else
                        {
                            // Custom collection type - create instance of custom type
                            return $"{targetVar} = new global::{collectionTypeName}({arrayExpr});";
                        }
                    }
                    // Default to List if no collection type name
                    return $"{targetVar} = new global::System.Collections.Generic.List<{shortElementType}>({arrayExpr});";

                default:
                    return $"{targetVar} = {arrayExpr};";
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Generates write code for a single primitive value with tag.
        /// Includes default value check for non-nullable types.
        /// </summary>
        public void GenerateWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string typeName,
            DataFormat format,
            int fieldId,
            bool isNullable,
            bool isRequired = false,
            string writerVar = "writer")
        {
            var wireType = TypeMapping.GetWireType(typeName, format);

            // Check if type is supported
            if (!TypeMapping.IsSimpleType(typeName)) return;

            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            bool isReferenceType = normalizedType == "System.String" || normalizedType == "System.Byte[]";

            string valueExpr;

            // For reference types (string, byte[]), use local variable to avoid null check then null access
            if (isReferenceType && !isNullable && !isRequired)
            {
                sb.StartNewBlock(); // Scope block to avoid name collisions
                sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                sb.AppendIndentedLine("if (instanceValue != null)");
                sb.StartNewBlock();
                valueExpr = "instanceValue";
            }
            else if (isNullable)
            {
                sb.StartNewBlock(); // Scope block to avoid name collisions
                sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                sb.AppendIndentedLine("if (instanceValue.HasValue)");
                sb.StartNewBlock();
                valueExpr = "instanceValue.Value";
            }
            else if (!isRequired)
            {
                var defaultCheck = TypeMapping.GetDefaultValueCheck(typeName, sourceVar);
                if (defaultCheck != null)
                {
                    sb.StartNewBlock(); // Scope block to avoid name collisions
                    sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                    var localDefaultCheck = TypeMapping.GetDefaultValueCheck(typeName, "instanceValue");
                    sb.AppendIndentedLine($"if ({localDefaultCheck})");
                    sb.StartNewBlock();
                    valueExpr = "instanceValue";
                }
                else
                {
                    valueExpr = sourceVar;
                }
            }
            else
            {
                valueExpr = sourceVar;
            }

            // Write tag
            GenerateWriteTag(sb, fieldId, wireType, writerVar);

            // For nullable bool, we need to write the actual value (could be true or false)
            if (TypeMapping.IsBooleanType(typeName) && isNullable)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteBool({valueExpr});");
            }
            else
            {
                var actualWriteExpr = TypeMapping.GetWriteExpression(typeName, valueExpr, format, writerVar);
                sb.AppendIndentedLine($"{actualWriteExpr};");
            }

            // Close blocks
            if (isReferenceType && !isNullable && !isRequired)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
            else if (isNullable)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
            else if (!isRequired && TypeMapping.GetDefaultValueCheck(typeName, sourceVar) != null)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
        }

        /// <summary>
        /// Generates write code for packed primitive array.
        /// </summary>
        public void GeneratePackedArrayWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string writerVar = "writer")
        {
            sb.StartNewBlock(); // Scope block to avoid name collisions
            sb.AppendIndentedLine($"var collection = {sourceVar};");
            sb.AppendIndentedLine("if (collection != null)");
            sb.StartNewBlock();

            // Write Len tag
            GenerateWriteTag(sb, fieldId, WireType.Len, writerVar);

            // Calculate packed size first
            sb.AppendIndentedLine($"var calculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, "calculator");
            sb.AppendIndentedLine("foreach (var item in collection)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementSizeExpr};");
            sb.EndBlock();

            // Write length
            sb.AppendIndentedLine($"{writerVar}.WriteVarUInt32((uint)calculator.Length);");

            // Write elements
            var elementWriteExpr = TypeMapping.GetElementWriteExpression(elementTypeName, "item", format, writerVar);
            sb.AppendIndentedLine("foreach (var item in collection)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementWriteExpr};");
            sb.EndBlock();

            sb.EndBlock(); // if
            sb.EndBlock(); // scope
        }

        /// <summary>
        /// Generates write code for non-packed repeated primitive field.
        /// </summary>
        public void GenerateNonPackedArrayWrite(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string writerVar = "writer")
        {
            var normalizedType = TypeMapping.NormalizeTypeName(elementTypeName);
            var shortType = TypeMapping.GetShortTypeName(elementTypeName);

            // Check if element type is enum
            bool isEnum = IsEnumType(elementTypeName);

            // For enums, use VarInt wire type; otherwise use TypeMapping
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(elementTypeName, format);

            sb.StartNewBlock(); // Scope block to avoid name collisions
            sb.AppendIndentedLine($"var collection = {sourceVar};");
            sb.AppendIndentedLine("if (collection != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine("foreach (var item in collection)");
            sb.StartNewBlock();

            // Add null validation for string and byte[] (Level200 compatibility)
            bool isString = normalizedType == "System.String";
            bool isByteArray = normalizedType == "System.Byte[]";

            if (isString || isByteArray)
            {
                var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                // Extract just the class name for error message
                var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                sb.AppendIndentedLine("if (item == null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                sb.EndBlock();
            }

            GenerateWriteTag(sb, fieldId, wireType, writerVar);

            // For enums, cast to int and write as varint; otherwise use TypeMapping
            if (isEnum)
            {
                sb.AppendIndentedLine($"{writerVar}.WriteVarInt32((int)item);");
            }
            else
            {
                var elementWriteExpr = TypeMapping.GetElementWriteExpression(elementTypeName, "item", format, writerVar);
                sb.AppendIndentedLine($"{elementWriteExpr};");
            }

            sb.EndBlock(); // foreach
            sb.EndBlock(); // if
            sb.EndBlock(); // scope
        }

        #endregion

        #region Size Calculation

        /// <summary>
        /// Generates size calculation for a single primitive value.
        /// </summary>
        public void GenerateSize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string typeName,
            DataFormat format,
            int fieldId,
            bool isNullable,
            bool isRequired = false,
            string calculatorVar = "calculator")
        {
            var wireType = TypeMapping.GetWireType(typeName, format);

            var normalizedType = TypeMapping.NormalizeTypeName(typeName);
            bool isReferenceType = normalizedType == "System.String" || normalizedType == "System.Byte[]";

            string valueExpr;

            // For reference types (string, byte[]), use local variable to avoid null check then null access
            if (isReferenceType && !isNullable && !isRequired)
            {
                sb.StartNewBlock(); // Scope block to avoid name collisions
                sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                sb.AppendIndentedLine("if (instanceValue != null)");
                sb.StartNewBlock();
                valueExpr = "instanceValue";
            }
            else if (isNullable)
            {
                sb.StartNewBlock(); // Scope block to avoid name collisions
                sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                sb.AppendIndentedLine("if (instanceValue.HasValue)");
                sb.StartNewBlock();
                valueExpr = "instanceValue.Value";
            }
            else if (!isRequired)
            {
                var defaultCheck = TypeMapping.GetDefaultValueCheck(typeName, sourceVar);
                if (defaultCheck != null)
                {
                    sb.StartNewBlock(); // Scope block to avoid name collisions
                    sb.AppendIndentedLine($"var instanceValue = {sourceVar};");
                    var localDefaultCheck = TypeMapping.GetDefaultValueCheck(typeName, "instanceValue");
                    sb.AppendIndentedLine($"if ({localDefaultCheck})");
                    sb.StartNewBlock();
                    valueExpr = "instanceValue";
                }
                else
                {
                    valueExpr = sourceVar;
                }
            }
            else
            {
                valueExpr = sourceVar;
            }

            // For nullable bool, we need to calculate size for the actual value (could be true or false)
            if (TypeMapping.IsBooleanType(typeName) && isNullable)
            {
                GenerateSizeTag(sb, fieldId, wireType, calculatorVar);
                sb.AppendIndentedLine($"{calculatorVar}.WriteBool({valueExpr});");
            }
            else
            {
                // Try to combine tag + value size for fixed-size types (optimization)
                if (!TagCodeHelper.TryAddCombinedTagAndValueSize(sb, fieldId, wireType, typeName, format, calculatorVar))
                {
                    // Variable size - tag was added, now add value size
                    var sizeExpr = TypeMapping.GetSizeExpression(typeName, valueExpr, format, calculatorVar);
                    sb.AppendIndentedLine($"{sizeExpr};");
                }
            }

            // Close blocks
            if (isReferenceType && !isNullable && !isRequired)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
            else if (isNullable)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
            else if (!isRequired && TypeMapping.GetDefaultValueCheck(typeName, sourceVar) != null)
            {
                sb.EndBlock(); // if
                sb.EndBlock(); // scope
            }
        }

        /// <summary>
        /// Generates size calculation for packed primitive array.
        /// </summary>
        public void GeneratePackedArraySize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string calculatorVar = "calculator")
        {
            sb.StartNewBlock(); // Scope block to avoid name collisions
            sb.AppendIndentedLine($"var collection = {sourceVar};");
            sb.AppendIndentedLine("if (collection != null)");
            sb.StartNewBlock();

            // Add Len tag size
            GenerateSizeTag(sb, fieldId, WireType.Len, calculatorVar);

            // Calculate packed content size
            sb.AppendIndentedLine($"var tempCalculator = new global::GProtobuf.Core.WriteSizeCalculator();");
            var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, "tempCalculator");
            sb.AppendIndentedLine("foreach (var item in collection)");
            sb.StartNewBlock();
            sb.AppendIndentedLine($"{elementSizeExpr};");
            sb.EndBlock();

            // Add length varint + content size
            sb.AppendIndentedLine($"{calculatorVar}.WriteVarUInt32((uint)tempCalculator.Length);");
            sb.AppendIndentedLine($"{calculatorVar}.AddByteLength(tempCalculator.Length);");

            sb.EndBlock(); // if
            sb.EndBlock(); // scope
        }

        /// <summary>
        /// Generates size calculation for non-packed repeated primitive field.
        /// </summary>
        public void GenerateNonPackedArraySize(
            StringBuilderWithIndent sb,
            string sourceVar,
            string elementTypeName,
            DataFormat format,
            int fieldId,
            string calculatorVar = "calculator")
        {
            var normalizedType = TypeMapping.NormalizeTypeName(elementTypeName);

            // Check if element type is enum
            bool isEnum = IsEnumType(elementTypeName);

            // For enums, use VarInt wire type; otherwise use TypeMapping
            var wireType = isEnum ? WireType.VarInt : TypeMapping.GetWireType(elementTypeName, format);

            sb.StartNewBlock(); // Scope block to avoid name collisions
            sb.AppendIndentedLine($"var collection = {sourceVar};");
            sb.AppendIndentedLine("if (collection != null)");
            sb.StartNewBlock();
            sb.AppendIndentedLine("foreach (var item in collection)");
            sb.StartNewBlock();

            // Add null validation for string and byte[] (Level200 compatibility)
            bool isString = normalizedType == "System.String";
            bool isByteArray = normalizedType == "System.Byte[]";

            if (isString || isByteArray)
            {
                var shortTypeName = TypeMapping.GetShortTypeName(elementTypeName);
                // Extract just the class name for error message
                var simpleTypeName = shortTypeName.Contains(".") ? shortTypeName.Substring(shortTypeName.LastIndexOf('.') + 1) : shortTypeName;
                sb.AppendIndentedLine("if (item == null)");
                sb.StartNewBlock();
                sb.AppendIndentedLine($"throw new System.InvalidOperationException(\"An element of type {simpleTypeName} was null; this might be as contents in a list/array\");");
                sb.EndBlock();
            }

            GenerateSizeTag(sb, fieldId, wireType, calculatorVar);

            // For enums, cast to int and calculate varint size; otherwise use TypeMapping
            if (isEnum)
            {
                sb.AppendIndentedLine($"{calculatorVar}.WriteVarInt32((int)item);");
            }
            else
            {
                var elementSizeExpr = TypeMapping.GetElementSizeExpression(elementTypeName, "item", format, calculatorVar);
                sb.AppendIndentedLine($"{elementSizeExpr};");
            }

            sb.EndBlock(); // foreach
            sb.EndBlock(); // if
            sb.EndBlock(); // scope
        }

        #endregion

        #region Tag Generation

        /// <summary>
        /// Generates code to write a precomputed tag.
        /// </summary>
        private void GenerateWriteTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string writerVar)
        {
            TagCodeHelper.WriteTag(sb, fieldId, wireType, writerVar);
        }

        /// <summary>
        /// Generates code to add tag size to calculator.
        /// </summary>
        private void GenerateSizeTag(StringBuilderWithIndent sb, int fieldId, WireType wireType, string calculatorVar)
        {
            TagCodeHelper.AddTagSize(sb, fieldId, wireType, calculatorVar);
        }

        #endregion
    }
}
