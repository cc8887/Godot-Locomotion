# ALS 曲线与移动功能移植缺口审计

日期：2026-09-09。范围：当前 `feature/p5a-events-actions` 工作区与 `p4_locomotion_demo.tscn`，包括上一轮尚未提交的上身修复。基准提交为 `d6b45e3`。

本轮是诊断与审计，没有修改运行时代码，没有回退上一轮改动，没有保存 UE 资产，也没有新增 commit。此前的上身修复只能算局部改善，不能视为原版 CycleBlending、换髋与起停行为已经移植完成。

## 结论

1. 交错步缺少的是原版方向状态机、曲线门控、转换混合曲线和逐骨骼 BlendProfile，不能用统一角速度限制替代。
2. 起步使用了错误的混合结构：原版是每方向的 `Stride × Walk/Run`，零步幅端有专门姿势；当前是把方向循环按速度混向通用 Idle。
3. 站立步幅曲线、当前混合姿势的 `Weight_Gait`、原始动画时长和实际动画播放相位没有形成原版一致的控制链。
4. 当前 Demo 没有接入已有 P5 Sync/Notify/Action 主运行时。核心测试通过不等于 Demo 已使用这些功能。
5. 先前可视回归检测了错误、直立、移动覆盖与旋转突跳，没有检测支撑脚水平滑移、换髋许可窗口与 UE 转换时序，因此会出现“测试通过但动作明显不对”。

## 证据来源

- 当前导出清单：126 个动画、32 个不同动画浮点曲线名称（包括派生的旋转速度曲线）；28 个独立 Curve 资产登记项。
- 读取本地 ALS V4 的 `ALS_AnimBP`：314 张主图/子图、2496 个 K2/AnimGraph 节点及引脚连接。
- 使用 UE 自带 T3D 导出器只读导出完整站立方向状态图、代表性转换节点和 `ChangeDirection` BlendProfile，补齐普通 K2 查询不能返回的非 K2 状态节点。
- 实际采样 `StrideBlend_N_Walk`、`StrideBlend_N_Run`、`DiagonalScaleAmount`、`ChangeDirection`；读取 AnimBP 默认值。
- 对照本地 ALS-Refactored `Source/ALS/Private/AlsAnimationInstance.cpp` 的速度混合、步幅、步态和髋部曲线逻辑。V4 的 `HipOrientation_Bias` 与 Refactored 的 `HipsDirectionLock` 不应只改名字就当成相同规则。
- 重读上一轮 `artifacts/upper-final-strafe/frames.json` 的起步逐帧数据。

UE 读取遵循 `ue-diagnosing-plugin-build-load`：完整 Editor target 构建及插件审计通过，再运行只读 commandlet。最终输出 `ALS_GRAPH_AUDIT_OK graphs=314 nodes=2496 unreadable=0 assets_saved=0`，退出码 0。没有修改插件源码。

原始材料在 `artifacts/locomotion-port-audit/`：`graphs.json`、`standing-directional-states.t3d`、`transitions.json`、`curves.json`、`defaults.json`、`ChangeDirection-profile.t3d`。只读诊断脚本为 `artifacts/audit_locomotion_graph.py`。这些诊断文件被 Git 忽略，不是正式资源导出链的一部分。

## 交错步与换向

原版站立 CycleBlending 有 `Move F/B/LF/LB/RF/RB` 状态，每个状态按四向 VelocityBlend 组合不同的循环。例如 Move RF 使用 F、B、LB、RF，Move LF 使用 F、B、LF、RB；它不是简单按照当前左右速度选择 LF 或 RF。

直接读取转换 `AnimStateTransitionNode_14`，对应 Move LB 到 Move LF：

```text
abs(HipOrientation_Bias) < 0.5
AND StateWeight(Move LB) == 1
AND Feet_Crossing == 0

CrossfadeDuration = 0.75 seconds
BlendMode = Custom
CustomBlendCurve = ChangeDirection
BlendProfile = ALS_Mannequin_Skeleton:ChangeDirection
```

Move RB 到 Move RF 有对应规则；另有带正负 0.5 偏向阈值的转换。部分普通方向转换使用 0.5 或 0.7 秒 Cubic 混合。这些是各类转换的配置，不是“按 A/D 后一律等待 0.75 秒”。

`ChangeDirection` 的归一化时间采样：0.25 时权重约 0.08，0.5 时为 0.5，0.75 时约 0.92。过渡前段较缓，加上交错步许可窗口，会产生用户描述的换向等待与随后换髋的观感。BlendProfile 还为两条腿及脚部 IK 链的 15 个骨骼配置 `BlendScale=2`，不是全身使用同一混合权重。

