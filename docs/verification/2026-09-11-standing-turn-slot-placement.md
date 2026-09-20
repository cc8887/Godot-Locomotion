# Standing Turn Slot 实际位置修正

日期：2026-09-11。工作区：`D:\GodotALS-p5a-events-actions`。
完整性修复第四十二批。未完成完整 Main/Slot，也未关闭基础移动视觉验收。

## 源图差异与本批实现

核对 Main 上游时确认，V4 的 `(N) Turn/Rotate` Slot 在 `(N) Not Moving` 内部：

```text
Idle evaluator -> FootLock/Enable_Transition overrides -> Turn/Rotate Slot
               -> RotationAmount scale -> Not Moving state output
```

旧 Controller 把 Turn 放在整个 Standing/Lean 输出之外，导致它在起步过渡中
仍能直接覆盖 Moving/Stop 姿势，并统一衰减它们的来源事件权重。本批修正：

1. 当前 Turn 双银行输出进入实际 Not Moving 状态，在 Standing 过渡栈之前组合。
   使用现有绑定和秒数，不增加播放时钟；本批没有把双银行改称通用 Montage 实现。
2. 动画局部姿势按 Turn A、Turn B、Idle source 的顺序加权，归一化一次；曲线
   使用同一 Slot 权重。仅非加法 Turn 输入受支持，资源不满足时明确拒绝。
3. Slot 前的 FootLock_L、FootLock_R、Enable_Transition 值读取实际源图输入引脚，
   不使用节点结构中仍为零的默认数组。新增编译器验证 Slot 名称、位置、更新策略、
   前后曲线节点和连接，拒绝动态或不支持的输入。
4. Standing 生效时外层旧 ActionBlend 姿势与曲线覆盖为零，包括回滚恢复路径；
   保留原 Turn 通道候选状态，使淡出和现有选择逻辑继续工作。
5. Cycle/Detail 来源通知不再因 Idle Slot 而被全局乘上 `1 - TurnAmount`。
   Slot 只影响自己的 Idle 来源，不应衰减 Moving 来源的通知。
6. Turn 独有曲线加入 Standing 曲线集合；Slot 候选输入加入 Worker 回滚检查。
   截图记录新增秒数、通道权重及经过 Not Moving 状态权重后的实际 Slot 贡献。

原生参考为本机 `AnimNode_Slot.cpp`、`AnimInstanceProxy.cpp` 的 SlotEvaluatePose。
没有新增 UE 原生完整 Slot 探针，没有修改或重新导出 UE 资产。

## 已知边界

- 通用多 Montage、加法/Mesh Space Slot、蒙太奇曲线覆盖、骨骼 BlendProfile、
  正式 occurrence/事件与源更新上下文仍未接通。当前桥接消除了放置位置错误，
  不代表这些通用语义完成。
- `RotationScale` 源连线已验证，但还没有接入实际值和最终 `RotationAmount`
  角色反馈。Turn 选择、时间、淡入淡出和 yaw 积分仍来自旧 P4 通道，需要继续迁移。
- Idle 当前为不推进时间、不产生来源通知的 teleport evaluator；本批没有宣称
  完成通用 Slot 的 AlwaysUpdateSourcePose、缓存生命周期和惯性化请求转发。
- Main Grounded/Main Movement、Grounded Slot/BaseLayer Slot、跨站姿完整混合、
  最终曲线及动态上身仍是下一阶段，不以本批局部接线替代完整外层图。

## 失败与修正

首次连续转身在第 666 帧被参数校验拒绝。旧接口字段名为 Phase，但合同实际是秒；
新采样器误作归一化时间并乘以动画时长。已改名为 TimeASeconds/TimeBSeconds，
直接采样秒数，按各资产真实时长校验。新增 Controller 测试选最长站立转身动作，
在超过一秒的位置采样，防止只测短相位而遗漏此错误。

失败日志和已有截图保留于 `artifacts/standing-slot-walk.log`、
`artifacts/standing-slot-walk/`；未放宽 30 度旋转守卫，未改 oracle、源数据或容差。

## 验证

- Godot 构建：0 警告、0 错误。
- Import 最终 829/829，新增 Slot 编译器 6 个专项测试。
- Core 常规复跑 1848/1848，排除独立 P5A golden/schema 套件；结果见
  `artifacts/test-results/standing-slot/core.trx`。
- Controller Slot：30/60/120 Hz、三种既有初始相位，共 1890 帧。
  306 个完全 Moving 正例验证 Turn 不影响姿势/脚锁曲线/来源事件；621 个 Idle
  正例验证 Slot 真实参与姿势。候选应用、回滚、替换输入和相同重试通过。
- Standing/Pivot/Detail/Sprint 既有生产测试继续通过，Cycle 热路径分配守卫通过。
  本批没有单独宣称活跃 Turn Slot 的零分配基准完成。
- Worker 单/多线程各 180 帧，摘要与上一批相同。两种模式第 25 帧来源事件晚期
  失败无回调泄漏，第 13 帧整帧晚期失败状态/控制器/姿势回滚通过。
- P4 全脚本通过：图、姿势、地形/移动平台、双模式回滚和既有零分配检查。

日志：`artifacts/standing-slot-*.log`；测试：`artifacts/test-results/standing-slot/`。
短 Worker 回放没有活跃长 Turn，不能把其未变摘要当作 Slot 视觉验证。

## 连续回放

| 场景 | 帧/截图 | 结果 |
| --- | --- | --- |
| standing-slot-walk-final | 720/120 | 横移后转身，101 帧 Turn，最大脚旋转 10.743 度 |
| standing-slot-turn-start | 720/120 | 转身中起步，12 帧 Slot 淡出与移动重叠，最大脚旋转 10.743 度 |

第二组第 646 帧起步时通道权重约 .9167，实际 Slot 姿势权重约 .91；第 651 帧
约为 .5/.37；第 658 帧归零。它已随 Not Moving 过渡退出，不再单独覆盖 Moving。
已查看第一组 660/672 和第二组 648/660 截图，均为实际角色和场景输出。

步行起步低脚位移代理仍约 7.9046 cm/帧，StartupAccepted=false；不是精确支撑脚
滑移，也没有改善。上身、交错步与最终人工验收保持未完成。

## 下一步

继续完整 Main、其内容状态与真实上游缓存权重；将 Slot 接到正式 P5 动作身份和
统一事务，补 RotationScale/最终曲线/朝向反馈，再完成动态上身与脚部约束。
原 P5B/P5C/P6/P7、道具、完整相机和十分钟性能预算范围不变，音频暂缓。
未提交、未回退用户修改，未修改已人工认可的键鼠控制。
