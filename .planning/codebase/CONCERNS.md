# Codebase Concerns

**Analysis Date:** 2026-02-21

## Tech Debt

**[High] Main window view model owns too many responsibilities:**
- Issue: `MainWindowViewModel` combines profile persistence, mount orchestration, backend creation, UI state, logging, and command enablement in one class.
- Files: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Impact: Small changes can break unrelated behavior, review cost is high, and safe refactoring is difficult.
- Fix approach: Split into focused services/controllers (profile storage, mount orchestration, backend management) and keep `MainWindowViewModel` as composition/orchestration only.

**[Medium] Custom argument parser is incomplete for shell-like edge cases:**
- Issue: `ParseArguments` handles only simple double-quote grouping and ignores escaping rules/single quotes.
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`
- Impact: User-provided `ExtraOptions` can be parsed unexpectedly, producing incorrect mount commands or scripts.
- Fix approach: Replace with a robust argument tokenizer library or constrain input format with strict validation and UI affordances.

## Known Bugs

**[High] Quick Connect settings are saved but not restored:**
- Symptoms: Quick Connect fields are serialized, but reload initializes `QuickConnectMode`, endpoint, username, and password to empty/default values.
- Files: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Trigger: Save a profile with Quick Connect settings, restart app, reload profiles.
- Workaround: Re-enter Quick Connect settings after each restart.

**[Medium] Mount start path can report success before mount actually stabilizes:**
- Symptoms: `StartRcloneAsync` returns after a fixed delay (`Task.Delay(250)`), while process execution continues in background; caller sets "Operation completed." on return.
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Trigger: Start mount with invalid/fragile runtime parameters where process exits shortly after start.
- Workaround: Manually run refresh status and inspect logs after each start.

## Security Considerations

**[High] Sensitive credentials are written to disk in plain text profile JSON:**
- Risk: `QuickConnectPassword` is serialized directly into persisted profile payload.
- Files: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
- Current mitigation: Script generation defaults to environment-variable password injection unless `AllowInsecurePasswordsInScript` is enabled.
- Recommendations: Remove password persistence or encrypt at rest (OS keychain/DPAPI/libsecret), and migrate existing stored values.

**[Medium] Raw password passed as process argument during obscuring step:**
- Risk: `rclone obscure <rawPassword>` is invoked directly; command-line args can be visible to local process inspection tools.
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`
- Current mitigation: Password is obscured before mount command arguments are finalized.
- Recommendations: Prefer stdin-based obscuring or rclone config mechanisms that avoid passing secrets in process arguments.

## Performance Bottlenecks

**[Medium] Multiple external process launches per user action:**
- Problem: Mount/test flows call external binaries repeatedly (`rclone help`, `rclone version`, `rclone obscure`, `mount`) with synchronous wait points.
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Core/Services/RcloneOptionsService.cs`, `RcloneMountManager.Core/Services/RcloneBackendService.cs`
- Cause: Runtime discovery and option handling rely on command invocations instead of longer-lived sessions/cached capabilities.
- Improvement path: Cache stable capability checks aggressively per binary path and reduce per-action subprocess count.

## Fragile Areas

**[High] LaunchAgent enable/disable path does not verify launchctl execution success:**
- Files: `RcloneMountManager.Core/Services/LaunchAgentService.cs`
- Why fragile: `RunLaunchCtlAsync` executes with `CommandResultValidation.None` and ignores exit code/output, so failures can be silent.
- Safe modification: Capture and check exit code/stdout/stderr in `RunLaunchCtlAsync`; bubble clear errors to caller/UI.
- Test coverage: No `LaunchAgentService` tests under `RcloneMountManager.Tests/`.

**[Medium] Runtime mount-state detection depends on parsing `mount` command text:**
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`
- Why fragile: `IsMountedAsync` checks for substring `" on {mountPoint}"` in command output, which is platform/output-format dependent.
- Safe modification: Use platform-specific APIs or structured parsing by OS, with fixture-based tests per output format.
- Test coverage: No mount-state parsing tests under `RcloneMountManager.Tests/`.

## Scaling Limits

**[Medium] Single local JSON file for all profiles and secrets:**
- Current capacity: One file at `%AppData%/RcloneMountManager/profiles.json` (platform-specific equivalent) stores all profiles.
- Limit: No locking/versioning/conflict strategy; concurrent edits or future schema evolution can cause data consistency issues.
- Scaling path: Introduce versioned schema + atomic writes + migration strategy and optional per-profile file isolation.

## Dependencies at Risk

**[Medium] External binary coupling to host environment (`rclone`, `mount`, `umount`, `fusermount`, `launchctl`):**
- Risk: Behavior depends on installed binaries, versions, and OS tool availability.
- Impact: Runtime failures occur outside compile-time safety; cross-platform parity is hard to guarantee.
- Migration plan: Add startup capability diagnostics, stricter preflight checks, and compatibility matrix tests per OS/rclone version.

## Missing Critical Features

**[High] No CI workflow enforcing build/test quality gates on changes:**
- Problem: Repository has release automation, but no detected PR/commit CI that runs `dotnet test`.
- Blocks: Consistent regression prevention and fast feedback for refactors in `RcloneMountManager.Core/` and `RcloneMountManager.GUI/`.

## Test Coverage Gaps

**[High] Orchestration and integration-heavy paths are untested:**
- What's not tested: `MainWindowViewModel` command flows, profile persistence lifecycle, backend creation flow, and LaunchAgent behavior.
- Files: `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`, `RcloneMountManager.Core/Services/RcloneBackendService.cs`, `RcloneMountManager.Core/Services/LaunchAgentService.cs`
- Risk: Regressions in user-critical workflows can pass current test suite unnoticed.
- Priority: High

**[Medium] Process execution and error path behavior lacks direct tests:**
- What's not tested: mount start/stop failure paths, command output parsing, and cancellation behavior in `MountManagerService`.
- Files: `RcloneMountManager.Core/Services/MountManagerService.cs`, `RcloneMountManager.Tests/Services/MountManagerServiceTests.cs`
- Risk: Runtime-specific failures surface only in manual/system testing.
- Priority: Medium

---

*Concerns audit: 2026-02-21*
