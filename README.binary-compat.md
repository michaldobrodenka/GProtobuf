# GProtobuf

## Project Overview

GProtobuf is a high‑performance Protocol Buffers implementation for .NET that uses **incremental source generators** to create custom serializers/deserializers at compile time. It focuses on **binary compatibility with `protobuf-net`** (Level 200 by default) and on efficient memory usage via `Span<byte>` operations and minimal allocations.

## ⚠️ Binary Compatibility Goals (protobuf-net)

This project aims to be **binary‑compatible with `protobuf-net`** for the supported surface area. That means:

- **Wire format** strictly follows Google Protocol Buffers, **plus** `protobuf-net` conventions for .NET types.
- **Compatibility level 200** (legacy) is the default: we use `bcl.proto` shapes for `DateTime`, `TimeSpan`, `Guid`, `Decimal`.
- Optionally, a future mode may target **Level 240+** (well-known types), mapping `DateTime → google.protobuf.Timestamp` and `TimeSpan → google.protobuf.Duration`. Until then, we emit the Level 200 encodings to match `protobuf-net` on the wire.

### Type compatibility specifics

- **Enum**: varint (`int32`) by default; can be forced to fixed via `DataFormat.FixedSize`. Matches `protobuf-net` behavior.
- **Numeric primitives**: match wire types (`varint` / `fixed32` / `fixed64`) and ZigZag for `sint32/sint64` (`DataFormat.ZigZag`).
- **`string` / `bytes` / messages**: length‑delimited; **never packed**.
- **`DateTime` (Level 200)**: encoded as `bcl.DateTime` — `sint64 value` = offset from 1970‑01‑01 in units given by `scale`; `scale` ∈ `DAYS|HOURS|MINUTES|SECONDS|MILLISECONDS|TICKS|MINMAX`; optional `kind` (`UNSPECIFIED|UTC|LOCAL`). `MINMAX`+small `value` encodes sentinels (`MinValue`/`MaxValue`). ZigZag applies to `value`.
- **`TimeSpan` (Level 200)**: encoded as `bcl.TimeSpan`/`bcl.TimeSpan?` equivalent; default representation uses ticks (varint via ZigZag) unless `scale` dictates otherwise.
- **`Guid` (Level 200)**: encoded as `bcl.Guid` (two `fixed64` parts) for binary compatibility with `protobuf-net`.
- **Maps** (`Dictionary<TKey,TValue>`): encoded as repeated **map entry messages** `{ key (field=1), value (field=2) }`. For **non‑packable** types (e.g., `string`), **each element repeats the tag**; no packed encoding.
- **Repeated numeric fields**: support **packed** encoding where applicable (**varint/fixed** numeric & enum types only). `string/bytes/message` are **not packable**.
- **Default‑value elision**: do not write zero/defaults (`0`, `false`, `null`, empty structs) — matches `protobuf-net` semantics.

> If `protobuf-net` diverges by version, the goal is to match **the canonical Level 200 encodings** first and add opt‑in flags for alternative encodings later.

---

## Features (v0)

### Supported Data Types

**Varint (variable‑length)**  
- `int`, `long` — signed integers (optionally ZigZag with `DataFormat.ZigZag` for negative optimization)  
- `uint`, `ulong` — unsigned integers  
- `bool` — varint `0`/`1` (practically fixed size on the wire)  
- `byte`, `sbyte` — 8‑bit integers  
- `short`, `ushort` — 16‑bit integers (can use `DataFormat.FixedSize`)

**Fixed‑size**  
- `float` — 32‑bit floating point (**Fixed32**)  
- `double` — 64‑bit floating point (**Fixed64**)  
- `Guid` — **two Fixed64** parts (Level 200)  
- `TimeSpan` — typically `int64` ticks (ZigZag varint) under Level 200

**Length‑delimited**  
- `string` — UTF‑8 with length prefix  
- `byte[]` — raw bytes with length prefix  
- **messages** — nested length‑delimited

**DataFormat options**  
- `Default` — standard Protocol Buffers encoding  
- `FixedSize` — force fixed‑size for applicable integer types  
- `ZigZag` — signed integer optimization (`sint32/sint64` semantics)

### Custom Messages

- Classes marked with `[ProtoContract]` (and fields `[ProtoMember(n)]`)  
- Nested message types supported  
- Null references are not serialized  
- `struct` with `[ProtoContract]` supported

### Collections

**Supported**  
- Arrays (`T[]`)  
- `List<T>`  
- `HashSet<T>`  
- `Dictionary<TKey,TValue>`  
- Any implementing `ICollection<T>`, `IList<T>`, `IDictionary<TKey,TValue>` (where feasible at generation time)

**Packed encoding**  
- **Not used by default** for numeric repeats unless configured; **never used** for `string/bytes/message`.  
- For maps: **no packed** — maps are repeated entry messages; keys/values follow entry fields `1` and `2` respectively.

**Fixed‑size arrays**  
- With `DataFormat.FixedSize`, numeric types use fixed encodings (e.g., float/double arrays avoid varint overhead).

**Special cases**  
- `byte[]` / `List<byte>` optimized as raw blocks (single length prefix)  
- Nested/jagged collections (e.g., `List<List<T>>`) **not supported** in v0  
- Dictionary values may be collections (still **not packed** for non‑packable element types)

### Tuples and KeyValuePair

- `Tuple<T1..T8>` and `ValueTuple<T1..T8>`  
- `KeyValuePair<TKey,TValue>` used internally for dictionaries  
- Generator produces **virtual types** for complex generics (see below)

### Virtual Types (generated helpers)

**SpanReaders**  
- `ReadTupleOfStringAndFloat(ref SpanReader reader)`  
- `ReadKeyValueOfMessageAndGuid(ref SpanReader reader)`

