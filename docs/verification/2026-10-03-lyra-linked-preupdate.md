# Lyra Linked 实例的游戏线程预更新

2026-10-03，继续在主目录推进 ALS 人物上的 Lyra Animation Interface / Layer 实现。沿用 ALS 的 68 skin / 69 raw / 81 logical 布局；本批没有更换人物、重导资源或修改 UE 探针。

## 原生时序与生产修正

原 `CanPlayIdleBreak.bplisp` 明确将三个 PropertyAccess 读取标为 `Batched_GameThreadPreEventGraph`。本机 `AnimSubsystem_PropertyAccess.cpp` 在 `OnPreUpdate_GameThread` 执行这一批读取；`AnimInstance.cpp` 的首次动画根访问 worker 更新属于后续阶段。它们需要各自的实例历史。

| 原缓存字段 | 原读取 | 本批 Godot 输入 |
| --- | --- | --- |
| K2Node_PropertyAccess_48 | Main.IsAnyMontagePlaying | Main 当前 Montage advance 前的实例存在性 |
| K2Node_PropertyAccess_49 | Main.HasVelocity | Main 上次提交的速度状态 |
| K2Node_PropertyAccess_50 | Main.IsJumping | Main 上次提交的跳跃状态 |

新 `LyraLinkedPreUpdateHost` 属于每个实际 Linked 图实例，初值读取原资产 CDO。Main 保留当前更新前的观察历史，并从绑定的 Montage bank 读取推进前的实例存在性，向全部实例准备三个缓存值。这个快照位于 prerequisite 播放/停止请求之后、advance/移除实例之前；不能只读 committed 数量，否则会漏掉普通玩法本帧的新播放请求。即使某实例没有实际根访问，预更新候选也会提交；首次根访问的五个 worker 字段仍按原访问条件更新。

Idle 图的 `CanPlayIdleBreak` 使用这三个缓存；Crouching、ADS、Firing 等原非批量读取仍使用该图访问当刻的 Main 观察。此前直接使用本帧速度、跳跃及推进后的 Montage 状态，混合了两个执行阶段。当前修正保留原根回调、共同 Sync、Montage bank 与唯一骨架发布路径。

预更新与 worker 使用相同的候选帧凭据。worker 访问必须匹配预更新身份；全部实例由同一角色预校验和提交，失败、退休及取消重试撤回候选。没有新增动画时钟或独立提交。

## 新的原生参考

新 wrapper 复用原 capture 脚本及冻结的 C++ 探针包，只补充 Montage 资源准备与作者输入。原 Python、PowerShell 采集脚本和旧参考保持字节；wrapper 与新 runner 的实际 SHA256 分别记录。第一次 `linked-private-v1-30-full` 已完整执行逐帧对照，但最后门禁要求 Montage overlap，作者输入遗漏了这一项；失败报告、源快照和参考保留。新 `capture_lyra_linked_private_v2.py` 在 FullBody Emote 内加入独立 AdditiveHitReact Slot 的受击叠加，最终参考标签为 `linked-private-v2-30-full`。

两次 UE 5.8 commandlet 均成功退出 0、0 资产保存；最终一批 Unarmed/Pistol/Rifle × single/three-groups/mixed/per-call 共 12 条原完整图轨迹、4320 帧。每条轨迹实际覆盖 63 帧 Firing、75 帧 ADS、66 帧 Crouching、55 帧移动图隐藏、一次恢复及 20 帧 Montage overlap，同类 Link 继续保留实例。Montage 缓存为 true 的帧数分别为 150、150、130。输入是作者物理观察和玩法标志，未回放动画结果驱动 Godot。

只读 inventory 分别检查旧参考和两批新参考的全部 47 个数值/布尔/向量字段。新参考中三 Provider 未访问实例的缓存 48/49/50 也实际变化，证明预更新不能仅在 worker 访问时执行。字段库存和变化统计不等于这 47 项全部已有生产实现或逐值验收。

新 UE 日志没有 `: Error:`，仍有 3254 条 Warning，包括 transient 动画压缩依赖及受控角色缺少装备上下文的原 Notify 警告。本批未修复这些夹具/原资产警告，也未使用日志干净程度替代结果检查。

