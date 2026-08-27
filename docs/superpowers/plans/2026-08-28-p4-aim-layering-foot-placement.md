# P4 Aim, Layering, Turn/Rotate and Foot Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在一个隔离功能分支内完成 P4 的曲线与 profile 资产合同、AimOffset 与上半身分层、Turn/Rotate In Place、Foot IK/Foot Lock/pelvis、动态平台、可操作 Demo 和 1/10 角色性能门禁。

**Architecture:** 保持 P3 的 Gather -> Worker -> Commit 所有权。Main Gather 发布完整 view/aim 和上一帧 probe 对应的物理命中；Worker 以纯 C# 模型计算 P4 状态，只推进一次 AnimationTree，并在一个统一 component-space modifier 中按 Aim -> pelvis -> feet 顺序写回 Skeleton；Main Commit 只提交 Worker 已计算的 actor yaw、下一帧 probe 和生命周期。运行时只使用编译后的整数 animation/curve/bone ID，任何姿态阶段失败都恢复整套 local pose 与 visual root。

**Tech Stack:** C# 12 / .NET 8、xUnit、Godot 4.7.2 .NET AnimationTree/Skeleton3D、PowerShell/Pester、Unreal Engine 5.9 C++ commandlet、ALS-Refactored `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

---

## File Map

| Path | Responsibility |
| --- | --- |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportTypes.h` | float curve key、canonical provenance、AimOffset 元数据导出结构 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.h` | 动画曲线读取接口 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.cpp` | 原始曲线、root yaw unwrap/derivative、additive base 读取 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp` | P4 曲线 JSON 的稳定有序写入 |
| `tools/schemas/als_manifest.schema.json` | 曲线 key、插值、infinity、provenance 的严格 schema |
| `src/Als.Import/Metadata/AlsAnimationMetadata.cs` | manifest 曲线 DTO |
| `src/Als.Import/Compilation/AlsAnimationSetDefinition.cs` | 编译后的 float curve 与 key 定义 |
| `src/Als.Import/Compilation/AlsAnimationSetCompiler.cs` | curve ID、有限性、单调时间和 provenance 编译 |
| `src/Als.Import/Compilation/AlsPoseAnimationProfile.cs` | P4 animation、curve、bone mask、Foot IK 的冻结 profile |
| `src/Als.Import/Compilation/AlsPoseProfileCompiler.cs` | stable ID 解析、mask 展开和跨骨架/additive 验证 |
| `assets/config/p4_pose_profile.json` | P4 唯一正式 stable-ID 配置 |
| `reference/als-v4-export.lock.json` | 新曲线 manifest 的审核 SHA、数量和 exporter 版本 |
| `scripts/generate-p4-profile.ps1` | 从完整 object path 原子生成 P4 profile |
| `src/Als.Core/Contracts/AlsSpatialSamples.cs` | platform-aware foot hit 与 probe 合同 |
| `src/Als.Core/Contracts/AlsLocomotionCommand.cs` | view pitch/aim pitch 原始命令 |
| `src/Als.Core/Contracts/AlsFrameInput.cs` | 完整 view/aim、foot hit、platform 与 quality 输入 |
| `src/Als.Core/Contracts/AlsRuntimeState.cs` | View、Turn、Rotate、Foot Lock、pelvis 固定宽度状态 |
| `src/Als.Core/Contracts/AlsFrameResult.cs` | P4 权重、phase、yaw、feet、probe 和诊断结果 |
| `src/Als.Core/Diagnostics/AlsResultDigest.cs` | P4 所有结果字段的确定性摘要 |
| `src/Als.Core/Pose/AlsViewPoseModel.cs` | relative view、Aim、Head/Spine 权重 |
| `src/Als.Core/Pose/AlsTurnRotateModel.cs` | Turn/Rotate 选择、phase、play rate 和 curve yaw |
| `src/Als.Core/Pose/AlsFootPlacementModel.cs` | Foot Lock、platform-local target、pelvis 修正 |
| `src/Als.Godot/Animation/AlsCurveSampler.cs` | 预编译整数 curve ID 的无分配采样 |
| `src/Als.Godot/Animation/AlsComponentPoseModifier.cs` | 统一 component-space Aim/pelvis/feet 修正与回滚 |
| `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs` | P4 Aim、Turn、Rotate 动画节点 |
| `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs` | P4 参数缓存、单次 advance、phase/curve 协调 |
| `src/Als.Godot/Locomotion/AlsCharacterMotor.cs` | Main Gather 的完整旋转、foot query 和平台证据 |
| `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs` | P4 Worker 顺序、完整姿态事务和摘要发布 |
| `src/Als.Godot/Locomotion/AlsP3CommitStage.cs` | actor yaw、probe cache、generation/lifecycle 提交 |
| `tools/unreal/AlsLocomotionTrace/**` | P4 cross-engine oracle 与 fixture commandlet |
| `tools/schemas/als_pose_trace.schema.json` | P4 golden 的严格 schema |
| `scripts/generate-p4-golden.ps1` | P4 fixture 原子生成和 reference lock 校验 |
| `tests/Als.Core.Tests/Fixtures/P4/*.json` | Aim、Turn/Rotate、feet/platform golden |
| `src/Als.Godot/Locomotion/P4PoseSmoke.cs` | Aim/layer/Turn/Rotate headless smoke |
| `src/Als.Godot/Locomotion/P4FootPlacementSmoke.cs` | flat/slope/stairs/platform/rollback smoke |
| `src/Als.Godot/Locomotion/P4AnimationHarness.cs` | 1/10 single/parallel 120/600 帧矩阵 |
| `scenes/tests/p4_*.tscn` | P4 headless 入口与固定物理场景 |
| `scenes/demo/p4_locomotion_demo.tscn` | P4 可操作场景 |
| `src/Als.Godot/Locomotion/P4LocomotionDemo.cs` | P4 Demo 场景装配与 smoke 驱动 |
| `scripts/verify-p4.ps1` | P4 focused、matrix、P0-P3、Release 和仓库闭环 |
| `docs/architecture/p4-pose-and-foot-placement.md` | 最终合同、证据、性能与手工验收记录 |

## Execution Contract

整个计划在新 worktree 和单一 `feature/p4-pose-foot-placement` 分支执行。Task 1-4 构成 `P4-Asset`，Task 5-9 构成 `P4-Pose`，Task 10-14 构成 `P4-Feet`，Task 15-17 完成跨引擎、矩阵、Demo 和仓库闭环。检查点只能报告阶段通过，只有 Task 17 的完整命令可以输出 `P4_VERIFICATION_OK`。

本计划创建的每个 Godot C# 脚本都必须把引擎生成的相邻 `.cs.uid` 文件纳入同一任务提交，不能手工编写 UID。

每个新 PowerShell 会话先定义：

```powershell
$godotExe = 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$unrealEditorCmd = 'D:\UnrealEngine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$uProject = 'D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
$referenceRoot = 'D:\GodotALS-References\ALS-Refactored'
```