当前 `AlsLocomotionAnimationController.cs` 的 `HipVariantBlendSeconds=0.2`、70/110 度半区保持和 10 rad/s 方向限速都没有读取上述曲线，也没有原版六状态转换。它们限制了突跳，但没有移植这套行为。

`Feet_Position` 也不能直接当作 `Feet_Crossing` 使用：在 V4 中，它参与 Stop 状态中的 Foot Down/Foot Up、Lock/Plant Left/Right Foot 分支，以及跳跃左右脚分支。

## 起步滑步

### 已确认的结构差异

原始 `ALS_N_WalkRun_F` 的采样：

| Stride | Walk/Run | 动画 |
| ---: | ---: | --- |
| 0 | 0 | ALS_N_WalkPose_F |
| 1 | 0 | ALS_N_Walk_F |
| 0 | 1 | ALS_N_RunPose_F |
| 1 | 1 | ALS_N_Run_F |

其他方向也有对应的 WalkRun 混合空间。数据已在清单中，但当前 `AlsLocomotionGraphBuilder.cs:854` 为整个站立图插入的是一个通用 Idle 中心，没有建立这些二维混合空间。

当前 Core 的 `Stride=clamp(speed/referenceSpeed,0,1)` 也不是源曲线。实际 V4 采样如下，速度单位 cm/s：

| 速度 | Walk Stride | Run Stride |
| ---: | ---: | ---: |
| 0 | 0.20 | 0.20 |
| 50 | 0.45 | 0.22 |
| 100 | 0.90 | 0.28 |
| 150 | 1.00 | 0.40 |
| 175 | 1.00 | 0.48 |
| 350 | 1.00 | 1.00 |

V4 `CalculateStrideBlend` 用 `Weight_Gait` 混合 Walk/Run 曲线，并通过 `BasePose_CLF` 处理蹲姿。`CalculateStandingPlayRate` 也依据当前混合姿势的 `Weight_Gait`，再除以 StrideBlend 和组件缩放。当前代码按离散 ActualGait 计算，没有消费这两个姿势曲线。

### 已有回放中的量化证据

固定镜头横移起步 frame 61 到 65：速度从 0.33 增至 1.67 m/s，Stride 从 0.19 增至 0.95；左脚骨骼世界高度约 0.13 m，连续五帧的水平位移分别为 10.95、10.55、10.48、10.61、10.39 cm。frame 67 后同一低位脚的单帧水平位移降低到约 0.26 至 0.46 cm。

这证明起步阶段存在明显的低位脚横向滑移。它与低速下从通用 Idle 快速拉开到完整侧向循环的结构问题一致。不能用“脚部旋转小于 30 度”判定其已经正确。

### 仍需针对性验证的时序风险

所有基础片段目前统一拉伸到 1 秒时间轴，但 V4 的 Walk 片段约 1.133 秒，Run F/LF/RF 为 0.8 秒，Run B/LB/RB 约 0.667 秒。Core 相位按 `deltaTime * PlayRate` 推进；控制器同时设置每帧 Seek 和 `PlayRate * Stride` 的 TimeScale。应统一“源片段秒数、归一化相位、Leader/Marker 映射、实际最终采样时间”，不能仅凭公式断言 TimeScale 的效果，也不能把它当作已经完成 UE 的播放速率匹配。

V4 默认 AnimatedWalk/Run/Sprint/CrouchSpeed 为 150/350/600/150 cm/s，与现有设置换算后相符。问题不能简单归因于这四个数值抄错。

## 曲线与功能缺口

“有资源”只代表资产或关键帧存在；“有 Core”代表可调用模型或测试存在；两者都不等于当前 Demo 已消费。

