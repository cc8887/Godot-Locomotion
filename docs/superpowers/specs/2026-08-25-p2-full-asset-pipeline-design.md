# P2 完整 ALS 资产导出与 Godot 导入设计

**状态：** 已确认设计，待实施计划

**日期：** 2026-08-25

**Godot 工程：** `.`

**UE 资产源工程：** `../AdvancedLocomotionSystemV`

**源引擎：** Unreal Engine 5.9.0

**目标引擎：** Godot 4.7.2 .NET

## 一、阶段目标

P2 建立一次覆盖全部目标 ALS 资产的可重复离线管线。管线从 UE 5.9 资产注册表发现资产和依赖，通过 C++ Editor Commandlet 批量导出 FBX、纹理和版本化 sidecar manifest，再由 Godot 严格导入和审计。阶段完成时，真实 Mannequin 骨架、代表性动画、Overlay 和道具必须能在 P1 process-group worker 中以 headless 模式运行。

P2 不迁移 Blueprint 可执行逻辑，不实现完整 locomotion 状态机，也不提交 Marketplace/Fab 二进制资产。它只解决资产发现、确定性导出、语义保真、严格导入和真实骨架线程 smoke。

## 二、已确认范围

### 2.1 纳入的资产

Commandlet 从以下 UE 内容根扫描：

- `/Game/AdvancedLocomotionV4/CharacterAssets`；
- `/Game/AdvancedLocomotionV4/Props`；
- `/Game/AdvancedLocomotionV4/Data`；
- `/Game/AdvancedLocomotionV4/Blueprints/AnimModifiers`；
- `/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys`；
- `/Game/AdvancedLocomotionV4/Blueprints/CameraSystem`；
- `/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic`。

目标集合包含：

- Mannequin Skeletal Mesh、Skeleton、Socket、Virtual Bone 和 PhysicsAsset；
- 全部基础 locomotion、crouch、InAir、Turn in Place、Rotate in Place 和 transition 动画；
- Aim Offset、BlendSpace、Curve、Notify、Notify State 和 Sync Marker；
- Mantle、Roll、Get-up、Montage section、blend、interrupt 和 Root Motion 元数据；
- 全部 Overlay 动画和 Overlay 依赖；
- 道具 Skeletal Mesh、Static Mesh、材质、材质实例和纹理依赖；
- Camera 和角色 Blueprint 中后续 Godot 重写所需的配置值与资产引用；
- ALS Data 目录中的曲线、数据表、枚举和结构定义元数据。

材质依赖闭包自动包含 CharacterAssets 和 Props 引用的纹理，即使纹理不在上述根的直接选择结果中。同一个 UE package path 只导出一次。

### 2.2 排除的资产

第一批明确排除：

- Audio；
- Level 和 BuiltData；
- Environment；
- UI；
- AI 和 Behavior Tree；
- GameMode；
- DerivedDataCache、Saved、Intermediate 和 Engine 内容；
- 未被目标资产引用的测试或开发者私有内容。

音频将在功能需要脚步和动作事件时作为独立切片加入，不阻塞 P2。

## 三、方案选择

### 3.1 采用方案

采用 UE 5.9 C++ Editor 插件和 Commandlet。导出器源码保存在 Godot 仓库 `tools/unreal/AlsGodotExporter`。PowerShell 驱动脚本先调用源引擎的 `RunUAT BuildPlugin`，把插件打包到已忽略的 `artifacts/unreal/AlsGodotExporter`，再部署到源 UE 工程的 `Plugins/AlsGodotExporter`。部署目录必须包含 `.godotals-managed` 哨兵文件；脚本拒绝覆盖没有该哨兵的同名目录。插件描述符使用 `EnabledByDefault`，不修改源工程 `.uproject`。

C++ 方案直接访问 UE Editor、AssetRegistry、UnrealEd 和动画编辑器 API，可完整读取 Python API 覆盖不稳定的 Curve、Notify、Sync Marker、Montage、BlendSpace、PhysicsAsset 和材质实例参数。

### 3.2 未采用方案

