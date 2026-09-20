# 完整动画所有者的最终根分支调度

第一百四十五批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 实现

AlsLayeredAnimationFrameRuntime 已按正式根权重调度普通链和 Ragdoll
动画链。普通链相关时依次求值 BaseLayer、Overlay、Post Layering/Aim、
手部和 Foot IK；不相关时这些组件仍接收生命周期与全局输入，但不
伪造来源 Update 或姿势 Evaluate。BaseLayer 此时完成全局属性、Montage
与共享批次，通过 PrepareUnvisitedGraph 参与同一事务。

Ragdoll SequencePlayer 使用原生 ALS_Flail 资源和共享批次的独立身份，
快照分支读取不可变的命名快照；最终根混合姿势/曲线后统一保存反馈。
所有组件校验后共同提交，来源时钟、事件、惯性化、Aim、手脚属性和
最终曲线在异常时共同回滚。手部增加隐藏提交和独立最后姿势身份，
权重也区分候选/已提交，取消的手部输入不会泄漏给下一次隐藏访问。

新增 AlsRagdollFrameObservation，包含当前帧身份、根刚体的 UE 坐标
厘米/秒速度，以及不可变快照。根进入/退出 Ragdoll 动画分支时必须
由调用方显式提供观测，不从 Motor 速度猜测；外来/非有限观测被拒绝。
快照的来源身份在实际求值时校验，支持检验普通链完成后的晚期失败。
只有完整 224/258 根绑定才能接这些输入，旧 223/257 分层入口不会
把 Ragdoll 输入当普通链成功处理。Ragdoll 原始采样器由所有者持有和释放。

Ragdoll 内部初始化判定同步修正为比较实际 Initialization 计数；相同
计数的 GlobalFrame 变化不重置 Flail 的 epoch 或时间。相关性仍由
实际动画 Update 遍历判断，不用外部 FrameId 间隔代替。

根动画调度已经进入真实生产所用的所有者类。场景层的
AlsProductionMovementRuntime 目前仍调用普通输入，没有根刚体采集或
快照桥接；所以这不等于物理 Ragdoll/Get-up 已进入 Demo。正常场景
使用完整脚部入口时已经走本次代码，默认入口仍是 BaseLayer。

## 自动验证

优化 Debug 构建 0 警告、0 错误。Import 手部 16 项、Core 根/Ragdoll
31 项全部通过：`artifacts/test-results/root-dispatch-hands.trx`、
`artifacts/test-results/root-dispatch-core.trx`。

`layered_frame_input_smoke.tscn -- --root-dispatch` 在 30/60/120 Hz
共 840 帧通过：195 普通链隐藏、429 根混合、399 Flail 求值、225
快照求值、9 次普通链恢复、18 次晚期快照失败、14 次缺少观测拒绝。
每帧取消重试，最终姿势/曲线、脚部属性、Aim、BasePoses、共享来源
时钟/epoch 和事件一致；隐藏普通链时只有 Flail 的一个 player/sample。
日志 `artifacts/root-dispatch-composition.log`。

上述组合使用完整所有者与真实动画资源，包含冷启动 Ragdoll、根过渡
中断、InAir/Crouching/Mantling 输入和快照恢复。速度/命中观测受控，
快照从前次最终动画姿势构造，未模拟刚体，不能代替物理恢复验收。

真实普通生产 single/parallel 各 960 帧通过，摘要保持：
result=B289A6FB5130DBB7、fullPose=7B82A91E8A09C723、
sampledPose=6804D603D2523040、root=DB5B813964D3479C；224/258，
37 来源事件，lag/stale=0。
`artifacts/root-dispatch-production-single.log`、
`artifacts/root-dispatch-production-parallel.log`。

旧上身/手部组合与真实映射输入各 1,260 帧通过：
`artifacts/root-dispatch-owned.log`、`artifacts/root-dispatch-layered-input.log`。
晚期姿势/事件回滚通过，事件回调泄漏为零：
`artifacts/root-dispatch-late-transaction.log`、
`artifacts/root-dispatch-late-events.log`。本批文件空白检查通过。

## 实际渲染观察

以完整脚部入口、parallel、真实 Demo 场景运行横移换向回放：
`p4_movement_visual_smoke.tscn -- --als-mode=parallel --als-cycle
--foot-ik-frame --strafe --run --capture-step=6 --diagnostic-capture`。
在 OpenGL Compatibility/RTX 2070 SUPER 上完成 720 帧、120 张截图，
552 移动帧、58 转身帧；最大相邻帧脚部旋转 14.138°，超过 30° 的
诊断记录为零。日志 `artifacts/root-dispatch-visual-strafe.log`。

产物目录 `artifacts/root-dispatch-visual-strafe/` 保留 frames.json、
body-frames.json、violations.json 和全部 PNG。运行既有分析脚本并
实际查看 movement-contact-sheet.png、strafe-directions.png：左右
横移端点和连续帧可见髋部、躯干及手臂姿势变化，未见本回放中的整腿
突发翻转。这属于定性观察和粗阈值检查，不是 UE 相同输入对照，也
没有量化支撑接触窗口滑移，不能据此宣布原始上身/换髋/滑步问题全部解决。

## 后续与完整目标

现在转向 P3/P4 的 UE 同输入多帧、起步与 A/D 换髋门控、上身空间和
动态层权重、平台及支撑脚轨迹验证，通过后切换完整默认入口。无需
等待完整 P6 物理玩法才继续这些视觉修复。根物理观测/恢复快照的
场景桥接、物理所有权/Get-up 与相机各模式继续归入对应后续范围。

P5A 剩余通用事件/动作、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 物理恢复/完整 Camera、P7 十分钟性能仍未完成。
既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms 超过 2.5ms
没有在本批关闭；没有全套测试、最终人工或性能通过声明。音频暂缓。
未 commit、revert 或 merge，保留已有工作区改动。
