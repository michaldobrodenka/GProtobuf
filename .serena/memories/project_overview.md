# GProtobuf Project Overview

## Purpose
GProtobuf is a high-performance Protocol Buffers implementation for .NET that uses incremental source generators to create custom serializers/deserializers at compile time. The project focuses on efficient memory usage through Span<byte> operations and minimal allocations.

## Tech Stack
- **Platform**: .NET 8.0 (main projects), .NET Standard 2.0 (generator)
- **Language**: C# with latest language features
- **Testing**: xUnit with FluentAssertions
- **Benchmarking**: BenchmarkDotNet
- **Code Generation**: Roslyn Source Generators (incremental)
- **Dependencies**: 
  - Microsoft.CodeAnalysis for source generation
  - protobuf-net for cross-testing compatibility
  
## Key Technologies
- Span<byte> and ref structs for zero-allocation parsing
- Incremental source generators for compile-time code generation
- Unsafe code blocks for performance-critical sections
- Protocol Buffers wire format implementation

## Project Status
According to README:
- [X] Incremental source generator - Complete
- [ ] Tests - In progress
- [ ] Create NuGet from the generator project - Pending
- [ ] Refactoring - Pending
- [ ] Documentation - Pending