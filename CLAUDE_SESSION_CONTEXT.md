# GProtobuf Development Session Context

## Project Overview
GProtobuf is a high-performance Protocol Buffers implementation for .NET using incremental source generators. The library creates custom serializers/deserializers at compile time with focus on efficient memory usage through Span<byte> operations.

## Recent Major Work Completed

### Guid Serialization Implementation (COMPLETED ✅)
**Objective**: Add comprehensive Guid support with protobuf-net BCL format compatibility

**Key Changes Made**:

1. **Generator Updates** (`GProtobuf.Generator/ObjectTree.cs`):
   - Implemented BCL-compatible Guid format using nested message with 2 fixed64 fields
   - Serialization: `Guid.ToByteArray()` → split into `low` (first 8 bytes) and `high` (last 8 bytes) → write as field 1 & 2 with Fixed64 wire type
   - Deserialization: Read nested message → parse field 1 & 2 as fixed64 → reconstruct Guid from bytes
   - Size calculation: Fixed 18 bytes (2 * (1-byte tag + 8-byte fixed64))
   - **Critical fix**: Changed `!= null` to `!= Guid.Empty` for proper empty value handling

2. **Core Infrastructure Updates**:
   - `SpanReader.cs`: Added `ReadFixed64()` method for BCL format
   - `StreamWriter.cs`: Added `WriteFixed64(ulong)` method  
   - `WriteSizeCalculator.cs`: Uses existing `WriteFixed64()` method
   - **Removed**: Obsolete `ReadGuid()` method that used incorrect 16-byte direct format

3. **Test Implementation** (ALL 9 TESTS PASSING ✅):
   - `TestModel/GuidTypesModel.cs`: New test model with 3 Guid properties
   - **GG Tests** (GProtobuf ↔ GProtobuf): 3 tests in `TestsGG.cs`
   - **GP Tests** (GProtobuf → Protobuf-net): 3 tests in `TestsGP.cs`  
   - **PG Tests** (Protobuf-net → GProtobuf): 3 tests in `TestsPG.cs`

**Wire Format Compatibility**:
```
Protobuf-net BCL Guid format:
0x0A 0x12 [nested message: field 1 (0x09 + 8 bytes low) + field 2 (0x11 + 8 bytes high)]
```

**Critical Success**: Perfect cross-compatibility between GProtobuf and protobuf-net for Guid serialization.

## Current Project State

### Architecture
- **GProtobuf.Core**: Serialization/deserialization primitives (SpanReader, StreamWriter, etc.)
- **GProtobuf.Generator**: Incremental source generator analyzing [ProtoContract] classes
- **GProtobuf.Tests**: Comprehensive test suite with TestModel classes
- **Generated Code**: Creates `{Namespace}.Serialization.cs` with Deserializers and Serializers classes

### Test Patterns
- **GG**: GProtobuf to GProtobuf (internal consistency)
- **GP**: GProtobuf to Protobuf-net (serialize with GProtobuf, deserialize with protobuf-net)
- **PG**: Protobuf-net to GProtobuf (serialize with protobuf-net, deserialize with GProtobuf)

### Build Commands
```bash
dotnet build                    # Build entire solution
dotnet test                     # Run all tests
dotnet test --filter "Guid"     # Run only Guid-related tests
dotnet clean GProtobuf.Tests && dotnet build GProtobuf.Tests  # Force regeneration
```

## Key Technical Details

### Guid Serialization Logic
**Empty Value Handling**: `if (obj.GuidValue != Guid.Empty)` - critical for skipping default values

**Serialization Process**:
1. Check `!= Guid.Empty`
2. Write field tag with `WireType.Len`
3. Write length prefix (18 bytes)
4. Convert to bytes: `guid.ToByteArray()`
5. Split: `low = BitConverter.ToUInt64(bytes, 0)`, `high = BitConverter.ToUInt64(bytes, 8)`
6. Write nested fields: `field 1: low (Fixed64)`, `field 2: high (Fixed64)`

### Source Generator Patterns
- Uses protobuf-net attributes (`[ProtoContract]`, `[ProtoMember]`) as input
- Generates code under `{Namespace}.Serialization` namespace
- TestModel classes generate `GProtobuf.Tests.TestModel.Serialization.*`

### Critical Files to Watch
- `ObjectTree.cs`: Core generator logic for all types
- `SpanReader.cs`: Low-level reading primitives
- `StreamWriter.cs`: Low-level writing primitives
- `TestModel/*.cs`: Test models using protobuf-net attributes

## Potential Future Work

### Known Optimization Areas
1. **Performance**: SpanReader/Writer optimizations for other types
2. **Types**: Additional .NET type support (DateTime, TimeSpan, etc.)
3. **Collections**: More efficient collection serialization
4. **Inheritance**: ProtoInclude hierarchy improvements

### Testing Gaps
- Performance benchmarks vs protobuf-net
- Large payload stress tests
- Memory allocation profiling

## Development Notes

### Important Conventions
- Always use absolute paths in tools
- TestModel classes should use `using ProtoBuf;` (not GProtobuf attributes)
- Generated code uses `global::` prefixes to avoid namespace conflicts
- Tests use `TestModel.Serialization.*` references (not fully qualified)

### Common Issues
- Source generator needs clean rebuild after changes
- TestModel namespace must match generator expectations
- Wire format compatibility requires exact BCL format implementation

## Session Summary
Successfully implemented complete Guid support with full protobuf-net compatibility, including comprehensive cross-compatibility test suite. All 9 Guid tests passing across GG/GP/PG patterns. Code is production-ready for Guid serialization use cases.