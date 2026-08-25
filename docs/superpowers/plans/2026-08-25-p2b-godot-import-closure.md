# P2B Godot Import Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strictly compile the audited P2A manifest into a Godot `AlsAnimationSet`, import every exported FBX/PNG, and prove a real Mannequin animation rig behaves equivalently in single-thread and process-group worker modes.

**Architecture:** Godot's built-in FBX importer remains the only geometry and animation decoder. The Godot-free `Als.Import` library validates files and typed metadata, converts UE coordinates, removes explicitly declared virtual bones from the physical FBX skeleton contract, and compiles stable integer lookup tables. The Godot layer persists the compiled definition as a generated Resource, reconstructs approximate materials from manifest data, retargets imported animation tracks onto the Mannequin scene, and runs a real-rig process-group smoke test.

**Tech Stack:** Godot 4.7.2 .NET, C# 12 / .NET 8, xUnit, System.Text.Json, Godot `PackedScene` / `Skeleton3D` / `AnimationPlayer`, PowerShell verification scripts.

---

## Fixed Decisions And Probe Results

- P2A output is `assets/generated/als_v4`; generated FBX, PNG, `.import`, `.tres`, and `.godot` cache remain untracked.
- `artifacts/p2a-determinism` must contain a `.gdignore`, otherwise Godot imports the second deterministic copy and doubles all imported resources.
- FBX image references point at original workstation TGA paths. `import_defaults.cfg` must set `fbx/embedded_image_handling=0`; P2B reconstructs materials from manifest texture IDs instead of loading FBX image paths.
- Mannequin asset ID is `86d98d8177feb473c8a5f406c5b42f8c2a2f7b07`; its imported scene contains a 68-bone `Skeleton3D` and mesh.
- UE Skeleton ID `b5b52715012cad50bf7a625ddf01e4335bb4fcf0` has 79 logical bones: 68 physical FBX bones plus 11 entries listed in `metadata.virtualBones`. The physical import contract is the ordered logical-bone list with those 11 virtual-bone names removed.
- Walk-forward clip ID `6124eafdcbeaaf04bca366add34c821faa0e4963` imports as a 1.133333-second animation named `Unreal Take` with 128 tracks rooted at `Skeleton3D:<bone>`.
- M4A1 asset ID is `2516ba17950769f5845f00f6c17c6d6ac913f475`; its imported scene contains the expected 9-bone skeleton and mesh.
- P2B does not implement locomotion state selection, Overlay gameplay, IK, Mantle behavior, or final performance budgets. It establishes validated runtime data and a real-rig execution boundary for those later slices.

### Task 1: Isolate Godot's Import Scan And Pin FBX Defaults

**Files:**
- Modify: `.gitignore`
- Create: `artifacts/.gdignore`
- Create: `benchmark-results/.gdignore`
- Create: `docs/.gdignore`
- Create: `src/Als.Core/.gdignore`
- Create: `src/Als.Import/.gdignore`
- Create: `tests/.gdignore`
- Create: `tools/.gdignore`
- Create: `import_defaults.cfg`
- Modify: `scripts/verify-p2a.ps1`
- Create: `tests/Als.Import.Tests/RepositoryRoot.cs`
- Create: `tests/Als.Import.Tests/RepositoryImportPolicyTests.cs`

- [ ] **Step 1: Write failing repository-policy tests**

Add tests that locate the repository root and assert:

```csharp
[Fact]
public void DeterminismArtifactsAreHiddenFromGodot()
{
    var root = RepositoryRoot.Find();
    Assert.True(File.Exists(Path.Combine(root, "artifacts", ".gdignore")));
    Assert.True(File.Exists(Path.Combine(root, "src", "Als.Import", ".gdignore")));
    Assert.False(File.Exists(Path.Combine(root, "src", "Als.Godot", ".gdignore")));
}

[Fact]
public void FbxImportDoesNotResolveExporterWorkstationTextures()
{
    var defaults = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "import_defaults.cfg"));
    Assert.Contains("fbx/embedded_image_handling=0", defaults, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Debug --filter RepositoryImportPolicyTests
```

Expected: FAIL because `artifacts/.gdignore`, `import_defaults.cfg`, and `RepositoryRoot` do not exist.

- [ ] **Step 3: Add the minimal tracked policy files**

Change `.gitignore` from the blanket `artifacts/` rule to:

```gitignore
artifacts/*
!artifacts/.gdignore
```

Create `.gdignore` markers in `artifacts`, `benchmark-results`, `docs`, `src/Als.Core`, `src/Als.Import`, `tests`, and `tools`. Do not create one in `assets/generated` or `src/Als.Godot`: generated assets and Godot scripts must remain visible to the editor. Create `import_defaults.cfg`:

