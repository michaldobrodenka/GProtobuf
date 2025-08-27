using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PooledBufferBenchmark
{
    public class PooledBufferUnmanaged<T> : IDisposable where T : unmanaged
    {
        private T[] _array;
        private int _count;

        public PooledBufferUnmanaged(int initialCapacity = 64)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            _array = ArrayPool<T>.Shared.Rent(initialCapacity);
        }

        public int Count => _count;
        public ReadOnlySpan<T> WrittenSpan => _array.AsSpan(0, _count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T value)
        {
            if ((uint)_count >= (uint)_array.Length)
                Grow(Math.Max(_array.Length * 2, 4));
            _array[_count++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ToArray()
        {
            int n = _count;
            if (n == 0) return Array.Empty<T>();
            var dst = GC.AllocateUninitializedArray<T>(n);
            _array.AsSpan(0, n).CopyTo(dst);
            return dst;
        }

        private void Grow(int newCapacity)
        {
            var newArr = ArrayPool<T>.Shared.Rent(newCapacity);
            Array.Copy(_array, 0, newArr, 0, _count);
            ArrayPool<T>.Shared.Return(_array, clearArray: false); // unmanaged -> netreba čistiť
            _array = newArr;
        }

        public void Dispose()
        {
            ArrayPool<T>.Shared.Return(_array, clearArray: false); // unmanaged -> netreba čistiť
            _array = Array.Empty<T>();
            _count = 0;
        }
    }
}
