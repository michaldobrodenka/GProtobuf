using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public ref struct ClassCollectionCollector<T>
    {
        private T[] buffer;
        private int count;

        public ClassCollectionCollector()
        {
            buffer = ArrayPool<T>.Shared.Rent(16);
            count = 0;
        }

        public ClassCollectionCollector(int initialCapacity = 8)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            buffer = ArrayPool<T>.Shared.Rent(initialCapacity);
            count = 0;
        }

        public int Count => count;

        public void Add(T item)
        {
            if (count == buffer.Length) Grow(buffer.Length * 2);
            buffer[count++] = item;
        }

        public void AddRange(ReadOnlySpan<T> items)
        {
            EnsureCapacity(count + items.Length);
            items.CopyTo(buffer.AsSpan(count));
            count += items.Length;
        }

        public void EnsureCapacity(int needed)
        {
            if (needed <= buffer.Length) return;
            int cap = buffer.Length;
            while (cap < needed) cap *= 2;
            Grow(cap);
        }

        private void Grow(int newCapacity)
        {
            var newBuf = ArrayPool<T>.Shared.Rent(newCapacity);
            Array.Copy(buffer, 0, newBuf, 0, count);
            ArrayPool<T>.Shared.Return(buffer, clearArray: true);
            buffer = newBuf;
        }

        public T[] ToArray()
        {
            var result = new T[count];
            Array.Copy(buffer, 0, result, 0, count);
            return result;
        }

        public List<T> ToList()
        {
            var result = new List<T>(count);
            result.AddRange(buffer.AsSpan(0, count));
            return result;
        }

        public void Dispose()
        {
            ArrayPool<T>.Shared.Return(buffer, clearArray: true);
            buffer = Array.Empty<T>();
            count = 0;
        }
    }
}