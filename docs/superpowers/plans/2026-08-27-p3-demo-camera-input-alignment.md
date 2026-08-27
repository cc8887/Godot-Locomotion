# P3 Demo Camera, Input, and Mannequin Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正 P3 demo 中 Mannequin 展示朝向与脚部高度、active/spare 可见性、临时 orbit 构图，并用真实 `Camera3D`、正式动画资产和 single/parallel 运行时门禁证明 WASD、逻辑朝向和动画方向一致。

**Architecture:** 保持 `Als.Core`、motor、input adapter 和 resolver 的逻辑空间合同不变；把 UE CharacterMesh 的固定变换作为 profile schema v2 的只读 presentation 数据，在主线程预计算并由 Worker 以 `logical * presentation` 组合到完整 visual subtree。Worker 发布带 identity 的数值 visual snapshot，主线程 Commit 消费同一 identity 后才标记 visual-ready 并显示角色。测试分别锁定 expected transform、真实相机 basis、动画方向和 single/parallel digest，避免用两套相同错误实现互相证明。

**Tech Stack:** C# 12 / .NET 8、Godot 4.7.2 .NET、System.Numerics、xUnit、Pester 3.4.0、PowerShell 7、Git worktree

---

## File Map

| Path | Responsibility |
| --- | --- |
| `assets/config/p3_locomotion_profile.json` | schema v2 的 presentation 常量与既有 stable-ID 动画映射 |
| `scripts/generate-p3-profile.ps1` | 确定性生成 schema v2 profile；presentation 是经审计的显式常量 |
| `src/Als.Import/Compilation/AlsLocomotionAnimationProfile.cs` | 只读 `AlsPresentationDefinition` 与编译后 locomotion profile |
| `src/Als.Import/Compilation/AlsLocomotionProfileCompiler.cs` | 严格解析 schema v2、有限值和精确属性集合 |
| `tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs` | schema、presentation 负例和方向资产三元组合同 |
| `tests/Als.Import.Tests/P3RepositoryFixtures.cs` | profile 的结构化/原始 JSON 变异辅助 |
| `tests/GenerateP3Profile.Tests.ps1` | generator 顺序、字节确定性、presentation 与 stable ID 锁定 |
| `src/Als.Godot/Locomotion/AlsP3Presentation.cs` | presentation 构造、逻辑/视觉组合、有限值检查、数值 snapshot/digest |
| `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs` | 预计算 presentation、identity-bound candidate 及公开诊断合同 |
| `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs` | 初始化和逐帧应用 corrected visual transform，发布 candidate |
| `src/Als.Godot/Locomotion/AlsP3CommitStage.cs` | 消费同 identity candidate，提交诊断并通知角色 visual-ready |
| `src/Als.Godot/Locomotion/AlsP3Character.cs` | Active/processing/collision/Visible 分离和 visual-ready 生命周期 |
| `src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs` | active/spare 隐藏、replacement phase 和 visible-count 约束 |
| `src/Als.Godot/Locomotion/P3PresentationSmoke.cs` | 正式 Mannequin expected-transform 与首帧 rollback smoke |
| `scenes/tests/p3_presentation_smoke.tscn` | presentation headless 测试入口 |
| `src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs` | frame identity、replacement、rollback、ready/visibility 证明 |
| `src/Als.Godot/Locomotion/P3bAnimationHarness.cs` | result/pose/full-pose/root 四种聚合摘要 |
| `src/Als.Godot/Locomotion/AlsOrbitCamera.cs` | P3 临时 orbit follow offset；继续跟随 main-thread motor |
| `src/Als.Godot/Locomotion/P3DemoInputSmoke.cs` | 真实 `Camera3D.GlobalBasis` 的 12 组相机相对输入矩阵 |
| `src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs` | 四方向 pose 和三种 rotation mode 动画门禁 |
| `src/Als.Godot/Locomotion/P3LocomotionDemo.cs` | 300 帧 demo 的 ready/visible 完成条件 |
| `scripts/p3b-verification-functions.ps1` | 严格 marker/parser 和四摘要 pair 比较 |
| `scripts/verify-p3b.ps1` | focused smoke、1/10 角色矩阵和完整回归总门禁 |
| `tests/VerifyP3b.Tests.ps1` | parser、gate 顺序、marker 和失败分类测试 |
| `docs/architecture/p3-basic-locomotion.md` | 最终架构、线程所有权、实测结果和 P6 边界 |

## Fixed Decisions

- 不修改 `AlsPlayerInputAdapter`、`AlsLocomotionCommandResolver`、`AlsLocomotionModel`、WASD 映射或 Core yaw/blend 数学。
- 不翻转输入，不给 view/camera/motor yaw 增加 90 度补偿。
- presentation 固定为 Godot translation `(0,-0.92,0)`、yaw `-PI/2`，组合顺序固定为 `logical * presentation`。
- camera target 仍为 active character 的 `MovementAnchor`；`FollowOffset=(0,0.53,0)` 只保留旧 P3 构图，不是完整 ALS Camera 参数。
- Standing 没有纯 L/R clip；侧向由 LF/LB 与 RF/RB 边界样本混合。Crouching 才有独立 L/R。
- 完整 ALS Camera、AimOffset、Turn/Rotate in Place、Foot IK、Overlay、Mantle/Roll、Ragdoll/Get-up 继续按主规划后续阶段实现，不在本批次展开。

Task 0 建立隔离 checkout 后，每个新的 PowerShell 会话都先执行：

```powershell
Set-Location '../GodotALS-p3-direction-alignment'
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$godotExe = 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$worktreeRoot = (& git rev-parse --show-toplevel).Trim()
if ($worktreeRoot -cne '../GodotALS-p3-direction-alignment') {
    throw "Unexpected implementation worktree: $worktreeRoot"
}
```

### Task 0: Create an Isolated Worktree and Capture the Baseline

**Files:**
- Read only: `artifacts/diagnostics/p3-direction-before.png`
- Generated/ignored: `assets/generated/als_v4/**`, `.godot/**`, `artifacts/diagnostics/**`

- [ ] **Step 1: Invoke the required isolation skill**

Use `superpowers:using-git-worktrees` before any implementation edit. Create branch `fix/p3-direction-alignment` from the confirmed `main` commit at the verified-free path `../GodotALS-p3-direction-alignment`.

Expected: the implementation shell is inside the isolated worktree, not `.`, and `git status --short --branch` is clean.

- [ ] **Step 2: Provision the ignored generated asset batch into the worktree**

Run from `../GodotALS-p3-direction-alignment`:

```powershell
New-Item -ItemType Directory -Force .\assets\generated | Out-Null
New-Item -ItemType Directory -Force .\assets\generated\als_v4 | Out-Null
Copy-Item -Path '.\assets\generated\als_v4\*' `
  -Destination '.\assets\generated\als_v4' -Recurse -Force
