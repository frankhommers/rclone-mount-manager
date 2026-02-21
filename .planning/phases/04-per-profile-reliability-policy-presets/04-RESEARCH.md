# Phase 4: Per-Profile Reliability Policy Presets - Research

**Researched:** 2026-02-21
**Domain:** Per-profile reliability policy presets for rclone mount behavior in Avalonia/.NET MVVM
**Confidence:** HIGH

## Summary

Phase 4 should implement reliability tuning as a typed preset system attached to each `MountProfile`, not as free-form flag editing. The current app already has the right primitives: profile-scoped state (`Profiles`, `SelectedProfile`), typed mount option storage (`MountOptions` dictionary + pinned options), deterministic command generation in `MountManagerService`, and JSON persistence via `PersistedProfile` in `MainWindowViewModel`.

The standard approach for this stack is: model presets as immutable definitions with stable IDs, expose preset selection in UI via `ComboBox` bound to ViewModel state, apply a preset by writing a controlled subset of mount options, and persist selected preset ID per profile. This satisfies POL-01 and POL-02 while preserving existing typed runtime state and testable ViewModel behavior.

Rclone reliability behavior should be preset-driven around documented mount/VFS and retry controls (for example `--vfs-cache-mode`, `--dir-cache-time`, `--attr-timeout`, `--retries`, `--low-level-retries`). Avoid sending users back to raw `ExtraOptions` for this phase.

**Primary recommendation:** Add a typed `ReliabilityPolicyPreset` catalog + `SelectedReliabilityPresetId` per profile, apply presets by updating managed mount/global option fields in a deterministic ViewModel path, and persist the selected preset ID in `profiles.json`.

## Standard Stack

The established libraries/tools for this domain:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Avalonia | 11.3.12 | Preset picker UI (`ComboBox`) and compiled bindings | Existing UI framework; official control/binding guidance is current |
| CommunityToolkit.Mvvm | 8.4.0 | Typed observable state + command generation (`[ObservableProperty]`, `[RelayCommand]`) | Existing project pattern for deterministic ViewModels |
| System.Text.Json | net10.0 BCL | Persist selected preset per profile in `profiles.json` | Existing serializer in `MainWindowViewModel` |
| rclone CLI options model | docs show v1.73.1 flag set | Reliability controls (`--vfs-cache-mode`, `--dir-cache-time`, `--attr-timeout`, retries) | Official source of truth for mount reliability knobs |
| Existing `MountOptions` dictionary pipeline | repo current | Converts typed option entries into final CLI arguments | Already integrated with mount start/script generation |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `RcloneOptionsService` (`rc --loopback options/info`) | repo current | Option metadata discovery and grouping | Use when validating that preset-managed flags are valid/current |
| xUnit | 2.9.3 | Deterministic regression tests for apply/persist/reload | Use for POL-01/POL-02 acceptance tests |
| `StringComparer.OrdinalIgnoreCase` dictionaries | net10.0 BCL | Stable option-key matching (`vfs_cache_mode`, etc.) | Use for preset patch application to options |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Typed preset IDs persisted per profile | Recompute from raw flags on load | Fragile reverse inference and migration pain |
| Controlled option patching | Full overwrite of `MountOptions` | Risky: clobbers unrelated user settings |
| ViewModel-based apply command | Apply in view code-behind | Breaks current deterministic/testable VM architecture |

**Installation:**
```bash
# No new dependency required for baseline Phase 4.
# Use existing Avalonia + CommunityToolkit.Mvvm + System.Text.Json stack.
```

## Architecture Patterns

### Recommended Project Structure
```
RcloneMountManager.Core/
└── Models/
    └── ReliabilityPolicyPreset.cs          # preset definition + stable ID + managed option patch

RcloneMountManager.GUI/
├── ViewModels/
│   └── MainWindowViewModel.cs             # selection state, apply command, persistence wiring
└── Views/
    └── MainWindow.axaml                   # preset picker + apply action

RcloneMountManager.Tests/
└── ViewModels/
    └── MainWindowViewModelPolicyPresetTests.cs  # apply/persist/reload/don't-clobber tests
```

### Pattern 1: Immutable Preset Catalog with Stable IDs
**What:** Define presets once (ID, label, description, managed option values), and bind by ID.
**When to use:** Always; this is the source of truth for available reliability policies.
**Example:**
```csharp
// Source: repo MVVM patterns + System.Text.Json enum/string guidance
public sealed record ReliabilityPolicyPreset(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyDictionary<string, string> MountOptionOverrides,
    IReadOnlyDictionary<string, string> GlobalOptionOverrides);
```

