# P2A UE 全量资产导出实现记录

## 阶段目标

P2A 建立 UE 5.9 到 Godot ALS 项目的可重复资产交付边界。本阶段一次发现并导出 ALS V4 角色、动画、Overlay、道具模型及其项目内依赖；音频、关卡、环境、UI、AI 和 GameMode 不在本阶段范围内。

本阶段完成 UE 侧导出、元数据、审计和确定性门禁，不代表 Godot 已完成这些资产的导入、重定向或运行时功能映射。后续 P2B 将消费本阶段的正式 manifest。

## 工具链与入口

- 源项目：`D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject`
- Unreal Engine：`5.9.0-0+UE5`
- 导出插件：`AlsGodotExporter 1.0.0`，Editor-only、Win64
- 目标内容根：`/Game/AdvancedLocomotionV4`
- 输出根：`assets/generated/als_v4`，由 Git 忽略
- 稳定资产 ID：`SHA1(UTF8(/Game/.../Asset.Asset))`，40 位小写十六进制
- FBX：ASCII、FBX 2020 / 7.7、X forward、无 LOD
- 纹理：PNG；本批次没有需要退回 TGA 的纹理

统一验证入口：

```powershell
.\scripts\verify-p2a.ps1 `
  -EngineRoot 'D:\UnrealEngine' `
  -UnrealProject 'D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
```

脚本按顺序执行插件 BuildPlugin、带管理哨兵的受控部署、ReadyCheck、dry-run、第一次完整导出、第二次隔离导出和逐文件确定性比较。完整导出必须带 `-AllowCommandletRendering -RenderOffscreen`；UE 5.9 的 SkeletalMesh FBX exporter 需要可用的 renderer scene，不能使用 `-nullrhi`。

## 实测资产清单

2026-08-25 对源项目实际扫描得到 267 个目标资产，`excluded=0`。这里的 `excluded=0` 表示纳入扫描的 ALS 角色、动画、Overlay 和 Props 范围内没有无法分类或被静默跳过的资产；它不表示音频等非目标目录被导出。

| 资产类型 | 计划数量 | 二进制导出 |
|---|---:|---:|
| AnimationSequence | 126 | 126 FBX |
| AnimMontage | 18 | manifest 元数据 |
| BlendSpace | 8 | manifest 元数据 |
| AimOffset | 1 | manifest 元数据 |
| Skeleton | 5 | manifest 元数据，由 SkeletalMesh FBX 携带骨架 |
| SkeletalMesh | 7 | 7 FBX |
| StaticMesh | 4 | 4 FBX |
| PhysicsAsset | 2 | manifest 元数据 |
| Curve | 28 | manifest 元数据 |
| Material | 6 | manifest 元数据 |
| MaterialInstance | 9 | manifest 元数据 |
| Texture | 4 | 4 PNG |
| Blueprint | 18 | manifest 配置摘要 |
| DataTable | 1 | manifest 配置摘要 |
| OtherConfig | 30 | manifest 配置摘要 |
| **合计** | **267** | **141 文件** |

二进制结果共 137 个 FBX 和 4 个 PNG。连同 `export_plan.json`、partial/formal manifest 和审计报告，单个输出目录共 146 个文件、142,702,695 bytes（约 136.1 MiB）。126 个不产生独立二进制文件的复合动画、材质和配置资产仍以稳定 ID、类型、依赖关系和类型化元数据进入 formal manifest。

9 个 MaterialInstance 均记录 parent 和三类显式 parameter override 数组。本批源资产实测包含 9 个 scalar、9 个 vector、0 个 texture override；texture 数组即使为空也会写出，避免 P2B 把“无覆盖”与“导出器遗漏字段”混为一谈。

代表性资产包括：

- `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin`
- `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan`
- `/Game/AdvancedLocomotionV4/Props/Meshes/M4A1.M4A1`
- `/Game/AdvancedLocomotionV4/Props/Meshes/Bow.Bow`
- `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Overlay/M4A1/ALS_Props_M4A1_Sprint_F.ALS_Props_M4A1_Sprint_F`
- `/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Overlay/Pistol/ALS_Props_Pistol_1H_Aim_Sweep_Crouched.ALS_Props_Pistol_1H_Aim_Sweep_Crouched`

