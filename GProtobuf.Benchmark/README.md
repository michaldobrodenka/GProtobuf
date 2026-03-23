# GProtobuf Benchmarks

This project contains comprehensive performance benchmarks comparing **GProtobuf** with **protobuf-net**.

## Benchmark Categories

### 1. Primitive Types
Tests serialization/deserialization of basic data types:
- int, long, float, double, bool, string, byte[]
- Fixed-size encoding variants  
- ZigZag encoding variants

### 2. Collections
Tests arrays and lists of primitive types:
- List<T> and T[] for various primitive types
- Packed encoding for numeric types
- String arrays and collections

### 3. Nested Messages
Tests complex object hierarchies:
- Person, Address, Company models
- Deep nesting with collections of nested objects
- Real-world scenarios with multiple relationships

## Running Benchmarks

### Run All Benchmarks
```bash
dotnet run --project GProtobuf.Benchmark --configuration Release
```

### Run Specific Categories
```bash
# Primitive types only
dotnet run --project GProtobuf.Benchmark --configuration Release -- primitives

# Collections only  
dotnet run --project GProtobuf.Benchmark --configuration Release -- collections

# Nested messages only
dotnet run --project GProtobuf.Benchmark --configuration Release -- nested
```

## What's Measured

- **Throughput**: Operations per second
- **Memory Allocation**: Gen 0/1/2 collections, allocated bytes
- **Execution Time**: Mean, median, min, max times
- **Comparison**: GProtobuf vs protobuf-net baseline

## Output

Results are generated in multiple formats:
- Console output with summary tables
- HTML reports in `BenchmarkDotNet.Artifacts/results/`
- CSV files for further analysis
- GitHub markdown format for documentation

## Key Performance Areas

The benchmarks focus on GProtobuf's main performance advantages:
- **Zero-allocation deserialization** using `Span<byte>` 
- **Compile-time code generation** vs runtime reflection
- **Optimized wire format encoding/decoding**
- **Memory-efficient array/collection handling**