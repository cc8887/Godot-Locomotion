# P2B Godot 全量导入闭环实现记录

## 阶段结论

P2B 已把 P2A 的正式 `als_manifest.json` 编译为 Godot 可直接消费的只读
`AlsAnimationSetResource`，并完成全量资源导入、真实 Mannequin 动画绑定、Overlay/道具冒烟、
rest pose 合同校验，以及真实 rig 的单线程/进程组并行等价验证。

本阶段建立的是稳定的数据与执行边界，不包含完整 locomotion 状态机、Overlay gameplay、IK、
Mantle、Ragdoll、Camera 行为或最终性能预算。这些功能从 P3 开始按 `ALS-Refactored` 模块逐项实现。

## 输入与导入配置

- 源数据：P2A 正式 manifest，`auditSummary.status=complete`。
- Godot：4.7.2 stable .NET，C# 12 / .NET 8。
- Godot importer：内置 FBX/纹理 importer。
- `[importer_defaults]` 固定在 `project.godot`：`animation/import=true`、
  `animation/fps=30`、`fbx/embedded_image_handling=0`。
- P2A ASCII FBX normalizer 会移除引用原 UE 工作站 TGA 的顶层 `Texture`/`Video` object 与 connection；
  P2B 根据 manifest texture/material ID 重建近似材质，不解析原工作站路径。
- 生成源、`.import`、`.godot/imported` 和编译 `.tres` 均由 Git 忽略。

## 严格编译合同

`Als.Import` 在 Godot API 之外完成以下检查与编译：

1. 严格 camelCase JSON、未知字段拒绝、schema/audit/count/order/stable-ID 校验；
2. 141 个声明文件的路径边界、大小、SHA-256、缺失/多余文件审计；
3. 坐标合同固定为 UE 左手 Z-up 厘米到目标右手 Y-up 米，`unitScale` 必须为 `0.01`；
4. Skeleton 编译为逻辑骨、物理骨、Virtual Bone、Socket 和双向整数表；
5. 类人骨架只要声明 pelvis/foot 骨，即必须完整包含 `root`、`pelvis`、`foot_l`、`foot_r`；
6. Animation、Montage、BlendSpace、AimOffset、Material、Texture、PhysicsAsset、Curve 和配置资产
   编译为稳定整数表；PhysicsAsset 的 body/constraint 骨名必须解析到依赖 mesh 的物理骨架；
7. 非空引用不允许猜测默认值，内容错误提供稳定错误码、资产 ID 和 JSON 字段路径。

完整定义会序列化进
`assets/generated/als_v4/compiled/als_animation_set.tres` 的 `DefinitionJson`，并保存 payload SHA-256。
Resource 重新加载时先校验 payload SHA-256，再恢复全部 runtime 表并核对 definition digest。资产 smoke 和真实 rig
harness 只读取这个 `.tres`，不再重新读取或编译 manifest，因此门禁覆盖了 P3 将使用的实际交付物。

## 实测导入结果

| 项目 | 实测值 |
|---|---:|
| 逻辑资产 | 267 |
| 声明二进制文件 | 141 |
| FBX | 137 |
| SkeletalMesh | 7 |
| StaticMesh | 4 |
| AnimationSequence | 126 |
| PNG/TGA | 4 / 0 |
| Godot `.godot/imported` 派生文件 | 282 |
| Godot 导入缓存 | 8.369 MiB |
| 编译 `als_animation_set.tres` | 257,370 bytes（251.338 KiB） |
| 清洁导入加完整 P2B 门禁 | 35.697 秒（本机单次基线，非正确性预算） |

导入审计要求所有 141 个文件均能由 `ResourceLoader` 载入为预期类型。7 个 SkeletalMesh 必须含
`Skeleton3D` 和 `MeshInstance3D`，4 个 StaticMesh 必须含 mesh；126 个动画必须含非空
`AnimationPlayer`，时长误差不超过 `1/30` 秒，并保持物理骨名的有序子集。`Proxy` mesh 是源资产定义的
1 骨简化代理，不被误判为完整 Mannequin。

## 骨架与代表动画

Mannequin skeleton 的合同为 79 个逻辑骨，其中 11 个 Virtual Bone、68 个 FBX 物理骨：

- 源 UE rest pose SHA-1：`6825ad4b0be4e80f14bc604df653ef4641f78521`；
- 目标物理 rest pose SHA-256：
  `ba7bf4ae766b6a84e60af6b062a522a6816759ce6dde53ee19d7602100bb673f`；
- Godot 实际 `Skeleton3D.GetBoneRest()` 经 FBX 骨局部轴逆变换、大小写和 `1.1e-5` 导入误差规范化后，
  与目标 SHA-256 完全一致。