Test-Path .\assets\generated\als_v4\als_manifest.json
```

Expected: `True`. Do not add generated assets or `.godot` import cache to Git.

- [ ] **Step 3: Record the current automated baseline**

Run:

```powershell
dotnet restore .\GodotALS.sln
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --editor --path $worktreeRoot --import --quit
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_demo_input_smoke.tscn
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_animation_graph_smoke.tscn
```

Expected current markers:

```text
GODOT_ALS_P3_DEMO_INPUT_OK actions=11 frames=8 camera=1 hud=1
GODOT_ALS_P3B_GRAPH_OK transitions=5 digest=3B75E5CD3AF16FEC
```

The graph digest above was observed on `main` during plan preparation; it is baseline evidence, not a post-change golden value.

Open `.\artifacts\diagnostics\p3-direction-before.png` and retain it as the visual baseline. Task 0 creates no commit.

### Task 1: Upgrade the Locomotion Profile to Schema v2

**Files:**
- Modify: `tests/GenerateP3Profile.Tests.ps1`
- Modify: `tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs`
- Modify: `tests/Als.Import.Tests/P3RepositoryFixtures.cs`
- Modify: `scripts/generate-p3-profile.ps1`
- Modify: `assets/config/p3_locomotion_profile.json`
- Modify: `src/Als.Import/Compilation/AlsLocomotionAnimationProfile.cs`
- Modify: `src/Als.Import/Compilation/AlsLocomotionProfileCompiler.cs`

- [ ] **Step 1: Write failing generator contract tests**

Extend `GenerateP3Profile.Tests.ps1` so the top-level order is exactly:

```text
schemaVersion,presentation,mannequin,standingIdle,crouchingIdle,standingSamples,crouchingSamples,jumpStart,fallLoop,land,leanAdditive
```

Require schema `2`, exact presentation child order `translationMeters,yawRadians`, translation `(0,-0.92,0)`, yaw within `1e-12` of `-[Math]::PI/2`, and generated bytes equal the tracked JSON. Add exact object-path/stable-ID/coordinate triples for standing F/B and crouching L/R.

Add this Pester 3.4-compatible body to the existing deterministic-output test after loading `$profile`:

```powershell
$profile.schemaVersion | Should Be 2
(@($profile.PSObject.Properties.Name) -join ',') | Should Be `
    'schemaVersion,presentation,mannequin,standingIdle,crouchingIdle,standingSamples,crouchingSamples,jumpStart,fallLoop,land,leanAdditive'
(@($profile.presentation.PSObject.Properties.Name) -join ',') | Should Be `
    'translationMeters,yawRadians'
(@($profile.presentation.translationMeters | ForEach-Object { [double]$_ }) -join ',') |
    Should Be '0,-0.92,0'
[Math]::Abs([double]$profile.presentation.yawRadians + ([Math]::PI / 2.0)) |
    Should BeLessThan 1e-12
[Convert]::ToBase64String([IO.File]::ReadAllBytes($first)) | Should Be `
    ([Convert]::ToBase64String([IO.File]::ReadAllBytes(
        (Join-Path $script:RepositoryRoot 'assets\config\p3_locomotion_profile.json'))))

$directionLocks = @(
    [pscustomobject]@{ Grid='standingSamples'; Index=0; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F.ALS_N_Walk_F'; Id='6124eafdcbeaaf04bca366add34c821faa0e4963'; X=0.0; Y=0.5 },
    [pscustomobject]@{ Grid='standingSamples'; Index=3; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_B.ALS_N_Walk_B'; Id='32fe18c71ccb860fe35c01d6b2b10fa2e4d98297'; X=0.0; Y=-0.5 },
    [pscustomobject]@{ Grid='crouchingSamples'; Index=1; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_L.ALS_CLF_Walk_L'; Id='21c24bd7df5192db2e2a860457f2b7b0681de41d'; X=-1.0; Y=0.0 },
    [pscustomobject]@{ Grid='crouchingSamples'; Index=2; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_R.ALS_CLF_Walk_R'; Id='db60b2c35ce5ef5216c782fc1f33549cbcf8278d'; X=1.0; Y=0.0 }
)
$manifest = Get-Content -LiteralPath $script:Manifest -Raw | ConvertFrom-Json
foreach ($lock in $directionLocks) {
    $asset = @($manifest.animations | Where-Object { $_.objectPath -ceq $lock.Path })
    $asset.Count | Should Be 1
    [string]$asset[0].id | Should Be $lock.Id
    $sample = @($profile.($lock.Grid))[$lock.Index]
    [string]$sample.animation | Should Be $lock.Id
    [double]$sample.x | Should Be $lock.X
    [double]$sample.y | Should Be $lock.Y
}
```

- [ ] **Step 2: Run the generator tests and observe RED**

Run:

```powershell
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\GenerateP3Profile.Tests.ps1' -EnableExit"
```

Expected: FAIL because the current generator/profile still emits schema 1 and has no `presentation`.

- [ ] **Step 3: Update the generator and regenerate the tracked profile**

In `generate-p3-profile.ps1`, emit the constant explicitly rather than deriving it from the manifest:

```powershell
$profile = [ordered]@{
    schemaVersion = 2
    presentation = [ordered]@{
        translationMeters = @([double]0.0, [double]-0.92, [double]0.0)
        yawRadians = [double](-[Math]::PI / 2.0)
    }
    mannequin = [string](Resolve-ExactManifestAsset $manifest 'skeletalMeshes' $mannequinPath).id
    standingIdle = [string](Resolve-ExactManifestAsset $manifest 'animations' $standingIdlePath).id
    crouchingIdle = [string](Resolve-ExactManifestAsset $manifest 'animations' $crouchingIdlePath).id
    standingSamples = $standingSamples
    crouchingSamples = $crouchingSamples
    jumpStart = [string](Resolve-ExactManifestAsset $manifest 'animations' $jumpStartPath).id
    fallLoop = [string](Resolve-ExactManifestAsset $manifest 'animations' $fallLoopPath).id
    land = [string](Resolve-ExactManifestAsset $manifest 'animations' $landPath).id
    leanAdditive = [string](Resolve-ExactManifestAsset $manifest 'blendSpaces' $leanAdditivePath).id
}
```

Run:

```powershell
pwsh -NoProfile -File .\scripts\generate-p3-profile.ps1
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\GenerateP3Profile.Tests.ps1' -EnableExit"
```

Expected: generator tests are GREEN immediately after the generator/tracked JSON change. Inspect the tracked JSON. Do not touch `p3_locomotion_settings.json`, reference locks, traces, manifests or export plans; their schema 1 contracts are unrelated.

- [ ] **Step 4: Write failing compiler tests**

Add a structured mutation helper for nested presentation changes, while keeping duplicate-key cases as raw string replacements because `JsonNode` de-duplicates properties. Tests must cover:

- compiled value `(0,-0.92,0)` and yaw `-MathF.PI/2` with float tolerance;
- v1 with presentation retained, so it fails as `ALSPROFILE003` rather than missing-property `ALSPROFILE007`;
- missing/non-object presentation, unknown/wrong-case/duplicate child properties;
- translation non-array, length 2/4, wrong element type and `1e100`;
- yaw wrong type and `1e100`.

Add these fixture helpers:

```csharp
public static string WithProfileMutation(Action<JsonObject> mutation) => Mutate(mutation);

public static string WithPresentationMutation(Action<JsonObject> mutation) =>
    Mutate(root => mutation(root["presentation"]!.AsObject()));
```

