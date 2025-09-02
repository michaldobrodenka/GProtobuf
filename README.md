\## Project Overview



GProtobuf is a high-performance Protocol Buffers implementation for .NET that uses incremental source generators to create custom serializers/deserializers at compile time. The project focuses on efficient memory usage through Span\&lt;byte\&gt; operations and minimal allocations.



\# Features for v0 #



\## Supported data types for v0 ##



* todo for every type if size is known or dynamic



\###Primitive data types###



* int, byte, float, double, bool, Guid, TimeSpan (compatibility level with protobuf-net 200)
* todo - which types can be fixed size, which types are varint
* string - how it is written



\###Custom messages###



* classes with supported data types



\###Collections###

* array of any supported data types (Except collections - jagged arrays are not supported as in protobuf-net)
* packed collections - TODO which types
* fixedSize - TODO which types
* List<>
* HashSet<>
* Dictionary<,>, if List<> is used in Dict key/value - by default is packed (todo really?)
* Dictionary<,> can be key or value in Dictionary<,>
* Any collection derived from/implementing List<>,ICollection<>, IList<>, IDictionary<,>
* When serializing - for list use for instead of foreach for perf
* byte\[]/List<byte> - special case todo why



\###Tuples (,)and KeyValue<,>###

* Tuple<,> ... Tuple<,,,,,,,>
* (,)... (,,,,,,,)



\###Virtual types###

* In case of Dict<,> or Tuple<,> - we should create virtual methods like SpanReaders.ReadTupleOfStringAndFloat(), StreamWriters.WriteKeyValueOfMessageAndGuid



\## Inheritance ##



with \[ProtoInclude], TODO how it is written

TODO multiple Inheritance 





\##Functionality##



* tag and wiretype - since we are generating code - precompute in code for serialization
* collect which types have \[ProtoContract], \[ProtoMember(x)] and \[ProtoInclude()]
* create tree of hierarchy
* add virtual types
* build in ref structs (GProtobuf.Core)

&nbsp;	- SpanReader - for reading (deserializing) from Span<byte>

&nbsp;	- StreamWriter - for serializing to Stream

&nbsp;	- BufferWriter - for serializing to IBufferWriter

&nbsp;	- WriteSizeCalculator - for tracking size of messages



* we are not serializing when value == default(dataType). For nullable values we are not serializing if value == null, then eg 0 is serialized for int etc
* for every defined class with ProtoContract (protocol buffers message) we generate 



1. `public static class Deserializers` with methods for Deserialize class eg `static global::Todos.Model.CustomNested DeserializeCustomNested(ReadOnlySpan<byte> data)` method which read object from Span<byte>

2\. `public static class Serializers` with serialize methods `static void SerializeCustomNested(Stream stream, global::Todos.Model.CustomNested obj)` (and `SerializeCustomNested(IBufferWriter<byte> buffer, global::Todos.Model.CustomNested obj)`) - write object into Stream/IBufferWriter



3\. generate SpanReaders - todo description and example

4\. generate StreamWriters - todo description and example

5\. generate BufferWriters - todo description and example

6\. generate SizeCalculators - todo description and example

&nbsp;			



