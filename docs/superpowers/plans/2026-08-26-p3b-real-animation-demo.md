# P3B Real Animation and Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已验证的 P3A motor/model 上绑定完整 P3 动画 profile、真实 Mannequin 动画图、可玩第三人称演示和 1/10 角色确定性多线程门禁。

**Architecture:** 初始化期把 P2 的 stable ID 编译为冻结 profile，并把所有 P3 clip 重绑到单一 Mannequin `Skeleton3D` 和 `AnimationLibrary`。运行期 worker 只把 P3A 的定宽结果写入预缓存的动画参数并手动推进，player 与 replay 只替换命令来源，不分叉 motor/model/animation 路径。

**Tech Stack:** C# 12 / .NET 8、xUnit、Godot 4.7.2 .NET AnimationTree/AnimationPlayer、P2 `AlsAnimationSetResource`、P3A contracts and replay harness

---

## File Map

| Path | Responsibility |
| --- | --- |
| `src/Als.Import/Compilation/AlsLocomotionAnimationProfile.cs` | stable ID 到 P3 animation purpose 的冻结定义 |
| `src/Als.Import/Compilation/AlsLocomotionProfileCompiler.cs` | profile 完整性、骨架和 additive 合同验证 |
| `assets/config/p3_locomotion_profile.json` | 明确的 stable ID 映射，不按名称发现 |
| `scripts/generate-p3-profile.ps1` | 从审核过的完整 UE object path 清单生成 stable-ID sample grid |
| `src/Als.Godot/Animation/AlsAnimationLibraryBuilder.cs` | 多 clip 单骨架库构建 |
| `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs` | 动画图、参数缓存和手动推进 |
| `src/Als.Godot/Locomotion/AlsP3Character.cs` | motor、exchange、worker visual rig 的角色组合根 |
| `src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs` | 键鼠命令来源 |
| `src/Als.Godot/Locomotion/AlsOrbitCamera.cs` | P3 最小第三人称 orbit camera |
| `src/Als.Godot/Locomotion/AlsLocomotionHud.cs` | 状态与性能读数 |
| `src/Als.Godot/Locomotion/P3bAnimationHarness.cs` | 1/10 角色真实动画门禁 |
| `scenes/tests/p3b_animation_harness.tscn` | headless real-animation 入口 |
| `scenes/demo/p3_locomotion_demo.tscn` | 可玩第三人称演示 |
| `scripts/verify-p3b.ps1` | P3B、P3A 和所有既有回归 |

在每个新的 PowerShell 会话先定义：

```powershell
$godotExe = "${env:GODOT_EXECUTABLE}"
```

### Task 0: Restore the Complete Generated Asset Batch

**Files:**
- Generated: `assets/generated/als_v4/**` (gitignored, never commit)

- [ ] **Step 1: Confirm the current generated directory is incomplete**

Run: `Test-Path 'assets/generated/als_v4/als_manifest.json'`

Expected on the current main checkout: `False`.

- [ ] **Step 2: Re-run the deterministic full P2A export**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-p2a.ps1 `
  -EngineRoot "${env:UE_ENGINE_ROOT}" `
  -UnrealProject "${env:ALS_UE_PROJECT_FILE}"
```

Expected: `GODOT_ALS_P2A_FULL_EXPORT_OK files=141`, deterministic export comparison success, and
`P2A_VERIFICATION_OK`.

- [ ] **Step 3: Re-import and validate the complete batch**

Run: `pwsh -NoProfile -File scripts/verify-p2b.ps1 -GodotExecutable $godotExe -CleanImport`

Expected: assets `267`, files `141`, skeletal meshes `7`, static meshes `4`, animations `126`, textures `4`,
then `P2B_VERIFICATION_OK`.

- [ ] **Step 4: Check the ignored boundary**

Run: `git status --short`

Expected: no generated asset or Godot import cache paths appear. This preflight creates no Git commit.

### Task 1: Compile a Strict Stable-ID Locomotion Profile

**Files:**
- Create: `assets/config/p3_locomotion_profile.json`
- Create: `scripts/generate-p3-profile.ps1`
- Create: `src/Als.Import/Compilation/AlsLocomotionAnimationProfile.cs`
- Create: `src/Als.Import/Compilation/AlsLocomotionProfileCompiler.cs`
- Create: `tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs`
- Create: `tests/Als.Import.Tests/P3RepositoryFixtures.cs`

- [ ] **Step 1: Write failing profile integrity tests**

Add the repository helper used by the tests:

```csharp
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

