# GProtobuf

## Project Overview

GProtobuf is a high-performance Protocol Buffers implementation for .NET that uses incremental source generators to create custom serializers/deserializers at compile time. The project focuses on efficient memory usage through Span<byte> operations and minimal allocations.

## Features for v0

### Supported Data Types

#### Primitive Data Types

**Variable-length encoded (Varint - dynamic size):**
* `int`, `long` - signed integers (use ZigZag encoding with DataFormat.ZigZag for negative number optimization)
* `uint`, `ulong` - unsigned integers  
* `bool` - encoded as varint (0 or 1) - so in reality serialization have fixed size.
* `byte`, `sbyte` - 8-bit integers
* `short`, `ushort` - 16-bit integers (can use DataFormat.FixedSize)

**Fixed-size encoded:**
* `float` - 32-bit floating point (WireType.Fixed32b - always 4 bytes)
* `double` - 64-bit floating point (WireType.Fixed64b - always 8 bytes)
* `Guid` - 128-bit identifier (encoded as two Fixed64 fields)
* `TimeSpan` - duration (encoded as int64 ticks with varint)

**Length-delimited (dynamic size):**
* `string` - UTF-8 encoded with length prefix
* `byte[]` - raw byte array with length prefix

**DataFormat options:**
* `DataFormat.Default` - standard Protocol Buffers encoding
* `DataFormat.FixedSize` - forces fixed-size encoding for int/long types
* `DataFormat.ZigZag` - optimizes negative number encoding for signed integers

### Custom Messages

* Classes marked with `[ProtoContract]` attribute
* Support for nested message types
* Automatic handling of null references (not serialized when null)
* Struct types also supported with `[ProtoContract]`

### Collections

**Supported collection types:**
* Arrays (`T[]`) - for any supported type T
* `List<T>` - generic lists
* `HashSet<T>` - unique value collections
* `Dictionary<TKey, TValue>` - key-value pairs
* Collections implementing `ICollection<T>`, `IList<T>`, `IDictionary<TKey, TValue>`

**Packed encoding:**
* Numeric primitive types in collections doesn't use use packed encoding by default! (it's a pitty)
* Numeric primitive types in collections in Dictionary<TKey, TValue> keys and values use packed encoding by default
* Packed arrays store all values consecutively with a single length (in bytes) prefix
* Supported for: int, uint, long, ulong, float, double, bool, byte, sbyte, short, ushort
* Not packed: strings, custom messages, complex types

**Fixed-size arrays:**
* When DataFormat.FixedSize is specified, numeric types use fixed-size encoding
* Useful for float/double arrays to avoid varint overhead

**Special cases:**
* `byte[]` and `List<byte>` - optimized as raw byte arrays without per-element encoding
* Nested collections not supported (no jagged arrays or List<List<T>>)
* Dictionary values can be collections (packed by default)

### Tuples and KeyValuePair

* `Tuple<T1, T2>` through `Tuple<T1, T2, T3, T4, T5, T6, T7, T8>`
* ValueTuples: `(T1, T2)` through `(T1, T2, T3, T4, T5, T6, T7, T8)`
* `KeyValuePair<TKey, TValue>` - used internally for Dictionary serialization

### Virtual Types

For complex generic types like `Dictionary<TKey, TValue>` and `Tuple<T1, T2>`, the generator creates specialized virtual methods:

**SpanReaders:**
* `ReadTupleOfStringAndFloat(ref SpanReader reader)`
* `ReadKeyValueOfMessageAndGuid(ref SpanReader reader)`

**StreamWriters:**
* `WriteTupleOfStringAndFloat(StreamWriter writer, Tuple<string, float> value)`
* `WriteKeyValueOfMessageAndGuid(StreamWriter writer, KeyValuePair<Message, Guid> value)`

When creating virtual type, generator must check if virtual type is already defined.

## Inheritance

Inheritance is supported through the `[ProtoInclude]` attribute:

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

**How it works:**
* ProtoInclude fields are written first in the wire format
* Each derived type is wrapped with its ProtoInclude field ID
* Supports polymorphic deserialization - correct derived type is instantiated
* Multiple inheritance levels supported (A -> B -> C -> D)
* Abstract base classes require ProtoInclude field to be present

**Wire format:**
* Base class fields are nested inside derived class envelope
* Example for DerivedClass1: `[Field 100: [Field 1: BaseProp] [Field 2: DerivedProp]]`

## Functionality

### Core Components

* **Tag and WireType** - Pre-computed in generated code for optimal performance
* **Type Discovery** - Analyzes types marked with `[ProtoContract]`, `[ProtoMember(n)]`, and `[ProtoInclude(n, Type)]`
* **Hierarchy Tree** - Builds complete type hierarchy including inheritance relationships
* **Virtual Types** - Generated for generic type combinations (Tuple, Dictionary, etc.)