不采用纯 Unreal Python 导出器，因为完整动画元数据和 PhysicsAsset 的 Python API 覆盖不足，可能形成 FBX 成功但 ALS 语义丢失的结果。

不采用 Python 发现加 C++ 补充的双路径，因为它需要合并两套排序、错误处理和稳定 ID 逻辑，增加确定性和维护风险。

## 四、组件边界

### 4.1 UE Editor 插件

`AlsGodotExporter` 是仅 Editor 使用的插件，不参与游戏 runtime。插件包含以下独立组件：

- `UAlsGodotExportCommandlet`：解析命令行、协调阶段、返回稳定退出码；
- `FAlsAssetDiscovery`：查询 AssetRegistry、应用根路径和排除规则、计算依赖闭包；
- `FAlsStableAssetId`：根据规范化 UE package path 生成稳定 ID；
- `FAlsExportPlanner`：把发现结果排序并生成 `export_plan.json`；
- `FAlsFbxExporter`：导出 Skeletal Mesh、Static Mesh、Skeleton 参考姿势和 Animation Sequence；
- `FAlsTextureExporter`：把源纹理导出为 PNG 或无法无损转 PNG 时的 TGA；
- `FAlsAnimationMetadataReader`：读取序列长度、采样率、循环、additive、Curve、Notify、Sync Marker 和 Root Motion；
- `FAlsCompositeAssetReader`：读取 Montage、BlendSpace、Aim Offset 和动画引用；
- `FAlsRigMetadataReader`：读取骨架层级、ref pose、Socket、Virtual Bone 和 PhysicsAsset；
- `FAlsMaterialMetadataReader`：读取父材质、纹理参数、标量参数和颜色参数；
- `FAlsManifestWriter`：写入规范 JSON、文件哈希、审计结果并原子发布正式 manifest。

组件通过普通值结构传递发现结果和导出记录。只有 Commandlet 负责日志、退出码和最终发布。

### 4.2 Godot 导入核心

新增普通 `.NET 8` 类库 `src/Als.Import`，不依赖 Godot Node，负责：

- 反序列化受支持 schema version；
- 校验路径、稳定 ID、排序、枚举、依赖和 SHA-256；
- 校验必要骨骼、层级、rest pose hash 和动画语义；
- 生成冻结的稳定 ID 表和 `AlsAnimationSetDefinition`；
- 输出包含资产路径、JSON 字段路径、期望值和实际值的结构化错误。

`tests/Als.Import.Tests` 使用可提交的小型 JSON fixture 测试 schema 和错误诊断，不包含 Marketplace/Fab 模型或动画。

### 4.3 Godot Editor 与运行时适配

Godot 使用内置 FBX/纹理 importer 生成 `.godot/imported` 缓存。`addons/als_importer` 只读取正式 `als_manifest.json`，调用 `Als.Import`，并为 Godot 资源路径建立稳定映射。运行时 `AlsAnimationSet` 是只读 Resource，包含骨骼 ID、clip ID、curve/event/sync 表、Root Motion policy、mask、动作和 Camera 配置。

P2 smoke 不要求完整 AnimationTree。它实例化真实 FBX 场景，取得 `Skeleton3D`、`AnimationPlayer` 或 `AnimationMixer`，验证代表性动画和一件 Overlay 道具，再把真实 rig 放入 P1 `SubThread/Order 1` worker 手动推进。

## 五、确定性数据流

```text
AssetRegistry scan
  -> normalized package paths
  -> explicit include/exclude rules
  -> hard/soft/management dependency closure
  -> type and source-path stable sort
  -> export_plan.json
  -> FBX / PNG / TGA export
  -> metadata extraction
  -> per-file SHA-256
  -> als_manifest.partial.json
  -> output audit
  -> atomic rename to als_manifest.json
```

Godot 只接受正式 `als_manifest.json`。`.partial` 文件用于失败诊断，不能触发导入。

稳定 ID 输入为规范化、区分大小写的 UE package path 和资产名，不包含磁盘绝对路径、时间戳、机器名或 AssetRegistry 遍历顺序。数组按稳定 ID 排序；JSON 使用固定字段顺序、UTF-8 无 BOM、LF 换行和 invariant number formatting。