Add the positive and version tests exactly around the existing `CompileFailure()` helper:

```csharp
[Fact]
public void RepositoryPresentationCompilesToTheLockedGodotTransform()
{
    var profile = AlsLocomotionProfileCompiler.Compile(
        P3RepositoryFixtures.ReadProfile(), P3RepositoryFixtures.LoadAnimationSet());

    Assert.Equal(new System.Numerics.Vector3(0f, -0.92f, 0f),
        profile.Presentation.TranslationMeters);
    Assert.InRange(MathF.Abs(profile.Presentation.YawRadians + MathF.PI / 2f), 0f, 1e-6f);
}

[Fact]
public void SchemaVersionOneIsRejectedBeforePresentationCompilation()
{
    var json = P3RepositoryFixtures.WithProfileMutation(root => root["schemaVersion"] = 1);
    var exception = CompileFailure(json);

    Assert.Contains(exception.Issues, issue =>
        issue.Code == "ALSPROFILE003" && issue.FieldPath == "$.schemaVersion" &&
        issue.Expected == "2" && issue.Actual == "1");
}
```

Use this exact negative-case table; every row must assert both issue code and full field path:

| Mutation | Code | Path |
| --- | --- | --- |
| remove `presentation` | `ALSPROFILE007` | `$.presentation` |
| set presentation to `0` | `ALSPROFILE004` | `$.presentation` |
| duplicate raw `presentation` property | `ALSPROFILE006` | `$.presentation` |
| add `fallback` child | `ALSPROFILE005` | `$.presentation.fallback` |
| remove `translationMeters` child | `ALSPROFILE007` | `$.presentation.translationMeters` |
| remove `yawRadians` child | `ALSPROFILE007` | `$.presentation.yawRadians` |
| replace `yawRadians` with `YawRadians` | `ALSPROFILE005` | `$.presentation.YawRadians` |
| duplicate raw `yawRadians` property | `ALSPROFILE006` | `$.presentation.yawRadians` |
| set translation to `0` | `ALSPROFILE025` | `$.presentation.translationMeters` |
| set translation to `[0,-0.92]` | `ALSPROFILE026` | `$.presentation.translationMeters` |
| set translation to `[0,-0.92,0,0]` | `ALSPROFILE026` | `$.presentation.translationMeters` |
| set translation element 1 to `"bad"` | `ALSPROFILE013` | `$.presentation.translationMeters[1]` |
| set translation element 1 to `1e100` | `ALSPROFILE013` | `$.presentation.translationMeters[1]` |
| set yaw to `"bad"` | `ALSPROFILE013` | `$.presentation.yawRadians` |
| set yaw to `1e100` | `ALSPROFILE013` | `$.presentation.yawRadians` |

Construct duplicate cases from raw profile text with ordinal replacements of the generated `"presentation": {` and `"yawRadians": -1.5707963267948966` lines; do not use `JsonNode` for those rows.

Run:

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Debug `
  --filter FullyQualifiedName~AlsLocomotionProfileCompilerTests
```

Expected: FAIL because `Presentation` and schema v2 parsing do not exist.

- [ ] **Step 5: Implement the read-only profile value and strict parser**

In `AlsLocomotionAnimationProfile.cs` add:

```csharp
public readonly record struct AlsPresentationDefinition(
    System.Numerics.Vector3 TranslationMeters,
    float YawRadians);
```

Add `AlsPresentationDefinition Presentation` immediately after `MannequinMeshId` in the profile record. In the compiler set `SchemaVersion = 2`, add exact presentation property sets, parse it before stable IDs, and pass it into the record:

```csharp
private static readonly string[] PresentationProperties =
[
    "translationMeters",
    "yawRadians",
];

private static readonly HashSet<string> PresentationPropertySet =
    new(PresentationProperties, StringComparer.Ordinal);
```

Insert `"presentation"` immediately after `"schemaVersion"` in `RequiredProperties`. In `Compile()` add:

```csharp
var presentation = ReadPresentation(properties["presentation"], "$.presentation");
```

Return the profile with the complete constructor order:

```csharp
return new AlsLocomotionAnimationProfile(
    skeletonId,
    mannequinId,
    presentation,
    standingIdleId,
    crouchingIdleId,
    standingSamples,
    crouchingSamples,
    jumpStartId,
    fallLoopId,
    landId,
    leanSamples,
    leanBasePoseId,
    allAnimationIds);
```

```csharp
private static AlsPresentationDefinition ReadPresentation(JsonElement element, string path)
{
    var properties = ValidateObject(
        element, PresentationPropertySet, path, PresentationProperties);
    var translation = properties["translationMeters"];
    if (translation.ValueKind != JsonValueKind.Array)
    {
        throw Failure("ALSPROFILE025", $"{path}.translationMeters", "Expected an array.");
    }
    if (translation.GetArrayLength() != 3)
    {
        throw Failure(
            "ALSPROFILE026", $"{path}.translationMeters",
            "Presentation translation must contain exactly three values.",
            "3", translation.GetArrayLength().ToString());
    }
    return new AlsPresentationDefinition(
        new System.Numerics.Vector3(
            ReadFiniteSingle(translation[0], $"{path}.translationMeters[0]"),
            ReadFiniteSingle(translation[1], $"{path}.translationMeters[1]"),
            ReadFiniteSingle(translation[2], $"{path}.translationMeters[2]")),
        ReadFiniteSingle(properties["yawRadians"], $"{path}.yawRadians"));
}
```

Change `ReadFiniteSingle()`'s message from sample-specific wording to `Expected a finite single-precision value.`; retain `ALSPROFILE013`.

Run the filtered C# test immediately:

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Debug `
  --filter FullyQualifiedName~AlsLocomotionProfileCompilerTests
```

Expected: GREEN before proceeding to the aggregate schema gate.

- [ ] **Step 6: Prove the schema slice is deterministic and green**

Run:

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Debug `
  --filter FullyQualifiedName~AlsLocomotionProfileCompilerTests
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\GenerateP3Profile.Tests.ps1' -EnableExit"
$before = (Get-FileHash .\assets\config\p3_locomotion_profile.json -Algorithm SHA256).Hash
pwsh -NoProfile -File .\scripts\generate-p3-profile.ps1
$after = (Get-FileHash .\assets\config\p3_locomotion_profile.json -Algorithm SHA256).Hash
if ($before -cne $after) { throw 'profile generation is not idempotent' }
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
```

Expected: all tests pass, hashes match, build succeeds.

- [ ] **Step 7: Commit the complete schema vertical slice**

```powershell
git add assets/config/p3_locomotion_profile.json scripts/generate-p3-profile.ps1 `
  src/Als.Import/Compilation/AlsLocomotionAnimationProfile.cs `
  src/Als.Import/Compilation/AlsLocomotionProfileCompiler.cs `
  tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs `
  tests/Als.Import.Tests/P3RepositoryFixtures.cs tests/GenerateP3Profile.Tests.ps1
git commit -m "feat: add P3 presentation profile schema"
```