### Built-in Reader/Writer Types (GProtobuf.Core)

* **SpanReader** - Zero-allocation ref struct for deserializing from `ReadOnlySpan<byte>`
* **StreamWriter** - Writes directly to Stream with internal buffering
* **BufferWriter** - Writes to `IBufferWriter<byte>` (planned)
* **WriteSizeCalculator** - Pre-calculates message size for buffer allocation

### Serialization Rules

* Default values are not serialized (0 for numbers, null for references, false for bool)
* For nullable types, null is not serialized; non-null values serialize their actual value
* All serialization follows Protocol Buffers wire format specification

### Generated Code Structure

For each class with `[ProtoContract]`, the generator creates:

1. **Deserializers class** with static methods:
   ```csharp
   public static global::Namespace.ClassName DeserializeClassName(ReadOnlySpan<byte> data)
   ```
   Reads complete objects from byte spans.

2. **Serializers class** with static methods:
   ```csharp
   public static void SerializeClassName(Stream stream, global::Namespace.ClassName obj)
   public static void SerializeClassName(IBufferWriter<byte> buffer, global::Namespace.ClassName obj)
   ```
   Writes objects to streams or buffer writers.

3. **SpanReaders class** with ref-based methods:
   ```csharp
   public static global::Namespace.ClassName ReadClassName(ref SpanReader reader)
   ```
   Low-level reader methods for nested message parsing and field-by-field deserialization.

4. **StreamWriters class** with writer methods:
   ```csharp
   public static void WriteClassName(StreamWriter writer, global::Namespace.ClassName value)
   ```
   Low-level writer methods for nested message writing with proper length prefixing.

5. **BufferWriters class** (planned):
   ```csharp
   public static void WriteClassName(ref BufferWriter writer, global::Namespace.ClassName value)
   ```
   High-performance writing to IBufferWriter<byte> targets.

6. **SizeCalculators class** with size computation:
   ```csharp
   public static int CalculateClassNameSize(global::Namespace.ClassName value)
   ```
   Pre-calculates exact byte size needed for serialization, enabling single-allocation buffers.

## Architecture Improvements (Lessons Learned)

Based on experience with the current implementation, here are recommended architectural improvements for a v2 refactor:

### 1. Modular Code Generation
**Current issue:** Everything is in a single massive `ObjectTree.cs` file (8000+ lines)

**Proposed solution:**
* `TypeAnalyzer.cs` - Type discovery and hierarchy building
* `DeserializerGenerator.cs` - Generates deserialization code
* `SerializerGenerator.cs` - Generates serialization code  
* `SizeCalculatorGenerator.cs` - Generates size calculation code
* `VirtualTypeGenerator.cs` - Handles Tuple/Dictionary virtual methods
* `InheritanceHandler.cs` - ProtoInclude and polymorphism logic
* `WireFormatHelper.cs` - Wire type determination and encoding helpers

### 2. Template-Based Generation
Instead of string concatenation, use T4 templates or Scriban for cleaner code generation:
* Separate templates for each generated class (Deserializers, Serializers, etc.)
* Easier to maintain and visualize generated code structure
* Better handling of indentation and formatting

### 3. Type System Abstraction
Create a proper type model:
```csharp
interface IProtobufType
{
    string GenerateReader(GenerationContext context);
    string GenerateWriter(GenerationContext context);
    string GenerateSizeCalculator(GenerationContext context);
    WireType GetWireType(DataFormat format);
}
```

### 4. Visitor Pattern for Type Processing
Replace switch statements with visitor pattern for better extensibility:
* `PrimitiveTypeVisitor`
* `CollectionTypeVisitor`
* `MessageTypeVisitor`
* `TupleTypeVisitor`

### 5. Configuration and Options
* Support for custom naming conventions
* Configurable null handling strategies
* Option to generate synchronous vs async methods
* Performance vs size optimization modes

### 6. Better Error Handling
* Generate compile-time errors for unsupported type combinations
* Validate ProtoMember field IDs don't conflict
* Check for circular dependencies in type hierarchy

### 7. Incremental Generation Optimization
* Cache type analysis results between compilations
* Only regenerate affected types when changes occur
* Use Roslyn's incremental generator pipeline more effectively

### 8. Testing Infrastructure
* Generate test harnesses alongside serializers
* Round-trip testing for all generated types
* Performance benchmarks as part of generation

## Performance Considerations

* Zero-allocation deserialization using ref structs
* Pre-computed tag values eliminate runtime calculations
* Packed arrays minimize wire format overhead
* Generated code eliminates reflection overhead
* Span-based operations for optimal memory usage
* Size pre-calculation enables single buffer allocation

## Compatibility

* Compatible with protobuf-net level 200 for supported types
* Follows standard Protocol Buffers wire format
* Generator is .NET Standard 2.0
* Generated code is .NET 8+