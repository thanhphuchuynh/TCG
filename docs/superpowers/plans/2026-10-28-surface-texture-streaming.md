# Shop finish texture streaming Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; independent
> runtime ownership, conservative reference audit and artifact proof may use
> superpowers:dispatching-parallel-agents with disjoint files/fixture projects.

**Goal:** Remove unused finish textures from eager load, preserving visuals/rules.
**Architecture:** Original Texture2D bundles; exact semantic catalogue; synchronous
material-slot leases. Existing ArtCache/ImageSize; private clone only.
**Tech Stack:** Unity2021.3.45f2,C#,Python,UnityPy.
**Spec:** ../specs/2026-10-28-surface-texture-streaming.md

## Constraints
- Only private switch-on-demand/macos-port-work game edits; source-only publication.
- Ultra5/imports/pixels/material properties/save IDs/RNG unchanged; no FEX/GC bypass.
- Unknown texture refs/default material refs stay resident. No Resources wholesale moves.
- Preserve previous artifacts/journals; no game release until original acceptance passes.

## Review Focus
- Shared material/slot owners and stale teardown never clear another live binding.
- Non-null runtime overrides and original null fallbacks keep original behavior.
- Owner disable does not blank the separate visible world renderer.
- Original clamped getters map to actual catalogue index without changing exceptions.
- Retry/drift/interrupted writes fail closed before clobbering authored content.

## Task1 Runtime material slots
Files Runtime/SurfaceManifest.cs,SurfaceTextureService.cs,SurfaceTextures.cs,
Tests/Unity/SurfaceRuntimeTests.cs,SurfaceRuntimeProbe.cs.
Interface SurfaceEntry{key,bundle,asset,bytes,crc}; SurfaceManifest v1 Validate().
SurfaceTextures.SetTexture(Material,GameObject,string,string,Texture) static.
SurfaceTextureService.Instance; Initialize(root,manifest,budget); Cache/LastError.
- [x] RED actual Unity fixtures for every ReviewFocus ownership/fallback case,
  native sharedTexture/load/unload/reload, failures and manifest checks.
- [x] Implement sync backend/64MiB/cache bundle dedup/owner lifecycle, canonical
  manifest, CRC/payload checks. GREEN full own Unity tests and host cache suite.

## Task2 Exact editor audit/build and guarded source hooks
Files Editor/SurfaceRecord.cs,SurfaceReferenceAudit.cs,SurfaceBuild.cs;
prepare_surfaces.py; Tests/Unity/SurfaceAuditTests.cs,SurfaceBuildTests.cs;
Tests/test_prepare_surfaces.py; modify Editor/StreamingBuild.cs orchestration.
SurfaceRecord{key,property,textureGuid,texturePath,textureName,bytes,texture[NonSerialized]}.
SurfaceReferenceAudit.Scan(cataloguePath,IList<SurfaceRecord>,IEnumerable<string>paths=null)
returns EligibleRecords,RetainedTextures(guid->reason). Only exact authorised
catalogue property refs allowed; default materials/aliases/unknown refs retained.
- [x] RED audit allowed catalogue vs unknown material/scene/Sprite/binary/Resources.
- [x] Build/strip only eligible fields, oneTextureLZ4/address/CRC manifest plus tiny
  canonical text; source catalogue identity/hash backup journal and safe retries.
- [x] RED executable source fixture all24calls, actualIndex after original getter,
  override/fallback and existing color/showBar flow; hash-guard/idempotent backup.
- [x] Integrate Restore before other preparation and Prepare before player build.
  GREEN actual Unity audit/builder/restore and Python/source tests.

## Task3 Artifact proof and gameplay
Files validate_surfaces.py,Tests/test_validate_surfaces.py; own diagnostics only.
Report surface-build.json prepared,eligibleRecords rows,retained list, counts/bytes.
- [x] RED fixture tamper/CRC/address/extra texture/manifest/resident/retained data.
- [x] Strict baseline vs new player textures incl pixels/all fields, geometry,
  unrelated catalogue/material/quality/Resources/FAT32. Run existing381art+252mesh
  checks as well. Fresh private build, no newquality or sourcebehavior fallbacks.
- [x] Fresh whole-change review; both Important fixes RED/GREEN, whole suite89/89.
- [ ] Own-source checkpoint/publish; gameplay acceptance remains open.
- [ ] Install verified build and repeat unlocked4GB shop/pack/album/save-load/memory.
