# 普通动作输入与主线程结果回传

本批在 `D:\GodotALS` / `main` 直接实现。按照 P5A 设计，普通 Demo 的 R 键触发
原生 Roll **原地动画预览**，X 取消已接受的动作；真实 Montage、Slot 姿势、通知、
GroundedEntry 和退出路径沿用当前生产图。没有添加翻滚位移，也没有完成 Roll
玩法门控、碰撞安全 Root Motion 或 Mantle。

## 接线与边界

`IAlsActionRequestSource` 是现有移动输入源的可选伴随接口。Motor Gather 将动作
复制到同一 `AlsFrameInput`，没有修改 `AlsLocomotionCommand` 的冻结布局或历史
回放数据。未配置动作的旧输入源继续使用原来的无动作哨兵值。

`AlsCapturedActionRequests` 以帧、角色和 generation 三者绑定不可变请求；读取
不消费输入边沿、不生成新 ID。错误身份、倒退 generation、非法命令形状及外部
伪造的 runtime-failure 取消均被拒绝。普通 Start 使用捕获帧号作为本代请求 ID，
同帧重试不会重复接受；Cancel 使用已提交 Accepted 的 request/epoch 所有权，
不把尚未接受的请求当成当前动作。同一捕获中取消优先，键盘 Echo 不生成请求。

普通输入通过 `_UnhandledInput` 锁存 R/X。Main Commit 延迟时，下一帧移动输入
可能已经预先采集，因此新边沿排到下一个尚未采集的帧，不修改既有帧。动作结果
在身份、诊断和提交帧号发布后由主线程派发；回调可排队下一次捕获，不能重入本帧。

替换角色时，已积分的 Motor 状态继续按检查点迁移，旧代动作请求清为无动作。
若移动输入已经预采集，则只重新绑定动作 generation，不重复应用蹲伏/旋转模式
切换。旧代排队边沿和迟到的 Accepted 不能复活新代的取消目标。

这次验证的是待发布请求不跨代和新代输入可继续使用。**已提交的 active Notify
State 在停用、销毁、故障/换代时生成主线程合成 End/Interrupted**，仍须接入完整
生产所有者镜像；不能把本次重建测试当成整个 P5A 生命周期闭合认证。

## 验证

证据位于 `artifacts/action-input-20260920`，未提交生成资产、引擎缓存或测试输出。

- Godot 优化构建通过，0 warning / 0 error；最终代码构建日志为
  `godot-lifecycle-build.log`。
- Core Release 2527/2527 通过、0 Skip，过滤未改动的
  `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests` 两类历史长矩阵；本批新增 9 项
  身份/请求形状/物理接受幂等性/零分配检查。未重新运行 Import 全库，因为本批
  未修改 Import 或导出数据。
- 普通入口实际 R/X 回放 30/60/120 Hz 通过：120/240/480 提交帧，每档 3 接受、
  1 替换、1 取消、1 自然完成、1 GroundedEntry，结束时动作与入口反馈清空。
  在原地测试中胶囊水平位移保持为零；不是用 Root Motion 模拟预览移动。
  `input-30.log`、`input-60.log`、`input-120.log` 为首轮结果，身份/等待边界调整后
  的三档复测在 `input-final-30.log` / `input-final-60.log` / `input-final-120.log`，均通过。
- `action_lifecycle_smoke` 使用普通主场景和实际按键：第 13 帧旧代 Start 被淘汰，
  新代重放 Motor 时不接受该动作；第 14 帧 Commit 挂起、第 15 帧已预采集，
  此时按 R 正确排到第 16 帧；第 16 帧 Accepted 回调按 X，仅第 17 帧取消。
  没有旧代动作回调、重复接受或改变已采集输入。日志 `action-lifecycle.log`。
- 旧 `p4_lifecycle_smoke` 首次失败：重建逻辑改变了旧输入源的无动作 generation
  哨兵，破坏原有 Motor 精确重放。修复为保留旧无动作格式，身份化动作输入才
  重绑定新代；没有降低断言。首错 `lifecycle.log`、通过 `lifecycle-final.log`。
  通过范围含初帧故障、停用/恢复、替换、运行中故障及移动平台恢复/移除。
- 普通键鼠 360 帧通过，Alt 行走 180 帧、左右横移 150/120 帧、四次鼠标事件，
  最终脚趾接触 83 帧。日志 `keyboard.log`，最终复测 `keyboard-final.log`。
  未宣称本批已完成渲染截图/人工观感验收。
- 十角色带真实动作输入，Single/Parallel 各 3621 帧通过。每种模式共 50 接受、
  20 替换、20 取消、10 自然完成；覆盖两次候选取消重试和一次 Main Commit 等待。
  同时保留 Overlay 切换、空中、蹲伏、最终脚部接触检查。
  两种模式 pose `CEC4EC705E945A65`、root `E030B6049AEDDCE1`、
  result `8406F373DABD4C3A` 完全相同。日志 `dispatch-single.log` / `dispatch-parallel.log`。
  后续修改限于普通按键适配器和 generation 迁移，该调度夹具不使用它们。

运行普通输入专项：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& '<Godot-4.7.2-console.exe>' --headless --path D:\GodotALS res://scenes/tests/action_input_smoke.tscn -- --hz=60
& '<Godot-4.7.2-console.exe>' --headless --path D:\GodotALS res://scenes/tests/action_lifecycle_smoke.tscn
```

运行十角色动作调度专项（另运行一次追加 `--parallel` 比较摘要）：

```powershell
& '<Godot-4.7.2-console.exe>' --headless --path D:\GodotALS res://scenes/tests/refactored_foot_dispatch_smoke.tscn -- `
  --hz=60 --characters=10 --action-requests --layered-frame --foot-ik-frame --based-foot-lock `
  --refactored-pose-curves --refactored-movement-curves --refactored-foot-frame `
  --foot-lock-gravity-twist --foot-lock-final-contact --foot-ground-clearance `
  --foot-contact-toes --overlay-cycle --contact-static
```

## 下一步

优先接完整生产图的生命周期 Notify/动作所有权镜像，补齐停用/销毁/换代及故障
清理，再推进 Overlay 道具玩法和 Roll/Mantle 的碰撞安全 Root Motion。P3/P4
地形、起停滑步、换髋与上下身联合人工验收仍未关闭；Ragdoll、Get-up、Pose
Recovery、完整 Camera 和 P7 十分钟性能认证继续保留。音频按用户要求暂缓。