### Task 0: Create the Isolated P4 Worktree

**Files:**
- No tracked file changes

- [ ] **Step 1: Use the required isolation skill**

Invoke `superpowers:using-git-worktrees`, verify `main` is clean and create a sibling worktree from commit `1d941ee0611ca2f6af710deab7a6d63f07e2105c` on branch `feature/p4-pose-foot-placement`.

Run:

```powershell
git -C D:\GodotALS status --short
git -C D:\GodotALS worktree add D:\GodotALS-p4-pose-foot-placement -b feature/p4-pose-foot-placement 1d941ee0611ca2f6af710deab7a6d63f07e2105c
git -C D:\GodotALS-p4-pose-foot-placement rev-parse HEAD
```

Expected: empty status and exact base SHA `1d941ee0611ca2f6af710deab7a6d63f07e2105c`.

- [ ] **Step 2: Establish the green baseline**

Run:

```powershell
pwsh -NoProfile -File D:\GodotALS-p4-pose-foot-placement\scripts\verify-p3b.ps1 -GodotExecutable $godotExe
```

Expected: exit `0`, `P3B_VERIFICATION_OK`, no `SCRIPT ERROR:` and no `ERROR:`. This preflight creates no commit.

### Task 1: Export Strict Float Curve Keys

**Files:**
- Modify: `tools/schemas/als_manifest.schema.json`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportTypes.h`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.h`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp`
- Modify: `tests/Als.Import.Tests/Fixtures/valid_manifest.json`
- Modify: `tests/Als.Import.Tests/AlsManifestSerializerTests.cs`

- [ ] **Step 1: Add failing strict-schema tests**

Add tests named `FloatCurveKeysRoundTripWithoutLoss`, `DuplicateCurveKeyTimeIsRejected`, `UnknownCurveInterpolationIsRejected`, and `NonFiniteCurveValueIsRejected`. The valid fixture contains this exact shape:

```json
"curves": [{
  "stableCurveId": 0,
  "canonicalKind": "None",
  "sourceName": "FootLock_L",
  "sourceProvenance": "source_curve",
  "preInfinity": "Constant",
  "postInfinity": "Constant",
  "keys": [{
    "timeSeconds": 0.0,
    "value": 1.0,
    "interpolation": "Cubic",
    "arriveTangent": 0.0,
    "leaveTangent": 0.0
  }]
}]
```

- [ ] **Step 2: Run and verify the old string-array contract fails**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter "FullyQualifiedName~AlsManifestSerializerTests"`

Expected: FAIL because `curves` currently accepts names only and does not enforce key order/interpolation.

- [ ] **Step 3: Implement the exporter DTO and deterministic writer**

Add exact native structures:

```cpp
struct FAlsExportedFloatCurveKey
{
    double TimeSeconds{0.0};
    double Value{0.0};
    FString Interpolation;
    double ArriveTangent{0.0};
    double LeaveTangent{0.0};
};

struct FAlsExportedFloatCurve
{
    int32 StableCurveId{INDEX_NONE};
    FString CanonicalKind{TEXT("None")};
    FString SourceName;
    FString SourceProvenance{TEXT("source_curve")};
    FString PreInfinity{TEXT("Constant")};
    FString PostInfinity{TEXT("Constant")};
    TArray<FAlsExportedFloatCurveKey> Keys;
};
```

Sort curves by `SourceName` with ordinal comparison, assign contiguous IDs after sorting, sort keys by time, and reject non-finite values or duplicate times before manifest publication. Map UE interpolation to only `Constant`, `Linear`, or `Cubic`; write all six curve properties and all five key properties in schema order.

- [ ] **Step 4: Run focused and schema regressions**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter "FullyQualifiedName~AlsManifestSerializerTests|FullyQualifiedName~AlsManifestValidatorTests"
pwsh -NoProfile -File scripts/build-als-exporter.ps1 -EngineRoot D:\UnrealEngine -UnrealProject $uProject
```

Expected: tests PASS and plugin build exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add tools/schemas/als_manifest.schema.json tools/unreal/AlsGodotExporter tests/Als.Import.Tests/Fixtures/valid_manifest.json tests/Als.Import.Tests/AlsManifestSerializerTests.cs
git commit -m "feat: export strict animation curve keys"
```

### Task 2: Derive the Canonical Rotation Yaw Curve

**Files:**
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`
- Create: `src/Als.Import/Compilation/AlsRotationYawCurveDeriver.cs`
- Create: `tests/Als.Import.Tests/AlsRotationYawCurveTests.cs`
- Modify: `tests/VerificationScripts.Tests.ps1`

- [ ] **Step 1: Write failing unwrap, derivative and provenance tests**

Test the deterministic reference vectors `170°, 179°, -179°, -170°` at `0.1s` intervals. The unwrapped values must be `170°, 179°, 181°, 190°`; the canonical radian-per-second derivative must be finite and positive. Add rejection cases for zero duration, non-increasing sample times and a root track containing `NaN`.

- [ ] **Step 2: Run and verify the canonical curve is absent**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsRotationYawCurveTests`

Expected: FAIL because canonical yaw derivation is not implemented.

- [ ] **Step 3: Implement deterministic canonical generation**

Expose an internal testable helper with this contract:

```csharp
internal static double[] DeriveRadiansPerSecond(
    ReadOnlySpan<double> timeSeconds,
    ReadOnlySpan<double> wrappedYawDegrees)
```

`AlsRotationYawCurveDeriver` is the test oracle for the platform-independent algorithm. The production C++ implementation mirrors it: sample root yaw at the animation frame rate, unwrap each shortest signed delta, use forward/backward difference at endpoints and centered difference internally, then write `CanonicalKind = "RotationYawSpeedRadiansPerSecond"`. The tests compare a native full-export fixture against the oracle output. Prefer a validated source curve only when its unit/sign mapping is declared; set provenance to `source_curve`, otherwise `derived_root_track`. Never replace the original source curves.

- [ ] **Step 4: Prove byte-stable full export**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsRotationYawCurveTests
pwsh -NoProfile -File scripts/verify-p2a.ps1 -EngineRoot D:\UnrealEngine -UnrealProject $uProject
```

Expected: unit tests PASS, two complete exports compare byte/hash equal, and `P2A_VERIFICATION_OK`.

- [ ] **Step 5: Commit**

```powershell
git add tools/unreal/AlsGodotExporter src/Als.Import/Compilation/AlsRotationYawCurveDeriver.cs tests/Als.Import.Tests/AlsRotationYawCurveTests.cs tests/VerificationScripts.Tests.ps1
git commit -m "feat: derive canonical turn rotation curves"
```

### Task 3: Compile Curves into Stable Integer IDs

**Files:**
- Modify: `src/Als.Import/Metadata/AlsAnimationMetadata.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationSetDefinition.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationSetCompiler.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationSetPayload.cs`
- Modify: `tests/Als.Import.Tests/AlsAnimationSetCompilerTests.cs`

- [ ] **Step 1: Add failing compiler and digest tests**

Assert that curve IDs are contiguous and sorted, source/provenance survive compilation, keys are immutable arrays, malformed key time/value fails with the JSON property path, and mutating any tangent/provenance changes the payload digest.

- [ ] **Step 2: Run and confirm DTO/type failures**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsAnimationSetCompilerTests`

