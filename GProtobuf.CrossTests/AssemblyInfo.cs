using GProtobuf.Core;

// Disable OnePassStreamWriter generation - needs more work for map entry types
// Known issues:
// 1. global::int[] syntax error for primitive array types in map entries
// 2. Missing XXXContent methods for collection/dictionary values in map entries
// 3. Tuple Item8+ accessor issues (partially fixed via GetTupleItemAccessor)
// [assembly: GProtobufOptions(GenerateOnePassStreamWriter = true)]
