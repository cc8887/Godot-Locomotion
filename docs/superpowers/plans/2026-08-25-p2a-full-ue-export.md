# P2A Full UE Asset Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用 UE 5.9 C++ Editor Commandlet 一次发现并导出全部目标 ALS 角色、动画、Overlay、道具及依赖，生成通过 schema、输出审计和双运行确定性门禁的正式 manifest。

**Architecture:** 普通 .NET `Als.Import` 类库先固定跨引擎 export plan/manifest 合同和稳定 ID；UE Editor 插件使用 AssetRegistry 生成确定性计划，加载目标资产提取语义元数据，再通过自动化 FBX/纹理 exporter 输出文件。PowerShell 驱动 `RunUAT BuildPlugin`、受控部署、dry-run、两次完整导出和 SHA-256 比较；正式 manifest 只在 Commandlet 自审计成功后由 `.partial` 原子发布。

**Tech Stack:** Unreal Engine 5.9 C++ Editor plugin、AssetRegistry、UnrealEd exporters、JSON、C# 12、.NET 8、xUnit、JSON Schema、PowerShell、Git

---

## 文件职责

```text
tools/schemas/als_export_plan.schema.json               P2A dry-run 计划 schema
tools/schemas/als_manifest.schema.json                  P2A/P2B 共享 manifest schema

src/Als.Import/Als.Import.csproj                        无 Godot 依赖的导入合同类库
src/Als.Import/Manifest/AlsAssetKind.cs                 跨 UE/Godot 固定资产类别
src/Als.Import/Manifest/AlsExportPlan.cs                export_plan JSON 合同
src/Als.Import/Manifest/AlsManifest.cs                  manifest 顶层和通用记录
src/Als.Import/Manifest/AlsStableAssetId.cs             SHA-1 稳定资产 ID
src/Als.Import/Validation/AlsExportPlanValidator.cs      路径、排序、ID、依赖和排除项验证
src/Als.Import/Validation/AlsValidationIssue.cs          结构化诊断
tests/Als.Import.Tests/*                                 schema/稳定 ID/validator 测试

tools/unreal/AlsGodotExporter/AlsGodotExporter.uplugin  Editor 插件描述符
tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/* 模块、Commandlet 和导出实现
scripts/build-als-exporter.ps1                           BuildPlugin 与受控部署
scripts/verify-p2a.ps1                                  P2A 统一门禁

assets/generated/als_v4/*                               本地生成，Git 忽略
artifacts/unreal/*                                      插件包和第二次导出，Git 忽略
docs/architecture/p2a-full-ue-export.md                 实测资产计数、限制和命令
```

稳定 ID 固定为 `SHA1(UTF8(normalizedObjectPath))` 的 40 位小写十六进制。`normalizedObjectPath` 必须是 `/Game/.../Asset.Asset`，使用 `/`、保留大小写、不含磁盘路径。

### Task 1：固定 export plan/manifest 合同和 C# 验证基础

**Files:**
- Create: `src/Als.Import/Als.Import.csproj`
- Create: `src/Als.Import/Manifest/AlsAssetKind.cs`
- Create: `src/Als.Import/Manifest/AlsExportPlan.cs`
- Create: `src/Als.Import/Manifest/AlsManifest.cs`
- Create: `src/Als.Import/Manifest/AlsStableAssetId.cs`
- Create: `src/Als.Import/Validation/AlsValidationIssue.cs`
- Create: `src/Als.Import/Validation/AlsExportPlanValidator.cs`
- Create: `tests/Als.Import.Tests/Als.Import.Tests.csproj`
- Create: `tests/Als.Import.Tests/AlsStableAssetIdTests.cs`
- Create: `tests/Als.Import.Tests/AlsExportPlanValidatorTests.cs`
- Create: `tests/Als.Import.Tests/Fixtures/valid_export_plan.json`
- Create: `tools/schemas/als_export_plan.schema.json`
- Create: `tools/schemas/als_manifest.schema.json`
- Modify: `GodotALS.sln`

- [ ] **Step 1：先写稳定 ID 和 export plan validator 失败测试**

核心测试 API：