Expected: FAIL because `AlsAnimationMetadata.Curves` is still `string[]`.

- [ ] **Step 3: Introduce exact compiled records**

```csharp
public enum AlsCurveInterpolation : byte { Constant, Linear, Cubic }
public enum AlsCurveProvenance : byte { SourceCurve, DerivedRootTrack }
public enum AlsCanonicalCurveKind : byte { None, RotationYawSpeedRadiansPerSecond }

public readonly record struct AlsFloatCurveKeyDefinition(
    float TimeSeconds,
    float Value,
    float ArriveTangent,
    float LeaveTangent,
    AlsCurveInterpolation Interpolation);

public sealed record AlsFloatCurveDefinition(
    int CurveId,
    AlsCanonicalCurveKind CanonicalKind,
    string SourceName,
    AlsCurveProvenance Provenance,
    AlsFloatCurveKeyDefinition[] Keys);
```

`AlsAnimationDefinition` stores `AlsFloatCurveDefinition[] Curves`. Compile all string enums with ordinal exact matching; validate finite keys, strictly increasing times, first/last key inside `[0, PlayLength]`, contiguous IDs, and at most one canonical yaw curve per animation. Serialize curve data into `AlsAnimationSetPayload`; include every curve field and key in `DefinitionDigest` field order. `AlsAnimationEventDigest` remains notify/sync-only and is not changed in P4.

- [ ] **Step 4: Run focused and full import tests**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsAnimationSetCompilerTests
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Import tests/Als.Import.Tests
git commit -m "feat: compile animation curves by stable id"
```

### Task 4: Generate and Compile the Strict P4 Profile

**Files:**
- Create: `assets/config/p4_pose_profile.json`
- Create: `reference/als-v4-export.lock.json`
- Create: `scripts/generate-p4-profile.ps1`
- Modify: `scripts/verify-p2a.ps1`
- Modify: `scripts/verify-p2b.ps1`
- Create: `src/Als.Import/Compilation/AlsPoseAnimationProfile.cs`
- Create: `src/Als.Import/Compilation/AlsPoseProfileCompiler.cs`
- Create: `tests/Als.Import.Tests/AlsPoseProfileCompilerTests.cs`
- Create: `tests/GenerateP4Profile.Tests.ps1`
- Modify: `tests/VerificationScripts.Tests.ps1`

- [ ] **Step 1: Write failing profile tests**

Cover exact stable-ID resolution for one AimOffset plus Down/Forward/Up sweeps, eight Turn clips, four Rotate clips, all canonical curve aliases, paired bone roots and Foot settings. Add one test each for unknown JSON fields, missing stable ID, wrong skeleton, incompatible additive base, missing canonical curve, missing bone, asymmetric L/R roots, duplicate mask bone and mask boundary escape.

- [ ] **Step 2: Run and verify missing generator/compiler failures**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsPoseProfileCompilerTests
Invoke-Pester tests/GenerateP4Profile.Tests.ps1 -Output Detailed
```

Expected: FAIL because the P4 profile/compiler/generator do not exist.

- [ ] **Step 3: Implement the versioned profile contract**

Use exact runtime records:

```csharp
public sealed record AlsPoseAnimationProfile(
    int SchemaVersion,
    int SkeletonId,
    AlsAimProfile Aim,
    AlsTurnProfile[] Turns,
    AlsRotateProfile[] Rotates,
    AlsLayerMaskProfile Masks,
    AlsFootPlacementSettings Feet);

public enum AlsPoseStance : byte { Standing, Crouching }

public readonly record struct AlsTurnProfile(
    int AnimationId, int CurveId, AlsPoseStance Stance,
    sbyte Direction, short NominalDegrees,
    float BasePlayRate, float BlendSeconds, byte ScaleAngle);

public readonly record struct AlsRotateProfile(
    int AnimationId, int CurveId, AlsPoseStance Stance, sbyte Direction);
```

The compiler expands roots by walking `AlsSkeletonDefinition.LogicalBones[].ParentLogicalId` into sorted unique logical bone IDs. Profile schema is `1`; JSON parser rejects unknown/duplicate properties. The generator requires exactly one manifest match for every path in this exact source map:

```text
aimOffset: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look.ALS_N_Look
aimDown: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep
aimForward: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_F_Sweep.ALS_N_Look_F_Sweep
aimUp: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_U_Sweep.ALS_N_Look_U_Sweep
standingTurn90Left: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90
standingTurn90Right: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R90.ALS_N_TurnIP_R90
standingTurn180Left: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L180.ALS_N_TurnIP_L180
standingTurn180Right: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R180.ALS_N_TurnIP_R180
crouchingTurn90Left: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L90.ALS_CLF_TurnIP_L90
crouchingTurn90Right: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R90.ALS_CLF_TurnIP_R90
crouchingTurn180Left: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L180.ALS_CLF_TurnIP_L180
crouchingTurn180Right: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R180.ALS_CLF_TurnIP_R180
standingRotateLeft: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90
standingRotateRight: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_R90.ALS_N_Rotate_R90
crouchingRotateLeft: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_L90.ALS_CLF_Rotate_L90
crouchingRotateRight: /Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_R90.ALS_CLF_Rotate_R90
maskRoots: spine_01, neck_01, clavicle_l, clavicle_r, Hand_L, hand_r, Pelvis, Thigh_L, Thigh_R, Foot_L, Foot_R
```

The generator writes the profile to a same-directory temporary file, flushes, atomically replaces the target, and deletes temporary files on failure. Add `-UpdateAssetLock` to `verify-p2a.ps1`; only after its two fresh full exports compare byte-for-byte may it atomically write `reference/als-v4-export.lock.json` with exact `schemaVersion`, lowercase manifest `sha256`, `assetCount: 267`, `fileCount: 141`, `animationCount: 126`, `exporterVersion` and source project ID. `verify-p2b.ps1` reads this tracked lock instead of embedding the former P2 SHA; it still rejects any byte difference and all count drift. Pester proves a modified lock or manifest fails. P3 profile/schema/digest remain unchanged.

