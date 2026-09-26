# 普通 Demo 接入 Refactored Standing / Crouching

日期：2026-09-26。基线：`main / f23030f`。全部工作在统一 Godot 主目录完成，无新 worktree。资源仍依赖本地被忽略的生成资产。

## 实际接入范围

普通 `scenes/demo/als_demo.tscn` 通过 `AlsDemoEntry` 默认启用 `--refactored-stance-hosts`，无需额外诊断参数。两个模型的角色 owner 各自持有运行时，主线程只共享不可变资源。当前链路为：

```text
现有移动输入/物理 Gather
  → 角色共享 Movement/Rest Parent、Grounded bank/queue
  → 新 Standing / 新 Crouching（原各自播放器、缓存、状态和惯性）
  → 现有 Grounded 站蹲过渡桥 → 新 Transition Slot / 惯性边界
  → 现有 Grounded Slot / 空中 / 落地 / 上身 / Overlay / 脚部
  → 原角色提交、物理历史、相机和道具
```

`AlsMainGroundedPoseEvaluation` 的 Standing/Crouching 缓存读取已改为新宿主真实姿态，输出经过生产骨骼提交；不是仅计算新宿主而继续显示旧姿态。新增诊断统计成功提交的新 Standing、Crouching 和 Transition 帧。`--legacy-animation` 保留回归路径。

桥接明确保留旧 Grounded 外层、旧空中/落地/上身和 Overlay 动作 bank；旧部分更新仍有重复开销。**本批不是完整 Refactored Grounded/Locomotion 图替换，也不是 R2–R7 全部完成或 1:1 整角色验收。**

## 组件及边界

- 新 Crouching host：Idle Turn Slot、Movement 方向/步幅/Lean、Stop 腿部层、旋转播放器、QuickFeet、node34 惯性与 PoseCrouching。原五状态/十二条边和原蹲伏配置继续使用，没有复制 Standing 数值。
- Standing/Crouching 共享 Movement/Rest Parent、动作 bank/queue。站蹲重叠时 Parent 只更新一次；全局更新允许空中/隐藏帧不访问 stance，已准备子图统一预校验、提交或取消。后处理只改候选队列，不在 worker 调用 Godot 场景 API。
- 原生 cm/local 坐标与现有 FBX local 坐标显式转换；物理骨按名称、父序及参考姿态验证。两个版本的虚拟骨名称/含义不同，分别按各自 source/target 重建，不按相同序号强行复用。
- 新曲线与旧外层通道显式映射；最终曲线成功提交后才作为下一帧 Parent 输入。RotationYawSpeed→RotationAmount 乘当前 delta。
- 修复 Godot 所用较新 .NET 运行时不支持含 `InlineArray` 结构的默认反射相等比较：更新上下文逐有效字段比较/散列，机器快照逐有效转换栈比较；未改变状态转换算法。此前 .NET 8 组件测试未暴露此问题。

## 尚未闭合的部分

1. 原 Grounded 的站蹲序列、Roll 基底、回调，以及原 Locomotion/Jump/Fall/Land 仍待执行接入；当前使用 V4 外层桥。
2. Rest 的真实足锁/脚目标反馈尚未接入，桥中这些输入为零，因此 DynamicTransitions 尚不能视为完整。源 Notify→ActivatePivot 尚未接入。MovingSmooth 目前沿用现有移动模型，需对齐原设置。
3. 新宿主内部惯性当前使用固定组件变换，世界变换由现有外层处理，仍待原生整图验证。
4. 原生上身/Overlay/手部最终图、完整动作资源与 bank、Mantle、物理恢复完整矩阵继续按 ROADMAP 推进。音频、道具物理、头颈拉长专项仍暂缓。
5. Crouching 新完整姿态宿主尚无新的 UE 连续整图 oracle；三频率组件测试不是原生等价证据。Standing 已有严格原生六组回归保持通过。

## UE 数据导出

