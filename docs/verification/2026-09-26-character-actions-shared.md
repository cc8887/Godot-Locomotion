# 角色共享 Grounded 动作与 Transition Slot 边界

在主目录 main（基线 `4a7208c`）实施 R2 第一批。没有新建项目目录，没有修改导出 JSON 或原生轨迹。本批完成动作资源重绑定、共享物理 bank/queue、统一帧提交与外层 Slot 组件；R2 全阶段仍未关闭。

## 实现和真实图位置

`AlsRefactoredCharacterActionProfile` 合并 Standing 的 Turn/Dynamic/Stop/QuickStop 与武器过渡资源。资源按来源路径去重，外部提供角色动画 ID 和 Grounded group ID；共享同一个来源时必须具有相同 Slot、duration 和 additive policy。编译保留 Refactored catalog 身份、原生 79 骨父序及完整曲线并集，拒绝缺失、别名 ID 和不一致资源。

Standing 的 `BindActions` 复用不可变图资源，将原局部编号映射为角色编号。新角色动作 runtime 注入唯一的 bank/queue；Standing 不再 Begin/Commit/Discard 共享 bank，公开的子图直接提交/后处理入口也会拒绝共享模式，必须经过角色协调器。独立 Standing 测试入口保持可用。

协调器先检查 Standing、Transition Slot、队列和 bank 全部候选，再一起提交。worker 请求按调用顺序覆盖同一个队列；主线程沿用 Transition→Turn→Stop，再派发 Standing QuickStop 的顺序，其他武器主线程通知随后执行。停止阻塞时保留待播放请求；本帧 Begin 冻结的快照不被后处理新播放改写。错误输入、采样故障或非法生产者导致整帧丢弃，可以用同身份重试。共享模式直接取消 Standing 也会取消整个共享帧，避免重试遗留队列请求；协调器提交后的内部清理则只清子图候选。此协调器只管理动作/子图事务，没有另建 Godot 线程调度器。

核对已导出的 AB_Als 编译图及编辑图后，实际位置为：

```text
Standing(5) / Crouching(4)
    → Grounded linked graph(6)
    → Transition Slot(13)
    → Locomotion linked graph(7) 的 Grounded Input
```

`AlsRefactoredTransitionSlotGraph` 校验 node13 的原输入/输出链接、Slot 名、source 更新策略和无回调规则，并交叉核对编辑图连接。`AlsRefactoredTransitionSlot` 读取唯一共享快照，实际采样和混合 mesh additive 姿态/曲线；保留 Slot source 更新上下文、Inactive 标志、update-only 与重复求值。惯性请求作为明确输出暴露，不能在不知道外层接收节点的情况下伪造消费。

本批连续集成测试在 Grounded 接口注入真实 Standing 输出作为**受控基底**。它验证 Slot 混合和共享所有权，不等于执行了 Grounded 站蹲选择/姿态写入，也不等于新 Locomotion/完整角色链路已接通。

## 验证

新增角色测试使用反向排序、从 700 起间隔 3 的非连续 ID，Grounded group ID 为 20。覆盖完整资源绑定、30/60/120 Hz 各 11 秒、多实例重叠、真实 Stop/Dynamic/QuickStop、跳过求值、重复求值、后处理后整帧取消重试、两个独立角色 owner 对照。另覆盖缺少 Slot 更新时禁止局部提交、采样晚期失败、同身份恢复、角色/代次/帧号/delta 错误、武器站立待机门控、worker/main 顺序及五类图变异拒绝。

原 Standing 原生连续测试新增共享角色 bank 模式。独立与共享各三频率，共 4620 帧/280134 次骨骼求值。原资源哈希和严格阈值照常验证；共享模式仅比较 Standing 子图输出，不把未求值的外层 Slot 算成 UE 整链通过。

| 共享模式 Hz | 最大位置差（cm） | 最大曲线差 | Parent 已比较字段/播放时钟差 |
|---|---:|---:|---:|
| 30 | 9.1195543e-6 | 3.5762787e-7 | 0 / 0 |
| 60 | 9.7340897e-6 | 3.5762787e-7 | 0 / 0 |
| 120 | 9.6676729e-6 | 2.3841858e-7 | 0 / 0 |

位置仍为 `2e-5 cm`，旋转/缩放/曲线仍为 `2e-6`，未放宽或跳过。

- 初轮 `character-actions-initial.trx`：16 通过、0 失败、0 跳过。
- 扩展 `character-actions-related.trx`：70 通过、0 失败、0 跳过；包含新增角色共享/边界用例、独立和共享 Standing 原生六组、旧 Standing 生命周期，以及 RestMontage/TransitionPose/QuickStop 相关回归。
- 收尾补充公共 Standing.Cancel 的共享帧取消语义后，`character-actions-cancel-final.trx`：27 通过、0 失败、0 跳过，重新覆盖角色动作、Standing 生命周期及六组原生对照。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

TRX 位于 ignored `artifacts/tests/character-actions/`，测试使用 Release 和 `DOTNET_TieredCompilation=0`。首次 Import 编译有两处不存在的 `StandingIdleOnly` 属性引用；核对原武器通知编译门禁后改为其固定 `true` 策略，后续编译通过，未改变原动作语义。没有运行全量 solution 测试；最终 `git diff --check` 通过，导出资源无修改。

## 下一批

继续真实 Grounded 上游（Standing/Crouching 输入、原回调/曲线/相关性）与 Transition→Locomotion 的组合，再导出新的原生连续链路。把 Mantle/Roll 等非 Grounded 动作纳入角色资源/通知/Root Motion 所有权时，继续扩展同一协调器；本批不能作为完整角色 profile 的验收。

普通 Demo 尚未切换，没有 Godot 运行、多帧画面、人工或十分钟性能验收。本批未启动或修改 UE。Ragdoll/Get-up/Pose Recovery 等旧目标保留；音频、道具物理、头颈专项继续按用户要求暂缓。
