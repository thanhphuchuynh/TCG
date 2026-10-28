# Switch suspension diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the precise native suspension wait cycle with tested, bounded diagnostics before changing suspension semantics.

**Architecture:** Instrument our Wine/Horizon layer using a fixed in-memory event ring and dump it from the existing stall monitor. Preserve build7 baseline and unmodified FEX. A demonstrated race gets a deterministic regression and a separately recorded minimal faithful fix.

**Tech Stack:** Existing native C, Python extraction/host regression tools, upstream Switch SDK CI and matched Wine DLLs when needed.

**Spec:** `docs/superpowers/specs/2026-10-28-switch-on-demand-design.md`.

## Global Constraints

- Own changes in publication patches/tools; work source in emulator-src/autorun only. No FEX edits, Unity changes, installed SD edits or Git pushes by delegated agent.
- Preserve real suspension, context synchronization and GC; never fake status or skip memory faults.
- Existing read-only analysis: `/Users/tphuc/Downloads/TCG/switch-on-demand/runtime-investigation/2026-10-28-readonly-suspension-analysis.md`.
- Logs currently prove main tid0004 parked with count1, two contexts and doorbell1; loading worker132 waits on futex. Actual owner/wait address and cycle remain unproven.
- No private contents or key values in diagnostic ring; addresses and numeric thread/operation metadata only. No synchronous file logging or heap allocation under object/CPU locks.

## Review Focus

- A filled/overwritten ring remains coherent under concurrent writers and wraparound.
- Diagnostics never introduce a new object-lock or CPU-lock ordering dependency.
- Snapshot reads use published valid records; bounded buffers never read arbitrary PE stack memory.
- Caller/target thread IDs and syscall phase survive nested calls and identify park begin/end.
- Existing CHPE tests do not substitute for WoW64 synchronization/notification-window coverage.

## Task 1: Tested native diagnostic ring

**Files:** own ring/host tests under runtime-investigation, final patch in publication/patches; actual paths chosen against current source.

**Interfaces:** fixed-capacity record contains monotonic sequence/time, event enum, caller/target IDs, handle, old/new suspend count/status, syscall ID/phase, park state and wait address/value. Native recorder allocates no memory; safe dump callback runs outside tracked locks.

- [ ] Write and run failing host regressions for wraparound, concurrent writers, coherent publication and bounded dump; assert numeric record contents rather than mocked log calls.
- [ ] Implement ring, run full relevant host tests, fix any broken existing helper extraction rather than claim a clean baseline without running it.
- [ ] Instrument NtSuspendThread/backend entry/exit, server suspend/resume, native context request/result, dispatcher phase, park enter/leave, futex wait/alert. Keep original semantics unchanged.
- [ ] Compile native source and run relevant production-function extraction tests. Deliver patch and test evidence to main agent, without commit/push/installation.

## Task 2: Fresh trace and minimal faithful fix

**Interfaces:** main agent owns CI dispatch/runtime installation/emulator; delegated agent reads new logs and proposes tested source corrections only after the wait cycle is established.

- [ ] Main agent builds the diagnostic runtime and preserves matched baseline; reproduce in isolated data without concurrent emulator owners.
- [ ] Identify suspension caller, target, native/PE phase, wait address and owner needed to release it. Distinguish actual cycle from plausible ThreadCreationMutex races.
- [ ] Write a deterministic failing regression using production paths/barriers for the demonstrated cycle. Native get-context must not falsely acknowledge a runnable target.
- [ ] Record the exact proposed correction, then implement the smallest faithful Wine/Horizon fix. Test translated, native notification/syscall and futex-blocked windows, nested counts, self-suspend, failures/rollback and termination as implicated by the correction.
- [ ] Main agent rebuilds and repeatedly tests actual Mono activity and game loads, restores 4 GB after diagnostics, and integrates only proven source-only changes. If a faithful correction requires prohibited or unavailable components, report the specific boundary; do not invent a successful result.
