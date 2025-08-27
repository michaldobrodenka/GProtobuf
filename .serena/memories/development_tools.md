# Development Tools and Configuration

## Code Analysis
- **Microsoft.CodeAnalysis.Analyzers** (v4.14.0) - Used in generator project
- **EnforceExtendedAnalyzerRules** - Enabled for generator project
- No explicit linting or formatting tools configured (no .editorconfig found)
- Standard .NET analyzers are implicitly used

## IDE and Development Environment
- Visual Studio 2022 (based on solution file format v17)
- Windows development environment
- .NET SDK 9.0.304 (compatible with .NET 8.0 targets)

## Debugging
- Debug configuration available in launchSettings.json for generator
- Conditional debugging code present (Debugger.Launch() in generator)
- Diagnostic MSBuild output available with `-v diag` flag

## Source Generator Development
- Generator outputs to `obj\Generated` directory
- EmitCompilerGeneratedFiles enabled in CrossTests for debugging
- Generator attached as Analyzer to consuming projects

## Recommended Linting/Formatting
Since no explicit formatting tools are configured, use default .NET tools:
```powershell
# Format code (requires dotnet-format tool)
dotnet format

# Analyze code
dotnet build /p:RunAnalyzers=true

# Run built-in code analysis
dotnet build /p:EnableNETAnalyzers=true
```

## Version Control
- Git repository
- Current branch: claude
- Main branch: main (for PRs)
- .serena directory for MCP configuration