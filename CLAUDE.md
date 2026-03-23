# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

GProtobuf is a high-performance Protocol Buffers implementation for .NET that uses incremental source generators to create custom serializers/deserializers at compile time. The project focuses on efficient memory usage through Span&lt;byte&gt; operations and minimal allocations.

## Build Commands

### Basic Commands
- `dotnet build` - Build the entire solution
- `dotnet test` - Run all tests in GProtobuf.Tests
- `dotnet test --logger "console;verbosity=detailed"` - Run tests with detailed output
- `dotnet pack GProtobuf.Generator` - Create NuGet package for the source generator

### Project-Specific Commands
- `dotnet build GProtobuf.Generator` - Build only the source generator
- `dotnet build GProtobuf.Tests` - Build only the test project
- `dotnet run --project GProtobuf.Benchmark` - Run performance benchmarks

## Architecture

### Core Components

1. **GProtobuf.Generator** - Incremental source generator that analyzes classes marked with `[ProtoContract]` and generates serialization code
   - `SerializerGenerator.cs`: Main incremental generator implementation
   - `ObjectTree.cs`: Builds type hierarchy and generates deserialization methods
   - `StringBuilderWithIndent.cs`: Code generation utility

2. **GProtobuf.Core** - Core serialization/deserialization primitives
   - `SpanReader.cs`: High-performance span-based reader for Protocol Buffer wire format
   - `SpanWriter.cs`: Writer implementation (currently commented out)
   - `WireType.cs`: Protocol Buffer wire type definitions

3. **GProtobuf.Definitions** - Shared attribute definitions for ProtoMember/ProtoInclude

### Generated Code Pattern

The source generator creates `{Namespace}.Serialization.cs` files containing:
- `Deserializers` class with `Deserialize{ClassName}(ReadOnlySpan&lt;byte&gt; data)` methods
- `SpanReaders` class with `Read{ClassName}(ref SpanReader reader)` methods

### Supported Features

- Basic types: int, double, string, byte[]
- Arrays with packed/non-packed encoding
- DataFormat options: Default, FixedSize, ZigZag
- Inheritance via ProtoInclude attributes
- Custom message types through nested object reading

## Testing

The project uses xUnit with Microsoft.CodeAnalysis testing utilities for generator testing:
- `SerializerGeneratorTests.cs` contains tests for code generation
- Tests verify generated code contains expected method signatures
- Use `dotnet test` to run all tests

## Development Workflow

1. **Modifying the Generator**: Changes to `SerializerGenerator.cs` or `ObjectTree.cs` require rebuilding the generator project
2. **Testing Generated Code**: The `Serializer` project references the generator and demonstrates usage with `ModelClass.cs`
3. **Generated Files**: Check `Serializer\GeneratedFiles\` for actual generated serialization code

## Important Notes

- The generator targets `netstandard2.0` for broad compatibility
- Generated code uses `global::` prefixes to avoid namespace conflicts
- SpanReader uses ref struct for zero-allocation parsing
- All generated deserializers handle wire format validation and field skipping
- ProtoInclude fields must appear first in the serialized data