- [ ] **Step 4: Run the P4-Asset checkpoint**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-p2a.ps1 -EngineRoot D:\UnrealEngine -UnrealProject $uProject -UpdateAssetLock
pwsh -NoProfile -File scripts/generate-p4-profile.ps1
Invoke-Pester tests/GenerateP4Profile.Tests.ps1 -Output Detailed
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj
pwsh -NoProfile -File scripts/verify-p2b.ps1 -GodotExecutable $godotExe -CleanImport
```

Expected: profile generated atomically, Pester/.NET tests PASS, 267 assets / 141 files audit remains green, and `P2B_VERIFICATION_OK`.

- [ ] **Step 5: Commit P4-Asset**

```powershell
git add assets/config/p4_pose_profile.json reference/als-v4-export.lock.json scripts/generate-p4-profile.ps1 scripts/verify-p2a.ps1 scripts/verify-p2b.ps1 src/Als.Import/Compilation tests/Als.Import.Tests/AlsPoseProfileCompilerTests.cs tests/GenerateP4Profile.Tests.ps1 tests/VerificationScripts.Tests.ps1
git commit -m "feat: compile strict P4 pose profile"
```

### Task 5: Extend the Fixed-Width P4 Contracts

**Files:**
- Modify: `src/Als.Core/Contracts/AlsLocomotionCommand.cs`
- Modify: `src/Als.Core/Contracts/AlsSpatialSamples.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameInput.cs`
- Modify: `src/Als.Core/Contracts/AlsRuntimeState.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameResult.cs`
- Modify: `src/Als.Core/Diagnostics/AlsResultDigest.cs`
- Modify: `src/Als.Core/Locomotion/AlsLocomotionTrace.cs`
- Modify: `src/Als.Core/Simulation/AlsSyntheticInputSource.cs`
- Modify: `src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs`
- Modify: `src/Als.Godot/Locomotion/AlsCharacterMotor.cs`
- Modify: `src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs`
- Modify: `src/Als.Godot/Locomotion/P3aMotorSmoke.cs`
- Modify: `tests/Als.Core.Tests/ContractLayoutTests.cs`
- Modify: `tests/Als.Core.Tests/AlsResultDigestTests.cs`
- Modify: `tests/Als.Core.Tests/P3TestInput.cs`

- [ ] **Step 1: Add failing unmanaged/default/digest tests**

Assert `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` is false for every new P4 state and the enclosing input/state/result. Assert default pitch, weights, directions and reason are zero, invalid platform/collider ID is `-1`, and mutation of every new scalar/vector/quaternion/ID changes `AlsResultDigest`.

- [ ] **Step 2: Run and confirm missing fields fail compilation**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~ContractLayoutTests|FullyQualifiedName~AlsResultDigestTests"`

Expected: FAIL with missing P4 contract members.

- [ ] **Step 3: Define fixed-width state and result boundaries**

Add these value types and embed them rather than adding references:

```csharp
public readonly record struct AlsViewPoseState(
    float RelativeYaw, float RelativePitch, float YawSpeed,
    float HeadWeight, float SpineWeight, float SpineResidualYaw,
    float LastWorldYaw);

public readonly record struct AlsTurnInPlaceState(
    float ActivationSeconds, float Phase, float PlayRate,
    float RemainingYaw, short NominalDegrees, sbyte Direction, byte Active);

public readonly record struct AlsRotateInPlaceState(
    float Phase, float PlayRate, sbyte Direction, byte Active);

public readonly record struct AlsFootLockState(
    Vector3 LocalPosition, Quaternion LocalRotation, Vector3 Offset,
    Quaternion Rotation, int PlatformId, float Amount, byte Locked);

public readonly record struct AlsPelvisCorrectionState(
    Vector3 CurrentOffset, Vector3 TargetOffset, float VerticalVelocity);
```

The command positional order becomes `MovementAxes, ViewYaw, ViewPitch, AimYaw, AimPitch, RequestedGait, RequestedStance, RequestedRotationMode, JumpPressed`; update every constructor call in the files listed above and keep `CreateDefault()` at zero pitch. Extend `AlsFootHit` with `int PlatformId`, platform position/rotation, `long ColliderId`, point velocity, `Valid` and `Walkable`; use `-1` for invalid IDs, matching the existing `AlsFloorSample` convention. Extend result with aim angles/weights, Turn/Rotate values and yaw deltas, pelvis and both foot outputs, next probe origins, modifier ticks and `AlsP4ReasonCode : ushort`. Hash every field in declaration order.

- [ ] **Step 4: Run focused and full Core tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~ContractLayoutTests|FullyQualifiedName~AlsResultDigestTests"
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj
dotnet build GodotALS.csproj -c Debug --no-restore
```

Expected: all tests PASS, Godot C# build succeeds and existing P3 digest fixtures remain unchanged.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Contracts src/Als.Core/Diagnostics src/Als.Core/Locomotion/AlsLocomotionTrace.cs src/Als.Core/Simulation/AlsSyntheticInputSource.cs src/Als.Godot/Animation/P3bAnimationGraphSmoke.cs src/Als.Godot/Locomotion/AlsCharacterMotor.cs src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs src/Als.Godot/Locomotion/P3aMotorSmoke.cs tests/Als.Core.Tests/ContractLayoutTests.cs tests/Als.Core.Tests/AlsResultDigestTests.cs tests/Als.Core.Tests/P3TestInput.cs
git commit -m "feat: add fixed width P4 pose contracts"
```

### Task 6: Implement View, Aim and Spine State

**Files:**
- Create: `src/Als.Core/Pose/AlsViewPoseModel.cs`
- Create: `src/Als.Core/Pose/AlsViewPoseSettings.cs`
- Create: `tests/Als.Core.Tests/AlsViewPoseModelTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Write failing behavioral tests**

Cover `179° -> -179°` shortest yaw speed, pitch clamp at `±π/2`, Aim center/up/down/left/right, `0.1s` aiming-in half-life, `0.7s` aiming-out half-life, `±π/6` residual yaw clamp, moving-platform world-yaw continuity, invalid delta/non-finite transactional failure, and 10,000 steady evaluations allocating `0 B`.

- [ ] **Step 2: Run and confirm model absence**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsViewPoseModelTests|FullyQualifiedName~HotPathAllocationTests"`

Expected: FAIL because `AlsViewPoseModel` is absent.

- [ ] **Step 3: Implement the pure transactional API**

```csharp
public static class AlsViewPoseModel
{
    public static bool TryEvaluate(
        in AlsViewPoseSettings settings,
        in AlsFrameInput input,
        in AlsRuntimeState currentState,
        out AlsRuntimeState nextState,
        out AlsViewPoseOutput output,
        out AlsP4ReasonCode reason);
}
```

Normalize angles to `[-π, π]`, clamp pitch to `[-π/2, π/2]`, derive yaw speed from shortest world-yaw delta, and use `alpha = 1 - exp2(-deltaTime / halfLife)` so split-frame and whole-frame damping agree. Compute all locals first; return `false` with caller state/result unchanged for non-finite input or non-positive delta. Aim and Spine output contains only numeric targets and weights, not Godot types.

- [ ] **Step 4: Run tests and allocation gate**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsViewPoseModelTests|FullyQualifiedName~HotPathAllocationTests"`

Expected: all cases PASS and allocation assertion equals `0`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Pose tests/Als.Core.Tests/AlsViewPoseModelTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat: evaluate deterministic view and aim pose"
```

### Task 7: Implement Turn and Rotate In Place

**Files:**
- Create: `src/Als.Core/Pose/AlsTurnRotateModel.cs`
- Create: `src/Als.Core/Pose/AlsTurnRotateSettings.cs`
- Create: `tests/Als.Core.Tests/AlsTurnRotateModelTests.cs`
- Modify: `tests/Als.Core.Tests/AlsLocomotionRotationTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Write failing threshold, selection and cancellation tests**

