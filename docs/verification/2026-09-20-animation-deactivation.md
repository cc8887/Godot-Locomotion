# 完整动画入口的停用与恢复

本批在 `D:\GodotALS` / `main` 继续实现 P5A 可恢复生命周期。永久销毁/换代已由
前一批覆盖；本批补齐同 generation 停用、恢复、新动作和输入丢弃。

## 行为与边界

完整生产角色的 `SetActive(false)` 现在结束动作和 Notify State，再次激活后
从新的动画更新继续。内部 `SetSchedulingActive` 仅暂停调度，保留原候选输入
重试与已提交动作。因此十角色夹具的“丢弃候选再重试”改用明确的调度接口，
原断言、请求、帧序列和摘要没有放宽。创建 spare、永久退役前关闭调度和销毁
过程也使用内部接口，避免把 generation retirement 错报成一般停用。
旧非完整入口保留历史停用合同，没有更改冻结阶段的回放语义。

在关闭 Worker admission、确认所有阶段 idle、丢弃尚未完成候选之后，停用清除
共同 Montage 物理播放、动作所有者、原生 Notify 活动集合及 Action/GroundedEntry
反馈。保留帧边界、动作请求高水位、物理实例序号和原生通知实例分配器；新动作
不会复用旧身份。旧输入的 Start 和 Cancel 都被消费，恢复重试时不重新接受，
也不产生针对已清理动作的错误取消。

三种恢复边界：

| 停用位置 | 恢复行为 |
| --- | --- |
| 帧 N 已完整提交 | 从 N+1 正常 Gather |
| N+1 的 Worker 候选尚未完成 | 重试原 N+1 Motor 输入，动作请求已作废；不重新积分移动 |
| N+1 的 Worker 已完成、Main Commit 被挂起 | 消费并放弃该结果发布，从 N+2 继续；不派发未提交的 Accepted |

最后一种情况不伪造 N+1 的主线程提交：已积分的 Motor 和 Worker 动画历史保留，
Main 动作镜像仍只关闭实际派发过的所有权。关闭调度时保存的脚部姿势和曲线反馈
作为同代恢复检查点，仍携带真实历史身份；不把它们改名为旧主线程提交帧。
首次恢复 Gather 使用匹配的检查点，之后继续消费普通 Main Commit 的反馈。

恢复检查点也解决了完整脚部链路在无待重试输入时暂停/恢复的缺口：清空场景
采样、却保留动画历史，会使下一帧的原生脚部身份不匹配。现在恢复时重新提供
相同身份的采样，不放宽 Core 的连续帧、姿势来源或 prediction 校验。

Main ownership 增加 revision；Begin 回调里立即停用再恢复也会使旧派发轮次失效，
避免仅检查 Closed 而漏放旧 Tick。Demo 停用时停止采集输入，并丢弃尚未 Gather
的动作和锁存边沿；停用期间的 R/X 不会变成恢复后的延迟动作。已经复制进
Motor 的帧保持原值，其旧动作由 Worker 请求高水位隔离。

## 验证

证据目录：`artifacts/animation-deactivation-20260920`。

- Godot 优化构建通过、0 warning / 0 error，最终构建 `build-integration.log`。
- Core Release 2539/2539 通过、0 Skip，排除未修改的两类历史 P5A Golden/Schema
  长矩阵。新 5 项覆盖旧 Start/Cancel 重试、物理实例分配器、prepared 阶段拒绝
  清理、镜像重新打开后拒绝旧 Tick，以及只丢弃未 Gather 的捕获输入。
- 普通主场景实际 R/X 的五种停用测试全部通过：`committed`、`pending`、`held`、
  `callback`、`callback-reopen`。每种 2 接受（停用前/恢复后各一次）、1 生命周期
  中断、1 显式取消、1 合成 End；generation 保持 1，新播放 epoch 和 Notify
  token 增长，恢复后不存在旧活动所有权。最终日志 `final-<boundary>.log`。
- 以实际 Motor 成功积分计数验证恢复：pending 重试增加 0 次，其他边界的新帧
  增加 1 次；暂停期间位置、计数和帧号不变。没有用“静止胶囊位置必须逐位不变”
  代替积分验证，因为正常新帧仍包含物理贴地/重力处理。
- 初轮 `committed` / `held` / callback 测试暴露并修复了脚部恢复身份缺口；
  `pending` 夹具最初没有关闭 Commit，导致人为暂停 Worker 时触发缺结果诊断，
  后来在同一测试边界同时关闭 Commit。首错保留于 `<boundary>.log`。
  第二轮 `checkpoint-*.log` 中，测试错误地要求新物理帧位置完全相同，并假设
  pending 恢复后的下一输入尚未预采集；已改为积分计数与实际预采集帧号断言。
  这些是测试模型修正，没有放宽生产身份或数值阈值。
- 旧生命周期通过，包含初帧/运行中故障与移动平台恢复/移除。普通键鼠 360 帧
  通过，Alt 180 帧、左右横移 150/120 帧、4 次鼠标事件、83 帧脚趾接触。
  前一批输入换代/提交等待专项仍通过：第 16 帧接受、第 17 帧回调取消。
- 永久退役的 `held`、`generation-callback` 和 `callback` 复测通过；每种仍只有
  1 动作中断和 1 合成 End，没有被新的可恢复停用路径抢先关闭或改变结束原因。
- 十角色 Single/Parallel 各 3621 帧通过：50 接受、20 替换、20 取消、10 完成，
  原候选取消/重试和提交等待覆盖保持。pose `CEC4EC705E945A65`、root
  `E030B6049AEDDCE1`、result `8406F373DABD4C3A` 双模式一致，且与前批相同。

运行新专项：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --headless --path D:\GodotALS res://scenes/tests/animation_deactivation_smoke.tscn -- --boundary=committed
```

依次将 `committed` 替换为 `pending`、`held`、`callback`、`callback-reopen`。

## 后续范围

运行时求值失败后的成功帧取消仍未完成：失败帧必须回滚并保留 Main ownership，
后续成功帧才发布 `InterruptedByRuntimeFailure` 及 End。当前冻结/换代恢复通过
不等于该路径完成。下一项继续此接线，再推进其余 Notify gameplay、Overlay
道具生命周期、Roll/Mantle 碰撞安全 Root Motion、Ragdoll/Get-up/Pose Recovery、
完整 Camera、地形/滑步/换髋/上半身人工验收和十分钟性能认证。音频继续暂缓。
本批没有完成渲染截图或整套 ALS 的 1:1 观感验收。
