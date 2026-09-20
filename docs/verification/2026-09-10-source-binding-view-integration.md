# 第二十八批：不可变源视图接入实际 Cycle

## 结果与边界

现有源 Profile 的不可变数值表已通过 Core 视图提供给实际 Cycle。Godot 不再为活动
Cycle 来源手工构造资产身份、Sync 元数据和播放倍率；它提供图贡献、源时间/epoch
及已解析样本，Core 使用同一源快照生成 Tick，再进入既有共享 Sync。

实际候选历史新增源快照标识，包含 Core 视图版本、SkeletonId 及完整 SHA-256 四段
数值。标识随现有 Cycle 候选提交和回滚，不增加时钟或队列。不同快照的已初始化
历史在下一次 Prepare 时被拒绝。

这不是完成了完整 P5 快照或物理 occurrence 布局。旧 Base22 编译器仍明确拒绝新图，
局部 37 播放器/59 样本身份仍未提升为全局 P5 handles；实际 Demo 仍是七个 Cycle
来源和临时 MovingWeight。Lean/Sprint Impulse、完整 Standing/Detail/Stop、动态上身
及源事件生产接线仍未完成。本批无新的视觉捕获或滑移改善结论。

## 实现

- `AlsLocomotionSourceProfile.CreateCoreView()`：直接借用既有私有不可变数组，无逐帧
  克隆。旧公开数组访问器仍保留防御性复制，新增属性提供关联动画集摘要。
- `AlsLocomotionSourceView`：只读 Span，覆盖全部局部播放器、样本、Sync 绑定、命名
  组 ID、序列和标记表；`AlsLocomotionSourceStamp` 无引用字段，保留完整 256 位摘要。
  它的版本是新 Core 视图合同版本 1，不改变原 Profile 摘要载荷版本 2。
- `AlsLocomotionSourceUpdate/AlsLocomotionSampleUpdate`：纯数值图贡献，不携带重复
  资产元数据；播放器 time/epoch、相关性/重置仍由调用方候选状态拥有。
- `AlsLocomotionSourceRuntime.TryBuildTicks`：验证预期快照标识、来源和样本归属、
  样本顺序/唯一性、状态时间和权重；从绑定解析 Constant/StandingPlayRate、Basis、
  资产及样本倍率和 Sync 配置。先暂存后写出，错误输入不会部分修改输出。
- `AlsStandingCycleGraph`：实际七源构造迁到该 Core 路径；组历史仍在现有 Controller/
  Worker 事务内。`StandingCycleSmoke` 比较候选标识，并主动损坏最后一段摘要验证拒绝。
- `DetailMachineSmoke.SourceState`：同样以图贡献接入共享构造入口，保留组件测试的
  状态/权重/epoch 所有权，不冒充完整 P5 生产运行时。

Core 视图是受信任的 Import 编译输出视图，不会逐帧重算整个数组的密码学摘要。
原始构造器仍遵循项目数值视图惯例；快照标识用于绑定身份与失配检测，不是任意外部
伪造 Span 的安全认证。源数据整体正确性由严格编译器和不可变所有权保证。

Sequence 倍率按本机 UE `AnimNode_SequencePlayer.cpp` 的 UpdateAssetPlayer 规则处理：
近零 Basis 得到零倍率，之后资产倍率由 Tick 应用。BlendSpace 不使用 Sequence Basis。
现有导入合同仍要求正 Basis、默认倍率钳制配置，不宣称支持所有 UE 节点动态配置。
Stop TeleportEvaluator 保留在视图中，但不能交给普通播放器 Tick 构造入口。

## 验证

Core 新增 25 项：完整快照标识的各字段失配、图贡献与源元数据转换、来源/样本错属、
重复、非有限值、非法倍率和时间、Teleport 拒绝、晚期失败输出不变、短输出缓冲、
近零 Basis 和预热零分配。Import 新增五项：所有表一致、完整摘要字节还原、防御性
副本不污染借用视图、来源摘要变化拒绝旧标识、12 个真实 Stop 求值器拒绝，以及
10000 次视图创建零分配（最后两项分别对应一个测试）。

既有联合源测试的六种配置（30/60/120 Hz，含/不含 Lean）共 1680 帧改走新构造
入口，其输出与直接按绑定构造的原 Tick 逐字段一致；实际 Sync 输出仍与原路径重试
一致。九个 Cycle 和十六个 Detail 来源均经过此构造合同，权重仍是测试夹具，不能
等同于完整 AnimBP 贡献或 Demo 接线。

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1733/1733 |
| Import 全套 Debug | 703/703 |
| 源视图相关 Import Debug | 43/43 |
| 源构造、共享 Sync、独立 Tick 相关 Release | 73/73 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

TRX 位于 `artifacts/test-results/source-view/`。首轮 Godot 构建在新增测试中发现修改
record struct 属性返回值的 CS1612 错误，改成局部值复制后重新赋回，再构建通过。
没有修改测试容差或跳过新用例。

实际资源结果：

```text
STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions=9 wait_frames=371 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603 source_timing=15216 timing_authority=1260 movement_frames=5040 sync=asset_runtime
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
```

Cycle 的 single/parallel 各 180 帧通过，result=`C658034A39C5917B`、完整姿势
`076E345A0151A012` 均未改变。两种模式晚期失败注入验证 source_sync、movement_input、
controller、pose、p4_banks 恢复。日志为 `artifacts/source-view-*.log`。

完整 `verify-p4-pose.ps1` 通过图、曲线、转身、两种模式脚部与晚期事务、姿势门禁。
拓扑/活动姿势各 10000 次零分配，活动耗时 4622.011 ms；不代替 P7 最终预算。

相机与输入文件 SHA-256 与前批一致。没有修改/构建 UE 插件、重新导出资源、Git 提交
或撤销用户修改。`git diff --check` 通过，仍有仓库既有 LF/CRLF 提示。

## 下一步

源表到 Core Tick 的实际接线已完成，不再重复制造独立的源快照包装或同步 facade。
继续依据整图来源清单扩展真正的 P5 物理布局与主快照，把源/样本身份、事件窗口和
状态生命周期纳入原有 Controller/Worker/P5 事务；不能仅移除 Base22 拒绝条件，或
按 AnimationId 合并不同来源。

同时推进完整生产图所需的源候选历史与贡献调度，使已有缓存、Standing/Detail/Stop、
惯性化组件消费实际 Main/Slot/Cycle/Lean/Sprint Impulse 姿势，而非上游夹具。之后
继续动态上身分层、Overlay/道具、P5C/P6/P7，保持整图原生轨迹和人工视觉验收开放。