```csharp
[Fact]
public void StableIdUsesNormalizedObjectPath()
{
    Assert.Equal(
        "521a92ef21af7e41f2a1f1df2d3ab94e813aa194",
        AlsStableAssetId.Create("/Game/Test/Asset.Asset"));
}

[Fact]
public void ValidatorRejectsUnsortedAssetsAndExcludedAudio()
{
    var plan = new AlsExportPlan(
        1,
        [
            Asset("b", "/Game/AdvancedLocomotionV4/Audio/Step.Step", AlsAssetKind.OtherConfig),
            Asset("a", "/Game/AdvancedLocomotionV4/Props/Meshes/Box.Box", AlsAssetKind.StaticMesh),
        ],
        new AlsExportPlanSummary(2, 0));

    var issues = AlsExportPlanValidator.Validate(plan);

    Assert.Contains(issues, issue => issue.Code == "ALSPLAN004");
    Assert.Contains(issues, issue => issue.Code == "ALSPLAN007");
}
```

- [ ] **Step 2：运行测试确认 RED**

Run:

```powershell
dotnet test .\tests\Als.Import.Tests\Als.Import.Tests.csproj
```

Expected: 项目或 `GodotAls.Import.Manifest` API 不存在。

- [ ] **Step 3：实现合同、稳定 ID 和 validator**

`AlsAssetKind` 固定为：

```csharp
public enum AlsAssetKind
{
    Skeleton,
    SkeletalMesh,
    StaticMesh,
    AnimationSequence,
    AnimMontage,
    BlendSpace,
    AimOffset,
    PhysicsAsset,
    Material,
    MaterialInstance,
    Texture,
    Curve,
    DataTable,
    Blueprint,
    OtherConfig,
}
```

`AlsExportPlanAsset` 字段固定为 `Id/ObjectPath/PackagePath/ClassPath/Kind/OutputPath/Dependencies/ExternalDependencies`。validator 错误码：schema `ALSPLAN001`、ID 格式 `002`、ID 不匹配 `003`、排序 `004`、重复 `005`、路径 `006`、排除路径 `007`、缺失依赖 `008`、输出路径 `009`、summary 不一致 `010`。

稳定 ID 实现：

```csharp
public static string Create(string objectPath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
    if (!objectPath.StartsWith("/Game/", StringComparison.Ordinal) ||
        objectPath.Contains('\\'))
    {
        throw new ArgumentException("UE object path must be a /Game path.", nameof(objectPath));
    }

    return Convert.ToHexString(
        SHA1.HashData(Encoding.UTF8.GetBytes(objectPath))).ToLowerInvariant();
}
```

- [ ] **Step 4：编写两个 JSON Schema 并确认 GREEN**

两个 schema 使用 draft 2020-12，`additionalProperties=false`。plan 要求 `schemaVersion=1`、`assets`、`summary`；manifest 要求设计规格中的全部顶层数组、`files` 和 `auditSummary`。稳定 ID pattern 为 `^[0-9a-f]{40}$`，生成路径 pattern 为 `^(meshes|animations|textures)/[0-9a-f]{40}\\.(fbx|png|tga)$`。

Run:

```powershell
dotnet sln .\GodotALS.sln add .\src\Als.Import\Als.Import.csproj --solution-folder src
dotnet sln .\GodotALS.sln add .\tests\Als.Import.Tests\Als.Import.Tests.csproj --solution-folder tests
dotnet test .\GodotALS.sln
```

Expected: 原有 23 项测试和新增测试全部通过，0 warning/error。

- [ ] **Step 5：提交**

```powershell
git add GodotALS.sln src/Als.Import tests/Als.Import.Tests tools/schemas
git commit -m "feat: define P2 asset export contracts"
```

### Task 2：建立 UE Editor 插件、Commandlet 和受控部署

**Files:**
- Create: `tools/unreal/AlsGodotExporter/AlsGodotExporter.uplugin`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/AlsGodotExporter.Build.cs`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Public/AlsGodotExporterModule.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExporterModule.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Public/AlsGodotExportCommandlet.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`
- Create: `scripts/build-als-exporter.ps1`
- Create: `scripts/verify-p2a.ps1`

- [ ] **Step 1：先创建失败的 P2A 插件门禁**

`verify-p2a.ps1` 接受必填 `-EngineRoot`、`-UnrealProject`，以及默认输出目录。第一版调用不存在的 `build-als-exporter.ps1`，然后要求：

```text
GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=1.0.0
```

Run:

```powershell
.\scripts\verify-p2a.ps1 `
  -EngineRoot '../UnrealEngine' `
  -UnrealProject '../AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
```

Expected: RED，构建脚本或插件不存在。

- [ ] **Step 2：实现最小 Editor 插件和 Commandlet**

`.uplugin` 固定 `EnabledByDefault=true`、`CanContainContent=false`、Editor module、Win64 allow list。Build.cs 依赖：

