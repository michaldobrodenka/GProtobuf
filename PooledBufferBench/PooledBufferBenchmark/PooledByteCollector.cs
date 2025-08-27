using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PooledBufferBenchmark
{
    class PooledByteCollectionCollector<T> : IDisposable where T : unmanaged
    {
        private byte[] buffer;
        private int writtenBytes;
        private int count;

        public PooledByteCollectionCollector(int initialElements = 1024)
        {
            int bytes = Math.Max(initialElements * Unsafe.SizeOf<T>(), 16);
            buffer = ArrayPool<byte>.Shared.Rent(bytes);
            writtenBytes = 0;
            count = 0;
        }

        public int Count => count;
        
        private static readonly int SizeOfT = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        //public void Add(T value)
        //{
        //    int size = Unsafe.SizeOf<T>();
        //    EnsureCapacity(size);
        //    // zapíš hodnotu T do bajtového buffera
        //    var v = value;
        //    MemoryMarshal.Write(_buffer.AsSpan(_writtenBytes), ref v);
        //    _writtenBytes += size;
        //    _count++;
        //}

        public unsafe void AddRange(scoped Span<T> values)
        {
            int n = values.Length;
            if (n == 0) return;

            int bytesToCopy = n * SizeOfT;
            EnsureCapacity(bytesToCopy);

            fixed (T* pSrc = values)
            fixed (byte* pDst = &buffer[writtenBytes])
            {
                Buffer.MemoryCopy(
                    source: pSrc,
                    destination: pDst,
                    destinationSizeInBytes: (long)(buffer.Length - writtenBytes),
                    sourceBytesToCopy: (long)bytesToCopy);
            }

            writtenBytes += bytesToCopy;
            count += n;
        }

        public unsafe void Add(T value)
        {
            EnsureCapacity(SizeOfT);

            fixed (byte* p = &buffer[writtenBytes])
            {
                *(T*)p = value;
            }

            writtenBytes += SizeOfT;
            count++;
        }


        public ReadOnlySpan<T> AsSpan()
        {
            Debug.Assert(writtenBytes == count * Unsafe.SizeOf<T>());
            return MemoryMarshal.Cast<byte, T>(buffer.AsSpan(0, writtenBytes));
        }

        public T[] ToArray()
        {
            int n = count;
            var arr = GC.AllocateUninitializedArray<T>(n);
            var src = AsSpan();
            src.CopyTo(arr.AsSpan());
            return arr;
        }

        //public unsafe T[] ToArrayUnsafe()
        //{
        //    int n = count;
        //    var arr = GC.AllocateUninitializedArray<T>(n);
        //    if (n == 0) return arr;

        //    int byteCount = writtenBytes;                     // == n * sizeof(T)
        //    fixed (byte* pSrc = &buffer[0])
        //    fixed (T* pDst = arr)
        //    {
        //        Buffer.MemoryCopy(
        //            source: pSrc,
        //            destination: pDst,
        //            destinationSizeInBytes: (long)n * SizeOfT,
        //            sourceBytesToCopy: byteCount);
        //    }
        //    return arr;
        //}

        //public List<T> ToList()
        //{
        //    int count = this.count;
        //    var slice = buffer.AsSpan(0, writtenBytes);

        //    List<T> fixedSizeResult = new(count);
        //    fixedSizeResult.AddRange(MemoryMarshal.Cast<byte, T>(slice));
        //    //CollectionsMarshal.SetCount(fixedSizeResult, count);
        //    //MemoryMarshal.Cast<byte, T>(slice).CopyTo(CollectionsMarshal.AsSpan(fixedSizeResult));
        //    return fixedSizeResult;
        //}

        // seems faster this way
        public List<T> ToList()
        {
            int count = this.count;
            var slice = buffer.AsSpan(0, writtenBytes);

            List<T> fixedSizeResult = new(count);
            CollectionsMarshal.SetCount(fixedSizeResult, count);
            MemoryMarshal.Cast<byte, T>(slice).CopyTo(CollectionsMarshal.AsSpan(fixedSizeResult));
            return fixedSizeResult;
        }


        private void EnsureCapacity(int sizeHint)
        {
            int needed = writtenBytes + sizeHint;
            if (needed <= buffer.Length) return;

            int newCap = Math.Max(buffer.Length * 2, needed);
            var newArr = ArrayPool<byte>.Shared.Rent(newCap);
            buffer.AsSpan(0, writtenBytes).CopyTo(newArr);
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            buffer = newArr;
        }

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            buffer = Array.Empty<byte>();
            writtenBytes = 0;
            count = 0;
        }
    }
}