internal static class P3RepositoryFixtures
{
    public static AlsAnimationSetDefinition LoadAnimationSet()
    {
        var path = Path.Combine(
            RepositoryRoot.Find(), "assets", "generated", "als_v4", "als_manifest.json");
        return AlsAnimationSetCompiler.Compile(AlsManifestSerializer.Load(path));
    }

    public static string ReadProfile() => File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "assets", "config", "p3_locomotion_profile.json"));

    public static string WithMissingJumpClip()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        root["jumpStart"] = "ffffffffffffffffffffffffffffffffffffffff";
        return root.ToJsonString();
    }
}
```

```csharp
[Fact]
public void RepositoryProfileCompilesAgainstTheP2AnimationSet()
{
    var definition = P3RepositoryFixtures.LoadAnimationSet();
    var json = P3RepositoryFixtures.ReadProfile();
    var profile = AlsLocomotionProfileCompiler.Compile(json, definition);

    Assert.NotEmpty(profile.StandingSamples);
    Assert.NotEmpty(profile.CrouchingSamples);
    Assert.True(profile.JumpStartAnimationId >= 0);
    Assert.True(profile.FallLoopAnimationId >= 0);
    Assert.True(profile.LandAnimationId >= 0);
}

[Fact]
public void MissingStableIdFailsWithoutFallback()
{
    var definition = P3RepositoryFixtures.LoadAnimationSet();
    var exception = Assert.Throws<AlsCompilationException>(() =>
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.WithMissingJumpClip(), definition));
    Assert.Contains("missing stable ID", exception.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run and verify missing compiler failure**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsLocomotionProfileCompilerTests`

Expected: FAIL because profile/compiler are absent.

- [ ] **Step 3: Implement profile compilation**

`generate-p3-profile.ps1` uses structured JSON parsing and an exact source map. The source map contains the Mannequin,
`ALS_N_Pose`, `ALS_CLF_Pose`, standing Walk/Run directional clips, `ALS_N_Sprint_F`, crouching `ALS_CLF_Walk_*`
clips, `ALS_N_JumpRun_LF`, `ALS_N_FallLoop`, `ALS_N_Land_Light`, and `ALS_N_Lean` as full
`/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton` object paths. It rejects zero or multiple manifest
matches and writes their stable IDs with explicit normalized sample
coordinates; asset basenames are never used as lookup keys.

The generated JSON has exactly these top-level properties: `schemaVersion`, `mannequin`, `standingIdle`,
`crouchingIdle`, `standingSamples`, `crouchingSamples`, `jumpStart`, `fallLoop`, `land`, and `leanAdditive`.
Compiler output stores only integer IDs, sample coordinates, rate scales, and one skeleton ID. Reject unknown
properties, missing/duplicate stable IDs, cross-skeleton samples, empty grids, unsupported additive type, and
mismatched base pose.

The committed source map and normalized coordinates are exactly:

```text
baseRoot: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base
mannequin: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin
standingIdle: ${baseRoot}/BasePoses/ALS_N_Pose.ALS_N_Pose
crouchingIdle: ${baseRoot}/BasePoses/ALS_CLF_Pose.ALS_CLF_Pose
standing walk: F(0,0.5), LF(-0.353553,0.353553), RF(0.353553,0.353553),
               B(0,-0.5), LB(-0.353553,-0.353553), RB(0.353553,-0.353553)
standing run:  F(0,1), LF(-0.707107,0.707107), RF(0.707107,0.707107),
               B(0,-1), LB(-0.707107,-0.707107), RB(0.707107,-0.707107)
standing sprint: ALS_N_Sprint_F(0,1.5)
crouching: ALS_CLF_Walk_F(0,1), L(-1,0), R(1,0), B(0,-1)
jumpStart: ${baseRoot}/InAir/ALS_N_JumpRun_LF.ALS_N_JumpRun_LF
fallLoop: ${baseRoot}/InAir/ALS_N_FallLoop.ALS_N_FallLoop
land: ${baseRoot}/InAir/ALS_N_Land_Light.ALS_N_Land_Light
leanAdditive: ${baseRoot}/Locomotion/Detail/ALS_N_Lean.ALS_N_Lean
```

For standing samples, the script expands each `ALS_N_<Gait>_<Direction>` token under `${baseRoot}/Locomotion/`
to `<path>.<asset>`; crouching samples use the same rule for `ALS_CLF_Walk_<Direction>`. These are deterministic
full object paths, not basename searches.

- [ ] **Step 4: Run import tests**

Run: `pwsh -NoProfile -File scripts/generate-p3-profile.ps1`

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj`

Expected: all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add assets/config scripts/generate-p3-profile.ps1 src/Als.Import/Compilation tests/Als.Import.Tests
git commit -m "feat: compile P3 locomotion animation profile"
```

### Task 2: Build One Skeleton with the Complete P3 Animation Library

**Files:**
- Create: `src/Als.Godot/Animation/AlsAnimationLibraryBuilder.cs`
- Modify: `src/Als.Godot/Animation/AlsAnimationBinder.cs`
- Create: `src/Als.Godot/Animation/P3bAnimationLibrarySmoke.cs`
- Create: `scenes/tests/p3b_animation_library_smoke.tscn`

- [ ] **Step 1: Add a failing real-asset smoke**

The smoke loads the compiled P2 set and P3 profile, builds the library, then requires one `Skeleton3D`, every profile animation exactly once, `Unreal Take` length within `1/30 s`, all track paths targeting the one skeleton, and no unsupported track types. It emits:

```text
GODOT_ALS_P3B_LIBRARY_OK bones=68 clips=<profile clip count> skeletons=1
```

Run: `& $godotExe --headless --path ${env:GODOT_ALS_ROOT} res://scenes/tests/p3b_animation_library_smoke.tscn`

Expected: nonzero exit because builder is missing.

- [ ] **Step 2: Extract reusable binding validation**

Move P2B's private track rewrite and target skeleton validation into internal methods used by both `AlsAnimationBinder` and the new builder. Preserve all P2B exception messages and tests.

- [ ] **Step 3: Implement the multi-clip builder**

The builder instantiates the Mannequin once, loads each `ResourcePath` by integer profile ID, duplicates `Unreal Take`, rewrites tracks, inserts the clip under deterministic name `clip_<integer id>`, and returns an owned result containing root, skeleton, player, library, and an integer-ID-to-`StringName` table built before worker processing starts.

- [ ] **Step 4: Run the smoke and P2B regression**

Run: `& $godotExe --headless --path ${env:GODOT_ALS_ROOT} res://scenes/tests/p3b_animation_library_smoke.tscn`

Run: `pwsh -NoProfile -File scripts/verify-p2b.ps1 -GodotExecutable $godotExe`

Expected: library marker PASS and `P2B_VERIFICATION_OK`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Animation scenes/tests/p3b_animation_library_smoke.tscn
git commit -m "feat: build P3 multi-clip animation library"
```

### Task 3: Build and Drive the Grounded/InAir Animation Graph

**Files:**
- Create: `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs`
- Create: `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs`
- Create: `src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs`
- Create: `scenes/tests/p3b_animation_graph_smoke.tscn`

- [ ] **Step 1: Add failing graph-state assertions**

The smoke feeds a fixed sequence of P3A results and asserts the active graph state after manual advance:

```text
Grounded/Standing -> Grounded/Crouching -> JumpStart -> FallLoop -> LandRecovery -> Grounded/Standing
```

It also hashes `pelvis`, `spine_03`, both hands and both feet before/after each segment and requires pose changes. Expected initial run: FAIL because controller is absent.

- [ ] **Step 2: Build the graph only during initialization**

`AlsLocomotionGraphBuilder.Build()` creates a top-level state machine with `Grounded`, `JumpStart`, `FallLoop`, and `LandRecovery`; the Grounded node contains standing/crouching directional blend nodes. It resolves blend coordinates from compiled `AlsBlendDefinition` samples and returns cached `StringName`/`NodePath` handles. No string path construction is allowed after `Warmup()`.

- [ ] **Step 3: Implement controller update**

Expose:

```csharp
public void Warmup();
public void Apply(in AlsFrameResult result, double deltaTime);
public ulong ComputePoseDigest(long frameId);
```

`Apply()` writes actual stance, animation state, blend coordinates, stride, play rate, lean and phase through cached parameter handles, then manually advances exactly once. `LandRecovery` is selected while physical locomotion is Grounded and returns to Grounded only when `LandingRecoveryTime` expires.

- [ ] **Step 4: Run smoke twice for deterministic pose hashes**

Run the graph smoke twice and compare its final digest lines. Expected: identical digest and `GODOT_ALS_P3B_GRAPH_OK transitions=5`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Animation scenes/tests/p3b_animation_graph_smoke.tscn
git commit -m "feat: drive P3 locomotion animation graph"
```

### Task 4: Integrate Motor, Worker Model, and Visual Rig

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsP3Character.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Create: `src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs`
- Create: `scenes/tests/p3b_frame_order_smoke.tscn`

- [ ] **Step 1: Add a failing no-lag integration smoke**

For every frame, record command frame, motor snapshot frame, model result frame, and pose-advance frame. Assert all four IDs are equal, the first jump frame is already `InAir/JumpStart`, the first landing frame is already `Grounded/LandRecovery`, and generation replacement rejects the old result.

- [ ] **Step 2: Implement strict process ownership**

`AlsP3Character` owns the main-thread motor and prebuilt visual worker. Worker order `1` reads the current exchange slot, calls `AlsLocomotionModel.Evaluate()`, applies the controller, computes pose digest, and publishes a result. Commit order `2` validates identity/generation and updates only diagnostics/HUD state. Initialization completes before setting the worker process group.

- [ ] **Step 3: Add debug/release failure policy**

Headless/debug exceptions emit `GODOT_ALS_P3B_FAIL code=<stable code>` and exit nonzero. Interactive release freezes the last valid pose, leaves the motor in MotorDriven Grounded/InAir operation, and publishes one structured diagnostic per failure identity. No idle/walk fallback is permitted.

- [ ] **Step 4: Run the smoke in single and parallel modes**

Run both modes with one character. Expected: `lag=0`, `stale=0`, one rejected old-generation fixture, and identical pose/result digests.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p3b_frame_order_smoke.tscn
git commit -m "feat: integrate P3 motor model and visual rig"
```

### Task 5: Add the Playable Third-Person Demo

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs`
- Create: `src/Als.Godot/Locomotion/AlsOrbitCamera.cs`
- Create: `src/Als.Godot/Locomotion/AlsLocomotionHud.cs`
- Create: `src/Als.Godot/Locomotion/P3LocomotionDemo.cs`
- Create: `scenes/demo/p3_locomotion_demo.tscn`
- Modify: `project.godot`
- Modify: `scripts/verify-p0.ps1`

- [ ] **Step 1: Define input actions and adapter tests in a headless smoke mode**

Add actions for `move_left/right/forward/back`, `walk`, `sprint`, `crouch_toggle`, `jump`, `rotation_mode_toggle`, `aim`, and `mouse_capture_toggle`. The adapter maps them exactly to the design controls and restores the prior rotation mode after RMB aim release.

- [ ] **Step 2: Implement the minimal orbit camera and work field**

The scene includes the real Mannequin character, flat floor, jumpable low obstacle, motor ramp, directional light, `SpringArm3D` camera, and an unframed diagnostic HUD. Mouse movement changes yaw/pitch with pitch clamp; `Esc` toggles capture. Do not add stairs, moving platforms, Foot IK, tutorial text, or P4 camera behavior.

- [ ] **Step 3: Make the demo the project main scene**

Set:

```ini
[application]
config/name="Godot ALS"
run/main_scene="res://scenes/demo/p3_locomotion_demo.tscn"
```

All automated scripts continue passing explicit test scenes.

Change the P0 Godot invocation from an implicit main scene to:

```powershell
$godotOutput = & $GodotExecutable --headless --path $ProjectRoot `
    'res://scenes/tests/headless_smoke.tscn' 2>&1
```

Run `verify-p0.ps1` immediately after the change and require `P0_VERIFICATION_OK`.

- [ ] **Step 4: Run a 300-frame headless demo smoke**

Run: `& $godotExe --headless --path ${env:GODOT_ALS_ROOT} res://scenes/demo/p3_locomotion_demo.tscn -- --als-smoke-frames=300`

Expected: `GODOT_ALS_P3_DEMO_OK frames=300 errors=0` and exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/demo/p3_locomotion_demo.tscn project.godot scripts/verify-p0.ps1
git commit -m "feat: add playable P3 locomotion demo"
```

### Task 6: Add the 1/10 Character Real-Animation Gate

**Files:**
- Create: `src/Als.Godot/Locomotion/P3bAnimationHarness.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3bHarnessContext.cs`
- Create: `scenes/tests/p3b_animation_harness.tscn`
- Create: `scripts/verify-p3b.ps1`

- [ ] **Step 1: Write the matrix parser before the harness**

Require marker:

```powershell
$markerPattern = 'GODOT_ALS_P3B_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+) p95_us=(\d+) p99_us=(\d+)'
```

The script rejects digest/pose mismatch between modes, any nonzero error/allocation counter, wrong worker affinity, or fewer than 600 measured advances per character.

- [ ] **Step 2: Run and verify the missing harness fails**

Run: `pwsh -NoProfile -File scripts/verify-p3b.ps1 -GodotExecutable $godotExe -SkipRegression`

Expected: FAIL because scene/harness is missing.

- [ ] **Step 3: Implement the complete measurement harness**

Reuse the production `AlsP3Character`; do not create a test-only model or animation path. Use 120 warmup plus 600 measured frames, replace character zero once after warmup, count model/controller/skeleton/exchange/commit allocations separately, record worker elapsed ticks into fixed preallocated arrays, and calculate p95/p99 after processing stops.

- [ ] **Step 4: Run all four matrix rows**

Run: `pwsh -NoProfile -File scripts/verify-p3b.ps1 -GodotExecutable $godotExe -SkipRegression`

Expected: four P3B OK markers, exact single/parallel equality, and `P3B_VERIFICATION_OK`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p3b_animation_harness.tscn scripts/verify-p3b.ps1
git commit -m "test: add P3B real-animation matrix"
```

### Task 7: Run Full P3 Closure and Document It

**Files:**
- Modify: `scripts/verify-p3b.ps1`
- Create: `docs/architecture/p3-basic-locomotion.md`

- [ ] **Step 1: Chain all required regressions**

Without `-SkipRegression`, run `verify-p3a.ps1`, `verify-p2b.ps1`, `verify-p1.ps1`, `verify-p0.ps1`, then `dotnet test GodotALS.sln -c Release --no-restore`. Reject every `SCRIPT ERROR` or `ERROR:` line from Godot output.

- [ ] **Step 2: Record the final architecture and evidence contract**

Document profile stable IDs, animation graph, initialization/worker ownership, demo controls, four matrix outputs, allocation buckets, p95/p99, exact verification command, and explicit P4/P5/P6/P7 boundaries including P5B Overlay gameplay.

- [ ] **Step 3: Run the complete P3 gate**

Run: `pwsh -NoProfile -File scripts/verify-p3b.ps1 -GodotExecutable $godotExe`

Expected: `P3B_VERIFICATION_OK`, `P3A_VERIFICATION_OK`, `P2B_VERIFICATION_OK`, `P1_VERIFICATION_OK`, `P0_VERIFICATION_OK`, Release tests PASS, no thread/resource errors, and no steady-state managed allocations.

- [ ] **Step 4: Launch the interactive demo for manual acceptance**

Run: `& $godotExe --path ${env:GODOT_ALS_ROOT} --editor res://scenes/demo/p3_locomotion_demo.tscn`

Verify WASD, Alt, Shift, Ctrl, Space, V, RMB, mouse orbit and Esc; inspect Stand/Crouch/Walk/Run/Sprint/Jump/Fall/Land on the real Mannequin. This manual check supplements but does not replace Step 3.

- [ ] **Step 5: Check boundaries and commit**

Run: `git diff --check`

Run: `git status --short`

Expected: only intended documentation/gate changes, with no generated cache/build files.

```powershell
git add scripts/verify-p3b.ps1 docs/architecture/p3-basic-locomotion.md
git commit -m "docs: close P3 basic locomotion"
```

P3 is complete only when Step 3 passes from a clean feature worktree and the interactive scene in Step 4 is operational. P4 starts only after that evidence is preserved.