```csharp
PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
PrivateDependencyModuleNames.AddRange(new[]
{
    "AssetRegistry", "Json", "JsonUtilities", "UnrealEd",
    "EngineAssetDefinitions", "AnimationDataController"
});
```

Commandlet 构造函数设置 `IsClient=false`、`IsEditor=true`、`LogToConsole=true`、`ShowErrorCount=true`。`Main()` 支持 `-ReadyCheck`，输出严格 marker 后返回 0；未知或缺少参数返回 2。

- [ ] **Step 3：实现 BuildPlugin 和受控部署**

脚本流程固定为：

1. 解析 `RunUAT.bat`、插件源、打包目录和目标 `Plugins/AlsGodotExporter`；
2. 校验所有路径为绝对路径；
3. 删除旧打包目录前确认它位于仓库 `artifacts/unreal`；
4. 调用 `RunUAT.bat BuildPlugin -Plugin=<uplugin> -Package=<package> -TargetPlatforms=Win64`；
5. 若目标插件目录存在但没有 `.godotals-managed`，立即失败；
6. 只删除带哨兵的目标目录，复制打包插件并创建哨兵；
7. 调用 `UnrealEditor-Cmd.exe <uproject> -run=AlsGodotExport -ReadyCheck -unattended -nop4 -nosplash -nullrhi`。

- [ ] **Step 4：运行门禁确认 GREEN**

Expected: UAT 和 UE 进程均退出 0，输出 `GODOT_ALS_EXPORTER_READY`，源 `.uproject` 内容未改变。

- [ ] **Step 5：提交**

```powershell
git add tools/unreal/AlsGodotExporter scripts/build-als-exporter.ps1 scripts/verify-p2a.ps1
git commit -m "feat: scaffold UE ALS export commandlet"
```

### Task 3：实现完整资产发现与 dry-run 计划

**Files:**
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportTypes.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsStableAssetId.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsStableAssetId.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAssetDiscovery.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAssetDiscovery.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportPlanner.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportPlanner.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`
- Modify: `scripts/verify-p2a.ps1`

- [ ] **Step 1：扩展门禁并确认 dry-run RED**

门禁运行 `-DryRun -Output=<assets/generated/als_v4>`，要求 `export_plan.json` 和 marker：

```text
GODOT_ALS_P2A_PLAN_OK assets=<N> exportable=<N> config=<N> excluded=0
```

PowerShell 解析 JSON 并断言资产类别包含 Skeleton、SkeletalMesh、StaticMesh、AnimationSequence、AnimMontage、BlendSpace 或 AimOffset、PhysicsAsset、Material/MaterialInstance、Texture、Blueprint；任一 object path 含 `/Audio/`、`/Environment/`、`/Levels/`、`/UI/`、`/AI/` 或 `/GameModes/` 则失败。

- [ ] **Step 2：实现稳定 ID 和类型分类**

C++ 使用 `FTCHARToUTF8`、`FSHA1::HashBuffer()`、`BytesToHex().ToLower()`，结果必须与 Task 1 fixture 一致。类型分类基于 `FAssetData.AssetClassPath`，子类 class path 显式映射到 `EAlsAssetKind`，未知目标资产归为 `OtherConfig`，不静默跳过。

- [ ] **Step 3：实现 AssetRegistry 扫描与依赖闭包**

使用：

```cpp
FARFilter Filter;
Filter.PackagePaths = IncludedRoots;
Filter.bRecursivePaths = true;
Filter.bIncludeOnlyOnDiskAssets = true;
AssetRegistry.GetAssets(Filter, Assets);
```

先按排除前缀过滤。对 Mesh、Material 和 MaterialInstance 使用 `GetDependencies(PackageName, ..., EDependencyCategory::Package)` 递归纳入 `/Game/AdvancedLocomotionV4` 下的 Material、MaterialInstance 和 Texture；`/Engine` 依赖记录为 external dependency，不复制 Engine 资产。最终按稳定 ID ordinal 排序并拒绝 ID/大小写冲突。

- [ ] **Step 4：写入规范 export_plan.json 并确认 GREEN**

JSON 字段顺序固定，保存 UTF-8 无 BOM。Commandlet 写临时文件后调用 `IFileManager::Move()` 原子替换计划。C# `AlsExportPlanValidator` 通过一个小 CLI 入口或 PowerShell 反序列化检查同一计划。

Run P2A gate，Expected: dry-run marker 成功、`excluded=0`、所有目标类别存在。

- [ ] **Step 5：提交**

```powershell
git add tools/unreal/AlsGodotExporter scripts/verify-p2a.ps1
git commit -m "feat: discover complete ALS asset export plan"
```

### Task 4：提取骨架、动画、复合资产、材质和配置元数据

**Files:**
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsRigMetadataReader.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsRigMetadataReader.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAnimationMetadataReader.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsCompositeAssetReader.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsCompositeAssetReader.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsMaterialMetadataReader.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsMaterialMetadataReader.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`