### Task 2: Apply `logical * presentation` and Publish Numerical Visual Evidence

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsP3Presentation.cs`
- Create: `src/Als.Godot/Locomotion/P3PresentationSmoke.cs`
- Create: `scenes/tests/p3_presentation_smoke.tscn`
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3Character.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`

- [ ] **Step 1: Write the failing expected-transform smoke**

Create `P3PresentationSmoke` using the real compiled animation set/profile and production `AlsP3Character`. Set the smoke root to main-thread process-group order 3 so assertions run after Motor order 0, Worker order 1 and Commit order 2. Add the same fixed floor used by frame-order smoke. In normal mode create three characters with initial logical yaw `0`, `+PI/2`, `-PI/2`, unique handles/exchange slots, and a fixed idle command source whose view yaw equals the initial yaw; call `SetActive(true)` after Configure and wait for all three to commit. Require:

```text
profile translation = (0,-0.92,0)
profile yaw = -PI/2
inactive real Skeleton3D derives imported local forward from Foot_L->ball_l and Foot_R->ball_r, then maps it to world -Z
logical origin Y=0.90 produces visual origin Y=-0.02
logical yaw 0,+PI/2,-PI/2 preserves the same local presentation relation
published candidate identity equals the committed frame identity
```

Calculate the expected transform independently in the same order-3 callback as `motor.GlobalTransform * context.PresentationTransform`, preventing a frame N diagnostics/frame N+1 motor comparison. Do not derive the expectation from the published snapshot. Add scene root script `GodotAls.Locomotion.P3PresentationSmoke`.

Run:

```powershell
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_presentation_smoke.tscn
```

Expected: FAIL because frame diagnostics has no visual snapshot/root digest and Worker still writes the logical transform directly.

- [ ] **Step 2: Add immutable visual contracts and presentation helpers**

Add the public numerical snapshot and internal candidate:

```csharp
public readonly record struct AlsP3VisualTransformSnapshot(
    NumericsVector3 BasisX,
    NumericsVector3 BasisY,
    NumericsVector3 BasisZ,
    NumericsVector3 Origin);

internal readonly record struct AlsP3VisualCommitCandidate(
    AlsFrameIdentity Identity,
    AlsP3VisualTransformSnapshot RootTransform,
    ulong PoseDigest,
    ulong FullPoseDigest,
    ulong RootDigest);
```

Append `VisualRootTransform` and `RootDigest` to `AlsP3FrameDiagnostics`. `AlsP3Presentation` must expose allocation-free helpers to:

- create `Transform3D` from `AlsPresentationDefinition`;
- convert the established `System.Numerics.Matrix4x4` logical transform to Godot;
- compose `ToGodot(logical) * presentation`;
- reject any non-finite basis/origin component;
- capture the numerical snapshot and compute the same quantized FNV digest currently used by Worker.

- [ ] **Step 3: Precompute presentation on the main thread**

In `AlsP3RuntimeContext` constructor, after assigning `Profile`, set:

```csharp
PresentationTransform = AlsP3Presentation.Create(profile.Presentation);
```

Expose it as a get-only property. All three existing context construction sites already pass a complete profile and need no constructor change.

- [ ] **Step 4: Establish the corrected rollback baseline before threading starts**

Change Worker configuration to:

```csharp
internal void Configure(
    AlsP3RuntimeContext context,
    AlsP3CharacterState state,
    in Transform3D initialLogicalTransform)
```

After the rig, graph, controller and pose arrays are ready, but before assigning `ProcessThreadGroup`, execute:

```csharp
var correctedRoot = AlsP3Presentation.Compose(
    initialLogicalTransform, context.PresentationTransform);
AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
_visualRoot.GlobalTransform = correctedRoot;
AlsP3Presentation.ThrowIfNonFinite(_visualRoot.GlobalTransform);
CapturePose();
```

Pass `_motor.GlobalTransform` from `AlsP3Character.Configure()`. This initial application does not mark the character ready.

- [ ] **Step 5: Apply and publish the corrected transform every Worker frame**

Replace the direct logical assignment with:

```csharp
var correctedRoot = AlsP3Presentation.Compose(
    input.CharacterTransform, _context.PresentationTransform);
AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
_visualRoot!.GlobalTransform = correctedRoot;
_controller!.Apply(_result, input.DeltaTime);
var appliedRoot = _visualRoot.GlobalTransform;
AlsP3Presentation.ThrowIfNonFinite(appliedRoot);
var poseDigest = _controller.ComputePoseDigest(frameId);
var fullPoseDigest = ComputeFullPoseDigest();
var rootDigest = AlsP3Presentation.ComputeDigest(appliedRoot);

var candidate = new AlsP3VisualCommitCandidate(
    _result.Identity,
    AlsP3Presentation.Capture(appliedRoot),
    poseDigest,
    fullPoseDigest,
    rootDigest);
_state.PublishResult(_result, candidate, _result.Identity.FrameId, frameId);
```

`AlsP3CharacterState.PublishResult()` writes `ModelResultFrameId`, `PoseAdvanceFrameId` and the complete candidate first, then calls the existing `ExchangeSlot.PublishResult()` last as the release publication. Commit reads it only after `TryConsumeResult(identity)` succeeds and rejects candidate identity mismatch. Do not allocate strings/arrays/collections and do not read Worker-owned Nodes from the main thread.

- [ ] **Step 6: Add a first-frame failure/rollback case without a production failure hook**

Add `--als-failure-policy=initial` to `P3PresentationSmoke`. During `_Ready()`, after Configure, `SetActive(true)` and before the first physics frame, capture the corrected root/skeleton while no Worker callback has run, then free the test character's `AnimationTree`. Construct this intentional-failure context in parallel mode with `headlessOrDebug: false`. Wait until `FailureDiagnosticCount > 0`, `IsPoseFrozen` and `WorkerInFlight == 0`; then prove the first Worker frame restored the corrected baseline and remained uncommitted. At this task boundary it emits:

```text
GODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 full_pose=[0-9A-F]{16} root=[0-9A-F]{16}
```

Any direct Node inspection in this fixture is allowed only before process groups start, or after Worker suspension and `WorkerInFlight == 0`.

- [ ] **Step 7: Run focused presentation verification**

```powershell
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_presentation_smoke.tscn
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_presentation_smoke.tscn -- `
  --als-failure-policy=initial
```

Expected:

```text
GODOT_ALS_P3_PRESENTATION_OK yaws=3 identity=1 root=[0-9A-F]{16}
GODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 full_pose=[0-9A-F]{16} root=[0-9A-F]{16}
```

- [ ] **Step 8: Commit the presentation vertical slice**

```powershell
git add src/Als.Godot/Locomotion/AlsP3Presentation.cs `
  src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs `
  src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs `
  src/Als.Godot/Locomotion/AlsP3Character.cs `
  src/Als.Godot/Locomotion/AlsP3CommitStage.cs `
  src/Als.Godot/Locomotion/P3PresentationSmoke.cs `
  scenes/tests/p3_presentation_smoke.tscn
git commit -m "fix: apply P3 mannequin presentation"
```

### Task 3: Gate Visibility on a Committed Visual-Ready Identity

**Files:**
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3Character.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs`
- Modify: `src/Als.Godot/Locomotion/P3PresentationSmoke.cs`
- Modify: `src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs`
- Modify: `src/Als.Godot/Locomotion/P3LocomotionDemo.cs`

