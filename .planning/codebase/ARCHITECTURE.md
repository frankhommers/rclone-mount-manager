# Architecture

**Analysis Date:** 2026-02-21

## Pattern Overview

**Overall:** MVVM desktop application with a shared core library and command-driven infrastructure adapters.

**Key Characteristics:**
- UI logic is centralized in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` and bound from Avalonia XAML views in `RcloneMountManager.GUI/Views/`.
- Domain models and infrastructure services live in `RcloneMountManager.Core/Models/` and `RcloneMountManager.Core/Services/`, with no direct Avalonia dependency.
- External operations (rclone, mount, launchctl) are executed via `CliWrap` inside service layer classes such as `RcloneMountManager.Core/Services/MountManagerService.cs`.

## Layers

**Presentation (Avalonia UI):**
- Purpose: Render views, bind commands/properties, and host dynamic option templates.
- Location: `RcloneMountManager.GUI/Views/`, `RcloneMountManager.GUI/App.axaml`, `RcloneMountManager.GUI/Controls/`.
- Contains: `.axaml` views, view code-behind, data templates, converters.
- Depends on: GUI view models in `RcloneMountManager.GUI/ViewModels/`, plus Avalonia runtime.
- Used by: Application startup path in `RcloneMountManager.GUI/Program.cs` and `RcloneMountManager.GUI/App.axaml.cs`.

**Application/ViewModel layer:**
- Purpose: Coordinate UI actions, profile state, async workflows, and persistence orchestration.
- Location: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`, `RcloneMountManager.GUI/ViewModels/MountOptionInputViewModel.cs`.
- Contains: RelayCommand handlers, state flags (`IsBusy`, `HasPendingChanges`), profile serialization flow.
- Depends on: Core models/services (`RcloneMountManager.Core/Models/*`, `RcloneMountManager.Core/Services/*`).
- Used by: XAML bindings in `RcloneMountManager.GUI/Views/MainWindow.axaml` and `RcloneMountManager.GUI/Views/MountOptionsView.axaml`.

**Core domain + infrastructure layer:**
- Purpose: Represent mount/backend option data and execute system/rclone operations.
- Location: `RcloneMountManager.Core/Models/`, `RcloneMountManager.Core/Services/`, `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`.
- Contains: `MountProfile`, option abstractions (`IRcloneOptionDefinition`), mount execution, backend discovery, launch agent integration.
- Depends on: `CliWrap`, `System.Text.Json`, and OS process/file APIs from .NET.
- Used by: GUI view models and tests (`RcloneMountManager.Tests/`).

## Data Flow

**Mount lifecycle flow:**

1. User triggers a command bound in `RcloneMountManager.GUI/Views/MainWindow.axaml` (for example `StartMountCommand`).
2. `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs` validates state, syncs option values, and calls `MountManagerService.StartAsync`.
3. `RcloneMountManager.Core/Services/MountManagerService.cs` resolves mount type/arguments and invokes OS commands (`rclone`, `mount`, `umount`) through `CliWrap`.
4. Process output/error streams are fed back to `AppendLog` in `MainWindowViewModel`, reflected in UI via `Logs`/`StatusText` bindings.

**Dynamic mount options flow:**

1. `MountOptionsViewModel.LoadOptionsAsync` in `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs` calls `RcloneOptionsService.GetMountOptionsAsync`.
2. `RcloneMountManager.Core/Services/RcloneOptionsService.cs` runs `rclone rc --loopback options/info` and parses JSON into `RcloneOptionGroup`/`RcloneOption`.
3. `MountOptionInputViewModel` and `TypedOptionViewModel` map option metadata to control-specific state and inclusion semantics.
4. `OptionControlTemplateSelector` in `RcloneMountManager.GUI/Controls/OptionControlTemplateSelector.cs` picks DataTemplates from `RcloneMountManager.GUI/App.axaml` by option control type.

**State Management:**
- Primary mutable state is in observable view models (`MainWindowViewModel`, `MountOptionsViewModel`, `MountProfile`).
- Profile persistence is JSON file based at runtime path built in `MainWindowViewModel` (`%AppData%/RcloneMountManager/profiles.json` semantics via `Environment.SpecialFolder.ApplicationData`).
- Running mount process state is tracked in-memory by `ConcurrentDictionary` instances in `MountManagerService`.

## Key Abstractions

**MountProfile:**
- Purpose: Canonical runtime and persisted config for a mount profile.
- Examples: `RcloneMountManager.Core/Models/MountProfile.cs`, nested `PersistedProfile` DTO in `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`.
- Pattern: Observable model with derived display properties plus serialization mapping.

**Option definition contract:**
- Purpose: Unify mount options and backend options for shared typed UI rendering.
- Examples: `RcloneMountManager.Core/Models/IRcloneOptionDefinition.cs`, `RcloneMountManager.Core/Models/RcloneOption.cs`, `RcloneMountManager.Core/Models/RcloneBackendOption.cs`.
- Pattern: Interface + polymorphic view model (`TypedOptionViewModel`) + template selector.

**Service adapters for external commands:**
- Purpose: Encapsulate CLI interactions and OS-specific behavior.
- Examples: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Core/Services/RcloneBackendService.cs`, `RcloneMountManager.Core/Services/LaunchAgentService.cs`.
- Pattern: Validate input, execute command, parse/transform output, throw domain-level `InvalidOperationException` on failure.

## Entry Points

**Desktop process entry:**
- Location: `RcloneMountManager.GUI/Program.cs`
- Triggers: OS process start.
- Responsibilities: Configure Serilog, start Avalonia desktop lifetime, catch fatal startup exceptions.

**Avalonia app initialization:**
- Location: `RcloneMountManager.GUI/App.axaml.cs`
- Triggers: Avalonia framework startup.
- Responsibilities: Load application resources, disable duplicate validation plugin, create `MainWindow` with `MainWindowViewModel`.

**Main UI composition:**
- Location: `RcloneMountManager.GUI/Views/MainWindow.axaml`
- Triggers: Window load and user interactions.
- Responsibilities: Bind profile editing, backend builder, mount actions, script preview, and activity log.

## Error Handling

**Strategy:** Service layer throws explicit exceptions; view model layer catches and converts to status/log updates.

**Patterns:**
- Command failures are converted to `InvalidOperationException` in core services (for example `MountManagerService`, `RcloneBackendService`, `RcloneOptionsService`).
- UI commands execute through `RunBusyActionAsync` in `MainWindowViewModel`, which catches exceptions and sets `StatusText` plus `ERR:` log lines.

## Cross-Cutting Concerns

**Logging:** Serilog is configured in `RcloneMountManager.GUI/Program.cs`, and user-visible logs are buffered per profile in `MainWindowViewModel.AppendLog`.

**Validation:** Input validation is imperative in command handlers/services (for example required source/backend/password checks in `MainWindowViewModel` and `MountManagerService`).

**Authentication:** No central auth subsystem; credentials are profile fields (`QuickConnectUsername`, `QuickConnectPassword` in `MountProfile`) passed to rclone, with optional obscuring via `rclone obscure` in `MountManagerService`.

---

*Architecture analysis: 2026-02-21*
