# Godot ALS Port Design

**Status:** Approved design

**Date:** 2026-08-25

**Godot:** 4.7.2 .NET

**Reference UE project:** `../AdvancedLocomotionSystemV`

**Reference implementation:** `Sixze/ALS-Refactored`, tag `4.17`, commit `cb03d53526fed8eaf62637621d4593a57753b317`

## 1. Objective

Build a standalone, Git-managed Godot 4.7.2 .NET sample project that recreates the milestone-B behavior of Advanced Locomotion System V4 using skeletal meshes and animation assets exported from the existing Unreal Engine project.

The completed sample must support at least ten full-quality ALS characters at 60 Hz physics and a stable 60 FPS on an Intel i7-10700. The first ten characters retain full animation evaluation, Aim Offset, Foot IK, Foot Lock, pelvis correction, root-motion actions, ragdoll, pose recovery, and event processing.

This repository owns the Godot runtime, tests, performance harness, UE export tool source, schemas, and documentation. The existing UE project remains an external asset source and validation host. Unreal assets are not copied into Git unless their license and repository size policy explicitly allow it.

## 2. Selected Development Strategy

Three strategies were considered:

1. **Asset-first port:** export every ALS asset before building the runtime. This gives representative data early, but couples thread architecture debugging to FBX and metadata failures.
2. **Feature-first vertical slice:** implement one complete character before performance infrastructure. This produces visible results quickly, but risks discovering incompatible thread ownership after animation logic has become coupled to SceneTree nodes.
3. **Runtime-foundation first:** establish deterministic data contracts, multithreaded dispatch, synthetic fixtures, and performance measurement before exporting UE assets, then implement behavior module by module. This is the selected strategy because concurrency and ownership are the highest-cost decisions to change later.

Visible ALS behavior is not the first milestone. The first milestone is a measurable, deterministic, safe animation distribution pipeline that can accept real assets without changing its cross-thread contract.

## 3. Repository Boundary

The standalone repository is `.` and uses `main` as its initial branch.

Planned top-level layout:

```text
GodotALS/
  project.godot
  GodotALS.csproj
  addons/
    als_importer/             Godot EditorPlugin and import pipeline
  assets/
    generated/                ignored generated imports
    fixtures/                 small redistributable test fixtures
  scenes/
    benchmark/
    tests/
    demo/
  src/
    Als.Core/                 pure C# contracts and locomotion logic
    Als.Godot/                Node adapters and runtime integration
    Als.Import/               manifest validation and compilation
    Als.Diagnostics/          timing, counters, traces, reports
  tests/
    Als.Core.Tests/
    Als.Import.Tests/
    Als.Integration.Tests/
  tools/
    unreal/                   UE Python/C++ exporter source
    schemas/                  versioned JSON schemas
  vendor/
    references/               pinned metadata only, no implicit code copy
  docs/
    architecture/
    benchmarks/
    superpowers/
```

Generated Godot imports, `.godot`, C# build outputs, UE-exported binary assets, benchmark captures, and local engine paths are ignored. Small fixtures required for deterministic tests may be committed only when their redistribution rights are documented.

## 4. Upstream Reference Policy

`ALS-Refactored` is a behavioral and algorithmic reference, not a linked runtime dependency. Development uses tag `4.17` at commit `cb03d53526fed8eaf62637621d4593a57753b317`; `main` is never used as an unpinned reference.

The upstream module boundaries guide the port:

- `ALS`: character state, animation state, locomotion actions, math, foot placement, ragdoll.
- `ALSCamera`: third-person and first-person view behavior.
- `ALSEditor`: UE-side setup and metadata export concepts.
- `ALSExtras`: examples and optional behavior, excluded unless needed by milestone B.

Each ported feature receives a provenance note containing upstream file paths, tag/commit, relevant UE assets or graphs, Godot implementation files, intentional semantic differences, and associated tests. MIT-licensed source logic may be translated, but UE/Fab animation and model asset licensing is reviewed separately.

The current asset source is a UE 5.9 project while reference tag 4.17 targets UE 5.7. The reference plugin is therefore not installed as a required UE 5.9 dependency. Any code consulted is treated as specification; exporter code must compile against the actual source project's UE version.

## 5. Runtime Architecture

### 5.1 Thread Stages

Every physics frame uses three ordered stages:

```text
Main Order 0: Gather
  -> immutable AlsFrameInput per character
Worker Order 1: Visual evaluation
  -> AlsFrameResult per character
Main Order 2: Commit
```

Gather owns input, `CharacterBody3D` motion observations, physics queries, floor and moving-platform state, mantle probes, ragdoll lifecycle observations, and publication of immutable inputs.