- [ ] **Step 1: Write failing lifecycle and replacement assertions**

Extend the smokes to prove:

- character is invisible before `AddChild()`/Configure and before its first valid commit;
- an active character becomes visible only when its current-generation candidate is committed;
- inactive spare is never visible;
- every replacement phase has `VisibleCharacterCount <= 1`;
- recovery has at least one frame with zero visible characters;
- retired character hides before release;
- generation mismatch resets ready and cannot reveal stale pose;
- an initial Worker failure leaves `IsVisualReady=false` and `Visible=false`.

After adding lifecycle diagnostics, extend the initial-failure marker to the final form:

```text
GODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 visual_ready=0 visible=0 full_pose=[0-9A-F]{16} root=[0-9A-F]{16}
```

Run single and parallel frame-order scenes. Expected RED: current `SetActive(false)` does not hide a Node3D and diagnostics do not expose ready/visibility.

- [ ] **Step 2: Extend lifecycle and slot diagnostics**

Use these public contracts:

```csharp
public readonly record struct AlsP3LifecycleDiagnostics(
    bool IsDisposed,
    bool IsActive,
    bool HasCollision,
    bool HasProcessing,
    bool IsVisible,
    bool IsVisualReady);

public enum AlsP3ReplacementPhase : byte
{
    None,
    AwaitingRetiredResult,
    AwaitingGenerationMismatch,
    AwaitingRecoveryCommit,
    Complete,
}
```

Append `Phase` and `VisibleCharacterCount` to `AlsP3SlotReplacementDiagnostics`. Replace the slot's private enum with the public one.

- [ ] **Step 3: Implement main-thread ownership of readiness and visibility**

Set `Visible = false` in `AlsP3Character`'s constructor. Also set it in the slot object initializer before `AddChild()` to lock the no-flash creation boundary.

Add `int VisualReady` to character state. `ResetVisualReady()` must clear it and hide on the main thread. `SetActive(false)` hides and resets readiness before disabling processing/collision; reactivation therefore always waits for a new valid commit. `SetActive(true)` restores processing/collision but stays hidden until that commit. `DisposeRuntimeCore()` unconditionally sets local `Visible=false` and `VisualReady=0`, including partial Configure failure. `LifecycleDiagnostics.IsVisible` reports local `Visible`, not `IsVisibleInTree()`, so a hidden parent cannot mask an erroneous child state.

Change commit configuration to accept its owner:

```csharp
internal void Configure(
    AlsP3RuntimeContext context,
    AlsP3CharacterState state,
    AlsP3Character owner)
```

After result/candidate identity and same-frame checks succeed, publish in this order:

1. copy target yaw and full diagnostics;
2. `Volatile.Write(ref _state.VisualReady, 1)`;
3. `Volatile.Write(ref _state.CommittedFrameId, frameId)` as the public diagnostics release fence;
4. call `owner.ShowCommittedVisual(identity)`.

`ShowCommittedVisual()` verifies main thread, active flag, current handle generation, matching committed frame and `VisualReady==1`, then sets only `Visible=true`. Worker must never set `Visible`.

- [ ] **Step 4: Enforce slot invariants at every transition**

In `AlsP3CharacterSlot`:

- hide old active before retirement/free;
- keep prebuilt spare hidden through classification and recovery;
- count `_active` and `_spare` local `Visible` flags directly, without `GetChildren()` enumeration or per-frame allocation;
- throw in headless/debug if the count exceeds one, if an inactive child is visible, or if a visible child is not ready;
- explicitly allow count zero during `AwaitingGenerationMismatch` and `AwaitingRecoveryCommit`.

`StartReplacementClassification()` explicitly calls `ResetVisualReady()` before `SetActive(true)`. Before assigning the spare into `_active`, assert that the retired local variable is already hidden. Teardown always hides both active and spare before disposal.

Do not add motor position/velocity transfer; camera/world continuity during replacement remains outside this batch.

- [ ] **Step 5: Remove unsafe steady-state test reads of Worker-owned Nodes**

Change normal `P3bFrameOrderSmoke` checks to consume `Diagnostics.VisualRootTransform` and `RootDigest`. In `P3bFrameOrderSmoke._Ready()`, change its own `ProcessThreadGroupOrder` from 0 to 3; `AlsP3CharacterSlot` already explicitly owns main-thread order 2, so replacement choreography remains unchanged. Require `WorkerInFlight == 0` before inspecting/freeing the visual `AnimationTree`; return immediately so the next physics frame performs the expected failure. Do not add a production test hook.

- [ ] **Step 6: Run lifecycle verification**

```powershell
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_presentation_smoke.tscn
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_presentation_smoke.tscn -- `
  --als-failure-policy=initial
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_frame_order_smoke.tscn -- `
  --als-mode=single
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_frame_order_smoke.tscn -- `
  --als-mode=parallel
& $godotExe --headless --path $worktreeRoot res://scenes/demo/p3_locomotion_demo.tscn -- `
  --als-smoke-frames=300
```

Expected: normal presentation smoke passes; initial-failure marker includes `visual_ready=0 visible=0`. Final fixed-field marker forms are:

```text
GODOT_ALS_P3B_FRAME_ORDER_OK mode=(single|parallel) frames=180 digest=[0-9A-F]{16} pose=[0-9A-F]{16} full_pose=[0-9A-F]{16} root=[0-9A-F]{16} lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 recovery_zero_visible=1
GODOT_ALS_P3_DEMO_OK frames=300 errors=0 ready=1 visible=1 max_visible=1
```

- [ ] **Step 7: Commit the lifecycle fix**

```powershell
git add src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs `
  src/Als.Godot/Locomotion/AlsP3Character.cs `
  src/Als.Godot/Locomotion/AlsP3CommitStage.cs `
  src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs `
  src/Als.Godot/Locomotion/P3PresentationSmoke.cs `
  src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs `
  src/Als.Godot/Locomotion/P3LocomotionDemo.cs
