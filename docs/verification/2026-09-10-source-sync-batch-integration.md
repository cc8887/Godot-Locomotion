# 第二十六批：源初始化与多组 Sync 接线

## 结果与边界

实际 Demo 的 Standing Cycle 已从单组调用迁到共享多组入口，仍使用原有候选帧与
Controller/Worker 提交事务。Detail 资源组件也改用同一入口，一次提交三个命名组。
本批没有新增动画时钟或事件队列，没有改变相机、输入或 P5 物理布局。

这不是完整 Standing/Detail/Stop 图的生产接线。Cycle 当前仍为七个播放器、25 个
采样来源；完整 Cycle 的 Lean 和 Sprint Impulse 尚未接入 Demo。外层 MovingWeight
仍为临时 MoveToward，Detail/Stop 联合测试的上游仍含夹具。运行摘要保持原值，
本批没有新的多帧视觉捕获，不宣称起步滑移、交错步或上身动作已经修复。

## 实现

- `AlsAssetSourceInitialization`：复用 SequencePlayer 秒时间与 BlendSpacePlayer
  归一化时间的初始化规则，包括未钳制起始位置恰为零时的反向末尾起播、倍率与
  PlayRateBasis；提供受检 epoch 递增。调用方仍拥有缓存权重、滤波器、样本和标记历史。
- `AlsSyncRuntime.TryEvaluateAssetSyncBatch`：按声明的组顺序汇总输入，保持各组内
  原始注册顺序，复用既有混合 Sequence/BlendSpace 同步算法。输出历史范围是批次全局
  索引；组不活跃时仍处理其历史退出。所有组成功才复制输出，后组失败不会发布前组，
  输入历史与输出缓冲重叠时同样成立。
- `AlsStandingCycleGraph`：实际时间映射接入初始化助手和批次入口，保留七源候选
  历史布局及已有提交/丢弃边界，不改变源权重或另算相位。
- `DetailMachineSmoke.SourceState`：移除逐组 Tick 循环和分组历史存储，改为一份
  扁平候选历史；显式状态清权重语义保留。它仍是组件适配器，不冒充生产 P5 所有权。

批次容量保护为 64 组、512 播放器、2048 样本；单组仍限制 128/512。全局身份唯一性
用复用的栈上整数排序检查。容量不是最终 P5 槽位，也不是十角色性能验收。

初始化规则依据本机 UE 的 AnimNode_SequencePlayer、AnimNode_AssetPlayerBase 与
AnimNode_BlendSpacePlayer 源码；本批没有重新运行 UE 或新增原生初始化探针。
范围限于导入合同中的默认倍率钳制配置，不包含姿势匹配初始化。初始化不会隐式清除
CachedBlendWeight，也没有把所有生命周期重置简化成重置时间。

## 验证

TRX 位于 `artifacts/test-results/source-batch/`：

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema 两类 | 1691/1691 |
| Import 全套 Debug | 695/695 |
| 初始化、多组 Sync、原生轨迹相关 Release | 31/31 |
| Godot 构建 | 0 错误、0 警告 |

31 项 Release 包括 17 项批次测试、12 项初始化测试和原生轨迹直接/批次两个入口。
已有 UE 混合 Sync 的 1152 帧原生期望值经新入口回放一致；这是已有夹具的新增接线
覆盖，不是本批新导出的整图轨迹。测试仍排除无组 Lean 的 scenario 6。

批次测试覆盖交错组输入、全局样本范围、空帧退休、别名提交与重试、错误历史、身份
冲突、晚组失败不改输出、每组及总量上限，以及预热后 1000 次调用零分配。

Import 新增 30/60/120 Hz 共 840 帧联合源测试，使用真实编译绑定中的 24 播放器与
42 样本（八个命名 Cycle 源和 16 个 Detail 源），覆盖交错注册、组合切换及全部退出。
贡献权重是测试输入，不是完整 AnimBP 的运行结果；该局部数量不冻结 P5 全图布局。

实际资源组件结果：

```text
STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions=9 wait_frames=371 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603 source_timing=15216 timing_authority=1260 movement_frames=5040 sync=asset_runtime
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
```

新 Cycle 在 single/parallel 均通过 180 帧 Worker 顺序检查，结果摘要均为
`C658034A39C5917B`，完整姿势摘要均为 `076E345A0151A012`，与迁移前相同。
两种模式的晚期失败注入均确认 source_sync/movement_input 以及控制器、姿势和
P4 缓冲恢复。日志为 `artifacts/source-batch-{single,parallel}*.log`。

完整 `verify-p4-pose.ps1` 通过，包含图、曲线、转身、双模式脚部、双模式晚期事务；
拓扑与活动姿势各 10000 次检查均为零分配，活动检查耗时 4650.607 ms。此为旧 P4
受控优化构建门禁，不是 P7 最终十分钟预算。预期注入的诊断不是未处理回归。

相机/输入 SHA-256 未变：

```text
AlsOrbitCamera.cs       6ED2DB72FF9D551C9E2D540BE6EC5889CD0CA9FC8A67425508C9A1D258BED666
AlsPlayerInputAdapter.cs BEE5F84E9FA5ABCF6D46C5209F222A51F6B10B5C64D1B10517A81779FA3BC8DB
```

本批没有修改/构建 UE 插件、重新导出资源、提交或撤销 Git 更改。

## 下一步

先补无同步组来源的独立 Tick；现有原生 BlendSpace 夹具 scenario 6 已包含 Lean，
应复用这份真实证据。不能把所有 NAME_None 来源塞进一个虚构同步组。Stop 的固定
TeleportEvaluator 也不能套用播放器的时间推进和 Notify 语义。

随后继续完整正式源布局、生产初始化/历史所有权和一次 Gather/Worker/Commit，
把实际 Main/Slot、Standing/Detail/Stop、惯性化、姿势/曲线与通知窗口接入同一事务。
新入口只是其中命名组的共同入口，没有完成该整体任务。继续原计划动态分层、Overlay、
P5C/P6/P7；保留同输入 UE/Godot 整图轨迹、多帧移动与人工视觉验收。
