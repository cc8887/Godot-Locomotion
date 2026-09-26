# 共享 Source Sync 接入生产宿主

基线：`main / 9215664`。本批在主目录实现，没有创建新项目或 worktree；保留用户的漫游 NPC、HUD、场景和其他未提交修改。

## 结果与边界

普通 Demo 的 `AlsRefactoredDemoStances` 通过原有 `LocomotionHostProfile.CreateRuntime()` 入口，现默认使用一个角色级 Source Sync bank。Standing Movement/Rotate、Crouching Movement/Rotate、Grounded 两条 stance 序列、Locomotion 空中源共六类 owner 均已接入。不是仅增加离线收集器或把各子图事件事后拼接。

这一批完成共享源时钟和姿态接入，**尚未把原源通知汇入角色级 Montage/Notify 队列，也没有移除旧移动通知兼容更新**。上身、完整主图连续 UE 对照及 R2–R7 其他缺项仍按 roadmap 推进。音频、道具物理、头颈拉长专项继续暂缓。

## 实现

- 实际节点访问时登记源输入及完整图上下文。Details additive、Direction/Forward、Rotate、Grounded 和 Air 各有真实登记点；deferred cache 之前的直接源保持原访问顺序。同名 Movement 跨站蹲共同选主，组插入顺序由外层范围维护。
- 子图持有 `IAlsRefactoredSourcePlayers` 局部地址视图，只映射局部 player ID；物理 bank 独占时间、epoch、BlendSpace 过滤和样本历史、姿态采样。视图不执行独立 Sync，也不能提交共享 bank。
- 子图遍历仅收集并核对源库存。Locomotion 完整遍历结束后执行一次 `Sources.Complete()`，随后捕获 Details/Rotate/Grounded/Air 下一更新读取的时钟。隐藏帧也完成空 tick，退休本帧组成员并保留节点自己的历史和待初始化标记。
- Standing Lean 的 Parent 参数仍在该子图遍历时采集，不延后读取可能被另一个 linked graph 刷新的共享 Parent。仅依赖源时间的阶段延后至 Sync 完成。
- Rotate 和 Grounded 直接源补传图上下文的 `RequestedInertialization`，保留实际 Inactive、状态作用域和权重。
- 在角色统一验证后依次发布子图和物理 bank；取消会丢弃所有候选。独立 Standing/Crouching/Grounded/Pose 测试入口仍可使用原独立 runtime，保持受控原生基线。

## 验证

本机证据目录：`artifacts/tests/refactored-source-hosts/`，不作为资产提交。

| 检查 | 结果 |
|---|---|
| Optimize Godot 构建 | 0 警告、0 错误 |
| 首轮宿主、共享 scope、Standing 原生相关测试 | 27 通过，`hosts-first.trx` |
| 新真实宿主三频率测试首轮 | 3 通过，`shared-hosts.trx` |
| 最终宿主、独立 Standing/Pose、Standing 原生及视图生命周期 | 28 通过，`hosts-final.trx` |
| CharacterAction 回归 | 14 通过，`character-actions.trx` |
| 普通 Demo 30/60/120 Hz | 850/1700/3400 帧通过；Grounded mask 30、Locomotion mask 31；各 3 次 Pivot、4 次动态补步 |
| 十角色移动平台 single / parallel | 各 3621 帧，2 次取消、1 次提交暂停；三个摘要完全一致 |
| Camera / Ragdoll 恢复普通入口 | 60 Hz、480 帧通过，包含布娃娃及第一人称切换 |
| 实际渲染 | 60 Hz、1700 帧通过，生成 35 张截图；抽查 12 张覆盖冲刺、站蹲、跳跃落地、换向及蹲姿瞄准 |

新宿主测试每频率 8 秒，共 1680 个提交帧，每帧取消重做并与无取消宿主比较。实际覆盖：站蹲同帧 Movement 成员、主播放器跨 owner 切换、空中直接源先于 deferred 地面源、隐藏范围空 tick、完整节点上下文、pose/curve/player/sample/group/order 候选一致。另验证视图在外层 tick 前不可求值/提交、漏登记或篡改输入拒绝、视图 Commit 不发布物理 bank、取消重试不增长 epoch。

十角色摘要：pose `561699D6F82CF4BE`，root `46D6E1BA1FED127A`，result `6197CB7DA7B5A89A`。相对平台帧 3101，旋转历史帧 3081；这是生产多线程确定性证据，不是 UE 完整角色轨迹等价或十分钟性能验收。

30/60 Hz 首轮 Demo 在最后将 Standing Lean 采集恢复到遍历时点之前运行。修订后已补跑 30 Hz（`demo-final-30.log`）；60 Hz 实际渲染、120 Hz、多角色及最终相关单元测试均使用修订后版本，三频率均完成最终代码验证。

相机日志 `camera.log`，实际渲染日志 `render.log`，截图在 `render/`。抽查帧：110、165、210、350、390、895、485、495、545、555、1400、1575；可见不同动作输出，蹲姿瞄准的 1400/1575 帧仍裁切下肢，不将截图生成或数据断言通过视为全部视觉验收。保留该相机取景问题，后续完整相机与人工矩阵处理。

本批没有 UE C++ 修改、UE 启动或新导出，没有执行 Core/Import 全量、十分钟预算或人工验收。现有严格 Standing 原生门槛未修改；本批测试未发生失败。已完成的 Godot 日志没有 ERROR/WARNING/Exception。

## 下一步

由当前共享物理 bank 产生真正的源 Notify tick，按实际 Sync 顺序、主从过滤、样本与 epoch 身份进入统一角色队列，合并 Montage 通知和类型化消费者，然后移除旧移动通知时钟。继续 Crouching/Grounded/Locomotion/Parent 连续原生对照和原上身／最终脚部等完整链路任务。