Test Turn at exact `45°` does not start, `45° + epsilon` uses mapped delay and strictly greater comparison, `<130°` chooses 90, `130°` chooses 180, all stance/direction combinations select eight distinct IDs, base rate is `1.2`, blend is `0.2s`, and movement/air/stance/mode/deactivate cancels. Test Rotate exact `50°` does not activate, signs choose L/R, yaw speed `180/460°/s` maps to `1.15/3.0`, smoothing half-life is `0.15s`, Turn/Rotate are mutually exclusive, and stationary Aiming disables P3 direct yaw.

- [ ] **Step 2: Run and verify the new state machine is missing**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsTurnRotateModelTests|FullyQualifiedName~AlsLocomotionRotationTests"`

Expected: FAIL because Turn/Rotate evaluation and yaw ownership are absent.

- [ ] **Step 3: Implement one yaw writer**

```csharp
public static bool TrySelectAndAdvance(
    in AlsTurnRotateSettings settings,
    in AlsFrameInput input,
    in AlsViewPoseOutput view,
    in AlsRuntimeState currentState,
    out AlsRuntimeState nextState,
    out AlsTurnRotateSelection selection,
    out AlsP4ReasonCode reason);

public static bool TryFinalizeYaw(
    in AlsTurnRotateSelection selection,
    float previousYawSpeedRadiansPerSecond,
    float currentYawSpeedRadiansPerSecond,
    out AlsTurnRotateOutput output,
    out AlsP4ReasonCode reason);
```

Turn delay maps linearly from yaw `[π/4, π]` to `[0, 0.75]`; choose 90 below `130°` and 180 otherwise. `TrySelectAndAdvance()` publishes animation/curve IDs plus previous/current phase without actor yaw. Worker samples that integer curve at both phases, then `TryFinalizeYaw()` validates the two finite radian-per-second values, integrates them trapezoidally over delta time and publishes yaw delta. Rotate uses the same two-step path. Add a yaw-source enum so exactly one of P3 locomotion, Turn or Rotate owns actor yaw per frame; failure in either step aborts the whole Worker transaction.

- [ ] **Step 4: Run behavioral and allocation tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsTurnRotateModelTests|FullyQualifiedName~AlsLocomotionRotationTests|FullyQualifiedName~HotPathAllocationTests"
```

Expected: tests PASS, accumulated yaw agrees with curve integration tolerance `1e-5`, steady allocation `0 B`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Pose tests/Als.Core.Tests/AlsTurnRotateModelTests.cs tests/Als.Core.Tests/AlsLocomotionRotationTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat: add curve driven turn and rotate state"
```

### Task 8: Sample Curves and Build the P4 Animation Graph

**Files:**
- Create: `src/Als.Godot/Animation/AlsCurveSampler.cs`
- Modify: `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs`
- Modify: `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs`
- Create: `src/Als.Godot/Animation/P4AnimationGraphSmoke.cs`
- Create: `scenes/tests/p4_animation_graph_smoke.tscn`

- [ ] **Step 1: Add a failing headless graph smoke**

The smoke compiles the real P4 profile, samples Constant/Linear/Cubic keys at boundaries and between keys, selects all eight Turn and four Rotate clips, and asserts `AdvanceCount == FrameCount` after 240 frames. It prints `P4_ANIMATION_GRAPH_OK frames=240 advances=240` only after all checks.

- [ ] **Step 2: Run and verify missing graph nodes fail**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_graph_smoke.tscn`

Expected: nonzero exit or missing sentinel because the sampler/P4 nodes are absent.

- [ ] **Step 3: Implement prebound curve and graph paths**

`AlsCurveSampler` accepts `AlsFloatCurveKeyDefinition[]` during initialization and samples into caller-provided buffers by integer curve ID. `AlsLocomotionGraphBuilder` creates named nodes `P4Turn`, `P4Rotate`, `P4AimDown`, `P4AimForward`, `P4AimUp`; `AlsLocomotionAnimationController` caches every `StringName` once, sets active clip/rate/phase without string construction, and calls manual `Advance(delta)` exactly once after all parameters are written.

- [ ] **Step 4: Run smoke twice for deterministic output**

Run:

```powershell
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_graph_smoke.tscn
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_graph_smoke.tscn
```

Expected twice: `P4_ANIMATION_GRAPH_OK frames=240 advances=240`, identical digest, no engine/script error.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Animation scenes/tests/p4_animation_graph_smoke.tscn
git commit -m "feat: build P4 curve driven animation graph"
```

### Task 9: Apply AimOffset and Upper-Body Layering

**Files:**
- Create: `src/Als.Godot/Animation/AlsComponentPoseModifier.cs`
- Create: `src/Als.Godot/Animation/AlsPoseScratch.cs`
- Create: `src/Als.Godot/Locomotion/P4PoseSmoke.cs`
- Create: `scenes/tests/p4_pose_smoke.tscn`
- Modify: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`

- [ ] **Step 1: Add failing real-Mannequin pose tests**

For center/up/down/left/right, assert five distinct stable pose digests. Assert Aim changes only compiled Head/Spine/Arm/Hand masks; `root`, `pelvis`, `thigh_l/r` and `foot_l/r` transforms remain bit-equal. Assert mesh-space delta is computed against the validated additive base, local/mesh Arm weights are complementary at full local weight, and injected failure after Aim restores every local bone transform and corrected visual root.

- [ ] **Step 2: Run and verify unchanged Skeleton fails**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_pose_smoke.tscn`

Expected: missing sentinel because P4 modifier is absent.

- [ ] **Step 3: Implement one transactional modifier**

Initialize fixed arrays for local pose, component pose, original pose, parent indices, affected flags and ordered bone IDs. Per frame: capture all local transforms, build component transforms in parent order, sample and blend Down/Forward/Up mesh-space additive deltas, apply Head/Spine/Arm/Hand masks, rebuild only affected local transforms, then write Skeleton once. The public transaction is:

```csharp
public bool TryApply(
    in AlsPoseModifierInput input,
    ref AlsPoseModifierOutput output,
    out AlsP4ReasonCode reason)
```

On any non-finite transform, invalid bone or injected stage failure, restore all captured local transforms and the captured visual root before returning `false`; publish no new result digest.

