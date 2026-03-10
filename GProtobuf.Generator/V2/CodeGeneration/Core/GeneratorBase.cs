using System;
using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.CodeGeneration;
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
        protected readonly GeneratorOptions _options;
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

        /// <summary>
        /// Gets whether string pooling is enabled for this generator.
        /// </summary>
        protected bool UseStringPooling => _options?.UseStringPooling ?? false;

        protected GeneratorBase(StringBuilderWithIndent sb, TypeRegistry registry, GeneratorOptions options = null)
            : this(sb, registry, null, null, false, options)
        {
        }

        protected GeneratorBase(StringBuilderWithIndent sb, TypeRegistry registry, VirtualMapTypeRegistry virtualMapRegistry, GeneratorOptions options = null)
            : this(sb, registry, virtualMapRegistry, null, false, options)
        {
        }

        protected GeneratorBase(
            StringBuilderWithIndent sb,
            TypeRegistry registry,
            VirtualMapTypeRegistry virtualMapRegistry,
            VirtualTupleTypeRegistry virtualTupleRegistry,
            bool passRegistryToPrimitiveHandler = false,
            GeneratorOptions options = null)
        {
            _sb = sb;
            _registry = registry;
            _options = options ?? GeneratorOptions.Default;
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

        #region Function Pointer Type Dispatch Optimization

        /// <summary>
        /// Threshold for using function pointer dispatch vs type switch.
        /// For small numbers of derived types, the type switch is faster due to JIT optimization.
        /// </summary>
        protected const int DictionaryDispatchThreshold = 8;

        /// <summary>
        /// Set of base types that need function pointer dispatch generation.
        /// Populated during type dispatch generation, used to generate dispatch tables at class level.
        /// </summary>
        protected HashSet<string> _typesNeedingDictionaryDispatch = new HashSet<string>();

        /// <summary>
        /// Generates FrozenDictionary with function pointers and wrapper methods for a polymorphic base type.
        /// Returns true if function pointer dispatch should be used, false for regular type switch.
        /// </summary>
        /// <param name="className">The class name of the base type</param>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="refParamType">The ref parameter type (e.g., "global::GProtobuf.Core.StreamWriter")</param>
        /// <param name="refParamName">The ref parameter name (e.g., "writer")</param>
        /// <param name="derivedTypes">List of derived types (sorted by depth, most derived first)</param>
        /// <param name="generateTargetCall">Action to generate the target method call inside wrapper (derivedType, derivedClassName, castVar)</param>
        /// <returns>True if function pointer dispatch was generated and should be used</returns>
        protected bool TryGenerateFunctionPointerDispatch(
            string className,
            string fullTypeName,
            string refParamType,
            string refParamName,
            List<string> derivedTypes,
            Action<string, string, string> generateTargetCall)
        {
            if (derivedTypes == null || derivedTypes.Count < DictionaryDispatchThreshold)
                return false;

            _sb.AppendIndentedLine($"#region {className} Function Pointer Dispatch");
            _sb.AppendNewLine();

            // Generate wrapper methods for each derived type
            foreach (var derivedType in derivedTypes)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                _sb.AppendIndentedLine($"private static void {className}Dispatch_{derivedClassName}(ref {refParamType} {refParamName}, global::{fullTypeName} instance)");
                _sb.StartNewBlock();
                generateTargetCall(derivedType, derivedClassName, $"(global::{derivedType})instance");
                _sb.EndBlock();
                _sb.AppendNewLine();
            }

            // Generate FrozenDictionary field
            _sb.AppendIndentedLine($"private static System.Collections.Frozen.FrozenDictionary<nint, nint> _{className}Dispatch;");
            _sb.AppendNewLine();

            // Generate lazy initializer
            _sb.AppendIndentedLine($"private static System.Collections.Frozen.FrozenDictionary<nint, nint> Get{className}Dispatch()");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"if (_{className}Dispatch != null) return _{className}Dispatch;");
            _sb.AppendNewLine();
            _sb.AppendIndentedLine($"var dict = new System.Collections.Generic.Dictionary<nint, nint>({derivedTypes.Count});");
            _sb.AppendIndentedLine("unsafe");
            _sb.StartNewBlock();

            foreach (var derivedType in derivedTypes)
            {
                var derivedClassName = TypeNameHelper.GetClassName(derivedType);
                _sb.AppendIndentedLine($"dict[typeof(global::{derivedType}).TypeHandle.Value] = (nint)(delegate*<ref {refParamType}, global::{fullTypeName}, void>)&{className}Dispatch_{derivedClassName};");
            }

            _sb.EndBlock(); // unsafe
            _sb.AppendIndentedLine($"_{className}Dispatch = System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(dict);");
            _sb.AppendIndentedLine($"return _{className}Dispatch;");
            _sb.EndBlock();
            _sb.AppendNewLine();

            _sb.AppendIndentedLine("#endregion");
            _sb.AppendNewLine();

            _typesNeedingDictionaryDispatch.Add(fullTypeName);
            return true;
        }

        /// <summary>
        /// Generates the function pointer lookup and direct call for type dispatch.
        /// </summary>
        /// <param name="className">The class name of the base type</param>
        /// <param name="fullTypeName">The full type name of the base</param>
        /// <param name="refParamType">The ref parameter type</param>
        /// <param name="refParamName">The ref parameter name</param>
        /// <param name="sourceVar">Source variable name (e.g., "obj", "instance")</param>
        /// <param name="derivedTypeCount">Number of derived types (for comment)</param>
        /// <param name="returnAfterCall">If true, generates 'return;' after call. If false, execution continues.</param>
        protected void GenerateFunctionPointerCall(
            string className,
            string fullTypeName,
            string refParamType,
            string refParamName,
            string sourceVar,
            int derivedTypeCount,
            bool returnAfterCall = true)
        {
            _sb.AppendIndentedLine($"// O(1) function pointer dispatch (optimized for {derivedTypeCount} types)");
            _sb.AppendIndentedLine($"var dispatchTable = Get{className}Dispatch();");
            _sb.AppendIndentedLine($"if (dispatchTable.TryGetValue(System.Type.GetTypeHandle({sourceVar}).Value, out var fnPtr))");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine("unsafe");
            _sb.StartNewBlock();
            _sb.AppendIndentedLine($"((delegate*<ref {refParamType}, global::{fullTypeName}, void>)fnPtr)(ref {refParamName}, {sourceVar});");
            _sb.EndBlock(); // unsafe
            if (returnAfterCall)
            {
                _sb.AppendIndentedLine("return;");
            }
            _sb.EndBlock();
        }

        #endregion
    }
}