git commit -m "fix: gate P3 visibility on committed visuals"
```

### Task 4: Make Result, Pose, Full-Pose, and Root Digests Unambiguous

**Files:**
- Modify: `tests/VerifyP3b.Tests.ps1`
- Modify: `scripts/p3b-verification-functions.ps1`
- Modify: `src/Als.Godot/Locomotion/P3bAnimationHarness.cs`
- Modify: `src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs`

- [ ] **Step 1: Write failing parser and parity tests**

Change the valid harness marker fixture to:

```text
GODOT_ALS_P3B_OK mode=single characters=1 warmup=120 frames=600 digest=0123456789ABCDEF pose=1111111111111111 full_pose=2222222222222222 root=3333333333333333 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0 p95_us=100 p99_us=200
```

Require exact order/no trailing fields and independent rejection for mismatched `Digest`, `Pose`, `FullPose` and `Root`. Add equivalent frame-order fixtures for the exact marker defined in Task 3. Replace the old source test that required `FullPoseDigest` to be aggregated into `pose=`.

Run:

```powershell
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\VerifyP3b.Tests.ps1' -EnableExit"
```

Expected: FAIL because parser/result objects only know two digests.

- [ ] **Step 2: Split all four harness aggregators**

In both C# smokes keep separate fields and append exactly:

```csharp
AlsResultDigest.Append(ref _resultDigest, diagnostics.Result);
Append(ref _poseDigest, diagnostics.PoseDigest);
Append(ref _fullPoseDigest, diagnostics.FullPoseDigest);
Append(ref _rootDigest, diagnostics.RootDigest);
```

Emit exact labels `digest=`, `pose=`, `full_pose=`, `root=` in that order. Do not rename `PoseDigest` or `FullPoseDigest`; the marker now states which one is aggregated.

- [ ] **Step 3: Update the strict parser and pair comparison**

Extend `ConvertFrom-P3bHarnessOutput`'s anchored regex, capture indices and returned object with `FullPose` and `Root`. `Assert-P3bResultPair` must compare all four fields independently. Add `ConvertFrom-P3bFrameOrderOutput` and `Assert-P3bFrameOrderPair`: the parser locks mode, `frames=180`, four summaries, `lag=0 stale=0 generation=1`, replacement evidence and visibility evidence; the pair assertion independently compares result/pose/full-pose/root. Keep all existing counter, allocation, affinity and percentile checks unchanged.

- [ ] **Step 4: Run RED-to-GREEN verification**

```powershell
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\VerifyP3b.Tests.ps1' -EnableExit"
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_frame_order_smoke.tscn -- `
  --als-mode=single
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_frame_order_smoke.tscn -- `
  --als-mode=parallel
```

Parse both frame-order outputs and call `Assert-P3bFrameOrderPair`. Expected: Pester passes and both modes emit four 16-hex summaries whose result/pose/full-pose/root values match pairwise.

- [ ] **Step 5: Commit the diagnostic contract**

```powershell
git add tests/VerifyP3b.Tests.ps1 scripts/p3b-verification-functions.ps1 `
  src/Als.Godot/Locomotion/P3bAnimationHarness.cs `
  src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs
git commit -m "test: publish P3 corrected root parity evidence"
```

### Task 5: Align the P3 Orbit Composition and Prove Camera-Relative WASD

**Files:**
- Modify: `src/Als.Godot/Locomotion/P3DemoInputSmoke.cs`
- Modify: `src/Als.Godot/Locomotion/AlsOrbitCamera.cs`

- [ ] **Step 1: Write failing camera offset and 12-direction tests**

Convert `P3DemoInputSmoke` to this explicit async exception boundary so synthetic events cross Godot's real input dispatch without losing failures after an `await`:

```csharp
public override async void _Ready()
{
    try
    {
        await RunSmokeAsync();
        _finished = true;
        GetTree().Quit();
    }
    catch (Exception exception)
    {
        _finished = true;
        GD.PushError($"GODOT_ALS_P3_DEMO_INPUT_FAIL {exception}");
        GetTree().Quit(1);
    }
}

private async Task RunSmokeAsync()
{
    ValidateActionMap();
    ValidateCommandSemantics();
    var evidence = await ValidateCameraRelativeMovementMatrixAsync();
    ValidateHudSemantics();
    Require(typeof(P3LocomotionDemo).IsSubclassOf(typeof(Node3D)),
        "demo root was not a Node3D production scene root");
    GD.Print(
        $"GODOT_ALS_P3_DEMO_INPUT_OK actions={ControlledActions.Length} " +
        $"directions={evidence.DirectionCount} camera_basis={evidence.CameraBasisChecks} " +
        $"pitch={evidence.PitchChecks} aiming={evidence.AimingChecks} " +
        $"cleared={evidence.ClearChecks} hud=1");
}

private readonly record struct InputSmokeEvidence(
    int DirectionCount,
    int CameraBasisChecks,
    int PitchChecks,
    int AimingChecks,
    int ClearChecks);
```

Replace the old synchronous `ValidateGodotInputBridge()` and `ValidateCameraSemantics()` calls with this single async helper, while preserving their action-map, pitch-clamp, capture-toggle and target-follow assertions inside the new flow. Add a hard assertion that `FollowOffset == (0,0.53,0)`.

For camera yaw `0`, `+PI/2`, `-PI/2`, test actions `move_forward`, `move_left`, `move_back`, `move_right`. For every case:

1. set captured mouse mode;
2. compute `deltaYaw = Mathf.Wrap(targetYaw - orbit.Yaw, -Mathf.Pi, Mathf.Pi)` and dispatch `Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(-deltaYaw / orbit.MouseSensitivity, 0f) })`;
3. await one process frame so `_UnhandledInput` and transforms settle;
4. `Input.ActionPress(action)`;
5. call `AlsPlayerInputAdapter.CaptureGodotFrame(frameId, orbit.Yaw)` and the production resolver;
6. read expected horizontal vectors from the real `Camera3D.GlobalBasis`;
7. release the action in `finally`.

Use this independent expectation:

```csharp
var forward = -camera.GlobalBasis.Z;
forward.Y = 0f;
forward = forward.Normalized();
var right = camera.GlobalBasis.X;
right.Y = 0f;
right = right.Normalized();
var basisYaw = Mathf.Atan2(forward.X, -forward.Z);
Require(Mathf.Abs(Mathf.Wrap(orbit.Yaw - basisYaw, -Mathf.Pi, Mathf.Pi)) < 1e-4f,
    "orbit yaw did not match the real Camera3D basis");
```

Convert expected Godot vectors explicitly before comparison:

```csharp
private static NumericsVector3 ToNumerics(in Vector3 value) =>
    new(value.X, value.Y, value.Z);

Require(NumericsVector3.Distance(
    resolved.WorldDirection, ToNumerics(expectedDirection)) < 1e-4f,
    $"camera-relative movement mismatch for yaw={targetYaw:R} action={action}");
```

Expected directions are W=`forward`, A=`-right`, S=`-forward`, D=`right`. Before the yaw matrix, dispatch a separate pitch-only event with `Relative = new Vector2(0f, 80f)`, await `ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame)`, and prove `Mathf.Abs((-camera.GlobalBasis.Z).Y) > 0.01f` before flattening. After the 12 cases, capture one zero-input frame and one `aim` + W frame; the latter must remain equal to camera forward and request `Aiming`.

Wrap the entire helper in `try/finally`: save the prior `Input.MouseMode`, release the currently pressed action in each case's own `finally`, release every `ControlledActions` entry in the outer `finally`, restore mouse mode, remove/free orbit and target, and then assert no controlled action leaked. `DirectionCount` increments once per completed yaw/action case and must equal 12. `CameraBasisChecks`, `PitchChecks`, `AimingChecks` and `ClearChecks` are binary category evidence: set each to 1 only after every assertion in that category passes. The success marker interpolates these measured values rather than hardcoding them.

- [ ] **Step 2: Run and observe RED**

```powershell
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_demo_input_smoke.tscn
```

Expected: FAIL first on current offset `(0,1.45,0)`; after that assertion is bypassed, the old smoke still lacks the required 12-case evidence.

- [ ] **Step 3: Make the single production change**

In `AlsOrbitCamera` change only:

```csharp
public Vector3 FollowOffset { get; } = new(0f, 0.53f, 0f);
```

Keep `P3LocomotionDemo.EnsureCameraTarget()` and `ValidateCameraFollow()` unchanged; they already require the active `MovementAnchor`. Do not change SpringArm length/FOV or `project.godot` input definitions.

- [ ] **Step 4: Run the completed real-input smoke**

```powershell
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3_demo_input_smoke.tscn
```

Expected exact marker:

```text
GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1
```

- [ ] **Step 5: Commit camera composition and its production-path test**

```powershell
git add src/Als.Godot/Locomotion/AlsOrbitCamera.cs `
  src/Als.Godot/Locomotion/P3DemoInputSmoke.cs
git commit -m "fix: align P3 orbit and camera-relative input"
```