- [ ] **Step 4: Run the P4-Pose checkpoint**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsViewPoseModelTests|FullyQualifiedName~AlsTurnRotateModelTests"
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_graph_smoke.tscn
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_pose_smoke.tscn
```

Expected: Core tests PASS; graph sentinel present; pose smoke prints `P4_POSE_OK` with nonzero Aim/Turn/Rotate digests and rollback count `1`.

- [ ] **Step 5: Commit P4-Pose**

```powershell
git add src/Als.Godot/Animation src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs src/Als.Godot/Locomotion/P4PoseSmoke.cs scenes/tests/p4_pose_smoke.tscn
git commit -m "feat: apply mesh space aim and layering"
```

### Task 10: Implement Pure Foot Lock and Pelvis Correction

**Files:**
- Create: `src/Als.Core/Pose/AlsFootPlacementModel.cs`
- Create: `src/Als.Core/Pose/AlsFootPlacementSettings.cs`
- Create: `tests/Als.Core.Tests/AlsFootPlacementModelTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Write failing foot/platform/pelvis tests**

Cover lock acquisition only for Grounded/MotorDriven/walkable hit/positive curve; same-platform translation and rotation preserving local target; ray miss, air, platform removal, base change, teleport, weight loss and overextension releasing lock; no jitter recapture; slope normal rotation clamps; pelvis chooses the lower required offset; independent up/down damping; no-hit return to zero; leg/capsule constraints; invalid input rollback; 10,000 steady evaluations allocating `0 B`.

- [ ] **Step 2: Run and confirm missing model failure**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsFootPlacementModelTests|FullyQualifiedName~HotPathAllocationTests"`

Expected: FAIL because Foot placement model is absent.

- [ ] **Step 3: Implement platform-local deterministic state**

```csharp
public static bool TryEvaluate(
    in AlsFootPlacementSettings settings,
    in AlsFrameInput input,
    float leftIkWeight,
    float rightIkWeight,
    float leftLockCurve,
    float rightLockCurve,
    in AlsRuntimeState currentState,
    out AlsRuntimeState nextState,
    out AlsFootPlacementOutput output,
    out AlsP4ReasonCode reason);
```

Store lock target in platform-local position/rotation when `PlatformId >= 0`, otherwise store world target with character-transform provenance. Rebuild world targets from the current platform transform. Treat miss/removal/release as normal reason codes. Choose the minimum vertical foot requirement for pelvis, apply separate exponential half-lives for rising/falling, clamp thigh/foot angles and capsule/leg reach, and compute all next values before assigning outputs.

- [ ] **Step 4: Run tests and allocation gate**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsFootPlacementModelTests|FullyQualifiedName~HotPathAllocationTests"`

Expected: all tests PASS and allocation assertion equals `0`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Pose tests/Als.Core.Tests/AlsFootPlacementModelTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat: solve deterministic foot lock and pelvis"
```

### Task 11: Gather Foot Queries and Platform Evidence

**Files:**
- Modify: `src/Als.Godot/Locomotion/AlsCharacterMotor.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Create: `src/Als.Godot/Locomotion/P4FootGatherSmoke.cs`
- Create: `scenes/tests/p4_foot_gather_smoke.tscn`

- [ ] **Step 1: Add a failing N/N+1 ownership smoke**

Use a fixed foot probe at Worker frame N, cache it at Commit N, move and rotate the platform before Gather N+1, and assert Gather publishes the new platform transform with the old pose-derived local request. Verify motor/input frame N+1 is not delayed, both hits are immutable snapshots, and Main never reads Skeleton/AnimationTree.

- [ ] **Step 2: Run and verify zero foot hits fail**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_foot_gather_smoke.tscn`

Expected: missing sentinel because `AlsCharacterMotor` currently publishes zero `FootHits`.

- [ ] **Step 3: Implement Gather/Commit probe exchange**

Preallocate two probe requests per slot. Commit validates identity/frame/generation then copies Worker probe origins only. On the next Main Gather, convert each origin using current character/platform transforms, issue a downward shape/ray query, validate finite point/normal and walkability, capture collider instance ID, platform transform and point velocity, and publish `AlsFootHit`. Detect teleport and base change from transform/platform discontinuity. Do not access Worker-owned nodes from Main.

- [ ] **Step 4: Run ownership and P3 frame-order regressions**

Run:

```powershell
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_foot_gather_smoke.tscn
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p3b_frame_order_smoke.tscn
```

Expected: `P4_FOOT_GATHER_OK latency=1` and existing P3 frame-order sentinel; no stale/missing/generation errors.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p4_foot_gather_smoke.tscn
git commit -m "feat: gather platform aware foot probes"
```

### Task 12: Apply Pelvis and Foot Corrections in the Unified Modifier

**Files:**
- Modify: `src/Als.Godot/Animation/AlsComponentPoseModifier.cs`
- Modify: `src/Als.Godot/Animation/AlsPoseScratch.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- Create: `src/Als.Godot/Locomotion/P4FootPlacementSmoke.cs`
- Create: `scenes/tests/p4_foot_placement_smoke.tscn`

- [ ] **Step 1: Add failing real-rig fixture tests**

Build flat, continuous slope, uneven stairs, translating platform and rotating platform fixtures. Assert foot targets contact the expected surfaces, pelvis is continuous, platform-local lock target stays constant, jump/base-change/teleport releases, and injected failures after pelvis and after left foot each restore every local bone plus root. Assert one Skeleton write transaction and one AnimationTree advance per frame.

- [ ] **Step 2: Run and verify feet remain uncorrected**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_foot_placement_smoke.tscn`

Expected: missing sentinel because pelvis/feet are not yet applied.

- [ ] **Step 3: Extend the existing component-space pass**

After Aim/layering, translate pelvis in component space, solve each leg with the compiled thigh/foot IDs and configured reach/angle limits, align foot up to the hit normal, then rebuild affected subtrees once. Keep one captured original pose for the entire Aim/pelvis/feet transaction. On any stage failure restore all bones and root, freeze the prior valid visual result and emit one bounded reason code.

- [ ] **Step 4: Run the P4-Feet checkpoint**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsFootPlacementModelTests
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_foot_gather_smoke.tscn
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_foot_placement_smoke.tscn
```

Expected: tests PASS; smoke prints `P4_FOOT_PLACEMENT_OK` with flat/slope/stairs/translate/rotate cases and rollback count `2`.

- [ ] **Step 5: Commit P4-Feet**

```powershell
git add src/Als.Godot/Animation src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs src/Als.Godot/Locomotion/P4FootPlacementSmoke.cs scenes/tests/p4_foot_placement_smoke.tscn
git commit -m "feat: apply pelvis and foot pose corrections"
```

### Task 13: Close Worker, Commit and Lifecycle Semantics

**Files:**
- Modify: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3Presentation.cs`
- Create: `src/Als.Godot/Locomotion/P4LifecycleSmoke.cs`
- Create: `scenes/tests/p4_lifecycle_smoke.tscn`

- [ ] **Step 1: Add failing lifecycle and yaw-ownership tests**

Verify Worker order is P3 -> View/Aim/Turn/Rotate -> Feet -> one Advance -> one modifier -> publish. Verify Commit applies exactly the published yaw delta and never recomputes it. Exercise deactivate, replace, stale result, generation mismatch, failure, successful recovery and platform removal; no old pose/probe/yaw may cross generation.

