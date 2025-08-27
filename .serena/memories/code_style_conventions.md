# Code Style and Conventions

## C# Language Settings
- **Language Version**: Latest (C# 12+)
- **Nullable Reference Types**: Enabled in most projects (except CrossTests)
- **Unsafe Code**: Allowed in Core and CrossTests projects
- **Implicit Usings**: Enabled in most projects

## Naming Conventions
- **Classes/Interfaces**: PascalCase (e.g., `SerializerGenerator`, `SpanReader`)
- **Methods**: PascalCase (e.g., `GetProtoMemberAttributes`, `Initialize`)
- **Parameters/Variables**: camelCase (e.g., `typeDefinition`, `namespaceName`)
- **Private Fields**: Underscore prefix likely (based on standard C# conventions)
- **Constants**: UPPER_CASE or PascalCase

## Code Organization
- One type per file generally
- Related functionality grouped (e.g., `SpanReader.cs` and `SpanReader.Collections.cs`)
- Test models organized in dedicated `TestModel/` directories
- Benchmarks organized by category

## Attribute Usage
- `[ProtoContract]` - Marks classes for serialization
- `[ProtoMember]` - Marks properties/fields for serialization
- `[ProtoInclude]` - Marks inheritance relationships

## Generated Code Patterns
- Files named `{Namespace}.Serialization.cs`
- Contains `Deserializers` and `SpanReaders` classes
- Uses `global::` prefix to avoid namespace conflicts
- Methods follow pattern: `Deserialize{ClassName}`, `Read{ClassName}`

## Performance Conventions
- Use of `Span<byte>` and `ReadOnlySpan<byte>` for zero-allocation
- `ref struct` for stack-only types (e.g., SpanReader)
- Unsafe code blocks where performance critical
- Minimal allocations design principle

## Testing Conventions
- xUnit for unit tests
- FluentAssertions for assertions
- Test classes suffixed with `Tests`
- Cross-testing with protobuf-net for compatibility verification

## Documentation
- XML documentation comments for public APIs
- TODO comments for pending work
- CLAUDE.md for AI assistant guidance

## Source Generator Conventions
- Incremental generators for performance
- Metadata-based approach using Roslyn APIs
- Generated code emitted with proper formatting/indentation