# 基座脚锁接入完整动画事务与实际场景

第一百六十批，2026-09-13。上一批完成纯模型，本批已接入实际完整根的
Main/Worker/Commit 路径。入口为 `--als-cycle --foot-ik-frame --based-foot-lock`。
默认 Demo 仍 BaseLayer；不带 based 选项的完整根继续作为 V4 对照。
新路径视觉验收失败，不能标为默认启用或完整修复。未提交、合并或回滚
已有修改；完整 P3/P4 至 P7 目标保持。

## 接入内容

新增 `AlsBasedFootLockFrame`，由 `AlsFootIkFrameRuntime` 共同准备、提交和
取消。按真实参考骨骼父链从 foot_l/r 找到 pelvis 直接子骨，使用其参考
局部平移求大腿轴；分别支持 Godot 与 FBX 骨骼空间。

完整图的 Hands 输出在进入脚部控制前采集双脚目标和骨盆组件旋转。
这些目标与模型自己的锁定后、IK 前 Final 分开保存，下一帧全局脚部
更新读取已提交历史。隐藏普通分支时，从最后根输出采集替代姿态；脚锁
全局属性仍随根帧推进。骨骼/曲线晚期失败时均取消候选。

Refactored 的 Foot IK 默认也使用 ik_foot_l/r，但其控制图与 V4 不同。
V4 控制链会修改这些骨骼，故不能直接把上一帧 IK 后目标再作为动画目标。
本适配在脚部控制前保留目标，且直接应用已经完成 target/anchor 混合的
Final，避免再次乘 Alpha。V4 offset/pelvis/knee/TwoBoneIK 顺序仍保留。
这属于明确的跨版本适配，尚未得到 Refactored 整图原生逐帧配对证明。

基座读取 Main 已采集的 FloorSample：使用完整 ColliderId，而非截短的
PlatformId 作为身份；变换为无缩放刚体。没有将场景对象放进 Worker。
动画自身速度用于 MovingSmooth：有输入且速度 >= 1 cm/s，或速度 > 150 cm/s。
两个门槛来自本地 Refactored C++ 默认值，不替换 V4 ShouldMove 条件。

`NotifyFootLockTeleport()` 发布显式单调事件序号，Gather 带入帧观察；
序号在移动 Step 候选之外，重试仍看到同一事件。动画自己的传送序号和
0.2 秒窗口计时随脚部历史提交/取消。组件测试已覆盖，完整 Godot 传送
场景仍需补测，不能把事件 API 的存在当作全套传送/网络平滑已完成。

新增紧凑锚点/约束诊断随实际脚部结果发布。控制器事务快照同时包含完整
已提交 based 状态，原有 controllerRestored 全值比较可检查新历史。

## 组件与完整移动回归

- Release 脚部 Import 测试 51 项通过，包括新增 11 项：30/60/120 Hz ×
  两种骨骼空间的实际控制输出、目标与 Final 分离、隐藏/正常分支取消与
  传送重试、禁止重复 Alpha 混合、热身后循环零分配。
  `artifacts/tests/foot-ik-frame-160-final.trx`。
- Core 模型、帧交换、布局共 46 项通过。
  `artifacts/tests/foot-contract-model-160-final.trx`。
- 优化 Debug 项目构建 0 warning / 0 error。
- 新路径 single/parallel 各 960 帧，完整移动状态、替换角色和通知链通过。
  首轮日志 `movement-production-{single,parallel}-160-based.log`，两模式
  result=`52FD88C3010C8E84`，full pose=`363B466D6421AB00`，sample pose=
  `8027BA035F496B4C`，root=`9EBE2D081A09FCF1`；36 事件、321 lock 帧、
  910 offset 帧、lag/stale=0。新规则有独立摘要，不覆盖 V4 摘要。
  最终有固定摘要断言的日志为 `movement-production-{single,parallel}-160-final.log`，
  两者完整最终结果行除 mode 外逐字一致，normalized result=
  `2A72AF57CC6426B0`。
- V4 single 960 帧原摘要仍通过：`EDD340F3A7BBFAF8` /
  `60FDBB3F45C29D6F`，日志 `movement-production-v4-160-final.log`。
