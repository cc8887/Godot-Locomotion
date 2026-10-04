# Lyra Main LocomotionSM 在 ALS81 上的连续原生对照

2026-10-01。直接在当前主目录实施；安装版 UE 5.8.1、Godot 4.7.2 mono。按用户要求沿用 ALS 模型/骨架路线，不展开 5.8/5.9 差异。**本批关闭固定 Provider 的 Main LocomotionSM 与十根共同执行对照；完整 Main 后续层、生产入口及整个移植目标仍未关闭。**

## 实现与原生边界

新增独立 `UAlsLyraMainLocomotionLibrary`，保持以前的原生采集类不变。临时 Character/Controller/Main/Linked 实例执行实际 Main thread-safe 更新、PropertyAccess、原 LocomotionSM、真实十个状态根及一次原生 `FAnimSync::TickAssetPlayerInstances`。三个 Provider 分别固定 Unarmed/Pistol/Rifle；采集器不喂入状态、权重、相关源或 Sync 结果。Tap 只转发原根 Initialize/Cache/Update/Evaluate，并记录原机器实际求值的根，不额外求值未选中的根。

194 个绝对序列与三个 Lean 序列使用已有 ALS 逻辑 81 骨库存；重建临时动画后校验长度、RateScale、Marker 和压缩根数据与不可变资源清单一致。实例注册先使用临时 Manny 网格载体，再将两个 RequiredBones 绑定 ALS81 并失效骨骼缓存；这是动画求值对照，不是网格蒙皮或渲染验收。目标骨名遮罩、spine_04/05→spine_03 去重等继续沿用已有适配。没有保存 UE 内容资产。

原生输出边界是 `LocomotionSM`，没有执行其后的上身、动作槽、最终惯性化和足部控制。采集器将这一边界的真实混合曲线发布给 Main，再调用真实 `Layer->CopyCurveValues(*Main)`；因此反馈只代表该明确边界。位置、旋转、速度、加速度与停止预测输入来自受控物理输入及真实 UObject 读回，没有运行 CharacterMovement 物理模拟。

Godot `LyraMainLocomotionHost` 独立计算完整宏、规则输入、相关源、双缓冲组有效性、初始化与权重清除、机器栈、根遍历、共同 Sync 及混合姿态。新 native smoke 的 UE 行只用于断言，不作为规则/状态/时钟输入。候选新增保留宏结束时的快照，供原生断言；仍由统一角色事务提交。

## 本批修正

1. UE `SkeletalMeshComponent.cpp:3291–3298` 在最终求值后将 Main 曲线复制给全部 Linked 实例；`AnimInstance.cpp:1784` 实际复制曲线映射。共享宿主的 Idle 现在按 enclosing Main 最终反馈提交 `TurnYawWeight`，包括 Idle 未访问的帧。独立 Idle 仍以自身最终输出为边界；取消丢弃 staged copy。
2. UE OrientationWarping 的 `UpdateInternal` 在跳过 Evaluate 时仍同步访问计数。Start/Cycle/Pivot 宿主现在候选化更新该计数与必要重置，并在 update-only 提交；角度、方向、根旋转和 Stride 求值历史仍由实际 Evaluate 更新。Cycle 的绑定/非绑定两条 Prepare 路径都接入。取消保持原历史。
3. 显式 Main Initialize 在原生规则观察前重置机器。自主规则现在在该情况下观察初始 Idle 的无相关源及 elapsed=0，而不读取旧 Cycle/Start 的相关源。

## 最终结果

三个 Provider × 30/60/120Hz × 移动/转身两类轨迹，共18条连续轨迹：

| 项目 | 最终结果 |
| --- | --- |
| 连续帧 / 状态 / 根 | 11,340 / 10 / 10 |
| 实际混合姿态 | 9,762，790,722 骨 |
| 实际根姿态 | 12,595，1,020,195 骨 |
| 转换 / 活跃序列时钟观察 | 246 / 24,318 |
| update-only / 隐藏 | 1,578 / 165 |
| Linked 反馈变化 / 原生轨迹取消重试 | 396 / 207 |
| 混合位置最大差 | 1.0812455139964926e-13 cm |
| 根位置最大差 | 1.0869213824647059e-13 cm |
| quaternion / scale 最大差 | 9.961119953842058e-16 / 3.8459253727671276e-16 |

