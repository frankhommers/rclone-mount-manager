# Coding Conventions

**Analysis Date:** 2026-02-21

## Naming Patterns

**Files:**
- Use PascalCase for C# source files, matching the primary type name (examples: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs`).
- Use `*Tests.cs` suffix for xUnit test classes (examples: `RcloneMountManager.Tests/Services/RcloneOptionsServiceTests.cs`, `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`).

**Functions:**
- Use PascalCase for public methods and properties (examples: `GenerateScript`, `ParseOptionsJson`, `GetControlType` in `RcloneMountManager.Core/Services/MountManagerService.cs` and `RcloneMountManager.Core/Models/RcloneOption.cs`).
- Use camelCase for private methods and local variables (examples: `ResolveMountPoint`, `AppendQuickConnectScriptArgs`, `mountPoint` in `RcloneMountManager.Core/Services/MountManagerService.cs`).
- Name test methods as `MethodOrScenario_Condition_ExpectedResult` (examples: `GenerateScript_SkipsBoolFalse`, `ParseOptionsJson_RcGroup_ExcludesMetricsOptions`, `StringList_ModifyItem_SyncsToValue` in `RcloneMountManager.Tests/...`).

**Variables:**
- Prefix private instance fields with `_` (examples: `_runningMounts` in `RcloneMountManager.Core/Services/MountManagerService.cs`, `_selectedProfile` generated in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).
- Use descriptive nouns for collections and dictionaries (examples: `Profiles`, `AvailableBackends`, `_profileLogs` in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).

**Types:**
- Use PascalCase for classes/enums/interfaces (examples: `MountProfile`, `QuickConnectMode`, `IRcloneOptionDefinition` in `RcloneMountManager.Core/Models/`).
- Use `I` prefix for interfaces (example: `RcloneMountManager.Core/Models/IRcloneOptionDefinition.cs`).

## Code Style

**Formatting:**
- Not detected: no dedicated formatter config files (`.editorconfig`, `.prettierrc`, `eslint.config.*`) were found at repository root.
- Use .NET SDK defaults with project-level language features enabled in `RcloneMountManager.Core/RcloneMountManager.Core.csproj`, `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`, and `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj` (`<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` where present).
- Use file-scoped namespaces (examples: `namespace RcloneMountManager.Core.Services;` in `RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `namespace RcloneMountManager.Tests.Services;` in `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`).

**Linting:**
- Not detected: no explicit lint/analyzer ruleset configuration file found.
- Use `dotnet build RcloneMountManager.slnx` as the primary quality gate (documented in `README.md` and `.github/pull_request_template.md`).

## Import Organization

**Order:**
1. Third-party/framework namespaces first (examples: `using Avalonia;`, `using Serilog;`, `using CommunityToolkit.Mvvm.ComponentModel;`).
2. Project namespaces next (examples: `using RcloneMountManager.Core.Models;`, `using RcloneMountManager.ViewModels;`).
3. `System.*` namespaces are present and typically grouped in the same block (examples in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` and `RcloneMountManager.Core/Services/MountManagerService.cs`).

**Path Aliases:**
- Not applicable: C# namespace imports are used; no alias-based path mapping config detected.

## Error Handling

**Patterns:**
- Validate inputs early using guard clauses and throw `InvalidOperationException` or `ArgumentNullException.ThrowIfNull` (examples in `RcloneMountManager.Core/Services/MountManagerService.cs`).
- Wrap IO/process boundaries in `try/catch` and surface user-facing status messages (examples in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` methods `LoadProfiles`, `SaveProfiles`, `RunBusyActionAsync`).
- Prefer explicit exit-code checks for CLI calls and convert failures into exceptions (examples in `RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `RcloneMountManager.Core/Services/MountManagerService.cs`).

## Logging

**Framework:** Serilog

**Patterns:**
- Configure logging once at startup in `RcloneMountManager.GUI/Program.cs` (`WriteTo.Console`, `WriteTo.File`, rolling logs).
- Route operational logs through helper methods and mark error lines with `ERR:` for level mapping (see `AppendLog` in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`).

## Comments

**When to Comment:**
- Keep comments sparse and use them for framework/runtime caveats only (examples in `RcloneMountManager.GUI/Program.cs` and `RcloneMountManager.GUI/App.axaml.cs`).
- Avoid redundant comments where method/type names already explain behavior.

**JSDoc/TSDoc:**
- Not applicable: repository is C#-based and does not use JS/TS doc comments.

## Function Design

**Size:**
- Keep helper classes and parsers compact (examples: `RcloneMountManager.Core/Helpers/DurationHelper.cs`, `RcloneMountManager.Core/Services/RcloneOptionsService.cs`).
- Large orchestration logic currently lives in view model/service classes (examples: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Core/Services/MountManagerService.cs`); follow existing pattern when extending those modules.

**Parameters:**
- Pass cancellation tokens through async service boundaries (examples: `GetMountOptionsAsync` in `RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `StartAsync` in `RcloneMountManager.Core/Services/MountManagerService.cs`).
- Pass behavior-specific collaborators explicitly (example: `Action<string> log` in `RcloneMountManager.Core/Services/MountManagerService.cs`).

**Return Values:**
- Return domain objects and typed collections rather than raw dynamic data (examples: `IReadOnlyList<RcloneOptionGroup>` in `RcloneMountManager.Core/Services/RcloneOptionsService.cs`).
- Return `Task`/`Task<T>` for async operations; use sync methods for deterministic transforms (examples: `GenerateScript` in `RcloneMountManager.Core/Services/MountManagerService.cs`, `Parse`/`Format` in `RcloneMountManager.Core/Helpers/DurationHelper.cs`).

## Module Design

**Exports:**
- Use one primary public type per file, often `sealed` for concrete service/model classes (examples: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Core/Models/RcloneOption.cs`).
- Use static helper modules for pure utility logic (examples: `RcloneMountManager.Core/Helpers/DurationHelper.cs`, `RcloneMountManager.Core/Helpers/SizeSuffixHelper.cs`).

**Barrel Files:**
- Not used: no barrel-style export aggregation pattern detected in C# projects.

---

*Convention analysis: 2026-02-21*