```ini
[scene]

fbx/embedded_image_handling=0
animation/import=true
animation/fps=30
```

Add a test-only `RepositoryRoot.Find()` helper that walks parents until both `GodotALS.sln` and `.git` are present. Update `verify-p2a.ps1` to recreate `artifacts/.gdignore` after any artifact cleanup, so local verification cannot remove the scan boundary.

- [ ] **Step 4: Verify GREEN and run P2A script syntax checks**

Run the focused test and parse all PowerShell verification scripts with `System.Management.Automation.Language.Parser.ParseFile`. Expected: tests pass and parser error count is zero.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore artifacts/.gdignore benchmark-results/.gdignore docs/.gdignore src/Als.Core/.gdignore src/Als.Import/.gdignore tests/.gdignore tools/.gdignore import_defaults.cfg scripts/verify-p2a.ps1 tests/Als.Import.Tests
git commit -m "fix: isolate generated ALS import inputs"
```

### Task 2: Strict Manifest Deserialization And File Audit

**Files:**
- Create: `src/Als.Import/Manifest/AlsManifestSerializer.cs`
- Create: `src/Als.Import/Validation/AlsManifestValidator.cs`
- Create: `src/Als.Import/Validation/AlsManifestValidationOptions.cs`
- Create: `src/Als.Import/Validation/AlsFileAuditor.cs`
- Modify: `src/Als.Import/Manifest/AlsManifest.cs`
- Create: `tests/Als.Import.Tests/Fixtures/valid_manifest.json`
- Create: `tests/Als.Import.Tests/AlsManifestSerializerTests.cs`
- Create: `tests/Als.Import.Tests/AlsManifestValidatorTests.cs`
- Create: `tests/Als.Import.Tests/AlsFileAuditorTests.cs`

- [ ] **Step 1: Write failing serializer and semantic-validation tests**

Cover exact camel-case property names, unknown-property rejection, schema version, `auditSummary.status=complete`, per-section stable-ID ordering, duplicate IDs, canonical object/output paths, summary counts, dependency/reference closure, and structured field paths:

```csharp
var result = AlsManifestValidator.Validate(manifest);
Assert.Contains(result, issue =>
    issue.Code == "ALSMANIFEST012" &&
    issue.FieldPath == "$.animations[0].metadata.skeletonId");
```

- [ ] **Step 2: Verify RED**

Run the three focused test classes. Expected: compile failure because serializer, validator, options, and auditor are absent.

- [ ] **Step 3: Implement strict loading and graph validation**

`AlsManifestSerializer.Load(path)` must use:

```csharp
new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
};
```

`AlsManifestValidator` builds one ordinal `Dictionary<string, (string Section, int Index)>`, recursively checks every metadata property named `id` or ending in `Id`, and emits stable issue codes without throwing for content errors.

- [ ] **Step 4: Implement streaming file audit**

`AlsFileAuditor.Validate(root, manifest)` rejects absolute paths, traversal, missing/empty files, size mismatch, SHA-256 mismatch, duplicate file rows, output files absent from `files[]`, and undeclared files under the four binary output folders. Hash with `SHA256.HashData(FileStream)` and lowercase hex.

- [ ] **Step 5: Verify GREEN and malformed fixture coverage**

Run all `Als.Import.Tests`. Expected: existing export-plan tests plus new manifest tests pass with no warning.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Import tests/Als.Import.Tests
git commit -m "feat: validate audited ALS manifests"
```

### Task 3: Compile Skeleton And Coordinate Contracts

**Files:**
- Create: `src/Als.Import/Metadata/AlsRigMetadata.cs`
- Create: `src/Als.Import/Compilation/AlsCoordinateConverter.cs`
- Create: `src/Als.Import/Compilation/AlsSkeletonCompiler.cs`
- Create: `src/Als.Import/Compilation/AlsSkeletonDefinition.cs`
- Create: `src/Als.Import/Compilation/AlsCanonicalPoseHash.cs`
- Create: `tests/Als.Import.Tests/AlsCoordinateConverterTests.cs`
- Create: `tests/Als.Import.Tests/AlsSkeletonCompilerTests.cs`
- Create: `tests/Als.Import.Tests/AlsCanonicalPoseHashTests.cs`

- [ ] **Step 1: Write failing coordinate and virtual-bone tests**

Assert the fixed mapping:

```csharp
Assert.Equal(new Vector3(2f, 3f, -1f),
    AlsCoordinateConverter.PositionCentimetersToMeters(new Vector3(100f, 200f, 300f)));
```

