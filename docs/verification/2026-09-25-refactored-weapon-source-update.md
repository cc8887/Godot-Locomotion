# 四武器状态源更新与时钟对照

## 实现

在主目录 `.` 的 `main` 新增 `AlsRefactoredWeaponSourceProfile/Runtime`，将原始三状态图编译为独立更新树，连接已完成的状态机与共享 SourcePlayer/Sync batch。未使用旧 V4 武器图代替。

- 按完整嵌套图路径解析 authored pose links，与 compiled property links、节点类型和状态归属交叉校验；只支持实际图中的 StateResult、TwoWay、MultiWay、local/mesh additive、SequencePlayer/Evaluator。拒绝未消费节点、跨状态连接、非树结构和未支持回调。
- 读取实际属性绑定：gait 累进量、站立/蹲伏、空中、落地预测、方向权重和冲刺加速。固定 additive Alpha 按原值传递；MultiWay 归一化，TwoWay 保留阈值边界单侧全权重。
- 复用 Core `AlsOverlayPoseWeights` 处理范围映射、插值和最终夹紧，每个节点独立保存插值历史和前帧子级相关性。Rifle 的 20/5、0/5、0/10 等插值以及冲刺加速 0..0.25 范围映射、20/.5 插值不被静态权重替代。
- 消费状态机原始 Entry/Update 顺序。状态初始化递归重置节点历史，播放器初始化请求可跨隐藏帧保留；`bResetChildOnActivation` 为真的冲刺分支在重新相关时初始化对应子树。
- 生成原始 rate、显式 host player ID、实际有效权重与 reset 标志，交由共享 SourcePlayer batch 推进；本更新器不拥有第二套播放时钟。
- 独立 committed/candidate 数组，取消不推进插值/初始化历史；绑定唯一机器 owner（包括拒绝同 profile 的另一个 runtime），帧/profile/输入异常拒绝。生产宿主仍须统一预检并提交机器、源更新与播放器。

## 原生验证

扩展既有 UE 导出器的可选输入，允许递归设置 `GroundedState.VelocityBlend`，补充 `StandingState.SprintAccelerationAmount`。原始模式不改变。新增四份变化输入轨迹，包含方向切换、Running/Sprinting/InAir/Prediction 变化、加速冲刺分支以及独立三阶段手臂覆盖段。

最终新轨迹 3,684 帧，既有轨迹 2,424 帧，四武器 × 三频率 × 两组共 **6,108 帧**。C# 与 UE 每帧比较实际更新的原 SequencePlayer 权重和推进后的时间；不把未更新播放器保留的旧 cached weight 当成零。

- 共 11,382 次 source update，产生 474 次 reset 请求；播放时钟最大差 **0**，权重最大差 **1.1920929e-7**，预算保持 `2e-6`。
- 变化输入在每个频率都覆盖并初始化所有 player：前三武器各 3 个，Rifle 全 6 个。
- 周期性在源 batch 完成后取消、重试，输入/reset 标志/播放器时钟完全一致。
- 新测试 **25 项**（24 原生对照 + owner/非法输入事务）通过；相关 Import **91 项**通过；Godot Optimize 构建 **0 警告/0 错误**。本批未单独运行 Core 全量。
- 首轮扩展轨迹数值对照全部通过，但加强覆盖断言后 Rifle/30 Hz 只有五个 player 被更新，91 项中 1 失败；第一次补三阶段输入仍失败。原因是测试误把 gait 量当成互斥分类，将 `GaitWalkingAmount` 设为 0，关闭了整条移动分支。按原图累进语义改为 Walking=1、Running=1，再控制 Sprinting/Acceleration，全部覆盖并通过。没有降低覆盖要求或放宽误差预算。首轮数据/失败 TRX 保留。

产物目录 `artifacts/refactored-weapon-source-update/`，最终参考来自 `cold-complete/`，与 `editor-complete/` 完全一致。之前 `cold/`、`cold-final/` 只作失败过程证据。

| 资源后缀（refactored_weapon_source_trace_） | 字节 | SHA256 |
|---|---:|---|
| Bow.json | 15411259 | CDE42A9A9E3A6F574080BF0B9961FE7CBD79DA1BB9CB2AF566BF8E27D02A4181 |
| PistolOneHanded.json | 15131109 | C36ED3317088D935A7893D0428F25650959563A4A6F807FCBA865E5494A6D1B4 |
| PistolTwoHanded.json | 15196503 | 9F60EDB29EE53F608B7E488A843ED2F46FC7B855FB4A10D7B3B335886F2F4748 |
| Rifle.json | 15234408 | BEEFF045B26307E4BAE8D2FF2433979B734C401FFB403771B9F6DE5ECCDA43B9 |

## UE 构建与边界

按 `ue-diagnosing-plugin-build-load` 技能完整构建项目 Editor 目标，4 actions，全部项目插件审计通过；fingerprint `68FD4C514E49D886A1B9C8C4B4AFE6ACB800354460881A560948D4D96AF2D95C`，BuildId 仍为 `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`。最终冷启动退出 0，普通 Editor PID 38880 导出并退出 0，四文件逐字节相同。DataValidation 退出 0，0 errors / 3 既有 warnings。普通 Editor 两条既有 Condition failed 和五类警告仍保留。未保存 UE 资产、未打包。

旧模式重新冷导出退出 0，四份原 `refactored_weapon_trace_*` 文件全部字节不变。

本批只完成状态内部的 source update/Sync 输入。原生也导出了 full pose/curves，但 C# 尚未求值或比较这些姿态。外层 Action 隐藏/恢复、完整状态 pose 合成、QuickFeet 每骨过渡混合和统一宿主消费仍待接。Rifle Movement 组在该独立原生 linked 图中没有真实 Locomotion 领头，不能将本批视为整角色移动同步验证。

Overlay 完整姿态仍 9/13，普通 Demo 未切到 Refactored 整链。本批无 Godot 场景/人工视觉、最终性能或全量验收。真实 Locomotion/统一角色宿主、完整 Turn/DynamicTransitions 调用端、Ragdoll/Flail/Get-up/Pose Recovery、相机复杂边界、十分钟性能预算等旧缺口不变；音频、道具物理和头颈诊断继续暂缓。用户未提交文件未纳入本批。
