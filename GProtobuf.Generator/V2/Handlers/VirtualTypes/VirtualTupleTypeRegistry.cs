using System.Collections.Generic;
using System.Linq;
using GProtobuf.Generator.V2.Helpers;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
{
    /// <summary>
    /// Registry for virtual Tuple types.
    /// Tracks all Tuple types used in the schema and generates unique safe names.
    /// Example: Tuple&lt;int, string&gt; → TupleOfIntAndString
    /// </summary>
    internal class VirtualTupleTypeRegistry
    {
        private readonly Dictionary<string, TupleTypeInfo> _registeredTypes = new Dictionary<string, TupleTypeInfo>();

        /// <summary>
        /// Registers a Tuple type for code generation.
        /// Also recursively registers any nested Tuple types found in item types.
        /// </summary>
        public TupleTypeInfo Register(string originalTypeName, List<string> itemTypes)
        {
            // Use original type name as key for deduplication
            if (_registeredTypes.TryGetValue(originalTypeName, out var existing))
            {
                return existing;
            }

            var safeName = GenerateSafeName(itemTypes);
            var info = new TupleTypeInfo
            {
                OriginalTypeName = originalTypeName,
                SafeName = safeName,
                ItemTypes = itemTypes
            };

            _registeredTypes[originalTypeName] = info;

            // Recursively register any Tuple types found in item types
            foreach (var itemType in itemTypes)
            {
                RegisterNestedTuples(itemType);
            }

            return info;
        }

        /// <summary>
        /// Recursively finds and registers Tuple types within a given type.
        /// Handles nested Tuples, collections of Tuples, and Dictionary/Map with Tuple keys/values.
        /// </summary>
        public void RegisterNestedTuples(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return;

            // Check if this is a Tuple type
            if (TupleHandler.IsTupleType(typeName))
            {
                var itemTypes = TupleHandler.ParseTupleTypes(typeName);
                Register(typeName, itemTypes);
                return;
            }

            // Check if this is a collection type (List<T>, IEnumerable<T>, etc.)
            if (IsCollectionType(typeName, out var elementType))
            {
                RegisterNestedTuples(elementType);
                return;
            }

            // Check if this is a Dictionary/Map type
            if (IsDictionaryType(typeName, out var keyType, out var valueType))
            {
                RegisterNestedTuples(keyType);
                RegisterNestedTuples(valueType);
                return;
            }
        }

        private bool IsCollectionType(string typeName, out string elementType)
        {
            elementType = null;

            // Check for List<T>, IList<T>, IEnumerable<T>, ICollection<T>, etc.
            var collectionPrefixes = new[]
            {
                "System.Collections.Generic.List<",
                "System.Collections.Generic.IList<",
                "System.Collections.Generic.IEnumerable<",
                "System.Collections.Generic.ICollection<",
                "List<",
                "IList<",
                "IEnumerable<",
                "ICollection<"
            };

            foreach (var prefix in collectionPrefixes)
            {
                if (typeName.StartsWith(prefix) && typeName.EndsWith(">"))
                {
                    var start = prefix.Length;
                    var length = typeName.Length - start - 1;
                    elementType = typeName.Substring(start, length);
                    return true;
                }
            }

            return false;
        }

        private bool IsDictionaryType(string typeName, out string keyType, out string valueType)
        {
            keyType = null;
            valueType = null;

            var dictionaryPrefixes = new[]
            {
                "System.Collections.Generic.Dictionary<",
                "System.Collections.Generic.IDictionary<",
                "Dictionary<",
                "IDictionary<",
                "Map<" // Support for custom Map types if any
            };

            foreach (var prefix in dictionaryPrefixes)
            {
                if (typeName.StartsWith(prefix) && typeName.EndsWith(">"))
                {
                    var genericArgs = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);
                    var types = SplitGenericArguments(genericArgs);
                    if (types.Count == 2)
                    {
                        keyType = types[0];
                        valueType = types[1];
                        return true;
                    }
                }
            }

            return false;
        }

        private List<string> SplitGenericArguments(string genericArgs)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < genericArgs.Length; i++)
            {
                char c = genericArgs[i];
                if (c == '<' || c == '(') depth++;
                else if (c == '>' || c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(genericArgs.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            if (start < genericArgs.Length)
            {
                result.Add(genericArgs.Substring(start).Trim());
            }

            return result;
        }

        /// <summary>
        /// Gets a registered Tuple type by original type name.
        /// </summary>
        public TupleTypeInfo GetByOriginalName(string originalTypeName)
        {
            _registeredTypes.TryGetValue(originalTypeName, out var info);
            return info;
        }

        /// <summary>
        /// Gets all registered Tuple types.
        /// </summary>
        public List<TupleTypeInfo> GetAllTypes()
        {
            return _registeredTypes.Values.ToList();
        }

        /// <summary>
        /// Generates a safe method name from Tuple item types.
        /// Examples:
        ///   [int, string] → TupleOfIntAndString
        ///   [MyModel, int, bool] → TupleOfMyModelAndIntAndBool
        ///   [Dictionary&lt;int,string&gt;, CustomClass] → TupleOfDictionaryOfIntAndStringAndCustomClass
        /// </summary>
        private string GenerateSafeName(List<string> itemTypes)
        {
            var parts = itemTypes.Select(t => TypeNameHelper.GetSafeMethodName(t));
            var name = "TupleOf" + string.Join("And", parts);

            // If name is too long (>200 chars), use hash to avoid issues
            if (name.Length > 200)
            {
                var hash = GetStableHash(string.Join(",", itemTypes));
                return $"Tuple_{hash}";
            }

            return name;
        }

        /// <summary>
        /// Generates a stable hash for long type names.
        /// </summary>
        private string GetStableHash(string input)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in input)
                {
                    hash = hash * 31 + c;
                }
                return ((uint)hash).ToString("X8");
            }
        }
    }

    /// <summary>
    /// Information about a registered Tuple type.
    /// </summary>
    internal class TupleTypeInfo
    {
        /// <summary>
        /// Original type name: System.Tuple&lt;int, string&gt;
        /// </summary>
        public string OriginalTypeName { get; set; }

        /// <summary>
        /// Safe name for method generation: TupleOfIntAndString
        /// </summary>
        public string SafeName { get; set; }

        /// <summary>
        /// List of item types: ["int", "string"]
        /// </summary>
        public List<string> ItemTypes { get; set; }

        /// <summary>
        /// Number of items in the tuple (2-8 for standard Tuple)
        /// </summary>
        public int Arity => ItemTypes?.Count ?? 0;
    }
}
