# 生产动画的主线程所有权与永久退役

本批继续在 `.` / `main` 实现 P5A 生命周期，完成**永久销毁和角色
generation 退役**的动作/Notify State 清理。它不代表可恢复停用或 Worker 故障
后的取消已完成，也没有新增 Roll 位移或改变原有移动/相机算法。

## 实现

每个角色保存独立的 `AlsCommittedAnimationLifecycle`。Main Commit 在发布前
用值副本校验所有事件和动作结果，再在实际回调前逐项更新所有权。未发布候选、
错误身份、重复提交和校验失败不能更新这份镜像；校验失败进入已有的冻结诊断
路径。镜像使用原固定 16 槽 Notify State 缓冲和固定 2 槽动作结果容量，不读取
Worker 的候选状态或 Montage 时钟。

原生 State 对象可在保持 InstanceId 的同时合并另一来源，因此不能拿冻结版
timeline 的整个 occurrence/epoch 元组匹配全部生产 Tick/End。新增原生匹配
路径校验 InstanceId 和 OwnerToken，并通过每次 Tick 更新来源及当前动画时间。
旧非原生事件继续使用原严格身份匹配；合成 End 保留最新原生上下文，回调时长
置零、ReachedEnd 为 false，并附上实际生命周期结束原因。

销毁产生 `InterruptedByLifecycle`，换代产生 `InterruptedByGeneration`。
清理只处理已经派发的 Begin/Accepted：例如一个新的 Start 已由 Worker 求值
但还在等待 Main Commit，销毁时只能中断旧的已接受动作，不能派发候选 Accepted。
镜像在任何清理回调前关闭并清空，重复或重入清理不会产生第二个 End/Interrupted。

若订阅者在 Begin/Accepted 回调中销毁角色，其余尚未派发的旧帧事件会停止。
终结事件的数据从角色中分离，加入主线程队列，并通过 Godot deferred 回调派发；
这样所有当前事件的订阅者先收到 Begin，End 不会重入 registry 释放、替代角色
激活或节点移除。结束数据仍携带旧角色 generation 和最后实际提交身份。
这是主循环正常运行时的退役合同；进程强退/引擎已经停止消息循环后的队列排空
不在本次验证范围内。

## 验证

证据目录：`artifacts/animation-retirement-20260920`。

- Godot 优化构建 0 warning / 0 error，最终日志 `build-deferred.log`。
- Core Release 2534/2534 通过，0 Skip，排除未改动的历史
  `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests` 两类长矩阵。
  新增 7 项检查覆盖部分派发后的清理、重复清理、原生来源合并、错误 token、
  校验原子性、容量溢出、动作替换身份及空事件帧热身后的零分配。
  首次专项中溢出测试试图向容量已满的单帧事件缓冲放入第 17 项，测试未能构造
  所需状态；修正为跨两帧累计 17 个活动状态后，原容量拒绝和原子性断言通过。
  首错保留于 `core-focused.log` / `ownership.trx`，最终在 `core-final.log` / TRX。
- 实际普通主场景 Roll 五种专项均通过，每种恰好 1 接受、1 中断、1 合成 End：
  `dispose`（播放中销毁）、`generation`（播放中换代）、`held`（新替换动作等
  提交时销毁）、`callback`（Begin 内销毁）、`generation-callback`（收到旧代
  End 后再销毁 Demo）。重复清理无附加回调，两位订阅者的 Begin/End 顺序正确。
  最终日志为 `final-<mode>.log`。清理过程中没有读取 Worker 活动 ownership。
- 前一批普通动作输入回放 60 Hz 通过，240 帧中 3 接受、1 替换、1 取消、1
  自然完成，结束时通知反馈清空。最终动作换代/等待提交专项也通过：旧待处理
  Start 没有复活，第 16 帧接受、回调排队第 17 帧取消。
- 旧 P4 生命周期回归通过，覆盖初帧/运行中故障、暂停恢复、换代及平台恢复/
  移除。这是旧链路回归，不能当作完整图 active Roll 故障取消认证。
- 普通 Alt/A/D/鼠标 360 帧通过，Alt 180 帧、左右横移 150/120 帧、4 次鼠标
  事件、83 帧脚趾接触。没有新增人工观感/渲染截图验收声明。
- 十角色动作输入 Single/Parallel 各 3621 帧通过，每种模式 50 接受、20 替换、
  20 取消、10 完成；包含候选取消、恢复重试和延迟提交。pose
  `CEC4EC705E945A65`、root `E030B6049AEDDCE1`、result `8406F373DABD4C3A`
  一致，且与前一批相同，说明本批提交侧跟踪未改变已验证回放的动画结果。

运行新专项：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --headless --path . res://scenes/tests/animation_retirement_smoke.tscn -- --retirement=dispose
```

将 `dispose` 依次替换为 `generation`、`held`、`callback`、`generation-callback`。

## 下一步与未完成边界

当前 `SetActive(false)` 还承载调度暂停和取消候选后的重试，不等同于永久销毁。
它仍保留动画检查点；不能仅发送 synthetic End 而让恢复后的旧 Montage/Notify
继续 Tick。下一步需要把调度暂停与 gameplay deactivate 分开，闭合恢复时的
Worker 状态及 Main ownership，尤其是 Worker 已完成、Main 尚未提交的边界。

运行时求值失败仍要遵循原合同：失败帧回滚、保留已提交镜像，之后成功帧通过
runtime-failure cancellation 结束旧动作，而不是在失败帧发布伪造 End。本批
没有把现有冻结/换代恢复机制冒充该功能。

随后继续其余 Notify gameplay、Overlay/道具生命周期、Roll/Mantle Root Motion、
Ragdoll/Get-up/Pose Recovery、完整 Camera；地形/起停滑步/换髋/上下身人工联合
验收和十分钟性能认证仍未关闭。音频按既定范围暂缓。