For the Mannequin-shaped fixture, assert 79 logical bones, 11 virtual bones, 68 physical bones, stable physical parent indices, and required `root`, `pelvis`, `foot_l`, `foot_r` IDs.

- [ ] **Step 2: Verify RED**

Run the three focused classes. Expected: compile failure because compilation types are absent.

- [ ] **Step 3: Implement typed rig metadata and coordinate conversion**

Deserialize bone local translation/rotation/scale, sockets, virtual bones, mesh `skeletonId`, and physics bodies/constraints. Map UE `(X forward, Y right, Z up)` centimeters to Godot `(X right, Y up, -Z forward)` meters as `(Y, Z, -X) * 0.01`. Convert rotations by basis conjugation `C * R * inverse(C)` and normalize quaternion sign so `W >= 0` before hashing.

- [ ] **Step 4: Compile logical and physical skeleton tables**

Assign integer IDs by manifest bone order. Build `LogicalBones`, `PhysicalBones`, `LogicalToPhysical`, `PhysicalToLogical`, sockets, and virtual-bone source/target indices. Reject virtual references that do not resolve and physical children whose surviving parent is missing.

- [ ] **Step 5: Implement canonical target-pose hashing**

Hash ordered UTF-8 bone name, parent ID, and coordinate-converted local transform after rounding floats to `1e-5`. Store separate `SourceRestPoseHash` and `TargetPhysicalRestPoseHash`; never compare UE's 79-bone source hash directly with Godot's 68-bone imported skeleton.

- [ ] **Step 6: Verify GREEN and commit**

Run all import tests, then commit:

```powershell
git add src/Als.Import tests/Als.Import.Tests
git commit -m "feat: compile ALS skeleton contracts"
```

### Task 4: Compile Animation, Montage, Blend, Material, And Physics Data

**Files:**
- Create: `src/Als.Import/Metadata/AlsAnimationMetadata.cs`
- Create: `src/Als.Import/Metadata/AlsCompositeMetadata.cs`
- Create: `src/Als.Import/Metadata/AlsMaterialMetadata.cs`
- Create: `src/Als.Import/Compilation/AlsAnimationSetCompiler.cs`
- Create: `src/Als.Import/Compilation/AlsAnimationSetDefinition.cs`
- Create: `src/Als.Import/Compilation/AlsAssetIndex.cs`
- Create: `tests/Als.Import.Tests/AlsAnimationSetCompilerTests.cs`
- Create: `tests/Als.Import.Tests/AlsMetadataReferenceTests.cs`

- [ ] **Step 1: Write failing typed-compilation tests**

The valid fixture must compile into deterministic arrays and integer IDs. Assert play length/sample rate, loop/root-motion/additive settings, curve/event/sync order, montage section/segment references, BlendSpace samples, material parent/parameter overrides, physics constraints, and exact stable-ID-to-index mappings.

- [ ] **Step 2: Verify RED**

Run the focused compiler tests. Expected: compile failure because the compiler API is absent.

- [ ] **Step 3: Implement typed metadata readers**

Use `JsonElement.Deserialize<T>(AlsManifestSerializer.JsonOptions)` per asset kind. Missing required arrays are errors even when the expected array is empty. Optional object references use `-1` after compilation; unresolved non-empty IDs are validation failures.

- [ ] **Step 4: Implement immutable animation-set definition**

`AlsAnimationSetDefinition` contains arrays for skeletons, meshes, clips, montages, blend spaces, aim offsets, materials, textures, physics, curves, and config assets plus ordinal stable-ID lookup tables. Integer IDs are assigned from already sorted manifest arrays; runtime lookup never scans object paths.

- [ ] **Step 5: Verify deterministic compilation**

Compile the same fixture twice, serialize the definition's canonical digest input, and assert identical SHA-256. Mutating one clip duration or reference must change the digest or produce a field-specific validation issue.

