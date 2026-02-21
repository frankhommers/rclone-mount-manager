# Phase 1: Startup Enablement and Safety Gates - Research

**Researched:** 2026-02-21
**Domain:** macOS launchd startup orchestration + startup preflight validation in a .NET/Avalonia app
**Confidence:** HIGH

## Summary

Phase 1 should be implemented as a strict "preflight -> gate -> persist -> apply launch agent" pipeline per profile, with manual mount/unmount kept on a separate execution path. The existing codebase already has most building blocks (profile persistence, script generation, launch-agent file generation, CLI process execution), but startup enablement currently bypasses preflight and uses legacy `launchctl load/unload` calls that are documented as legacy.

The standard macOS-first approach for per-user startup jobs is LaunchAgents in `~/Library/LaunchAgents` with a `Label`, `ProgramArguments`, and `RunAtLoad` in the plist. For operational safety, use modern launchctl domain-target commands (`bootstrap`/`bootout`) and check command exit codes explicitly. Validate plist syntax with `plutil -lint` before activation.

Preflight checks must be first-class and typed (critical vs warning). For this phase, critical checks are: rclone binary resolvable/executable, mount path valid/writable, cache path valid/writable (if configured), and credentials available for the chosen profile mode. Only allow startup enable when all critical checks pass; disabling startup must never block manual mount workflows.

**Primary recommendation:** Implement a dedicated `StartupPreflightService` and gate `ToggleStartupAsync` so enablement is impossible until critical preflight checks pass, then activate LaunchAgent with `launchctl bootstrap gui/<uid> <plist>` and persist profile startup state only after successful activation.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `launchd` + LaunchAgents (`~/Library/LaunchAgents`) | macOS built-in | Per-user run-at-login startup | Apple-supported user-login automation mechanism |
| `launchctl` (`bootstrap`, `bootout`, `enable`, `disable`) | macOS built-in | Load/unload/enable service instances in target domains | Current command set; `load/unload` are legacy in current manpage |
| `plutil` | macOS built-in | Validate plist syntax before load | Fast, deterministic syntax validation for launchd plists |
| .NET `net10.0` + `CliWrap` | net10.0 + 3.10.0 | Execute `launchctl`, `plutil`, and `rclone` commands safely | Already used across service layer; consistent process handling |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.Json` | .NET runtime | Persist per-profile startup flags/settings | Persisting startup state in `profiles.json` |
| `System.IO.Path.GetFullPath` | .NET runtime | Normalize/validate file paths deterministically | Preflight for mount/cache path validity |
| `rclone` CLI (`lsd`, `listremotes`, `config file`, `obscure`) | current installed binary | Verify connectivity/config/credential availability and password handling | Preflight credential + remote checks, secure argument preparation |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| User LaunchAgents + `launchctl` | `SMAppService` login items | Better native UX for signed app bundles, but higher packaging/signing complexity; not needed for this phase's macOS-first baseline |
| Runtime shell-script execution | Direct `ProgramArguments` in plist to launch app helper/binary | Fewer script files but tighter coupling to executable paths and argument evolution |
| `launchctl load/unload -w` | `bootstrap/bootout` + `enable/disable` | `load/unload` retained for legacy compatibility; newer subcommands are recommended in current `launchctl` docs |

**Installation:**
```bash
# No new NuGet packages required for Phase 1.
# Runtime dependencies remain: rclone + macOS launchd/launchctl/plutil.
```

## Architecture Patterns

### Recommended Project Structure
```
RcloneMountManager.Core/
├── Services/
│   ├── StartupPreflightService.cs     # runs typed startup checks and returns report
│   ├── LaunchAgentService.cs          # create/validate plist + bootstrap/bootout
│   └── MountManagerService.cs         # manual mount lifecycle (unchanged path)
├── Models/
│   ├── StartupPreflightReport.cs      # result + critical/warning classification
│   └── StartupCheckResult.cs          # per-check outcome and message
RcloneMountManager.GUI/
└── ViewModels/
    └── MainWindowViewModel.cs         # orchestrates preflight gating + persistence
