using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GProtobuf.Core
{
    /// <summary>
    /// High-performance array builder for reference types using a unified object[] pool.
    /// All reference types share the same ArrayPool&lt;object&gt;.Shared, avoiding pool fragmentation.
    /// Unlike using ArrayPool&lt;T&gt;.Shared for each type, this consolidates all class types into one pool.
    /// </summary>
    /// <remarks>
    /// Benefits over List&lt;T&gt;.Add():
    /// - List starts at capacity 4 and grows: 4→8→16→32→64→128 (many allocations for 100 items)
    /// - ObjectArrayBuilder rents from pool (often pre-allocated), only final array is allocated
    ///
    /// Benefits over ArrayPool&lt;T&gt;.Shared:
    /// - Each T gets its own pool (MyClass pool, OtherClass pool, etc.) - fragmentation
    /// - ObjectArrayBuilder uses single object[] pool for ALL reference types
    /// </remarks>
    public ref struct ObjectArrayBuilder<T> where T : class
    {
        private object[] buffer;
        private int count;

        public ObjectArrayBuilder(int initialCapacity = 16)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            buffer = ArrayPool<object>.Shared.Rent(initialCapacity);
            count = 0;
        }

        public int Count => count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            var buf = buffer;
            if (buf == null)
                ThrowObjectDisposedException();
            if (count == buf.Length)
                Grow();
            buffer[count++] = item;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowObjectDisposedException()
        {
            throw new ObjectDisposedException(nameof(ObjectArrayBuilder<T>));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            GrowTo(buffer.Length * 2);
        }

        private void GrowTo(int newCapacity)
        {
            var newBuf = ArrayPool<object>.Shared.Rent(newCapacity);
            Array.Copy(buffer, 0, newBuf, 0, count);
            ArrayPool<object>.Shared.Return(buffer, clearArray: true); // Must clear to avoid memory leaks!
            buffer = newBuf;
        }

        /// <summary>
        /// Creates an array from the collected items.
        /// </summary>
        /// <returns>A new array containing all added items.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if called after Dispose.</exception>
        public T[] ToArray()
        {
            if (buffer == null)
                throw new ObjectDisposedException(nameof(ObjectArrayBuilder<T>));

            if (count == 0)
                return Array.Empty<T>();

            var result = new T[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = (T)buffer[i];
            }
            return result;
        }

        /// <summary>
        /// Creates a list from the collected items.
        /// </summary>
        /// <returns>A new list containing all added items.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if called after Dispose.</exception>
        public List<T> ToList()
        {
            if (buffer == null)
                throw new ObjectDisposedException(nameof(ObjectArrayBuilder<T>));

            if (count == 0)
                return new List<T>();

            var list = new List<T>(count);
            CollectionsMarshal.SetCount(list, count);
            var span = CollectionsMarshal.AsSpan(list);
            for (int i = 0; i < count; i++)
            {
                span[i] = (T)buffer[i];
            }
            return list;
        }

        /// <summary>
        /// Returns the pooled buffer. MUST be called to avoid memory leaks.
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            if (buffer != null)
            {
                ArrayPool<object>.Shared.Return(buffer, clearArray: true); // Must clear references!
                buffer = null;
            }
            count = 0;
        }
    }
}
