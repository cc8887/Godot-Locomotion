# Cycle 缓存更新权重与事件上下文

日期：2026-09-11。第三十八批，继续完整性补完；未 commit、revert 或覆盖既有修改。

## 实际缺口与修正

实际 Cycle 此前将多个状态对同一方向姿势的贡献相加，直接作为来源更新权重。
但 UE SaveCachedPose 的延后更新选择最高权重的调用上下文，相等时保留先到者。
姿势混合需要累计贡献，不代表来源时钟、Sync Leader 选择和通知筛选也使用累计值。

- 新增 AlsCycleCacheWeights：按过渡遍历顺序访问每个状态一次，方向缓存选择最大
  更新权重，并保留获胜状态身份和 ActiveContext。相同权重保持首次访问者。
- 实际 AlsStandingCycleGraph 使用该结果生成来源更新及通知上下文；Sprint/Forward
  子分支继续使用自身的更新权重与活跃状态。姿势和曲线混合仍保留累计贡献。
- 局部 MultiWayBlend 权重相关、但状态全局贡献为零或很小时仍保留更新。
  局部总权重不超过阈值时不更新，不沿用仅用于姿势采样的 Forward 回退。
- Import 核对六个方向状态内实际缓存输入、VelocityBlend 分量、归一化和非 Additive
  设置，图连线漂移会拒绝编译，不能静默落到硬编码的另一张图。
- 实际资源测试独立计算最高权重与累计贡献，验证二者确有不同，以及实际 Tick
  权重、状态活跃标志、来源时间和候选回滚。视觉采集增加逐帧通知上下文及斜向输入。

源码依据为本机 UE 的 AnimNode_SaveCachedPose.cpp PostGraphUpdate、
AnimNode_StateMachine.cpp UpdateTransitionStandardBlend/UpdateState，以及
AnimNode_MultiWayBlend.cpp UpdateCachedAlphas/Update_AnyThread。
本批只读 UE 源码，未新增 UE 原生导出、插件修改、构建或启动验收。

边界：这是当前六方向 Cycle 的实际更新修正，不是全图缓存调度的完整接入。
Main/Slot 上下文、全图缓存顺序、祖先状态链和 Root Motion 上下文仍未贯通。
完整 Standing/Detail/Stop 仍未接入 Demo，外层临时 MoveToward 混合未移除。

## 验证

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema 套件 | 1835/1835 |
| Import 全套 Debug | 815/815 |
| Core 缓存/来源事件/Sync 相关 Release | 110/110 |
| Import 来源/缓存相关 Release | 71/71 |
| Godot 构建 | 0 警告、0 错误 |
| 实际 Standing Cycle | 30/60/120 Hz，17337 次来源时间检查，零分配与回滚通过 |
| 实际 Sprint 分支 | 1260 帧、9 次回滚通过 |
| Detail / GroundedCache | 真实资源组件通过，仍为 demo=not_connected |
| Cycle 单线程/多线程 | 各 180 帧、9 个来源事件，摘要一致 |
| late_source_event / late_transaction | 两种模式均通过 |

140 个过渡中断/重入案例与已有延后缓存遍历器对照；它不是新增 UE 整图原生证据。
首轮专项 42 通过、2 失败，原因是测试误用方向枚举下标，修正为对应枚举后 44/44。
随后补总权重边界测试，最终全套包含这些案例。保留首轮 TRX，未放宽断言。
证据位于 artifacts/test-results/cycle-cache/ 及 artifacts/cycle-cache-*.log。

结果摘要为 87102148085DD874；来源事件上下文改变了结果摘要。
full_pose=4BE93FAC6C035F8C、root=309E8D0E0BEEB2CB 保持不变。
这不证明姿势已改善，也不代表完整移动等价。相机和输入文件 SHA256 未变。

## 连续回放

实际渲染各 720 帧、120 张截图，三组通过现有动作守卫：

| 有效回放目录，均在 artifacts 下 | 最大单帧脚旋转 | 说明 |
| --- | --- | --- |
| cycle-cache-strafe-visual | 9.987 度 | 换髋开始于 291/487 帧，Feet_Crossing 均为零 |
| cycle-cache-sprint-visual | 18.884 度 | 240 个冲刺帧、52 个分支混合帧 |
| cycle-cache-diagonal-flat-visual | 8.965 度 | 1276 个来源通知 Tick，其中 88 个非活跃上下文 |

已抽查横移 288/294/306、冲刺 132/240、最终斜向 240/252/276 帧图像：
角色可见且运动，最终斜向无重叠地面的闪烁条纹。斜向覆盖普通方向过渡，未触发
换髋门控分支；不得用它代替横移换髋验收。上身和滑步整体仍未通过最终人工验收。

首轮 cycle-cache-diagonal-visual 中扩大地面与原演示地形重叠，存在闪烁条纹，
保留该记录但不作为最终平地对照。仅在斜向测试实例隐藏其他地形并关闭其碰撞后
重新采集；正式 Demo 场景未修改。两次不同地形的数值差异不作为算法改善证据。
采图中的帧率不用于十分钟 Release 性能预算验收。

## 后续顺序

1. 优先把 Main/Slot、完整 Standing/Detail/Stop/Pivot 接入 Controller/Worker，
   移除外层临时混合；已有组件验证不能继续代替生产接线。
2. 将实际相关来源统一纳入时钟、缓存、状态事件和唯一 P5 候选提交事务。
3. 接最终曲线顺序与动态 Layering/Add/LS、Lean、YawOffset、手部 IK 和 Overlay。
4. 按原规划继续动作、Root Motion、Ragdoll/Get-up 等后续阶段，音频仍暂缓。
5. 用同输入 UE/Godot 状态、来源时间、曲线与骨骼轨迹对照，加连续截图、人工验收
   和最终十分钟性能预算，才关闭完整移动与上身项。