### Pattern 2: Controlled Option-Patch Application
**What:** Apply only a managed set of reliability keys, leaving unrelated options untouched.
**When to use:** Every time user applies a preset.
**Example:**
```csharp
// Source: existing MountOptions dictionary command pipeline in MountManagerService
foreach (var key in ManagedReliabilityKeys)
{
    options.Remove(key);
}

foreach (var (key, value) in preset.MountOptionOverrides)
{
    options[key] = value;
}
```

### Pattern 3: ViewModel-First UI Wiring
**What:** Bind `ComboBox.SelectedItem`/`SelectedValue` to ViewModel selected preset ID and apply via `[RelayCommand]`.
**When to use:** Preset selection and apply UX in `MainWindow.axaml`.
**Example:**
```xml
<!-- Source: https://docs.avaloniaui.net/docs/reference/controls/combobox -->
<ComboBox ItemsSource="{Binding ReliabilityPresets}"
          SelectedValue="{Binding SelectedReliabilityPresetId}"
          SelectedValueBinding="{Binding Id}" />
```

### Pattern 4: Persist Selection Explicitly Per Profile
**What:** Add selected preset ID to `MountProfile` and `PersistedProfile` load/save mapping.
**When to use:** Save/Load profile lifecycle.
**Example:**
```csharp
// Source: existing PersistedProfile mapping in MainWindowViewModel
public string ReliabilityPresetId { get; set; } = "balanced";
```

### Anti-Patterns to Avoid
- **Reverse-infer preset from raw flags:** ambiguous and brittle once users tweak options.
- **Global preset setting:** violates per-profile requirement POL-01.
- **Applying presets by writing `ExtraOptions` text:** bypasses typed option model and creates parsing edge cases.
- **Silent full-option replacement:** can erase non-policy user customizations.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Option metadata catalog | Hardcoded giant list of rclone flags | Existing `RcloneOptionsService` + `options/info` metadata | Keeps UI/options aligned with installed rclone capabilities |
| Command argument rendering | New flag-string builder for presets | Existing `MountOptions` -> CLI conversion in `MountManagerService` | Already handles booleans and key conversion `_` -> `-` |
| Property/command boilerplate | Manual `INotifyPropertyChanged` and command wiring | `[ObservableProperty]` and `[RelayCommand]` generators | Existing project convention; less error-prone |
| Profile persistence format migrations by ad-hoc string edits | Manual JSON text munging | `System.Text.Json` typed model mapping | Safer evolution and testability |

**Key insight:** Phase 4 is a policy-modeling and state-transition problem, not a command-line parsing problem. Reuse existing typed option and persistence pipelines.

## Common Pitfalls

### Pitfall 1: Preset Apply Clobbers Unrelated Options
**What goes wrong:** Applying a reliability preset unexpectedly removes other mount settings.
**Why it happens:** Full dictionary replacement instead of targeted managed-key patching.
**How to avoid:** Define `ManagedReliabilityKeys`; remove/rewrite only those.
**Warning signs:** user-set non-policy option disappears after preset apply.

### Pitfall 2: Preset Selection Does Not Survive Reload
**What goes wrong:** UI reopens with default/blank preset despite previous selection.
**Why it happens:** preset ID not persisted in `PersistedProfile` load/save mapping.
**How to avoid:** add explicit persisted field and migration-safe default.
**Warning signs:** `profiles.json` lacks policy field after save.

### Pitfall 3: Policy Works for Rclone but Breaks NFS Profiles
**What goes wrong:** NFS profile receives rclone-oriented flags or invalid UX path.
**Why it happens:** no mount-type guard around policy applicability.
**How to avoid:** scope reliability presets to rclone profile types, or define explicit NFS-safe behavior.
**Warning signs:** NFS start path hits rclone flag validation errors.

### Pitfall 4: Duplicate Source of Truth
**What goes wrong:** selected preset says one thing, effective options reflect another.
**Why it happens:** preset ID and options mutate in different code paths.
**How to avoid:** one apply path that sets both selected ID and managed options atomically.
**Warning signs:** tests pass for apply, fail after profile switch or save/reload.

