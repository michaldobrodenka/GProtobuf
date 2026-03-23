using System;

namespace GProtobuf.Core
{
    /// <summary>
    /// Recursion depth guard for preventing stack overflow during protobuf deserialization.
    /// OPT-2: Zero-allocation implementation using Enter/Exit pattern.
    /// </summary>
    /// <remarks>
    /// Thread-safe implementation using [ThreadStatic] counter.
    /// Usage pattern (generated code):
    /// <code>
    /// public static MyClass ReadMyClass(ref SpanReader reader)
    /// {
    ///     RecursionGuard.Enter();
    ///     try
    ///     {
    ///         // deserialization logic
    ///     }
    ///     finally
    ///     {
    ///         RecursionGuard.Exit();
    ///     }
    /// }
    /// </code>
    ///
    /// Design rationale (OPT-2):
    /// - ThreadStatic: No synchronization overhead, thread-safe automatically
    /// - Enter/Exit methods: Zero heap allocations (no IDisposable boxing)
    /// - try/finally: Guaranteed cleanup even on exception
    /// - Max depth 100: Matches protobuf-net Level200 default
    /// - Aggressive inlining: JIT optimizes to 2-3 CPU instructions
    /// </remarks>
    public static class RecursionGuard
    {
        /// <summary>
        /// Maximum allowed recursion depth for nested message deserialization.
        /// Default: 100 (CompatibilityLevel.Level200).
        /// </summary>
        public const int MaxDepth = 100;

        /// <summary>
        /// Current recursion depth for this thread.
        /// ThreadStatic ensures thread-safety without locks.
        /// </summary>
        [ThreadStatic]
        private static int _currentDepth;

        /// <summary>
        /// Enters a new recursion level. Must be paired with Exit() in finally block.
        /// OPT-2: Zero allocations (no IDisposable boxing).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when recursion depth exceeds MaxDepth.
        /// Protects against stack overflow from maliciously crafted protobuf messages.
        /// </exception>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Enter()
        {
            if (++_currentDepth > MaxDepth)
            {
                // Reset depth before throwing to prevent permanent corruption
                _currentDepth = MaxDepth;
                throw new InvalidOperationException(
                    $"Protobuf deserialization exceeded maximum recursion depth ({MaxDepth}). " +
                    "This may indicate a circular reference or maliciously crafted message.");
            }
        }

        /// <summary>
        /// Exits current recursion level. Must be called in finally block after Enter().
        /// OPT-2: Guaranteed cleanup via try/finally pattern.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Exit()
        {
            _currentDepth--;
        }

        /// <summary>
        /// Gets current recursion depth for this thread.
        /// Primarily for testing and diagnostics.
        /// </summary>
        internal static int CurrentDepth => _currentDepth;

        /// <summary>
        /// Resets recursion depth to zero.
        /// Used for testing to ensure clean state between test runs.
        /// </summary>
        public static void Reset()
        {
            _currentDepth = 0;
        }
    }
}