- [ ] **Step 6: Verify GREEN and commit**

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Debug
git add src/Als.Import tests/Als.Import.Tests
git commit -m "feat: compile ALS animation set definitions"
```

### Task 5: Add Godot Resource Bridge And Full Imported-Resource Audit

**Files:**
- Modify: `GodotALS.csproj`
- Create: `src/Als.Godot/Assets/AlsAnimationSetResource.cs`
- Create: `src/Als.Godot/Assets/AlsAssetResourceEntry.cs`
- Create: `src/Als.Godot/Import/AlsGodotImportCoordinator.cs`
- Create: `src/Als.Godot/Import/AlsImportedResourceAuditor.cs`
- Create: `src/Als.Godot/Import/AlsMaterialBuilder.cs`
- Create: `addons/als_importer/plugin.cfg`
- Create: `src/Als.Godot/Import/AlsImporterPlugin.cs`
- Create: `src/Als.Godot/Import/P2bImportEntry.cs`
- Create: `scenes/tests/p2b_import.tscn`
- Create: `scripts/verify-p2b.ps1`
- Modify: `project.godot`

- [ ] **Step 1: Add a failing headless P2B gate**

`verify-p2b.ps1` must require a complete formal manifest, build the solution, clear only `.godot/imported` and generated `.import`/compiled outputs when `-CleanImport` is set, run `godot --headless --editor --import`, reject `ERROR:`/`SCRIPT ERROR:`, then run `p2b_import.tscn`. Initial expected result: non-zero because `P2B_IMPORT_OK` is absent.

- [ ] **Step 2: Reference `Als.Import` and implement generated Resources**

Add explicit `TargetFramework=net8.0` and the `Als.Import` project reference to `GodotALS.csproj` so the editor does not rewrite the project file. `AlsAnimationSetResource` stores schema/exporter/source IDs, definition digest, and parallel arrays of stable IDs, integer IDs, Godot resource paths, source object paths, skeleton hashes, and semantic counts. Generated output is `res://assets/generated/als_v4/compiled/als_animation_set.tres`.

- [ ] **Step 3: Implement imported-resource audit**

For all 141 binary rows, convert manifest paths to `res://assets/generated/als_v4/<relativePath>`, require `ResourceLoader.Exists`, and load the expected type: FBX as `PackedScene`, PNG/TGA as `Texture2D`. Inspect 7 skeletal scenes for `Skeleton3D` and `MeshInstance3D`, 4 static scenes for mesh, and 126 animation scenes for `AnimationPlayer`, non-empty animation, duration tolerance `1/30` second, and physical bone-name order.

- [ ] **Step 4: Implement material reconstruction**

Ignore FBX image paths. Build `StandardMaterial3D` instances from compiled scalar/vector/texture overrides, resolve texture IDs to stable PNG paths, and match imported mesh surface material names to manifest material names. Emit structured unresolved-slot diagnostics; the P2B smoke requires Mannequin and M4A1 to receive a non-null material, but does not require visual parity shaders.

- [ ] **Step 5: Implement coordinator and EditorPlugin**

`AlsGodotImportCoordinator.Run()` loads, validates, audits, compiles, saves the Resource atomically through a temporary `.tres`, reloads it, and returns a count/error report. `AlsImporterPlugin` exposes one editor tool-menu command that calls the same coordinator; enable it in `project.godot` under `[editor_plugins]`. Headless entry calls the coordinator directly and prints:

```text
P2B_IMPORT_OK assets=267 files=141 skeletal=7 static=4 animations=126 textures=4
```

- [ ] **Step 6: Run headless import to verify GREEN**

Expected: no determinism-directory resources, no missing original TGA errors, all 141 resources load, compiled Resource reloads, and the success marker appears.

- [ ] **Step 7: Commit**

```powershell
git add GodotALS.csproj project.godot import_defaults.cfg addons src/Als.Godot scenes/tests/p2b_import.tscn scripts/verify-p2b.ps1
git commit -m "feat: import audited ALS assets into Godot"
```

Include editor-generated `.uid` files only for tracked C# scripts under `src/Als.Godot`; the `.gdignore` boundaries from Task 1 must prevent UID files under core libraries, tests, docs, and UE tools.

### Task 6: Bind Representative Animations To The Real Mannequin Rig

**Files:**
- Create: `src/Als.Godot/Animation/AlsAnimationBinder.cs`
- Create: `src/Als.Godot/Animation/AlsPoseDigest.cs`
- Create: `src/Als.Godot/Import/P2bAssetSmoke.cs`
- Create: `scenes/tests/p2b_asset_smoke.tscn`
- Modify: `scripts/verify-p2b.ps1`

- [ ] **Step 1: Extend the gate and verify RED**

Require Mannequin, M4A1, Walk Forward, Run Forward, Turn L90, Mantle 1m, and M4A1/Pistol Overlay stable IDs. Expected failure: `P2B_ASSET_SMOKE_OK` is absent.

- [ ] **Step 2: Implement animation binding**

Instantiate the Mannequin scene, locate its `Skeleton3D` and `AnimationPlayer`, duplicate the source clip's `Unreal Take`, and rewrite each track node portion from `Skeleton3D:<bone>` to the target skeleton path relative to the target player's root. Reject missing physical bones, unknown track types, zero-track clips, and duration mismatches. Set callback mode to manual and call `Advance(delta)` explicitly.

