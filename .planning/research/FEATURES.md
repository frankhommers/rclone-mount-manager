# Feature Landscape

**Domain:** macOS desktop rclone mount manager (reliable boot auto-mount + mount-management UX)
**Researched:** 2026-02-21

## Table Stakes

Features users expect. Missing = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Profile/bookmark management (create/edit/delete, per-remote options) | Every mature mount tool centers on saved connections | Med | Existing app already has this baseline; keep improving ergonomics and validation |
| Fast mount/unmount with clear live status | Users expect click-to-connect and immediate state feedback | Med | Mountain Duck exposes status lights and connect/disconnect in tray UI |
| Auto-start + restore previous mounts at login | Core expectation for a "set once" mount utility | Med | Mountain Duck explicitly pairs Login Item + Save Workspace for reconnect-after-login |
| Startup-safe preflight checks before mount | Reliability depends on prerequisites being validated first | Med | Check mount point existence/writability, rclone binary, FUSE/NFS path, cache path, credentials availability |
| Actionable failure notifications and retry controls | Network/cloud mounts fail; silent failure destroys trust | Med | Mountain Duck surfaces mount/unmount/error notifications and retry/disconnect actions |
| Local caching mode with sane defaults | Remote filesystems need compatibility/performance support | High | rclone mount docs: many apps require `--vfs-cache-mode writes|full`; without it writes/seek/retries are limited |
| Basic activity and logs for operations | Users need to know what is stuck, syncing, or failed | Med | Mountain Duck has activity and pending operations views; equivalent visibility is table-stakes |

## Differentiators

Features that set product apart. Not expected, but valued.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Auto-mount Readiness Score + one-click fixups | Turns "will this survive reboot?" into explicit pass/fail | Med | Show checks like launch agent installed, credentials resolvable, cache disk format, mount path permissions |
| Deterministic startup orchestration (queue, backoff, dependency gates) | Dramatically improves real boot reliability in noisy startup conditions | High | Per-mount startup order, jitter/backoff, bounded retries, and "stop on auth error" policy |
| Post-boot verification + self-heal loop | Detects false-positive starts and repairs automatically | High | Verify mount presence and basic I/O after login; auto-remount with reasoned retry budget |
| Guided failure diagnosis (human-readable root cause + next action) | Reduces support burden and user frustration | Med | Translate stderr/exit codes into categories: auth, path, provider, local env, transient network |
| Per-mount runtime policy presets | Lets users choose behavior by workload (latency vs compatibility vs disk usage) | Med | Presets can map to rclone VFS/cache flags and polling behavior, with advanced override |
| "Reliable boot" test harness in-app | Unique confidence feature before trusting production data | High | Simulate cold-start sequence and dependency checks without reboot; optional reboot checklist mode |

## Anti-Features

Features to explicitly NOT build. Common mistakes in this domain.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| "Magic" auto-mount with no visible startup state | Users cannot tell if boot mount is pending, failed, or degraded | Provide startup timeline, current phase, and explicit success criteria |
| Infinite silent retries | Hides true failure and burns resources | Use bounded retries with backoff + clear escalation to user action |
| One-size-fits-all cache defaults for all remotes/workloads | Causes either incompatibility or disk bloat | Offer policy presets and explain tradeoffs before applying |
| Editing launchd artifacts manually as primary UX | Error-prone and non-discoverable for most users | Keep launchd integration internal; expose validated settings through UI |
| Treating mount "connected" as success without I/O validation | False confidence; mounted-but-unusable states are common | Verify mount + minimal read/write checks before marking healthy |
| Exposing every rclone flag directly in primary flow | Overwhelms users and increases misconfiguration | Progressive disclosure: safe defaults first, advanced panel second |

## Feature Dependencies

```text
Profile/bookmark model
  -> Preflight checks
  -> Startup registration (login item / launch integration)
  -> Startup orchestration (order, retries, backoff)
  -> Post-boot verification
  -> Health/status UI + diagnostics

Caching policy layer
  -> Runtime compatibility (app file operations)
  -> Reliable writes/retries under transient failures

Observability (activity + logs + categorized errors)
  -> Actionable retry/self-heal
  -> Trust in "configure once, mount on boot"
```

## MVP Recommendation

For MVP (this milestone), prioritize:
1. **Unified "Configure + Auto-mount" flow** (single path from profile setup to startup enablement)
2. **Preflight + readiness checks** (before user leaves setup)
3. **Boot orchestration + post-boot verification** (bounded retries, explicit success/failure state)

One differentiator to include in MVP:
- **Guided failure diagnosis** (top 5 failure classes with one-click recovery actions)

Defer to post-MVP:
- **In-app reboot simulation harness**: high leverage but higher implementation complexity
- **Fine-grained per-mount policy engine**: start with presets, then expand
- **Collaboration/team policy features**: out of scope for single-user macOS-first milestone

## Sources

- rclone mount command docs (`https://rclone.org/commands/rclone_mount/`) - HIGH (official docs; includes platform caveats, VFS limits, daemon behavior)
- rclone remote control docs (`https://rclone.org/rc/`, `https://rclone.org/commands/rclone_rcd/`) - HIGH (official docs for mount management APIs)
- Mountain Duck docs: overview, interface, connect modes, preferences, known issues (`https://docs.cyberduck.io/mountainduck/`) - HIGH (official product docs)
- ExpanDrive site/docs (`https://www.expandrive.com/`, `https://docs.expandrive.com/`) - MEDIUM (official but partly marketing-oriented for feature depth)
- CloudMounter product docs/site (`https://cloudmounter.net/`) - MEDIUM (official but marketing-heavy)
- Apple launchd daemon/agent guide (`https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html`) - MEDIUM (official Apple source but archived/older; concepts still relevant, modern APIs should be revalidated during implementation)