原姿态门槛 1e-8 cm、quaternion 1e-10、scale 1e-12 保持；曲线值/存在性/Flags、整数属性与 typed RootMotion 同时检查。Main 宏、完整规则输入、状态、elapsed、初始化、当前/上一权重、栈参数、更新上下文、相关源与 Sync 有效性均对照。活跃源内部时间、previous/delta 及 Marker 索引精确检查；非零 Marker 距离逐位比较。**该原生 JSON 数值编码将 +0/-0 都写作 0，无法验证 Marker 距离零值的符号位；不将这部分描述为逐位验收。**没有扩大任何非零误差门槛。

两个独立 UE 进程均退出0；fresh trace 完整 JSON 语义相同，已有 v1 fixture 不重写。508 个原包及此前643个 JSON 的 SHA256 保持；新增两个 ignored fixture（requests/native），其中 native 为497,131,711字节。Debug 与 ExportRelease Optimize 最终均0警告0错误。Core Orientation 10项通过。

修正后自主宿主 11,340 帧的取消重试与角色隔离回归通过；移动 relevant 观察由5476变5458，因为18次显式初始化正确读取初始 Idle。独立 Main Idle 2520帧保持通过。最终统一回归包含旧四根原生3780帧/8400姿态、Air3780帧/7650姿态、两套Idle各26460帧、十根Scope3780帧/11064姿态与Main反馈次序；所有 Godot 最终日志无 ERROR/WARNING。

## 失败、警告与证据

- 首次 UE 退出3：替换骨架后旧缓存索引仍被 Warp 使用，引发 BitArray 断言；骨骼缓存失效后修复。保留 `main-als-native-ue-export-first.log`。
- 首次 C# 编译误把 class transition 当 Nullable struct 使用 `HasValue`，已修；保留 `main-als-native-debug-first.log`。
- 首次整链姿态在 Start frame15发现 quaternion 差0.07755，定位为 skipped-Evaluate 访问计数；随后 frame132定位显式初始化相关源观察；转身 frame36发现 JSON 零符号信息缺失。保留 `main-als-native-godot-first/roots/warp/initialization.log`。
- Warp 修正过程中自主夹具曾报 `Main-own/unarmed/30/0/unevaluated Cycle warp advanced`，发现绑定 Cycle Prepare 尚未接计数候选，已补齐并最终复跑。这一错误来自本次工具输出；最终回归日志记录修复后结果。
- 两次成功 UE commandlet 都报告0错误、3020警告。实际警告分类：1647条临时序列 ConditionalPostLoad dependency、1368条原资产 GameplayTag、4条 commandlet Editor unavailable、2条旧映射注册、1条既有连接插件许可提示。没有将 UE 报为零警告；临时依赖警告与本次重建有关，原项目配置未改动。

主要证据在 `artifacts/lyra-analysis/`：`main-als-native-ue-build-cache.log`、`main-als-native-ue-export-cache.log`、`main-als-native-ue-export-repeat.log`、`main-als-native-godot-final.log`、`main-als-native-debug-final.log`、`main-als-native-optimize-final.log`、`main-als-native-core-regression.log`、`main-als-native-orientation.trx`、`main-linked-feedback-own-regression-final.log`、`main-linked-feedback-idle-regression.log`、`main-linked-feedback-scope-regression-final.log`、`main-als-native-verification.log`。`tools/verify_lyra_main_als_native.py` 核对资产、fixture、采集源码/构建镜像和最终日志。

## 剩余工作

保持 Main 之后实际14入口的 typed 生产 Layer 调度、换类生命周期/Linked BlendIn-Out、统一 Notify/事件驱动 Pivot、Montage 与 additive、原上身后的惯性、FootPlacement/LegIK、普通 Demo gather/骨架发布、多角色、渲染和性能验收开放。当前三个固定 Provider 的成功对照不代表完整换装备或整幅 Main AnimGraph 验收。用户修改和此前暂缓项保留；没有提交或推送。
