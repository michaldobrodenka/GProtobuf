using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GProtobuf.Core
{
    public ref struct StackThenPoolCollectionCollector<T> where T : unmanaged
    {
        private Span<T> stage;            // stackalloc staging buffer
        private int stageCount;
        private PooledByteCollectionCollector<T>? pool; // lazy created
        private readonly int pooledInit;

        public StackThenPoolCollectionCollector(scoped Span<T> initialBuffer, int pooledInitialElements = 64)
        {
            unsafe
            {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
                this.stage = initialBuffer;
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
            }
            stageCount = 0;
            pool = null;
            pooledInit = pooledInitialElements;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T value)
        {
            // stage buffer first
            if (stageCount < stage.Length)
            {
                stage[stageCount++] = value;
                return;
            }

            FlushStageToPool();
            stage[stageCount++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange(scoped Span<T> values)
        {
            int n = values.Length;
            if (n == 0) return;

            // 1) Fill the staging buffer up to capacity; if the whole range fits, we're done.
            int space = stage.Length - stageCount;
            if (n <= space)
            {
                values.CopyTo(stage.Slice(stageCount));
                stageCount += n;
                return;
            }

            // Fill as much as fits into the remaining staging space.
            if (space > 0)
            {
                values[..space].CopyTo(stage.Slice(stageCount));
                stageCount += space; // == stage.Length
                values = values[space..];
            }

            // 2) Staging is full → flush it to the pool with a single AddRange.
            FlushStageToPool();

            // 3) Send the large bulk directly to the pool in one or a few large AddRange calls,
            //    but keep a tail < stage.Length in staging (better for subsequent Add operations).
            int rem = values.Length;
            if (rem >= stage.Length)
            {
                int bulkLen = rem - (rem % stage.Length); // leave a tail for staging
                if (bulkLen > 0)
                {
                    EnsurePool();
                    pool!.AddRange(values[..bulkLen]);
                    values = values[bulkLen..];
                    rem = values.Length;
                }
            }

            // 4) Keep the remainder (tail < staging capacity) in the staging buffer.
            if (rem > 0)
            {
                values.CopyTo(stage);
                stageCount = rem;
            }
        }

        public int Count => (pool is null ? stageCount : pool.Count + stageCount);

        public T[] ToArray()
        {
            T[] arr;
            if (pool is null)
            {
                arr = GC.AllocateUninitializedArray<T>(stageCount);

                if (stageCount > 0)
                    stage[..stageCount].CopyTo(arr);
            }
            else
            {
                if (stageCount > 0)
                FlushStageToPool();
                arr = pool.ToArray();
            }

            return arr;
        }

        //public unsafe T[] ToArrayUnsafe()
        //{
        //    T[] arr;

        //    if (pool == null)
        //    {
        //        arr = GC.AllocateUninitializedArray<T>(stageCount);


        //        if (stageCount > 0)
        //        {
        //            //_stage[.._stageCount].CopyTo(arr);
        //            var numberOfBytes = (long)stageCount * Unsafe.SizeOf<T>();
        //            var byteBuffer = MemoryMarshal.Cast<T, byte>(stage);
        //            fixed (byte* pSrc = byteBuffer)
        //            fixed (T* pDst = arr)
        //            {
        //                Buffer.MemoryCopy(
        //                    source: pSrc,
        //                    destination: pDst,
        //                    destinationSizeInBytes: numberOfBytes,
        //                    sourceBytesToCopy: numberOfBytes);
        //            }
        //        }
        //    }
        //    else
        //    {
        //        if (stageCount > 0)
        //            FlushStageToPool();

        //        arr = pool.ToArrayUnsafe();
        //    }

        //    return arr;
        //}

        //public List<T> ToList()
        //{
        //    List<T> list;

        //    if (pool == null)
        //    {
        //        list = new(stageCount);
        //        CollectionsMarshal.SetCount(list, stageCount);
        //        var dst = CollectionsMarshal.AsSpan(list);
        //        stage[..stageCount].CopyTo(dst);
        //    }
        //    else
        //    {
        //        if (stageCount > 0)
        //            FlushStageToPool();
        //        list = pool.ToList();
        //    }

        //    return list;
        //}

        public List<T> ToList()
        {
            List<T> list;

            if (pool == null)
            {
                list = new(stageCount);
                //CollectionsMarshal.SetCount(list, stageCount);
                //var dst = CollectionsMarshal.AsSpan(list);
                //stage[..stageCount].CopyTo(dst);
                list.AddRange(stage[..stageCount]);
            }
            else
            {
                if (stageCount > 0)
                    FlushStageToPool();
                list = pool.ToList();
            }

            return list;
        }

        public void Dispose()
        {
            pool?.Dispose();
            pool = null;
            stage = default;
            stageCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushStageToPool()
        {
            if (stageCount == 0) return;
            EnsurePool();
            pool!.AddRange(stage[..stageCount]); // jedno AddRange = jedno fixed/memcopy
            stageCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsurePool()
        {
            pool ??= new PooledByteCollectionCollector<T>(pooledInit);
        }
    }
}
