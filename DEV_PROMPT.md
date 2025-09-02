# Developer Prompt: Reimplement GProtobuf with Binary Compatibility to protobuf-net

You are implementing a .NET source-generator based Protocol Buffers library named **GProtobuf** with a non-negotiable requirement: **binary compatibility with `protobuf-net` Level 200 (legacy)** for supported shapes. The output MUST match `protobuf-net` on the wire.

## Goals
1. **Binary compatibility** with `protobuf-net` Level 200:
   - Use `bcl.proto` wire shapes for `DateTime`, `TimeSpan`, `Guid`, `Decimal`.
   - Exact field numbers, wire types, ZigZag behavior, default elision.
   - Map entries `{ key (1), value (2) }`, repeated as individual entry messages.
   - Repeated numeric/enum fields support packed encoding; `string/bytes/message` are never packed.
2. **Performance**:
   - Source-generated serializers/deserializers; no reflection in hot paths.
   - `ReadOnlySpan<byte>` + ref structs for parsing; precomputed tags; size precomputation.
3. **Ergonomics**:
   - Attributes: `[ProtoContract]`, `[ProtoMember(n)]`, `[ProtoInclude(n, typeof(T))]`.
   - Optional `DataFormat` (`Default`, `FixedSize`, `ZigZag`) to align with protobuf-net expectations.
4. **Extensibility**:
   - Clear type model and visitor-based codegen for primitives, collections, messages, tuples, and dictionaries.

## Hard Requirements (Wire Format)
- **Enums**: varint by default; support `FixedSize`.
- **Integers**: honor `Default`/`FixedSize`/`ZigZag` like protobuf-net.
- **`string`/`bytes`/messages**: length-delimited, never packed.
- **Maps**: encode as repeated entry messages with `key (field=1)` and `value (field=2)`; no packed optimization inside entries.
- **`DateTime`** (Level 200): `bcl.DateTime { sint64 value; TimeSpanScale scale; DateTimeKind kind; }`
  - `value` = offset from 1970-01-01 in units of `scale` (ZigZag).
  - `scale` in `{ DAYS=0, HOURS=1, MINUTES=2, SECONDS=3, MILLISECONDS=4, TICKS=5, MINMAX=15 }`.
  - `MINMAX` + small `value` for sentinels (Min/Max).
- **`TimeSpan`** (Level 200): `bcl.TimeSpan` analog; default ticks via ZigZag if `scale=TICKS`.
- **`Guid`** (Level 200): two `fixed64` parts (`bcl.Guid` shape).
- **Default elision**: do not write default values. Null ref types omitted.
- **Field order**: handle in any order; parser must tolerate interleaving; last wins for non-repeated.
- **Float/Double**: Fixed32/Fixed64 (little-endian as per protobuf).

## Codegen Architecture
- **Analyzer** builds a type graph from `[ProtoContract]`, `[ProtoMember]`, `[ProtoInclude]`.
- **Generators** emit: `Serializers`, `Deserializers`, `SpanReaders`, `SizeCalculators`, optional `BufferWriters`.
- **Virtual Types** for generics (e.g., Tuple, Dictionary) with de-duplication.
- **Options**: switch for Level 240+ (Timestamp/Duration) as a future feature.

## Testing Matrix
- Round-trip for: primitives, enums, strings, bytes, nested messages.
- Repeated packed vs. unpacked for all numeric types.
- Maps with string keys/values and numeric keys/values.
- Inheritance with `[ProtoInclude]` across multiple levels.
- `DateTime` cases: MinValue, MaxValue, Unix epoch, typical UTC/local/unspecified, ticks granularity.
- `TimeSpan` cases: zero, negative, large spans, tick granularity.
- `Guid` fixed layout tests against `protobuf-net` byte vectors.
- Cross-verify byte-for-byte against `protobuf-net` serialization outputs.

## Non-Goals (v0)
- Well-known types mode (Level 240+) — can be added later behind a flag.
- Nested/jagged collections (e.g., `List<List<T>>`).

## Deliverables
- `GProtobuf.Core` runtime (SpanReader, StreamWriter, SizeCalculator).
- Source generators for serializers/deserializers and helpers.
- Benchmarks and a conformance test suite that compares bytes with `protobuf-net`.