FBX 固定使用 UE 5.9 的 `UFbxExportOption`：`FBX_2020`、`bASCII=true`、固定 forward axis 和显式 mesh/animation 选项。导出后只规范化 FBX header 中经测试确认的非语义 SDK 字段，例如创建时间、保存时间和随机 FileId；任何未知变化仍令确定性门禁失败。规范化后必须先通过 Godot importer smoke，才计算文件 SHA-256。连续两次导出相同源资产时，`export_plan.json`、`als_manifest.json` 和全部规范化输出文件 SHA-256 必须一致。

## 六、输出布局

所有生成结果位于 Godot 仓库已忽略的目录：

```text
assets/generated/als_v4/
  export_plan.json
  als_manifest.json
  audit/
    export_report.json
    export_report.txt
  meshes/
    skeletal/
    static/
  animations/
  textures/
  partial/
    als_manifest.partial.json
```

FBX 文件名使用稳定 ID，不直接依赖大小写不敏感文件系统上的原始资产名。manifest 保留原 UE object path、package path、asset name、class path、输出相对路径和依赖稳定 ID。

## 七、Manifest 合同

顶层字段至少包含：

- `schemaVersion`；
- `exporterVersion`；
- `sourceEngineVersion`；
- `sourceProjectId`；
- `sourceContentRoot`；
- `coordinateSystem`；
- `unitScale`；
- `skeletons`；
- `skeletalMeshes`；
- `staticMeshes`；
- `animations`；
- `montages`；
- `blendSpaces`；
- `aimOffsets`；
- `materials`；
- `textures`；
- `physicsAssets`；
- `curves`；
- `configAssets`；
- `files`；
- `auditSummary`。

坐标合同固定记录 UE 左手 Z-up、厘米和导出 FBX 转换规则，以及 Godot 右手 Y-up、米。骨架记录父索引、局部 ref pose、规范化 Godot ref pose、必要骨骼、Socket、Virtual Bone 和 rest pose hash。

Animation Sequence 记录 skeleton ID、长度、采样率、帧数、循环、additive 类型、base pose、Root Motion policy、Curve、Notify、Notify State、Sync Marker 和导出 FBX。Montage、BlendSpace 和 Aim Offset 不伪装成独立动画轨道，而是记录引用 clip、sample/section 布局和混合语义。

Material 只记录可移植参数，不承诺复制 UE Shader。Godot 导入器生成可检查的 `StandardMaterial3D` 近似材质；无法映射的参数进入审计警告，但缺失必需纹理是错误。

## 八、失败策略与退出码

Commandlet 对以下情况返回非零：

- 输入工程、输出目录或内容根无效；
- 发现重复稳定 ID 或大小写路径冲突；
- 目标资产类型不受支持；
- 依赖闭包包含缺失 package；
- FBX 或纹理导出失败；
- 骨架缺少 root、pelvis、foot_l 或 foot_r；
- 共享 Skeleton 的动画得到不一致 rest pose hash；
- Root Motion 动作没有有效 root track；
- Montage、BlendSpace、Aim Offset 引用未导出的动画；
- PhysicsAsset 引用不存在的骨骼；
- 输出文件缺失、为空或 SHA-256 不匹配；
- manifest 无法通过自身 schema 校验。

退出码固定为：`2` 参数错误、`3` 资产发现错误、`4` 导出错误、`5` 元数据错误、`6` 输出审计错误。成功为 `0`。

发生错误时保留 `export_plan.json`、`.partial` manifest 和审计报告，不覆盖上一次成功的正式 manifest。日志必须包含 UE object path、处理阶段和确定的错误码。

Godot 导入失败时不生成或更新 `AlsAnimationSet`。诊断必须包含 manifest 相对路径、JSON 字段路径、期望值和实际值；不允许通过猜测默认值继续。

## 九、验证策略

### 9.1 单元和 schema 测试

