# 完整动画链路的故障恢复取消

本批直接在 `D:\GodotALS` 的 `main` 实现，没有创建项目副本或 worktree。
用户未提交的 `2026-08-28-p4-aim-layering-foot-placement.md` 保持原样。

## 完成的行为

接通 P5A 已规划的“失败帧回滚，成功恢复帧才取消旧动作”。生产完整图的
Worker 求值发生错误后，只有确认 controller、姿势、根变换已恢复，并且
Worker 的旧提交与 Main 提交相符，Release 策略才允许原位重试。

恢复锁存位于动画提交状态之外，记录失败输入身份和故障前提交身份。
失败不能消费锁存；controller finalize、runtime state 和结果发布均成功后
才消费。重试使用原先 Gather 的帧 N，不再次积分 Motor、不重新采集该帧请求。
这里的“严格更新身份”是相对已提交的 N-1；失败的 N 没有变成提交帧，不能
简单跳到 N+1，否则脚部、曲线和来源历史会断帧。可重试错误保留旧脚部探测和
旋转反馈；旧冻结路径仍有独立的清理策略。

恢复帧先取消旧逻辑动作，再处理原帧最多一个普通请求。取消原因是
`InterruptedByRuntimeFailure`，不会误报替换或正常完成；即使物理 Montage
恰好在恢复帧终止，也由故障取消优先。物理淡出仍归共同 Montage 所有。

取消的物理播放不会把本帧尚未派发的 Notify 加入队列。已提交的原生 Notify
State 按 Montage 来源及完整 playback epoch 匹配，通过原生 EndAll 逻辑生成
End，携带故障原因、原实例身份和非自然到达终点标记。End 位于新 Begin/Tick
之前，同一 Notify 类的新播放也不会继承旧状态。容量不足或后续求值失败时，
整帧仍回滚；没有从 Main 直接清空镜像或在失败帧伪造结束回调。

最多自动重试三次，即初次失败加三次重试仍失败时保持冻结和原输入。失败记录
沿用同身份去重；不能完整恢复、Gather/物理查询/Main 提交等错误不会自动放行。
Debug/headless 的严格失败策略不变，第一次故障即退出。专项通过初始化前的
内部测试入口选择 Release 策略，没有修改普通 Demo 的默认失败策略。

## 验证与证据

目录：`artifacts/animation-failure-recovery-20260920`。

- 优化构建成功，0 warning / 0 error，`build-final.log`。
- Core Release 2545/2545 通过、0 Skip，`core-final.trx`；与前批一致排除未改动的
  两类历史 P5A Golden/Schema 长矩阵。新增 6 项覆盖锁存与身份隔离、重试上限、
  恢复失败阻断、物理终止优先级、原生状态同类重启、End 容量溢出和无效旧状态。
- 最后的参数/错误码校验补充后，相关 57 项再次通过，`focused-final.trx`。
- 完整 Demo 的真实 R/X 输入：Single 连续失败两次后恢复、多线程连续失败两次
  并在恢复帧接受新动作、多线程单次失败均验证；失败期间保持 12 帧的提交，
  13 帧原输入不变、Motor 积分不增加。成功时恰好 1 中断和 1 故障 End，随后新
  动作可正常接受、取消；重复错误只有 1 条诊断。见 `single.log`、
  `parallel-replacement.log`、`parallel-final.log`。
- 连续四次失败保持冻结，已提交动作和 Notify 仍各 1、故障中断/End 都为 0，
  持续观察三轮无继续积分，`persistent.log`。
- Debug 策略专项按预期退出码 1，错误为注入的 BeforePublish 异常，见
  `debug-policy-final.log`。这是预期负测，不记为通过运行到终点的场景。
- 十角色 Single/Parallel 各 3621 帧通过，分别 50 接受、20 替换、20 取消、
  10 完成；保留候选取消/重试及等待 Main 提交的边界覆盖。
  pose `CEC4EC705E945A65`、root `E030B6049AEDDCE1`、result `8406F373DABD4C3A`
  两种模式一致，并与上一批一致；见 `ten-single.log`、`ten-parallel.log`。
- 旧生命周期（包含初帧错误和移动平台换代恢复）、待处理帧的语义停用恢复、
  普通键鼠回归复测，见 `legacy-lifecycle.log`、`deactivation-pending.log`、`keyboard.log`。

首轮 `first-single.log` 的失败来自测试仅按 epoch 判定动作事件，误把同 epoch
的 locomotion 事件视为旧 Roll。已改成同时限定 SourceActionId；生产匹配原本
就限定 Montage 来源。`debug-policy.log` 首轮还把预期退出时的 Lifecycle 清理
当作意外结果，已在 Debug 负测中明确接受退出清理。首错均保留，没有删除证据。

运行专项：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --headless --path D:\GodotALS res://scenes/tests/animation_failure_recovery_smoke.tscn -- --single
```

去掉 `--single` 使用 Parallel；加 `--replacement` 在故障帧带入新 Start；
`--failures=1|2|3|4` 设置连续故障次数；`--debug-policy` 验证首次失败退出。

## 剩余范围

本批完成的是完整图 Worker 成功回滚后的同代恢复取消，不把所有故障统一改为
可恢复；物理查询、Gather、Main 提交和恢复自身的错误仍保守冻结。新的同代
故障路径尚未完成移动平台运动中的专项认证；旧移动平台换代恢复通过不代替它。
本批未进行截图或人工观感验收，也没有改动、重导入 UE 资产。

接下来继续剩余 Notify gameplay 和 Overlay 道具装备/切换生命周期，再推进
Roll/Mantle 碰撞安全 Root Motion、Ragdoll/Get-up/Pose Recovery、完整 ALS Camera。
起停滑步、换髋、上半身、复杂地形接触仍需完整观感验收，最终十分钟性能认证
和全图当前版本认证也仍在目标清单中。音频继续暂缓。