```

### Pattern 1: Preflight-Gated Startup Enable
**What:** Run required checks, classify failures, block enable on critical failures, and show explicit per-check messages.
**When to use:** Every startup enable attempt and when profile inputs affecting startup safety change.
**Example:**
```csharp
// Source: project pattern + requirements SAFE-01/02/03
var report = await startupPreflight.RunAsync(profile, cancellationToken);
if (!report.CriticalChecksPassed)
{
    throw new InvalidOperationException(report.ToUserFacingMessage());
}
await launchAgentService.EnableAsync(profile, script, log, cancellationToken);
profile.StartAtLogin = true;
SaveProfiles();
```

### Pattern 2: Domain-Targeted launchctl Operations
**What:** Use explicit domain targets for per-user jobs (`gui/<uid>`), then bootstrap/bootout service plist.
**When to use:** Enabling/disabling startup entries for currently logged-in macOS user.
**Example:**
```bash
# Source: local `man launchctl` (modern subcommands)
launchctl bootstrap "gui/$UID" "$HOME/Library/LaunchAgents/com.rclonemountmanager.profile.<id>.plist"
launchctl bootout "gui/$UID" "com.rclonemountmanager.profile.<id>"
```

### Pattern 3: Persist-After-Success State Mutation
**What:** Update profile `StartAtLogin` only after launchctl operation succeeds; disabling should preserve manual mount fields.
**When to use:** Any startup toggle action.
**Example:**
```csharp
// Source: existing `MainWindowViewModel` persistence pattern
await launchAgentService.DisableAsync(profile, log, cancellationToken);
profile.StartAtLogin = false;
SaveProfiles();
```

### Anti-Patterns to Avoid
- **Toggle without preflight:** allows broken startup configs and violates SAFE-03.
- **Using `load/unload` as primary path:** legacy behavior and weaker domain clarity vs `bootstrap/bootout`.
- **Mutating startup flags before command success:** persisted state drifts from actual launchd state.
- **Coupling startup enable to manual mount runtime state:** can break BOOT-02 by regressing manual workflow.
- **Single string error for all failures:** violates SAFE-02 explicit per-failure reporting.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Plist syntax validation | Custom XML checks | `plutil -lint` | Handles real plist parser rules and catches malformed files early |
| launchd state inference | Parse arbitrary `launchctl print` text format | Command exit status + known labels + file presence checks | `print` output is explicitly non-stable API |
| Path canonicalization | Manual `~`, relative, separator parsing | `Path.GetFullPath` + explicit existence/writability checks | Avoids cross-platform/path edge-case bugs |
| Credential "obscuring" | Homemade reversible obfuscation | `rclone obscure` for rclone-compatible values | Matches rclone's expected password format |
| Startup wiring | Custom login polling loop in app | LaunchAgent `RunAtLoad` + launchctl domain loading | Native OS scheduler semantics, less runtime complexity |

**Key insight:** most Phase 1 failures are orchestration/edge-case failures, not algorithmic ones; using native launchd/rclone/.NET primitives avoids fragile custom control-flow code.

## Common Pitfalls

### Pitfall 1: Legacy launchctl Path and Silent Failure
**What goes wrong:** `load/unload` path appears to work but can hide operational errors; legacy commands are not preferred.
**Why it happens:** historical examples and older docs.
**How to avoid:** move to `bootstrap/bootout` with explicit domain target and check exit codes/stdErr.
**Warning signs:** startup toggle says enabled, but service not present in target domain.

### Pitfall 2: Non-Atomic State Persistence
**What goes wrong:** `StartAtLogin=true` persisted even when plist load failed.
**Why it happens:** profile mutation before validating command success.
**How to avoid:** mutate/save profile only after successful launchctl operation.
**Warning signs:** app restart shows enabled but no LaunchAgent active.

### Pitfall 3: Incomplete Preflight Coverage
**What goes wrong:** preflight checks only binary existence and misses mount/cache path validity or credentials.
**Why it happens:** ad-hoc validation split across UI commands.
**How to avoid:** centralized preflight service with required check list mapped directly to SAFE-02.
**Warning signs:** enable passes; first boot immediately fails with path or auth error.

### Pitfall 4: Breaking Manual Workflows While Toggling Startup
**What goes wrong:** disable startup accidentally removes or alters manual mount behavior.
**Why it happens:** shared state/flags between startup and runtime mount execution.
**How to avoid:** keep startup metadata isolated from mount command generation and runtime dictionaries.
**Warning signs:** after disabling startup, `StartMountCommand` or `StopMountCommand` behavior changes.

### Pitfall 5: Over-Reliance on File Existence as "Enabled" Truth
**What goes wrong:** plist exists but service is disabled/booted out, leading to false enabled status.
**Why it happens:** status checks only inspect filesystem.
**How to avoid:** combine plist presence with launchctl domain/service check and persisted flag reconciliation.
**Warning signs:** `IsEnabled` true while launchctl reports service disabled.

## Code Examples

Verified patterns from official sources:

### Validate LaunchAgent Plist Before Activation
```bash
# Source: `man plutil`
plutil -lint "$HOME/Library/LaunchAgents/com.rclonemountmanager.profile.<id>.plist"
```

### Use Modern launchctl Subcommands
```bash
# Source: local `man launchctl` (bootstrap/bootout, enable/disable)
launchctl bootstrap "gui/$UID" "$plist_path"
launchctl enable "gui/$UID/com.rclonemountmanager.profile.$profile_id"
launchctl bootout "gui/$UID/com.rclonemountmanager.profile.$profile_id"
```

### Explicit Process Exit Validation in .NET
```csharp
// Source: existing project pattern (`CliWrap` usage in service layer)
var result = await Cli.Wrap("launchctl")
    .WithArguments(args)
    .WithValidation(CommandResultValidation.None)
    .ExecuteBufferedAsync(cancellationToken);