- [ ] **Step 1：先在门禁中要求 partial manifest 元数据并确认 RED**

dry-run 也必须生成 `partial/als_manifest.partial.json`。PowerShell 检查：Mannequin skeleton 含 root/pelvis/foot_l/foot_r，animation 数量大于 0，至少一个动画含 curve 或 notify，Montage section 非空，BlendSpace sample 非空，PhysicsAsset body 非空，Overlay object path 非空，Props mesh 非空。

- [ ] **Step 2：实现 rig 和 animation metadata reader**

骨架读取 `FReferenceSkeleton` 的 bone name、parent index 和 ref bone pose；local transform 以 translation/rotation quaternion/scale 数组写入。rest pose hash 对按 bone index 排序的 UTF-8 bone name、parent 和 IEEE754 float bits 做 SHA-1。

Animation Sequence 读取 skeleton、play length、sampling frame rate、sample keys、loop interpolation、additive settings、root motion、`Notifies`、`AuthoredSyncMarkers` 和 DataModel curve names。Notify/marker 按 trigger time、name、原始 index 排序。

- [ ] **Step 3：实现 Montage、BlendSpace/Aim Offset 和 PhysicsAsset reader**

Montage 记录 CompositeSections、SlotAnimTracks、segment clip references、blend in/out 和 next section。BlendSpace/Aim Offset 记录三个 blend parameter 及每个 sample 的 animation ID、sample value 和 rate scale。PhysicsAsset 记录 `SkeletalBodySetups` bone、primitive count，以及 constraint 两端 bone。

- [ ] **Step 4：实现材质、纹理和 Blueprint/Data 配置元数据**

Material Instance 记录 parent ID、texture/scalar/vector parameter overrides；基础 Material 记录 referenced textures。Blueprint/Data 只记录 class path、asset references 和可序列化的默认配置摘要，不序列化 bytecode 或图。

- [ ] **Step 5：写 partial manifest 并确认门禁 GREEN**

manifest 包含设计规格固定的全部顶层数组。dry-run 中 `files=[]`、`auditSummary.status="planned"`，只写 partial，不写正式 manifest。

- [ ] **Step 6：提交**

```powershell
git add tools/unreal/AlsGodotExporter scripts/verify-p2a.ps1
git commit -m "feat: extract complete ALS asset metadata"
```

### Task 5：实现 FBX、纹理导出、规范化和输出审计

**Files:**
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsFbxExporter.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsFbxExporter.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsTextureExporter.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsTextureExporter.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsFbxNormalizer.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsFbxNormalizer.cpp`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsOutputAuditor.h`
- Create: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsOutputAuditor.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsGodotExportCommandlet.cpp`

- [ ] **Step 1：扩展门禁要求 full export 并确认 RED**

Commandlet full 模式成功 marker：

```text
GODOT_ALS_P2A_EXPORT_OK assets=<N> files=<N> fbx=<N> textures=<N> warnings=<N>
```

门禁要求正式 `als_manifest.json` 存在、partial 仍保留、每个 `files[].relativePath` 存在且非空、SHA-256 为 64 位小写十六进制。

- [ ] **Step 2：实现自动化 FBX 和纹理导出**

为 `UAssetExportTask` 设置 `bAutomated=true`、`bPrompt=false`、`bReplaceIdentical=true`、`bUseFileArchive=true`。FBX options 固定 `FBX_2020`、ASCII、front X axis、vertex color、无 LOD、导出 morph target、animation 不带 preview mesh。SkeletalMesh、StaticMesh 和 AnimationSequence 使用对应 UE exporter；Skeleton 由 SkeletalMesh FBX 和 manifest ref pose 承载。

Texture 优先 `UTextureExporterPNG`，不支持 PNG 时使用 TGA exporter；输出扩展名写回 plan/manifest，不能通过改后缀伪装格式。

- [ ] **Step 3：实现 ASCII FBX 非语义字段规范化**

只允许替换已登记的 header key：`FileId`、`CreationTime`、`LastSaved` 及其分量。normalizer 输出被修改 key 列表；出现未知 header 时间/随机字段或二进制 FBX 头时返回导出错误 4。规范化后文件必须仍以 `; FBX` 文本头开始。

- [ ] **Step 4：实现 SHA-256 和输出审计**

读取文件到 `TArray64<uint8>`，调用 `FPlatformMisc::GetSHA256Signature()`，使用 `FSHA256Signature.Signature` 生成 64 位小写 hex。审计拒绝缺失/空文件、重复相对路径、路径逃逸、hash 失败、目标资产没有输出，以及 metadata 内部引用缺失。

- [ ] **Step 5：原子发布正式 manifest 并确认 GREEN**

先写 `partial/als_manifest.partial.json`，审计通过后复制为同目录临时正式文件并用 `IFileManager::Move()` 替换 `als_manifest.json`。失败时不得改变已有正式 manifest。

- [ ] **Step 6：提交**

```powershell
git add tools/unreal/AlsGodotExporter scripts/verify-p2a.ps1
git commit -m "feat: export and audit complete ALS asset set"
```

### Task 6：建立双运行确定性门禁并完成真实全量导出

**Files:**
- Modify: `scripts/verify-p2a.ps1`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsExportPlanner.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsManifestWriter.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsFbxNormalizer.cpp`
- Modify: `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsOutputAuditor.cpp`
- Create: `scripts/compare-p2a-exports.ps1`

