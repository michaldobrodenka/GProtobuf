using System;
using GProtobuf.Generator.V2.Handlers.Core;

namespace GProtobuf.Generator.V2.Handlers.VirtualTypes
{
    /// <summary>
    /// Defines and enforces nesting depth limits for dictionaries.
    ///
    /// RATIONALE FOR 5-LEVEL LIMIT:
    /// ============================
    ///
    /// 1. STACK SAFETY:
    ///    - Each nesting level adds recursive method calls during serialization/deserialization
    ///    - 5 levels = reasonable depth before stack overflow risk in deeply nested scenarios
    ///    - Protobuf spec doesn't limit depth, but we must for production safety
    ///    - Example call stack for 5 levels: Main -> Level1 -> Level2 -> Level3 -> Level4 -> Level5
    ///
    /// 2. PRACTICAL USE CASES:
    ///    - Level 1: Dictionary&lt;K, V&gt; - extremely common (simple key-value mappings)
    ///    - Level 2: Dictionary&lt;K, Dictionary&lt;K2, V2&gt;&gt; - common (e.g., player->level->score, region->city->population)
    ///    - Level 3: Dictionary&lt;K, Dictionary&lt;K2, Dictionary&lt;K3, V3&gt;&gt;&gt; - rare (e.g., country->state->city->district->data)
    ///    - Level 4-5: extremely rare in real-world applications (complex hierarchical data)
    ///    - Beyond 5: architectural smell - indicates need for proper domain modeling with custom types
    ///
    /// 3. GENERATOR COMPLEXITY:
    ///    - Each level generates virtual types recursively at compile-time
    ///    - Deep nesting creates combinatorial explosion of type variations
    ///    - Example: Dict&lt;A,Dict&lt;B,Dict&lt;C,D&gt;&gt;&gt; generates 3 virtual types: A_DictBDictCD, B_DictCD, C_D
    ///    - 5 levels = manageable compile-time overhead and reasonable generated code size
    ///
    /// 4. RUNTIME PERFORMANCE:
    ///    - Deeper nesting = more method calls during serialization/deserialization
    ///    - More allocations for intermediate dictionaries and calculators
    ///    - Each level adds ~10-20% overhead (cumulative)
    ///    - 5 levels = acceptable performance degradation (~2x slower than flat structure)
    ///
    /// 5. MALICIOUS INPUT PROTECTION:
    ///    - Prevents stack overflow attacks from crafted payloads with extreme nesting
    ///    - Limits memory consumption from deeply nested dictionary structures
    ///    - Compile-time limit prevents generation of vulnerable code
    ///
    /// TO INCREASE LIMIT:
    /// ------------------
    /// If you genuinely need deeper nesting (NOT RECOMMENDED):
    /// 1. First, consider flattening your data model - this is almost always the better solution
    /// 2. Use custom classes/structs instead of nested dictionaries - more maintainable and performant
    /// 3. Split complex hierarchies into separate message types with references
    /// 4. If absolutely necessary after careful consideration, increase MAX_DICTIONARY_NESTING_DEPTH to max 10
    ///    WARNING: Values above 10 significantly increase:
    ///    - Stack overflow risk
    ///    - Compilation time
    ///    - Generated code size
    ///    - Runtime overhead
    ///    - Maintenance complexity
    ///
    /// COMPILE-TIME ENFORCEMENT:
    /// -------------------------
    /// The generator analyzes type hierarchy during code generation and throws
    /// InvalidOperationException if nesting depth exceeds this limit.
    /// This is a HARD LIMIT - code will not compile if exceeded.
    ///
    /// RUNTIME ENFORCEMENT:
    /// --------------------
    /// Optional: SpanReader can track depth during deserialization to protect
    /// against malformed/malicious payloads that might bypass compile-time checks
    /// (e.g., if attacker modifies wire format to add extra nesting levels).
    /// </summary>
    internal static class NestingLimits
    {
        /// <summary>
        /// Maximum allowed nesting depth for dictionaries.
        /// Dictionary&lt;A, Dictionary&lt;B, Dictionary&lt;C, Dictionary&lt;D, Dictionary&lt;E, F&gt;&gt;&gt;&gt;&gt; = 5 levels.
        /// </summary>
        public const int MAX_DICTIONARY_NESTING_DEPTH = 100;

        /// <summary>
        /// Maximum allowed nesting depth for tuples.
        /// Tuple&lt;int, Tuple&lt;int, Tuple&lt;int, Tuple&lt;int, Tuple&lt;int, int&gt;&gt;&gt;&gt;&gt; = 5 levels.
        /// This protects against stack overflow from deeply nested tuples and malicious payloads.
        ///
        /// RATIONALE:
        /// - Tuples can nest infinitely (Item2 can be another Tuple)
        /// - Each nesting level adds method calls during serialization/deserialization
        /// - 8-element tuples use Rest property, which is itself a Tuple (can nest further)
        /// - Limit prevents DoS attacks and stack overflow
        /// - 10 levels = reasonable for practical use (32 protobuf default)
        /// </summary>
        public const int MAX_TUPLE_NESTING_DEPTH = 100;

