# ALS / Lyra 共用 additive 姿态、完整惯性载荷与 RootYaw 数学

2026-10-04，直接在当前主目录推进。当前目标按 `ROADMAP.md` 顶部执行：迁移 Lyra locomotion 思想和运动核心算法，复用 ALS，覆盖手枪与步枪；不要求完整复制 UE 私有实现或 URO。

## 本批实现

`AlsPrecisePoseBlender.AccumulateAdditive` 接收原始权重，并提供 profile 强制 identity blend 与既有数值后端选项。原 ALS 的 `LocalApply`、完整权重路径，以及 Lyra Main / Montage 实际调用同一低层运算。骨权重、节点相关性、归一化次数及其位置由调用节点决定，避免将有不同调用策略的节点强行合并。

`AlsPoseDataInertialization` 放在纯 .NET Core。复用原 `AlsInertialization` 和 `AlsInertialDecay`，接管曲线 presence / flags、RootMotion 速度差分、旋转 Log/Exp、衰减、组件 teleport 历史以及完整 CopyFrom / Reset。骨数、曲线数与长度单位可配置，不依赖 Lyra 的 81 骨布局或 Godot 类型。整数等其他属性由适配层透传。

`LyraMainInertialization` 仅保留实际资源布局身份、原 Main 节点配置校验和输入／输出适配。两处旧诊断反射入口改为读取 Core 所拥有的同一内核，生产接口无需公开可变内核。

`AlsRootRotationMath` 接管 yaw 四元数数学，默认使用托管 SinCos。Godot 只负责寻找与装载可选 Win64 数值后端；DLL 缺失时回退到 Core。原数值桥仍可用于既有原生参考，其源码本来就不依赖 Unreal 引擎。现阶段不把完全相同的 Win64 舍入作为可玩 locomotion 的必需条件。

## Core 验证

Release 最终 102 项通过、0 失败、0 跳过。其中新增 12 项：一骨独立载荷下的曲线缺失与 flags、RootMotion 速度过渡、候选取消后重试、Reset、错误骨数／曲线数／单位 CopyFrom 拒绝，以及默认托管 yaw 方向、整圈旋转、可选后端输入与非有限角度拒绝。其余 90 项为已有 ALS 惯性、混合与采样相关回归。

新增测试验证通用 API 与可变历史边界；原生产输入和完整数据对照由下面的 Godot 场景补充。日志与 TRX 位于 `artifacts/lyra-analysis/pose-data-inertia-core-v4-tests*`。

## 运行验证

Debug / ExportRelease 最终构建均 0 错误、0 警告。运行期间 14 份实施／验证源冻结，范围之外的当前 dirty 文件、导出资源和配置以初始快照保护。

Debug 10 个、实际 Optimize 9 个 Godot 进程，以及数值桥不可用的 Debug 普通十角色 1 个进程，共 20 个均退出 0，无 ERROR/WARNING。

| 范围 | 每构建实际结果与证明边界 |
| --- | --- |
| Main / Aiming | 现有完整通道组合与原层姿态夹具通过，保持原比较门槛 |
| Main / Slot | 47610 frames / 19944 poses，RootMotion P/Q/S 差值均 0；retry 和完整通道通过 |
| 完整惯性载荷 | 40320 frames / 11250 poses / 16680 root checks；RootMotion P/Q/S 差值均 0，原生 pose 最大位置差 2.93e-14 cm、四元数／scale 差 0；72 项属性／曲线探针保留 |
| 最终 Rig | 三频／三装备 7560 frames / 7296 poses / 188184 sweeps，retry 通过；这里碰撞为解析夹具，不作为真实复杂地形证明 |
| 当前 ALS 普通场景 | 60Hz / 1700 frames、3 Pivot / 4 dynamic requests，生产入口回归通过 |
| ALS 瞄准与 Grounded | 原瞄准姿态及十 owner single / parallel 运行通过；Grounded 1050 sources / 420 profile blends / 102 interrupted、原零热分配检查通过 |
| 普通 Lyra 十角色 | 每构建 4800 角色发布，实际 Godot 移动、最终 Rig、ALS skin、武器与通知消费者运行通过 |

默认后端的 Debug / Optimize 完整十角色报告相同，并与上一批动画数据 Core 报告相同。Optimize 临时替换的六份 Debug DLL/PDB 按 SHA256 完整恢复。专项夹具标记 `production=false`、`nativeWholeMain=false` 的范围保持，不将它们扩写为完整原生角色等价。

`verify-lyra-managed-yaw.ps1` 在无项目 Godot 进程时，将两份现有数值桥 DLL 临时移到独立证据位置，确认候选路径均不可用后启动新的普通十角色进程；结束后在 finally 按原路径和 SHA256 恢复。运行中的两进程模块列表实际检查分别为 41 / 90 个模块，均没有 `LyraNativeMath.dll`。这次 4800 发布也通过，完整报告与默认后端报告相同，证明当前场景实际走通了 Core 托管回退；没有由此声称所有平台／所有角度与 MSVC 舍入完全相等。

七张 FramePostDraw GPU 截图已查看联系表和步枪蹲姿原图；全身、手枪瞄准、步枪蹲姿、跳跃／落地与反向移动可见，画面仍偏白。近景握持和复杂地形观感继续开放。

最终 `pose-data-inertia-core-v4-audit.json` 通过：14 份冻结源、4615 份其余基线、870 份导出 JSON、710 原 UE 包、9 配置，以及既有原生探针／所引用安装引擎源码保护通过。初始快照捕获已有 dirty 主目录，未重置用户修改。源码／资源保护、两构建和回退报告、六份程序集及两份数值桥恢复均有实际哈希证据。日志、报告和截图以 `artifacts/lyra-analysis/pose-data-inertia-core-v4-*` 为前缀。

## 失败与修正记录

最初提取脚本曾使用构建目录作为相对根，随后保护断言误匹配错误字符串中的 `input.`，均在写入 Core／适配器之前停止，已修正。部分姿态抽取的 v1 编译日志保留，不作为完整本批交付证据。

v2 Core 编译和测试构建失败于项目已有 `GodotAls.Core.Math` 命名空间遮蔽 `System.Math`；已明确使用 `System.Math`，未改算法或误差门槛。失败日志保留为 `pose-data-inertia-core-v2-{build,tests}.log`，v3 的 93 项测试通过后再加入 RootYaw 迁移，最终 v4 为 102 项。

## 尚未关闭

本批迁移范围是上述共用运算和完整惯性载荷，不代表总体目标已完成。仍需完成剩余通用能力审计，以及 ALS 比例下的近景握持、站蹲瞄准、连续起停/Pivot、复杂地形脚部和实际键盘验收。URO、精确 UE 调度与额外 Provider 继续后移。

本次源码检查发现的下一批具体通用入口为 `LyraAimWeightHost.Interp`、`LyraLeftHandLayerHost.BlendLocal`、`LyraMainUpdateHost` 的角度归一化／ClampAngle，以及 `LyraMainObservationHost` 的标准化和旋转矩阵轴。应继续核对并复用现有 Core 插值、姿态、Camera Math 与旋转能力；Lyra 的权重目标、wall 阈值和图更新顺序属于其策略。不能把已有 `BlendTransform` 与不同算术顺序的 `BlendWith` 无条件替换。

本批未启动或修改 UE，没有资产保存／重导，没有提交或推送。现有资源、用户修改和已暂缓项保留。