新增 `tools/unreal/export_refactored_locomotion_machines.py`，只读导出 Grounded、Locomotion 两个 linked graph 的三个 baked machines（含 Jump），写入 `assets/config/refactored_locomotion_machines.json`。数据绑定当前 catalog 字节哈希，普通 Demo 暂未消费这份外层状态机数据。

按 `ue-diagnosing-plugin-build-load` 技能执行完整 Editor build 与四插件审计（ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp），而非单独构建插件叶模块。首次缺 DotNet 10，进程内设置引擎自带 DOTNET_ROOT 后重试成功；最终 0 actions：

- build 日志 ID：`20260926T040524107Z-14ded168f37741a9aa488819cb405996`。
- BuildId：`b4127720-ddfd-475f-a955-59a24fb7ace6`。
- fingerprint：`173CD9BE7773184C3C321FF981F9A60E642CC56B08B290250FC1E1F47AE8B94C`。
- UE `Saved/Logs/refactored-locomotion-machines-cold-v2.log` 与 `normal-v2.log` 均实际退出 0；两份 JSON 字节一致，100676 字节。
- SHA256：`B3FFBBBB944CCC868911B4ED253C339B9DB5076716C2D7E28AE26277D2280F52`。

首轮错误地预期 Locomotion 仅一个 machine，已改为两个并保留日志。首普通启动的 `Start-Process -Wait` 等待后代进程，被人工中断；不计为成功退出证据，改为等待 Editor 自身后重跑 normal-v2。无 UE C++/配置/资产修改，本批未运行 DataValidation 或打包。

## 验证

本地证据目录：`artifacts/tests/new-demo-chain/`，失败记录保留，不随代码提交。

| 检查 | 结果 |
|---|---|
| Import 相关组件、事务及 Standing 原生回归 | `demo-host-related.trx` 45 通过，0 失败/跳过；此前 `shared-parents.trx` 23 通过、共享站蹲三组 3 通过 |
| Core 上下文/缓存/快照比较 | `context-core-final.trx` 39 通过，0 失败/跳过 |
| Crouching 连续事务 | 30/60/120 Hz 各 11 秒，共 2310 帧；真实姿态、取消重试及动作 |
| 共享站蹲与隐藏帧事务 | 30/60/120 Hz 各 5 秒，共 1050 帧；两 stance 重叠、隐藏 Parent/Slot、后处理后取消重试 |
| 普通 Demo 60 Hz single | Standing 317 / Crouching 169 / Transition 428 帧，air 69、sprint 103 |
| 普通 Demo 60 Hz parallel + 渲染 | 同上，9 张多帧 PNG 位于 `visual-60/` |
| 普通 Demo 30 Hz parallel | Standing 133 / Crouching 70 / Transition 214，air 34、sprint 51 |
| 普通 Demo 120 Hz parallel | Standing 636 / Crouching 339 / Transition 857，air 138、sprint 209 |
| 普通键鼠 | `demo-keyboard-mouse.log`，360 帧，Alt/A/D、4 个鼠标事件，含 Alt 释放继续移动、上一提交反馈、脚趾接触 |
| 10 角色 single / parallel | `demo-dispatch-*-10-v2.log`，各 3621 角色帧、2 次取消重试、1 次延迟提交；air 520/crouch 600/locked 1011/events 120/rays 6544，三种摘要一致 |
| 普通相机/物理恢复 | `demo-camera-recovery-v2.log` 480 帧，换肩、第一/第三人称、Ragdoll 进入退出和起身完成 |
| 普通翻滚玩法 | `demo-rolling.log` 420 帧，接受 2/忙拒绝 2/完成 2，翻滚 114 帧、转向 57 帧；含空中门控、期望蹲伏保留和同帧 Root Motion |
| Overlay / 底层动作交接 | `demo-overlay-actions-10-v3.log` 十角色并行通过，持物 2400 帧、返回 Default，接受 50/替换 20/取消 20/完成 10，取消及 hold 均命中真实 Start，最终接触 610 帧 |
| 物理姿态边界 | `physics-world-pose.log/json`，8 组/160 bodies/192 captures/72 拒绝用例通过；最大位置差 4.952927e-6 m |
| Godot Optimize 构建 | 0 警告，0 错误 |