- [ ] **Step 1：实现两个隔离输出目录的比较器**

第一次输出到 `assets/generated/als_v4`，第二次输出到 `artifacts/p2a-determinism/als_v4`。比较器检查相对文件集合、文件长度和 SHA-256；manifest 逐字节比较。差异输出首个 relative path、两侧长度和 hash，返回非零。

- [ ] **Step 2：运行 dry-run 和第一次完整导出**

Run:

```powershell
.\scripts\verify-p2a.ps1 `
  -EngineRoot '../UnrealEngine' `
  -UnrealProject '../AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject' `
  -GodotProjectRoot '.'
```

Expected: plugin build/deploy、dry-run、full export 完成；输出全部目标类别，音频等排除数量为 0。

- [ ] **Step 3：运行第二次完整导出和确定性比较**

Expected: 两侧相对文件、manifest 和 SHA-256 全部相同，输出 `P2A_DETERMINISM_OK`。

- [ ] **Step 4：若失败，按 systematic-debugging 修复**

保留两个输出目录。先确定差异层：计划、metadata JSON、FBX header/track、texture bytes 或文件集合；只修改已确认根因的 reader/exporter/normalizer，再重跑单资产复现和完整矩阵。

- [ ] **Step 5：提交门禁修正**

```powershell
git add scripts/verify-p2a.ps1 scripts/compare-p2a-exports.ps1 tools/unreal/AlsGodotExporter
git commit -m "test: enforce deterministic complete ALS export"
```

### Task 7：记录 P2A 结果并回归 P0/P1

**Files:**
- Create: `docs/architecture/p2a-full-ue-export.md`
- Modify: `docs/superpowers/plans/2026-08-25-p2a-full-ue-export.md`

- [ ] **Step 1：记录真实资产计数和已知限制**

文档记录 UE/插件版本、每种资产计划/导出数量、输出大小、耗时、warnings、排除数量、代表性资产路径、manifest/hash、FBX normalizer 修改字段和 P2B 前提。明确生成资产不进入 Git。

- [ ] **Step 2：运行最终验证**

```powershell
.\scripts\verify-p2a.ps1 -EngineRoot '../UnrealEngine' -UnrealProject '../AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject' -GodotProjectRoot '.'
.\scripts\verify-p1.ps1 -GodotExecutable 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
.\scripts\verify-p0.ps1 -GodotExecutable 'Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

Expected: `P2A_VERIFICATION_OK`、`P1_VERIFICATION_OK`、`P0_VERIFICATION_OK`，所有 .NET 测试通过，构建 0 warning/error。

- [ ] **Step 3：提交完成记录**

```powershell
git add docs/architecture/p2a-full-ue-export.md docs/superpowers/plans/2026-08-25-p2a-full-ue-export.md
git commit -m "docs: record P2A full UE asset export"
```

- [ ] **Step 4：最终审计**

```powershell
git status --short --branch
git log --oneline --decorate -14
git ls-files | Select-String -Pattern '(^|/)(assets/generated|artifacts|Binaries|Intermediate|\.godot|bin|obj)/'
```

Expected: feature 分支干净；生成资产、插件包、UE build 输出和 .NET/Godot 缓存均未被 Git 跟踪。