### Task 6: Lock Directional Assets, Poses, and Rotation Modes

**Files:**
- Modify: `tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs`
- Modify: `src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs`

- [ ] **Step 1: Write the exact directional asset test**

For every sample, resolve the compiled animation ID back through `AlsAnimationSetDefinition.Animations` and assert object path, stable ID and coordinates together. Lock:

| Sample | Complete object path | Stable ID | Coordinate |
| --- | --- | --- | --- |
| Standing F | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F.ALS_N_Walk_F` | `6124eafdcbeaaf04bca366add34c821faa0e4963` | `(0,0.5)` |
| Standing B | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_B.ALS_N_Walk_B` | `32fe18c71ccb860fe35c01d6b2b10fa2e4d98297` | `(0,-0.5)` |
| Standing LF | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_LF.ALS_N_Walk_LF` | `44a7f89b2c1dac832ca63753c131a037420f9d7e` | `(-0.353553,0.353553)` |
| Standing LB | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_LB.ALS_N_Walk_LB` | `a4c6e0e455e7be7355cdd7c3ce49272d07773b18` | `(-0.353553,-0.353553)` |
| Standing RF | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_RF.ALS_N_Walk_RF` | `fc2d3a4142a1bd82d20877d806c783ff56dfe688` | `(0.353553,0.353553)` |
| Standing RB | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_RB.ALS_N_Walk_RB` | `eb84a748fee4615754ce3cbcd3c259b33918b935` | `(0.353553,-0.353553)` |
| Crouch L | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_L.ALS_CLF_Walk_L` | `21c24bd7df5192db2e2a860457f2b7b0681de41d` | `(-1,0)` |
| Crouch R | `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_R.ALS_CLF_Walk_R` | `db60b2c35ce5ef5216c782fc1f33549cbcf8278d` | `(1,0)` |

Use `Assert.Equal(expectedPath, definition.Animations[sample.AnimationId].ObjectPath)`; an `EndsWith` assertion is insufficient.

- [ ] **Step 2: Write failing direction/rotation-mode graph cases**

Extend `VerifyGaitBlendMapping()` or add adjacent `VerifyDirectionMatrix()` with production model/controller flow:

- LookingDirection W/A/S/D -> standing-run coordinates `(0,1)`, `(-1,0)`, `(0,-1)`, `(1,0)`;
- Aiming uses `viewYaw == aimYaw`, starts character yaw away from that value, and iterates production model frames until normalized yaw error is below `1e-3f`; W/A/S/D world movement remains camera-relative, target yaw converges to aim yaw, and after convergence the local graph coordinates are `(0,1)`, `(-1,0)`, `(0,-1)`, `(1,0)`;
- VelocityDirection iterates each of four world directions for at most 240 fixed frames, updating frame identity, command rotation mode, `FrameInput.RotationMode`, `FrameInput.CharacterYaw`, `CharacterTransform`, `ViewRotation` and `AimRotation` every frame; after normalized yaw error is below `1e-3f`, require `abs(blend.X) < 1e-3f` and `blend.Y > 0`;
- Jump/Fall/Land transition count remains five.

Both Aiming and VelocityDirection loops keep one mutable `characterYaw`. Each frame writes that same value into `FrameInput.CharacterYaw` and a `CharacterTransform` basis constructed from it; after `AlsLocomotionModel.Evaluate()` the loop feeds `result.TargetYaw` back into `characterYaw` for the next frame. This prevents a test-only mismatch between the scalar yaw and transform basis.

For F/B/left-side/right-side pose evidence, build a fresh library/graph/controller per case, `AddChild(library.Root)`, warm it, apply eight fixed frames at `AnimationPhase=0.375f`, then compute every pose digest with the same digest frame ID `777`. Dispose controller, graph and library in that order through their existing `using` scopes. Require four pairwise-distinct pose digests, append them in F/B/left/right order into a returned `directionDigest`, and do not reuse a stateful controller across cases.

- [ ] **Step 3: Run the focused tests before changing production code**

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj -c Release `
  --filter FullyQualifiedName~RepositoryProfileLocksDirectionalObjectPathsStableIdsAndCoordinates
dotnet build .\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental
& $godotExe --headless --path $worktreeRoot res://scenes/tests/p3b_animation_graph_smoke.tscn
```

Expected: the import mapping test passes once added against the already-correct profile; graph smoke initially fails until the characterization matrix is complete. This task is test-only. If the completed production-path fixture demonstrates a real controller/graph defect, stop this task, record the failing case and add a separately reviewed minimal-fix task before changing any production file. Do not alter Core direction math to satisfy a mistaken standing pure-L/R assumption.

- [ ] **Step 4: Require deterministic directional pose evidence**

Change `VerifyDirectionMatrix()` to return `directionDigest`. Run the graph smoke twice, extract its anchored marker, and require both `direction_digest` and the existing final `digest` to match. Expected form:

```text
GODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=[0-9A-F]{16} digest=[0-9A-F]{16}
```

- [ ] **Step 5: Commit the animation-direction lock**

```powershell
git add tests/Als.Import.Tests/AlsLocomotionProfileCompilerTests.cs `
  src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs
git commit -m "test: lock P3 directional animation behavior"
```

### Task 7: Add Every Focused Smoke to the Persistent P3B Gate

**Files:**
- Modify: `tests/VerifyP3b.Tests.ps1`
- Modify: `scripts/verify-p3b.ps1`
- Modify: `scripts/p3b-verification-functions.ps1`
- Modify: `docs/architecture/p3-basic-locomotion.md`

- [ ] **Step 1: Write failing verifier-order and exact-marker tests**

Require `verify-p3b.ps1`, including `-SkipRegression`, to run these before the 1/10-character matrix:

1. `p3_demo_input_smoke.tscn`;
2. `p3b_animation_library_smoke.tscn`;
3. `p3_presentation_smoke.tscn` normal and initial-failure modes;
4. `p3b_animation_graph_smoke.tscn` twice with equal digest;
5. `p3b_frame_order_smoke.tscn` single and parallel;
6. `p3_locomotion_demo.tscn -- --als-smoke-frames=300`.

Pester must reject nonzero exit, `SCRIPT ERROR:`/`ERROR:`, any `GODOT_ALS_P3B_FAIL`/`GODOT_ALS_P3_DEMO_INPUT_FAIL`, missing/duplicate/malformed markers, wrong order, either graph digest mismatch, any frame-order digest mismatch, missing visibility evidence and any omitted scene.

