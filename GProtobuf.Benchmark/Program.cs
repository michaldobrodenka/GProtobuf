using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using GProtobuf.Benchmark.Benchmarks;
using System;

namespace GProtobuf.Benchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ManualConfig.Create(DefaultConfig.Instance);
            
            // Add .NET 8.0 job with optimized settings
            config.AddJob(Job.Default
                .WithRuntime(CoreRuntime.Core80)
                .WithWarmupCount(3)
                .WithIterationCount(5)
                //.WithGcServer(true)
                .WithGcConcurrent(true)
                .WithId(".NET 8.0"));

            // Uncomment to run with InProcess toolchain (faster but less accurate)
            // config.AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));

            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "primitives":
                        Console.WriteLine("Running Primitive Types Benchmarks...");
                        BenchmarkRunner.Run<PrimitiveTypesBenchmark>(config);
                        break;
                    case "collections":
                        Console.WriteLine("Running Collections Benchmarks...");
                        BenchmarkRunner.Run<CollectionsBenchmark>(config);
                        break;
                    case "nested":
                        Console.WriteLine("Running Nested Messages Benchmarks...");
                        BenchmarkRunner.Run<NestedMessagesBenchmark>(config);
                        break;
                    case "all":
                    default:
                        RunAllBenchmarks(config);
                        break;
                }
            }
            else
            {
                RunAllBenchmarks(config);
            }
        }

        private static void RunAllBenchmarks(IConfig config)
        {
            var bench = new NestedMessagesBenchmark();
            bench.Setup();
            bench.GProtobuf_SerializeCustom();

            Console.WriteLine("Running All Benchmarks...");
            Console.WriteLine();

            //Console.WriteLine("1. Primitive Types Benchmarks");
            //BenchmarkRunner.Run<PrimitiveTypesBenchmark>(config);

            //Console.WriteLine();
            //Console.WriteLine("2. Collections Benchmarks");
            //BenchmarkRunner.Run<CollectionsBenchmark>(config);

            //Console.WriteLine();
            Console.WriteLine("3. Nested Messages Benchmarks");
            BenchmarkRunner.Run<NestedMessagesBenchmark>(config);

            Console.WriteLine();
            Console.WriteLine("All benchmarks completed!");
        }
    }
}