- 实际 parallel 第 25 帧注入晚期来源事件失败，runtime/result/controller/
  pose/banks 全部恢复；其中 controller 快照已包含新 based 状态。
  一个候选事件未泄漏回调。日志 `movement-late-source-160-based.log`，exit 0。
  该场景证明该失败点的完整链回滚，不代表所有锁定/传送场景都已回放。

上述测试证明接入/事务和所覆盖的数值行为，不代表视觉通过或十分钟性能预算。

## 实际平台诊断

同一真实 Demo、Main 物理与 Worker 完整图，各 360 帧。后两秒共同满锁
且双脚 trace 有可行走命中的窗口测量；这仍不是完整鞋底接触真值。

| 场景 | 满锁帧对 | 位置锚点变化 / 大腿约束帧 | 未触发大腿约束的帧对 | 该连续窗口最大漂移 | 最终脚骨到世界锚点最大水平误差 |
| --- | --- | --- | --- | --- | --- |
| 平移 1.25,0,-0.5 m/s | 119 | 0 / 0 | 119 | 0.000608 mm | 0.000970 mm |
| 旋转 0.1 rad/s | 119 | 0 / 0 | 119 | 0.014799 mm | 0.001719 mm |
| 旋转 0.35 rad/s | 119 | 99 / 99 | 20 | 0.004220 mm | 0.001909 mm |

平移日志 `movement-platform-graph-160-translation-final.log`；旋转日志
`movement-platform-graph-160-{slow-rotation,rotation}-final.log`。
旋转变体新增 0.1 mm 门槛检查实际目标跟随及未触发大腿约束的连续窗口，
没有删除总漂移输出。原速旋转总窗口仍移动 106.063 mm；99 次锚点变化
全部对应原版大腿约束，不再归因于骨骼追不上锚点。但约束何时应触发及
看起来是否合理，仍必须做原生/视觉对照，不能凭误差很小就关闭脚部问题。

## 视觉失败：停止进入满锁时的旋转突变

已完成 720 帧、120 张截图的横移/换向/停止渲染，并检查移动接触表。
目录 `artifacts/movement-visual-160`。首次发现异常后补录约束诊断，目录
`artifacts/movement-visual-160-diagnostic`，仍为相同 720 帧/120 张截图。
两次进程均明确 exit 1；没有调整原 30° 单帧旋转门槛。

`violations.json` 唯一异常：frame 616，左脚相邻帧旋转 41.100044°。

| 帧 | 当前最终左锁曲线 | 更新时左 Alpha | 大腿约束 | 脚掌约束 |
| --- | --- | --- | --- | --- |
| 613 | 0.75994515 | 0 | 0 | 0 |
| 614 | 0.9352741 | 0 | 0 | 0 |
| 615 | 1 | 0 | 0 | 0 |
| 616 | 1 | 1 | 1 | 1 |
| 617–620 | 1 | 1 | 0 | 1 |

第 616 帧第一次读到上一帧满锁曲线；自身速度已到零，立即捕获并同时
触发大腿/脚掌限制。锁定组件高度约 14.05 cm。该值表示脚骨原点高度，
鞋底接触需要单独测量。与第 156 批 V4 数据比较，
两路径在此阶段都保留约 14 cm 的脚骨高度，新路径额外发生旋转突变。
因此不能简单归因于脚锁混合重复、平台运输或单/多线程不同步。

现有证据指向 V4 停止曲线/姿态与 Refactored 捕获及约束的版本适配，
还未通过原生输入回放确认最终原因。本批保留失败，不以减少锁定权重、
增加任意延迟或修改角度上限绕过。

已查看接触表及第 612/618 帧原始截图。图像用于检查停止前后姿态，41.10°
数值来自连续帧骨骼四元数记录；六帧间隔截图本身不能替代这份逐帧证据。

## 下一步

1. 捕获异常帧实际 pre-IK 目标、旧 Final、骨盆/大腿轴及 C++ 模型输入，
   在原生 Refactored 函数中配对，区分数学移植偏差与跨版本图/曲线规则差异。
2. 明确停止进入满锁的曲线消费与捕获时序，修复实际旋转不连续，再通过
   相同渲染门槛。平台强转下约束动作也需要画面与原生对照。
3. 补实际换基座、传送窗口、重新有效、离台等完整图生命周期场景，之后
   才讨论默认完整根启用；继续 P3/P4 相位/接触/人工验收及 P5A 至 P7。
