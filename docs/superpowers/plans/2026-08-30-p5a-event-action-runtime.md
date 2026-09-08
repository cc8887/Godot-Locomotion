# P5A Event, Sync and Action Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在现有 P4 生产链上一次完成 Manifest v2、全量 ALS 动画时间线重导出、通用 Curve/Event/Sync/Dynamic Transition/ActionPlayer、真实 Transition 与原地 Roll、跨引擎证据、1/10 角色并行矩阵和可操作 Demo，同时保持 P0-P4 行为与零分配门禁。

**Architecture:** 保持 `Gather -> Worker -> Commit` 单一所有权。UE 5.9 exporter 只把 authored Curve、Notify/Notify State、Sync Marker、Montage section/slot/segment 转成严格 v2 数据；Import 在初始化期完成 stable string 到连续 integer ID、统一 occurrence/authority layout、profile 与 Action timeline 的编译；纯 `Als.Core` 以 caller-owned span/state/buffer 预计算 Curve/Sync/Transition/Action、lane blend 与完整 timeline candidate；Godot Worker 恰好推进一次 `AnimationTree`、执行 P4 pose transaction，再用完整 P4 candidate state finalize P5 并一次发布；Main Commit 通过 frame/character/generation/stage 校验后才派发事件并更新 committed-state mirror。

**Tech Stack:** C# 12 / .NET 8、xUnit、Godot 4.7.2 .NET `AnimationTree` / `Skeleton3D`、PowerShell / Pester、Unreal Engine 5.9 C++ commandlet、ALS-Refactored `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

**Spec:** `docs/superpowers/specs/2026-08-30-p5a-event-action-runtime-design.md`

**Global Constraints:**

- P5A 是单次完整交付，不建立另一条 dispatch/runtime；所有 Godot 改动扩展现有 `AlsP3Character -> AlsP3WorkerRoot -> AlsP3CommitStage`。
- `Als.Core` 不引用 Godot 或 `Als.Import`；热路径只接收固定宽度值类型、`ReadOnlySpan<T>`、`Span<T>` 和 caller-owned storage。
- Worker 不调用 gameplay、不发 Godot signal、不访问外部 `SceneTree`，也不按字符串解析资产、curve、event、marker、section 或 AnimationTree 参数。
- Manifest 固定为 `schemaVersion: 2`、`exporterVersion: "2.0.0"`；P5A importer 明确拒绝 v1。
- typed kind 只有 `Generic`、`Footstep`、`SetAction`、`SetGroundedEntry`、`EarlyBlendOut`、`RootMotionScale`；未知 UE class 必须退化成 `Generic`，禁止按 display name 猜语义。
- `AlsEventBuffer.Capacity == 16`、`AlsActionOutcomeBuffer.Capacity == 2`；任何溢出必须全有或全无，不能截断。
- Marker 只参与 Sync，不进入 gameplay event buffer；Transition lane 与 Action lane 完全独立。
- P5A Roll 原地播放，不移动 `CharacterBody3D`、不消费 Root Motion；Overlay gameplay 属于 P5B，Mantle/Roll Root Motion 属于 P5C，Ragdoll/Get-up/Camera 属于 P6，十分钟 Release 性能认证属于 P7。
- 完整导出仍覆盖 267 项资产、141 个正式文件、126 个 AnimationSequence，Overlay 与道具保留，音频排除。只有真实 UE inventory 证据才能改变这些数字。
- 每个新 Godot C# 文件必须让 Godot 4.7.2 .NET 生成相邻 `.cs.uid`，并与脚本同一次提交；不得手写或复制 UID。
- `.godot/`、`bin/`、`obj/`、`artifacts/` 与 `assets/generated/als_v4/**` 保持 ignored；canonical generated batch 必须存在于本地验证环境，但不提交二进制资产。
- 只有顶层 `scripts/verify-p5a.ps1` 能输出一次且仅一次 `P5A_VERIFICATION_OK`；子 gate 使用各自唯一 marker。
- `AlsFrameResult.P5FailureCode` 是成功结果 envelope 的 ABI 保留字段，任何已发布结果都必须为 `None`；失败 attempt 不构造 `AlsFrameResult`，非 `None` code 只进入 `AlsP5FailureRecord`。测试必须拒绝带非 `None` code 的结果 publication，最终架构文档记录这一规格细化。
- Task 6 显式修订设计规格：occurrence identity 加入 `OccurrenceHandleId` 与编译期 `BoundaryOrdinal`，Notify ownership 保存完整 identity；唯一 versioned layout 覆盖 Base/Turn/Rotate/Transition/Action handles 与 authority；所有 lane 使用 Core-owned 两 bank 离散 blend；旧 non-owning Cancel 若已低于 request high-watermark 归类为 delayed replay并静默 no-op，而当前/更新的首次 stale Cancel 仍返回 `RejectedInvalidRequest`；runtime-recovery cancel 在同帧正常 request 前执行。规格、ABI、测试和最终文档必须同一次收口，不能保留两套解释。

---

## File Map

