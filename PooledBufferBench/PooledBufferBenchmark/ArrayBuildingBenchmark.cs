using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace PooledBufferBenchmark
{
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 2)]
    public class ArrayBuildingBenchmark
    {
        [Params(2, 10, 50, 1000)]
        public int ElementCount { get; set; }

        [Benchmark(Baseline = true)]
        public int[] List_ToArray()
        {
            var list = new List<int>();
            for (int i = 0; i < ElementCount; i++)
            {
                list.Add(i);
            }
            return list.ToArray();
        }

        //[Benchmark]
        //public int[] PooledBufferUnmanaged_ToArray()
        //{
        //    using var buffer = new PooledBufferUnmanaged<int>();
        //    for (int i = 0; i < ElementCount; i++)
        //    {
        //        buffer.Add(i);
        //    }
        //    var result = buffer.ToArray();
        //    return result;
        //}

        //[Benchmark]
        //public int[] PooledBufferInt_ToArray()
        //{
        //    using var buffer = new PooledIntBuffer();
        //    for (int i = 0; i < ElementCount; i++)
        //    {
        //        buffer.Add(i);
        //    }
        //    var result = buffer.ToArray();
        //    return result;
        //}

        //[Benchmark]
        //public int[] PooledBufferCollector_ToArray()
        //{
        //    using var buffer = new PooledByteCollector<int>();
        //    for (int i = 0; i < ElementCount; i++)
        //    {
        //        buffer.Add(i);
        //    }
        //    var result = buffer.ToArrayUnsafe();
        //    return result;
        //}

        

        [Benchmark]
        public int[] StackPooledBufferCollector_ToArray()
        {
            using var collector = new StackThenPoolCollectionCollector<int>(stackalloc int[32], 1024);
            for (int i = 0; i < ElementCount; i++)
            {
                collector.Add(i);
            }
            var result = collector.ToArray();
            return result;
        }

        //[Benchmark]
        //public int[] StackPooledBufferCollector_ToArrayUnsafe()
        //{
        //    using var collector = new StackThenPoolCollectionCollector<int>(stackalloc int[32], 1024);
        //    for (int i = 0; i < ElementCount; i++)
        //    {
        //        collector.Add(i);
        //    }
        //    var result = collector.ToArrayUnsafe();
        //    return result;
        //}

        [Benchmark]
        public List<int> StackPooledBufferCollector_ToList()
        {
            using var collector = new StackThenPoolCollectionCollector<int>(stackalloc int[32], 1024);
            for (int i = 0; i < ElementCount; i++)
            {
                collector.Add(i);
            }
            var result = collector.ToList();
            return result;
        }

        [Benchmark]
        public List<int> StackPooledBufferCollector_ToList2()
        {
            using var collector = new StackThenPoolCollectionCollector<int>(stackalloc int[32], 1024);
            for (int i = 0; i < ElementCount; i++)
            {
                collector.Add(i);
            }
            var result = collector.ToList2();
            return result;
        }
    }
}