# 蹲姿主状态机与姿势内容合同

日期：2026-09-11。第四十六批，工作区 `D:\GodotALS-p5a-events-actions`。
承接第四十五批；没有 commit、revert、UE 资产保存或键鼠控制修改。

## 完整性修复方案

目前并非已经完整照搬 ALS 后只剩调参。Standing/Stop/Detail、方向反馈及部分
来源 Sync/Notify 已接生产；Main 上游、完整蹲姿图、最终曲线反馈和动态上身还未闭环。
按原规划继续，但把 P3/P4 的欠缺作为基础移动补完，不推迟给 Mantle/Ragdoll。

1. 完成 Main/Standing/Crouching 的状态、内容、缓存和来源初始化闭环；共享源时间
   和候选事件，不按动画名合并独立播放器，不用固定延迟代替原生规则。
2. 接最终曲线及其帧序：Weight_Gait、BasePose_CLF、Stride/PlayRate、YawOffset、
   RotationScale；移除仍然重复或近似的计算，核对真实速度与支撑脚世界位移。
3. 完成 P4 的动态 Layering/Add/LS、Lean/IK mask、Aim/Overlay 上身组合及完整脚部
   约束。不能仅靠静态骨骼 mask 修正双臂、侧身或换髋。
4. 继续原 P5A 通用 Notify/State/Sync/ActionPlayer 与 Montage/Slot 事务，P5B Overlay
   玩法/道具/手部 IK，再推进 P5C Mantle/Roll/Root Motion 和 P6 Ragdoll/Get-up/
   Pose Recovery/完整 Camera。音频仍暂缓。
5. 同输入、速度、朝向下比较状态、转换时刻、源时间、曲线、骨骼与支撑脚滑移，
   连续截图并人工验收；最后执行 P7 十角色全质量、30 秒热身、10 分钟 Release。

资产图按本地 ALS V4 AnimBP；与固定 Refactored 基线存在差异的算法分别标明来源。
不把 V4 HipOrientation_Bias 与 Refactored HipsDirectionLock 当成同一个规则。

## 本批实现

- `AlsGroundedMachineCompiler.CompileGrounded` 读取正式 Grounded 导出；旧 `Compile`
  仍要求原 input schema，旧 Main/Standing/Stop 路径不切换。新增 Crouching 配置。
- 主状态机保留原生顺序：Not Moving、Moving、Rotate Left、Rotate Right、Stop。
  12 条边，最多每帧 3 次转换；重入时按现有机器生命周期跳过首次混合。
- Moving 完整权重后松键，以 0.1 秒进入 Stop；读取上一帧 Stop 权重等于 1 后，
  以 0.5 秒 Hermite + QuickFeet 退出。未完整起步时松键走独立 0.2 秒快速停止边。
- Rotate 保留 0.2 秒惯性化请求、左右输入优先级、零时长自动结束和循环越界观察。
  状态通知索引/名称保持，候选重试不修改已提交状态。通用玩法分发本批未接。
- `AlsCrouchingPoseCompiler` 严格验证五个状态的内容连线。Idle 在 Slot 之前写
  FootLock_L/R、Enable_Transition，Slot 后以 RotationScale 缩放 RotationAmount；
  Rotate 以 RotateRate 缩放 RotationAmount，不能复用 Idle 的缩放输入。
- Moving/Stop 引用 `(CLF) Locomotion Cycles`。Stop 保留 source player 46/47 两个
  evaluator，左右 `ik_foot`/`thigh` 深度零分支，Mesh Space Rotation、Override 曲线、
  两个满权重图层及原更新顺序。按物理骨父链编译互不重叠的左右腿 mask。
- 读取实际输入 pin，不误用序列化结构中为零的 CurveValues。输入来源、曲线顺序、
  图层规则、未连接节点、生命周期或编译后播放器归属不支持时明确拒绝。

本批的姿势内容是不可变合同，没有新增完整蹲姿姿势执行器，没有接入 Demo。
QuickFeet 的原生逐骨骼权重函数已在第四十四批完成，本批只复用其机器 profile 标识。
蹲姿 Cycles/方向机器、缓存、惯性化消费、正式事件反馈仍需继续组装。

## 验证

输出目录：`artifacts/test-results/crouching-state/`。

| 检查 | 结果 |
| --- | --- |
| 新蹲姿机器/内容专项 | 36/36（17 机器 + 19 内容） |
| Import 全套 | 942/942 |
| Import 相关 Release | 105/105 |
| Core 常规首轮 | 1862 通过、1 失败，共 1863 |
| Core 常规完整复跑 | 1863/1863 |
| Core 分配用例单独复跑 | 1/1 |
| Core 状态机/缓存 Release | 31/31 |
| Godot Optimize 构建 | 0 错误、0 警告 |
| Standing Controller | 5040 帧；Pivot 5040，Detail 1890，Sprint 1260 |
| Main 内容组件 | 来源 1050 帧、受控混合 420 帧、中断 104 帧、活动求值 0 B |
| 蹲姿来源真实资源 | 420 帧、7140 次姿势采样、30 个事件，六条 Walk 均有运动 |

状态规则在 30/60/120 Hz 测试；包含同时左右输入的原生优先级、转换上限、父上下文
为 0.4 时的局部完整权重、重新进入初始化、相同候选重试和独立工作线程零分配。
这些是原生导出合同与共享执行器测试，不是新导出的完整 UE AnimBP 逐帧轨迹。

首轮 Core 失败为既有 `AlsPoseCacheEvaluationTests.CandidateCopyLifecycleAndRepeatedReadsAllocateNothing`，
期望 0 B、实际 3936 B。保留 `core-full.trx`，单独复跑、完整复跑 `core-repeat.trx`
及相关 Release 均通过；没有改动
该测试、缓存实现或分配阈值，不声称间歇测量差异的根因已解决。

`v4_locomotion_source_graph.json` 与 `v4_grounded_dependencies.json` 未修改，SHA-256
均为 `B0B5DFB8BFDE3059FA25BF9E8685AAC14CF71FD9F0C795C67A46A224AE589EF2`。
本批没有修改/重新构建 UE 插件，没有新增 UE 验证结论；此前的两条普通 Editor
AutomationTest 错误仍未关闭。本批未运行新移动截图或最终性能验收，不宣称视觉改善。

## 下一步

直接实现原生蹲姿 Cycles 与 CLF_Directional States 的规则和内容，再组装主状态的
真实姿势执行/缓存，接入 Main 上游权重及统一 P5 事务。然后继续最终曲线与动态上身。
不要把本批合同编译测试或旧 Standing 回归通过当成这些生产路径已经完成。
