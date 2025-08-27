using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Pooled buffer fields (replacing PooledByteCollectionCollector)
        private byte[] pooledBuffer;
        private int pooledWrittenBytes;
        private int pooledCount;
        
        private readonly int pooledInitialElements;
        private static readonly int SizeOfT = Unsafe.SizeOf<T>();

        public StackThenPoolCollectionCollector(scoped Span<T> initialBuffer, int pooledInitialElements = 64)
        {
            unsafe
            {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
                this.stage = initialBuffer;
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
            }
            stageCount = 0;
            
            // Initialize pooled buffer fields
            pooledBuffer = Array.Empty<byte>();
            pooledWrittenBytes = 0;
            pooledCount = 0;
            this.pooledInitialElements = pooledInitialElements;
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

            // 2) Staging is full → flush it to the pool
            FlushStageToPool();

            // 3) Send the large bulk directly to the pool in one or a few large AddRange calls,
            //    but keep a tail < stage.Length in staging (better for subsequent Add operations).
            int rem = values.Length;
            if (rem >= stage.Length)
            {
                int bulkLen = rem - (rem % stage.Length); // leave a tail for staging
                if (bulkLen > 0)
                {
                    AddRangeToPool(values[..bulkLen]);
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

        public int Count => pooledCount + stageCount;

        public T[] ToArray()
        {
            int totalCount = pooledCount + stageCount;
            T[] arr = GC.AllocateUninitializedArray<T>(totalCount);

            if (pooledCount > 0)
            {
                // Copy from pooled buffer
                Debug.Assert(pooledWrittenBytes == pooledCount * SizeOfT);
                var pooledSpan = MemoryMarshal.Cast<byte, T>(pooledBuffer.AsSpan(0, pooledWrittenBytes));
                pooledSpan.CopyTo(arr.AsSpan());
            }

            if (stageCount > 0)
            {
                // Copy from stage buffer
                stage[..stageCount].CopyTo(arr.AsSpan(pooledCount));
            }

            return arr;
        }

        public List<T> ToList()
        {
            int totalCount = pooledCount + stageCount;
            List<T> list = new(totalCount);
            CollectionsMarshal.SetCount(list, totalCount);
            var dst = CollectionsMarshal.AsSpan(list);

            if (pooledCount > 0)
            {
                // Copy from pooled buffer
                Debug.Assert(pooledWrittenBytes == pooledCount * SizeOfT);
                var pooledSpan = MemoryMarshal.Cast<byte, T>(pooledBuffer.AsSpan(0, pooledWrittenBytes));
                pooledSpan.CopyTo(dst);
            }

            if (stageCount > 0)
            {
                // Copy from stage buffer
                stage[..stageCount].CopyTo(dst[pooledCount..]);
            }

            return list;
        }

        public void Dispose()
        {
            if (pooledBuffer.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(pooledBuffer, clearArray: false);
                pooledBuffer = Array.Empty<byte>();
            }
            pooledWrittenBytes = 0;
            pooledCount = 0;
            stage = default;
            stageCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushStageToPool()
        {
            if (stageCount == 0) return;
            AddRangeToPool(stage[..stageCount]);
            stageCount = 0;
        }

        private unsafe void AddRangeToPool(scoped Span<T> values)
        {
            int n = values.Length;
            if (n == 0) return;

            int bytesToCopy = n * SizeOfT;
            EnsurePoolCapacity(bytesToCopy);

            fixed (T* pSrc = values)
            fixed (byte* pDst = &pooledBuffer[pooledWrittenBytes])
            {
                Buffer.MemoryCopy(
                    source: pSrc,
                    destination: pDst,
                    destinationSizeInBytes: (long)(pooledBuffer.Length - pooledWrittenBytes),
                    sourceBytesToCopy: (long)bytesToCopy);
            }

            pooledWrittenBytes += bytesToCopy;
            pooledCount += n;
        }

        private void EnsurePoolCapacity(int sizeHint)
        {
            // Initialize pool if needed
            if (pooledBuffer.Length == 0)
            {
                int bytes = Math.Max(pooledInitialElements * SizeOfT, 16);
                pooledBuffer = ArrayPool<byte>.Shared.Rent(bytes);
                return;
            }

            // Check if we need to grow
            int needed = pooledWrittenBytes + sizeHint;
            if (needed <= pooledBuffer.Length) return;

            // Grow the pool
            int newCap = Math.Max(pooledBuffer.Length * 2, needed);
            var newArr = ArrayPool<byte>.Shared.Rent(newCap);
            pooledBuffer.AsSpan(0, pooledWrittenBytes).CopyTo(newArr);
            ArrayPool<byte>.Shared.Return(pooledBuffer, clearArray: false);
            pooledBuffer = newArr;
        }
    }
}