代表动画均从完整 `.tres` 通过稳定 ID 查找，绑定时明确要求导入动画名 `Unreal Take`，且每条轨道必须为
`Skeleton3D:<bone>` 的 position/rotation/scale 轨道：

| 动画 | 时长 | 30 Hz 采样键 | 用途 |
|---|---:|---:|---|
| `ALS_N_Walk_F` | 1.133333 s | 35 | 基础行走；Godot 实测 128 tracks |
| `ALS_N_Run_F` | 0.800000 s | 25 | 基础跑步 |
| `ALS_N_TurnIP_L90` | 2.000000 s | 61 | 原地转向 |
| `ALS_N_Mantle_1m_LH` | 1.500000 s | 46 | Mantle 代表数据 |
| `ALS_Props_M4A1_Poses` | 0.400000 s | 13 | Overlay |
| `ALS_Props_Pistol_1H_Poses` | 0.300000 s | 10 | Overlay |

六个 clip 均可手动推进；前四个动作在 root、pelvis、spine_03、双手和双脚采样中产生姿态变化，
最终姿态摘要不全相同。M4A1 SkeletalMesh 可实例化并被识别为 prop。

## 材质边界

P2B 生成 `StandardMaterial3D` 近似材质：

- 映射名称包含 roughness/metallic 的 scalar 参数；
- 映射首个 color/tint vector 到 albedo color；
- 映射首个 texture override 或 referenced texture 到 albedo texture；
- 支持父材质递归复制和子实例 override；
- 仅在当前 mesh 的 `materialIds` 集合内按导入 surface material 名称匹配，禁止跨 mesh 全局选材质。

名称为空、未命中或在当前 mesh 内存在歧义时，不按 dependency 顺序猜测 surface 槽位，直接输出
结构化 `ALSMATERIAL002` error 并使代表资产门禁失败。
本次 Mannequin 和 M4A1 均按名称命中，未产生材质诊断。

P2 不承诺 UE shader 视觉等价，也未映射 normal/ORM、材质函数、复杂透明/布料或自定义 shader graph。
视觉材质对齐应作为后续独立切片处理。

## 真实 Rig 并行等价

真实 rig harness 使用 P1 的 Gather（主线程/Order 0）、Worker（Order 1）和 Commit（主线程/Order 2）边界。
每个 worker 独占 Mannequin、Skeleton3D、AnimationPlayer、Walk clip 和双缓冲 exchange；资源加载与绑定在
worker 加入树前完成，测量期 worker 不访问 `ResourceLoader`、文件系统、兄弟节点或全局场景。

固定 60 Hz 运行 120 帧，并在第 60 帧替换 character 0。摘要同时包含 frame/character/generation、
七骨 pose digest，以及从编译 runtime 表推进的 notify/sync event digest。Walk 含 2 个 notify 和 2 个
sync marker，门禁明确校验事件数，避免“空事件摘要”误通过。

| 角色数 | single / parallel digest | 事件数 | parallel off-main | missing / stale | replacements |
|---:|---|---:|---:|---:|---:|
| 1 | `CD25891B5DB4B6C3` | 8 | 1 | 0 / 0 | 1 |
| 10 | `60A7B9E62FC29BEA` | 44 | 10 | 0 / 0 | 1 |

两组 single/parallel digest 完全一致；替换前还会确认旧并行 worker 已在非主线程实际运行。
这是正确性门禁，不是十分钟压力或最终性能预算。

## 回归与仓库边界

最终回归包含：

- `verify-p2b.ps1 -CleanImport`：全量导入、完整 Resource、资产 smoke、真实 rig 1/10 矩阵；随后连续三轮缓存复用门禁同样通过，四轮 Godot 进程均正常退出；
- `verify-p1.ps1`：1/10/16/32 角色 single/parallel 摘要一致，所有阶段分配计数为 0；
- `verify-p0.ps1`：基础线程交换门禁通过；
- `dotnet test GodotALS.sln -c Release --no-restore`：67 项测试通过；
- `git diff --check` 与跟踪文件审计通过。

Git 跟踪 0 个 FBX/PNG/TGA、`.import`、`.godot`、生成 `.tres`、UE Binaries/Intermediate、插件包、
`bin` 或 `obj`。`artifacts` 下只跟踪 `artifacts/.gdignore`。

## 后续实施入口

P3 可以稳定依赖 `AlsAnimationSetResource.LoadDefinition()` 和 stable-ID integer lookup，不再按磁盘文件名或
UE object path 扫描资源。建议顺序仍为 Character Motor 与基础 locomotion、stance/gait/rotation mode、
Overlay gameplay、Mantle、IK、Ragdoll、Camera；每个模块增加 UE/Godot golden trace 或固定输入回放。
音频继续作为独立事件系统切片，不阻塞当前 P2 完成状态。
