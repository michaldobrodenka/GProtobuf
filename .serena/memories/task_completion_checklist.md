# Task Completion Checklist

When completing any coding task in the GProtobuf project, ensure you:

## 1. Code Quality Checks
- [ ] Code follows established naming conventions (PascalCase for types/methods, camelCase for parameters)
- [ ] Code uses appropriate C# language features (nullable reference types, pattern matching, etc.)
- [ ] Generated code includes proper `global::` prefixes to avoid namespace conflicts
- [ ] Unsafe code is only used where absolutely necessary for performance

## 2. Build Verification
- [ ] Run `dotnet build` to ensure the solution builds without errors
- [ ] Check for any build warnings and address them
- [ ] If modifying the generator, rebuild dependent projects to verify generation works

## 3. Testing
- [ ] Run `dotnet test` to ensure all existing tests pass
- [ ] Add new tests for any new functionality
- [ ] For generator changes, verify generated code in `obj\Generated` directory
- [ ] Run cross-tests with protobuf-net for compatibility if modifying serialization logic

## 4. Performance (if applicable)
- [ ] Run benchmarks with `dotnet run --project GProtobuf.Benchmark -c Release` if changes affect performance
- [ ] Ensure no performance regressions
- [ ] Verify zero-allocation goals are maintained for hot paths

## 5. Documentation
- [ ] Update XML documentation comments for public APIs
- [ ] Update CLAUDE.md if adding new architectural patterns or commands
- [ ] Add TODO comments for any incomplete functionality

## 6. Source Control (when requested)
- [ ] Stage changes with `git add`
- [ ] Write descriptive commit message
- [ ] Include Co-Authored-By attribution if appropriate

## Quick Verification Commands
```powershell
# Minimum verification
dotnet build
dotnet test

# Full verification
dotnet clean
dotnet build
dotnet test --logger "console;verbosity=detailed"
dotnet run --project GProtobuf.Benchmark -c Release  # If performance-related
```

## Generator-Specific Checklist
If modifying the source generator:
- [ ] Incremental generation is maintained (no full regeneration on every change)
- [ ] Generated code is properly formatted with indentation
- [ ] Generated file names follow pattern `{Namespace}.Serialization.cs`
- [ ] Test the generator with various attribute configurations

## Common Issues to Check
- Namespace conflicts (use `global::` prefix in generated code)
- Memory allocations in hot paths (use Span<T> and ref structs)
- Proper wire format handling (correct WireType for each data type)
- ProtoInclude fields appearing first in serialized data

## Note on Linting/Formatting
- **No automated linting configured** (confirmed by user)
- **No CI/CD pipeline yet** (confirmed by user)
- Rely on IDE built-in formatting and manual code review
- Follow existing code style patterns in the codebase