if (result.ExitCode != 0)
{
    var stderr = string.IsNullOrWhiteSpace(result.StandardError) ? "launchctl failed" : result.StandardError.Trim();
    throw new InvalidOperationException(stderr);
}
```

### Deterministic Path Validation
```csharp
// Source: .NET docs for Path.GetFullPath
var absoluteMountPath = Path.GetFullPath(profile.MountPoint);
Directory.CreateDirectory(absoluteMountPath); // verify creatable/writable in preflight
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `launchctl load/unload -w` | `bootstrap/bootout` + `enable/disable` | Documented in current `launchctl` manpage as legacy alternatives | Clearer domain targeting, more maintainable startup orchestration |
| Treat plist `Disabled` key as source of truth | External enable/disable state managed by launchctl | Modern launchd behavior (documented in launchd.plist/launchctl manpages) | Must query launchctl state; file contents alone are insufficient |
| Broad startup attempts without preflight | Explicit preflight gate before enabling | Reliability-first startup UX pattern | Reduces boot-time failure rate and support burden |

**Deprecated/outdated:**
- `launchctl load`/`unload` as primary API: legacy subcommands, retain only for compatibility fallback.
- `OnDemand` plist key semantics: deprecated, replaced by `KeepAlive` behavior model.

## Open Questions

1. **Should Phase 1 adopt `SMAppService` now or defer?**
   - What we know: LaunchAgents are sufficient and already aligned with current architecture.
   - What's unclear: future notarization/signing roadmap and Login Items UX requirements.
   - Recommendation: defer; keep LaunchAgent path in Phase 1 and revisit in a dedicated packaging/integration phase.

2. **Where should cache-path preflight read from for all profile modes?**
   - What we know: current model has mount point and options; explicit cache directory may be implicit in options.
   - What's unclear: whether Phase 1 should add explicit cache path field or parse mount options only.
   - Recommendation: for Phase 1, validate explicit cache-related options if present; define dedicated cache-path field in later policy phase if needed.

3. **Credential availability definition for Quick Connect profiles**
   - What we know: passwords may be in profile or env vars depending on secure-script mode.
   - What's unclear: strict requirement for non-empty stored credentials vs runtime-provided env.
   - Recommendation: classify missing credentials as critical unless profile explicitly declares env-var-based credential source.

## Sources

### Primary (HIGH confidence)
- Local `man launchctl` (macOS): modern subcommands (`bootstrap`, `bootout`, `enable`, `disable`) and legacy notes (`load`/`unload`).
- Local `man launchd.plist` (macOS): key semantics (`Label`, `ProgramArguments`, `RunAtLoad`, `KeepAlive`, `Disabled`) and caveats.
- Local `man plutil` (macOS): `-lint` syntax validation behavior.
- Official rclone docs: `rclone mount`, `rclone nfsmount`, `rclone lsd`, `rclone listremotes`, `rclone config file`, `rclone obscure` (rclone.org command docs).
- Official .NET docs: `File.SetUnixFileMode`, `Environment.SpecialFolder`, `Path.GetFullPath` (learn.microsoft.com, net10.0).

### Secondary (MEDIUM confidence)
- Apple Terminal User Guide (support.apple.com): launchd usage and LaunchAgents/LaunchDaemons folder guidance.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - based on local macOS man pages + official rclone/.NET documentation.
- Architecture: HIGH - grounded in existing repository structure plus verified platform tooling behavior.
- Pitfalls: MEDIUM - derived from verified docs plus codebase-specific failure modes not yet runtime-tested in this phase.

**Research date:** 2026-02-21
**Valid until:** 2026-03-23 (30 days)
