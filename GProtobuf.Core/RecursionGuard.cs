using System;

namespace GProtobuf.Core
{
    /// <summary>
    /// Recursion depth guard for preventing stack overflow during protobuf deserialization.
    /// </summary>
    /// <remarks>
    /// Thread-safe implementation using [ThreadStatic] counter.
    /// Usage pattern (generated code):
    /// <code>
    /// public static MyClass ReadMyClass(ref SpanReader reader)
    /// {
    ///     using (RecursionGuard.EnterLevel())
    ///     {
    ///         // deserialization logic
    ///     }
    /// }
    /// </code>
    ///
    /// Design rationale:
    /// - ThreadStatic: No synchronization overhead, thread-safe automatically
    /// - Struct DepthScope: Zero heap allocations (stack-allocated)
    /// - IDisposable pattern: Automatic depth decrement on scope exit
    /// - Max depth 100: Matches protobuf-net Level200 default
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
        /// Enters a new recursion level. Returns IDisposable that automatically exits on Dispose.
        /// </summary>
        /// <returns>Disposable scope that decrements depth on exit.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when recursion depth exceeds MaxDepth.
        /// Protects against stack overflow from maliciously crafted protobuf messages.
        /// </exception>
        public static IDisposable EnterLevel()
        {
            if (++_currentDepth > MaxDepth)
            {
                // Reset depth before throwing to prevent permanent corruption
                _currentDepth = MaxDepth;
                throw new InvalidOperationException(
                    $"Protobuf deserialization exceeded maximum recursion depth ({MaxDepth}). " +
                    "This may indicate a circular reference or maliciously crafted message.");
            }

            return new DepthScope();
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

        /// <summary>
        /// RAII-style depth scope. Automatically decrements depth on disposal.
        /// Struct to avoid heap allocation (stack-allocated only).
        /// </summary>
        private struct DepthScope : IDisposable
        {
            /// <summary>
            /// Decrements recursion depth when leaving scope.
            /// Called automatically by 'using' statement.
            /// </summary>
            public void Dispose()
            {
                _currentDepth--;
            }
        }
    }
}