### Pitfall 5: Reliability Presets Depend on Raw `ExtraOptions`
**What goes wrong:** POL-02 not met because users must still edit raw flags for expected reliability behavior.
**Why it happens:** preset design omits key reliability controls and falls back to free-form text.
**How to avoid:** include critical reliability knobs directly in preset definitions.
**Warning signs:** docs/tooltips instruct users to type flags manually.

## Code Examples

Verified patterns from official sources:

### MVVM Observable Property (selection state)
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty
[ObservableProperty]
private string _selectedReliabilityPresetId = "balanced";
```

### MVVM Relay Command (explicit apply action)
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand
[RelayCommand]
private void ApplyReliabilityPreset()
{
    // deterministic patch to SelectedProfile.MountOptions
}
```

### Enum/String JSON customization for stable persisted values
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties#enums-as-strings
var options = new JsonSerializerOptions();
options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
```

### Rclone reliability controls to include in preset scope
```text
// Source: https://rclone.org/commands/rclone_mount/
--vfs-cache-mode, --dir-cache-time, --attr-timeout

// Source: https://rclone.org/flags/
--retries, --low-level-retries, --retries-sleep
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual raw flag editing (`ExtraOptions`) for reliability | Preset-based policy selection with controlled option patching | Phase 4 target (2026) | Safer UX and fewer invalid combinations |
| Implicit reliability state inferred from options | Explicit persisted preset ID per profile | Phase 4 target (2026) | Deterministic load/revisit behavior |
| Reflection-heavy bindings without compile-time checks | Compiled bindings with `x:DataType` and optional `x:CompileBindings` | Avalonia 11.x (docs updated 2025-11-14) | Better binding safety/performance |

**Deprecated/outdated:**
- Treating reliability policy as ad-hoc command text.
- Requiring users to understand low-level rclone retry/cache flags for normal tuning.

## Open Questions

1. **Preset taxonomy and defaults**
   - What we know: phase requires per-profile selection and apply without raw flags.
   - What's unclear: final preset set (for example conservative/balanced/aggressive) and exact values.
   - Recommendation: lock 3 presets with explicit managed-key tables in planning, plus one default (`balanced`).

2. **Global retry controls storage location**
   - What we know: retries are global rclone flags (`--retries`, `--low-level-retries`, `--retries-sleep`) while mount options are already profile-scoped.
   - What's unclear: whether to store these in profile model separately or encode only mount/VFS options in Phase 4.
   - Recommendation: define profile-scoped fields for any non-mount flags included in policy to keep per-profile semantics explicit.

3. **Interaction with advanced mount options editor**
   - What we know: app already exposes editable mount options and pinned flags.
   - What's unclear: whether manual edits to managed reliability keys should detach from preset (custom state) or be overwritten on next apply.
   - Recommendation: make apply explicit and idempotent; if managed keys diverge, display preset as `custom` until reapplied.

## Sources

### Primary (HIGH confidence)
- Repository source inspection:
  - `RcloneMountManager.GUI/ViewModels/MainWindowViewModel.cs`
  - `RcloneMountManager.GUI/Views/MainWindow.axaml`
  - `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`
  - `RcloneMountManager.Core/Models/MountProfile.cs`
  - `RcloneMountManager.Core/Services/MountManagerService.cs`
  - `RcloneMountManager.Core/Services/RcloneOptionsService.cs`
- https://docs.avaloniaui.net/docs/reference/controls/combobox - ComboBox binding semantics (updated 2026-01-07)
- https://docs.avaloniaui.net/docs/basics/data/data-binding/compiled-bindings - compiled binding guidance (updated 2025-11-14)
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/observableproperty - observable property generation
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand - relay command generation
- https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/customize-properties#enums-as-strings - stable enum/string serialization options
- https://rclone.org/commands/rclone_mount/ - mount/VFS reliability behavior and defaults
- https://rclone.org/flags/ - retry/global flag definitions and defaults

### Secondary (MEDIUM confidence)
- https://rclone.org/rc/#options-info - option-block schema and `options/info` metadata structure (page banner version text is older; endpoint schema still matches usage in current repo)

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - directly from repo dependencies and official Avalonia/.NET/rclone docs.
- Architecture: HIGH - grounded in existing ViewModel, mount option pipeline, and persistence structure.
- Pitfalls: HIGH - derived from current code paths plus requirement constraints POL-01/POL-02.

**Research date:** 2026-02-21
**Valid until:** 2026-03-23 (30 days)
