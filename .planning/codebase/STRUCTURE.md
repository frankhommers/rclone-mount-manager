# Codebase Structure

**Analysis Date:** 2026-02-21

## Directory Layout

```text
rclone-mount/
├── RcloneMountManager.Core/      # Shared models, typed option view-model primitives, and CLI services
├── RcloneMountManager.GUI/       # Avalonia desktop application (views, GUI view models, app entry)
├── RcloneMountManager.Tests/     # xUnit tests for core and GUI view-model behavior
├── scripts/                      # Packaging/build scripts (macOS app + DMG)
├── docs/plans/                   # Planning/design documents
├── .planning/codebase/           # Generated mapper outputs for orchestrator workflows
├── .github/workflows/            # CI/release automation
└── RcloneMountManager.slnx       # Solution definition for all projects
```

## Directory Purposes

**`RcloneMountManager.Core/`:**
- Purpose: Keep app-agnostic logic and data contracts reused by GUI and tests.
- Contains: `Models/` domain/config objects, `Services/` infrastructure adapters, `Helpers/` parsing/format helpers, `ViewModels/TypedOptionViewModel.cs`.
- Key files: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `RcloneMountManager.Core/Models/MountProfile.cs`.

**`RcloneMountManager.GUI/`:**
- Purpose: Host Avalonia runtime, UI composition, and command orchestration.
- Contains: `Program.cs`, `App.axaml*`, `Views/`, `ViewModels/`, `Controls/`, `Converters/`, `Assets/`.
- Key files: `RcloneMountManager.GUI/Program.cs`, `RcloneMountManager.GUI/App.axaml`, `RcloneMountManager.GUI/Views/MainWindow.axaml`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`.

**`RcloneMountManager.Tests/`:**
- Purpose: Verify parsing helpers, service logic, and GUI view-model behavior.
- Contains: mirrored subfolders `Helpers/`, `Models/`, `Services/`, `ViewModels/` with `*Tests.cs` files.
- Key files: `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`, `RcloneMountManager.Tests/ViewModels/MountOptionsViewModelTests.cs`.

**`scripts/`:**
- Purpose: Packaging automation outside normal app runtime.
- Contains: shell scripts.
- Key files: `scripts/build-macos-app.sh`, `scripts/create-dmg.sh`.

## Key File Locations

**Entry Points:**
- `RcloneMountManager.GUI/Program.cs`: Process entry and global logging bootstrap.
- `RcloneMountManager.GUI/App.axaml.cs`: Avalonia app initialization and root window setup.

**Configuration:**
- `Directory.Build.props`: shared assembly/file version properties.
- `RcloneMountManager.GUI/RcloneMountManager.GUI.csproj`: GUI dependencies, Avalonia settings, core project reference.
- `RcloneMountManager.Core/RcloneMountManager.Core.csproj`: core package dependencies.
- `.github/workflows/release-dmg.yml`: release automation workflow.

**Core Logic:**
- `RcloneMountManager.Core/Services/`: mount operations, backend discovery/creation, launch agent integration.
- `RcloneMountManager.Core/Models/`: mount profile/options/backend data contracts.
- `RcloneMountManager.GUI/ViewModels/`: command handlers and UI state orchestration.

**Testing:**
- `RcloneMountManager.Tests/Helpers/`: helper parser/formatter tests.
- `RcloneMountManager.Tests/Services/`: service behavior tests.
- `RcloneMountManager.Tests/ViewModels/`: view-model logic tests.

## Naming Conventions

**Files:**
- C# source files use PascalCase class-per-file naming: `MountManagerService.cs`, `MainWindowViewModel.cs`.
- XAML view pairs use `*.axaml` + `*.axaml.cs`: `MainWindow.axaml` and `MainWindow.axaml.cs`.
- Test files use `<Subject>Tests.cs`: `RcloneOptionsServiceTests.cs`.

**Directories:**
- Top-level project directories are `RcloneMountManager.<Area>`: `RcloneMountManager.Core`, `RcloneMountManager.GUI`, `RcloneMountManager.Tests`.
- Within projects, folders map to responsibility (`Services`, `Models`, `ViewModels`, `Views`, `Controls`, `Converters`).

## Where to Add New Code

**New Feature:**
- Primary code: orchestrating UI command/state in `RcloneMountManager.GUI/ViewModels/`; reusable domain/service logic in `RcloneMountManager.Core/Services/` and `RcloneMountManager.Core/Models/`.
- Tests: place alongside matching concern in `RcloneMountManager.Tests/<Area>/` (for example new service tests in `RcloneMountManager.Tests/Services/`).

**New Component/Module:**
- Implementation: new Avalonia views in `RcloneMountManager.GUI/Views/`, companion VM in `RcloneMountManager.GUI/ViewModels/`, and optional template/converter support in `RcloneMountManager.GUI/Controls/` or `RcloneMountManager.GUI/Converters/`.

**Utilities:**
- Shared helpers: put parsing/format-only helpers in `RcloneMountManager.Core/Helpers/`; keep command execution wrappers in `RcloneMountManager.Core/Services/`.

## Special Directories

**`RcloneMountManager.Core/bin` and `RcloneMountManager.Core/obj`:**
- Purpose: .NET build outputs/intermediate files.
- Generated: Yes.
- Committed: No.

**`RcloneMountManager.GUI/bin` and `RcloneMountManager.GUI/obj`:**
- Purpose: GUI assembly output and build intermediates.
- Generated: Yes.
- Committed: No.

**`RcloneMountManager.Tests/bin` and `RcloneMountManager.Tests/obj`:**
- Purpose: test binaries, coverage adapters, and intermediates.
- Generated: Yes.
- Committed: No.

**`.planning/codebase/`:**
- Purpose: generated architecture/stack/testing concern maps for GSD orchestration.
- Generated: Yes.
- Committed: Yes (repository-tracked planning artifacts).

---

*Structure analysis: 2026-02-21*
