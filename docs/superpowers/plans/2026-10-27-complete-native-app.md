# Complete Native Mac App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Package the existing native game and all production mods as a local double-click Mac app.
**Architecture:** An arm64 AppKit launcher stages its immutable bundled runtime into Application Support once, then invokes the unchanged BepInEx startup script without Terminal. A per-user lock prevents concurrent preparation and duplicate launches; readiness receipts identify completed installations. Existing runtime configuration remains untouched on subsequent launches.
**Tech Stack:** Swift Foundation/AppKit, Python packaging, macOS codesign/ditto.
**Spec:** docs/superpowers/specs/2026-10-27-complete-native-app-design.md

## Global Constraints
- Apple Silicon and macOS 26+; no Windows compatibility runtime.
- Include every production mod; exclude logs/caches/probes, Switch experiments and user saves.
- Keep existing game, mods, Steam initialization and save behavior unchanged.
- Preserve the original build; produce local output only, no full-game public upload.

## Review Focus
- Interrupted preparation must not leave a ready partial runtime.
- A second launch must preserve mod configuration and custom objects.
- A moved app must reuse its matching prepared runtime.
- Concurrent launches must not mutate the same runtime.
- Missing/corrupt runtime files must produce a useful error without replacing saves/settings.

### Task 1: Runtime preparation and launch core
**Files:** complete-app/RuntimeCore.swift, complete-app/RuntimeTests.swift.
**Interfaces:** RuntimeManager(payload: URL, storage: URL); prepare() throws -> URL; acquireLock() throws; launch(arguments:[String]) throws -> Process. Payload runtime.json supplies safe runtime ID, game app name and size.
- [x] Write tests for atomic first preparation, repeated-launch settings preservation, missing payload, preparation failure cleanup, runtime integrity, duplicate locks and shell arguments with spaces.
- [x] Run test entry point with placeholder core and confirm expected missing-feature failure.
- [x] Implement staging copy, required-file validation, versioned readiness receipt, advisory lock and launch specification. Runtime folder lives under the selected Application Support root, never inside the app.
- [x] Run all core tests successfully and commit only task sources.

### Task 2: Native UI, package and live verification
**Files:** complete-app/App.swift, complete-app/main.swift, complete-app/build.py, complete-app/README.md.
- [x] Implement native first-run preparation progress and actionable errors. Hide launcher once game starts; retain its runtime lock until child exits. A completed nonzero launch exit offers the log path.
- [x] Build payload from supplied native-build, taking only game app, BepInEx core/patchers/plugins/config, Doorstop, startup script and Steam app ID. Record inventory, exclude private caches/logs and diagnostic probes; copy original icon.
- [x] Compile arm64 launcher, ad-hoc sign nested code and app, verify signature and payload inventory, and commit own source only.
- [x] Prepare full runtime using test destination, verify repeated preparation preserves settings, launch normally to main menu and confirm production mod loading. Re-verify outer signature, save sentinels and original build after test.
- [x] Make a local ZIP if free space permits; provide app and instructions. Request final independent code review and fix important findings before delivery.
