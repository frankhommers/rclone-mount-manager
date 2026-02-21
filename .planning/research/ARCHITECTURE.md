# Architecture Patterns

**Domain:** Reliable desktop mount orchestration (rclone mount manager)
**Researched:** 2026-02-21

## Recommended Architecture

For reliable desktop mounts, the most stable structure is a **supervisor + worker** model:

1. **OS supervisor** starts and restarts workers (`launchd` on macOS, `systemd` on Linux, SCM/Task Scheduler patterns on Windows).
2. **Mount worker** is a per-profile process (rclone mount) with explicit startup arguments.
3. **App control plane** (your GUI + Core services) owns desired state, preflight validation, health interpretation, and recovery policy.

For this project's existing split (GUI + Core services + command adapters), keep GUI as control plane UI and add a reliability-focused orchestration layer in Core.

```text
GUI (intent + diagnostics)
  -> Core Orchestrator (state machine + policy)
      -> Startup Adapter (launchd)
      -> Runtime Adapter (rclone + rc)
      -> Health Adapter (mount table + rc + probe)
      -> Recovery Engine (retry/backoff/circuit-breaker)
```

### Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| GUI | Configure profiles, show health/failures, request actions | Core Orchestrator API |
| Profile Store | Persist desired state, last-known-good settings, failure counters | Core Orchestrator |
| Startup Orchestrator | Register/unregister startup jobs, reconcile desired vs actual startup state | launchd adapter, Profile Store |
| Mount Runtime Manager | Build rclone args, start/stop, track process identity | command adapter, Health Supervisor |
| Health Supervisor | Multi-signal health checks (process + mount + rc + probe) | Runtime Manager, OS adapter |
| Recovery Engine | Apply retry policy, backoff, disable-on-loop, surface remediation | Health Supervisor, Startup Orchestrator |
| Event/Telemetry Log | Structured startup and failure timeline for user diagnosis | All Core services, GUI |

### Data Flow

```text
Boot/login -> launchd starts profile job
-> wrapper invokes rclone mount (+ rc)
-> Health Supervisor checks readiness:
   (A) process alive
   (B) mountpoint present in OS mount table
   (C) rc endpoint responsive / mount listed
   (D) lightweight filesystem probe succeeds
-> if healthy: mark profile Healthy
-> if not healthy by timeout: Recovery Engine classifies + retries/backoff
-> if failure budget exceeded: disable startup for that profile + raise actionable error
```

## Patterns to Follow

### Pattern 1: Per-profile state machine (authoritative desired/actual split)
**What:** Model each profile with explicit states (`Disabled`, `Registered`, `Starting`, `Healthy`, `Degraded`, `Recovering`, `Failed`, `Quarantined`).
**When:** Always, especially for boot-time orchestration where async races are common.
**Example:**
```typescript
type MountState =
  | "Disabled"
  | "Registered"
  | "Starting"
  | "Healthy"
  | "Degraded"
  | "Recovering"
  | "Failed"
  | "Quarantined";
```

### Pattern 2: Supervisor-native startup, app-native policy
**What:** Let `launchd` do process supervision; keep retry limits, classification, and user-facing policy in app Core.
**When:** macOS-first desktop reliability work.
**Why:** `launchd` is strong at lifecycle; app is better for domain semantics (mountpoint busy vs auth vs network).

### Pattern 3: Layered health checks (not just PID)
**What:** Declare health only when process, mount visibility, and I/O probe all pass.
**When:** After startup and periodically during runtime.
**Example:**
```typescript
healthy = processAlive && mountListed && rcReachable && probeReadDirOk;
```

### Pattern 4: Failure classification before retry
**What:** Classify failures into deterministic buckets (`ConfigError`, `AuthError`, `DependencyMissing`, `TransientNetwork`, `MountBusy`, `Unknown`).
**When:** Every failed startup or degraded health event.
**Why:** Prevents infinite retries for non-recoverable failures.

### Pattern 5: Bounded retries + quarantine
**What:** Exponential backoff with jitter and a failure budget; exceed budget -> quarantine profile and require explicit user action.
**When:** Boot loops, unstable remotes, repeated mount busy errors.