Each character has one visual worker root in a `SUB_THREAD` process group. It owns that character's `AnimationTree`, `Skeleton3D`, modifiers, IK nodes, and mutable `AlsRuntimeState`. It may read only its frame input, immutable animation-set data, and worker-owned state.

Commit validates the result identity and freshness, consumes root motion through collision-aware movement, changes drive modes, dispatches typed events, updates cameras and audio, and performs SceneTree side effects.

Workers never access another character's nodes, perform world physics queries, dispatch gameplay callbacks, mutate shared collections, or enqueue nested work that waits on the same worker pool.

### 5.2 Stable Cross-Thread Contract

The architecture is fixed around:

```text
Evaluate(
  in AlsFrameInput input,
  ref AlsRuntimeState state,
  ref AlsFrameResult result
)
```

Frame structures contain only fixed-width values, stable integer IDs, bounded preallocated event storage, and math types with explicit units. They contain no `Node`, `Resource`, `String`, delegate, dynamic collection, or mutable shared reference.

Conventions are `float32`, meters, seconds, radians, right-handed coordinates, and Y-up. Godot-specific types are converted at Gather and Commit boundaries. The hot path performs zero managed allocations after warm-up.

Each character slot uses double-buffered input and result storage with `FrameId`, `CharacterId`, and `SlotGeneration`. Commit rejects incomplete, stale, duplicate, future, or generation-mismatched results. Slots are recycled only after Commit and always increment their generation.

### 5.3 Pose and Movement Ownership

There are four explicit drive modes:

| Drive mode | Movement owner | Pose owner |
| --- | --- | --- |
| `MotorDriven` | Main-thread character motor | Worker visual rig |
| `AnimationDriven` | Worker proposes, Main consumes | Worker visual rig |
| `PhysicsDriven` | Main-thread physics | Physical bone simulation |
| `RecoveryBlend` | Main holds the capsule | Worker visual rig |

Only Commit changes drive mode, and at most one transition occurs per physics frame. Mantle, Roll, and Get-up cannot directly move `CharacterBody3D`; they emit a proposed root-motion delta. A main-thread solver applies collision-safe translation and rotation, reports the consumed fraction and interruption reason, and feeds residual error into the next frame.

Ragdoll transitions explicitly transfer skeleton ownership so animation evaluation and physics never write the pose simultaneously. Recovery captures local bone poses, determines face-up or face-down state, aligns the capsule, stops physics, and blends from the captured pose into the selected Get-up animation.

## 6. Asset Contract

The Unreal exporter produces FBX files plus a versioned `als_manifest.json`. FBX carries mesh, skin, skeleton, and animation tracks. The manifest carries semantics that FBX cannot reliably represent:

- source UE version, ALS variant, exporter version, and source identifiers;
- skeleton hierarchy, rest-pose hash, sockets, virtual bones, units, and axes;
- curves, Notify and Notify State data, Sync Markers, and stable names;
- BlendSpace and Aim Offset samples;
- montage sections, blend settings, interruption rules, and root-motion policy;
- track filters, required bones, physics-body metadata, and camera settings.

The Godot importer validates the manifest against a versioned JSON schema and compiles it into an immutable `AlsAnimationSet` containing stable integer IDs and contiguous lookup data. Runtime code does not repeatedly resolve bones, clips, parameters, or curves by string.

Missing required bones, incompatible rest poses, invalid units or axes, discontinuous loops, missing root tracks, invalid additive bases, missing required curves/events/markers, invalid physics references, and stable-ID collisions fail the import. There is no silent fallback.

## 7. Feature Port Order

After the runtime foundation and importer are validated, features are implemented in this order:

1. Character motor, movement state, gait, stance, and rotation modes.
2. Locomotion blend selection, stride, play rate, lean, Jump, Fall, and Land.
3. Looking Direction, Aiming, Aim Offset, Turn in Place, Rotate in Place, and layered pose correction.
4. Foot IK, Foot Lock, pelvis correction, slope, stairs, and moving platforms.
5. Typed events, Notify States, Sync Markers, Dynamic Transition, and action playback.
6. Mantle and Roll with collision-safe root-motion consumption.
7. Ragdoll, face-up/face-down detection, pose capture, recovery blend, and Get-up.
8. Third-person and first-person camera, shoulder switching, lag, FOV, and obstruction handling.

Every feature is a vertical slice with pure-model tests, Godot integration tests, a focused demo scene, single-thread/multithread equivalence checks, and a ten-character performance checkpoint. A feature is not complete when only the visual result works.

## 8. Implementation Phases and Gates

### Phase 0: Repository and deterministic core

