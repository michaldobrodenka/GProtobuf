using System;
using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.Handlers;
using GProtobuf.Generator.V2.Handlers.Core;
using GProtobuf.Generator.V2.Handlers.VirtualTypes;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.CodeGeneration.Core
{
    /// <summary>
    /// Base class for code generators that share common fields, constructors, and helper methods.
    /// Reduces code duplication between SizeCalculatorGenerator, SpanReaderGenerator, and StreamWriterGenerator.
    /// </summary>
    internal abstract class GeneratorBase
    {
        protected readonly StringBuilderWithIndent _sb;
        protected readonly TypeRegistry _registry;
        protected readonly PrimitiveHandler _primitiveHandler;
        protected readonly CollectionHandler _collectionHandler;
        protected readonly TupleHandler _tupleHandler;
        protected readonly VirtualMapTypeRegistry _virtualMapRegistry;
        protected readonly VirtualTupleTypeRegistry _virtualTupleRegistry;
        protected string _currentNamespace;
        protected int _nestedCalcCounter;

        /// <summary>
        /// Gets the virtual map type registry used by this generator.
        /// </summary>
        public VirtualMapTypeRegistry VirtualMapRegistry => _virtualMapRegistry;

        /// <summary>
        /// Gets the virtual tuple type registry used by this generator.
        /// </summary>
        public VirtualTupleTypeRegistry VirtualTupleRegistry => _virtualTupleRegistry;

        protected GeneratorBase(StringBuilderWithIndent sb, TypeRegistry registry)
            : this(sb, registry, null, null, false)
        {
        }

        protected GeneratorBase(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry)
            : this(sb, registry, virtualMapRegistry, null, false)
        {
        }

        protected GeneratorBase(
            StringBuilderWithIndent sb,
            TypeRegistry registry,
            VirtualMapTypeRegistry virtualMapRegistry,
            VirtualTupleTypeRegistry virtualTupleRegistry,
            bool passRegistryToPrimitiveHandler = false)
        {
            _sb = sb;
            _registry = registry;
            _primitiveHandler = passRegistryToPrimitiveHandler ? new PrimitiveHandler(registry) : new PrimitiveHandler();
            _collectionHandler = new CollectionHandler(sb, registry);
            _virtualTupleRegistry = virtualTupleRegistry ?? new VirtualTupleTypeRegistry();
            _virtualMapRegistry = virtualMapRegistry ?? new VirtualMapTypeRegistry(_virtualTupleRegistry, _registry);
            _tupleHandler = new TupleHandler(sb, _virtualTupleRegistry);
        }

        /// <summary>
        /// Collects ProtoInclude types that are not in the main types list.
        /// This is a common operation used in GenerateAll methods.
        /// </summary>
        /// <param name="processedTypes">Set of already processed type names</param>
        /// <returns>Set of ProtoInclude type names that need processing</returns>
        protected HashSet<string> CollectUnprocessedProtoIncludeTypes(HashSet<string> processedTypes)
        {
            var protoIncludeTypes = new HashSet<string>();

            foreach (var registeredType in _registry.GetByNamespace(_currentNamespace))
            {
                if (registeredType.ProtoIncludes != null)
                {
                    foreach (var include in registeredType.ProtoIncludes)
                    {
                        if (!processedTypes.Contains(include.Type))
                        {
                            var includeTypeNamespace = TypeNameHelper.GetNamespace(include.Type);
                            if (includeTypeNamespace == _currentNamespace)
                            {
                                protoIncludeTypes.Add(include.Type);
                            }
                        }
                    }
                }
            }

            return protoIncludeTypes;
        }

        /// <summary>
        /// Generates a unique nested calculator variable name.
        /// </summary>
        protected string GetNextNestedCalcVar()
        {
            return $"nestedCalc{_nestedCalcCounter++}";
        }

        /// <summary>
        /// Resets the nested calculator counter. Call at the start of methods that need unique variable names.
        /// </summary>
        protected void ResetNestedCalcCounter()
        {
            _nestedCalcCounter = 0;
        }

        /// <summary>
        /// Iterates over all ProtoMembers and CustomBufferMembers of a type, calling the appropriate handler for each.
        /// This is a common pattern used in GenerateSimple*, GenerateBaseFieldsOnly*, etc.
        /// </summary>
        /// <param name="type">The type definition</param>
        /// <param name="sourceVar">The source variable name (e.g., "obj", "instance")</param>
        /// <param name="handleProtoMember">Action to handle each ProtoMember</param>
        /// <param name="handleCustomBufferMember">Action to handle each CustomBufferMember (optional)</param>
        protected void ForEachTypeMember(
            TypeDefinition type,
            string sourceVar,
            Action<ProtoMemberAttribute, string> handleProtoMember,
            Action<CustomBufferMember, string> handleCustomBufferMember = null)
        {
            if (type.ProtoMembers != null)
            {
                foreach (var member in type.ProtoMembers)
                {
                    handleProtoMember(member, sourceVar);
                }
            }

            if (handleCustomBufferMember != null && type.CustomBufferMembers != null)
            {
                foreach (var customMember in type.CustomBufferMembers)
                {
                    handleCustomBufferMember(customMember, sourceVar);
                }
            }
        }

        /// <summary>
        /// Iterates over ProtoMembers only (with optional null/count check).
        /// </summary>
        protected void ForEachProtoMember(
            IReadOnlyList<ProtoMemberAttribute> members,
            string sourceVar,
            Action<ProtoMemberAttribute, string> handleMember)
        {
            if (members != null && members.Count > 0)
            {
                foreach (var member in members)
                {
                    handleMember(member, sourceVar);
                }
            }
        }

        /// <summary>
        /// Iterates over CustomBufferMembers only (with optional null/count check).
        /// </summary>
        protected void ForEachCustomBufferMember(
            IReadOnlyList<CustomBufferMember> members,
            string sourceVar,
            Action<CustomBufferMember, string> handleMember)
        {
            if (members != null && members.Count > 0)
            {
                foreach (var member in members)
                {
                    handleMember(member, sourceVar);
                }
            }
        }

        #region Dictionary-Based Type Dispatch Optimization

        /// <summary>
        /// Threshold for using dictionary-based dispatch vs type switch.
        /// For small numbers of derived types, the type switch is faster due to JIT optimization.
        /// </summary>
        protected const int DictionaryDispatchThreshold = 8;

        /// <summary>
        /// Set of base types that need dictionary dispatch generation.
        /// Populated during type dispatch generation, used to generate dictionaries at class level.
        /// </summary>
        protected HashSet<string> _typesNeedingDictionaryDispatch = new HashSet<string>();

        /// <summary>
        /// Generates dictionary field and GetTypeIndex method for a polymorphic base type.
        /// Returns true if dictionary dispatch should be used, false for regular type switch.
        /// </summary>
        /// <param name="className">The class name of the base type</param>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="derivedTypes">List of derived types (sorted by depth, most derived first)</param>
        /// <returns>True if dictionary dispatch was generated and should be used</returns>
        protected bool TryGenerateDictionaryDispatch(string className, string fullTypeName, List<string> derivedTypes)
        {
            if (derivedTypes == null || derivedTypes.Count < DictionaryDispatchThreshold)
                return false;

            // Generate dictionary field
            _sb.AppendIndentedLine($"#region {className} Type Dispatch Dictionary");
            _sb.AppendIndentedLine($"private static System.Collections.Generic.Dictionary<nint, int> _{className}TypeIndex;");
            _sb.AppendNewLine();

            // Generate lazy initializer
            _sb.AppendIndentedLine($"private static System.Collections.Generic.Dictionary<nint, int> Get{className}TypeIndex()");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"if (_{className}TypeIndex != null) return _{className}TypeIndex;");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"var dict = new System.Collections.Generic.Dictionary<nint, int>({derivedTypes.Count});");

            for (int i = 0; i < derivedTypes.Count; i++)
            {
                _sb.AppendIndentedLine($"dict[typeof(global::{derivedTypes[i]}).TypeHandle.Value] = {i};");
            }

            _sb.AppendIndentedLine($"_{className}TypeIndex = dict;");
            _sb.AppendIndentedLine("return dict;");
            _sb.EndBlock();
            _sb.AppendNewLine();
            _sb.AppendIndentedLine("#endregion");
            _sb.AppendNewLine();

            _typesNeedingDictionaryDispatch.Add(fullTypeName);
            return true;
        }

        /// <summary>
        /// Generates the dictionary lookup and integer switch for type dispatch.
        /// </summary>
        /// <param name="className">The class name of the base type</param>
        /// <param name="sourceVar">Source variable name (e.g., "obj", "instance")</param>
        /// <param name="derivedTypes">List of derived types (sorted by depth, most derived first)</param>
        /// <param name="generateCase">Action to generate code for each case (index, derivedType, derivedClassName)</param>
        protected void GenerateDictionaryBasedSwitch(
            string className,
            string sourceVar,
            List<string> derivedTypes,
            Action<int, string, string> generateCase)
        {
            _sb.AppendIndentedLine($"// O(1) dictionary-based type dispatch (optimized for {derivedTypes.Count} types)");
            _sb.AppendIndentedLine($"var typeDict = Get{className}TypeIndex();");
            _sb.AppendIndentedLine($"if (typeDict.TryGetValue(System.Type.GetTypeHandle({sourceVar}).Value, out var typeIndex))");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("switch (typeIndex)");
            _sb.StartNewBlock();

            for (int i = 0; i < derivedTypes.Count; i++)
            {
                var derivedType = derivedTypes[i];
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"case {i}:");
                _sb.IncreaseIndent();
                generateCase(i, derivedType, derivedClassName);
                _sb.DecreaseIndent();
            }

            _sb.EndBlock();
            _sb.EndBlock();
        }

        #endregion
    }
}