**StreamWriters**  
- `WriteTupleOfStringAndFloat(StreamWriter writer, Tuple<string,float> value)`  
- `WriteKeyValueOfMessageAndGuid(StreamWriter writer, KeyValuePair<Message,Guid> value)`

Generators must deduplicate virtual helpers to avoid duplicates.

---

## Inheritance

Use `[ProtoInclude]` on base types:

```csharp
[ProtoContract]
[ProtoInclude(100, typeof(DerivedClass1))]
[ProtoInclude(101, typeof(DerivedClass2))]
public abstract class BaseClass
{
    [ProtoMember(1)]
    public string BaseProp { get; set; }
}

[ProtoContract]
public class DerivedClass1 : BaseClass
{
    [ProtoMember(2)]
    public int DerivedProp { get; set; }
}
```

**How it’s written**  
- Each derived instance is **wrapped inside** the field specified by `[ProtoInclude]` on the base; that field contains the derived message payload.  
- Polymorphic deserialization instantiates the correct derived type.  
- Multiple levels (A→B→C→D) are supported.  
- Abstract bases require at least one `[ProtoInclude]` mapping.

**Wire shape (conceptual)**  
- Derived envelope contains base fields nested within the derived payload.

---

## Functionality

### Core components

- **Tag & WireType**: precomputed in generated code for speed  
- **Type discovery**: scan `[ProtoContract]`, `[ProtoMember]`, `[ProtoInclude]`  
- **Hierarchy tree**: base/derived relationships  
- **Virtual types**: helpers for generics (e.g., Tuple, Dictionary)

### Runtime components (GProtobuf.Core)

- **SpanReader** — ref struct, zero‑allocation parsing from `ReadOnlySpan<byte>`  
- **StreamWriter** — buffered writes to `Stream`  
- **BufferWriter** — writes to `IBufferWriter<byte>` (planned)  
- **WriteSizeCalculator** — computes exact message size in advance

### Serialization rules

- Default values are omitted  
- Nullable primitives: only present when non‑null  
- Conformant to Protocol Buffers + protobuf‑net Level 200 addenda

### Generated code layout

1. **Deserializers**  
   ```csharp
   public static Namespace.ClassName DeserializeClassName(ReadOnlySpan<byte> data)
   ```

2. **Serializers**  
   ```csharp
   public static void SerializeClassName(Stream stream, Namespace.ClassName obj)
   public static void SerializeClassName(IBufferWriter<byte> buffer, Namespace.ClassName obj)
   ```

3. **SpanReaders**  
   ```csharp
   public static Namespace.ClassName ReadClassName(ref SpanReader reader)
   ```

4. **StreamWriters**  
   ```csharp
   public static void WriteClassName(StreamWriter writer, Namespace.ClassName value)
   ```

5. **BufferWriters** (planned)  
   ```csharp
   public static void WriteClassName(ref BufferWriter writer, Namespace.ClassName value)
   ```

6. **SizeCalculators**  
   ```csharp
   public static int CalculateClassNameSize(Namespace.ClassName value)
   ```

---

## Architecture (v2 planning)

1. **Modular generation**
   - `TypeAnalyzer.cs`, `DeserializerGenerator.cs`, `SerializerGenerator.cs`, `SizeCalculatorGenerator.cs`, `VirtualTypeGenerator.cs`, `InheritanceHandler.cs`, `WireFormatHelper.cs`

2. **Template‑based generation**
   - Prefer Scriban/T4 over string concatenation

3. **Type system abstraction**
   ```csharp
   interface IProtobufType {
       string GenerateReader(GenerationContext ctx);
       string GenerateWriter(GenerationContext ctx);
       string GenerateSizeCalculator(GenerationContext ctx);
       WireType GetWireType(DataFormat fmt);
   }
   ```

4. **Visitor pattern**
   - `PrimitiveTypeVisitor`, `CollectionTypeVisitor`, `MessageTypeVisitor`, `TupleTypeVisitor`

5. **Options**
   - Naming conventions, nullability, sync vs async, perf vs size modes

6. **Error handling**
   - Compile‑time diagnostics for unsupported shapes, field ID conflicts, cycles

7. **Incremental build optimizations**
   - Cache analysis; regenerate only impacted nodes

8. **Testing**
   - Round‑trip tests for all generated types; perf benchmarks

---

## Performance Considerations

- Zero‑allocation parsing where possible  
- Precomputed tags; minimized branching in hot paths  
- Packed arrays (for numeric repeats) when enabled  
- No reflection in hot paths (all source‑generated)  
- Span‑based operations; exact size precomputation

---

## Compatibility

- **Primary**: `protobuf-net` **Level 200** (bcl types) for supported shapes  
- Standard Protocol Buffers wire format elsewhere  
- Generator targets .NET Standard 2.0; generated code targets .NET 8+

---

## Quick Start

```csharp
[ProtoContract]
public class Person {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
}

var person = new Person { Id = 123, Name = "Alice" };
using var ms = new MemoryStream();
GProtobuf.Serializers.SerializePerson(ms, person);
ms.Position = 0;
var clone = GProtobuf.Deserializers.DeserializePerson(ms.ToArray());
```

---

## Notes & Gotchas

- **Packed vs. unpacked**: only numeric/enum repeats are packable; `string/bytes/message` are not.  
- **Map entries**: `{ key=1, value=2 }`, each repeated element has its own entry (no packing).  
- **ZigZag** applies to `sint32/sint64` (and to `bcl.DateTime.value` / `bcl.TimeSpan.value`).  
- **Fixed32/Fixed64** are little‑endian on the wire as per Protobuf.
