using BenchmarkDotNet.Running;

namespace PooledBufferBenchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<ArrayBuildingBenchmark>();
        }
    }
}