| Path | Responsibility |
| --- | --- |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsNotifyClassRegistry.*` | 显式 UE class alias 到 typed kind/payload 的唯一映射 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.*` | Sequence timeline、marker、curve 导出 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsCompositeAssetReader.cpp` | Montage timeline、section、slot、segment 导出 |
| `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp` | Manifest v2 的稳定有序写入 |
| `tools/schemas/als_manifest.schema.json` | v2 timeline、payload、marker、montage 严格 schema |
| `src/Als.Import/Metadata/*.cs` | v2 JSON DTO 与逐字段验证 |
| `src/Als.Import/Compilation/AlsAnimationSet*.cs` | 全局 event/marker ID、montage 引用、definition digest |
| `assets/config/p5a_animation_runtime.json` | P5A 唯一正式 event/sync/transition/action stable-ID 配置 |
| `src/Als.Import/Compilation/AlsP5aAnimationRuntimeProfile*.cs` | profile、section graph 和扁平 Action timeline 编译 |
| `src/Als.Import/Compilation/AlsP5OccurrenceLayout*.cs` | 全局 occurrence/authority ID 的唯一 versioned 编译器 |
| `src/Als.Import/Compilation/AlsP5CoreRuntimeBinding*.cs` | Oracle 与 Godot 共用的唯一 Import-to-Core owned snapshot bridge |
| `src/Als.Core/Contracts/AlsP5RuntimeContracts.cs` | P5A 固定宽度 ABI、reason/result code |
| `src/Als.Core/Curves/AlsCurveRuntime.cs` | Constant/Linear/Cubic、loop 与 normalized blend |
| `src/Als.Core/Events/AlsTimelineRuntime.cs` | occurrence、authority、Notify State 和事务 event buffer |
| `src/Als.Core/Sync/AlsSyncRuntime.cs` | Leader/Follower marker pair 与 foot phase |
| `src/Als.Core/Transitions/AlsDynamicTransitionRuntime.cs` | ALS 顺序的四槽动态过渡选择 |
| `src/Als.Core/Actions/AlsActionPlayer.cs` | 单 Action lane、section、priority 与 outcome |
| `src/Als.Core/Animation/AlsP5Runtime.cs` | Core 子系统固定顺序与整帧事务 |
| `tools/unreal/AlsLocomotionTrace/**` | P5A ALS-Refactored/UE cross-engine trace |
| `tools/schemas/als_p5a_trace_plan.schema.json` | Oracle 生成、UE 消费的 semantic-source sidecar 合同 |
| `tools/Als.P5aOracle/**` | 同时引用 Import/Core 并传递唯一 layout 的 port-oracle host |
| `tests/Als.Core.Tests/Fixtures/P5A/*.json` | versioned P5A golden |
| `src/Als.Godot/Animation/AlsP5aAnimationRuntimeBinding.cs` | Import definition 到 Core/Godot integer binding adapter |
| `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs` | additive Transition lane 与 full-body Action lane |
| `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs` | 双 lane prepared/apply/commit/rollback 事务 |
| `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs` | P5A candidate state、一次 advance、timeline 与 atomic publication |
| `src/Als.Godot/Locomotion/AlsP3CommitStage.cs` | generation 校验、main-only sink 与 mirror 更新 |
| `src/Als.Godot/Locomotion/AlsP5aEventSink.cs` | immutable event/outcome main-thread sink |
| `src/Als.Godot/Locomotion/AlsP5aCommittedStateMirror.cs` | committed Notify State 与 lifecycle synthetic End |
| `src/Als.Godot/Locomotion/AlsP5aFailureExchange.cs` | failed Worker attempt 的单槽有界 SPSC record |
| `src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs` | Main 验证后的 failure record 诊断环 |
| `src/Als.Godot/Locomotion/P5a*Smoke.cs` | binding、graph、timeline、transaction、lifecycle、demo smoke |
| `src/Als.Godot/Locomotion/P5aAnimationHarness.cs` | 1/10 single/parallel 矩阵与 allocation 采样 |
| `scripts/verify-p5a*.ps1` | focused、matrix、demo、regression 和 closure gate |
| `docs/verification/p5a-demo-manual.md` | 交互 Demo 的版本化人工验收记录，不由 headless smoke 冒充 |
| `docs/architecture/p5a-event-action-runtime.md` | 最终合同、真实资产证据、性能和手工验收记录 |

## Execution Contract

计划在既有 worktree `../GodotALS-p5a-events-actions`、分支 `feature/p5a-events-actions` 上执行。规格提交 `ce292b2` 必须是 `HEAD` 祖先。Task 1-5 为 Asset/Import；Task 6-12 为 Core；Task 13 为 cross-engine oracle；Task 14-17 为 Godot production integration；Task 18-22 为派发、语义、矩阵、Demo 与完整闭环。

Task 1-5 必须串行，因为它们共享 manifest/definition/profile。Task 6 是 Core ABI owner，之后 Task 7、9、10 可以由独立 agent 实现，但合并必须按任务编号进行；Task 8、11、12 依赖前序状态合同。Task 14-17 必须由一个 Godot integration owner 串行处理共享 hot files。每次进入下一任务前都要运行上一任务的 GREEN 命令并提交。

每个新 PowerShell 会话先定义：

```powershell
$repo = '../GodotALS-p5a-events-actions'
$godotExe = 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$unrealEditorCmd = '../UnrealEngine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$uProject = '../AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
$referenceRoot = '../GodotALS-References\ALS-Refactored'
Set-Location $repo
```

### Task 0: Verify the Existing Isolated Baseline

**Files:**
- No tracked file changes

- [ ] **Step 1: Invoke the required execution and TDD skills**

Invoke `superpowers:subagent-driven-development` or `superpowers:executing-plans`, then invoke `superpowers:test-driven-development`. Keep one task per implementation agent and run review before merging each task.

- [ ] **Step 2: Verify branch, ancestry and worktree isolation**

Run:

```powershell
git status --short
git branch --show-current
git merge-base --is-ancestor ce292b2 HEAD
git -C . status --short
```

Expected: feature worktree is clean; branch is `feature/p5a-events-actions`; ancestry exits `0`; the main checkout may show only the user-owned edit to `docs/superpowers/plans/2026-08-28-p4-aim-layering-foot-placement.md`. Do not edit, restore, stage or commit that main-checkout file.

- [ ] **Step 3: Verify the copied canonical generated batch**

Run:

```powershell
(Get-ChildItem assets/generated/als_v4 -Recurse -File).Count
(Get-FileHash assets/generated/als_v4/als_manifest.json -Algorithm SHA256).Hash
```

Expected before v2 export: `288` local files and manifest SHA-256 `F12C56C705F05C7C55E77955BD14A3729A2E96D5FAA669FDBD759702B0EA846E`.

- [ ] **Step 4: Establish the green P4 baseline**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug
pwsh -NoProfile -File scripts/verify-p4.ps1 -GodotExecutable $godotExe -Focused
```

Expected: Core `539/539`, Import `249/249`, then `P4_FOCUSED_VERIFICATION_OK`; no `SCRIPT ERROR:` or unapproved `ERROR:`. Task 0 creates no commit.

### Task 1: Export Deterministic Typed Sequence and Montage Timelines

**Files:**
- Modify: `tools/unreal/AlsGodotExporter/AlsGodotExporter.uplugin`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsNotifyClassRegistry.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsNotifyClassRegistry.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportTypes.h`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.h`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsCompositeAssetReader.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`
- Modify: `scripts/build-als-exporter.ps1`
- Modify: `tests/BuildAlsExporter.Tests.ps1`
- Create: `tests/Als.Import.Tests/AlsTimelineExporterSourceContractTests.cs`

- [ ] **Step 1: Add failing exporter source-contract tests**

Add tests named `ExporterDeclaresEveryTypedTimelineField`, `RegistryUsesExplicitClassAliasesWithoutDisplayNameInference`, `SequenceAndMontageBothEmitTimeline`, `StableIdsIncludeSourceIndexAndClassPath`, `NativeTickModesMapExplicitly`, `NameNoneSectionNormalizesToEmptyString`, `DescriptorDeclaresExporterVersionTwo`, and `ReadyMarkerDeclaresExporterVersionTwo`. The tests inspect the production C++ sources, plugin descriptor and build gate and require the exact public native contract:

```cpp
struct FAlsExportedTimelineEntry
{
    FString StableEventId;
    FString Kind;
    FString SourceClassPath;
    FString DisplayName;
    double TimeSeconds{0.0};
    double DurationSeconds{0.0};
    double TriggerWeightThreshold{0.0};
    FString TickMode;
    int32 SourceIndex{INDEX_NONE};
    int32 TrackIndex{INDEX_NONE};
    TSharedPtr<FJsonObject> Payload;
};

struct FAlsExportedSyncMarker
{
    FString StableMarkerId;
    FString Name;
    double TimeSeconds{0.0};
    int32 SourceIndex{INDEX_NONE};
    int32 TrackIndex{INDEX_NONE};
};
```

- [ ] **Step 2: Run RED tests**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter FullyQualifiedName~AlsTimelineExporterSourceContractTests
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/BuildAlsExporter.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
```

Expected: FAIL because no registry exists, Montage has no timeline, marker audit fields are absent, and producer version is still `1.0.0`.

- [ ] **Step 3: Implement the explicit class registry and stable IDs**

Expose this native API:

```cpp
static bool FAlsNotifyClassRegistry::Export(
    const FAnimNotifyEvent& NotifyEvent,
    const FString& AssetStableId,
    int32 SourceIndex,
    FAlsExportedTimelineEntry& OutEntry,
    FString& OutError);
```

The registry matches exact C++ class paths and reviewed Blueprint generated-class aliases. It emits only the six approved kinds and their exact payload shape. Unknown classes emit `Generic` plus `{}` while preserving `sourceClassPath` and `displayName`. It must not branch on `NotifyName`, display text or substring matching. Map UE's native notify tick enum branches explicitly to `Queued` and `BranchingPoint`; a native self-test constructs one of each and the exporter source contract rejects a hard-coded single mode.

Compute lowercase SHA-1 from UTF-8 strings:

```text
<assetStableId>|timeline|<sourceIndex>|<sourceClassPath>
<assetStableId>|marker|<sourceIndex>|<name>
```

Reject duplicate source indices, missing class/name, non-finite times/thresholds and invalid duration before publication.

- [ ] **Step 4: Emit identical structures for Sequence and Montage**

`FAlsAnimationMetadataReader` reads `UAnimSequenceBase::Notifies` and authored sync markers. `FAlsCompositeAssetReader` reads `UAnimMontage::Notifies` directly in addition to existing sections/slots/segments. Preserve UE array index as `sourceIndex`; obtain `trackIndex` from the authored notify/marker track. Normalize UE `NAME_None` section links to JSON `""`; never serialize the literal `"None"`. Sort serialized entries by `timeSeconds`, then `sourceIndex`, `trackIndex`, and stable ID using ordinal comparison. Change manifest schema to `2`, the `.uplugin` `Version` / `VersionName` to `2` / `2.0.0`, and every ready/producer declaration to `2.0.0`; source-contract and Pester tests must reject disagreement between these version sources.

- [ ] **Step 5: Run GREEN source/build gates**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter FullyQualifiedName~AlsTimelineExporterSourceContractTests
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/BuildAlsExporter.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
pwsh -NoProfile -File scripts/build-als-exporter.ps1 -EngineRoot ../UnrealEngine -UnrealProject $uProject
```

Expected: tests PASS, plugin build exits `0`, and ready output contains `GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0`.

- [ ] **Step 6: Commit**

```powershell
git add tools/unreal/AlsGodotExporter/AlsGodotExporter.uplugin tools/unreal/AlsGodotExporter/Source scripts/build-als-exporter.ps1 tests/BuildAlsExporter.Tests.ps1 tests/Als.Import.Tests/AlsTimelineExporterSourceContractTests.cs
git commit -m "feat(exporter): emit deterministic v2 typed timelines"
```

### Task 2: Require the Strict Manifest v2 Schema and DTOs

**Files:**
- Modify: `tools/schemas/als_manifest.schema.json`
- Modify: `src/Als.Import/Manifest/AlsManifest.cs`
- Modify: `src/Als.Import/Manifest/AlsManifestSerializer.cs`
- Modify: `src/Als.Import/Metadata/AlsAnimationMetadata.cs`
- Modify: `src/Als.Import/Metadata/AlsCompositeMetadata.cs`
- Modify: `src/Als.Import/Validation/AlsManifestValidationOptions.cs`
- Modify: `src/Als.Import/Validation/AlsManifestValidator.cs`
- Modify: `tests/Als.Import.Tests/Fixtures/valid_manifest.json`
- Create: `tests/Als.Import.Tests/Fixtures/p5a_typed_timeline_manifest.json`
- Modify: `tests/Als.Import.Tests/AlsManifestSerializerTests.cs`
- Modify: `tests/Als.Import.Tests/AlsManifestValidatorTests.cs`

- [ ] **Step 1: Migrate fixtures and add failing strict cases**

Set both fixtures to `schemaVersion: 2` and `exporterVersion: "2.0.0"`. Add one valid row for every typed kind, at least one Notify State (`durationSeconds > 0`), one Montage-owned event and a Left/Right marker pair. Use this exact field shape:

```json
{
  "stableEventId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "kind": "EarlyBlendOut",
  "sourceClassPath": "/Script/ALS.AlsAnimNotifyState_EarlyBlendOut",
  "displayName": "Early Blend Out",
  "timeSeconds": 0.25,
  "durationSeconds": 0.5,
  "triggerWeightThreshold": 0.25,
  "tickMode": "BranchingPoint",
  "sourceIndex": 3,
  "trackIndex": 0,
  "payload": {
    "blendOutSeconds": 0.2,
    "checkInput": true,
    "checkLocomotionMode": true,
    "locomotionMode": "Grounded",
    "checkRotationMode": true,
    "rotationMode": "LookingDirection",
    "checkStance": true,
    "stance": "Standing"
  }
}
```

Include valid entries for both `Queued` and `BranchingPoint`. Add a terminal Montage section whose `nextSection` is `""`. Add tests rejecting v1, missing/unknown fields, illegal kind-specific payload fields, NaN/Infinity text, time outside source length, state end after source length, threshold outside `[0,1]`, negative/duplicate source index, malformed SHA-1, duplicate event/marker ID, illegal tick mode, Montage without timeline, and the legacy literal `nextSection: "None"`.

Freeze payload discrimination exactly as follows: `Generic -> {}`; `Footstep -> foot: Unspecified/Left/Right`; `SetAction -> action: None/Rolling/Mantling/Ragdolling/GettingUp`; `SetGroundedEntry -> mode: None/FromRoll`; `EarlyBlendOut -> blendOutSeconds + four check flags (`checkInput`, `checkLocomotionMode`, `checkRotationMode`, `checkStance`) + the frozen Grounded/InAir/Mantling/Ragdoll/Recovering locomotionMode + VelocityDirection/LookingDirection/Aiming rotationMode + Standing/Crouching stance`; `RootMotionScale -> finite translationScale >= 0`. Disabled EarlyBlendOut comparisons still require their enum fields.

- [ ] **Step 2: Run RED serializer/validator tests**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsManifestSerializerTests|FullyQualifiedName~AlsManifestValidatorTests"
```

Expected: FAIL because supported schema is `1` and current DTOs only expose `name/time/duration/sourceIndex`.

- [ ] **Step 3: Implement exact DTOs and schema discrimination**

Introduce `AlsTimelineEventMetadata`, six sealed payload DTOs behind a kind-aware converter, and the expanded `AlsAnimationSyncMarkerMetadata`. Apply `JsonUnmappedMemberHandling.Disallow`. `AlsManifestSerializer.Load` and `AlsManifestValidator` reject any schema other than `2`; `animations[*].metadata.timeline` and `montages[*].metadata.timeline` are required arrays, including when empty.

Montages continue to use `/Script/Engine.AnimMontage`, keep their section/slot/segment contract and gain the same timeline definition. Curves must use the structured object form; v2 never accepts the legacy string-array form.

- [ ] **Step 4: Run GREEN schema regressions**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsManifestSerializerTests|FullyQualifiedName~AlsManifestValidatorTests|FullyQualifiedName~AlsMetadataReferenceTests"
```

Expected: PASS; every v2 fixture round-trips without field loss and all negative JSON paths report stable validation locations.

- [ ] **Step 5: Commit**

```powershell
git add tools/schemas/als_manifest.schema.json src/Als.Import tests/Als.Import.Tests/Fixtures tests/Als.Import.Tests/AlsManifestSerializerTests.cs tests/Als.Import.Tests/AlsManifestValidatorTests.cs tests/Als.Import.Tests/AlsMetadataReferenceTests.cs
git commit -m "feat(import): require manifest schema v2 timelines"
```

### Task 3: Compile Global Timeline, Marker and Montage Definitions

**Files:**
- Modify: `src/Als.Import/Compilation/AlsAnimationSetDefinition.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationSetCompiler.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationSetPayload.cs`
- Modify: `src/Als.Import/Compilation/AlsAssetIndex.cs`
- Modify: `src/Als.Import/Compilation/AlsAnimationEventDigest.cs`
- Modify: `tests/Als.Import.Tests/AlsAnimationSetCompilerTests.cs`
- Modify: `tests/Als.Import.Tests/AlsAnimationEventDigestTests.cs`
- Create: `tests/Als.Import.Tests/AlsP5aManifestCompilationTests.cs`

- [ ] **Step 1: Add failing compilation and digest tests**

Cover deterministic manifest reorder, global event/marker IDs, recomputed event/marker stable-ID formula mismatch, kind payload validation, unresolved segment references, duplicate/descending section times, empty terminal `nextSection`, unknown non-empty `nextSection`, zero/non-finite play rate, invalid loop count/range, multiple authored events with equal payload, and Montage timeline bounds. Add one mutation test for every curve/event/marker/section/slot/segment/payload field, including event `sourceClassPath` / `displayName`; each mutation must change `DefinitionDigest`.

- [ ] **Step 2: Run RED compilation tests**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5aManifestCompilationTests|FullyQualifiedName~AlsAnimationSetCompilerTests|FullyQualifiedName~AlsAnimationEventDigestTests"
```

Expected: FAIL because event IDs are clip-local, markers lack source/track/stable identity, Montage has no timeline, and digest omits these semantics.

- [ ] **Step 3: Add immutable compiled definitions**

Use Import-owned names to avoid ambiguity with Core hot-path structs:

```csharp
public sealed record AlsCompiledTimelineEventDefinition(
    int EventId,
    string StableEventId,
    AlsCompiledTimelineEventKind Kind,
    int SourceAssetId,
    string SourceClassPath,
    string DisplayName,
    float TimeSeconds,
    float DurationSeconds,
    float TriggerWeightThreshold,
    AlsCompiledTimelineTickMode TickMode,
    int SourceIndex,
    int TrackIndex,
    AlsCompiledTimelinePayloadDefinition Payload);

public sealed record AlsAnimationSyncMarkerDefinition(
    int MarkerId,
    string StableMarkerId,
    string Name,
    float TimeSeconds,
    int SourceIndex,
    int TrackIndex);
```

Before assigning IDs, recompute lowercase SHA-1 from `<assetStableId>|timeline|<sourceIndex>|<sourceClassPath>` and `<assetStableId>|marker|<sourceIndex>|<name>` and reject any mismatch. Assign contiguous global event IDs by ordinal `StableEventId`, and marker IDs by ordinal `StableMarkerId`, after duplicate detection. `AlsAnimationDefinition` and `AlsMontageDefinition` hold their sorted event arrays; Montage holds validated integer section/slot/segment references and maps terminal empty links to `NextSectionId = -1`. Preserve authored `sourceIndex`, `sourceClassPath` and `displayName`; do not deduplicate different authored sources with equal payload.

- [ ] **Step 4: Make payload serialization and digest exhaustive**

`AlsAnimationSetPayload` serializes/deserializes every new field and computes digest from canonical binary order, not JSON property order. Include pre/post curve infinity audit values, event stable ID/source class/display name, every compact payload source value, marker name/ID/time/source/track, section name/next/start, slot name, segment ID/range/rate/loops and Montage timeline. Raw float bits, signed zero and distinct NaN payloads remain distinguishable where the contract permits values.

Extend `AlsFloatCurveDefinition` with non-positional trailing `init` properties `PreInfinity` and `PostInfinity`, both defaulting to `Constant`, so every existing P4 constructor remains source-compatible while the digest/audit contract no longer drops v2 curve metadata. The compiler always sets both properties from v2. Runtime clip loop policy still owns time normalization; infinity modes are not applied a second time.

- [ ] **Step 5: Run GREEN fixture-backed Import tests**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5aManifestCompilationTests|FullyQualifiedName~AlsAnimationSetCompilerTests|FullyQualifiedName~AlsAnimationEventDigestTests"
```

Expected: focused fixture-backed tests PASS. Do not run repository/canonical Import tests yet: the local canonical batch is deliberately still v1 until Task 4 performs the real dual export. Task 4 owns the first full Import/P2B GREEN run against v2.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Import/Compilation tests/Als.Import.Tests/AlsAnimationSetCompilerTests.cs tests/Als.Import.Tests/AlsAnimationEventDigestTests.cs tests/Als.Import.Tests/AlsP5aManifestCompilationTests.cs
git commit -m "feat(import): compile integer timeline and montage bindings"
```

### Task 4: Re-export the Complete Canonical ALS Batch and Update Its Lock

**Files:**
- Modify: `scripts/verify-p2a.ps1`
- Modify: `scripts/compare-p2a-exports.ps1`
- Modify: `scripts/asset-lock-functions.ps1`
- Modify: `reference/als-v4-export.lock.json`
- Modify: `tests/BuildAlsExporter.Tests.ps1`
- Create: `tests/VerifyP5aExport.Tests.ps1`
- Modify: `tests/VerificationScripts.Tests.ps1`
- Generated locally, ignored: `assets/generated/als_v4/**`

- [ ] **Step 1: Add failing v2 export/lock tests**

Require producer `2.0.0`, manifest schema `2`, Sequence and Montage timeline arrays, stable event/marker IDs, audited `Queued`/`BranchingPoint` counts whose sum equals total events, no literal `nextSection: "None"`, two independent export roots, byte equality before publication, exact inventory `267/141/126`, excluded audio, and one atomic publication transaction for canonical bytes plus the lock only after comparison. Add fault-injection cases at comparison, canonical swap and lock swap: every failure must preserve the previous canonical tree and previous lock byte-for-byte, and remove staging/backup directories. Resolve and validate every staging, backup, canonical and lock path as an absolute descendant of the repository before any move or deletion. Lock `schemaVersion` remains `1`; it is not the manifest schema.

- [ ] **Step 2: Run RED Pester tests**

Run:

```powershell
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path @("tests/BuildAlsExporter.Tests.ps1","tests/VerifyP5aExport.Tests.ps1","tests/VerificationScripts.Tests.ps1"); if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
```

Expected: FAIL because P2A gates and current lock still require `1.0.0` / the v1 SHA.

- [ ] **Step 3: Upgrade the existing P2A transaction instead of creating a second exporter flow**

Keep `verify-p2a.ps1` as the one build/dry-run/export/determinism/publish path. It must export first to canonical staging, export again to `artifacts/p2a-determinism/als_v4`, call `compare-p2a-exports.ps1`, validate schema/inventory/timeline, and only then enter a recoverable two-resource publish transaction. Write and flush a repository-local transaction journal before moving anything; on startup, recover any incomplete journal before a new export. Move the old canonical tree and lock to validated repository-local backups, install both candidates, mark the journal committed, and delete journal/backups only after both swaps succeed. On any exception restore both old resources byte-for-byte and clean all staging/backup paths. `-UpdateAssetLock` controls whether this joint transaction is allowed; no code path may publish only one resource.

- [ ] **Step 4: Run the GREEN two-export certificate against real UE**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-p2a.ps1 -EngineRoot ../UnrealEngine -UnrealProject $uProject -UpdateAssetLock
```

Expected: ready/dry-run/export audit succeeds; native self-test covers both tick modes; two full roots are byte-identical; `assetCount=267`, `fileCount=141`, `animationCount=126`; canonical manifest is v2; tick-mode audit totals match all authored events; terminal sections use empty links; audio is absent; lock records exporter `2.0.0` and the new lowercase SHA-256.

- [ ] **Step 5: Validate the published canonical batch through Import and Godot P2B**

Run:

```powershell
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug
pwsh -NoProfile -File scripts/verify-p2b.ps1 -GodotExecutable $godotExe
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path @("tests/BuildAlsExporter.Tests.ps1","tests/VerifyP5aExport.Tests.ps1","tests/VerificationScripts.Tests.ps1"); if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
```

Expected: all tests PASS, `P2B_VERIFICATION_OK`, and canonical lock matches the local v2 manifest.

- [ ] **Step 6: Commit tracked evidence only**

```powershell
git add scripts/verify-p2a.ps1 scripts/compare-p2a-exports.ps1 scripts/asset-lock-functions.ps1 reference/als-v4-export.lock.json tests/BuildAlsExporter.Tests.ps1 tests/VerifyP5aExport.Tests.ps1 tests/VerificationScripts.Tests.ps1
git commit -m "chore(assets): publish verified ALS manifest v2 lock"
```

Do not force-add `assets/generated/als_v4/**`.

### Task 5: Compile the Strict P5A Runtime Profile

**Files:**
- Create: `assets/config/p5a_animation_runtime.json`
- Create: `scripts/generate-p5a-profile.ps1`
- Create: `src/Als.Import/Compilation/AlsP5aAnimationRuntimeProfile.cs`
- Create: `src/Als.Import/Compilation/AlsP5aAnimationRuntimeProfileCompiler.cs`
- Create: `src/Als.Import/Compilation/AlsP5OccurrenceLayout.cs`
- Create: `src/Als.Import/Compilation/AlsP5OccurrenceLayoutCompiler.cs`
- Create: `tests/GenerateP5aProfile.Tests.ps1`
- Create: `tests/Als.Import.Tests/AlsP5aAnimationRuntimeProfileCompilerTests.cs`
- Create: `tests/Als.Import.Tests/AlsP5OccurrenceLayoutCompilerTests.cs`

- [ ] **Step 1: Add failing generator and compiler tests**

Cover schema/unknown fields, unique semantic IDs, all six kinds, exact `Enable_Transition` curve semantic with additive-to-default/missing-value policy, exactly one Sync group (reject zero or two), explicit member/loop/leader rules, missing or duplicate Left/Right marker pairs, the four stance/foot transition keys, non-finite settings, unknown asset IDs, cross-skeleton binding, transition additive base/type, non-additive Roll sequence, duplicate action name, multiple Montage slots, missing section, non-loop section cycle, loop cycle not returning to start, invalid priority/rate/blend, unresolved segment sequence, invalid segment montage/clip ranges, clipped/multi-loop event ordinals, stable occurrence/authority layout digest and demo-case references that do not resolve to a declared transition/action.

- [ ] **Step 2: Run RED profile tests**

Run:

```powershell
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/GenerateP5aProfile.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5aAnimationRuntimeProfileCompilerTests|FullyQualifiedName~AlsP5OccurrenceLayoutCompilerTests"
```

Expected: FAIL because the profile, generator and compiler do not exist.

- [ ] **Step 3: Generate the exact tracked profile**

The generator resolves exact object paths against the v2 manifest and atomically writes this logical shape:

```json
{
  "schemaVersion": 1,
  "eventSemantics": [
    {"kind":"Generic","semanticId":0},
    {"kind":"Footstep","semanticId":1},
    {"kind":"SetAction","semanticId":2},
    {"kind":"SetGroundedEntry","semanticId":3},
    {"kind":"EarlyBlendOut","semanticId":4},
    {"kind":"RootMotionScale","semanticId":5}
  ],
  "curveSemantics": {
    "allowTransitions": {
      "sourceName":"Enable_Transition",
      "missingValue":1.0,
      "blendMode":"AdditiveToDefault",
      "clampMinimum":0.0,
      "clampMaximum":1.0
    }
  },
  "syncGroups": [
    {"name":"Grounded","leftMarker":"Left","rightMarker":"Right","members":[
      {"animation":"21c24bd7df5192db2e2a860457f2b7b0681de41d","loopPolicy":"Loop","canLead":true},
      {"animation":"245ea51e30449a60b7d2b783ec0f6d24ec3bacc0","loopPolicy":"Loop","canLead":true},
      {"animation":"32fe18c71ccb860fe35c01d6b2b10fa2e4d98297","loopPolicy":"Loop","canLead":true},
      {"animation":"44a7f89b2c1dac832ca63753c131a037420f9d7e","loopPolicy":"Loop","canLead":true},
      {"animation":"572c3c83c9007964c233db4c7288ae38e20c3dec","loopPolicy":"Loop","canLead":true},
      {"animation":"6124eafdcbeaaf04bca366add34c821faa0e4963","loopPolicy":"Loop","canLead":true},
      {"animation":"859f8a49c55747e7382a1ae15970b23cc12f3f85","loopPolicy":"Loop","canLead":true},
      {"animation":"8ae1b9703a7d0144e570247d88629530b376885f","loopPolicy":"Loop","canLead":true},
      {"animation":"8bd6ad52ad04a2ad23b47187886c630701df5260","loopPolicy":"Loop","canLead":true},
      {"animation":"8eb8837c9f32973628b83ed2b98f7d68a0e82aa9","loopPolicy":"Loop","canLead":true},
      {"animation":"945bdda63e8a379694c792c3545fe11dd166b724","loopPolicy":"Loop","canLead":true},
      {"animation":"a4c6e0e455e7be7355cdd7c3ce49272d07773b18","loopPolicy":"Loop","canLead":true},
      {"animation":"b07a51bbab122c81679ac30d3f2f78f45ac14dc8","loopPolicy":"Loop","canLead":true},
      {"animation":"db60b2c35ce5ef5216c782fc1f33549cbcf8278d","loopPolicy":"Loop","canLead":true},
      {"animation":"eb84a748fee4615754ce3cbcd3c259b33918b935","loopPolicy":"Loop","canLead":true},
      {"animation":"f9ec8804e251f03c57e04dde4db9bd921457bae4","loopPolicy":"Loop","canLead":true},
      {"animation":"fc2d3a4142a1bd82d20877d806c783ff56dfe688","loopPolicy":"Loop","canLead":true}
    ]}
  ],
  "dynamicTransition": {
    "distanceMeters":0.08,
    "blendSeconds":0.2,
    "playRate":1.5,
    "cooldownFrames":2,
    "slots":[
      {"stance":"Standing","foot":"Left","animation":"3e23712571d6bbea8744fc94dad0904a6a0a0b5d"},
      {"stance":"Standing","foot":"Right","animation":"97d46bf9858376893c1c34a128c27044b4467d82"},
      {"stance":"Crouching","foot":"Left","animation":"3e23712571d6bbea8744fc94dad0904a6a0a0b5d"},
      {"stance":"Crouching","foot":"Right","animation":"97d46bf9858376893c1c34a128c27044b4467d82"}
    ]
  },
  "actions": [
    {"name":"Roll","montage":"2d9341182885d90ad666fff32c025937438b1827","slot":"BaseLayer","startSection":"Default","priority":100,"interruptible":true,"playRate":1.0,"blendSeconds":0.2,"loopPolicy":"Once"}
  ],
  "demoCases": {
    "transitionStance":"Standing",
    "transitionFoot":"Left",
    "rollAction":"Roll"
  }
}
```

P5A ABI supports exactly one Sync group because `AlsFrameResult` carries one `AlsSyncResult`; the profile compiler rejects `syncGroups.Count != 1` before allocating any runtime IDs. Multi-group support requires a future explicit result-buffer ABI revision and is out of scope here. `Grounded.members` is the literal, ordinal list of the 17 P3 production locomotion assets with authored Left/Right markers; Overlay-only marker clips and pose/action clips are not silently admitted. Every member explicitly declares `loopPolicy` and `canLead`. `curveSemantics.allowTransitions` binds the actual exported ALS V4 source curve `Enable_Transition`; missing curves contribute the frozen base value `1`, while present negative additive samples reduce and clamp the final value to `[0,1]`. Runtime never probes `AllowTransitions` or any alternate string. `dynamicTransition.slots` has exactly Standing/Crouching x Left/Right. Crouching explicitly reuses the standing IDs because ALS V4 exports only the two transition clips:

```text
Left  = 3e23712571d6bbea8744fc94dad0904a6a0a0b5d
Right = 97d46bf9858376893c1c34a128c27044b4467d82
```

The real Roll sequence is `39eecd72ffdddb8ba0eb2bb0683f1c958d68fdd9`; the selected Montage is `2d9341182885d90ad666fff32c025937438b1827`, slot `BaseLayer`, section `Default`.

- [ ] **Step 4: Compile names and stable IDs away**

Expose:

```csharp
public static AlsP5aAnimationRuntimeProfile Compile(
    string json,
    AlsAnimationSetDefinition animationSet);
```

The result contains only integer event semantics, per-animation `Enable_Transition` curve IDs (or `-1`) plus its numeric combine policy, group/member/marker IDs, four transition animation IDs plus their validated additive base animation IDs, numeric settings, action/montage/slot/section IDs including finite positive `MontageDurationSeconds`, integer demo-case references and frozen arrays. Require both Transition clips to resolve the exported `ALS_N_Pose` additive base `621a81bf492cb9120b45cfd91b685854afb7dc75`; require every Roll segment Sequence to be non-additive.

At compile time emit a stable `AlsCompiledActionSegmentBinding[]` containing action/slot/segment IDs, Sequence animation ID, Montage start/end, source animation start/end, play rate and loop count. Also expand each Action into `AlsCompiledActionTimelineEntry[]`: combine Montage-authored timeline with each referenced Sequence timeline, apply segment start/range/play rate and every declared loop iteration, retain `SegmentBindingIndex`, Montage-vs-Sequence source kind plus source animation/action identity, and sort by mapped Montage time/source/phase key. For both source kinds the compiled entry's `TimeSeconds` and `DurationSeconds` are in Action Montage coordinates; Sequence-local time never enters the Timeline runtime. Every compiled entry stores `BoundaryOrdinal`: ordinary/Montage entries use `0`; a flattened Sequence occurrence uses its zero-based loop-iteration ordinal plus `1`, and Begin/Tick/End belonging to the same mapped State occurrence share that value. Runtime copies this field verbatim and never derives it by rescanning the compiled table.

Freeze Sequence range clipping per loop iteration. Require positive finite segment play rate/range and validate in double with `1e-8` tolerance that `MontageEnd-MontageStart == LoopCount * (AnimationEnd-AnimationStart) / SegmentPlayRate`; pin the final mapped boundary to the exported `MontageEnd` after validation. Action definition play rate advances runtime Montage time but never participates in this authored-coordinate mapping. For source range `[animationStart, animationEnd]`, copy an instant only when its source time is inside that closed range. Treat a Notify State as active on authored half-open interval `[S,E)`: for each declared iteration compute `B=max(S,animationStart)` and `C=min(E,animationEnd)`, omit `C<=B`, and map `[B,C]` to that iteration. This moves a Begin before the range to the mapped iteration start and an End after the range to the mapped iteration end. A clipped End is a regular compiled End with `TerminationReason=None`, not a runtime interruption. At a loop cut, the previous iteration's clipped End sorts before the next iteration's clipped Begin at the same Montage time, so ownership never leaks across a source wrap. Traverse definitions in stable `SegmentBindingIndex -> EventId -> SourceIndex -> loop iteration` order, but set each flattened entry's value exactly to `BoundaryOrdinal=checked(loopIteration+1)`; distinct events use `EventId`, and distinct segments use their required occurrence handle. Tests cover formula mismatch, Begin before range, End after range, a State crossing two loop cuts, an instant on each endpoint, stable ordinals across frame splits/retry, ordinal overflow and compile/runtime buffer rollback. Validate that each section is covered by resolvable segment time and that every segment boundary maps to a finite clip phase. Runtime never remaps object paths or JSON.

Add one pure integer `AlsP5OccurrenceLayoutCompiler` in Import and use it everywhere occurrence or authority IDs are needed. It consumes the compiled P3 locomotion/P4 pose/P5A profiles and assigns contiguous handles in this exact order: fixed physical Base graph bank slots in profile/slot order, fixed Turn-bank slots, fixed Rotate-bank slots, Transition, each Action Montage in definition order, then each Action segment binding in compiled order. Member, non-member and Idle slots are all explicit. `GraphSlotIndex` identifies the physical bank; current/outgoing is transient runtime state, so an occurrence retains the same handle when its bank becomes outgoing. It separately assigns globally dense authority domains: one production P4 locomotion/Turn/Rotate replacement domain covering those slots, then Transition, then each Action's Montage and Sequence domains. Sync group IDs remain separate mapping metadata and only declared Base members receive Sync mappings. The returned immutable layout contains a version and digest; Task 13 copies it exactly once into the owned binding snapshot, whose `CreateOccurrenceLayoutView()` is then the sole view consumed by the Oracle and Task 14 Godot adapter. No consumer may duplicate its sorting/allocation algorithm or reconstruct entries from only the digest.

- [ ] **Step 5: Run GREEN generation and profile tests**

Run:

```powershell
pwsh -NoProfile -File scripts/generate-p5a-profile.ps1
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/GenerateP5aProfile.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5aAnimationRuntimeProfileCompilerTests|FullyQualifiedName~AlsP5OccurrenceLayoutCompilerTests"
```

Expected: `P5A_PROFILE_GENERATION_OK groups=1 members=17 transitions=4 actions=1 segments=1`, Pester PASS and compiler tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add assets/config/p5a_animation_runtime.json scripts/generate-p5a-profile.ps1 src/Als.Import/Compilation/AlsP5aAnimationRuntimeProfile.cs src/Als.Import/Compilation/AlsP5aAnimationRuntimeProfileCompiler.cs src/Als.Import/Compilation/AlsP5OccurrenceLayout.cs src/Als.Import/Compilation/AlsP5OccurrenceLayoutCompiler.cs tests/GenerateP5aProfile.Tests.ps1 tests/Als.Import.Tests/AlsP5aAnimationRuntimeProfileCompilerTests.cs tests/Als.Import.Tests/AlsP5OccurrenceLayoutCompilerTests.cs
git commit -m "feat(import): compile strict p5a runtime profile"
```

### Task 6: Freeze the P5A Core ABI, Event and Outcome Buffers

**Files:**
- Modify: `docs/superpowers/plans/2026-08-30-p5a-event-action-runtime.md`
- Modify: `docs/superpowers/specs/2026-08-30-p5a-event-action-runtime-design.md`
- Create: `src/Als.Core/Contracts/AlsP5RuntimeContracts.cs`
- Create: `src/Als.Core/Contracts/AlsP5OccurrenceLayoutContracts.cs`
- Create: `src/Als.Core/Events/AlsActionOutcome.cs`
- Create: `src/Als.Core/Events/AlsActionOutcomeBuffer.cs`
- Create: `src/Als.Core/Events/AlsP5FailureRecord.cs`
- Modify: `src/Als.Core/Events/AlsAnimationEvent.cs`
- Modify: `src/Als.Core/Events/AlsEventBuffer.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameInput.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameResult.cs`
- Modify: `src/Als.Core/Contracts/AlsRuntimeState.cs`
- Modify: `src/Als.Core/Diagnostics/AlsResultDigest.cs`
- Modify: `src/Als.Core/Simulation/AlsSyntheticLocomotionModel.cs`
- Create: `tests/Als.Core.Tests/AlsP5ContractTests.cs`
- Create: `tests/Als.Core.Tests/AlsP5FailureRecordTests.cs`
- Modify: `tests/Als.Core.Tests/ContractLayoutTests.cs`
- Modify: `tests/Als.Core.Tests/AlsEventBufferTests.cs`
- Modify: `tests/Als.Core.Tests/AlsResultDigestTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing unmanaged/layout/digest tests**

Tests must assert sequential unmanaged layout, explicit enum storage/value, default invalid IDs, insertion order, exact capacities, Action/Transition logical summaries versus visual-tail state, every lane graph/blend field, layout version/FNV bytes and one-mutation-per-entry-field digest behavior, stable failure-record identity/digest, and that every production/test constructor call supplies the full global occurrence identity. Preserve existing event phase numeric values and use a separate sort rank later:

```csharp
public enum AlsAnimationEventPhase : byte
{
    Trigger = 0,
    Begin = 1,
    Tick = 2,
    End = 3,
}
```

- [ ] **Step 2: Run RED ABI tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5ContractTests|FullyQualifiedName~AlsP5FailureRecordTests|FullyQualifiedName~ContractLayoutTests|FullyQualifiedName~AlsEventBufferTests|FullyQualifiedName~AlsResultDigestTests"
```

Expected: FAIL to compile because P5A contracts and outcome buffer are absent and `AlsAnimationEvent` lacks global occurrence identity.

- [ ] **Step 3: Implement frozen enums and compact payload**

Use exact reason/result values from the spec:

```csharp
public enum AlsP5FailureCode : ushort
{
    None = 0,
    InvalidDeltaTime = 1,
    NonFiniteInput = 2,
    InvalidBinding = 3,
    InvalidTimeline = 4,
    InvalidSyncGroup = 5,
    EventBufferOverflow = 6,
    StalePreparedFrame = 7,
    NonFiniteOutput = 8,
}

public enum AlsActionResultCode : ushort
{
    None = 0,
    Accepted = 1,
    Completed = 2,
    RejectedInvalidRequest = 3,
    RejectedMissingDefinition = 4,
    RejectedBusy = 5,
    RejectedLowerPriority = 6,
    InterruptedByReplacement = 7,
    InterruptedByExplicitCancel = 8,
    InterruptedByEarlyBlendOut = 9,
    InterruptedByLifecycle = 10,
    InterruptedByGeneration = 11,
    InterruptedByRuntimeFailure = 12,
}
```

Also add explicit byte enums with these exact mappings: `AlsTimelineEventKind` (`Generic=0`, `Footstep=1`, `SetAction=2`, `SetGroundedEntry=3`, `EarlyBlendOut=4`, `RootMotionScale=5`), `AlsTimelineTickMode` (`Queued=0`, `BranchingPoint=1`), `AlsTimelineSourceKind` (`Animation=0`, `Montage=1`, `MontageSegmentAnimation=2`), `AlsActionCommand` (`None=0`, `Start=1`, `Cancel=2`, `CancelForRuntimeFailure=3`), `AlsTransitionFoot` (`Left=0`, `Right=1`), `AlsTimelineFoot` (`Unspecified=0`, `Left=1`, `Right=2`), `AlsTimelineAction` (`None=0`, `Rolling=1`, `Mantling=2`, `Ragdolling=3`, `GettingUp=4`), `AlsTimelineGroundedEntryMode` (`None=0`, `FromRoll=1`), `AlsTimelineLocomotionMode` (`Grounded=0`, `InAir=1`, `Mantling=2`, `Ragdoll=3`, `Recovering=4`), `AlsTimelineRotationMode` (`VelocityDirection=0`, `LookingDirection=1`, `Aiming=2`) and `AlsTimelineStance` (`Standing=0`, `Crouching=1`). Freeze this compact payload layout:

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsCompactEventPayload(
    int SemanticId,
    int EnumValue0,
    int EnumValue1,
    int EnumValue2,
    float ScalarValue0,
    ushort Flags,
    AlsActionResultCode TerminationReason);
```

`Flags` contains only the four `EarlyBlendOut` check bits: bit 0 = `checkInput`, bit 1 = `checkLocomotionMode`, bit 2 = `checkRotationMode`, bit 3 = `checkStance`; bits 4..15 must be zero. Unused fields must be zero; Import adapter tests mutate every valid bit and reject every noncanonical high-bit or kind/field combination.

- [ ] **Step 4: Expand event, request, state and result contracts**

Use these exact public value surfaces:

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAnimationEvent(
    int EventId,
    int SourceAnimationId,
    int SourceActionId,
    int OccurrenceHandleId,
    long PlaybackEpoch,
    long PlaybackCycle,
    ulong OwnerToken,
    long EventSequence,
    int BoundaryOrdinal,
    float AnimationTime,
    float Weight,
    AlsTimelineEventKind Kind,
    AlsAnimationEventPhase Phase,
    AlsCompactEventPayload Payload);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionRequest(
    long RequestId,
    AlsActionCommand Command,
    int ActionDefinitionId,
    int StartSectionId,
    int Priority,
    uint SlotGeneration);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionOutcome(
    long RequestId,
    int ActionDefinitionId,
    long PlaybackEpoch,
    AlsActionResultCode ResultCode);

public struct AlsActionPlayerState
{
    public int ActionDefinitionId;
    public int SectionId;
    public int SegmentBindingIndex;
    public long RequestId;
    public long LastProcessedRequestId;
    public long LastProcessedCommandRequestId;
    public AlsActionCommand LastProcessedCommand;
    public long PlaybackEpoch;
    public float PlaybackTime;
    public int Priority;
    public byte Playing;
    public byte Interruptible;
}

public struct AlsDynamicTransitionState
{
    public int AnimationId;
    public int QueuedAnimationId;
    public long PlaybackEpoch;
    public float PreviousPlaybackTime;
    public float PlaybackTime;
    public int CooldownFrames;
    public AlsTransitionFoot Foot;
    public AlsTransitionFoot QueuedFoot;
    public byte Active;
    public byte Queued;
}

public struct AlsLaneBlendState
{
    public int OutgoingOccurrenceHandleId;
    public int OutgoingAnimationId;
    public int OutgoingBindingIndex;
    public long OutgoingPlaybackEpoch;
    public float OutgoingClipTime;
    public float LaneWeight;
    public float IncomingMix;
    public float BlendSeconds;
    public byte VisualActive;
    public byte OutgoingActive;
}

public readonly record struct AlsLaneGraphSource(
    int OccurrenceHandleId, int AnimationId, int BindingIndex,
    long PlaybackEpoch, float PreviousClipTime, float CurrentClipTime,
    float ContributingDeltaSeconds, float PlayRate, byte Active);

public readonly record struct AlsLaneGraphInstruction(
    AlsLaneGraphSource Outgoing, AlsLaneGraphSource Incoming,
    float LaneWeight, float IncomingMix,
    float OutgoingEffectiveWeight, float IncomingEffectiveWeight);

public enum AlsP5OccurrenceSourceKind : byte
{
    Base = 1,
    Turn = 2,
    Rotate = 3,
    Transition = 4,
    ActionMontage = 5,
    ActionSequence = 6
}

public readonly record struct AlsP5OccurrenceLayoutEntry(
    AlsP5OccurrenceSourceKind SourceKind,
    int SourceBindingIndex,
    int GraphSlotIndex,
    int OccurrenceHandleId,
    int AuthorityGroupId);

public readonly ref struct AlsP5OccurrenceLayoutView
{
    public readonly int Version;
    public readonly ulong Digest;
    public readonly ReadOnlySpan<AlsP5OccurrenceLayoutEntry> Entries;

    public AlsP5OccurrenceLayoutView(
        int version,
        ulong digest,
        ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries)
    {
        Version = version;
        Digest = digest;
        Entries = entries;
    }
}
```

Layout version is exactly `1`. `SourceBindingIndex` addresses the frozen profile-order binding, while `GraphSlotIndex` addresses its fixed physical graph bank/slot and is `0` when the source has no separate slot. Runtime current/outgoing role is not part of the layout key and cannot change a live occurrence handle. The only public Core validation entry point is `AlsP5OccurrenceLayoutContract.Validate(version, digest, entries)`; it validates without allocation, sorting or mutation and privately recomputes FNV-1a 64 over little-endian `Version`, entry count and every field in declaration order, excluding struct padding. `Digest=0`, duplicate keys/handles, negative fields, non-ordinal handles, sparse/out-of-range authorities and nonzero Transition/Action graph slots are invalid. Core never references Import. Task 6 tests only the Core value view, validator and exact caller-supplied bytes. Task 13 owns the real Import layout -> exact Core field-copy bridge, and Task 14 owns Core snapshot -> Godot adapter comparison; neither round-trip is claimed here and no host may allocate IDs or alter their order.

Update the design spec in this task, not at documentation cleanup, with these normative amendments: complete occurrence/ordinal ownership identity, layout v1/digest and exact production handles, local contributing windows, Core-owned two-bank blend state/formulas, logical Transition summary versus queued/tail data, runtime-recovery-before-normal-request outcome order, the exact three two-slot Action outcome path classes, and pre-foot prepare/post-foot finalize state merge. Replace the old statement that only replacement can produce two outcomes: freeze the order as replacement interruption -> acceptance, runtime-failure interruption -> one normal request result, or rejected normal command -> existing-owner Completed/EBO terminal. Later tasks may only implement these contracts; they may not silently reinterpret the older prose.

Amend the specification's occurrence identity in this task: `OccurrenceHandleId` is included before source IDs/epoch in every instant/Begin/End/Tick dedup key. It is a collision-free compiled graph/source occurrence handle, not an authored string; ordering still uses the frozen semantic keys and consults the handle only as the final complete-tie breaker. `PlaybackCycle` and `BoundaryOrdinal` complete the instant/Begin/End identity; Tick additionally uses the enclosing `AlsFrameResult.Identity.FrameId`. This lets the same Sequence/event legitimately occur in Base and Action or in two Montage segments even if their local epoch/cycle values coincide. Freeze `AnimationTime` as the finite nonnegative occurrence offset from the current frame's simulation start, not clip-local authored time. It is therefore comparable across clips and cycles inside one character frame; authored source-local time remains in the immutable definition addressed by `EventId`. `AlsActionOutcomeBuffer` is an inline array with capacity `2`, bounds-checked indexer, `TryAdd` and `Clear`. Keep `AlsEventBuffer` at `16` and make it support value-copy transaction tests.

Map compact authored payload fields exactly: Footstep/SetAction/SetGroundedEntry use `EnumValue0`; EarlyBlendOut uses locomotion/rotation/stance in `EnumValue0/1/2`, blend seconds in `ScalarValue0` and its four checks in `Flags` bits 0..3; RootMotionScale uses `ScalarValue0`; Generic uses only its semantic ID. `TerminationReason` is nonzero only for synthetic End.

Define `AlsActionPlayerState`, `AlsDynamicTransitionState` and `AlsLaneBlendState` in `AlsP5RuntimeContracts.cs`, so later model tasks consume rather than redeclare them. `LastProcessedRequestId` is the monotonic Start-ID high-watermark and permanent tombstone; `LastProcessedCommandRequestId + LastProcessedCommand` records the last command transition for audit and replay tests. Positive Start IDs are strictly increasing within one character stream; an owning Cancel intentionally reuses its Start ID. Default high-watermark is `0`; `SegmentBindingIndex`, `QueuedAnimationId`, `LastProcessedCommandRequestId`, every inactive ID and every inactive blend-tail index default to `-1`. Runtime-failure recovery is deliberately not stored in committed Core state; Task 17 owns a per-slot external latch so rollback remains byte-exact. Give `AlsActionRequest` a static `None` value, then add `ActionRequest` as a trailing `init` property on `AlsFrameInput`, matching the existing P4 extension pattern and preserving every current positional constructor and call site; `CreateDefault` supplies `None` with the current identity generation. Append `ActionPlayer`, `DynamicTransition`, `ActionBlendLane` and `DynamicTransitionBlendLane` to `AlsRuntimeState`; `CreateDefault` and `ValidateP5Defaults` require all logical ownership and visual tails inactive. Append these exact unmanaged summaries to `AlsFrameResult`, with all default IDs `-1`:

```csharp
public readonly record struct AlsSyncResult(
    int GroupId, int LeaderOccurrenceHandleId, int LeaderAnimationId,
    long LeaderPlaybackEpoch, int PreviousMarkerId, int NextMarkerId,
    long Cycle, float Phase, float LeftFootPhase, float RightFootPhase);

public readonly record struct AlsDynamicTransitionQueuedSelection(
    int AnimationId, AlsTransitionFoot Foot,
    float BlendSeconds, float PlayRate, byte Active);

public readonly record struct AlsDynamicTransitionPlaybackSummary(
    int AnimationId, AlsTransitionFoot Foot,
    float BlendSeconds, float PlayRate, float EffectiveWeight, byte Active);

public readonly record struct AlsActionPlayback(
    int OccurrenceHandleId, int ActionDefinitionId, int AnimationId,
    int SectionId, int SegmentId,
    long PlaybackEpoch, float PreviousTime, float CurrentTime,
    float PreviousClipTime, float CurrentClipTime, float FinalSegmentDeltaSeconds,
    float PlayRate, float BlendSeconds, float EffectiveWeight, byte Active);

public readonly record struct AlsP5FailureRecord(
    AlsFrameIdentity Identity,
    AlsP5FailureCode Code,
    ulong LastCommittedResultDigest,
    uint AttemptOrdinal);
```

The result fields are `Sync`, `DynamicTransition` (`AlsDynamicTransitionPlaybackSummary`), `ActionPlayback`, `ActionOutcomes` and `P5FailureCode`. `AlsDynamicTransitionQueuedSelection` is internal candidate data only and never appears in a frame result. `EffectiveWeight` always means the current logical source's exact prepared graph contribution, never a queued choice, duration or target blend; a tail-only frame reports the logical summary inactive even while the graph instruction fades its visual tail. Only a successfully published frame uses `AlsFrameResult`, and its `P5FailureCode` must be `None`; a failed Worker attempt uses the separate `AlsP5FailureRecord` contract and never impersonates a successful result. `AttemptOrdinal` is `1` for the first failure of an identity, and retrying that same identity/code is deduplicated by the Godot mailbox added in Task 17. Legacy `RequestedAction` remains unchanged for P3/P4 regression. Do not add a four-parameter compatibility constructor: migrate `AlsSyntheticLocomotionModel`, `AlsEventBufferTests` and both `HotPathAllocationTests` call sites to the full constructor so no legacy call can silently manufacture duplicate occurrence identities. The synthetic P3 path must preserve its historical digest bytes: use default/invalid P5 identity and payload fields, `EventSequence=0`, and the existing `state.AnimationPhase` value for `AnimationTime`, so it does not activate the P5 digest extension. Freeze the three existing digest constants unchanged: default P3 `13994752607362685853`, synthetic sequence `1797804522282832714`, and P4 `18222991105242177202`.

- [ ] **Step 5: Extend digest with an explicit P5 version marker**

When any P5 field is non-default, append bytes `P`, `5`, `A`, `1`, then every event identity/payload field, including occurrence handle, cycle and boundary ordinal, every sync field, transition field, action playback field, both outcomes and failure code in storage order. The only failure-record digest API is `AlsResultDigest.AppendFailureRecord(ref ulong digest, in AlsP5FailureRecord record)`. It appends ASCII `P5F1`, then `Identity.FrameId int64 LE`, `Identity.CharacterId uint32 LE`, `Identity.SlotGeneration uint32 LE`, `Code uint16 LE`, `LastCommittedResultDigest uint64 LE` and `AttemptOrdinal uint32 LE`. It is a pure raw serializer and accepts `Code=None` and `AttemptOrdinal=0`. Tests must prove raw float bits, occurrence handle, owner token, event sequence and outcome order all affect the result digest and every failure-record field affects its digest, while the three frozen legacy digest constants above remain exact. Task 6 freezes the successful-result invariant `P5FailureCode=None` but does not modify `AlsFrameExchange` or claim enforcement: `AlsResultDigest` is a pure serializer and hashes every raw `ushort` failure-code value. Task 17 owns the first real result-publication preflight rejection, and Task 18 repeats it before visual commit or dispatch.

- [ ] **Step 6: Run GREEN ABI and full Core tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5ContractTests|FullyQualifiedName~AlsP5FailureRecordTests|FullyQualifiedName~ContractLayoutTests|FullyQualifiedName~AlsEventBufferTests|FullyQualifiedName~AlsResultDigestTests"
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug
```

Expected: focused and full Core tests PASS; `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` is false for every non-ref-struct frame/P5 storage contract, while the stack-only occurrence view is proven `IsByRefLike` by reflection and exercised through legal stackalloc/span binding. This task does not claim a Roslyn or negative-compilation illegal-escape test.

- [ ] **Step 7: Commit**

```powershell
git add docs/superpowers/plans/2026-08-30-p5a-event-action-runtime.md docs/superpowers/specs/2026-08-30-p5a-event-action-runtime-design.md src/Als.Core/Contracts src/Als.Core/Events src/Als.Core/Diagnostics/AlsResultDigest.cs src/Als.Core/Simulation/AlsSyntheticLocomotionModel.cs tests/Als.Core.Tests/AlsP5ContractTests.cs tests/Als.Core.Tests/AlsP5FailureRecordTests.cs tests/Als.Core.Tests/ContractLayoutTests.cs tests/Als.Core.Tests/AlsEventBufferTests.cs tests/Als.Core.Tests/AlsResultDigestTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat(core): add p5 runtime contracts and result buffers"
```

### Task 7: Add the Allocation-free General Curve Runtime and Preserve P4 Semantics

**Files:**
- Create: `src/Als.Core/Curves/AlsCurveRuntime.cs`
- Create: `tests/Als.Core.Tests/AlsCurveRuntimeTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`
- Modify: `src/Als.Godot/Animation/AlsCurveSampler.cs`
- Modify: `src/Als.Godot/Animation/P4AnimationGraphSmoke.cs`

- [ ] **Step 1: Characterize the current P4 sampler before changing it**

Extend `P4AnimationGraphSmoke` with exact vectors for endpoint clamp, Constant, Linear, Cubic Hermite overshoot, multi-curve sampling, canonical `RotationYawSpeedRadiansPerSecond`, non-finite rejection and warm-loop allocation. Record the current expected results, including tangent units as value/second.

- [ ] **Step 2: Add failing Core sampling/blending tests**

Add `SamplesConstantLinearAndCubicSegments`, `ClampsNonLoopAndNormalizesLoopTime`, `SplitsLargeLoopTimeIntoInt64Cycle`, `RequiredEmptyCurveFailsButOptionalContributesZero`, `RejectsNegativeOrNonFiniteWeight`, `NormalizesWeightedBlend`, `ZeroTotalWeightReturnsZero`, `AdditiveSemanticUsesFrozenMissingDefault`, `NegativeTransitionSampleDisablesAndClamps`, and `WarmEvaluationAllocatesZeroBytes`.

- [ ] **Step 3: Run RED tests and the unchanged P4 characterization**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsCurveRuntimeTests
pwsh -NoProfile -File scripts/verify-p4-pose.ps1 -GodotExecutable $godotExe
```

Expected: Core test fails to compile; P4 pose still passes and records the behavior that must be preserved.

- [ ] **Step 4: Implement the Core curve API**

```csharp
public enum AlsCurveInterpolationMode : byte
{
    Constant = 0,
    Linear = 1,
    Cubic = 2
}

public readonly record struct AlsCurveKey(
    float TimeSeconds,
    float Value,
    float ArriveTangent,
    float LeaveTangent,
    AlsCurveInterpolationMode Interpolation);

public readonly record struct AlsCurveBinding(
    int CurveId,
    int KeyOffset,
    int KeyCount,
    float DurationSeconds,
    byte Required,
    byte Loop);

public readonly record struct AlsCurveBlendSample(
    int BindingIndex,
    long Cycle,
    float TimeSeconds,
    float Weight);

public static bool TrySample(
    in AlsCurveBinding binding,
    ReadOnlySpan<AlsCurveKey> keys,
    long cycle,
    float timeSeconds,
    out float value,
    out AlsP5FailureCode failure);

public static bool TryBlend(
    ReadOnlySpan<AlsCurveBinding> bindings,
    ReadOnlySpan<AlsCurveKey> keys,
    ReadOnlySpan<AlsCurveBlendSample> samples,
    out float value,
    out AlsP5FailureCode failure);

public static bool TryBlendAdditiveToDefault(
    float defaultValue,
    float clampMinimum,
    float clampMaximum,
    ReadOnlySpan<AlsCurveBinding> bindings,
    ReadOnlySpan<AlsCurveKey> keys,
    ReadOnlySpan<AlsCurveBlendSample> samples,
    out float value,
    out AlsP5FailureCode failure);
```

Use binary search and double intermediates for Hermite. `AlsCurveInterpolationMode` is the Core-owned enum; the initialization adapter maps Import's `AlsCurveInterpolation` member-by-member and rejects unknown numeric values, so Core never references Import. Add `TryAdvancePlaybackTime(duration, loop, currentCycle, currentTime, deltaSeconds, playRate, out nextCycle, out nextTime)`; it uses double intermediate math, checked `int64` cycle arithmetic and finite in-cycle float time, so large deltas never round-trip through one unbounded float. `TrySample` validates the supplied cycle/time pair, clamps non-loop clips and samples loop clips at in-cycle time. Normalize ordinary blend by total active weight; do not renormalize missing optional curves independently. The additive semantic overload begins at the frozen default, adds `sample * effectiveWeight` only for present bindings, clamps once, and therefore maps missing `Enable_Transition` to `1` and a full-weight `-1` sample to `0` without runtime string lookup.

- [ ] **Step 5: Delegate P4 sampler to Core after initialization flattening**

`AlsCurveSampler` converts Import definitions to flat `AlsCurveBinding[]` / `AlsCurveKey[]` only in its constructor, then forwards hot sampling to `AlsCurveRuntime`. It preserves public constructors and `TrySample` behavior so P4 callers do not change. Canonical yaw provenance/sign remains untouched.

- [ ] **Step 6: Run GREEN Core, P4 and allocation gates**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsCurveRuntimeTests|FullyQualifiedName~HotPathAllocationTests"
pwsh -NoProfile -File scripts/verify-p4-pose.ps1 -GodotExecutable $godotExe
```

Expected: PASS, P4 sampled values/digest unchanged, and warm sampling reports `0 B`.

- [ ] **Step 7: Commit**

```powershell
git add src/Als.Core/Curves tests/Als.Core.Tests/AlsCurveRuntimeTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs src/Als.Godot/Animation/AlsCurveSampler.cs src/Als.Godot/Animation/P4AnimationGraphSmoke.cs
git commit -m "feat(core): add allocation-free curve runtime"
```

### Task 8: Implement Deterministic Timeline and Notify State Ownership

**Files:**
- Create: `src/Als.Core/Events/AlsTimelineContracts.cs`
- Create: `src/Als.Core/Events/AlsTimelineRuntime.cs`
- Create: `src/Als.Core/Events/AlsNotifyStateRuntime.cs`
- Create: `tests/Als.Core.Tests/AlsTimelineRuntimeTests.cs`
- Create: `tests/Als.Core.Tests/AlsNotifyStateRuntimeTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing interval, authority and ownership tests**

Cover `(previous,current]`, exact boundary, one-time new-occurrence left-boundary activation, time-0 instant/State Begin for Base/Transition/Action accept and segment handoff, activation rollback/retry, non-loop clamp with a true half-frame window, loop wrap, multiple cycles, large delta, different clip durations crossing in one frame, two disjoint Action segment windows each containing an event, one segment loop represented by several adjacent same-key slices, clipped State End -> next-loop Begin at one boundary with stable owned ordinal, simultaneous Action Montage and Sequence domains, Sequence-only segment handoff, terminal truncation closing both Action domains, non-unit segment range/rate/multi-loop Sequence events evaluated in mapped Montage coordinates, two reused-Sequence segments each matching only its own definition range, absolute frame occurrence order, Base/Action and Base/Turn/Transition use of the same clip with exact handles and no wildcard double publication, Grounded member -> Idle/non-member authority handoff, Turn/Rotate bank replacement in the shared P4 domain, same-identity retry, Tick identity including `FrameId`, threshold consumption, effective-weight authority, persisted authority, animation-ID/epoch tie-break, authority handoff, enter/hold/end, one-frame full state crossing, explicit cancel, replacement, section jump, runtime-failure deferral, 17 occurrences in one frame, and the cross-frame active-State transition from 16 to 17 owners followed by close-and-slot-reuse.

Assert fixed sort order independently of enum numeric values:

```text
absolute time -> old End -> sourceIndex -> End/Trigger/Begin/Tick -> animation ID -> epoch -> boundary ordinal -> occurrence handle
```

- [ ] **Step 2: Run RED timeline tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsTimelineRuntimeTests|FullyQualifiedName~AlsNotifyStateRuntimeTests"
```

Expected: FAIL because cursor, ownership, authority and preflight APIs do not exist.

- [ ] **Step 3: Implement caller-owned timeline contracts**

```csharp
public readonly record struct AlsTimelineEventDefinition(
    int EventId,
    int SourceAnimationId,
    int SourceActionId,
    int RequiredOccurrenceHandleId,
    AlsTimelineSourceKind SourceKind,
    int SourceIndex,
    int TrackIndex,
    int BoundaryOrdinal,
    float TimeSeconds,
    float DurationSeconds,
    float TriggerWeightThreshold,
    AlsTimelineEventKind Kind,
    AlsTimelineTickMode TickMode,
    AlsCompactEventPayload Payload);

public struct AlsTimelineCursor
{
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public double ConsumedUnwrappedTimeSeconds;
}

public struct AlsTimelineAuthorityState
{
    public int GroupId;
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public byte Active;
}

public struct AlsNotifyStateOwnership
{
    public int EventId;
    public int BoundaryOrdinal;
    public int OccurrenceHandleId;
    public int AnimationId;
    public int ActionId;
    public long PlaybackEpoch;
    public long PlaybackCycle;
    public ulong OwnerToken;
    public byte Active;
}

public readonly record struct AlsTimelinePlayback(
    int OccurrenceHandleId,
    int AnimationId,
    int ActionId,
    int AuthorityGroupId,
    long PlaybackEpoch,
    double PreviousUnwrappedTimeSeconds,
    double CurrentUnwrappedTimeSeconds,
    double FrameStartOffsetSeconds,
    double FrameEndOffsetSeconds,
    float DurationSeconds,
    float Weight,
    AlsActionResultCode TerminationReason,
    byte Loop,
    byte ActivatesAtWindowStart,
    byte ClosesAfterWindow);
```

`OccurrenceHandleId` is the stable compiled occurrence key added to the amended spec identity; `PlaybackEpoch` remains the per-owner discontinuity generation. `RequiredOccurrenceHandleId >= 0` requires an exact playback-handle match. `-1` exists only for explicit independent Timeline unit tests and never appears in a production P5 binding. Task 14 copies every authored Base definition per fixed Base/Turn/Rotate slot with that slot's exact handle; Transition, Action Montage and Action Sequence definitions likewise receive their exact layout handles. Therefore one animation reused across roles cannot match a wildcard copy or publish twice. `BoundaryOrdinal` is compiler-owned immutable identity: ordinary and non-repeated Montage definitions use `0`; flattened Sequence definitions copy the nonzero mapped occurrence ordinal produced in Task 5. Timeline copies it to every public phase of that occurrence and never derives it from a scan or the current frame window.

`AlsTimelinePlayback` carries explicit timeline duration plus monotonic unwrapped previous/current time, so loop count and every crossed boundary are derivable without ambiguous local time. Its local frame window must satisfy `0 <= FrameStartOffsetSeconds <= FrameEndOffsetSeconds <= frameEnd-frameStart`. Continuing looped Base/Transition sources normally use the full frame; a non-loop source that clamps early uses its exact contributing end offset, and each Action traversal slice uses its exact contributing subwindow. `TerminationReason` must be `None` unless `ClosesAfterWindow=1`; authored and compile-time clipped Ends always retain their definition payload with `None`, while only runtime-generated synthetic Ends copy the closing playback's reason. Action natural completion maps `Completed`; replacement, explicit cancel, EBO and runtime recovery map their exact `InterruptedBy*` code. Action segment handoff/section jump, Base/Turn/Rotate closure, and Transition replacement/natural completion map `None`. Main-only lifecycle/generation closure remains supplied directly to `TryAppendSyntheticEnds` as `InterruptedByLifecycle`/`InterruptedByGeneration`. Action-owned definitions match `ActionId + RequiredOccurrenceHandleId`; ordinary clips match `AnimationId` and required handle policy. `AlsTimelineOccurrence` carries source-local cycle/time, `double FrameOccurrenceTimeSeconds`, source index, phase, the definition's exact boundary ordinal and Tick frame ID; occurrence time is `frameStart + localWindowStart + boundaryFraction * localWindowDuration`, so events from several Action slices sort correctly against Base/Transition events. Its dedup key includes occurrence handle plus the amended spec fields. Add caller-supplied `Span<AlsTimelineOccurrence>` scratch and a persisted `AlsTimelineAuthorityState` slot per authority group; runtime never allocates or calls LINQ.

The normal interval remains `(previous,current]`. Exactly once for a newly activated handle/epoch, `ActivatesAtWindowStart=1` additionally includes the left boundary: emit an instant exactly at `previous`, and if `previous` lies inside an authored State (including exactly its Begin), emit Begin and that frame's Tick according to tick mode. Do not emit an End that lies only on the activation left boundary. Adjacent continuation slices set the flag to zero, so time-0 or segment-start events cannot duplicate. At a point window shared by an old close and a new activation, materialize all old synthetic/authored Ends first, then the new instant/Begin/Tick phases. Activation writes cursor/ownership only in candidate state; rollback/retry reproduces the same candidate bytes, and only one successful commit makes later windows left-exclusive.

- [ ] **Step 4: Implement preflight, authority and all-or-nothing publication**

Expose:

```csharp
public static bool TryEvaluate(
    ReadOnlySpan<AlsTimelineEventDefinition> definitions,
    ReadOnlySpan<AlsTimelinePlayback> playbacks,
    long frameId,
    double frameStartTimeSeconds,
    double frameEndTimeSeconds,
    Span<AlsTimelineCursor> cursors,
    Span<AlsTimelineAuthorityState> authorities,
    Span<AlsNotifyStateOwnership> ownership,
    ref ulong nextOwnerToken,
    Span<AlsTimelineOccurrence> scratch,
    ref AlsEventBuffer events,
    out AlsP5FailureCode failure);
```

`AuthorityGroupId < 0` means an explicitly independent authored playback and is never suppressed; nonnegative IDs are globally dense competing domains across the complete character, never profile-local IDs. The Task 5 layout supplies one Base/Turn/Rotate production replacement domain, one Transition domain and each Action's distinct Montage and Sequence domains; Sync `GroupId` is unrelated timing metadata. Inside every overlapping elementary interval, choose maximum finite nonnegative effective `Weight`, then smallest `AnimationId`, then smallest `PlaybackEpoch`, then smallest `OccurrenceHandleId`; enum/numeric source kind never participates. Montage and Sequence can therefore both publish in the same window, while a segment handle change performs an ordered handoff only inside the Sequence domain. First validate every local window, then sweep each nonnegative authority group over the sorted union of its window boundaries. Authority competition occurs only where windows overlap; disjoint Action slices are evaluated sequentially and are never candidates against one another. Adjacent slices with the same complete key are legal only when `previous.CurrentUnwrappedTime == next.PreviousUnwrappedTime` and their frame-window endpoints touch; treat them as one ordered continuation for cursor/authority purposes. Reject overlapping duplicate keys, gaps disguised as continuation or backward local time as `InvalidTimeline`. Persist only the final authority after the full sweep.

Preflight and count every required occurrence across all intervals, including synthetic End records required by any `ClosesAfterWindow` playback. In the same scratch-only pass, compute the final candidate active ownership set after every ordered End/Begin and require its peak and final count to fit both the supplied ownership span and the production/Main mirror capacity of `16`; do not validate only this frame's occurrence count. If either event count or candidate active ownership exceeds its capacity, return `EventBufferOverflow` without changing cursor, authority, ownership, owner-token counter or buffer. Otherwise materialize all interval occurrences into the one caller scratch, stable-sort the at-most-16 entries by `FrameOccurrenceTimeSeconds` and the frozen secondary keys, assign `EventSequence` from zero, set public `AnimationTime = checked((float)(FrameOccurrenceTimeSeconds - frameStartTimeSeconds))`, and commit all cursor/authority/ownership/token/buffer state once. Tests require finite nonnegative offsets, nondecreasing `AnimationTime`, both events from a two-segment frame, all loop-slice occurrences, and distinct ordered offsets against Base/Transition crossings. They also accumulate 16 long-lived States over separate frames, prove the 17th Begin rolls back byte-for-byte even though that frame has only one occurrence, then close one owner and prove the next Begin reuses capacity. Threshold-missed instant events still advance cursor. Begin stores the definition's `BoundaryOrdinal` and consumes the next nonzero value from the caller-owned transactional `nextOwnerToken`; checked exhaustion returns `InvalidTimeline` without mutation. Tick, authored End and every synthetic End copy the owned ordinal unchanged, so a multi-loop State retains the same complete identity across frames and cancellation. Authored End matches occurrence handle + event ID + boundary ordinal + epoch + token. A closing playback emits stable synthetic Ends at its local window end and copies its validated `TerminationReason` into each synthetic payload, then clears its cursor/authority/ownership only as part of the successful transaction; rollback retains the occurrence, ordinal, epoch and counter. Tests cover every Action close-reason mapping, `None` for ordinary/segment/Transition closures, rejection of non-`None` on a non-closing playback, and authored/clipped End remaining `None`.

- [ ] **Step 5: Implement the main-owned committed mirror container**

`AlsCommittedNotifyStateBuffer` is a separate 16-slot inline value buffer. `TryApply(in AlsAnimationEvent)` only accepts successful committed Begin/End; a Worker-validated batch is guaranteed to fit, and tests treat any mirror-capacity failure after validation as an invariant violation rather than a recoverable post-visual-commit path. `TryAppendSyntheticEnds(reason, identity, ref destination)` emits stable ordered Ends from mirror data and clears only after all outputs fit. It never references Worker-owned arrays.

- [ ] **Step 6: Run GREEN timeline/allocation tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsTimelineRuntimeTests|FullyQualifiedName~AlsNotifyStateRuntimeTests|FullyQualifiedName~HotPathAllocationTests"
```

Expected: all semantic cases PASS; overflow preserves byte-for-byte state; warm timeline evaluation allocates `0 B`.

- [ ] **Step 7: Commit**

```powershell
git add src/Als.Core/Events tests/Als.Core.Tests/AlsTimelineRuntimeTests.cs tests/Als.Core.Tests/AlsNotifyStateRuntimeTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat(core): add deterministic notify timeline runtime"
```

### Task 9: Implement Marker Sync, Leader/Follower Mapping and Foot Phase

**Files:**
- Create: `src/Als.Core/Sync/AlsSyncContracts.cs`
- Create: `src/Als.Core/Sync/AlsSyncRuntime.cs`
- Create: `tests/Als.Core.Tests/AlsSyncRuntimeTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing marker-pair tests**

Cover unique leader, max weight, animation-ID then epoch then occurrence-handle tie-break, simultaneous current/outgoing uses of the same animation with equal epoch/weight but different handles, zero followers, multiple followers, different clip durations, Left/Right pair phase, follower remap to exact previous/current local times, mapped play rate, loop wrap/cycle, missing pair, duplicate time, illegal marker order, undeclared member/group, insufficient output span, non-finite weight/time and proof that no marker reaches `AlsEventBuffer`.

- [ ] **Step 2: Run RED sync tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsSyncRuntimeTests
```

Expected: FAIL because Sync contracts/runtime are absent.

- [ ] **Step 3: Implement integer-only Sync bindings**

```csharp
public readonly record struct AlsSyncMarkerDefinition(
    int MarkerId,
    int MarkerNameId,
    int AnimationId,
    int SourceIndex,
    int TrackIndex,
    float TimeSeconds);

public readonly record struct AlsSyncGroupBinding(
    int GroupId,
    int MemberOffset,
    int MemberCount,
    int LeftMarkerNameId,
    int RightMarkerNameId);

public readonly record struct AlsSyncMemberBinding(
    int GroupId,
    int AnimationId,
    float DurationSeconds,
    byte Loop,
    byte CanLead);

public readonly record struct AlsSyncPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    double PreviousUnwrappedTimeSeconds,
    double CurrentUnwrappedTimeSeconds,
    float Weight);

public readonly record struct AlsSyncMappedPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    float DurationSeconds,
    long PreviousCycle,
    long CurrentCycle,
    float PreviousTimeSeconds,
    float CurrentTimeSeconds,
    float MappedPlayRate);

public static bool TryEvaluateGroup(
    ReadOnlySpan<AlsSyncMarkerDefinition> markers,
    in AlsSyncGroupBinding group,
    ReadOnlySpan<AlsSyncMemberBinding> members,
    ReadOnlySpan<AlsSyncPlayback> playbacks,
    double frameDeltaSeconds,
    Span<AlsSyncMappedPlayback> mappedPlaybacks,
    out int mappingCount,
    out AlsSyncResult result,
    out AlsP5FailureCode failure);
```

Expose `TryEvaluateGroup` over marker/group/member/playback spans, a finite positive `frameDeltaSeconds`, plus a caller-owned `Span<AlsSyncMappedPlayback>`, returning `mappingCount` and `AlsSyncResult`. The pure routine remains group-shaped for focused tests, but Task 5/12 production bindings contain exactly one group and therefore write the one public `AlsFrameResult.Sync`; passing zero or multiple production groups is `InvalidSyncGroup`. Select the Leader by maximum finite weight, then smallest `AnimationId`, smallest `PlaybackEpoch`, and finally smallest `OccurrenceHandleId`; slot-local epochs may legitimately tie, so the handle tie-break is mandatory and copied into `AlsSyncResult`. The Leader advances in unwrapped time before the call; the runtime emits one mapping for every active occurrence, including the Leader. Mappings are keyed by `OccurrenceHandleId + AnimationId + PlaybackEpoch`, so simultaneous current/outgoing branches using the same clip cannot alias. Followers map the Leader's same-name previous/next marker pair and phase into their own clip duration/cycle, producing exact previous/current local seconds and `MappedPlayRate = mappedUnwrappedDelta / frameDeltaSeconds`. Any duplicate occurrence key, invalid pair/group/delta or insufficient output span returns `InvalidSyncGroup`, sets `mappingCount=0`, and leaves the output/default state unchanged.

- [ ] **Step 4: Run GREEN sync/allocation tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsSyncRuntimeTests|FullyQualifiedName~HotPathAllocationTests"
```

Expected: exact IDs/cycle/leader/mapping count match, every mapped local time reaches the expected marker interval with phase tolerance `1e-5`, marker event count remains zero and warm evaluation is `0 B`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Sync tests/Als.Core.Tests/AlsSyncRuntimeTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat(core): add marker sync runtime"
```

### Task 10: Implement ALS-ordered Dynamic Transition Selection

**Files:**
- Create: `src/Als.Core/Transitions/AlsDynamicTransitionContracts.cs`
- Create: `src/Als.Core/Transitions/AlsDynamicTransitionRuntime.cs`
- Create: `tests/Als.Core.Tests/AlsDynamicTransitionRuntimeTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing order and tie-break tests**

Test cooldown consumed first, `AllowTransitions >= 1 - 1e-5`, lock relevance, strict squared-distance threshold, one-foot selection, larger-distance selection, exact tie choosing Left, all four stance/foot IDs, queue-at-N result logically inactive/promote-at-N+1 result active with positive candidate weight, active/current `Foot` surviving a simultaneous next queue, finite delta advancement, active phase/completion/queued replacement, replacement emitting old zero-window closure plus new playback, a clip ending halfway through the frame with the exact terminal offset/event ordering/residual fade, completion closing the contributing playback at its terminal offset followed by a tail-only logically inactive summary, Notify State ownership release, invalid binding/input rollback, no selection leaving cooldown zero and valid selection arming exactly two complete selection frames. A selection queued at N must make N+1 and N+2 ineligible and first allow another selection at N+3.

- [ ] **Step 2: Run RED transition tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsDynamicTransitionRuntimeTests
```

Expected: FAIL because selection runtime is absent.

- [ ] **Step 3: Implement the pure value API**

```csharp
public readonly record struct AlsDynamicTransitionClipBinding(
    int AnimationId,
    int AdditiveBaseAnimationId,
    float DurationSeconds);

public readonly record struct AlsDynamicTransitionBinding(
    int OccurrenceHandleId,
    int AuthorityGroupId,
    AlsDynamicTransitionClipBinding StandingLeft,
    AlsDynamicTransitionClipBinding StandingRight,
    AlsDynamicTransitionClipBinding CrouchingLeft,
    AlsDynamicTransitionClipBinding CrouchingRight,
    float DistanceMeters,
    float BlendSeconds,
    float PlayRate,
    int CooldownFrames);

public readonly record struct AlsDynamicTransitionInput(
    AlsStance Stance,
    float AllowTransitions,
    Vector3 LeftTarget,
    Vector3 LeftLock,
    byte LeftRelevant,
    Vector3 RightTarget,
    Vector3 RightLock,
    byte RightRelevant);

public readonly record struct AlsDynamicTransitionPlayback(
    int OccurrenceHandleId,
    int AnimationId,
    long PlaybackEpoch,
    float PreviousTime,
    float CurrentTime,
    float DurationSeconds,
    float PlayRate,
    float BlendSeconds,
    double FrameEndOffsetSeconds,
    byte ContributesThisFrame,
    byte CooldownBlockedThisFrame,
    byte ActivatesAtFrameStart,
    byte ClosesAfterFrame);

public static bool TryAdvance(
    in AlsDynamicTransitionBinding binding,
    float deltaTime,
    in AlsDynamicTransitionState current,
    out AlsDynamicTransitionState next,
    out AlsDynamicTransitionPlayback closingPlayback,
    out AlsDynamicTransitionPlayback playback,
    out AlsP5FailureCode failure);

public static bool TryQueue(
    in AlsDynamicTransitionBinding binding,
    in AlsDynamicTransitionInput input,
    byte cooldownBlockedThisFrame,
    in AlsDynamicTransitionState current,
    out AlsDynamicTransitionState next,
    out AlsDynamicTransitionQueuedSelection queuedSelection,
    out AlsP5FailureCode failure);
```

`TryAdvance` validates finite positive delta and returns two fixed playback slots. Promotion copies `QueuedFoot` into the independent active `Foot`, so a later queue cannot rewrite the current result identity. When a queued selection replaces an active Transition at frame start, `closingPlayback` contains the old occurrence/epoch with previous=current old time, `FrameEndOffsetSeconds=0`, zero contribution, no activation and `ClosesAfterFrame=1`; the new `playback` receives the incremented epoch and `ActivatesAtFrameStart=1`. First activation follows the same rule. Continuation sets activation to zero. `FrameEndOffsetSeconds` is `min(deltaTime, (DurationSeconds-PreviousTime)/PlayRate)` with double intermediates; it defines the exact `[0,endOffset]` contributing window and must be finite. Without replacement, `closingPlayback` is inactive/default. Natural completion returns the final clamped contributing interval in `playback` with `ClosesAfterFrame=1`, so Timeline closes any Transition Notify State at that terminal offset and the blend tail uses only the remaining frame time. These closure/current outputs share the binding's globally remapped Transition authority domain and are preflighted together; rollback commits neither activation, closure, offset nor epoch change. Task 12, not `TryQueue`, produces the public playback summary and effective weight from this logical current plus the lane blend candidate.

`TryAdvance` snapshots `CooldownBlockedThisFrame = current.CooldownFrames > 0`, then decrements only the candidate count for the next frame. `TryQueue` receives that snapshot and returns without evaluating either foot whenever it is set, even when the decremented candidate count has reached zero. It never changes current-frame playback; after P4 pose evaluation it selects from that successful frame's physical target/lock evidence, stores one queued clip/foot for N+1, and arms the two-frame cooldown only for a valid selection. Therefore queue-at-N blocks selection at N+1 and N+2 and permits it at N+3.

Core performs no world query. Godot computes relevance from the same successful P4 lock amount with the frozen `1e-5` epsilon and supplies P4's physical target plus owned lock world positions. This explicit current-frame-probe/N+1-playback rule removes the otherwise impossible dependency on a pose that only exists after the frame's single `AnimationTree.Advance()`.

- [ ] **Step 4: Run GREEN transition/allocation tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsDynamicTransitionRuntimeTests|FullyQualifiedName~HotPathAllocationTests"
```

Expected: PASS and warm selection allocates `0 B`.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Transitions tests/Als.Core.Tests/AlsDynamicTransitionRuntimeTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat(core): add dynamic transition selection runtime"
```

### Task 11: Implement the Single-lane ActionPlayer and Section Graph

**Files:**
- Create: `src/Als.Core/Actions/AlsActionContracts.cs`
- Create: `src/Als.Core/Actions/AlsActionPlayer.cs`
- Create: `tests/Als.Core.Tests/AlsActionPlayerTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing request, section and interruption tests**

Cover Idle Start, missing definition, invalid section/current slot generation, request priority lower/equal/higher than the active owner, non-interruptible busy, lower priority rejection, equal/higher replacement, stale cancel including `Start42(A) -> Cancel42(B)` and natural-complete-then-first-`Cancel42`, exact stale-Cancel replay, explicit cancel using the owner Start request ID, monotonic high-watermark replay/tombstone behavior, `Start42 -> Cancel42 -> late Start42`, `Start43 -> late Start42`, non-loop section completion with a Notify State whose authored End lies beyond the cut, loop graph, section jump epoch, Montage segment boundary, time-0 instant/State activation on accept and handoff, same-frame Montage notify plus Sequence notify/state, same-section adjacent segments reusing the same Sequence but each consuming only its own definition/identity and closing only old Sequence ownership, clipped State ownership/ordinal across a loop cut and later cancel, multi-segment and segment-loop traversal slices, exact mapped-Montage occurrence order for non-unit segment range/rate/multi-loop while clip time only drives the graph, both definition and segment rates non-unit, resolved Sequence animation/phase, validated Montage duration, accepted-frame zero advancement with a separate blend step, a cross-segment frame whose graph lands on the final segment pose, traversal scratch overflow rollback, EarlyBlendOut checks, runtime-failure cancellation before a simultaneous newer-frame normal request, and fixed two-outcome ordering for replacement, recovery-plus-request, and rejected-command-plus-existing-owner-terminal combinations.

Freeze this capacity rule in tests: a newly accepted or replaced Action cannot also emit completion/EarlyBlendOut in the acceptance frame; its first timeline advancement is the next successful frame. Its one-time zero-window left-boundary activation and blend-weight step are allowed in that frame but are neither playback advancement nor completion. Exactly three path classes can fill both outcome slots: replacement emits old `InterruptedByReplacement` then new `Accepted`; a recovery frame first emits old `InterruptedByRuntimeFailure`, then its one normal request emits at most one Accepted/Rejected result; or one rejected ordinary command is recorded first and the still-active existing owner subsequently emits `Completed` or `InterruptedByEarlyBlendOut` during normal same-frame advancement. A rejected command never suppresses that advancement. The internal recovery cancel always runs before that frame's `ActionRequest`, so a replacement cannot add a third outcome. Tests cover lower-priority Start and malformed/non-owning Cancel followed by both natural completion and EBO, with request outcome before terminal outcome.

- [ ] **Step 2: Run RED ActionPlayer tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsActionPlayerTests
```

Expected: FAIL because Action definitions/state machine are absent.

- [ ] **Step 3: Implement exact action bindings and state**

```csharp
public readonly record struct AlsActionDefinition(
    int OccurrenceHandleId,
    int MontageAuthorityGroupId,
    int SequenceAuthorityGroupId,
    int DefinitionId,
    int MontageId,
    float MontageDurationSeconds,
    int SlotId,
    int StartSectionId,
    int Priority,
    float PlayRate,
    float BlendSeconds,
    byte Interruptible,
    byte Loop);

public readonly record struct AlsActionSectionBinding(
    int ActionDefinitionId,
    int SectionId,
    int NextSectionId,
    float StartTime,
    float EndTime);

public readonly record struct AlsActionSegmentBinding(
    int OccurrenceHandleId,
    int ActionDefinitionId,
    int SlotId,
    int SegmentId,
    int AnimationId,
    float MontageStartTime,
    float MontageEndTime,
    float AnimationStartTime,
    float AnimationEndTime,
    float PlayRate,
    int LoopCount);

public readonly record struct AlsActionTraversalSlice(
    int ActionOccurrenceHandleId,
    int SegmentOccurrenceHandleId,
    int SectionId,
    int SegmentBindingIndex,
    int SegmentId,
    int AnimationId,
    long PlaybackEpoch,
    double PreviousMontageTime,
    double CurrentMontageTime,
    double PreviousClipUnwrappedTime,
    double CurrentClipUnwrappedTime,
    double FrameStartOffsetSeconds,
    double FrameEndOffsetSeconds,
    byte ActivatesActionAtSliceStart,
    byte ActivatesSegmentAtSliceStart,
    byte ClosesActionAfterSlice,
    byte ClosesSegmentAfterSlice);
```

Consume the `AlsActionPlayerState` frozen in Task 6; do not redeclare it. Expose `TryApplyRequest`, `TryAdvance`, and `TryInterruptEarlyBlendOut`; each accepts readonly definition/section/segment bindings and a caller-owned outcome buffer. `TryApplyRequest` also receives the current slot generation, returns `startedOrReplacedThisFrame`, and rejects any request whose `SlotGeneration` differs. A Start request must name a section in the selected definition. Gather defaults its priority from the compiled definition, but Core treats the request's integer priority as the comparison value defined by the spec and does not require equality with the definition. Replacement writes old `InterruptedByReplacement` then new `Accepted`.

Freeze cancellation and replay as follows. Start request IDs are positive and strictly increasing within one character's stream; Cancel is the intentional equal/older command because it reuses the owning Start ID, and slot generation remains the cross-reuse guard. A Start with `RequestId <= LastProcessedRequestId` is a delayed replay and is a no-op forever; a newer Start advances the high-watermark even when it produces a deterministic rejection, so it cannot later be accepted after state changes. Cancel must carry the active owning Start's same request ID and action definition ID; that owning Cancel is the only allowed lower-than-high-watermark state transition and executes once even if a newer rejected Start already raised the high-watermark.

For a non-owning Cancel, `RequestId < LastProcessedRequestId` is always a delayed no-op. At equality, it is an exact replay only when `LastProcessedCommandRequestId == RequestId && LastProcessedCommand == Cancel`; otherwise it is the first equal-ID stale Cancel, returns one `RejectedInvalidRequest`, and records that command pair. Thus `Start42(A) -> Cancel42(B)` and a first `Cancel42` after Action 42 naturally completed each reject once, while their exact replays are no-ops. A non-owning Cancel newer than the high-watermark also rejects once, advances the high-watermark and records the pair. A malformed equal-ID Cancel consumes that `(Cancel,42)` idempotency key; the active Action can then end only by completion, replacement, EarlyBlendOut or lifecycle/runtime failure. Every non-no-op command updates `LastProcessedCommandRequestId + LastProcessedCommand`. This permanently prevents `Start42 -> Cancel42 -> late Start42` and `Start43 -> late Start42` from resurrecting an action. `CancelForRuntimeFailure` is internal-worker only and yields `InterruptedByRuntimeFailure` on the next successful identity selected by the external Task 17 latch.

`TryAdvance` accepts a caller-owned `Span<AlsActionTraversalSlice>` with minimum production capacity `16`, walks the validated section graph in Montage time, and emits one ordered slice for every contributing section/segment/loop portion. Each slice carries the Action definition's Montage occurrence handle, the active compiled segment binding's distinct Sequence occurrence handle and exact frame-relative start/end offsets. Task 12 constructs two Timeline playbacks per slice: both use `PreviousMontageTime/CurrentMontageTime`, `AlsActionDefinition.MontageDurationSeconds` and `Loop=0`; Montage entries use `MontageAuthorityGroupId` plus the Action handle, while Sequence entries use `SequenceAuthorityGroupId` plus the segment handle. The already-flattened definitions provide mapped Montage-coordinate time/duration, exact required handle and immutable boundary ordinal. `PreviousClipUnwrappedTime/CurrentClipUnwrappedTime` are exclusively graph output and must never be passed to Timeline. Repeated segment loops are compiled entries with distinct boundary ordinals, not runtime Timeline cycles. Both source classes can emit in one frame, while only Sequence sources hand off on a same-section segment boundary. It also stores the final `SegmentBindingIndex` and returns an `AlsActionPlayback` whose `OccurrenceHandleId` is that final segment handle and whose animation/segment/clip/rate/delta fields drive the real graph.

Freeze `AlsActionPlayback.PlayRate = definition.PlayRate * finalSegment.PlayRate`; it never contains the frame-fraction scale. `FinalSegmentDeltaSeconds` is the simulation-time portion of this frame spent in that final segment. Acceptance/replacement sets both activation flags on the zero-delta initial slice, so authored time-0 Montage/Sequence events can fire without advancing. On a same-section segment change, the old slice sets only `ClosesSegmentAfterSlice=1`; Montage ownership continues, and the next slice sets only `ActivatesSegmentAtSliceStart=1` at the same frame offset. A section jump sets both close flags, increments the Action epoch, and sets both activation flags on the new slice at that same offset. Natural non-loop completion sets both close flags on the final slice, even when an authored Notify State End lies after the truncation point. Cancel/replacement/runtime recovery mark both domains closing at offset zero; EarlyBlendOut marks both closing at frame end; Task 12 supplies all such flags before its one global Timeline preflight. A continuation within the same segment sets no activation/close flags. Rollback commits no activation, close, handoff or epoch change. The P5A Roll profile is validated `Once` with one segment, while generic Core tests exercise bounded loop graphs. More than 16 slices returns `InvalidTimeline` with state, outcomes and slice count unchanged.

When `startedOrReplacedThisFrame` is true, orchestration must not call normal advancement: it emits the accepted initial segment at its start time with zero contributing delta, so acceptance/replacement cannot advance or complete in that frame. On later frames a cross-segment interval preflights every traversal slice for Montage/Sequence events, but the Action lane incoming graph source carries only the final slice's unscaled combined `PlayRate`, previous/current clip time and maps `AlsActionPlayback.FinalSegmentDeltaSeconds` into `AlsLaneGraphSource.ContributingDeltaSeconds`. Task 16 performs the sole `ContributingDeltaSeconds / frameDelta` graph scaling; after the one global `AnimationTree.Advance(frameDelta)`, its pose must equal `AlsActionPlayback.CurrentClipTime`. Missing coverage, a non-finite mapping or an invalid final slice fails transactionally as `InvalidBinding`.

- [ ] **Step 4: Implement exact EarlyBlendOut condition evaluation**

`TryInterruptEarlyBlendOut` accepts the flattened Action definitions/range, current Action/segment handles, candidate final Montage time, provisional incoming effective weight, plus value-only `hasInput`, locomotion mode, rotation mode and stance. It considers only exact-handle EBO State intervals containing that final time whose threshold is met; disabled comparisons retain and validate their enum values but do not participate. Freeze the ALS-Refactored predicate exactly as `(checkInput && hasInput) || (checkLocomotionMode && locomotionMode == expectedLocomotionMode) || (checkRotationMode && rotationMode == expectedRotationMode) || (checkStance && stance == expectedStance)`: enabled checks are OR alternatives, not an AND gate. All four disabled, or all enabled comparisons missing, means no match. Tests cover each check matching alone, multiple enabled checks with only one match, all disabled, all misses, and every flag bit/high-bit validation path. If several active EBO definitions match, choose the lowest compiled `SourceIndex`, then `EventId`, and use that payload's `blendOutSeconds`. A match marks both Action domains closing at frame end, emits one `InterruptedByEarlyBlendOut`, and returns the selected blend duration to Task 12; it does not mutate Timeline ownership or call gameplay. Invalid ranges/handles/weight fail transactionally.

- [ ] **Step 5: Run GREEN Action/allocation tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsActionPlayerTests|FullyQualifiedName~HotPathAllocationTests"
```

Expected: PASS; replacement and recovery-plus-request each fill exactly two slots in their frozen order; 10,000 warm iterations allocate `0 B`.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Core/Actions tests/Als.Core.Tests/AlsActionPlayerTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat(core): add single-lane action player"
```

### Task 12: Compose the Two-phase P5A Core Frame Transaction

**Files:**
- Create: `src/Als.Core/Animation/AlsP5RuntimeBindings.cs`
- Create: `src/Als.Core/Animation/AlsP5Runtime.cs`
- Create: `tests/Als.Core.Tests/AlsP5RuntimeTransactionTests.cs`
- Create: `tests/Als.Core.Tests/AlsP5HotPathAllocationTests.cs`
- Modify: `src/Als.Core/Diagnostics/AlsResultDigest.cs`
- Modify: `tests/Als.Core.Tests/AlsResultDigestTests.cs`
- Modify: `tests/Als.Core.Tests/ContractLayoutTests.cs`

- [ ] **Step 1: Add failing whole-frame transaction tests**

Cover fixed subsystem order, invalid input/binding, Base/Turn/Rotate occurrence/epoch/authority handoff, member -> Idle/non-member and Turn -> Rotate Notify State closure, simultaneous current/outgoing branches using the same animation, restart/backward-seek/inactive-reuse epoch changes, crossfade expiry closure and rollback/retry epoch stability, per-member Sync mappings, resolved Action segment playback, accepted-frame zero advancement plus fade-in, Action/Transition replacement crossfade and rapid second replacement, runtime-recovery cancel followed by same-frame accepted Start reusing one outgoing tail, partial-frame terminal fade and tail-only logical inactivity, multi-slice Action timeline and cross-segment final playback, queued-N/promoted-N+1 Transition with full two-frame cooldown, action + transition lane independence, authoritative P4-equivalent foot/AllowTransitions curves across idle/grounded/crouch/jump/fall/land plus Turn/Rotate crossfades, graph/timeline weight bit equality, timeline preflight, EarlyBlendOut same-frame decision, successful prepared/finalized token, stale prepared token, injected failure between prepare/finalize, retry of identical identity, event/action result digest, cursor/authority/ownership/blend rollback and whole P5A path warm allocation.

- [ ] **Step 2: Run RED transaction tests**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5RuntimeTransactionTests|FullyQualifiedName~AlsP5HotPathAllocationTests"
```

Expected: FAIL because no frame orchestrator or prepared token exists.

- [ ] **Step 3: Implement span-based binding and scratch views**

`AlsP5RuntimeBindings` is a constructible `readonly ref struct` beginning with `int Version`, `ulong Digest` and `ulong LayoutDigest`, followed by the compiled general-curve keys/bindings/semantic policy, Core-owned `AlsP4FootCurveRuntimeBinding` span and four numeric IK constants, event/marker/sync/transition/action/section/segment/action-timeline spans. Version is `1`. Freeze the binding FNV-1a 64-bit input sequence exactly as little-endian `Version`, then `LayoutDigest`, then every remaining payload scalar and every span element field in constructor/declaration order; exclude the `Digest` field itself and all struct padding. The Task 13 owned snapshot and every `CreateCoreView()` result carry this same nonzero `Digest`, never recompute a differently ordered variant. Full span/range/cross-reference validation and digest recomputation happen exactly once in the snapshot compiler and again only at character `Configure` as an initialization gate. The view constructor and per-frame `CreateCoreView()` are O(1): assign the already-validated header/spans and check only `Version==1`, nonzero `Digest` and nonzero matching `LayoutDigest`; they never scan or hash immutable tables. Production arrays remain private snapshot storage, so hot Core APIs may trust the configured view. Mutation/rejection tests target compiler/initialization validation, not a per-frame constructor rescan. The Core foot binding mirrors only `{ AnimationId, LeftLockCurveId, RightLockCurveId, LeftLockDefault, RightLockDefault }`; it never references the Import `AlsFootCurveProfile` type. `AlsP5RuntimeScratch` is a constructible `ref struct` over caller-owned candidate cursors, authority and ownership arrays, candidate Action/Transition lane blend states/instructions, at-least-16 occurrence slots, at-least-16 `AlsActionTraversalSlice` slots, a Timeline playback span sized to `basePlaybackCapacity + 2 Transition + (2 * actionTraversalCapacity)`, active curve blend spans, Sync mapped-playback output and temporary event/outcome buffers. `AlsP5FrameInput` is a constructible `readonly ref struct` containing identity, finite simulation frame start/end, delta, current slot generation, `AlsActionRequest`, internal `CancelActionForRuntimeFailure`, locomotion/rotation/stance/input scalars and readonly phase-1 playback/curve views produced by the controller. P4 physical transition evidence is intentionally absent here because it does not exist until after the single graph advance and pose transaction. Each ref-struct surface has an explicit constructor assigning every readonly field; none can escape the call or be stored in `AlsRuntimeState`. To satisfy C# ref-safety, a constructor that stores a nested ref-struct/span view accepts that view by value, never by `in`; `in` remains permitted for ordinary unmanaged scalar structs that do not carry managed references or spans.

Freeze the phase-1 value surfaces as `AlsBasePlaybackDescriptor(OccurrenceHandleId, AnimationId, AuthorityGroupId, PlaybackEpoch, PreviousUnwrappedTimeSeconds, CurrentUnwrappedTimeSeconds, FrameStartOffsetSeconds, FrameEndOffsetSeconds, DurationSeconds, Weight, Loop, ActivatesAtFrameStart, ClosesAfterFrame)` plus a Core-owned `readonly ref struct AlsP4CurveFrameInput` with `ReadOnlySpan<AlsBasePlaybackDescriptor>` fields named `Base`, `TurnBanks` and `RotateBanks`, followed by scalar `AlsAnimationState`, `ActionBlendAmount` and `ActionModeBlendAmount`. Every contributor therefore carries the full occurrence, authority and local-window key. The controller computes an early non-loop clamp end from its unscaled seek/rate decision with double intermediates; looped/full contributors use `[0,delta]`, and closure occurs at the true clamp offset. All three spans feed Timeline; only Base entries declared as Sync members feed Sync, and mapped Sync times replace only that exact matching occurrence's curve/timeline time. Before Timeline, Core derives the same final frame weight used by P4 composition: each Base weight is multiplied by `(1 - ActionBlendAmount)`, each Turn-bank weight by `ActionBlendAmount * (1 - ActionModeBlendAmount)`, and each Rotate-bank weight by `ActionBlendAmount * ActionModeBlendAmount`. These contributors share the globally remapped P4 replacement authority domain, so Idle/non-member/member and Turn/Rotate handoffs select one event owner and close the displaced State. Core reproduces the existing P4 curve sampler exactly: choose IK weight from `GroundedIkWeight/JumpStartIkWeight/FallLoopIkWeight/LandRecoveryIkWeight`, weight-blend current/previous Base locks, weight-blend Turn banks, weight-blend Rotate banks, lerp Turn/Rotate by `ActionModeBlendAmount`, then lerp Base/action by `ActionBlendAmount` and clamp lock values once. Missing curve bindings use the frozen per-binding defaults. No Godot curve resampling is authoritative in P5 mode.

- [ ] **Step 4: Implement prepare/finalize without mutating committed state**

Expose:

```csharp
public static bool TryPrepare(
    in AlsP5RuntimeBindings bindings,
    in AlsP5FrameInput input,
    in AlsRuntimeState preFootCandidateState,
    ReadOnlySpan<AlsTimelineCursor> currentCursors,
    ReadOnlySpan<AlsTimelineAuthorityState> currentAuthorities,
    ReadOnlySpan<AlsNotifyStateOwnership> currentOwnership,
    ulong currentNextOwnerToken,
    ref AlsP5RuntimeScratch scratch,
    out AlsP5PreparedFrame prepared,
    out AlsP5FailureCode failure);

public static bool TryFinalize(
    in AlsP5PreparedFrame prepared,
    ref AlsP5RuntimeScratch scratch,
    in AlsFrameResult p4Result,
    in AlsRuntimeState p4NextState,
    in AlsDynamicTransitionInput currentP4TransitionProbe,
    Span<AlsTimelineCursor> nextCursors,
    Span<AlsTimelineAuthorityState> nextAuthorities,
    Span<AlsNotifyStateOwnership> nextOwnership,
    out ulong nextOwnerToken,
    out AlsRuntimeState nextState,
    out AlsFrameResult result,
    out AlsP5FailureCode failure);
```

`preFootCandidateState` is the current frame's already-evaluated P3 locomotion/view/Turn/Rotate candidate with the prior committed P4 foot and P5 fields; it is not the previous frame's whole committed state. `p4NextState` is that same candidate after the current P4 foot-placement/pose transaction. Finalize must preserve the latter and replace only P5-owned fields.

Freeze both lane blends as Core-owned, frame-discrete linear envelopes. The only step function, in this exact float operation order, is `Step(value,target,seconds,b) = b <= 1e-5 ? target : clamp(value + sign(target-value) * seconds / b, 0, 1)`. Core computes candidate values once; rollback consumes nothing. The graph has one logical Action gameplay owner and one logical Transition gameplay owner, but each has at most two purely visual banks `O` (outgoing) and `I` (incoming). Canonical states are: only `I` => `IncomingMix=1`; only tail `O` => `IncomingMix=0`; `O+I` => mix in `[0,1]`; neither => both scalar values zero. `LaneWeight` mixes the lane against Base (or sets Transition additive amount). Compute once in declaration order: `OutgoingEffectiveWeight = O.Active ? LaneWeight * (1 - IncomingMix) : 0`, then `IncomingEffectiveWeight = I.Active ? LaneWeight * IncomingMix : 0`. `AlsLaneGraphInstruction` carries all four floats; the AnimationTree receives only `LaneWeight` and `IncomingMix`, while the controller preserves and validates the two effective weights for Timeline/diagnostics with no second calculation or graph parameter.

On first Action accept or Transition promotion with no tail, set `I=new`, `O=none`, `IncomingMix=1`, and step `LaneWeight` from `0` toward `1` using the complete frame delta; Action clip delta remains zero in its acceptance frame. Accept while a tail exists retains that sole `O`, sets `I=new`, starts mix at zero and steps both scalars toward one with the new definition's `BlendSeconds`.

Replacement first captures the replaced logical source's effective contribution `P` from the old instruction (`P=LaneWeight` when no old tail, otherwise `P=IncomingEffectiveWeight`), discards any older visual tail, freezes the replaced logical pose as the sole `O`, and rebases `LaneWeight=P`, `IncomingMix=0` before installing the new `I` and stepping both toward one. This rebase prevents a rapid second replacement from jumping the replaced source from `LaneWeight*IncomingMix` back to the full lane weight. The old gameplay owner closes and the new one activates at frame offset zero; the discarded tail emits nothing.

For a terminal at offset `tau`, first step the active `LaneWeight/IncomingMix` toward their steady-state targets for exactly `tau`, then capture the logical source contribution `P` at that instant. Discard an older tail, rebase that source to the sole frozen `O`, clear `I`, set `IncomingMix=0`, and step `LaneWeight` from `P` toward zero for only `frameDelta-tau`. Natural completion, explicit cancel and runtime failure use the active definition's `BlendSeconds`; explicit cancel/runtime failure have `tau=0`. EarlyBlendOut uses its payload `blendOutSeconds` with `tau=frameDelta`, so residual time is zero and fade starts next frame. If a normal Start is accepted later in the same recovery frame, reuse the just-created tail and follow accept-with-tail without first consuming a separate fade-out or emitting another old-owner outcome. A zero blend snaps; at weight zero clear the bank. Visual tails never enter Timeline, authority, Notify ownership or outcomes and never become gameplay owners.

Every Timeline `Weight` is bit-copied from the matching source's effective weight in that frame's frozen prepared instruction. Transition replacement uses outgoing weight for the old zero-window close and incoming weight for the new activation. Action Montage and Sequence playbacks normally share the incoming effective weight. Natural completion, explicit cancel and runtime-failure paths that rebase before freezing the instruction use its outgoing effective weight for their closing interval. EarlyBlendOut is the deliberate exception: the current frame's graph instruction remains the pre-rebase incoming instruction, so its frame-end closing Montage/Sequence intervals copy that exact `IncomingEffectiveWeight`; only the committed next blend state rebases the logical source to sole `O` with `LaneWeight` equal to that incoming contribution and `IncomingMix=0`, starting the tail fade on the next frame. Thus threshold/authority and the actual graph pose consume one Core-computed scalar. A weight-zero missed instant still advances its cursor. Tests cover accept fade-in with zero Action clip delta, replacement crossfade, a rapid second replacement, partial-frame natural completion, cancel/EarlyBlendOut/runtime-failure fade, EBO incoming-close/next-tail rebase bit equality, zero blend, threshold consumption, graph/timeline bit equality and byte-identical rollback/retry.

Timeline window propagation is mechanical: Base/Turn/Rotate copy both descriptor offsets; the old Transition replacement close uses `[0,0]` and current Transition uses `[0, playback.FrameEndOffsetSeconds]`; Action Montage and Sequence copy each traversal slice's start/end offsets. The same offsets populate `AlsLaneGraphSource.ContributingDeltaSeconds` for Transition and the final Action segment. Reject any mismatch before Timeline, so no subsystem may independently infer a terminal time from clamped floats.

`TryPrepare` validates everything, applies `CancelActionForRuntimeFailure` first when armed, then applies the frame's one normal `ActionRequest`, promotes/advances the Transition queued by N-1, advances Action through its resolved segment and traversal slices, and determines every request/replacement/natural-terminal frame offset. It then advances provisional candidate lane blends. Before Timeline, `TryInterruptEarlyBlendOut` checks the compiled EBO State intervals at the candidate final Montage time using the provisional incoming effective weight and the exact definition handle/threshold plus Gather value conditions. This reproduces whether the EBO State is active at frame end without mutating Timeline ownership. If it triggers, freeze this frame's Action instruction from the unchanged incoming graph state, set both Action close flags/outcome at frame end using that instruction's `IncomingEffectiveWeight`, and separately rebase only the committed next blend state to an outgoing tail; residual fade time is zero, so this frame's graph weights do not change. Freeze the Transition instruction normally.

Next compute Sync, write one mapping per active declared Base occurrence, use those mapped times for curves, and call Timeline exactly once over the complete Base/Turn/Rotate/Transition/Action playback set with weights copied from those frozen instructions. Transition replacement contributes its old zero-window closing playback and new activating current playback; natural completion and EBO mark their final playback windows closing before this one global preflight/sort. Task 12 maps the terminal Action outcome into each closing Montage/Sequence playback's `TerminationReason`: `Completed`, `InterruptedByReplacement`, `InterruptedByExplicitCancel`, `InterruptedByEarlyBlendOut` or `InterruptedByRuntimeFailure`; nonterminal segment/section handoffs and every non-Action closure use `None`. For each Action slice emit Montage-domain and Sequence-domain Timeline playbacks using Montage-coordinate previous/current time, exact definition-handle filtering, local window and the slice's respective activation/close flags; never use clip coordinate or flatten those authored sources into one authority candidate. An accepted/replaced Action uses its zero-delta initial slice and cannot advance in that frame, but its one-time activation may publish authored time-0 events whose `Weight` already equals the new graph contribution. `TryPrepare` returns the frozen graph instructions, authoritative `LeftIk/RightIk/LeftLock/RightLock/AllowTransitions` curve values, mapping counts and a transaction token. It writes only scratch; sampling a follower at the pre-remap common phase, using old weight for an accepted time-0 event, dropping a Transition closure, collapsing multiple Action slices to one timeline interval, omitting Turn/Rotate timeline contributors, cross-matching reused-Sequence definitions, suppressing Montage or Sequence events, or diverging from the frozen P4 foot blend is a test failure.

After the graph and P4 pose succeed, `TryFinalize` accepts that token once, validates the current P4 transition probe, queues any valid Transition for N+1 using prepared `AllowTransitions`, copies every non-P5 field from the supplied `p4NextState` unchanged, overlays only the prepared P5 state/blend fields, publishes preflighted occurrences/cursors/authorities/ownership into caller outputs, merges P5 fields into the supplied P4 result and sets `P5FailureCode=None`. Tests mutate every P4 foot-lock/pelvis/pose field between prepare and finalize and require bit-identical preservation in `nextState`. Any failure publishes no candidate state/event/outcome. The split is mandatory: no implementation may select a Transition from current physical targets and also affect the already-advanced graph in the same frame.

- [ ] **Step 5: Freeze failure recovery behavior**

Core stores no pending failure bit and has no failed-identity policy. It only honors the explicit `CancelActionForRuntimeFailure` scalar supplied in `AlsP5FrameInput`, closing old ownership once and emitting `InterruptedByRuntimeFailure` in that candidate. Core tests apply that flag to a fresh input and prove it commits transactionally. Task 17 alone owns retry policy: an exact failed identity is short-circuited before Core/graph work with no result/event/outcome publication, so the observable committed digest remains unchanged; the first strictly newer identity receives the flag, and the external latch is consumed only after successful finalize/controller/state commit.

- [ ] **Step 6: Run GREEN Core closure**

Run:

```powershell
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~AlsP5RuntimeTransactionTests|FullyQualifiedName~AlsP5HotPathAllocationTests|FullyQualifiedName~AlsResultDigestTests|FullyQualifiedName~ContractLayoutTests"
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug
dotnet test GodotALS.sln -c Debug --no-restore
```

Expected: all tests PASS; retry digests match; full Curve/Timeline/Sync/Transition/Action prepare/finalize loop is `0 B` after warm-up.

- [ ] **Step 7: Commit**

```powershell
git add src/Als.Core/Animation src/Als.Core/Diagnostics/AlsResultDigest.cs tests/Als.Core.Tests/AlsP5RuntimeTransactionTests.cs tests/Als.Core.Tests/AlsP5HotPathAllocationTests.cs tests/Als.Core.Tests/AlsResultDigestTests.cs tests/Als.Core.Tests/ContractLayoutTests.cs
git commit -m "feat(core): transact complete p5 animation frames"
```

### Task 13: Add the Deterministic P5A Cross-engine Golden

2026-09-08 后续修订已获用户批准，以下范围优先于本节旧的原生清单数量、F91 结束帧和 snapshot 冻结值；Task 13C 验收完成前仍不进入 Task 14：

- 先通过独立诊断模式发现八个用例、374 帧实际涉及的原生资产与 Notify，再冻结独立的辅助审计清单。诊断输出不能发布为 Golden；正式采集仍拒绝未知资产，辅助资产不能冒充 canonical source。
- Roll 必须区分片段遍历结束、动作生命周期结束与视觉淡出。以真实 Montage 参数和逐帧观测校核自然停止规则，必要时扩展 Core 的值类型状态与 Import 绑定；不得硬编码帧号、容忍整帧差或把原生输出作为 Core 输入。
- 已导出的 canonical Roll 包含 `.1` 淡入、`.3` 淡出、HermiteCubic、默认触发时间和自动淡出开关，无须为此重复导出。profile 的 `.2` lane blend 仍是独立呈现策略。
- 废除逐帧原生输出与旧合成整份模板的相等校验，改为闭合字段/类型、精确来源/身份、跨帧生命周期约束和独立跨引擎比较。事件、活动状态、结果数组保留 `16/16/2` 上限及双方严格顺序，不冻结旧帧数量。
- 所有受影响的版本、绑定摘要和 schema 同步更新；未变的资产、layout、graph 不制造无关变更。新增状态必须覆盖事务回滚、reset、审计序列化及零分配测试。
- 本次计划与 trace schema 升为 `2`，bindings 为 `2/e458fef4df7a854d`；layout 与 graph 仍为 `1`。独立辅助清单冻结 9 项资产、8 项 authored Footstep，既有 9 个语义 source 不增加。静态事件清单不等于实际发生记录。
- 混合权重可能先于剩余时间归零达到目标，Core 保留真实浮点余时而不拒绝或强制清零。正式原生闭包只允许已验证的动态 Montage 包装引用已登记的持久资源，不普遍放行 transient 资产。
- `Enable_Transition` 按既定 `AdditiveToDefault` 规则累计：默认值 `1` 加各实际曲线增量乘有效权重，缺失曲线贡献 `0`。已是增量的 authored 值不能再次减去默认值；原始动画图曲线只要求有限值，不强制限制为 `[0,1]`。
- 双次完整原生采集、双次独立 Core 回放、同引擎确定性、跨引擎比较、完整拒绝矩阵和独立审查通过后，才发布新 Golden。相机、输入、P4 行为与 `Gather -> Worker -> Commit` 所有权保持不变。

2026-09-08 用户确认的 13C 修正：当前 Golden 尚未通过真实采集复核，不能据此进入 Task 14。以下规则覆盖先前诊断输出中不可靠的原生数值，但不改变资产、reference commit、snapshot 或 374 个测量帧：

- `/Game` 语义曲线与 exporter 的 authored keys 同源，调用 `EvaluateCurveData(..., true)`；`canonicalAssetOracle.compressedCurves` 独立调用 `false`，只作原生审计，不替代语义值。`Enable_Transition` 必须实际采样。
- 每个测量帧只执行一次正常 engine/world update，不额外调用动画 tick 或骨骼 refresh。`nativeActual.frameUpdateAudit` 的 `animationUpdates/evaluations/postUpdates/meshTicks` 必须都是实测整数 `1`；初始姿态在测量前通过普通预热稳定。
- Started/Cancelled/Finished 来自真实 Montage delegate。结束前保存播放值快照并按作用域解绑回调；禁止补造实例、延后结束帧或把视觉权重改成 `1`。Transition 注入仅限已批准的脚部探针与更新 guard，不强制打开 transition permission。
- Physical Notify 使用队列与活动状态快照，明确不声称通用回调拦截。顺序依据引擎执行约定；可能在单帧内进入并退出且不能证明的短状态拒绝采集，未知相关来源拒绝，消失状态不得继续输出 Tick。
- Montage 结束不等于所有 Sequence State 已消失。若本帧原生活动列表仍有同一 State，只能通过先前唯一的 NotifyInstanceID 与精确来源/事件身份连续观察，采用本帧引用时间，不推进已销毁的 Montage。仅队列残留不产生 Tick；结束快照仅可用于真实结束回调所在帧。原生实际保留的状态如实记入 physical audit。
- 原生 canonical 必须由 raw DTO 与精确 source/event/marker crosswalk 投影，不能复制冻结的 native expected。压缩审计与原生视觉权重不能作为 Core 输入。真实跨引擎结束帧或离散状态差异必须报告，不得用容差、冻结值或挪帧掩盖。
- 只有修正后的两次真实采集、独立 Core replay、跨引擎比较与 verifier 全部通过，才能重新发布并提交 Golden；旧的未提交 fixture 不是通过证据。

2026-09-09 Task 13C 验收完成，Task 14 及后续 Godot 接入尚未实施：

- 已完成真实曲线/单次正常更新/生命周期回调修正、独立原生 canonical 投影，以及由资产参数驱动的 Core Montage 生命周期。当前固定输入下 Roll 自然结束于 F90；实现不硬编码该帧，片段遍历、动作结束和视觉淡出保持分离。
- 正式七步生成流程和独立 UE-free verifier 均正常退出，分别输出唯一 `P5A_GOLDEN_GENERATION_OK` 与 `P5A_GOLDEN_FIXTURE_OK`，用例数均为 `8`，reference commit 均为 `b754d6f0f2bb03741d301f8fb88077ebfe561e17`。374 帧的双次原生采集、双次独立 Core 回放、同引擎确定性、跨引擎比较及原子发布通过。
- 唯一 Golden 为 `tests/Als.Core.Tests/Fixtures/P5A/trace_p5a_runtime.json`，1047469 字节，SHA-256 为 `DE6E763FC71C7173D960287A94101099F0FBDDD0CE5C320CBA1F535089906D1A`。Git 自动换行开启时的重新检出也保持相同字节与哈希。
- 最终脚本回归 `71/71`、重编译插件后的原生测试 `15/15`、Debug Import 绑定测试 `45/45` 通过。Release Import 全量为 `519/519`。独立审查为 `SPEC PASS / QUALITY PASS`。
- 所选 226 项矩阵由原运行的 225 项通过，以及唯一 Family6 清理失败后的完整同测试重跑通过共同覆盖；两份原始 TRX 均保留，不能称为单次 226/226 全绿。正式 verifier 自己的子测试报告则全部通过，与该覆盖并集无关。
- 全量 Core 回归仍为 `1376/1378`，两项 Sync 输入/绑定冲突测试未在本轮修复；代码历史显示冲突早于本轮，但没有用干净 HEAD 重跑来证明基线失败。Import 此前一次 `2264B` 分配波动尚未解释，后续通过不等于已定位并修复。因此本记录不是仓库全绿或完整 P5A Demo 验收。
- 实际运行发现的两项工具兼容问题已通过测试先行修正：仅允许受文件锁和身份审计约束的 `zen.exe` 启动同目录 Crashpad；原生自测保留执行，仅将普通诊断移出正式标记前缀。未放宽正式标记、进程身份或发布校验。相机、输入、P4 行为、资产锁与并发所有权均未改动。

**Files:**
- Modify: `src/Als.Import/Als.Import.csproj`
- Create: `src/Als.Import/Compilation/AlsP5CoreRuntimeBindingSnapshot.cs`
- Create: `src/Als.Import/Compilation/AlsP5CoreRuntimeBindingCompiler.cs`
- Create: `tests/Als.Import.Tests/AlsP5CoreRuntimeBindingCompilerTests.cs`
- Create: `tools/schemas/als_p5a_trace.schema.json`
- Create: `tools/schemas/als_p5a_trace_plan.schema.json`
- Modify: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsLocomotionTraceCommandlet.cpp`
- Create: `src/Als.Core/Animation/AlsP5aTrace.cs`
- Create: `tools/Als.P5aOracle/Als.P5aOracle.csproj`
- Create: `tools/Als.P5aOracle/Program.cs`
- Create: `scripts/generate-p5a-golden.ps1`
- Create: `scripts/verify-p5a-golden.ps1`
- Create: `tests/GenerateP5aGolden.Tests.ps1`
- Create: `tests/Als.Core.Tests/AlsP5aGoldenTests.cs`
- Create: `tests/Als.Core.Tests/Fixtures/P5A/trace_p5a_runtime.json`
- Modify: `tests/Als.Core.Tests/Als.Core.Tests.csproj`

- [x] **Step 1: Add failing schema/generator/golden tests**

Require trace kind `P5A`, schema version `1`, exact reference commit, patch hashes, real asset IDs, sample window, curve value, layout/Core-binding/graph-build versions and digests, occurrence identity/order, marker pair/cycle/phase, transition choice and Action state/outcome. Freeze a separate version-1 native trace-plan sidecar with distinct `sources` and `cases` sections. Each source entry contains a lowercase 40-hex `traceSourceId`, the host-only exact layout key `{ SourceKind, SourceBindingIndex, GraphSlotIndex }`, and expected UE-observable evidence `{ assetStableId, nativeRole, montageStableId, sectionName, segmentIndex }` with inapplicable fields empty/-1. The Oracle host alone derives `traceSourceId` as lowercase SHA-1 of this exact byte preimage in field order: raw ASCII `ALS_P5A_TRACE_SOURCE_V1` (no terminator), the compiled `SourceKind` byte, signed `SourceBindingIndex` and `GraphSlotIndex` as little-endian `int32`, then `assetStableId`, `nativeRole`, `montageStableId` and `sectionName` each as a little-endian signed `int32` UTF-8 byte count followed by the exact UTF-8 bytes without normalization, then signed `segmentIndex` as little-endian `int32`. The host rejects a digest collision or duplicate preimage; UE only verifies and echoes the supplied ID after observing the evidence and never reproduces this allocator. Tests mutate every preimage field and freeze the generated IDs. Every case begins from the same declared empty/default P5 ownership, cursor, authority, Action, Transition and lane state; neither schema serializes Core-only internals nor asks UE to reset a non-native structure. The ordered schedule instead uses enough common warm-up frames to build every long-lived state on both sides. Per-frame shared semantic inputs are identity/generation and simulation window/delta; P3 locomotion state, locomotion/rotation mode, stance and `hasInput`; Base/Turn/Rotate source time, weight, activation/closure and local contributing window keyed by `traceSourceId`; semantic Action request; and upstream P4 physical foot target/lock probe. The Action request wire is exactly `{ command, requestId, actionTraceSourceId, startSectionName, priority, slotGeneration }`: Start/Cancel reference an `ActionMontage` source entry, None uses empty source/section, and `requestId` follows the lossless signed-64 rule below. The Oracle resolves that source + section name through the snapshot into `ActionDefinitionId/StartSectionId`; UE resolves it through sidecar-observed Montage/section identity into native objects. Both schemas reject `ActionDefinitionId`, `StartSectionId` or any other Import/profile integer identity in shared/native rows. `AllowTransitions`, general curve samples, Sync mapping, Timeline ownership and Action/Transition/lane next state are outputs independently computed by each engine, never plan inputs. No expected/native observation is stored in the input section. UE resets its native character to the declared semantic default and consumes these frames; the port oracle creates Core defaults and independently consumes the same frames. UE raw `nativeActual` rows use `{ traceSourceId, observedAssetStableId, nativeRole, observedMontageStableId, observedSectionName, observedSegmentIndex, ...observed values }`; they never contain profile indices, graph slots, occurrence handles or authority IDs. Canonical rows contain the sidecar/layout-resolved handle/authority identity. Reject duplicate/unknown trace IDs, evidence mismatch, non-unique layout resolution, missing/extra/reordered case frames and any unused sidecar source. Tests mutate every true input family and require the port result to change or reject even when a fixed nativeActual document is held constant; changing curve keys/samples or playback time must independently change computed `AllowTransitions`/Transition behavior. Tests also reject any code path that reads nativeActual values to construct Core input, and reject any plan field that attempts to seed Core-only state or `AllowTransitions`. Add a failing pure bridge test that maps every Import curve/event/marker/sync/transition/action/section/segment/foot and graph-build field into owned arrays, mutates each source field, validates all three digests, and round-trips `CreateCoreView()`, `CreateOccurrenceLayoutView()` and `CreateGraphBuildView()` without Godot. Freeze exactly these eight ordinal case IDs: `grounded_marker_interval`, `authority_tie`, `standing_transition_left`, `standing_transition_right`, `crouching_transition_reuse`, `roll_default_section`, `montage_owned_notify`, `segment_sequence_notify_state`. Add generator tests for deterministic sidecar generation, native run twice, both trace representations, the oracle host ProjectReferences, native canonicalization/Core oracle twice through the same compiled snapshot, same-engine byte comparison, cross-engine tolerant structural comparison, stale-fixture rejection, lock check, staging cleanup, atomic publication and exact case set/count.

Freeze lossless wire types in both schemas. `LayoutDigest`, `BindingDigest`, `GraphDigest` and every other exact `ulong` (including owner token) are exactly 16 lowercase hexadecimal characters with no `0x`; every exact signed 64-bit field, including frame ID, playback epoch/cycle, event sequence and Action request/outcome `RequestId`, is an invariant-culture decimal string matching `^(0|-?[1-9][0-9]*)$`. JSON numbers are forbidden for all 64-bit fields. Float/double samples remain finite JSON numbers. The sidecar contains no self-referential digest field: after the host closes the complete file, the PowerShell generator hashes its exact raw bytes with SHA-256, passes the lowercase 64-hex value separately as `-P5ATracePlanSha256`, and requires raw/canonical documents to repeat that value. UE hashes the exact bytes before parsing and rejects any mismatch, so C++ and C# share no implicit canonical-JSON algorithm. Checked invariant parsers handle all 64-bit fields. Schema/tests cover each signed family above `2^53` (including `RequestId`), `long.MinValue/MaxValue`, `ulong.MaxValue`, `-0`, leading signs/zeroes, wrong case/width and overflow; generator tests cover BOM/LF/trailing-byte changes by their byte hash.

- [x] **Step 2: Run RED golden tests**

Run:

```powershell
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/GenerateP5aGolden.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsP5aGoldenTests
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter FullyQualifiedName~AlsP5CoreRuntimeBindingCompilerTests
```

Expected: FAIL because the pure Core-binding bridge, trace kind, schema, generator and fixture do not exist.

- [x] **Step 3: Extend the existing UE trace commandlet**

Add `-TraceKind=P5A -P5ATracePlan=<absolute staging path> -P5ATracePlanSha256=<64-lower-hex>` to the same plugin and shared ready/build flow. Capture real ALS runtime state from the locked reference and source project for the eight frozen cases: grounded Left/Right marker interval, authority tie, Standing Left/Right transition, Crouching explicit-reuse transition, Roll `Default` section, Montage-owned Notify and segment Sequence Notify/State. Before capture, C++ hashes the exact sidecar bytes, strictly validates the supplied hash/schema/commit/assets, resolves each requested UE package/object, resets the native runtime to the common semantic default and applies only that case's ordered warm-up/input frames. It checks the observed runtime role plus Montage/section/segment evidence. Every event/playback row writes only `traceSourceId` plus independently observed `nativeActual` evidence/results; input rows are neither copied into actual output nor synthesized from observations. C++ neither copies the .NET layout allocator nor emits profile indices, graph-slot IDs, Godot occurrence/authority IDs or port reason codes. Emit exact `P5A_TRACE_READY_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17` and `P5A_TRACE_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`.

- [x] **Step 4: Generate deterministic native and port documents**

In this task add a one-way `Als.Import -> Als.Core` project reference; Core remains independent. Declare `AlsP5CoreRuntimeBindingCompiler` as `public static class` and `AlsP5CoreRuntimeBindingSnapshot` as `public sealed class`; `Compile`, `CreateCoreView`, `CreateOccurrenceLayoutView`, `CreateGraphBuildView`, `Version`, `Digest`, `LayoutDigest`, `GraphDigest` and `AnimationSetDefinitionDigest` are public so both the Oracle host and Godot assembly can consume the bridge, while every owned backing array remains private and immutable after construction. `AnimationSetDefinitionDigest` is the existing canonical 64-lower-hex Task 3 definition digest copied at compilation; it is initialization provenance, not a hot Core string field. The compiler is the sole pure bridge from the canonical animation set plus P3/P4/P5 profiles and `AlsP5OccurrenceLayout` into owned arrays. It exact-copies the complete layout entry array, including Idle/non-member Base banks, and `CreateOccurrenceLayoutView()` returns that owned copy with the original layout version/digest; no consumer reconstructs handles from bindings or a digest. `CreateCoreView()` returns `AlsP5RuntimeBindings`; it maps the Core curve enum, exact required handles/ordinals, Base/Turn/Rotate authority/windows, foot constants, marker/sync data and every Transition/Action binding. Snapshot version is `1`; its nonzero binding FNV-1a digest uses exactly the Task 12 byte sequence: little-endian `Version`, `LayoutDigest`, then every remaining runtime payload scalar and array element field in constructor/declaration order, excluding the stored `Digest` field and padding.

Add the public value-only types `public readonly record struct AlsP5GraphSample(int AnimationId, float X, float Y, float RateScale)` and `public readonly record struct AlsP5GraphMaskHeader(AlsPoseMaskKind Kind, int LogicalRootBoneId, int BoneOffset, int BoneCount)`. Add an Import-owned constructible `readonly ref struct AlsP5GraphBuildView` with `Version=1`, `Digest=GraphDigest`, `SkeletonId`, `MannequinMeshId`, `RootMotionExtractionLogicalBoneId`, `RootMotionExtractionPhysicalBoneId`, the existing public value type `AlsPresentationDefinition`, Standing/Crouching idle IDs, Jump/Fall/Land IDs, Lean additive base ID, `ReadOnlySpan<AlsP5GraphSample>` Standing/Crouching/Lean samples, `ReadOnlySpan<int>` P3 `AllAnimationIds`, the existing public value type `AlsAimProfile`, `ReadOnlySpan<AlsTurnProfile>`, `ReadOnlySpan<AlsRotateProfile>`, `ReadOnlySpan<AlsP5GraphMaskHeader>` plus one flattened `ReadOnlySpan<int> LogicalBoneIds`, and the exact normalized-track `ReadOnlySpan<int>`. Mask roots/members are explicitly canonical logical IDs; no physical bone index or Godot path enters this view. They preserve complete P4 pose provenance for Configure validation and the existing component-pose stage, but are not AnimationTree bone filters. No reference-type `AlsLocomotionAnimationSample` crosses this public view. Its public constructor assigns every readonly field; every stored span parameter is passed by value, never `in`, and `CreateGraphBuildView()` directly constructs it over snapshot-owned arrays so the ref struct cannot escape their lifetime. It contains the complete P3 locomotion/Lean graph topology plus P4 Aim/Turn/Rotate graph data and P4 mask provenance, not just event-bearing or Sync clips. The compiler requires the P3/P4 skeleton IDs to match, resolves `RequiredBones.Root` through the canonical `LogicalToPhysical` table without assuming name or index zero, and includes every field/span element in a separate nonzero little-endian FNV-1a `GraphDigest` in the exact view declaration order, recursively using the named value-type declaration order and excluding digest/padding. Compile performs the one full validation/hash before publication; character Configure may explicitly revalidate once, while all three `Create*View()` methods only copy validated headers/spans in O(1). It contains no Godot type/resource/path. Bridge tests compile the exact public constructor/ref-safety/type surface, prove both element structs contain no managed references, mutate every layout/runtime/graph field, Aim ID, normalized membership, skeleton/mannequin/root ID and mask range through compiler/Configure validation, exercise a non-identity logical-to-physical permutation, and separately prove every AnimationSet mutation changes the copied definition provenance. Task 14/15 must consume this same snapshot and may only add validated Godot resource/parameter/Action-filter handles.

The script first invokes the tracked Oracle host in `--write-native-plan` mode. The host loads the canonical manifest plus P3/P4/P5 profiles, calls the one Task 5 layout compiler and the public pure Core-binding compiler, then obtains `layoutView`, `runtimeBindings` and `graphBuildView` from that one snapshot and atomically writes the deterministic source map plus complete case/input schedule; two writes must be byte-identical. The script then invokes UE twice into separate raw roots with that same read-only sidecar and validates/diffs those raw bytes. Finally it invokes the host twice to call `AlsP5aTrace.WriteCanonicalPair(rawPath, tracePlanPath, nativeCanonicalPath, portCanonicalPath, in layoutView, in runtimeBindings, graphBuildView.Digest)`. Before either call, the host requires `graphBuildView.Digest == snapshot.GraphDigest`; the Core method requires the plan and raw metadata to equal the supplied current `graphDigest` as well as the current layout/binding digests before resolving a row. This scalar keeps Core independent of Import while preventing a graph-only P3/P4 profile change from being laundered through an old sidecar. The method validates every raw `traceSourceId` and observed evidence against exactly one sidecar entry, validates that entry against exactly one layout/binding entry, resolves handles/authorities to create the native-canonical document, and separately executes the full Core Curve/Timeline/Sync/Transition/Action oracle from the sidecar's initial state and frame inputs to create the port-canonical document. The port path may read raw only for provenance/case-key completeness and never for a runtime input or expected value. It never references Import or allocates IDs. Native run 1/2 canonical bytes must match each other, and port run 1/2 bytes must match each other. Cross-engine comparison is structural: stable/discrete identity, order, cycles, outcomes and failure codes compare exactly; finite curve/time/phase/weight values compare with absolute tolerance `1e-5`. After both pairwise comparisons pass, atomically publish the native-canonical document as the sole committed fixture; do not require native and port JSON bytes to be identical. Every Oracle host mode emits exactly once `P5A_ORACLE_DIGESTS layout=<16-lower-hex> bindings=<16-lower-hex> graph=<16-lower-hex> plan=<64-lower-hex>`; the PowerShell script parses and suppresses this child marker, requires both runs and the fixture to match, and rejects missing/duplicate/malformed markers. Generator and Core tests include a stale plan whose layout/binding digests remain current but whose graph-only profile field and `GraphDigest` are old; canonicalization must reject it before any output publication. Use the exact assets:

```text
Transition Left:  3e23712571d6bbea8744fc94dad0904a6a0a0b5d
Transition Right: 97d46bf9858376893c1c34a128c27044b4467d82
Roll Sequence:    39eecd72ffdddb8ba0eb2bb0683f1c958d68fdd9
Roll Montage:     2d9341182885d90ad666fff32c025937438b1827
```

The PowerShell generator captures each native subprocess output, requires exactly one ready/generation marker per subprocess, and does not forward those child markers into its own output. Boundary tests include values exactly at, just below and just above `1e-5`, NaN/Infinity rejection and a discrete mismatch despite float tolerance. It emits only one `P5A_GOLDEN_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17` after both native and port documents contain the exact ordered case set and the structural comparison passes.

Add a separate UE-free committed-fixture gate `verify-p5a-golden.ps1`. It validates the checked-in schema/fixture bytes and exact ordered case set/count/commit, then invokes the Oracle host in `--verify-fixture` mode. That mode reloads the current canonical manifest plus P3/P4/P5 profiles, recompiles the current snapshot, requires the fixture's version/layout/binding/graph digests/provenance and every exact integer identity to match, and executes the current Core oracle against native expected values with the same discrete-exact/float-`1e-5` comparator. Pester includes stale layout/binding/graph digests and changed current-profile negative cases. The gate also runs focused `AlsP5aGoldenTests`, rejects warnings/errors or duplicate markers, and alone emits exactly `P5A_GOLDEN_FIXTURE_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`. It never invokes UE or regenerates the fixture and never forwards the generator or native trace markers; this is the exact golden marker consumed by Task 22.

- [x] **Step 5: Run real generation and GREEN golden tests**

Run:

```powershell
pwsh -NoProfile -File scripts/generate-p5a-golden.ps1 -UnrealEditorCmd $unrealEditorCmd -UnrealProject $uProject -ReferenceRoot $referenceRoot
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/GenerateP5aGolden.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Debug --filter FullyQualifiedName~AlsP5aGoldenTests
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Debug --filter FullyQualifiedName~AlsP5CoreRuntimeBindingCompilerTests
pwsh -NoProfile -File scripts/verify-p5a-golden.ps1
```

Expected: deterministic comparison passes, generator prints `P5A_GOLDEN_GENERATION_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`, all eight golden cases PASS, and the committed-fixture gate prints exactly `P5A_GOLDEN_FIXTURE_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`.

- [x] **Step 6: Commit**

```powershell
git add src/Als.Import/Als.Import.csproj src/Als.Import/Compilation/AlsP5CoreRuntimeBindingSnapshot.cs src/Als.Import/Compilation/AlsP5CoreRuntimeBindingCompiler.cs tests/Als.Import.Tests/AlsP5CoreRuntimeBindingCompilerTests.cs tools/schemas/als_p5a_trace.schema.json tools/schemas/als_p5a_trace_plan.schema.json tools/unreal/AlsLocomotionTrace src/Als.Core/Animation/AlsP5aTrace.cs tools/Als.P5aOracle scripts/generate-p5a-golden.ps1 scripts/verify-p5a-golden.ps1 tests/GenerateP5aGolden.Tests.ps1 tests/Als.Core.Tests/AlsP5aGoldenTests.cs tests/Als.Core.Tests/Fixtures/P5A tests/Als.Core.Tests/Als.Core.Tests.csproj
git commit -m "test(golden): add deterministic p5a cross-engine trace"
```

### Task 14: Bind the Compiled P5A Profile and Close the Animation Library

**Files:**
- Create: `src/Als.Godot/Animation/AlsP5aAnimationRuntimeBinding.cs`
- Create (Godot-generated): `src/Als.Godot/Animation/AlsP5aAnimationRuntimeBinding.cs.uid`
- Modify: `src/Als.Godot/Animation/AlsAnimationLibraryBuilder.cs`
- Create: `src/Als.Godot/Animation/P5aRuntimeBindingSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Animation/P5aRuntimeBindingSmoke.cs.uid`
- Create: `scenes/tests/p5a_runtime_binding_smoke.tscn`

- [ ] **Step 1: Add a failing real-profile binding smoke**

Load the canonical v2 manifest, P3 locomotion profile, P4 pose profile and P5A runtime profile. Compile the Task 5 occurrence layout and Task 13 pure Core-binding snapshot once, build the P5A library from `animationSet + snapshot`, then pass only that snapshot and stamped library to the adapter. Assert one skeleton/mannequin and exact required root mapping, six event semantic IDs, one compiled `Enable_Transition` semantic, one 17-member group with duration-bearing bindings, the exact four P4 IK constants and every `AlsFootCurveBinding`, four transition slots, one Roll action, one declared slot/section, all compiled Montage segment bindings and a library closure containing every P3 `AllAnimationIds`/Lean resource, all four P4 Aim resources, every Turn/Rotate resource, both Transition/additive-base resources and every Montage-segment Sequence exactly once. Assert every closure animation has exactly one immutable clip-name/play-length/normalized binding equal to the canonical source and loaded Godot resource. Assert the exact normalized-track subset, GraphBuildView graph topology and logical P4 mask provenance, and an Action filter that excludes exactly the validated root while containing every physical descendant. The non-identity logical-to-physical fixture must prove that P4 mask spans remain logical and that only the Action descendant set becomes Godot filter paths. Also assert every occurrence handle is nonnegative/unique, the Base/Turn/Rotate authority domain is shared, Montage and Sequence authority domains differ, animation-set/layout/binding/graph provenance stamps and exact Transition/Action definition/segment handles survive all three snapshot views, every definition receives the required handle/ordinal policy, and recompiling the Godot adapter cannot change any pure snapshot value. Pairing a library built from another definition/layout/binding/graph digest, wrong skeleton or wrong root mapping must fail before graph construction.

- [ ] **Step 2: Run RED binding smoke**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_runtime_binding_smoke.tscn
```

Expected: build/smoke fails because no P5A adapter or P5A library overload exists.

- [ ] **Step 3: Implement initialization-only Import-to-Core flattening**

```csharp
internal sealed class AlsP5aAnimationRuntimeBinding
{
    public static AlsP5aAnimationRuntimeBinding Compile(
        AlsP5CoreRuntimeBindingSnapshot coreBindings,
        AlsAnimationLibraryBuildResult library);

    public AlsP5RuntimeBindings CreateCoreView();
    public AlsP5OccurrenceLayoutView CreateOccurrenceLayoutView();
    public AlsP5GraphBuildView CreateGraphBuildView();
}
```

The constructor does not receive or flatten raw P3/P4/P5 profiles a second time. It requires the library's immutable P5A build stamp `{ AnimationSetDefinitionDigest, LayoutDigest, BindingDigest, GraphDigest }` to equal the supplied snapshot in O(1), delegates all three views to that owned snapshot, and consumes the library's already-validated skeleton/root, Action filter paths and per-animation clip-name/play-length/normalized table while allocating only immutable Godot parameter names plus physical-slot/Sync membership descriptors. It does not create AnimationTree nodes or parameter paths; those cannot exist until Task 15. It never assigns/remaps occurrence or authority IDs, maps curve enums, recopies timeline/marker/action fields, guesses bone zero/name, or reconstructs P3/P4 topology or logical-to-physical bone paths. P4 logical masks remain owned by the existing `AlsComponentPoseModifier` initialization/pose transaction and are never duplicated as AnimationTree filters. Every fixed Base/Turn/Rotate physical bank, including Idle/non-member slots with no event definitions, resolves `GraphSlotIndex -> OccurrenceHandleId/AuthorityGroupId` from the occurrence view; configured Sync membership comes from the Core binding view; Transition, Action Montage and Sequence use their snapshot handles. The single Action graph lane parameter created later is a Godot handle, not a timeline occurrence handle. The frozen order is `snapshot + stamped library -> AlsP5aAnimationRuntimeBinding descriptors -> Task 15 graph build/parameter handles -> Task 16 controller`. Task 13 Oracle, Task 15 graph handles and Task 12 Core bindings therefore consume the same layout/binding/graph provenance. Validate same-animation reuse without double publication, skeleton identity, Godot animation closure/resource presence and that every snapshot animation ID resolves exactly once. Import retains `sourceClassPath`/`displayName` only for initialization audit; no JSON, object path, event name, marker name or callback enters the hot-path view.

- [ ] **Step 4: Add `BuildP5a` library closure**

Add `BuildP5a(AlsAnimationSetDefinition animationSet, AlsP5CoreRuntimeBindingSnapshot coreBindings)`. It first requires `animationSet.DefinitionDigest == coreBindings.AnimationSetDefinitionDigest`, then uses `CreateGraphBuildView()` for the exact precompiled P3 `AllAnimationIds`/Standing/Crouching/Jump/Fall/Land/Lean closure, all P4 Aim/Turn/Rotate/additive-base IDs and normalized-track subset; it unions only the snapshot Core view's Transition/additive-base, Sync and Action segment Sequence IDs. It never infers graph-only clips from occurrence layout. For every unique closure ID, publish one immutable resource descriptor `{ AnimationId, owned StringName ClipName, float PlayLengthSeconds, byte NormalizedTrack }`; `PlayLengthSeconds` is copied from the canonical definition and must be finite/positive and match the loaded Godot `Animation.Length` under the existing import tolerance, while `NormalizedTrack` must exactly match graph-view membership. Using graph-view `SkeletonId`, `MannequinMeshId` and both root IDs, validate the canonical skeleton's `RequiredBones.Root`, logical-to-physical mapping, target `Skeleton3D` identity/rest mapping and root ancestry. Validate every GraphBuildView logical mask header/range/member against the canonical skeleton for provenance, but do not convert or publish those masks as AnimationTree filter paths: the unchanged `AlsComponentPoseModifier` remains their sole runtime consumer in the P4 pose transaction. Independently precompute the only new graph filter, the immutable Action paths for every target physical descendant below the root-motion extraction bone while excluding the root itself; the non-identity mapping fixture must produce this expected physical descendant set. Wrong skeleton/root, invalid mask range/member, missing/extra descendant or normalized-ID mismatch fails before publication. Write the exact immutable `{ AnimationSetDefinitionDigest, LayoutDigest, BindingDigest, GraphDigest }`, physical root ID/path, Action filter paths and resource descriptors into `AlsAnimationLibraryBuildResult`; do not reread P3/P4/P5 profiles or reproduce their sorting. Montage metadata itself is not an FBX clip; the Action lane plays its resolved segment Sequence while Core owns Montage time/section semantics.

- [ ] **Step 5: Generate UIDs and run GREEN smoke**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_runtime_binding_smoke.tscn
```

Expected: both `.cs.uid` files exist and output is exactly `P5A_RUNTIME_BINDING_OK event_semantics=6 curve_semantics=1 groups=1 members=17 transitions=4 actions=1 segments=1 occurrence_handles_valid=1 authority_global=1 action_domains=2 layout_digest=1 graph_digest=1 root_filter=1`; the locked Roll Montage currently contains exactly one segment.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Godot/Animation/AlsP5aAnimationRuntimeBinding.cs src/Als.Godot/Animation/AlsP5aAnimationRuntimeBinding.cs.uid src/Als.Godot/Animation/AlsAnimationLibraryBuilder.cs src/Als.Godot/Animation/P5aRuntimeBindingSmoke.cs src/Als.Godot/Animation/P5aRuntimeBindingSmoke.cs.uid scenes/tests/p5a_runtime_binding_smoke.tscn
git commit -m "feat(godot): bind compiled p5a animation runtime"
```

### Task 15: Add Independent Transition and Action Graph Lanes

**Files:**
- Modify: `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs`
- Create: `src/Als.Godot/Animation/P5aAnimationGraphSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Animation/P5aAnimationGraphSmoke.cs.uid`
- Create: `scenes/tests/p5a_animation_graph_smoke.tscn`

- [ ] **Step 1: Add a failing graph topology and real-clip smoke**

Assert the legacy `BuildLayeredBranch` P4 path remains byte-identical, the new P5-specific branch consumes the stamped GraphBuildView and has no branch-wide timing node above mapped locomotion, every base point has an independent seek/rate handle, mapped follower times alter the actual sampled pose without double scaling even when the legacy base rate/seek inputs are non-unit, all P3 Lean/other graph-only clips and P4 Down/Forward/Up Aim plus additive base remain present/functional with the exact normalized subset, and non-member/idle preserve legacy timing. The reconstructed P4 graph must still contain zero bone filters; P4 logical masks remain applied once by the later component-pose stage. Dynamic Transition is additive; Action affects every physical descendant below the validated root while excluding exactly that root; each P5 lane has its own clip bank/seek/weight handles; the final topology is deterministic; both real Transition clips and Roll Sequence affect pose; the Roll root bone stays on the base pose; and activating one lane never mutates the other's parameters.

- [ ] **Step 2: Run RED graph smoke**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_graph_smoke.tscn
```

Expected: FAIL because the graph only exposes P4 action-mode blending and has no P5 lanes.

- [ ] **Step 3: Build the fixed graph topology once**

Append nodes in this order:

```text
P4 output
  -> [TransitionBank0, TransitionBank1]
  -> AnimationNodeBlend2 P5DynamicTransitionSourceMix (IncomingMix)
  -> AnimationNodeAdd2 P5DynamicTransitionAdd (LaneWeight)
  -> [ActionBank0, ActionBank1]
  -> AnimationNodeBlend2 P5ActionSourceMix (IncomingMix)
  -> AnimationNodeBlend2 P5ActionLaneBlend (LaneWeight)
  -> Output
```

Add the exact assembly-internal P5-specific entry point `internal static AlsLocomotionGraphBuildResult BuildP5a(AlsAnimationLibraryBuildResult library, AlsP5aAnimationRuntimeBinding binding)` on the existing public builder; keeping the method internal avoids exposing its internal binding parameter and CS0051, while production/smokes remain in the same assembly. It validates the four-part stamp once, then obtains GraphBuildView, OccurrenceLayoutView and Core Sync membership from that one binding; do not pass raw P3/P4 profiles, skeleton definitions or independently compiled tables, and do not modify the legacy P4 `BuildLayeredBranch` topology/parameter behavior. Recreate the existing P4 locomotion, Lean, Aim, Turn and Rotate graph topology from the graph view and obtain every duration/clip name from the library's immutable resource descriptors before appending P5 lanes. GraphBuildView's logical P4 mask spans participate in the validated `GraphDigest` but create no graph node/filter; only the library's prevalidated Action descendant paths become a bone filter. No Task 15 code may map a logical bone ID or derive a NodePath. Reject any stamp/root/Action-filter, resource-duration/normalized, layout-slot or Sync-member mismatch before publishing nodes. In the P5 overload, remove the old branch-wide `AnimationNodeTimeScale -> AnimationNodeTimeSeek` ownership above `Locomotion + Lean`. Wrap every base BlendSpace point, including non-member/idle, as its own `AnimationNodeAnimation -> AnimationNodeTimeScale -> AnimationNodeTimeSeek` subtree, and give Lean a separate equivalent scale/seek subtree before layering. Configured Sync-member nodes use the real clip timeline (`UseCustomTimeline=false`, `StretchTimeScale=false`) so mapped seconds are not distorted by one-second normalization; set their BlendSpaces to `SyncModeEnum.None`, because Godot's length-only `CyclicMutable` normalization must not run simultaneously. Non-member/idle and Lean handles receive the same legacy base rate/seek decision, while an active Sync occurrence receives only its Core mapping. There is no outer scale or seek to apply a second time. Tests mutate a layout handle/slot, resource duration, Action filter path/order, GraphBuildView mask value/digest and Sync membership under stale/correct digest combinations and require deterministic rejection rather than a silently different graph mapping; a correct current mask digest must not add a P4 graph filter.

Return a frozen `AlsP5SyncGraphHandle[]` keyed by occurrence handle, animation ID and seek/rate paths for every configured member across current and outgoing locomotion branches. Tests set the former outer/base inputs to deliberately non-unit values and require the real member pose to land at Core's mapped current time exactly; they also compare P5-disabled/non-member/idle and the untouched P4 builder against the prior pose/parameter digest. This topology, rather than a controller convention alone, is the invariant preventing double seek/rate application.

Each lane owns exactly two seekable banks. An integer binding selects which clip each bank contains; it never performs blending. Each Transition bank computes `Transition clip - exported additive base clip` through `AnimationNodeSub2`, then seeks the delta. `P5DynamicTransitionSourceMix` blends frozen outgoing bank 0 to incoming bank 1 using Core's `IncomingMix`, and `P5DynamicTransitionAdd` applies that mixed delta to P4 output using Core's `LaneWeight`. The validated additive base is `ALS_N_Pose` (`621a81bf492cb9120b45cfd91b685854afb7dc75`). The two Action banks hold outgoing/current resolved Montage segment Sequences; `P5ActionSourceMix` uses the same frozen inner-mix rule before `P5ActionLaneBlend` mixes against locomotion with `LaneWeight`. Enable the outer Action blend's bone filter using exactly the immutable paths precomputed by `BuildP5a`: every target physical descendant below the validated root-motion extraction bone and never the root transform itself. This produces an in-place full-body Roll without extracting or applying Root Motion. The resulting coefficients are exactly `LaneWeight*(1-IncomingMix)` and `LaneWeight*IncomingMix`; the two effective-weight fields are validation/Timeline values, not extra graph fades. The `P5` prefixes prevent collision with the existing P4 `ActionMode` / `ActionBlend` nodes. Sync subtrees, both banks, filter paths and all `StringName` handles are created during graph construction. Runtime never adds/removes nodes or resolves a node by authored asset name.

- [ ] **Step 4: Expose immutable P5 graph handles**

Add fixed paths for every Sync-member seek/rate, both Transition bank animations/phases/rates plus inner mix/outer add amount, and both Action bank animations/phases/rates plus inner mix/outer blend amount. Continue owning/disposal of every Godot resource through `AlsLocomotionGraphBuildResult`; no new global/static mutable Godot object is allowed.

- [ ] **Step 5: Generate UID and run GREEN graph/P4 regressions**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_graph_smoke.tscn
pwsh -NoProfile -File scripts/verify-p4-pose.ps1 -GodotExecutable $godotExe
```

Expected: `P5A_ANIMATION_GRAPH_OK sync_members=17 sync_pose=1 lanes=2 transition_additive=1 action_full_body=1`, followed by P4 pose success.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs src/Als.Godot/Animation/P5aAnimationGraphSmoke.cs src/Als.Godot/Animation/P5aAnimationGraphSmoke.cs.uid scenes/tests/p5a_animation_graph_smoke.tscn
git commit -m "feat(godot): add p5a transition and action graph lanes"
```

### Task 16: Extend the Animation Controller Prepared Transaction

**Files:**
- Create: `src/Als.Godot/Animation/AlsP5aAnimationInput.cs`
- Create (Godot-generated): `src/Als.Godot/Animation/AlsP5aAnimationInput.cs.uid`
- Modify: `src/Als.Godot/Animation/AlsLocomotionAnimationController.cs`
- Create: `src/Als.Godot/Animation/P5aAnimationControllerSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Animation/P5aAnimationControllerSmoke.cs.uid`
- Create: `scenes/tests/p5a_animation_controller_smoke.tscn`

- [ ] **Step 1: Add failing controller prepare/apply/rollback tests**

Cover pure base-decision preparation, current/outgoing Base/Turn/Rotate playback extraction with layout authority/activation flags and exact local windows, a non-loop Base clip clamping halfway through the frame before a later other-source event, same-animation branches with distinct occurrence handles/epochs, restart/backward-seek/inactive-reuse epoch increments, outgoing crossfade expiry closure, different mapped phases for three active Sync members, real per-member seek/rate writes with no outer timing multiplication, Base/Turn/Rotate curve contributor extraction, idle, Transition only, Action only, both active, accepted Action zero advance with nonzero fade-in weight, two-bank replacement and rapid replacement, partial-frame/tail-only fade, graph/timeline effective-weight equality, cross-segment final pose, changed-parameter cache, bank replacement, resolved segment phase, apply once, finalize once, stale/foreign token, discard before apply, rollback after apply including epoch/time/blend, legacy P4 overload equivalence and retry pose/parameter digest. Assert one successful frame increments `GraphAdvanceCount` by exactly one.

- [ ] **Step 2: Run RED controller smoke**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_controller_smoke.tscn
```

Expected: FAIL because prepared frames carry no P5 instructions.

- [ ] **Step 3: Add integer-only controller input**

```csharp
public readonly ref struct AlsP5aAnimationInput
{
    public readonly byte Enabled;
    public readonly ReadOnlySpan<AlsSyncMappedPlayback> SyncMappings;
    public readonly AlsLaneGraphInstruction TransitionLane;
    public readonly AlsLaneGraphInstruction ActionLane;
    public readonly AlsFootCurveSample FootCurves;
    public readonly float AllowTransitions;

    public AlsP5aAnimationInput(
        ReadOnlySpan<AlsSyncMappedPlayback> syncMappings,
        in AlsLaneGraphInstruction transitionLane,
        in AlsLaneGraphInstruction actionLane,
        in AlsFootCurveSample footCurves,
        float allowTransitions)
    {
        Enabled = 1;
        SyncMappings = syncMappings;
        TransitionLane = transitionLane;
        ActionLane = actionLane;
        FootCurves = footCurves;
        AllowTransitions = allowTransitions;
    }

    public static AlsP5aAnimationInput Disabled => default;
}

public readonly record struct AlsPreparedBaseBlendDecision(
    int AnimationIdA,
    int AnimationIdB,
    int AnimationIdC,
    float WeightA,
    float WeightB,
    float WeightC,
    float PhaseNormalized);

public readonly record struct AlsPreparedP4BankDecision(
    int AnimationIdA,
    int AnimationIdB,
    float PlayRateA,
    float PlayRateB,
    float PhaseA,
    float PhaseB,
    float BlendAmount);

public readonly record struct AlsPreparedAimDecision(
    float DownPhase,
    float ForwardPhase,
    float UpPhase,
    float DownWeight,
    float ForwardWeight,
    float UpWeight);

public readonly record struct AlsPreparedBaseDecision(
    long OwnerId,
    long Revision,
    double DeltaTimeSeconds,
    AlsAnimationState AnimationState,
    AlsStance Stance,
    AlsPreparedBaseBlendDecision CurrentBase,
    AlsPreparedBaseBlendDecision PreviousBase,
    float BaseTransitionAlpha,
    AlsPreparedP4BankDecision Turn,
    AlsPreparedP4BankDecision Rotate,
    AlsPreparedAimDecision Aim,
    float P4ActionModeBlendAmount,
    float P4ActionBlendAmount);

public readonly ref struct AlsPreparedBaseAnimationFrame
{
    public readonly AlsPreparedBaseDecision Decision;
    public readonly ReadOnlySpan<AlsBasePlaybackDescriptor> Playbacks;
    public readonly AlsP4CurveFrameInput FootCurveInput;

    public AlsPreparedBaseAnimationFrame(
        in AlsPreparedBaseDecision decision,
        ReadOnlySpan<AlsBasePlaybackDescriptor> playbacks,
        AlsP4CurveFrameInput footCurveInput)
    {
        Decision = decision;
        Playbacks = playbacks;
        FootCurveInput = footCurveInput;
    }
}
```

The four named prepared-decision records, `AlsLaneGraphInstruction` and the existing `AlsFootCurveSample` are public ordinary unmanaged scalar values with exactly the constructor fields above; only the two containing `readonly ref struct` values carry spans. `OwnerId + Revision` identifies the controller's latest tentative base scratch, `DeltaTimeSeconds` is the sole delta later used by the final graph advance, and the remaining fields are the complete cross-phase public playback/blend projection. Existing controller-private `PreparedApply`, transition-target and bank-request scratch stays private and is matched to that identity rather than leaking implementation handles into the public ABI. Contract-layout tests prove the four records contain no managed references and compile their exact public constructors. `Enabled` is canonical `0/1`: the active constructor always writes `1`, `Disabled` is the all-default `0` value, and any other value or nondefault field with `Enabled=0` is rejected. A valid P5 frame remains enabled even when mappings are empty, both lanes are inactive and all curve values are zero; mode is never inferred from payload contents. `FootCurves` contains Core's exact `LeftIk/RightIk/LeftLock/RightLock` result and `AllowTransitions` carries the fifth prepared curve scalar for token equality/diagnostics; the latter remains owned and consumed by the Core finalize path, not by AnimationTree. Those spans point to controller-owned preallocated arrays, are valid only for the immediate Worker call chain and can never be stored in a field, async state machine or `AlsRuntimeState`.

Split the controller into two pre-apply preparation phases. `PrepareBaseFrame(in result, in p4Input, deltaTime)` returns `AlsPreparedBaseAnimationFrame` containing the pending P3/P4 decision plus fixed-capacity current/outgoing descriptor spans for Base, Turn-bank and Rotate-bank contributors required by Task 12. It may overwrite only controller-owned tentative base scratch and advance a checked tentative `Revision`; it does not mutate committed/controller graph state or claim the final `_preparedTransactionState` slot. If it throws before returning a ticket, it clears its own partially written tentative scratch before rethrowing, so the caller performs no controller cleanup. Only the latest successfully returned exact `OwnerId + Revision` can be consumed once by `PrepareFrame`; `DiscardBaseFrame(in baseFrame)` invalidates that tentative scratch after a Core rejection/exception without changing an epoch, time or graph parameter, and a newer `PrepareBaseFrame` invalidates every older view. Every fixed graph occurrence/physical slot persists `{ OccurrenceHandleId, AnimationId, PlaybackEpoch, NextPlaybackEpoch, UnwrappedTime, Active }`; `NextPlaybackEpoch` is slot-local, checked and starts at `1`. Only that slot increments its own counter on first activation, animation change, explicit restart, state/stance reset, backward seek or reuse after inactive, so activity in another Base/Turn/Rotate slot cannot perturb its epoch. Action and Transition retain their separate Core-owned occurrence-local counters. An incoming physical bank always consumes its own new epoch even when it uses the same animation as the outgoing bank. The first/restarted/incoming descriptor sets `ActivatesAtFrameStart=1`; a continuing descriptor sets it to zero. Keep each outgoing occurrence and epoch until its weight reaches zero, then emit exactly one zero-contribution `ClosesAfterFrame=1` descriptor and remove it only when the whole Worker/controller transaction commits. The layout-supplied handle and P4 replacement authority ID are copied into every descriptor. Preparation, discard and rollback cannot consume a slot-local epoch, activation, time or close flag; retry reproduces identical descriptor bytes. Tests cover mid-base-prepare failure, foreign/stale/already-consumed base tickets, discard/retry and interleaved unrelated slot activations, requiring each target slot's epoch sequence to remain identical.

`PrepareBaseFrame` owns only that discardable tentative scratch and never touches `AnimationTree`; Worker converts its descriptors and P4 curve contributors to Core input. After Task 12 `TryPrepare`, Worker constructs the enabled P5 input from the Core transaction's Sync mappings, two lane instructions, four-value `AlsFootCurveSample` and `AllowTransitions`. `PrepareFrame(in baseFrame, in p5Input)` first resolves/validates every precompiled integer graph handle and calculates all base/seek/rate/bank/weight values into locals without touching the tree or final fields. Only after every throwing operation succeeds does one no-throw publication block copy the locals into the final prepared fields, set the one final prepared slot, invalidate the tentative ticket and return the fixed token. If the method throws before returning, its own catch clears both tentative and partial final scratch and guarantees `_preparedTransactionState` is empty; the Worker performs no `DiscardBaseFrame`, `DiscardPrepared` or rollback for that call. A successfully returned final token is the only proof that the caller owns the final cleanup obligation. It retains and validates both outgoing/incoming occurrence handles, binding indices, epochs, previous/current times, rates, lane weight, incoming mix, two exact effective weights and all five finite curve scalars; retry/replacement cannot be inferred from animation ID. Tests explicitly inject mid-`PrepareFrame` validation/construction failure and cover two simultaneous branches with the same animation ID, lane-tail expiry, rapid replacement, restart, a fully zero but enabled P5 input, disabled legacy mode, base-ticket discard, and rollback/retry so epoch reuse cannot duplicate an occurrence identity.

Define controller-owned `internal sealed class AlsAnimationPreparationException : InvalidOperationException` in `AlsLocomotionAnimationController.cs` with an internal readonly `AlsP5FailureCode Code`; its constructor accepts only `InvalidBinding`, `StalePreparedFrame` or `NonFiniteOutput`. New P5 graph/handle validation uses `InvalidBinding`, `ValidatePrepared`/`ValidateAppliedPrepared` stale/foreign/reused token guards use `StalePreparedFrame`, and non-finite post-apply graph/pose output uses `NonFiniteOutput`. Preserve the legacy P4 overload's existing `ArgumentException`/`ArgumentOutOfRangeException` parameter guards and byte-for-byte behavior. The controller smoke asserts both the exact new codes and the unchanged legacy exception types. Task 17, in the same assembly, catches the typed exception and copies `Code`; at the P5 controller call boundary it maps a legacy `ArgumentOutOfRangeException` to `NonFiniteInput` and a legacy `ArgumentException` to `InvalidBinding` by exception type plus call phase, never by `Exception.Message`.

Preserve the existing `PrepareFrame(in AlsFrameResult, in AlsP4AnimationInput, double)` overload. It delegates through `PrepareBaseFrame` and `AlsP5aAnimationInput.Disabled`, producing byte-for-byte P4 behavior so Task 16 and every later intermediate commit still build before Worker adopts the split API.

- [ ] **Step 4: Preserve one-advance and full rollback semantics**

`ApplyPrepared` writes only changed AnimationTree parameters, including each mapped member's previous local seek and mapped rate, both bank selectors/times/rates, and each lane's Core-supplied `LaneWeight` plus `IncomingMix`; it then calls `AnimationTree.Advance(deltaTime)` once. `OutgoingEffectiveWeight` and `IncomingEffectiveWeight` remain in the prepared token as Timeline/diagnostic values, are bit-compared to Core's frozen formula and are never resolved or written as graph parameters. A mapping must match `OccurrenceHandleId + AnimationId + PlaybackEpoch` and every active configured occurrence must have exactly one mapping; otherwise preparation fails before mutation. In the P5 topology the old outer locomotion rate is forced absent/neutral and never receives a seek; mapped member timing is exclusively inner-node timing, while Lean/non-member handles receive legacy timing. For the Action incoming source, seek the final contributing segment to its `PreviousClipTime` and use effective graph rate `PlayRate * ContributingDeltaSeconds / deltaTime` exactly once in this controller; Transition uses the same contribution scaling when it clamps before frame end. Zero delta holds the accepted initial pose, and post-advance validation must land at the incoming `CurrentClipTime` within `1e-5` including cross-segment frames. Outgoing visual sources have previous=current frozen time, zero contributing delta and zero rate. Tests assert the two graph coefficients imply the stored effective weights exactly and that no hidden fade exists.

`ApplyPrepared` owns every failure before its successful return. Its internal catch covers state-machine travel, each parameter/cache write, `AnimationTree.Advance`, pose validation and the final state transition; it restores the committed state-machine/bank selection, every changed parameter/cache value, elapsed/occurrence scratch, skeleton pose snapshot and `GraphAdvanceCount`, clears the final prepared slot, then rethrows. Thus a mid-write or post-advance exception leaves no caller-owned live token and the Worker must not call `RollbackPrepared` again. Only a successful return changes the caller phase to applied; a later P4 pose/Core-finalize/commit failure then uses `RollbackPrepared`. Inject failures after the first parameter write, immediately after `Advance` and during post-advance validation, and require byte-identical graph/cache/pose/counter restoration plus a same-identity retry.

`PrepareCommit` performs every controller-side applied-token/pose/value validation and returns a fixed commit token without mutating committed state. `TryFinalizePreparedCommit` is allocation-free and non-throwing: on `false` it changes no controller field and leaves the applied token valid for `RollbackPrepared`; on `true` it performs only prevalidated scalar/value assignments, persists base occurrence/epoch/time/closure state, Sync mapping and both P5 lanes, and consumes the token. It is called only as Task 17's commit gate immediately before the final no-throw publication tail. Tests pass stale/foreign/reused tokens and prove `false` plus successful rollback, then prove a valid finalize contains no Godot call, allocation, callback or throwing validation. `RollbackPrepared` is valid only after `ApplyPrepared` returned and before a successful finalize, and restores graph parameters, state-machine/bank selection, elapsed state, every slot-local epoch counter, occurrence states and pose snapshot without publishing counts. `DiscardPrepared` is valid only after `PrepareFrame` returned and before `ApplyPrepared` is called; it clears unapplied state. Calling either cleanup for a method that threw before returning is a stale-token error and is forbidden by the Worker phase machine. When `Enabled=1`, `SampleFootCurves` returns the prepared token's exact Core-supplied `AlsFootCurveSample` rather than sampling a second, unsynchronized phase, and Worker bit-compares the cached `AllowTransitions` with the Core transaction token before finalize. When `Enabled=0`, the unchanged legacy overload retains the existing P4 sampler. Do not call `AnimationPlayer.Advance` anywhere.

- [ ] **Step 5: Generate UIDs and run GREEN controller/P4 gates**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_controller_smoke.tscn
pwsh -NoProfile -File scripts/verify-p4-pose.ps1 -GodotExecutable $godotExe
```

Expected: `P5A_ANIMATION_CONTROLLER_OK base_split=1 sync_seek=1 lanes=2 advance=1 rollback=1 retry=1`, followed by P4 pose success.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Godot/Animation/AlsP5aAnimationInput.cs src/Als.Godot/Animation/AlsP5aAnimationInput.cs.uid src/Als.Godot/Animation/AlsLocomotionAnimationController.cs src/Als.Godot/Animation/P5aAnimationControllerSmoke.cs src/Als.Godot/Animation/P5aAnimationControllerSmoke.cs.uid scenes/tests/p5a_animation_controller_smoke.tscn
git commit -m "feat(godot): transact p5a animation controller lanes"
```

### Task 17: Integrate P5A Gather and the Atomic Worker Frame

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsP5aWorkerStorage.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aWorkerStorage.cs.uid`
- Create: `src/Als.Godot/Locomotion/AlsP5aFailureExchange.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aFailureExchange.cs.uid`
- Create: `src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs.uid`
- Create: `src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs.uid`
- Modify: `src/Als.Godot/Locomotion/AlsCharacterMotor.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3Character.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Create: `src/Als.Godot/Locomotion/P5aTransactionSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aTransactionSmoke.cs.uid`
- Create: `scenes/tests/p5a_transaction_smoke.tscn`

- [ ] **Step 1: Add a failing production-path transaction smoke**

Use a real P5A character slot and inject failures before any controller ticket, during base prepare, after Core prepare, during final controller prepare, during graph apply, after graph apply, after P4 pose apply, after Core finalize and after final publication preflight but before the commit gate. Assert input/request/slot-generation identity, phase-1 base occurrence/epoch handoff, one graph advance, one successful result publication, bit-preservation of every P4 candidate runtime field through P5 finalize, complete pose/visual/controller/committed-P5 state rollback, zero event leakage, same-identity retry digest, one deduplicated Release failure record drained through the production Main dispatcher with the prior result digest, no failure-only `AlsFrameResult`, unchanged committed state while the external recovery latch is armed, runtime-failure closure on the first newer successful identity, and an unchanged legacy missing-result counter/slot lifecycle both while its failure record is pending and on an acknowledged same-identity no-op retry. Also inject a stale/full result destination and stale controller commit token: both must reject during preflight or return `false` before controller finalization, leaving rollback possible. There is deliberately no “after controller finalize” fault hook because a successful commit gate is followed only by the branch-free no-throw publication tail specified below; a source-contract test rejects allocation, array copy, Godot call, delegate/virtual callback, throw statement or fallible helper in that tail.

- [ ] **Step 2: Run RED transaction smoke**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_transaction_smoke.tscn
```

Expected: FAIL because character slots have no P5 storage/request and Worker does not call the P5 transaction.

- [ ] **Step 3: Add per-slot preallocated storage and N+1 request publication**

`AlsP5aWorkerStorage` allocates two swappable committed/candidate banks for cursor, authority and ownership arrays, committed/candidate `nextOwnerToken` scalars, plus base-playback, Action traversal, Timeline playback/occurrence, curve and Sync-mapping scratch once during `Configure`, sized from the compiled profile and bounded by validated maximums. Commit swaps the prevalidated bank indices/references; it never copies an array in the final block. `nextOwnerToken` starts at `1`, advances only with a successful whole-frame commit and resets only on generation reuse after Main lifecycle closure. Gather copies one `AlsActionRequest` into the trailing `AlsFrameInput.ActionRequest`; callbacks/input can only enqueue a request for the next Gather frame and cannot reenter the current Worker frame. The request's `SlotGeneration` is copied from the current identity.

`AlsP5aFailureExchange` is a preallocated per-slot single-producer/single-consumer one-record mailbox for `AlsP5FailureRecord`. It uses sequence publication, never overwrites an unread record, and deduplicates repeated `(Identity, Code)` attempts. In addition to the consumable record, it retains one atomically published `LastAcknowledgedFailureIdentity` tombstone after order-3 acknowledgment; this is classification state, not a second diagnostic queue. The tombstone persists across exact-identity no-op retries and newer failed attempts, and clears only after order 2 fully commits a strictly newer successful result for the same character/generation or generation lifecycle cleanup completes. A slot cannot begin its next Worker frame until the prior frame's Commit/dispatcher phase has drained or acknowledged the mailbox, so one pending record plus one acknowledged identity is a proven bound rather than a lossy queue.

Add a separate per-slot `AlsP5aRuntimeFailureLatch` outside `_runtimeState` and committed P5 storage. It stores `FailedIdentity`, `Code`, `RecoveryRequired=1` and whether that identity's one diagnostic was published. After the mailbox is acknowledged, retrying the exact latest failed identity is a deterministic Worker no-op before Core/controller preparation: no second record, graph advance, result, event or outcome is published, and the prior committed result digest remains observable byte-for-byte. “Strictly newer” means same character and slot generation with a greater `FrameId`; the first such identity prepares with `CancelActionForRuntimeFailure=1`, and the latch clears only after that candidate fully commits. If that newer recovery attempt fails, failure publication atomically replaces `FailedIdentity/Code` with the current identity/code while keeping `RecoveryRequired=1`; order 3 advances the acknowledged tombstone to that same identity. Therefore retrying the newest failed recovery frame also short-circuits before Core. Generation retirement closes through Main lifecycle and clears the latch rather than carrying it into a reused slot. Rollback without a new failure record cannot clear or regress the latch. This side-channel is explicitly excluded from committed-result/state equality, preserving byte-exact rollback. Tests cover `F fail/ack -> G recovery attempt fail/ack -> retry G` with zero Core/graph work and no duplicate record.

Create the order-3 main-thread `AlsP5aCommitDispatcher` and bounded `AlsP5aFailureDiagnostics` in this task, with only the failure-mailbox drain implemented initially. Before the existing order-2 `AlsP3CommitStage` classifies a missing result, it queries both exchange states. An exact pending record for the expected character/slot/generation/frame marks `P5FailurePending`; an empty mailbox plus exact `LastAcknowledgedFailureIdentity` marks `P5FailureAcknowledgedNoOp`. Both branches skip missing-result diagnostics/counters, slot release/deactivation, visual/result mutation and normal result handling, but only the pending branch leaves a record for order 3 and only the acknowledged branch emits no second diagnostic. Stale/future/mismatched pending records or tombstones cannot mask ordinary classification. The order-3 dispatcher validates identity, character, generation and prior committed digest, records/acknowledges exactly once, atomically publishes the tombstone and never changes visual/result/ownership. After a strictly newer successful normal commit, order 2 clears that tombstone; failure/rollback cannot clear it. Tests prove four injection phases each produce one order-3 diagnostic, zero missing-result increments and no premature slot lifecycle transition; after acknowledgment, an exact-identity production retry takes `P5FailureAcknowledgedNoOp` with no result/record/diagnostic/missing increment; a newer failure retains or advances the protected tombstone; a newer successful commit clears it; and a genuinely missing frame with neither state follows the unchanged legacy path. The transaction smoke waits for this real order-3 consumer before retrying. Task 18 extends the same dispatcher with successful event candidates and lifecycle mirror behavior; it does not replace this path.

Freeze `AlsP5FailurePolicy` in the runtime context: Debug/headless production defaults to `FailFastAfterRecord`, Release defaults to `RecordAndContinue`, and only internal smoke/harness construction may override it. The transaction smoke runs both policies, proving the same rollback/record bytes before the Debug path throws and the Release path continues.

- [ ] **Step 4: Integrate the exact Worker order**

In `AlsP3WorkerRoot._PhysicsProcess`:

```text
copy P3/P4/P5 checkpoints
evaluate P3 locomotion plus view/Turn/Rotate into candidateRuntimeState/candidateResult
controller PrepareBaseFrame(candidateResult/P4 input) and expose active Base/Turn/Rotate playbacks
Core TryPrepare P5A from those playbacks and the pre-foot-placement candidateRuntimeState; promote N-1 Transition and map Sync
controller PrepareFrame(base decision + P5 graph instructions)
consume Core-prepared foot curves and evaluate P4 foot placement
controller ApplyPrepared exactly once
apply P4 component pose transaction
build current P4 physical target/lock probe from successful modifier output
Core TryFinalize P5A with the complete post-foot-placement P4 candidateRuntimeState, queue Transition for N+1, overlay only P5 state fields, and fill candidate result/storage
prepare controller commit, reject any candidate whose `P5FailureCode != None`, and derive/validate the exact result-exchange destination/publication ticket without mutating the exchange
call the non-throwing TryFinalize controller commit gate; on true enter the no-throw tail that swaps runtime/P5 banks, assigns candidate state/result and publishes the prevalidated result sequence last
```

Track controller ownership only from successful method returns, never by inspecting a half-written flag. If Core `TryPrepare` rejects/throws after `PrepareBaseFrame` returned but before `PrepareFrame` is called, the Worker calls `DiscardBaseFrame`. If `PrepareBaseFrame`, `PrepareFrame` or `ApplyPrepared` itself throws, that method has already cleared/restored its own partial phase and the Worker performs no controller cleanup. After `PrepareFrame` returns but before `ApplyPrepared` is invoked, a failure uses `DiscardPrepared`; after `ApplyPrepared` returns and until controller finalize succeeds, any failure uses `RollbackPrepared`. `TryFinalizePreparedCommit(false)` is in that last category. These paths are mutually exclusive and smoke injections cover each boundary, including mid-prepare, mid-parameter-write, post-advance validation and final preflight rejection. Before the commit gate, Task 17 performs the first production enforcement of the successful-envelope invariant by rejecting `P5FailureCode != None`; it also derives and validates the exact result-exchange slot/sequence ticket without mutating the exchange, computes every digest, finishes all Core/pose/controller commit tokens, and proves candidate/committed banks are swappable. Any failure here still rolls back and publishes only an `AlsP5FailureRecord`. Call valid non-throwing `TryFinalizePreparedCommit` as the gate; if it unexpectedly returns `false`, perform no other write and roll back normally. After it returns `true`, only non-throwing scalar/struct assignments, preallocated bank-index/reference swaps and the prevalidated SPSC sequence publication remain, with result publication last as the visibility boundary. No code after successful controller finalize may allocate, copy arrays, call Godot, invoke callbacks, validate, branch to a failure result or throw. Main cannot observe intermediate Worker fields before the final sequence release. On any pre-commit failure, also rollback the full local P4 pose/visual root when applicable, restore `_runtimeState`, `_result` and committed P5 storage, publish no candidate result/event/outcome, and publish exactly one `AlsP5FailureRecord` containing the failed identity, stable code and prior committed result digest. The current successful P4 modifier output supplies physical targets; candidate P4 runtime state supplies owned lock positions/relevance; prepared Core curves supply `AllowTransitions`. `TryFinalize` may only queue this evidence for N+1. No Worker world query or gameplay callback is allowed.

Freeze integration error mapping by typed exception/reason, never by message text. Core `AlsP5FailureCode` values pass through unchanged. Invalid/missing profile, graph handle, resource or controller binding maps to `InvalidBinding`; stale, foreign or reused prepared/controller tokens map to `StalePreparedFrame`; Timeline/traversal violations map to `InvalidTimeline` except explicit occurrence-buffer overflow, which remains `EventBufferOverflow`; Sync validation maps to `InvalidSyncGroup`; non-finite controller/pose/modifier output maps to `NonFiniteOutput`. Map P4 reasons exactly: `InvalidDeltaTime -> InvalidDeltaTime`, `NonFiniteInput -> NonFiniteInput`, `NonFiniteCurve -> NonFiniteOutput`, and `InvalidSettings/InvalidRotation/InvalidSelection/InvalidRuntimeState/PoseRestoreFailed -> InvalidBinding`. Unknown integration exceptions map conservatively to `InvalidBinding`. Unit/smoke injection covers every row and asserts the exact stable code.

In headless/debug gates, `EventBufferOverflow` and other P5 runtime failures publish the diagnostic record and fail immediately after rollback. In Release gameplay, retain the last valid visual/result/committed state byte-for-byte, publish the one bounded stable-code record and arm only the external next-newer-successful-identity recovery latch; retrying the same failed identity does not publish a second record or cancel early. Never publish a partial event list or a failure-only frame result.

- [ ] **Step 5: Extend lifecycle snapshot and equality/digest checks**

`CaptureFailureLifecycleSnapshot` and `RestoreFailureLifecycleSnapshot` include fixed committed P5 scalar state plus a verified storage digest; generation reuse receives clean default cursors/ownership/action/transition and a cleared external failure latch. Failure rollback assertions compare committed bytes before observing the separately armed latch. Debug failure diagnostics report stable codes, not exception message matching.

- [ ] **Step 6: Generate UIDs and run GREEN transaction plus P4 regression**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
& $godotExe --headless --path $repo res://scenes/tests/p5a_transaction_smoke.tscn
pwsh -NoProfile -File scripts/verify-p4.ps1 -GodotExecutable $godotExe -Focused
```

Expected: `P5A_TRANSACTION_OK worker=1 base_split=1 advance=1 publication=1 rollback=4 retry=1 failure_record=1 duplicate=0 recovery_latch=1`, then `P4_FOCUSED_VERIFICATION_OK`.

- [ ] **Step 7: Commit**

```powershell
git add src/Als.Godot/Locomotion/AlsP5aWorkerStorage.cs src/Als.Godot/Locomotion/AlsP5aWorkerStorage.cs.uid src/Als.Godot/Locomotion/AlsP5aFailureExchange.cs src/Als.Godot/Locomotion/AlsP5aFailureExchange.cs.uid src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs.uid src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs.uid src/Als.Godot/Locomotion/AlsCharacterMotor.cs src/Als.Godot/Locomotion/AlsP3Character.cs src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs src/Als.Godot/Locomotion/AlsP3CommitStage.cs src/Als.Godot/Locomotion/P5aTransactionSmoke.cs src/Als.Godot/Locomotion/P5aTransactionSmoke.cs.uid scenes/tests/p5a_transaction_smoke.tscn
git commit -m "feat(godot): integrate atomic p5a worker frames"
```

### Task 18: Dispatch Validated Events on Main and Close Lifecycle Ownership

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsP5aEventSink.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aEventSink.cs.uid`
- Create: `src/Als.Godot/Locomotion/AlsP5aCommittedStateMirror.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aCommittedStateMirror.cs.uid`
- Modify: `src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CommitStage.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3Character.cs`
- Modify: `src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs`
- Create: `src/Als.Godot/Locomotion/P5aLifecycleSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aLifecycleSmoke.cs.uid`
- Create: `scenes/tests/p5a_lifecycle_smoke.tscn`

- [ ] **Step 1: Add failing commit/lifecycle tests**

Cover valid current result, missing/stale/future/duplicate result, character mismatch, generation mismatch, stage-order mismatch, sink thread ID, `AnimationTime -> EventSequence` order across multi-cycle/different-clip crossings, action outcome order, Begin/End mirror, valid/deduplicated/stale-generation failure record, prior-result digest validation, explicit replacement, deactivate, retirement and generation reuse. Every invalid result must dispatch exactly zero events/outcomes and every failure record must leave visual/result/mirror ownership unchanged.

- [ ] **Step 2: Run RED lifecycle smoke**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_lifecycle_smoke.tscn
```

Expected: FAIL because the Task 17 dispatcher only drains failures; Commit has no successful-result sink/candidate path or committed-state mirror.

- [ ] **Step 3: Add the immutable main-only sink and mirror**

```csharp
public interface IAlsP5aEventSink
{
    void Dispatch(in AlsFrameIdentity identity, in AlsAnimationEvent animationEvent);
    void Dispatch(in AlsFrameIdentity identity, in AlsActionOutcome outcome);
}

internal sealed class AlsP5aCommittedStateMirror
{
    public void ApplyCommitted(in AlsFrameResult result);
    public void DispatchLifecycleEnd(
        IAlsP5aEventSink sink,
        AlsActionResultCode reason,
        in AlsFrameIdentity identity);
}
```

The mirror owns one `AlsCommittedNotifyStateBuffer` and the last committed active Action owner. It contains no reference to Worker storage. Extend the existing Task 17 `AlsP5aCommitDispatcher`, still one main-thread node at process-group order `3`, with a preallocated slot-sized successful-candidate array; all per-character Commit stages remain at order `2`. Its existing `IAlsP5aFailureSink`/`AlsP5aFailureDiagnostics` path remains the sole bounded failure consumer and exposes count/last digest to smokes; it is not a gameplay event. The default sinks record bounded diagnostics only; audio, Overlay props and Root Motion consumers remain absent.

- [ ] **Step 4: Dispatch only after every Commit validation succeeds**

As defense in depth after Task 17's publication preflight, and before committing any visual/result, `AlsP3CommitStage` independently validates exchange/result identity, frame, character, generation, command/motor/model/pose stage, foot probes, `P5FailureCode=None`, finite nonnegative frame-relative `AnimationTime`, contiguous `EventSequence == 0..Count-1`, nondecreasing `AnimationTime -> EventSequence`, and unique complete occurrence identities. Every P5 event requires a nonnegative compiled `OccurrenceHandleId`; a legacy P3 event may retain invalid/default P5 identity only when the P5 digest extension is absent. It then reserves the character/frame's unique slot in the dispatcher's preallocated candidate array; duplicate or capacity failure rejects before visual commit. After all checks/reservation succeed, it commits the visual/result and fills that reserved immutable candidate. At order `3`, `AlsP5aCommitDispatcher` stable-sorts candidates by `FrameId -> CharacterId` and preserves each already-validated event buffer order, which is exactly the frozen `AnimationTime -> EventSequence` order. For one candidate, dispatch and mirror-apply every event first in sequence order, then every outcome in buffer order; synthetic state closure therefore precedes its terminal Action outcome. A sink cannot enqueue into the current frame; its request reaches Gather N+1.

The same order-3 dispatcher drains each `AlsP5aFailureExchange` even when no result exists. It validates character/generation, requires the record's `LastCommittedResultDigest` to equal the slot's unchanged committed result, rejects duplicates, records it through `IAlsP5aFailureSink`, and acknowledges the mailbox. It does not apply a visual/result, dispatch an event/outcome or mutate the committed ownership mirror.

- [ ] **Step 5: Close ownership during deactivate and generation retirement**

When no Worker recovery frame can run, enqueue lifecycle closure through the same dispatcher. It dispatches stable synthetic Ends followed by `InterruptedByLifecycle` or `InterruptedByGeneration`, clears the mirror, acknowledges closure, then allows slot recycling. Do not fabricate a Worker `AlsActionRequest`, inspect Worker arrays or clear ownership written by a newer epoch.

- [ ] **Step 6: Generate UIDs and run GREEN lifecycle tests**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
& $godotExe --headless --path $repo res://scenes/tests/p5a_lifecycle_smoke.tscn
```

Expected: `P5A_LIFECYCLE_OK valid=1 rejected=6 sink_main=1 failure=1 failure_stale=1 deactivate=1 generation=1 leaked=0`.

- [ ] **Step 7: Commit**

```powershell
git add src/Als.Godot/Locomotion/AlsP5aEventSink.cs src/Als.Godot/Locomotion/AlsP5aEventSink.cs.uid src/Als.Godot/Locomotion/AlsP5aCommittedStateMirror.cs src/Als.Godot/Locomotion/AlsP5aCommittedStateMirror.cs.uid src/Als.Godot/Locomotion/AlsP5aCommitDispatcher.cs src/Als.Godot/Locomotion/AlsP5aFailureDiagnostics.cs src/Als.Godot/Locomotion/AlsP3CommitStage.cs src/Als.Godot/Locomotion/AlsP3RuntimeContext.cs src/Als.Godot/Locomotion/AlsP3Character.cs src/Als.Godot/Locomotion/AlsP3CharacterSlot.cs src/Als.Godot/Locomotion/P5aLifecycleSmoke.cs src/Als.Godot/Locomotion/P5aLifecycleSmoke.cs.uid scenes/tests/p5a_lifecycle_smoke.tscn
git commit -m "feat(godot): commit p5a events and lifecycle ownership"
```

### Task 19: Add Production Timeline, Sync and Action Semantic Smokes

**Files:**
- Create: `src/Als.Godot/Locomotion/P5aTimelineSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aTimelineSmoke.cs.uid`
- Create: `src/Als.Godot/Locomotion/P5aSyncActionSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aSyncActionSmoke.cs.uid`
- Create: `scenes/tests/p5a_timeline_smoke.tscn`
- Create: `scenes/tests/p5a_sync_action_smoke.tscn`
- Modify: `src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs`

- [ ] **Step 1: Add failing end-to-end semantic smokes**

The timeline scene covers real and synthetic instant/state events, split window, loop/multi-cycle/large delta, simultaneous same-animation occurrence handles/epochs, crossfade closure, cross-playback `EventSequence`, authority handoff, threshold consumption, section jump/multi-slice ordering, same-frame Montage + Sequence events, Sequence-only segment handoff and terminal dual-domain closure, monotonic request replay tombstones, replacement, explicit cancel, EarlyBlendOut, overflow, one Release failure record, same-identity retry and first-newer-identity recovery. The sync/action scene covers real grounded markers, follower mapped local time and actual graph pose with no outer double timing, Transition queue at N/promotion at N+1 plus selection blocked at N+1/N+2 and eligible at N+3, accepted Roll initial pose with zero advancement, resolved Roll segment Sequence/phase, a synthetic cross-segment final pose, independent Transition + Roll, section completion, two-outcome replacement, recovery-cancel-before-new-Start with two ordered outcomes and zero marker callbacks.

- [ ] **Step 2: Run RED semantic scenes**

Run:

```powershell
dotnet build GodotALS.csproj -c Debug
& $godotExe --headless --path $repo res://scenes/tests/p5a_timeline_smoke.tscn
& $godotExe --headless --path $repo res://scenes/tests/p5a_sync_action_smoke.tscn
```

Expected: FAIL because the smoke drivers/scenes are absent.

- [ ] **Step 3: Drive only the production character path**

The scenes configure real `AlsP3Character` slots and enqueue deterministic Gather inputs; they do not call Core models directly. Capture Worker/result/sink/failure diagnostics and compare them to the P5A golden ordering. Overflow injects 17 valid occurrences and proves the prior visual/state/digest survives with zero partial delivery, exactly one validated failure record and no failure-only result. Sync must inspect graph seek/rate parameters and a pose digest at the mapped follower time; reporting only a computed phase is insufficient.

- [ ] **Step 4: Extend existing frame-order audit**

`P3bFrameOrderSmoke` additionally records P5 prepare, animation advance, P4 pose, P5 finalize, result publication and main dispatch stages. Require one strictly ordered sequence per successful frame and no P5 stage on a rejected/stale result.

- [ ] **Step 5: Generate UIDs and run GREEN semantic/P3 order gates**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
& $godotExe --headless --path $repo res://scenes/tests/p5a_timeline_smoke.tscn
& $godotExe --headless --path $repo res://scenes/tests/p5a_sync_action_smoke.tscn
pwsh -NoProfile -File scripts/verify-p3b.ps1 -GodotExecutable $godotExe -SkipRegression
```

Expected markers:

```text
P5A_TIMELINE_OK interval=1 loop=1 authority=1 states=1 overflow=1 failure=1 retry=1
P5A_SYNC_ACTION_OK sync=1 followers=1 sync_pose=1 no_double_time=1 transition_queue=1 transition_cooldown=2 roll_initial=1 roll_segment=1 segment_cross=1 replacement=2 marker_events=0
P3B_FOCUSED_VERIFICATION_OK regression=skipped
```

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Godot/Locomotion/P5aTimelineSmoke.cs src/Als.Godot/Locomotion/P5aTimelineSmoke.cs.uid src/Als.Godot/Locomotion/P5aSyncActionSmoke.cs src/Als.Godot/Locomotion/P5aSyncActionSmoke.cs.uid scenes/tests/p5a_timeline_smoke.tscn scenes/tests/p5a_sync_action_smoke.tscn src/Als.Godot/Locomotion/P3bFrameOrderSmoke.cs
git commit -m "test(godot): cover p5a production event semantics"
```

### Task 20: Certify the 1/10 Single/Parallel Runtime Matrix

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsP5aHarnessContext.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/AlsP5aHarnessContext.cs.uid`
- Create: `src/Als.Godot/Locomotion/P5aAnimationHarness.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aAnimationHarness.cs.uid`
- Create: `scenes/tests/p5a_animation_harness.tscn`
- Create: `scripts/p5a-verification-functions.ps1`
- Create: `scripts/verify-p5a-matrix.ps1`
- Create: `tests/VerifyP5A.Tests.ps1`
- Modify: `tests/VerificationScripts.Tests.ps1`

- [ ] **Step 1: Add failing parser, threshold and matrix tests**

Pester must reject missing/duplicate marker, malformed key/value, wrong character/mode, nonzero integrity count, Sync mapping/pose mismatch, Transition same-frame activation instead of N+1 promotion, unresolved Action segment, unexpected/duplicate failure record, main-thread parallel Worker, off-main sink, advance/publication count mismatch, nonzero warm allocation, digest mismatch and time above the existing P4 per-cell budget. It must accept only four cells and two exact pairs.

- [ ] **Step 2: Run RED Pester tests**

Run:

```powershell
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path @("tests/VerifyP5A.Tests.ps1","tests/VerificationScripts.Tests.ps1"); if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
```

Expected: FAIL because P5A harness/parser/scripts do not exist.

- [ ] **Step 3: Implement the production harness and bounded diagnostics**

Use the same deterministic 120-frame warm-up and 600 measured frames as P4. Exercise locomotion, marker-mapped graph poses, queued/promoted dynamic transitions, resolved Roll segment accept/complete/replacement, Notify State, one `RecordAndContinue` recoverable failure during warm-up and one lifecycle recycle. Hash complete frame/event/sync/action/failure output. Reset allocation/timing counters only after the failure mailbox is drained and warm-up finishes, then measure current-thread P5 Core/controller/Worker and main Commit/sink allocation. Parallel mode must observe at least one Worker thread ID different from main; event and failure sinks must always equal main.

- [ ] **Step 4: Run the fixed four-cell matrix**

`verify-p5a-matrix.ps1` runs, in this order:

```powershell
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_harness.tscn -- --mode=single --characters=1 --warmup=120 --frames=600
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_harness.tscn -- --mode=parallel --characters=1 --warmup=120 --frames=600
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_harness.tscn -- --mode=single --characters=10 --warmup=120 --frames=600
& $godotExe --headless --path $repo res://scenes/tests/p5a_animation_harness.tscn -- --mode=parallel --characters=10 --warmup=120 --frames=600
```

For each character count, single/parallel result digest, event count/order, Sync mappings/pose digest, Transition queue/promotion, Action segment/outcomes and failure-record digest must match exactly. Require zero duplicate/missing/reordered/stale dispatches, one advance/publication per successful frame, no result publication for the injected failure, existing P4 timing ceilings and `0 B` steady-state managed allocation.

- [ ] **Step 5: Generate UIDs and run GREEN matrix**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path @("tests/VerifyP5A.Tests.ps1","tests/VerificationScripts.Tests.ps1"); if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
pwsh -NoProfile -File scripts/verify-p5a-matrix.ps1 -GodotExecutable $godotExe
```

Expected: Pester PASS and `P5A_MATRIX_VERIFICATION_OK cells=4 pairs=2`.

- [ ] **Step 6: Commit**

```powershell
git add src/Als.Godot/Locomotion/AlsP5aHarnessContext.cs src/Als.Godot/Locomotion/AlsP5aHarnessContext.cs.uid src/Als.Godot/Locomotion/P5aAnimationHarness.cs src/Als.Godot/Locomotion/P5aAnimationHarness.cs.uid scenes/tests/p5a_animation_harness.tscn scripts/p5a-verification-functions.ps1 scripts/verify-p5a-matrix.ps1 tests/VerifyP5A.Tests.ps1 tests/VerificationScripts.Tests.ps1
git commit -m "test(godot): certify p5a single parallel matrix"
```

### Task 21: Add the Playable P5A Transition and Roll Demo

**Files:**
- Create: `scenes/demo/p5a_locomotion_demo.tscn`
- Create: `src/Als.Godot/Locomotion/P5aLocomotionDemo.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aLocomotionDemo.cs.uid`
- Create: `src/Als.Godot/Locomotion/P5aDemoSmoke.cs`
- Create (Godot-generated): `src/Als.Godot/Locomotion/P5aDemoSmoke.cs.uid`
- Create: `scenes/tests/p5a_demo_smoke.tscn`
- Modify: `src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs`
- Modify: `src/Als.Godot/Locomotion/AlsLocomotionHud.cs`
- Modify: `project.godot`
- Create: `scripts/verify-p5a-demo.ps1`
- Create: `docs/verification/p5a-demo-manual.md`

- [ ] **Step 1: Add a failing input/demo certificate**

Keep the accepted P3/P4 input smoke and its exact 11-item `ControlledActions` marker unchanged. Add a separate P5A input assertion in `P5aDemoSmoke` for a project-level `roll` action bound to physical `R` (`82`), total map size `12`, and deterministic N+1 request ID behavior. Add `RollPressed` as a trailing `init` property on `AlsPlayerInputSnapshot`, preserving its existing positional constructor and all P3 smoke call sites. Demo smoke must preserve WASD/camera/aim/sprint/crouch/jump, create a real foot-lock error at N, observe its Dynamic Transition promotion/pose at N+1, accept and complete the real resolved Roll segment, observe authored events, and prove character translation from Root Motion is zero.

- [ ] **Step 2: Run RED demo gates**

Run:

```powershell
pwsh -NoProfile -File scripts/verify-p5a-demo.ps1 -GodotExecutable $godotExe
```

Expected: FAIL because P5A demo scene, roll mapping and certificate do not exist.

- [ ] **Step 3: Build on the accepted P4 camera/input scene**

Create a new P5A scene that inherits/instances the P4 demo production rig; do not replace or rename the P4 regression scene. `AlsPlayerInputAdapter` keeps a monotonic next request ID and exposes one bounded `TryDequeueActionRequest(slotGeneration, out request)` value handoff. It enqueues exactly one Roll Start on just-press, Gather stamps the current slot generation, and neither path writes Worker state. `verify-p5a-demo.ps1` first runs the unchanged P3 input scene and validates its 11-action marker, then runs the P5A input/demo scene and validates the separate 12-action marker. Dynamic Transition is triggered by actual P4 target-to-lock error in the scene, not a direct controller call.

- [ ] **Step 4: Add restrained runtime diagnostics**

The HUD must expose the minimum bounded evidence needed by the interactive checklist: current sync pair/phase; Dynamic Transition queued/promoted frame IDs, side and resolved clip; the last eight Action outcomes in request order; authored-event count; and finite Roll start/end world positions plus their translation delta. These are read-only Main-thread diagnostics, not gameplay consumers. The HUD must not contain tutorial copy or keyboard-shortcut instructions, and it must not instantiate audio, Overlay prop gameplay or Root Motion translation consumers.

- [ ] **Step 5: Set the P5A scene as the runnable project and run GREEN verification**

Run:

```powershell
& $godotExe --editor --headless --path $repo --quit-after 2
pwsh -NoProfile -File scripts/verify-p5a-demo.ps1 -GodotExecutable $godotExe
```

Expected markers:

```text
GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1
P5A_DEMO_INPUT_OK actions=12 roll=1 request_ids=1
P5A_DEMO_OK frames=300 roll=1 roll_segment=1 transition=1 transition_n1=1 authored_events=1 root_motion=0
P5A_DEMO_VERIFICATION_OK frames=300 rigs=1
```

- [ ] **Step 6: Commit the automated-GREEN Demo code**

Commit only the already GREEN executable/demo code; the manual evidence file must not exist yet:

```powershell
git add scenes/demo/p5a_locomotion_demo.tscn src/Als.Godot/Locomotion/P5aLocomotionDemo.cs src/Als.Godot/Locomotion/P5aLocomotionDemo.cs.uid src/Als.Godot/Locomotion/P5aDemoSmoke.cs src/Als.Godot/Locomotion/P5aDemoSmoke.cs.uid scenes/tests/p5a_demo_smoke.tscn src/Als.Godot/Locomotion/AlsPlayerInputAdapter.cs src/Als.Godot/Locomotion/AlsLocomotionHud.cs project.godot scripts/verify-p5a-demo.ps1
git commit -m "feat(demo): add playable p5a transition and roll preview"
```

- [ ] **Step 7: Run and record the interactive Demo acceptance**

Require a clean worktree, bind the acceptance to the immutable Demo commit, then launch the actual project window rather than a headless smoke:

```powershell
if (git status --porcelain) { throw 'Manual acceptance requires a clean committed Demo tree.' }
$testedDemoCommit = git rev-parse HEAD
& $godotExe --path $repo res://scenes/demo/p5a_locomotion_demo.tscn
if ((git rev-parse HEAD) -ne $testedDemoCommit -or (git status --porcelain)) { throw 'Demo code or HEAD changed during manual acceptance.' }
```

Do not continue until a human has performed and observed every item below in that window:

1. Rotate the camera at least 90 degrees in yaw and exercise pitch; `W/S` still move along/opposite the camera's horizontal forward and `A/D` remain camera-relative lateral movement, with no reversed axis or camera snap.
2. Exercise walk/run, aim, sprint, crouch and jump; the accepted P4 camera/input behavior and foot placement remain intact.
3. From locomotion, release and sharply reverse direction until the real target-to-lock error queues a Dynamic Transition; observe promotion on N+1, the expected left/right clip in the HUD and no pose pop, double transition or stuck lane.
4. Return to rest, record the character world position, press physical `R` once and wait for completion; observe one accepted Roll `Default` segment, authored event activity, `Completed` outcome and no locomotion freeze. The demo's measured start/end Root Motion translation delta must be finite and `<= 0.0001 m`.
5. Repeat Roll three times, including one replacement request while active; outcomes remain ordered, the Action lane clears, camera/input remain responsive and no prop/audio/Overlay gameplay appears.

Create `docs/verification/p5a-demo-manual.md` using this exact ordered, line-oriented schema; replace every angle-bracket placeholder with the value observed in the HUD:

```text
Date: YYYY-MM-DD
OS: <non-empty single-line OS description>
Godot-Version: <non-empty exact version>
Scene: res://scenes/demo/p5a_locomotion_demo.tscn
Tested-Demo-Commit: <40-lowercase-hex>
Item-1-Camera-Input: PASS
Item-2-Locomotion: PASS
Item-3-Transition: PASS
Item-4-Roll: PASS
Item-5-Repetition-Replacement: PASS
Transition-Queued-Frame: <nonnegative decimal frame ID>
Transition-Promoted-Frame: <queued frame ID plus one>
Transition-Side: <Left or Right>
Transition-Clip: <non-empty resolved clip ID/name>
Roll-Outcomes: <id1>:Accepted>Completed;<id2>:Accepted>Completed;<id3>:Accepted>InterruptedByReplacement;<id4>:Accepted>Completed
Authored-Event-Count: <positive decimal integer>
Root-Motion-Delta-Meters: <finite invariant-culture decimal in [0,0.0001]>
Notes: <non-empty single line; use none when there are no notes>
P5A_MANUAL_DEMO_OK camera_input=1 locomotion=1 transition=1 roll=1 authored_events=1 root_motion=0
```

Every label and the final marker must occur exactly once; the marker must be the final non-empty line and no unknown label is allowed. Validate `Date` as an actual ISO calendar date; trim and reject empty/placeholder OS, Godot version, Transition clip and Notes; require the exact scene and `$testedDemoCommit`; require all five items to be `PASS`; require promoted frame = queued frame + 1 without overflow; require a declared side; parse exactly four positive, strictly increasing request IDs with the shown result sequence; require positive authored-event count; and parse the Root Motion delta invariantly as finite and within the closed threshold. A FAIL, malformed field or unperformed item blocks Task 21. Any later change to a Task 21 code path invalidates the evidence and requires a new Demo code commit plus rerunning Steps 5-7. Automated markers cannot create or satisfy this record. Task 22 may validate and cite the checked-in record but must not claim that its headless certificate reran the human interaction.

- [ ] **Step 8: Commit only the manual evidence**

```powershell
git add docs/verification/p5a-demo-manual.md
if ((git diff --cached --name-only) -ne 'docs/verification/p5a-demo-manual.md') { throw 'Manual evidence commit contains unexpected files.' }
git commit -m "docs: record p5a manual demo acceptance"
```

### Task 22: Close Unified Verification, Documentation and Repository Evidence

**Files:**
- Create: `scripts/verify-p5a.ps1`
- Modify: `scripts/p5a-verification-functions.ps1`
- Modify: `tests/VerifyP5A.Tests.ps1`
- Modify: `tests/VerificationScripts.Tests.ps1`
- Create: `docs/architecture/p5a-event-action-runtime.md`
- Modify: `README.md`

- [ ] **Step 1: Add failing top-level verification tests**

Require exact child marker validation, including the reachable committed-fixture marker `P5A_GOLDEN_FIXTURE_OK cases=8 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`, timeout/exit handling, rejected warnings/errors, schema/lock check, focused Export/Import/Core/golden tests, both semantic/lifecycle scenes, matrix, demo, the checked-in manual-evidence schema/provenance/unique marker, repository Pester, P0-P4 regression, Debug/Release tests, clean worktree and one top-level success marker. The verifier validates but does not generate or rewrite manual evidence. It parses the exact full `Tested-Demo-Commit`, requires it to resolve as a commit and be an ancestor of current `HEAD`, finds exactly one commit that first added `docs/verification/p5a-demo-manual.md`, requires that evidence commit's sole parent to equal the tested Demo commit, requires `git diff --exit-code <evidence-commit> HEAD -- docs/verification/p5a-demo-manual.md` to be empty, and requires `git diff --exit-code <tested-demo-commit> HEAD -- <all Task 21 code paths except the evidence file>` to be empty. Add negative fixtures proving a child cannot spoof `P5A_VERIFICATION_OK`, a zero-test Pester run cannot pass, and every manual-schema rule from Task 21 rejects missing, duplicate, unknown, malformed, placeholder, `FAIL`, bad-sequence, bad-number, non-finite or out-of-range evidence. Also reject nonexistent/non-commit/non-ancestor tested identity, wrong evidence parent, post-commit evidence-blob drift, drift in every Task 21 code-path family, and a committed-fixture child with a missing/duplicate marker or wrong case count/commit.

- [ ] **Step 2: Run RED verifier tests**

Run:

```powershell
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path @("tests/VerifyP5A.Tests.ps1","tests/VerificationScripts.Tests.ps1"); if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
```

Expected: FAIL because `verify-p5a.ps1` and closure rules do not exist.

- [ ] **Step 3: Implement the single top-level certificate**

Expose mandatory `-GodotExecutable`, `-EngineRoot` and `-UnrealProject` parameters and pass the Unreal paths through to the exporter build/source gate. Resolve all three paths up front and fail before running children when any is invalid; do not hard-code external tool/project paths. The top-level certificate validates the committed Task 13 golden and does not regenerate it, so it intentionally has no hidden `UnrealEditorCmd` or `ReferenceRoot` dependency.

In fixed order, `verify-p5a.ps1` performs:

```text
restore + optimized Debug build
exporter ready/source/schema/lock focused gates
full Import tests
full Core tests + `verify-p5a-golden.ps1` committed-fixture gate
binding/graph/controller/transaction/timeline/sync-action/lifecycle smokes
four-cell matrix
demo certificate
checked-in manual Demo schema/commit-parent/immutable-blob/code-path provenance validation (no interactive launch or rewrite)
all repository Pester
full verify-p4.ps1 regression, which includes P0-P3
Release solution/Core/Import tests
repository diff/clean closure from P5A base ce292b2
```

Child output is parsed line-by-line through `p5a-verification-functions.ps1`; only the top-level script writes `P5A_VERIFICATION_OK`, exactly once and only after all stages return zero. The committed golden stage must run `verify-p5a-golden.ps1` and accept exactly its one frozen fixture marker; the top level intentionally does not expect the unreachable UE generator/native markers. Every internal Pester invocation uses `-PassThru` and requires both `TotalCount > 0` and `FailedCount == 0`; process exit code alone is not accepted.

- [ ] **Step 4: Write final architecture and README evidence**

Document manifest v2 fields/class registry, stable ID formulas, profile counts/real asset IDs, Core ABI/capacities/reason codes, and every Task 6 spec amendment: `OccurrenceHandleId + BoundaryOrdinal` identity/final tie-breakers and owned ordinal; the single versioned Base/Turn/Rotate/Transition/Action layout/digest; exact-handle-only production definitions; local contributing windows; two-bank Core blend/rebase formulas and logical-summary/tail distinction; recovery-cancel-before-normal-request ordering; all three ordered two-slot outcome path classes (replacement, recovery-plus-request, rejected-command-plus-existing-owner-terminal); delayed old Cancel classification; successful-result `P5FailureCode=None` versus separate failed-attempt record. Also record curve math, Base/Turn/Rotate effective authority weights, local-window authority sweep, occurrence handle/epoch lifecycle, dual Action authority domains and loop-cut closure, Notify State ownership, Sync timing ownership, ALS dynamic-transition values/order, monotonic Action request/timeline rules, external failure-recovery latch, pre-foot prepare/post-foot finalize state merge, oracle layout provenance and matrix measurements. Cite only the exact `docs/verification/p5a-demo-manual.md` blob proven unchanged from its evidence commit, reproduce its tested Demo commit plus Transition/Roll/Root Motion measurements and explicitly distinguish that human result from the headless Demo certificate. State the unchanged P5B/P5C/P6/P7 boundaries explicitly.

- [ ] **Step 5: Run the GREEN focused closure, inspect diff and commit**

Run:

```powershell
dotnet test GodotALS.sln -c Debug --no-restore
pwsh -NoProfile -Command '$r = Invoke-Pester -PassThru -Path "tests/*.Tests.ps1"; if ($r.TotalCount -le 0 -or $r.FailedCount -ne 0) { exit 1 }'
git diff --check
git status --short
```

Expected: tests PASS and only Task 22 files are dirty. Then commit:

```powershell
git add scripts/verify-p5a.ps1 scripts/p5a-verification-functions.ps1 tests/VerifyP5A.Tests.ps1 tests/VerificationScripts.Tests.ps1 docs/architecture/p5a-event-action-runtime.md README.md
git commit -m "docs: close p5a event action runtime verification"
```

- [ ] **Step 6: Invoke verification-before-completion and run the clean full certificate**

Invoke `superpowers:verification-before-completion`, then run:

```powershell
pwsh -NoProfile -File scripts/verify-p5a.ps1 -GodotExecutable $godotExe -EngineRoot ../UnrealEngine -UnrealProject $uProject
git status --short
```

Expected: all child gates PASS; exactly one final `P5A_VERIFICATION_OK`; final `git status --short` is empty in the feature worktree. Do not report completion from a focused or partial gate.

## Completion Evidence

The execution handoff must report the feature branch/HEAD, v2 manifest and lock SHA-256, real Transition/Roll IDs, Import/Core test totals, cross-engine `cases=8` and locked reference commit, semantic/lifecycle/failure markers, all four matrix rows with paired digests/allocation/timing, automated Demo marker, checked-in `P5A_MANUAL_DEMO_OK` evidence with its measured Root Motion delta, the unique full certificate marker and the clean-worktree result. It must also state that generated assets remain ignored and that P5B/P5C/P6/P7 were not silently folded into P5A.
