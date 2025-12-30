using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace GProtobuf.Generator.V2.CodeGeneration
{
    /// <summary>
    /// Generates TupleValue structs for Tuple serialization.
    /// These structs are used as zero-allocation intermediate objects during serialization/deserialization.
    /// Using struct ensures no heap allocations for the TupleValue wrapper itself.
    /// </summary>
    internal class TupleValueGenerator
    {
        private readonly StringBuilderWithIndent _sb;
        private readonly VirtualTupleTypeRegistry _tupleRegistry;

        public TupleValueGenerator(
            StringBuilderWithIndent sb,
            VirtualTupleTypeRegistry tupleRegistry)
        {
            _sb = sb;
            _tupleRegistry = tupleRegistry;
        }

        /// <summary>
        /// Generates all TupleValue structs for registered tuple types.
        /// </summary>
        public void GenerateAllTupleValueStructs()
        {
            var allTupleTypes = _tupleRegistry.GetAllTypes();
            if (allTupleTypes == null || allTupleTypes.Count == 0)
                return;

            _sb.AppendLine("");
            _sb.AppendIndentedLine("// Generated TupleValue structs for Tuple serialization");

            foreach (var tupleInfo in allTupleTypes)
            {
                GenerateTupleValueStruct(tupleInfo);
            }
        }

        /// <summary>
        /// Generates methods that work directly with TupleValue structs.
        /// These methods wrap the existing Tuple...Content methods.
        /// </summary>
        public void GenerateTupleValueMethods(string generatorType)
        {
            var allTupleTypes = _tupleRegistry.GetAllTypes();
            if (allTupleTypes == null || allTupleTypes.Count == 0)
                return;

            _sb.AppendLine("");
            _sb.AppendIndentedLine($"// TupleValue methods for {generatorType}");

            foreach (var tupleInfo in allTupleTypes)
            {
                var structName = GetTupleValueStructName(tupleInfo);
                var methodName = tupleInfo.SafeName;

                switch (generatorType)
                {
                    case "SpanReaders":
                        GenerateReadTupleValueMethod(structName, methodName, tupleInfo);
                        break;
                    case "StreamWriters":
                        GenerateWriteTupleValueMethod(structName, methodName, tupleInfo, "global::GProtobuf.Core.StreamWriter", "StreamWriters");
                        break;
                    case "BufferWriters":
                        GenerateWriteTupleValueMethod(structName, methodName, tupleInfo, "global::GProtobuf.Core.BufferWriter", "BufferWriters");
                        break;
                    case "SizeCalculators":
                        GenerateSizeTupleValueMethod(structName, methodName, tupleInfo);
                        break;
                }
            }
        }

        private void GenerateReadTupleValueMethod(string structName, string methodName, TupleTypeInfo tupleInfo)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static {structName} Read{structName}(ref SpanReader reader)");
            _sb.StartNewBlock();

            // Call the existing Read...Content method
            _sb.AppendIndentedLine($"var tuple = SpanReaders.Read{methodName}Content(ref reader);");

            // Flatten the tuple and convert to struct
            var flattenedTypes = FlattenTupleTypes(tupleInfo.ItemTypes);

            _sb.AppendIndentedLine($"return new {structName}");
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Generate flattened item assignments
            GenerateFlattenedTupleToStructAssignments(tupleInfo.ItemTypes, "tuple", flattenedTypes.Count);

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("};");

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates assignments from nested tuple to flat struct.
        /// Example: Item1 = tuple.Item1, Item2 = tuple.Item2.Item1, Item3 = tuple.Item2.Item2
        /// </summary>
        private void GenerateFlattenedTupleToStructAssignments(List<string> itemTypes, string tupleVar, int totalItems)
        {
            int currentItem = 1;
            GenerateFlattenedAssignmentsRecursive(itemTypes, tupleVar, ref currentItem, totalItems);
        }

        private void GenerateFlattenedAssignmentsRecursive(List<string> itemTypes, string tupleVar, ref int currentItemIndex, int totalItems)
        {
            for (int i = 0; i < itemTypes.Count; i++)
            {
                var itemType = itemTypes[i];
                var itemAccess = $"{tupleVar}.Item{i + 1}";

                if (TupleHandler.IsTupleType(itemType))
                {
                    // Nested tuple - recurse
                    var nestedTypes = TupleHandler.ParseTupleTypes(itemType);
                    GenerateFlattenedAssignmentsRecursive(nestedTypes, itemAccess, ref currentItemIndex, totalItems);
                }
                else
                {
                    // Leaf item - generate assignment
                    var comma = currentItemIndex < totalItems ? "," : "";
                    _sb.AppendIndentedLine($"Item{currentItemIndex} = {itemAccess}{comma}");
                    currentItemIndex++;
                }
            }
        }

        private void GenerateWriteTupleValueMethod(string structName, string methodName, TupleTypeInfo tupleInfo, string writerTypeName, string generatorClassName)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static void Write{structName}(ref {writerTypeName} writer, {structName} tupleValue)");
            _sb.StartNewBlock();

            // Convert flat struct back to nested Tuple
            int currentItemIndex = 1;
            var tupleConstruction = GenerateNestedTupleConstruction(tupleInfo.ItemTypes, ref currentItemIndex);
            _sb.AppendIndentedLine($"var tuple = {tupleConstruction};");

            // Call the existing Write...Content method
            _sb.AppendIndentedLine($"{generatorClassName}.Write{methodName}Content(ref writer, tuple);");

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates nested tuple construction from flat struct items.
        /// Example: new System.Tuple&lt;string, System.Tuple&lt;int, bool&gt;&gt;(tupleValue.Item1, new System.Tuple&lt;int, bool&gt;(tupleValue.Item2, tupleValue.Item3))
        /// </summary>
        private string GenerateNestedTupleConstruction(List<string> itemTypes, ref int currentItemIndex)
        {
            if (itemTypes.Count == 0)
                return "";

            // Build the tuple type name
            var tupleTypeName = BuildTupleTypeName(itemTypes);

            // Build the constructor arguments
            var args = new List<string>();
            foreach (var itemType in itemTypes)
            {
                if (TupleHandler.IsTupleType(itemType))
                {
                    // Nested tuple - recurse
                    var nestedTypes = TupleHandler.ParseTupleTypes(itemType);
                    var nestedConstruction = GenerateNestedTupleConstruction(nestedTypes, ref currentItemIndex);
                    args.Add(nestedConstruction);
                }
                else
                {
                    // Leaf item - use struct item
                    args.Add($"tupleValue.Item{currentItemIndex}");
                    currentItemIndex++;
                }
            }

            return $"new {tupleTypeName}({string.Join(", ", args)})";
        }

        /// <summary>
        /// Builds the full tuple type name from item types.
        /// Example: [string, Tuple&lt;int, bool&gt;] → System.Tuple&lt;string, System.Tuple&lt;int, bool&gt;&gt;
        /// </summary>
        private string BuildTupleTypeName(List<string> itemTypes)
        {
            var typeArgs = string.Join(", ", itemTypes.Select(t =>
                TupleHandler.IsTupleType(t) ? t : t));

            return $"System.Tuple<{typeArgs}>";
        }

        private void GenerateSizeTupleValueMethod(string structName, string methodName, TupleTypeInfo tupleInfo)
        {
            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public static void Calculate{structName}Size(ref global::GProtobuf.Core.WriteSizeCalculator calculator, {structName} tupleValue)");
            _sb.StartNewBlock();

            // Convert flat struct back to nested Tuple
            int currentItemIndex = 1;
            var tupleConstruction = GenerateNestedTupleConstruction(tupleInfo.ItemTypes, ref currentItemIndex);
            _sb.AppendIndentedLine($"var tuple = {tupleConstruction};");

            // Call the existing Calculate...ContentSize method
            _sb.AppendIndentedLine($"SizeCalculators.Calculate{methodName}ContentSize(ref calculator, tuple);");

            _sb.EndBlock();
        }

        /// <summary>
        /// Generates a single TupleValue struct for a specific tuple type.
        /// Flattens nested tuples into a flat structure with Item1, Item2, Item3...
        /// Example: Tuple&lt;string, Tuple&lt;int, bool&gt;&gt; becomes:
        /// struct { string Item1; int Item2; bool Item3; }
        /// </summary>
        private void GenerateTupleValueStruct(TupleTypeInfo tupleInfo)
        {
            var structName = GetTupleValueStructName(tupleInfo);

            _sb.AppendLine("");
            _sb.AppendIndentedLine($"public struct {structName}");
            _sb.AppendIndentedLine("{");
            _sb.IncreaseIndent();

            // Flatten tuple types and generate Item properties
            var flattenedTypes = FlattenTupleTypes(tupleInfo.ItemTypes);
            for (int i = 0; i < flattenedTypes.Count; i++)
            {
                var itemType = EnsureGlobalPrefix(flattenedTypes[i]);
                _sb.AppendIndentedLine($"public {itemType} Item{i + 1} {{ get; set; }}");

                if (i < flattenedTypes.Count - 1)
                {
                    _sb.AppendLine("");
                }
            }

            _sb.DecreaseIndent();
            _sb.AppendIndentedLine("}");
        }

        /// <summary>
        /// Flattens nested tuple types into a single list.
        /// Example: ["string", "Tuple&lt;int, bool&gt;"] becomes ["string", "int", "bool"]
        /// </summary>
        private List<string> FlattenTupleTypes(List<string> itemTypes)
        {
            var result = new List<string>();

            foreach (var itemType in itemTypes)
            {
                if (TupleHandler.IsTupleType(itemType))
                {
                    // Recursively flatten nested tuples
                    var nestedTypes = TupleHandler.ParseTupleTypes(itemType);
                    var flattened = FlattenTupleTypes(nestedTypes);
                    result.AddRange(flattened);
                }
                else
                {
                    result.Add(itemType);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the TupleValue struct name from tuple info.
        /// Uses the same naming as Virtual Tuple methods (SafeName).
        /// Example: Tuple&lt;int, Tuple&lt;string, bool&gt;&gt; → TupleValue_intAndTupleOfstringAndbool
        /// </summary>
        private string GetTupleValueStructName(TupleTypeInfo tupleInfo)
        {
            // Use SafeName but replace "TupleOf" with "TupleValue_"
            return "TupleValue_" + tupleInfo.SafeName.Replace("TupleOf", "");
        }

        /// <summary>
        /// Ensures type has global:: prefix if needed.
        /// </summary>
        private string EnsureGlobalPrefix(string typeName)
        {
            if (typeName.StartsWith("global::"))
                return typeName;

            // Primitive types don't need global::
            if (IsPrimitiveType(typeName))
                return typeName;

            // System types don't need global::
            if (typeName.StartsWith("System."))
                return typeName;

            return $"global::{typeName}";
        }

        /// <summary>
        /// Checks if type is primitive.
        /// </summary>
        private bool IsPrimitiveType(string typeName)
        {
            var primitives = new[] { "int", "long", "short", "byte", "sbyte",
                "uint", "ulong", "ushort", "float", "double", "bool", "char", "string" };
            return primitives.Contains(typeName);
        }
    }
}