- [ ] **Step 3: Implement normalized pose digest**

For `root`, `pelvis`, `spine_03`, `hand_l`, `hand_r`, `foot_l`, and `foot_r`, hash frame number, bone ID, and quantized local pose position/rotation/scale. Exclude timing and object instance IDs.

- [ ] **Step 4: Implement representative asset smoke**

Load and advance each representative clip for `min(60, ceil(playLength * 30))` frames, require at least one selected bone pose to change for non-pose clips, instantiate Mannequin and M4A1 meshes, apply approximate materials, and print:

```text
P2B_ASSET_SMOKE_OK mannequinBones=68 clips=6 overlay=2 props=1
```

- [ ] **Step 5: Verify GREEN and commit**

Run `verify-p2b.ps1`, then commit the binder, digest, smoke scene, and script updates.

### Task 7: Prove Real-Rig Single/Parallel Process-Group Equivalence

**Files:**
- Create: `src/Als.Godot/Dispatch/AlsRealRigWorkerRoot.cs`
- Create: `src/Als.Godot/Dispatch/P2bRealRigHarness.cs`
- Create: `src/Als.Godot/Dispatch/AlsRealRigHarnessContext.cs`
- Create: `scenes/tests/p2b_real_rig_harness.tscn`
- Modify: `scripts/verify-p2b.ps1`

- [ ] **Step 1: Add the failing real-rig matrix**

Run one and ten Mannequins for 120 fixed 60 Hz frames in `single` and `parallel` modes. Require matching digests, every parallel worker observed off-main-thread, `missing=0`, `stale=0`, and no thread-access errors. Expected failure: harness classes and marker are absent.

- [ ] **Step 2: Implement worker ownership**

Each worker instantiates and exclusively owns one Mannequin, its Skeleton3D, AnimationPlayer, bound Walk clip, runtime state, and exchange. Gather publishes only unmanaged input; worker manually advances animation and publishes pose digest/result; Commit consumes by stable character ID. No worker accesses siblings, scene-global nodes, ResourceLoader, or file IO after `_Ready()`.

- [ ] **Step 3: Implement stable lifecycle and digest output**

Warm all workers before measurement. Preserve the P1 generation rules and replace character zero after frame 60. Print one marker per run:

```text
GODOT_ALS_P2B_RIG_OK mode=parallel characters=10 frames=120 digest=<HEX> missing=0 stale=0 off_main=10
```

- [ ] **Step 4: Verify GREEN**

The script compares each character-count pair and fails if digest or semantic counts differ. This is a correctness gate, not the final 10-minute performance benchmark.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Dispatch scenes/tests/p2b_real_rig_harness.tscn scripts/verify-p2b.ps1
git commit -m "test: verify real ALS rigs across process groups"
```

### Task 8: Final Regression, Documentation, And Handoff

**Files:**
- Create: `docs/architecture/p2b-godot-import-closure.md`
- Modify: `docs/superpowers/plans/2026-08-25-p2b-godot-import-closure.md`
- Modify: `docs/superpowers/specs/2026-08-25-p2-full-asset-pipeline-design.md`

- [ ] **Step 1: Run the complete fresh verification matrix**

```powershell
.\scripts\verify-p2b.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' -CleanImport
.\scripts\verify-p1.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
.\scripts\verify-p0.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet test .\GodotALS.sln -c Release --no-restore
git diff --check
```

Expected: `P2B_VERIFICATION_OK`, P1/P0 markers, all tests pass, and no build/import/thread error.

- [ ] **Step 2: Record actual import and smoke results**

Document Godot importer version/options, imported counts, cache size/time, physical/logical bone counts, representative clip durations/tracks, material limitations, real-rig digests, worker counts, and known warnings. Update the P2 design status to P2 complete only if all ten completion conditions pass.

- [ ] **Step 3: Audit repository boundaries**

Require no tracked FBX/PNG/TGA, `.import`, `.godot`, generated `.tres`, UE binaries, plugin packages, `bin`, or `obj`. Confirm only `artifacts/.gdignore` is tracked beneath `artifacts`.

- [ ] **Step 4: Mark plan checks and commit**

```powershell
git add docs
git commit -m "docs: record P2B Godot import closure"
```

- [ ] **Step 5: Review the full branch against the design**

Check `main...HEAD` for missing P2 completion conditions, placeholder text, silent fallback, path assumptions, and untested production methods. P3 may start only after this review and the complete P2B gate pass.