- Establish the Godot .NET project, solution, formatting, test projects, local path configuration, and CI-ready commands.
- Define frame contracts, bounded event buffers, runtime state, double buffering, generation checks, and deterministic math helpers.
- Build tests before runtime implementations.

Gate: all contract tests pass; no Godot node is present in `Als.Core`.

### Phase 1: Multithreaded dispatch harness

- Implement Gather, per-character visual worker roots, and Commit barriers.
- Use a small synthetic skeleton and procedural or redistributable animations.
- Exercise 1, 10, 16, and 32 characters in single-thread and multithread modes.
- Measure stage wall time, per-character work, dropped/stale results, and managed allocations.

Gate: fixed input yields equivalent state, root-motion proposals, event order, and poses within declared tolerances; lifecycle stress produces no stale submissions or thread-access violations.

### Phase 2: UE export and Godot import

- Implement the manifest schema and golden fixture first.
- Build the UE 5.9 exporter against `../AdvancedLocomotionSystemV`.
- Export a minimal skeleton and a small locomotion clip set before bulk export.
- Compile and validate `AlsAnimationSet` in Godot.

Gate: a clean checkout can reproduce the import from documented external inputs, and malformed fixtures fail with actionable diagnostics.

### Phase 3: ALS feature slices

Implement the feature order in section 7. After each slice, compare results to captured UE golden data and the pinned ALS-Refactored behavior reference.

Gate: the slice's functional, determinism, lifecycle, and ten-character performance tests pass before the next slice starts.

### Phase 4: Final integration and performance

- Run the complete milestone-B demo and required stress scenarios.
- Profile before introducing any GDExtension.
- Replace only measured hotspots behind existing interfaces.
- Record reproducible Release-export benchmark reports.

Gate: ten full-quality characters meet the performance contract on the target CPU for a ten-minute measurement after a 30-second warm-up.

## 9. Error Handling and Diagnostics

Debug builds fail immediately on worker exceptions, illegal node ownership, malformed assets, stale results, drive-mode conflicts, event-buffer overflow, and unexpected allocations in guarded hot paths.

Release builds preserve the last valid visual pose, keep or return the motor to a safe locomotion state, discard invalid results, and write a bounded diagnostic record. Asset import errors remain fatal in all configurations. Root-motion collision interruption is an expected typed result, not an exception.

Diagnostic records include frame, character, generation, stage, drive mode, animation/action IDs, elapsed time, allocation delta, and a stable reason code. Recording must not introduce per-frame allocation in the measured path.

## 10. Verification and Performance Contract

Automated verification is layered:

- Pure C# unit tests for state transitions, dampers, curve sampling, sync/event ordering, buffers, generation, and root-motion accounting.
- Schema and importer tests using valid and invalid golden manifests.
- Headless Godot integration tests for process groups, node ownership, animation evaluation, IK, lifecycle changes, and deterministic replay.
- UE exporter tests for manifest completeness and source-to-export counts.
- Cross-engine golden tests comparing clip metadata, sampled transforms, curves, events, markers, and action timing.
- Release benchmark scenes for 1, 10, 16, and 32 characters.

Target measurements on Intel i7-10700:

| Metric | Requirement |
| --- | ---: |
| Physics rate | 60 Hz |
| First ten characters | Full quality, 60 Hz animation |
| Gather + Commit main-thread p95 | <= 1.5 ms |
| Worker critical path p95 | <= 2.5 ms |
| ALS critical path p99 | <= 4.0 ms |
| Total CPU frame p99 | <= 16.67 ms |
| Managed allocation after warm-up | 0 B/frame |
| Stale, duplicate, or lost results | 0 |
| Duplicate or out-of-order typed events | 0 |

The benchmark uses a Release export, fixed rendering settings, a 30-second warm-up, and a ten-minute sample. Average FPS alone is not an acceptance metric. Animation LOD, reduced update rate, disabled IK, or simplified graphs cannot be used for the first ten characters.

## 11. Explicit Non-Goals

- No Godot Core changes or maintained engine fork.
- No per-character operating-system threads.
- No general runtime retargeting for arbitrary skeletons.
- No network prediction or complete weapon/overlay framework in milestone B.
- No direct `.uasset` reader.
- No early GDExtension rewrite without profiler evidence.
- No worker-thread gameplay callbacks or external SceneTree mutation.

## 12. Definition of Done

The project is complete only when the fixed Unreal asset set imports through FBX plus the manifest, milestone-B behaviors are usable in the standalone sample, single-thread and multithread modes pass equivalence tests, root-motion actions are collision-safe, ragdoll has unambiguous pose ownership, event semantics are deterministic, the hot path allocates no managed memory after warm-up, and the ten-character i7-10700 performance gate passes without reducing quality.
