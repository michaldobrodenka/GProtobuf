using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PooledBufferBenchmark
{
    public class PooledIntBuffer : IDisposable
    {
        private int[] _array;
        private int _count;

        public PooledIntBuffer(int initialCapacity = 64)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            _array = ArrayPool<int>.Shared.Rent(initialCapacity);
        }

        public int Count => _count;
        public ReadOnlySpan<int> WrittenSpan => _array.AsSpan(0, _count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int value)
        {
            if ((uint)_count >= (uint)_array.Length)
                Grow(Math.Max(_array.Length * 2, 4));
            _array[_count++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int[] ToArray()
        {
            int n = _count;
            if (n == 0) return Array.Empty<int>();
            var dst = GC.AllocateUninitializedArray<int>(n);
            _array.AsSpan(0, n).CopyTo(dst);
            return dst;
        }

        private void Grow(int newCapacity)
        {
            var newArr = ArrayPool<int>.Shared.Rent(newCapacity);
            Array.Copy(_array, 0, newArr, 0, _count);
            ArrayPool<int>.Shared.Return(_array, clearArray: false); // unmanaged -> netreba čistiť
            _array = newArr;
        }

        public void Dispose()
        {
            ArrayPool<int>.Shared.Return(_array, clearArray: false); // unmanaged -> netreba čistiť
            _array = Array.Empty<int>();
            _count = 0;
        }
    }
}