## 验收边界

`--whole-main-preupdate-fields` 将三个真实候选/提交值与原实例 Before、Updated、After 快照逐项比较。与原八字段参数一起，可经 `verify-lyra-multi-owner-graphs.ps1 -WorkerFields -PreUpdateFields` 执行。参考快照只用于断言，生产输入仍来自角色 Main 和真实 Montage bank。

最终 Debug / 实际 Optimize 构建均为 0 警告、0 错误。固定最终源码后，64 个 Godot 进程全部成功退出 0，日志无 Godot ERROR/WARNING：

| 验证 | 两构建合计 | 实际范围 |
| --- | ---: | --- |
| 旧三 Hz 原生参考 | 24 进程 | 四布局 pre-rig，原骨骼/曲线/属性/root 门槛与逐帧取消重试 |
| 新作者输入原生参考 | 16 进程 | 四布局 pre-rig / 最终 Rig，实际开火、ADS、隐藏恢复及 Montage overlap |
| 普通绑定与玩法 | 22 进程 | 原绑定、三 Provider × 三 Hz、普通十角色 |
| 无组逐调用点十角色 | 2 进程 | 每角色 14 实例、60 Hz、480 物理帧，换类及取消重试 |

原生矩阵共 77760 提交帧及逐帧重试；八个原 worker/图字段共 17107200 比较，三个预更新缓存共 6415200 比较。比较覆盖每实际实例五个 Before/Updated/After 阶段快照。原精度没有放宽，已有 Montage 覆盖与 overlap 门禁没有删改。二十份普通完整报告和逐调用点十角色报告与前批完全相同，Debug / Optimize 完整报告相同。

`AlsMontageBeforeTickTests` 的 6 项定向 Core 测试全部通过，包含本帧 prerequisite 新播放、立即停止、Discard 后拒绝查询及取消重试的快照边界。未重跑全量 managed 测试。

独立审计 [linked-private-v2-integrity.json](../../artifacts/lyra-analysis/linked-private-v2-integrity.json) 为 `auditPassed=true`：22 份冻结源码、140 份验证证据、最终程序集与六轮六文件 Debug 恢复一致；869 旧资源 JSON、710 原 UE 包、9 项项目配置、原探针/旧参考/NativeMath 保持 SHA256。新参考的 12 个分片哈希和实际采集来源也通过核验。`fullPrivateFieldParity=false`、`fullPhysicalParity=false`、`goalComplete=false` 保留。

实现入口为 [实例预更新](../../src/Als.Godot/Animation/Lyra/LyraLinkedPreUpdateHost.cs)、[Main 时序](../../src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs) 和 [Montage 推进前快照](../../src/Als.Core/Actions/AlsDynamicMontageRuntime.cs)；执行脚本及最终二进制哈希见上述审计与各 matrix / ordinary verification 文件。

本批范围为原三个 Provider、十四入口、1/3/4/14 实例及上述输入下的预更新时序。其余 PropertyAccess 私有缓存、Idle/Turn/Pivot/Stride 的完整字段审计、不同类替换的初始化缓存、default/self/Unlink/部分覆盖整图、共享/持久实例、任意 Provider 拓扑及同函数多调用点的通用执行器仍开放。

后续按原执行阶段逐项推进：先追查 inventory 中隐藏实例仍变化的加速度、速度、摩擦与停止速度缓存，确认初始化回调及读取阶段；再对齐 Idle/Turn/Pivot/Stride 的完整私有历史；最后扩展 default/self/Unlink/部分覆盖和不同 Provider 拓扑的完整图参考。部分停止缓存在原首个 Before 快照已与资产 CDO 不同，需要解释原初始化路径，不能直接用 CDO 或参考常数填充运行状态。通用图调用继续以节点身份路由，同函数多个调用点分别保留实例历史。

完整 Chaos/Jolt 同输入物理此前仍有 314/1680 帧差异；地形、近景握持、其他 Provider、GPU/人工验收、全量 managed、十分钟、性能、独立导出和音频/道具物理/头颈暂缓项保留。整体移植目标仍 active。