| 组 | 当前状态 | 未完成部分 |
| --- | --- | --- |
| Feet_Crossing、HipOrientation_Bias | 动画关键帧已导出，Demo 未消费 | 换髋许可窗口、偏向与六方向状态转换 |
| ChangeDirection 曲线和 BlendProfile | 独立 Curve 只有登记；本轮仅做诊断读取 | 正式导出实际曲线/逐骨骼配置，0.75 秒自定义转换 |
| Feet_Position | 已导出，Demo 未消费 | Stop 的 Foot Up/Down、Lock/Plant 与跳跃脚选择 |
| StrideBlend_N_Walk/Run、WalkPose/RunPose | 资产/混合空间结构已登记，当前图没用 | 六方向 Stride × Walk/Run，曲线驱动步幅 |
| Weight_Gait、BasePose_N/CLF | 已导出，Demo 未消费 | 按实际混合姿势计算步态权重与播放速率 |
| VelocityBlend、DiagonalScaleAmount | 当前用原始局部速度和方向映射替代 | F/B/L/R 独立权重、原版插值和对角补偿 |
| YawOffset_FB/LR、图内 YawOffset | 上轮只读取得部分曲线；当前角色目标朝向没有接入偏移反馈 | V4 各方向姿势写入 YawOffset，再反馈角色朝向；Refactored 对应命名为 RotationYawOffset |
| Layering_Head/Spine/Pelvis/Legs/Arm/Hand 及 Add/LS 曲线 | 已导出；静态骨骼 mask 和 Aim 局部实现存在 | 动态曲线权重、加法层、局部/网格空间选择及 Overlay 组合 |
| Enable_SpineRotation、Enable_HandIK_L/R、HipOrientation_Bias | 已导出，Demo 未按源图消费 | 武器/Overlay 的脊柱、手部 IK、髋部偏向 |
| Mask_Lean、Mask_Sprint、Mask_LandPrediction | 已导出，Demo 未消费 | 按姿势屏蔽 Lean、冲刺分支和落地预测 |
| Enable_FootIK_L/R、Weight_InAir 等图内曲线 | UE 图会读写；不能仅依赖动画文件曲线清单 | 图内 ModifyCurve、最终曲线混合和消费者；当前 IK 用状态默认值 |
| FootLock_L/R、RotationAmount 派生旋转速度 | 已有实际消费与局部功能 | 不代表完整 Stop/Pivot 或全部曲线策略已经移植 |
| Enable_Transition、Sync、Notify、ActionPlayer | P5 Core/导入绑定及测试存在 | 当前 P4 Demo 的 Worker 主循环没有调用 P5 运行时 |
| Not Moving / Moving / Stop / Pivot | 当前只有简化 Grounded 与 Idle 中心 | 独立起停状态、ShouldMove、支撑脚选择和 Pivot |
| LandPrediction、Mantle、Ragdoll/Get-up/Pose Recovery | 当前 Demo 无完整行为链 | 后续模块，不能用契约字段或现有动画资源代表已完成 |
| Mask_FootstepSound 与音频 | 不在本轮优先级 | 用户已明确音频可暂缓，不作为当前阻塞项 |

28 条独立 Curve 资产的清单记录主要是 objectPath、assetClass、registry metadata，缺少可由 Godot 正式加载的数值曲线负载。P3 已抽取的部分设置参数不等于这些独立曲线全部移植完成。动态生成的曲线还需要审计图中 ModifyCurve 节点，不能只补 FBX 导出。

## 当前 Demo 接线核对

`P4LocomotionDemo.cs:142` 建立 `AlsP3RuntimeContext`，由 `AlsP3WorkerRoot` 运行 LocomotionModel、P4 View/Turn/FootPlacement 和 `AlsLocomotionAnimationController.ApplyPrepared`。Godot 层对 P5 的引用集中在资源库、绑定类和绑定烟测，没有调用 `AlsP5Runtime.TryPrepare` 或将其 Sync 映射用于这个 Demo 的最终基础姿势采样。

P5 功能不应标成“完全没有实现”，但必须标成“当前人工验证场景未接入”。单纯加载曲线或通过绑定 digest 检查，也不能验证实际交错步同步。

## 建议实施顺序

1. 先建立红色验收用例：不同起始相位的 A/D 反向、前后髋偏向、不同 Overlay、30/60/120 Hz；记录许可窗口、状态转换、支撑脚水平滑移和源片段时间。保留现有错误与旋转门禁，但不再作为动作正确性的唯一标准。
2. 补正式数据：独立浮点/向量曲线的关键帧、插值和边界；ChangeDirection BlendProfile；六套 WalkRun 混合空间及其 Pose 端；图内曲线默认与合成规则。区分 V4 与 Refactored 的语义来源。
3. 建立实际使用的曲线/播放反馈链，把已有 P5 同步能力接到 Demo 的采样与事务流程，明确采样时序和回滚所有权。
4. 重建基础移动图：四向 VelocityBlend、每方向 Stride × WalkRun、Weight_Gait/播放速率；替换通用 Idle 缩幅，验证起步滑移。
5. 重建 CycleBlending 六状态与原版转换：Feet_Crossing、HipOrientation_Bias、ChangeDirection 及逐骨骼 BlendProfile。用原版行为替换上一轮的半区偏移与统一角速度策略，不继续把临时策略叠加上去。
6. 补 Stop/Pivot、Feet_Position 分支与动态分层，再推进 Overlay、动作和其他后续模块。镜头和输入保持现有已验证行为，除非新证据明确指向它们。

该顺序需要实施时评估 Core 状态、播放成员数量和图布局契约，不能为了保留旧 digest 而继续限制正确的混合结构。本轮未实施上述重构，也未重新宣称 Demo 动作验收通过。