### Pattern 6: Idempotent reconcile loop
**What:** A periodic reconciler compares desired state vs actual system state and repairs drift.
**When:** App launch, wake from sleep, and on explicit "Repair startup" action.

## Anti-Patterns to Avoid

### Anti-Pattern 1: Treating "process started" as success
**What:** Marking startup complete immediately after command spawn.
**Why bad:** Mount can fail after spawn; user sees false green state.
**Instead:** Require readiness gates (mount present + probe) before Healthy.

### Anti-Pattern 2: Legacy `launchctl load/unload` workflow as primary control
**What:** Relying on deprecated/legacy command surface as core orchestration.
**Why bad:** Harder diagnostics and weaker modern domain targeting.
**Instead:** Use modern domain-oriented `bootstrap/bootout/kickstart/print` flows in adapter, keep compatibility fallback only if needed.

### Anti-Pattern 3: Single global cache/runtime paths for all profiles
**What:** Multiple mounts sharing the same cache/runtime dirs blindly.
**Why bad:** rclone docs warn overlapping VFS cache usage can corrupt data.
**Instead:** Generate per-profile cache/runtime directories.

### Anti-Pattern 4: Unbounded restart loops
**What:** "Always restart" with no budget or user-visible escalation.
**Why bad:** Battery/CPU drain and noisy logs, no actionable remediation.
**Instead:** Backoff + quarantine + clear recovery instructions.

### Anti-Pattern 5: Hidden startup environment assumptions
**What:** Assuming shell env (`PATH`, `HOME`, secrets env vars) exists at startup.
**Why bad:** Startup contexts differ from interactive shells.
**Instead:** Use absolute paths, explicit env, and startup preflight checks.

## Scalability Considerations

| Concern | At 10 profiles | At 100 profiles | At 1000 profiles |
|---------|----------------|-----------------|------------------|
| Startup storm | Simple staggered starts (2-3 at a time) | Priority tiers + token bucket concurrency | Queue-based orchestration service with strict admission control |
| Health polling overhead | 15-30s poll loop per profile OK | Shared scheduler + adaptive polling | Event-driven + sparse probes, aggregate health snapshots |
| Failure blast radius | Per-profile isolation | Group-level circuit breakers by backend/provider | Multi-level isolation (profile/group/global) with hard caps |
| Logs/diagnostics | In-memory + file log | Structured event store with retention | Indexed event pipeline and sampled telemetry |

## Integration Notes for Current Codebase

- Current `LaunchAgentService` generates plist with `RunAtLoad=true` and `KeepAlive=false`; this is startup but not resilient restart behavior.
- Current mount runtime tracks in-memory process state; boot reliability needs persisted state and post-boot reconciliation (processes can exist before GUI opens).
- Recommended next increment:
  1. Add `StartupOrchestratorService` (desired/actual reconcile).
  2. Add `MountHealthSupervisor` (layered checks).
  3. Add `RecoveryPolicyEngine` (retry budget + quarantine).
  4. Upgrade launchd adapter to modern `launchctl` command surface.

## Sources

- HIGH: rclone mount docs (daemon behavior, VFS cache reliability, systemd notes): https://rclone.org/commands/rclone_mount/
- HIGH: rclone RC API docs (`mount/listmounts`, `mount/unmount`, `core/stats`, jobs): https://rclone.org/rc/
- HIGH: systemd service semantics (`Type`, `Restart`, `WatchdogSec`, timeouts): https://man7.org/linux/man-pages/man5/systemd.service.5.html
- MEDIUM: launchd/plist semantics (KeepAlive, RunAtLoad, throttling, file locations): https://manp.gs/mac/5/launchd.plist and https://manp.gs/mac/1/launchctl
- MEDIUM: Apple launchd guide (historical but still useful conceptual model): https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html
- MEDIUM: Windows Task Scheduler/service ecosystem references: https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-start-page and https://learn.microsoft.com/en-us/windows/win32/services/services
