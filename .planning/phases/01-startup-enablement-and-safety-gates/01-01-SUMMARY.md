---
phase: 01-startup-enablement-and-safety-gates
plan: 01
subsystem: infra
tags: [csharp, dotnet, startup, preflight, safety, rclone]
requires: []
provides:
  - Typed startup preflight result models with explicit severity classification
  - Deterministic startup preflight pipeline for binary, mount path, cache path, and credentials
  - Unit coverage for pass/failure modes that enforce SAFE-01/SAFE-02 behavior
affects: [01-02-launch-agent-activation, 01-03-viewmodel-startup-gating]
tech-stack:
  added: []
  patterns:
    - Typed preflight check results with machine-readable keys and explicit user-facing messages
    - Critical-gate boolean derived from per-check severity classification
key-files:
  created:
    - RcloneMountManager.Core/Models/StartupCheckResult.cs
    - RcloneMountManager.Core/Models/StartupPreflightReport.cs
    - RcloneMountManager.Core/Services/StartupPreflightService.cs
    - RcloneMountManager.Tests/Services/StartupPreflightServiceTests.cs
  modified:
    - RcloneMountManager.Core/Services/StartupPreflightService.cs
key-decisions:
  - "Model startup checks as typed records keyed by stable check IDs instead of parsing status strings."
  - "Treat missing explicit cache path as a warning, but classify invalid configured cache paths as critical failures."
  - "Resolve invalid filesystem paths into critical report entries rather than throwing from preflight execution."
patterns-established:
  - "Preflight services return complete typed reports even when checks fail."
  - "Regression tests assert explicit failure classes/messages for each required safety category."
duration: 3 min
completed: 2026-02-21
---

# Phase 1 Plan 01: Startup Preflight Domain Summary

**Typed startup preflight checks now classify binary, mount path, cache path, and credential safety deterministically before startup enablement.**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-21T19:10:04Z
- **Completed:** 2026-02-21T19:13:45Z
- **Tasks:** 3/3
- **Files modified:** 4

## Accomplishments

- Added `StartupCheckSeverity`, `StartupCheckResult`, and `StartupPreflightReport` models with stable check keys and helper APIs for deterministic gating.
- Implemented `StartupPreflightService.RunAsync` with ordered binary, mount-path, cache-path, and credential checks plus critical-vs-warning classification.
- Added focused `StartupPreflightServiceTests` coverage for expected pass mode and all required SAFE-02 failure categories.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add typed startup preflight result models** - `24850ce` (feat)
2. **Task 2: Implement startup preflight pipeline service** - `35400b2` (feat)
3. **Task 3: Add preflight unit coverage for pass and failure modes** - `01bab26` (test)

Additional deviation fix commit:

- `03153ab` (fix): classify invalid path inputs as critical report failures instead of throwing.

## Files Created/Modified

- `RcloneMountManager.Core/Models/StartupCheckResult.cs` - Typed per-check outcome model and severity enum.
- `RcloneMountManager.Core/Models/StartupPreflightReport.cs` - Aggregate preflight report with gate and summary helpers.
- `RcloneMountManager.Core/Services/StartupPreflightService.cs` - Ordered preflight pipeline and check implementations.
- `RcloneMountManager.Tests/Services/StartupPreflightServiceTests.cs` - Deterministic coverage for startup preflight pass/failure behavior.

## Decisions Made

- Used machine-readable check keys (`binary`, `mount-path`, `cache-path`, `credentials`) for stable ViewModel gating and assertions.
- Kept cache-path absence as a warning to avoid false startup blocking unless a cache path is explicitly configured.
- Ensured invalid path inputs become typed critical failures rather than exceptions so the UI can always show actionable failure reasons.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Prevented invalid mount/cache paths from throwing in preflight execution**

- **Found during:** Task 3 (Add preflight unit coverage for pass and failure modes)
- **Issue:** Path resolution exceptions (`Path.GetFullPath`) escaped `RunAsync`, preventing deterministic report generation.
- **Fix:** Added guarded path resolution and converted invalid path inputs into explicit critical check results.
- **Files modified:** `RcloneMountManager.Core/Services/StartupPreflightService.cs`
- **Verification:** `dotnet test RcloneMountManager.slnx --filter "FullyQualifiedName~StartupPreflightServiceTests"` (5 passed)
- **Commit:** `03153ab`

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Auto-fix was required to satisfy deterministic preflight behavior and explicit failure classification.

## Issues Encountered

- The plan-specified test filter `ClassName=StartupPreflightServiceTests` returned no matches under the current xUnit discovery shape; verification used `FullyQualifiedName~StartupPreflightServiceTests` to execute the targeted suite.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for `01-02-PLAN.md` to consume typed preflight outcomes when launch-agent activation is modernized.
- No blockers carried forward from this plan.

---
*Phase: 01-startup-enablement-and-safety-gates*
*Completed: 2026-02-21*