十角色摘要：pose `185EACBF86D4CA89`，root `8B8AAD0E466EA3B5`，result `651F9896CD2B47A9`。这是同输入调度一致性，不是 UE 原生整图对照。

截图人工检查覆盖前进、冲刺、蹲伏两向横移、站起、跳跃、落地和停止；未见整体骨骼飞离，空中仍使用旧姿态。仅证明已查看这些帧，不能关闭交错步、换髋延迟、滑步或头颈缺陷，也不是用户人工签收。未运行十分钟预算，HUD 的 workerMs 不作为性能证据。

## 接入中发现并修复的问题

- 首轮资源映射误要求 Refactored 与 V4 全部虚拟骨同名，初始化失败；改为物理骨验证、虚拟骨分别重建。
- 先后两个 `InlineArray` 默认比较在真实 Demo 第 31 帧抛异常，分别修上下文及机器快照；保留 `demo-stances-single-60*.log`。
- 首相机/布娃娃测试失败：用户场景 `Climbable2M` 物体整体缩放 2，刚体环境绑定拒绝。将其碰撞盒尺寸改为 2×2×2，网格子节点保留 scale=2，父刚体改为单位变换；场景位置及可见/碰撞尺寸保持原样。未回退其他用户地形改动。
- 首十角色测试退出 0 但日志有物理历史错误，**不计成功**。原物理姿态边界只容纳单个 Slot 的 1e-5 残差，新旧两层 Slot 组合观察到 1.27e-5。物理桥按两层省略权重给出 2e-5 每轴边界，保留动画组合后的平移，刚体方向仍正交化；原生动画 oracle 阈值完全未改。新增双层乘积的世界位置校验、3e-5/真实缩放拒绝及无部分发布检查。
- 普通 Demo/十角色 smoke 增加 BodyHistory.Failure 检查，防止动画结果通过却遗漏物理历史失败。修后 single/parallel 十角色日志无 ERROR/WARNING，姿态摘要与修改前相同，证明未修改动画输出规避检查。

初期蹲伏 profile、提交顺序及共享测试资源闭包的失败保留在各 `crouching-host-*.trx` / `shared-stance-crossfade.trx`；修后上述相关回归通过。没有运行全仓所有测试。

附加动作 smoke 首轮误把允许替换的底层 ActionPlayer 场景与 `--rolling-gameplay`（忙时拒绝）组合，预期不同而失败；移除冲突参数后，单角色模式的停用重试又错开该历史测试按墙钟安排的 action Start/hold。该专项原记录使用十角色、hold owner=2，与暂停 owner=0/1 分开；按原十角色条件重跑，失败日志仍保留。普通翻滚另由 `rolling_gameplay_smoke` 验证，不能以底层动作测试代替玩法门控。

## 本机复现

加载不上传的 `.env.local.ps1` 后构建，再运行普通入口：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true --no-restore
& $env:GODOT_EXECUTABLE --path . scenes/demo/als_demo.tscn
```

自动新链路检查使用 `scenes/tests/refactored_stance_demo_smoke.tscn`：引擎参数 `--headless --fixed-fps 60`，用户参数 `--stance-hz=60`；追加 `--stance-single` 检查单线程，去掉 `--headless` 并追加绝对 `--capture-dir` 可输出多帧画面。30/120 Hz 同时调整 fixed-fps 与 stance-hz。

下一批按 ROADMAP 的 R3 原 Grounded/Locomotion 外层和真实通知/足部反馈闭环继续，沿用当前普通入口，不再建立项目副本。