- 稳定 ID 对相同 package path 重复生成相同值；
- manifest 数组顺序变化会被 validator 拒绝；
- 路径逃逸、绝对路径和反斜杠被拒绝；
- 重复 ID、缺失依赖和错误 hash 被拒绝；
- 必要骨骼、rest pose、Root Motion、Montage 引用和 PhysicsAsset 引用逐项验证；
- 错误包含资产和字段路径；
- 合法 fixture 编译为确定的 `AlsAnimationSetDefinition`。

### 9.2 UE dry-run

`-DryRun` 只扫描并写 `export_plan.json`，不创建 FBX 和纹理。门禁检查：

- 计划至少包含一个 Skeleton、一个 Skeletal Mesh、Animation Sequence、Montage、BlendSpace/Aim Offset、PhysicsAsset、Overlay 资产、Static Mesh、Material 和 Texture；
- Audio、Level、Environment、UI、AI 和 GameMode 数量为 0；
- 每个计划项类型受支持，依赖指向计划内或显式外部基础资产；
- 计划按稳定 ID 排序且无重复。

### 9.3 UE 完整导出

完整导出后立即运行输出审计。随后在新的临时输出目录再次执行完整导出，比较两个 manifest 的规范化内容和每个相对文件的 SHA-256。任何差异使 P2 门禁失败。

### 9.4 Godot headless 导入

Godot 以 `--headless --editor --import` 导入 `assets/generated/als_v4`。严格导入器验证完整 manifest 和全部文件引用，输出导入数量和错误统计。

### 9.5 真实资产 smoke

headless smoke 至少验证：

- Mannequin `Skeleton3D` 存在，骨数大于 0；
- root、pelvis、foot_l、foot_r 可通过导入期稳定 ID 解析；
- Godot 骨架 rest pose hash 与 manifest 的 Godot 规范化 hash 一致；
- Idle、Walk Forward、Run Forward、Turn、Mantle 和一个 Overlay clip 可加载并推进；
- 一件道具 Mesh 和近似材质可实例化；
- 真实 rig 在 single 和 parallel worker 中运行相同固定帧数，pose/事件 digest 相同；
- 无 Godot thread-access error、缺帧、过期 generation 或非零退出码。

## 十、完成条件

P2 只有同时满足以下条件才完成：

1. UE 5.9 Editor 插件和 Commandlet 能从干净构建通过命令行运行；
2. dry-run 覆盖全部确认的资产类别并排除音频等非目标类别；
3. 全量导出成功，正式 manifest 只在审计后发布；
4. 双次全量导出通过确定性比较；
5. `Als.Import.Tests` 全部通过；
6. Godot headless 完成全部生成资产导入；
7. 真实骨架、代表性动画、Overlay 和道具 smoke 通过；
8. 真实 rig single/parallel digest 相同；
9. P0 和 P1 门禁继续通过；
10. Git 不跟踪 Marketplace/Fab 二进制资产、`.godot`、UE Binaries/Intermediate 或本机路径。

## 十一、实施分段

P2 由两个连续、可独立验收的实施计划完成：

1. **P2A 完整 UE 导出闭环**：manifest schema、C# schema validator 基础、C++ Editor 插件、Commandlet、dry-run、全量导出、双运行确定性审计和插件受控部署。P2A 的交付物是已审计的 `assets/generated/als_v4`，不包含 Godot 运行时加载。
2. **P2B Godot 导入闭环**：完整 `Als.Import` validator/compiler、Editor 导入适配、`AlsAnimationSet`、全部生成资产 headless import、真实骨架/动画/Overlay/道具 smoke，以及真实 rig 的 single/parallel 等价验证。

P2B 只能在 P2A 正式 manifest 通过审计后开始。两个计划均在独立 Git worktree 中实施并分别回归 P0/P1。

## 十二、后续边界

P2 完成后，P3 才开始基础 Character Motor、gait、stance、rotation mode 和 locomotion 动画映射。P3 使用本阶段生成的稳定 ID 与 `AlsAnimationSet`，不再按字符串或磁盘路径发现资产。

音频、脚步表面映射和音效事件作为后续事件系统切片实施。完整 Overlay gameplay 逻辑也不属于 P2；P2 只保证 Overlay 动画、道具和依赖可被确定导出、导入和引用。
