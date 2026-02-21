# Testing Patterns

**Analysis Date:** 2026-02-21

## Test Framework

**Runner:**
- xUnit 2.9.3 via `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`.
- Test SDK: `Microsoft.NET.Test.Sdk` 17.14.1 via `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`.
- Test project: `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj` (references both `RcloneMountManager.Core/RcloneMountManager.Core.csproj` and `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`).

**Assertion Library:**
- xUnit `Assert.*` APIs (examples in `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs` and `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`).

**Run Commands:**
```bash
dotnet test RcloneMountManager.slnx                                  # Run all tests
dotnet test RcloneMountManager.slnx --filter "ClassName=RcloneOptionsServiceTests"  # Run targeted suite
dotnet test RcloneMountManager.slnx --collect:"XPlat Code Coverage" # Coverage collection (coverlet.collector)
```

## Test File Organization

**Location:**
- Separate test project under `RcloneMountManager.Tests/` with folders mirroring production namespaces: `Helpers/`, `Models/`, `Services/`, `ViewModels/`.

**Naming:**
- File naming uses `<Subject>Tests.cs` (examples: `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs`, `RcloneMountManager.Tests/Services/RcloneOptionsServiceTests.cs`).
- Class naming matches file naming and keeps one test class per subject.

**Structure:**
```
RcloneMountManager.Tests/
├── Helpers/*Tests.cs
├── Models/*Tests.cs
├── Services/*Tests.cs
└── ViewModels/*Tests.cs
```

## Test Structure

**Suite Organization:**
```csharp
public class MountManagerServiceTests
{
    private readonly MountManagerService _service = new();

    [Fact]
    public void GenerateScript_SkipsBoolFalse()
    {
        var profile = CreateProfile();
        profile.MountOptions["debug_fuse"] = "false";

        var script = _service.GenerateScript(profile);

        Assert.DoesNotContain("--debug-fuse", script);
    }
}
```

**Patterns:**
- Follow Arrange/Act/Assert sequencing with explicit local setup in each test (examples across `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`).
- Use `[Theory]` + `[InlineData]` for parser/format matrix tests (examples in `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs` and `RcloneMountManager.Tests/Helpers/SizeSuffixHelperTests.cs`).
- Use `[Fact]` for scenario-driven behavior checks (examples in `RcloneMountManager.Tests/ViewModels/MountOptionInputViewModelTests.cs`).

## Mocking

**Framework:**
- No mocking framework detected (no Moq/NSubstitute package reference in `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`).

**Patterns:**
```csharp
private static MountProfile CreateProfile()
{
    return new MountProfile
    {
        Type = MountType.RcloneFuse,
        Source = "remote:media",
        MountPoint = "/tmp/test-mount",
        MountOptions = new Dictionary<string, string>(),
    };
}
```
- Prefer direct construction of real domain objects as test inputs (pattern in `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`).
- Use reflection only when needed to set private state for focused tests (pattern in `RcloneMountManager.Tests/ViewModels/MountOptionsViewModelTests.cs` via `BindingFlags.NonPublic`).

**What to Mock:**
- Not applicable in current codebase; tests currently avoid mocks and focus on pure/domain behavior.

**What NOT to Mock:**
- Keep core value objects and option models real (`MountProfile`, `RcloneOption`, `RcloneBackendOption`) as done in `RcloneMountManager.Tests/Models/*.cs` and `RcloneMountManager.Tests/ViewModels/*.cs`.

## Fixtures and Factories

**Test Data:**
```csharp
[Theory]
[InlineData("5m", 0, 5, 0)]
[InlineData("1h30m", 1, 30, 0)]
public void Parse_ValidDuration_ReturnsTimeSpan(string input, int hours, int minutes, int seconds)
{
    var result = DurationHelper.Parse(input);
    Assert.Equal(new TimeSpan(hours, minutes, seconds), result);
}
```

**Location:**
- Inline fixture data is stored directly in test files with `[InlineData]` and helper builders (examples in `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs` and `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`).

## Coverage

**Requirements:**
- No minimum coverage threshold enforced by config (no threshold settings detected in repository config files).
- Coverage tooling is available via `coverlet.collector` package in `RcloneMountManager.Tests/RcloneMountManager.Tests.csproj`.

**View Coverage:**
```bash
dotnet test RcloneMountManager.slnx --collect:"XPlat Code Coverage"
```

## Test Types

**Unit Tests:**
- Primary test type. Focus on deterministic logic in helpers/models/services/view-model state transitions (examples in `RcloneMountManager.Tests/Helpers/`, `RcloneMountManager.Tests/Models/`, `RcloneMountManager.Tests/Services/`, `RcloneMountManager.Tests/ViewModels/`).

**Integration Tests:**
- Limited. Current suites mainly validate pure methods and in-memory behavior; no dedicated integration test project or external dependency harness detected.

**E2E Tests:**
- Not used (no Playwright/Cypress/Appium project or config detected).

## Common Patterns

**Async Testing:**
```csharp
[Fact]
public async Task RefreshBackendsAsync_LoadsOptions()
{
    await viewModel.RefreshBackendsAsync();
    Assert.True(viewModel.AvailableBackends.Count > 0);
}
```
- Use async test methods returning `Task` when testing async APIs. This pattern is required when adding coverage for methods like `StartAsync`/`StopAsync` in `RcloneMountManager.Core/Services/MountManagerService.cs`.

**Error Testing:**
```csharp
[Fact]
public void Parse_Null_ReturnsZero()
{
    Assert.Equal(TimeSpan.Zero, DurationHelper.Parse(null));
}
```
- Validate failure/boundary behavior using explicit assertions for null/empty/default inputs (examples in `RcloneMountManager.Tests/Helpers/DurationHelperTests.cs` and `RcloneMountManager.Tests/Helpers/SizeSuffixHelperTests.cs`).

## Quality Gates

- PR checklist requires successful build and tests in `.github/pull_request_template.md`.
- Core documented quality flow is `dotnet build RcloneMountManager.slnx` then `dotnet test RcloneMountManager.slnx` as shown in `README.md` and `CONTRIBUTING.md`.
- CI workflow currently focuses on release packaging in `.github/workflows/release-dmg.yml`; no separate always-on CI test workflow is detected.

---

*Testing analysis: 2026-02-21*