- [ ] **Step 2: Run and verify partial P4 lifecycle fails**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_lifecycle_smoke.tscn`

Expected: missing sentinel because P4 generation/recovery rules are not integrated end to end.

- [ ] **Step 3: Integrate one transactional publish path**

Capture pose/root before any P4 visual mutation. Only publish the new `AlsFrameResult` after Core, curve, controller and modifier all succeed. On failure retain last valid visual pose, discard the new result/probes, reset yaw ownership to safe MotorDriven, deduplicate the stable reason code and require a new valid generation after replacement. Commit validates identity/frame/generation/completion before applying yaw or copying probes.

- [ ] **Step 4: Run lifecycle and P3 visibility regressions**

Run:

```powershell
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_lifecycle_smoke.tscn
pwsh -NoProfile -File scripts/verify-p3b.ps1 -GodotExecutable $godotExe -SkipRegression
```

Expected: `P4_LIFECYCLE_OK`, all mismatch/error counters expected by the scenario are consumed and return to zero, P3 focused verification remains green.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p4_lifecycle_smoke.tscn
git commit -m "fix: make P4 pose publication transactional"
```

### Task 14: Add Cross-Engine P4 Golden Fixtures

**Files:**
- Create: `tools/schemas/als_pose_trace.schema.json`
- Modify: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsLocomotionTraceCommandlet.cpp`
- Modify: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsTraceCharacter.h`
- Modify: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsTraceCharacter.cpp`
- Create: `scripts/generate-p4-golden.ps1`
- Create: `tests/GenerateP4Golden.Tests.ps1`
- Create: `src/Als.Core/Pose/AlsPoseTrace.cs`
- Create: `tests/Als.Core.Tests/AlsPoseGoldenTests.cs`
- Create: `tests/Als.Core.Tests/Fixtures/P4/*.json`

- [ ] **Step 1: Add failing schema/generator/comparer tests**

Require reference commit and patch hashes, source object paths, exact state/ID/direction/phase/reason fields, and position/rotation tolerances. Cases are Aim center/up/down/left/right; standing/crouching Turn L/R 90/180; standing/crouching Rotate L/R; flat/slope/stairs; platform translate/rotate/base-change/teleport/release. Generator failure must preserve the previous official fixture directory.

- [ ] **Step 2: Run and verify fixtures are absent**

Run:

```powershell
Invoke-Pester tests/GenerateP4Golden.Tests.ps1 -Output Detailed
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsPoseGoldenTests
```

Expected: FAIL because schema, generator and fixtures do not exist.

- [ ] **Step 3: Implement trace generation and comparison**

Extend the locked UE commandlet to call the identified ALS reference functions and serialize state plus selected local bone transforms. `generate-p4-golden.ps1` first invokes `prepare-p3-reference.ps1`, verifies exact commit `b754d6f0f2bb03741d301f8fb88077ebfe561e17`, builds/runs commandlet into a unique temp directory, validates every JSON against the schema, compares an immediate second run byte-for-byte, then atomically replaces `tests/Als.Core.Tests/Fixtures/P4`. The C# comparer requires exact discrete values and uses declared tolerances only for transforms.

- [ ] **Step 4: Generate and replay all P4 fixtures**

Run:

```powershell
pwsh -NoProfile -File scripts/generate-p4-golden.ps1 -UnrealEditorCmd $unrealEditorCmd -UnrealProject $uProject -ReferenceRoot $referenceRoot
Invoke-Pester tests/GenerateP4Golden.Tests.ps1 -Output Detailed
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsPoseGoldenTests
```

Expected: generator prints `P4_GOLDEN_OK`, Pester and all golden cases PASS.

- [ ] **Step 5: Commit**

```powershell
git add tools/schemas/als_pose_trace.schema.json tools/unreal/AlsLocomotionTrace scripts/generate-p4-golden.ps1 tests/GenerateP4Golden.Tests.ps1 src/Als.Core/Pose/AlsPoseTrace.cs tests/Als.Core.Tests/AlsPoseGoldenTests.cs tests/Als.Core.Tests/Fixtures/P4
git commit -m "test: add P4 cross engine pose golden"
```

### Task 15: Add the 1/10 Single/Parallel Matrix and Performance Gate

**Files:**
- Create: `src/Als.Godot/Locomotion/P4AnimationHarness.cs`
- Create: `src/Als.Godot/Locomotion/AlsP4HarnessContext.cs`
- Create: `scenes/tests/p4_animation_harness.tscn`
- Create: `scripts/p4-verification-functions.ps1`
- Modify: `tests/VerificationScripts.Tests.ps1`

- [ ] **Step 1: Add failing harness-output tests**

Require 120 warmup and 600 measured frames, character 0 replacement after warmup, 1/10 characters in single/parallel, and pair equality for result/pose/full-pose/root/Aim/TurnRotate/Feet digests. Require zero missing/stale/generation/lag/thread errors, zero model/curve/controller/modifier/skeleton/exchange/commit allocations, exactly one advance and commit per measured frame, Gather+Commit p95 `<=1.5ms`, Worker p95 `<=2.5ms`, total p99 `<=4.0ms`.

- [ ] **Step 2: Run and verify harness sentinel is absent**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_harness.tscn -- --mode=single --characters=1 --warmup=120 --frames=600`

Expected: missing sentinel because P4 harness is absent.

- [ ] **Step 3: Implement the production-path matrix**

Reuse `AlsP3Character`, Worker and Commit; do not add a reduced feature path. Feed deterministic camera, Turn/Rotate, slope/stairs/platform and release phases. Preallocate per-character digest/timing buffers during warmup. Print one machine-readable result line containing every counter and percentile; `p4-verification-functions.ps1` parses named fields and rejects duplicates, omissions, non-finite timings or nonzero errors.

- [ ] **Step 4: Run all four matrix cells**

Run:

```powershell
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_harness.tscn -- --mode=single --characters=1 --warmup=120 --frames=600
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_harness.tscn -- --mode=parallel --characters=1 --warmup=120 --frames=600
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_harness.tscn -- --mode=single --characters=10 --warmup=120 --frames=600
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_animation_harness.tscn -- --mode=parallel --characters=10 --warmup=120 --frames=600
```

Expected: each prints `P4_MATRIX_OK`; paired digests match; allocations/errors are `0`; timing thresholds pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion/P4AnimationHarness.cs src/Als.Godot/Locomotion/AlsP4HarnessContext.cs scenes/tests/p4_animation_harness.tscn scripts/p4-verification-functions.ps1 tests/VerificationScripts.Tests.ps1
git commit -m "test: gate P4 parallel pose performance"
```

### Task 16: Build the P4 Demo and Operational HUD

**Files:**
- Create: `scenes/demo/p4_locomotion_demo.tscn`
- Create: `src/Als.Godot/Locomotion/P4LocomotionDemo.cs`
- Modify: `src/Als.Godot/Locomotion/AlsLocomotionHud.cs`
- Modify: `project.godot`
- Create: `src/Als.Godot/Locomotion/P4DemoSmoke.cs`
- Create: `scenes/tests/p4_demo_smoke.tscn`

- [ ] **Step 1: Add a failing 300-frame Demo smoke**

Assert one visible production rig, current P3 WASD/mouse alignment unchanged, RMB enters Aiming, V changes rotation mode, Shift/Ctrl/Space remain functional, and the scripted route reaches slope, stairs, translating and rotating platforms. Assert HUD fields `frame/mode/aim/turn/rotate/leftLock/rightLock/pelvis/errors/workerMs` exist and no tutorial text is present.

- [ ] **Step 2: Run and verify the P4 scene is absent**

Run: `& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_demo_smoke.tscn -- --als-smoke-frames=300`

Expected: missing scene or missing sentinel.

- [ ] **Step 3: Build the versioned playable scene**

Copy the P3 production character/camera/input semantics into the P4 scene, add a continuous slope, varied stairs, one translating platform, one vertical-axis rotating platform and a walkable platform-to-static base-change route. Bind the P4 profile and production Worker path. HUD displays only live operational diagnostics and uses bounded fixed formatting.

- [ ] **Step 4: Run automated and manual Demo checks**

Run:

```powershell
& $godotExe --headless --path D:\GodotALS-p4-pose-foot-placement res://scenes/tests/p4_demo_smoke.tscn -- --als-smoke-frames=300
& $godotExe --path D:\GodotALS-p4-pose-foot-placement --editor res://scenes/demo/p4_locomotion_demo.tscn
```

Expected automated sentinel: `P4_DEMO_OK frames=300 rigs=1`. Manually verify Aim continuity, all Turn/Rotate variants, cancellation, flat/slope/stairs foot contact, platform-local lock and teleport/base-change release; record pass/fail in the architecture document during Task 17.

- [ ] **Step 5: Commit**

```powershell
git add scenes/demo/p4_locomotion_demo.tscn scenes/tests/p4_demo_smoke.tscn src/Als.Godot/Locomotion/P4LocomotionDemo.cs src/Als.Godot/Locomotion/P4DemoSmoke.cs src/Als.Godot/Locomotion/AlsLocomotionHud.cs project.godot
git commit -m "feat: add playable P4 pose and feet demo"
```

### Task 17: Close Full P4 Verification and Documentation

**Files:**
- Create: `scripts/verify-p4.ps1`
- Create: `tests/VerifyP4.Tests.ps1`
- Create: `docs/architecture/p4-pose-and-foot-placement.md`
- Modify: `docs/superpowers/specs/2026-08-25-godot-als-port-design.md`
- Modify: `README.md`

- [ ] **Step 1: Add failing verification-script tests**

Test that focused mode never emits full success, all Godot output streams are captured, nonzero exit and `SCRIPT ERROR:`/`ERROR:`/ALS failure markers fail, all four matrix cells are mandatory, full mode invokes non-skip `verify-p3b.ps1`, Release tests, locked-base repository closure, `git diff --check`, clean status, and exactly one final `P4_VERIFICATION_OK`.

- [ ] **Step 2: Run and verify script tests fail**

Run: `Invoke-Pester tests/VerifyP4.Tests.ps1 -Output Detailed`

Expected: FAIL because `verify-p4.ps1` does not exist.

- [ ] **Step 3: Implement the fixed verification order**

`verify-p4.ps1` executes: Debug build; P4 import/profile/curve focused tests; Core unit/golden tests; animation graph/pose/foot gather/foot placement/lifecycle smokes; Demo smoke; 1/10 single/parallel matrix; full repository Pester; non-skip `verify-p3b.ps1`; Release solution tests; base-SHA closure; `git diff --check`; clean-worktree audit. Only after all pass print exactly:

```text
P4_VERIFICATION_OK
```

The architecture document records profile schema/digest, canonical curve provenance, mask expansion, thread ownership, N/N+1 foot query, rollback, golden tolerances, four matrix outputs, measured percentiles, allocation/error counters and manual Demo checklist. Update the master roadmap to mark P4 complete only after evidence exists; P5A/P5B/P5C/P6/P7 remain pending.

- [ ] **Step 4: Run self-review before the full gate**

Run:

```powershell
rg -n "TODO|TBD|placeholder|\.\.\." src tools scripts tests assets/config docs/architecture/p4-pose-and-foot-placement.md
rg -n "RotationYawSpeedRadiansPerSecond|P4_VERIFICATION_OK|platform-local|AdvanceCount" src tools scripts tests docs/architecture/p4-pose-and-foot-placement.md
dotnet test GodotALS.sln -c Debug --no-restore
git diff --check
```

Expected: first command has no matches in newly added P4 files; required-contract search finds implementation and tests; Debug tests PASS; diff check is clean.

- [ ] **Step 5: Run the complete P4 closure**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-p4.ps1 -GodotExecutable $godotExe -UnrealEditorCmd $unrealEditorCmd -UnrealProject $uProject -ReferenceRoot $referenceRoot
```

Expected: every P4 check, P0-P3 regression and Release test passes; final and unique sentinel is `P4_VERIFICATION_OK`.

- [ ] **Step 6: Request code review and resolve findings**

Invoke `superpowers:requesting-code-review`. Apply each verified finding with its own red/green test, rerun the focused gate that owns it, then rerun Step 5. Do not merge with an unresolved correctness or verification finding.

- [ ] **Step 7: Commit verification evidence**

```powershell
git add scripts/verify-p4.ps1 tests/VerifyP4.Tests.ps1 docs/architecture/p4-pose-and-foot-placement.md docs/superpowers/specs/2026-08-25-godot-als-port-design.md README.md
git commit -m "docs: close P4 pose and foot placement"
git status --short
```

Expected: commit succeeds and status is empty.

- [ ] **Step 8: Finish the branch through the required integration skill**

Invoke `superpowers:finishing-a-development-branch`. Present the verified branch/merge choices, merge only after user confirmation, then run `verify-p4.ps1` once on the resulting `main` checkout before removing the worktree.

## Final Completion Checklist

- [ ] P4-Asset exports deterministic raw/canonical curves, additive base and strict stable-ID profile.
- [ ] P4-Pose implements complete pitch/yaw Aim, declared masks, eight Turn clips and four Rotate clips with one yaw owner.
- [ ] P4-Feet implements flat/slope/stairs, platform-local Foot Lock, pelvis damping and all release paths.
- [ ] Worker performs one AnimationTree advance, one transactional modifier and one result publication per frame.
- [ ] Failure restores complete pose/root and stale/replacement generations expose no old visual data.
- [ ] Cross-engine golden and 1/10 single/parallel digests pass with `0 B` steady allocations and zero frame/thread errors.
- [ ] P4 short performance thresholds pass without quality reduction; P7 10-minute certification remains separate.
- [ ] Playable Demo preserves the approved P3 keyboard/mouse/camera semantics and passes manual acceptance.
- [ ] Full `verify-p4.ps1` passes P0-P4 Debug/Release/repository closure and emits exactly one `P4_VERIFICATION_OK`.
