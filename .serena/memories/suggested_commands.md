# Suggested Commands for GProtobuf Development

## Build Commands
```powershell
# Build entire solution
dotnet build

# Build specific projects
dotnet build GProtobuf.Generator
dotnet build GProtobuf.Core
dotnet build GProtobuf.CrossTests

# Build in Release mode
dotnet build -c Release

# Clean build
dotnet clean
dotnet build --no-incremental
```

## Testing Commands
```powershell
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test project
dotnet test GProtobuf.CrossTests

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "FullyQualifiedName~SerializerGeneratorTests"
```

## Benchmark Commands
```powershell
# Run benchmarks
dotnet run --project GProtobuf.Benchmark -c Release

# Run specific benchmark
dotnet run --project GProtobuf.Benchmark -c Release -- --filter "*PrimitiveTypes*"
```

## Package Commands
```powershell
# Create NuGet package for generator
dotnet pack GProtobuf.Generator

# Pack with specific version
dotnet pack GProtobuf.Generator /p:PackageVersion=1.0.0

# Pack all projects
dotnet pack
```

## Generator Development Commands
```powershell
# View generated files (they're in obj\Generated directory after build)
dir GProtobuf.CrossTests\obj\Debug\net8.0\Generated\GProtobuf.Generator\

# Force regeneration
dotnet clean GProtobuf.CrossTests
dotnet build GProtobuf.CrossTests
```

## Windows/PowerShell Utilities
```powershell
# List files
dir
Get-ChildItem

# Change directory
cd [path]
Set-Location [path]

# View file content
type [file]
Get-Content [file]

# Search in files
findstr /s /i "pattern" *.cs
Select-String -Pattern "pattern" -Path *.cs -Recurse

# Git commands
git status
git diff
git log --oneline -10
git add .
git commit -m "message"

# Remove files/directories
Remove-Item [path] -Recurse -Force
del [file]
rmdir [directory] /s /q
```

## Development Workflow Commands
```powershell
# Typical development cycle
dotnet build                           # Build solution
dotnet test                            # Run tests
dotnet run --project GProtobuf.Benchmark -c Release  # Check performance

# After making generator changes
dotnet build GProtobuf.Generator       # Rebuild generator
dotnet clean GProtobuf.CrossTests      # Clean test project
dotnet build GProtobuf.CrossTests      # Rebuild to trigger generation
dotnet test GProtobuf.CrossTests       # Test generated code
```

## Debugging Commands
```powershell
# Run with verbose MSBuild output
dotnet build -v detailed

# Run with diagnostic MSBuild output
dotnet build -v diag

# Check generated files location
dir GProtobuf.CrossTests\obj\Debug\net8.0\generated\ -Recurse
```