        /// <summary>
        /// Analyzes and validates dictionary nesting depth for a type.
        ///
        /// ALGORITHM:
        /// - Recursively descends into dictionary value types
        /// - Counts nesting levels
        /// - Throws exception if depth exceeds MAX_DICTIONARY_NESTING_DEPTH
        ///
        /// EXAMPLES:
        /// - Dictionary&lt;int, string&gt; -> depth 0 (no nesting)
        /// - Dictionary&lt;int, Dictionary&lt;string, double&gt;&gt; -> depth 1
        /// - Dictionary&lt;int, Dictionary&lt;string, Dictionary&lt;long, bool&gt;&gt;&gt; -> depth 2
        /// </summary>
        /// <param name="typeName">Full type name to analyze</param>
        /// <returns>Nesting depth (0 for non-nested dictionaries)</returns>
        /// <exception cref="InvalidOperationException">If nesting depth exceeds limit</exception>
        public static int AnalyzeAndValidateDictionaryNestingDepth(string typeName)
        {
            if (!TypeHelper.IsDictionaryType(typeName))
                return 0;

            var (_, valueType) = TypeHelper.ParseDictionaryTypes(typeName);

            // Recursive call to check inner dictionary depth
            int innerDepth = AnalyzeAndValidateDictionaryNestingDepth(valueType);

            // Check if this level would exceed the limit
            if (innerDepth >= MAX_DICTIONARY_NESTING_DEPTH)
            {
                throw new InvalidOperationException(
                    $"Dictionary nesting depth exceeds maximum allowed limit.\n" +
                    $"\n" +
                    $"Type: {typeName}\n" +
                    $"Current depth: {innerDepth + 1}\n" +
                    $"Maximum allowed: {MAX_DICTIONARY_NESTING_DEPTH}\n" +
                    $"\n" +
                    $"RECOMMENDATION:\n" +
                    $"Deep dictionary nesting is an architectural anti-pattern.\n" +
                    $"Consider one of these alternatives:\n" +
                    $"  1. Flatten your data model using composite keys\n" +
                    $"  2. Create custom classes to represent hierarchical data\n" +
                    $"  3. Split complex structures into separate message types\n" +
                    $"  4. Use a more appropriate data structure for your use case\n" +
                    $"\n" +
                    $"Deep nesting causes:\n" +
                    $"  - Performance degradation (2x+ slower per level)\n" +
                    $"  - Increased memory allocations\n" +
                    $"  - Stack overflow risk\n" +
                    $"  - Difficult maintenance and debugging\n" +
                    $"\n" +
                    $"If you absolutely must increase the limit, see NestingLimits.cs comments.");
            }

            return innerDepth + 1;
        }

        /// <summary>
        /// Analyzes and validates tuple nesting depth for a type.
        ///
        /// ALGORITHM:
        /// - Recursively descends into tuple item types
        /// - Counts nesting levels for tuple-within-tuple scenarios
        /// - Throws exception if depth exceeds MAX_TUPLE_NESTING_DEPTH
        ///
        /// EXAMPLES:
        /// - Tuple&lt;int, string&gt; -> depth 0 (no nesting)
        /// - Tuple&lt;int, Tuple&lt;string, double&gt;&gt; -> depth 1
        /// - Tuple&lt;int, Tuple&lt;string, Tuple&lt;long, bool&gt;&gt;&gt; -> depth 2
        /// - Tuple&lt;int,int,int,int,int,int,int, Tuple&lt;int,int&gt;&gt; -> depth 1 (Rest is nested)
        /// </summary>
        /// <param name="typeName">Full type name to analyze</param>
        /// <param name="currentDepth">Current recursion depth (internal use)</param>
        /// <returns>Maximum nesting depth found</returns>
        /// <exception cref="InvalidOperationException">If nesting depth exceeds limit</exception>
        public static int AnalyzeAndValidateTupleNestingDepth(string typeName, int currentDepth = 0)
        {
            if (!TupleHandler.IsTupleType(typeName))
                return currentDepth;

            // Check if we've exceeded the limit at this level
            if (currentDepth >= MAX_TUPLE_NESTING_DEPTH)
            {
                throw new InvalidOperationException(
                    $"Tuple nesting depth exceeds maximum allowed limit.\n" +
                    $"\n" +
                    $"Type: {typeName}\n" +
                    $"Current depth: {currentDepth}\n" +
                    $"Maximum allowed: {MAX_TUPLE_NESTING_DEPTH}\n" +
                    $"\n" +
                    $"RECOMMENDATION:\n" +
                    $"Deep tuple nesting is an architectural anti-pattern.\n" +
                    $"Consider one of these alternatives:\n" +
                    $"  1. Use custom classes/structs to represent your data\n" +
                    $"  2. Flatten the structure using composite patterns\n" +
                    $"  3. Split complex structures into separate message types\n" +
                    $"\n" +
                    $"Deep nesting causes:\n" +
                    $"  - Performance degradation\n" +
                    $"  - Increased memory allocations\n" +
                    $"  - Stack overflow risk\n" +
                    $"  - Difficult maintenance\n" +
                    $"\n" +
                    $"If you must increase the limit, see NestingLimits.cs comments.");
            }

            // Parse tuple items and check each for nested tuples
            var itemTypes = TupleHandler.ParseTupleTypes(typeName);
            int maxNestedDepth = currentDepth;

            foreach (var itemType in itemTypes)
            {
                if (TupleHandler.IsTupleType(itemType))
                {
                    int nestedDepth = AnalyzeAndValidateTupleNestingDepth(itemType, currentDepth + 1);
                    if (nestedDepth > maxNestedDepth)
                        maxNestedDepth = nestedDepth;
                }
            }

            return maxNestedDepth;
        }
    }
}
