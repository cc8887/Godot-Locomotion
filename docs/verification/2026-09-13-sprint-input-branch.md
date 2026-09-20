# Sprint 输入分支完整接入

第一百四十九批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 缺项与正式修复

上一批确认 Standing Cycle 的 Sprint 输入只消费 `ALS_N_Sprint_F`，
虽已编译 `ALS_N_Sprint_F_Impulse` 来源，却没有更新和求值原图中的
TwoWayBlend。这属于原 P3 移植缺项，已继续实施，没有推迟到 P5/P6。

- 新 `AlsSprintInputCompiler` 核验原 V4 图的 Sprint 连接、A/B 动画、
  独立共享来源、循环和 StandingPlayRate，拒绝不支持的回调/输入模式。
- 读取原 `RelativeAccelerationAmount.X`（Godot 对应 -Z）。原范围
  0..0.25 映射到 0..1，原插值速率上升 20、下降 .5，保留 Clamp。
  没有新增统一等待时间或人为姿势偏移。
- 新候选 `AlsSprintInputState` 保存 alpha 插值、访问相关性和子节点
  激活状态。父初始化与新相关子节点初始化分别处理；未访问保持历史。
- 两个来源各自保留 PlayerId、epoch、时间与缓存权重，一起进入共享
  tick/事件事务；Impulse 的原 PlayRateBasis 为 .8330000042915344，
  不覆盖为主 Sprint 的 1。原编译节点 207 对应正式来源 7。
- `AlsSprintInputSource` 按同步秒数采样两个原动画，以原 TwoWayBlend
  顺序混合姿势和曲线，再由 Standing 方向缓存、对角缩放和 Lean 消费。
  完整入口来源布局仍为 224/258，没有借用另一动画的播放身份。

直接节点语义参考本地 UE 5.9 `AnimNode_TwoWayBlend.cpp`；直接资产/
动画图对照为当前 ALS V4，ALS-Refactored C++ 仍是架构参考。

## UE 同属性配对

新增 `--parity-sprint` 六秒夹具，覆盖加速、稳定冲刺、退回跑步、停止、
再次冲刺、变化加速度和视角，30/60/120 Hz 各左右视角轨迹。它仍是
完整动画所有者的受控属性/场景观测回放，不是两引擎实际 Motor 同输入。

| 配对 | 帧数 | 超阈值姿势帧 | 最大位置/角度差 | 曲线 | 实际 tick 子集 |
| --- | ---: | ---: | --- | --- | --- |
| Sprint | 2520 | 62 | .0421459 cm / .0684298° | 全部匹配 | 9016 次通过，时间差 0，最大权重差 2.384e-7 |
| 原横移 | 1260 | 30 | .0285769 cm / .0586825° | 全部匹配 | 4252 次，时间差 0，仍有 4 次权重差 |

严格位置 .001 cm、角度 .02°、缩放 1e-5、曲线 1e-4 门槛未修改。
两条姿势比较仍退出 1；Sprint 来源比较退出 0，横移来源比较退出 1。
横移权重差仍在两个 30 Hz 轨迹第 61 帧，最大 .013902425765991211。
不得把实际 tick 子集通过称为原生全部访问、通知和最终姿势全部等价。

证据：`artifacts/full-graph-godot-149-sprint.json`、
`full-graph-ue-149-sprint.json`、`full-graph-parity-149-sprint.json`、
`full-graph-sources-149-sprint.json`，以及 `149-strafe` 同类报告。
横移请求 SHA256 与第 148 批一致，因此复用 `full-graph-ue-148-tail.json`。
Sprint 请求 SHA256：
`6945A1B2A29F774A2B11DF7877A8E51652977643CCE077CDE74A83FAD68D117D`。

UE 启动前完整 Editor/三插件构建与审计通过，未改原生源码或 DLL。
BuildId `4855b08e-0078-431b-b819-d1a7a48b513d`；日志前缀
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T040733527Z-4f364897ed5c41f4ab445b68502b0179`。
冷导出退出 0，0 error/0 warning。本批没有重做普通 Editor/插件打包；
不能将旧二进制重启验证称为新 Sprint 夹具的普通 Editor 复验。

## 生产、事务和实际渲染

优化 Debug 构建 0 警告、0 错误。Core 11 项、Import 23 项通过，含新
候选历史/子激活、原参数/来源身份及不支持配置拒绝验证。TRX：
`artifacts/test-results/sprint-input-core-149.trx`、`sprint-input-149.trx`。

生产 single/parallel 各 960 帧通过，result=`A60FF3CFBD847474`，
fullPose=`389A376BB89FBC43`，sampledPose=`B1C0577B44AB4DDF`，
root=`DB5B813964D3479C`；39 事件，316 锁脚帧、910 偏移帧，lag/stale=0。
日志 `sprint-input-production-single-149-final.log` 和 parallel 对应日志。
旧摘要失败日志保留，新摘要仅作双线程回归，不是 UE 等价依据。

新增测试开关 `--parity-retry`：Sprint 全部 2520 帧先求值、丢弃，再以
同输入重试，检查已提交 Sprint 历史/共享时间未泄漏，姿势、曲线、来源
时钟和逐条事件一致。非重试与重试的完整 JSON 文件 SHA256 相同：
`0ED91F03077EC2282CB9BFF8EECA14897A9F6861BB052630FEBAEE40A679E34B`。
日志 `full-graph-godot-149-sprint-retry-final.log`。首次测试直接比较含
InlineArray 的整个 Standing record 导致 Equals 不支持，已改为显式
Sprint 历史比较与既有逐项来源比较；初次失败日志保留。

晚期姿势失败（第 13 帧）与来源事件失败（第 25 帧）回滚通过，事件
回调泄漏 0，日志 `sprint-input-late_transaction-149.log` 和
`sprint-input-late_source_event-149.log`。这两个早期夹具本身不证明
冲刺窗口的故障注入覆盖，冲刺每帧丢弃/重试由上面的专项独立验证。

默认 BaseLayer 180 帧单线程生产回归通过，75/109 来源、lag/stale=0，
日志 `sprint-input-default-demo-149.log`。完整上身/脚部入口尚未设为默认。

真实渲染 `sprint-input-visual-149`：720 帧、120 截图、240 Sprint 帧、
52 SprintBlend 帧；最大相邻帧脚旋转 23.977°，未触发既有 30° 检查。
运行分析脚本并查看 186..360 帧的 30 格连续图，能看到前倾、交替摆臂、
退回跑步和再加速。该观察不等于支撑脚滑移测量或 UE 视觉 1:1 验收。

## 继续顺序与未关闭项

Sprint_F_Impulse 已从“数据存在但未消费”变为正式图接入；完整 P3/P4
验收仍未通过。下一项定位起步小姿势差和横移缓存权重差，然后完成实际
Motor、反向时序、平台支撑/接触和人工验收，通过后切完整默认入口。
通用 P5A 剩余项、P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/恢复/完整 Camera、P7 十分钟性能预算仍按原规划继续。
原 Core 23 项失败、Import 分配不稳定、旧 p95 2.559 ms 超 2.5 ms
均未被本批专项关闭。音频暂缓。本批没有 commit、revert 或合并。