## Manifest 与输出审计

正式入口为 `assets/generated/als_v4/als_manifest.json`。发布顺序是先写 partial manifest，导出全部文件，完成路径、长度、SHA-256、引用和数量审计，再原子替换 formal manifest。失败时不会把 incomplete manifest 发布为正式结果。

本次 formal manifest：

- `auditSummary.status=complete`
- `assetCount=267`
- `fileCount=141`
- `errorCount=0`
- `warningCount=0`
- 单次实测 manifest SHA-256：`b5d5602974b95bc45aaa9ed9b4f6bf0a9c1033f19dbf659082b2135a781bd569`

`files[]` 为每个 FBX/PNG 记录相对路径、长度和 SHA-256。审计拒绝空文件、缺失文件、重复路径、输出根逃逸、无效 hash、目标资产缺少输出以及内部引用缺失。本批对 skeleton、animation、Montage、BlendSpace、material parent 和 texture 的 194 个 metadata 引用检查结果为 0 缺失。SHA-256 通过 UE 所带 OpenSSL 计算；UE 5.9 当前的平台 SHA-256 API 在该 commandlet 路径会发生 native crash，因此没有使用它。

## 确定性处理

UE FBX exporter 会写入导出时间、绝对输出路径和进程相关 FBX object ID。normalizer 只在 ASCII FBX 导出完成后处理已知的非语义字段：

- `CreationTimeStamp.Year/Month/Day/Hour/Minute/Second/Millisecond`
- `CreationTime`
- `LastSaved`
- `DocumentUrl`
- `SrcDocumentUrl`
- `ObjectId`，按声明和 bind-pose 首次出现顺序映射到固定 ID

normalizer 不修改动画采样值、骨骼变换、网格顶点、材质参数或曲线值。两次隔离完整导出的 146 个相对文件已逐项比较文件集合、长度和 SHA-256，formal manifest 另做原始字节比较，结果为：

```text
P2A_DETERMINISM_OK files=146
```

两次 commandlet 实测用时为 38.08 秒和 37.82 秒。计时受 UE 启动、shader 和 DDC 状态影响，只用于记录当前机器基线，不作为正确性门禁。

## 已知提示与限制

导出器审计为 0 warning、0 error。UE 进程仍会输出源工程或本机配置层提示：

- ALS 原生 `Calculate_RotationAmount` 动画修改器使用已废弃的 `FinalizeBoneAnimation`；
- Derived Data Cache 配置迁移提示；
- 项目缺少 `Grabbable` 自定义碰撞通道的 profile 提示。

这些提示没有造成目标资产缺失或审计失败，也没有在导出器报告中被隐藏。后续若源 ALS 内容升级，应优先把动画修改器迁移到 `UAnimDataController`，并重新运行完整门禁。

生成资产、第二次确定性输出、插件包、UE Binaries/Intermediate 和 .NET/Godot 缓存均不进入 Git。Git 只保存合同、schema、导出器源码、脚本和实现记录。

## P2B 前提

P2B 应从 formal manifest 驱动 Godot 导入，不按文件名猜测关联关系，并至少建立以下门禁：

1. 验证骨名、父索引、rest pose hash、单位比例和 UE Z-up 左手系到 Godot Y-up 右手系转换；
2. 导入 7 个 SkeletalMesh、4 个 StaticMesh、126 个 AnimationSequence 和 4 个纹理，核对 Godot 侧文件/资源计数；
3. 校验动画时长、采样率、Root Motion、曲线、notify、sync marker、Montage section 和 BlendSpace sample；
4. 建立 Material/MaterialInstance 到 Godot 材质参数的显式映射；
5. 先用 Mannequin 加一组 Base locomotion 与一个 M4A1/Pistol Overlay 做真实骨架 smoke，再扩到全部 Overlay 和 Props；
6. 在 P1 Gather/Worker/Commit 架构中验证真实 Skeleton3D/AnimationMixer 的线程所有权、single/parallel 等价性和性能。

P2B 完成后，才进入参考 `ALS-Refactored` C++ 行为的逐模块移植：基础 locomotion、stance/gait/rotation mode、Overlay、mantle、ragdoll、IK 和 camera，且每个模块都需要 UE/Godot golden trace 或等价回放对比。