- [ ] **Step 2: Run the verifier tests and observe RED**

```powershell
pwsh -NoProfile -Command "Import-Module Pester; Invoke-Pester '.\tests\VerifyP3b.Tests.ps1' -EnableExit"
```

Expected: FAIL because the current verifier only runs the animation harness matrix and downstream P3A chain.

- [ ] **Step 3: Implement one strict scene-gate helper**

Add an `Invoke-P3bSceneGate` helper with `ExpectedExactMarkers` and `ExpectedRegexMarkers` arrays. It captures all streams, requires exit zero, rejects engine/script error and ALS failure marker lines, and requires every expected marker exactly once. Multiple evidence markers from one scene are valid; an unexpected duplicate is not. Use it for every scene instead of duplicating loose output handling.

Lock existing library evidence exactly:

```text
GODOT_ALS_P3B_LIBRARY_OK bones=68 clips=28 skeletons=1
GODOT_ALS_P3B_LIBRARY_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1
```

Lock the graph lifecycle marker exactly and its result with an anchored regex:

```text
GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1 borrowed=1
\AGODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=([0-9A-F]{16}) digest=([0-9A-F]{16})\z
```

The other final gate markers are:

```text
GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1
\AGODOT_ALS_P3_PRESENTATION_OK yaws=3 identity=1 root=([0-9A-F]{16})\z
\AGODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 visual_ready=0 visible=0 full_pose=([0-9A-F]{16}) root=([0-9A-F]{16})\z
\AGODOT_ALS_P3B_FRAME_ORDER_OK mode=(single|parallel) frames=180 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) full_pose=([0-9A-F]{16}) root=([0-9A-F]{16}) lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 recovery_zero_visible=1\z
GODOT_ALS_P3_DEMO_OK frames=300 errors=0 ready=1 visible=1 max_visible=1
```

Run graph twice and compare both capture groups. Parse frame-order single/parallel with `ConvertFrom-P3bFrameOrderOutput` and call `Assert-P3bFrameOrderPair`; merely checking that both markers exist is insufficient.

Use regex only for measured 16-hex digests and timing values; all fixed fields remain exact. Keep the focused completion marker distinct from full verification:

```text
P3B_FOCUSED_VERIFICATION_OK regression=skipped
P3B_VERIFICATION_OK
```

- [ ] **Step 4: Run focused closure and fix only evidenced failures**

```powershell
pwsh -NoProfile -File .\scripts\verify-p3b.ps1 `
  -GodotExecutable $godotExe -ProjectRoot $worktreeRoot -SkipRegression
```

Expected: every focused scene marker, four P3B single/parallel rows for 1 and 10 characters, zero missing/stale/generation/lag/allocation counters, matching result/pose/full-pose/root digests, then the focused completion marker.

- [ ] **Step 5: Update the architecture record with measured evidence**

Document:

- profile schema v2 and fixed presentation source;
- `logical * presentation` ownership and finite-value failure path;
- visual candidate publication/commit order;
- Active/Visible/visual-ready/replacement-zero-window contract;
- `digest/pose/full_pose/root` marker meanings;
- real Camera3D 12-direction test and temporary `0.53 m` P3 offset;
- directional stable IDs/coordinates and measured smoke output;
- exact boundary that keeps full ALS Camera in P6.

Record the actual focused verifier markers from this run; do not retain stale digest values from the pre-fix baseline.

- [ ] **Step 6: Commit the persistent closure gate and documentation**

```powershell
git add tests/VerifyP3b.Tests.ps1 scripts/verify-p3b.ps1 `
  scripts/p3b-verification-functions.ps1 docs/architecture/p3-basic-locomotion.md
git commit -m "test: close P3 direction alignment gates"
```

- [ ] **Step 7: Run the full clean-worktree regression gate**

The full verifier intentionally rejects a dirty worktree, so run it only after the preceding commit:

```powershell
pwsh -NoProfile -File .\scripts\verify-p3b.ps1 `
  -GodotExecutable $godotExe -ProjectRoot $worktreeRoot
```

Expected evidence includes all focused markers, the 1/10-character single/parallel matrix, `P3A_VERIFICATION_OK`, `P2B_VERIFICATION_OK`, `P1_VERIFICATION_OK`, `P0_VERIFICATION_OK`, Release tests, repository closure, and exactly one final `P3B_VERIFICATION_OK`.

### Task 8: Perform Actual-Window Acceptance and Finish the Branch

**Files:**
- Generated/ignored: `artifacts/diagnostics/p3-direction-after.png`

- [ ] **Step 1: Launch the actual demo window**

```powershell
& $godotExe --path $worktreeRoot res://scenes/demo/p3_locomotion_demo.tscn
```

- [ ] **Step 2: Perform the keyboard/mouse acceptance matrix**

Verify in the rendered window:

- one rig only, initial camera behind the character, feet at ground height;
- yaw 0/90/180 degrees: W depth-forward, S toward camera, A/D screen-left/right;
- LookingDirection, VelocityDirection and RMB Aiming remain visibly distinct;
- Walking, Running, Sprinting, Crouching, Jump, Fall and Land animation direction matches displacement;
- replacement may show the declared short zero-visible window, but never raw/stale/double rigs;
- HUD reports zero runtime/thread/missing/stale/generation errors.

Save the after screenshot to `../GodotALS-p3-direction-alignment\artifacts\diagnostics\p3-direction-after.png` and compare it with `.\artifacts\diagnostics\p3-direction-before.png`. Keep both artifacts out of Git.

- [ ] **Step 3: Invoke verification-before-completion and inspect repository state**

Use `superpowers:verification-before-completion`, then run:

```powershell
git status --short --branch
git log --oneline main..HEAD
```

Expected: clean feature branch with the planned commits and no generated asset/cache/artifact entries.

- [ ] **Step 4: Request an implementation review**

Use `superpowers:requesting-code-review`. Review priorities are matrix order, cross-thread publication identity, no Worker-to-main Node reads, replacement visibility, parser exactness and absence of accidental Core/input changes. Resolve findings with focused tests and a new commit; never rewrite evidence by weakening gates.

- [ ] **Step 5: Present integration choices**

Use `superpowers:finishing-a-development-branch` after review and all verification passes. Do not merge into `main` without the user's explicit choice at that checkpoint.

## Completion Criteria

- schema v2 strictly carries `(0,-0.92,0)` and `-PI/2` from generator through compiler/runtime context;
- real visual root initializes and advances as `logical * presentation`, with corrected rollback on first failure;
- active/inactive/ready/visible remain separate; no slot ever shows two or unready rigs;
- actual `Camera3D.GlobalBasis` proves all 12 WASD/yaw cases and Aiming remains camera-relative;
- P3 camera still follows the motor and uses only the temporary `0.53 m` composition offset;
- directional paths, stable IDs, coordinates, pose distinctions and rotation modes are locked;
- single/parallel result, pose, full-pose and root summaries match with zero steady-state allocations;
- focused and full P3B gates, all downstream gates, actual-window acceptance and code review pass;
- no change reaches Core movement/yaw/blend formulas, input signs, UE source assets, generated asset batch or full ALS Camera scope.
