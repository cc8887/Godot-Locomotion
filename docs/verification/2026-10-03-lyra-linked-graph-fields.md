# Lyra Linked 实例的图私有状态对照

2026-10-03，接续 ALS 人物与 Animation Interface / Layer 路线。新增实际图状态的只读候选视图，将此前已运行的 Idle、Pivot、Stride 和左手状态纳入完整 Main 的逐实例原生检查。整个移植目标继续 active。

## 实现与核验边界

Idle、Pivot、Start、Cycle 宿主暴露现有 pending 候选；对应 Pose 宿主直接转交其 Source 宿主的值。左手入口读取自身候选权重。没有候选时读取已提交历史，读取不创建图、不更新播放器，也不发布状态。

`LyraWholeMainDiagnosticSmoke --whole-main-graph-fields` 按原生 owner 身份定位真实 `LyraItemLayerGraphInstance`，读取这个实例自己的图宿主。新增十二字段：

| 来源 | 字段 |
| --- | --- |
| Idle | IdleBreakDelayTime、TimeUntilNextIdleBreak、CurrentIdleBreakIndex |
| Turn | TurnInPlaceRotationDirection、TurnInPlaceRecoveryDirection、TurnInPlaceAnimTime |
| Pivot | PivotStartingAcceleration、TimeAtPivotStop、StrideWarpingPivotAlpha |
| Start / Cycle | StrideWarpingStartAlpha、StrideWarpingCycleAlpha |
| LeftHand | LeftHandPoseOverrideWeight |

每帧比较 Before、Updated、After，取消重试再比较 Before、Updated，共五个阶段。非零 double 逐位相同，signed zero 数值等价；向量的三个分量均检查。索引由实际 int 值比较。原生动画字段只用于断言，初始化仍来自原资产与实际物理输入。

`verify-lyra-multi-owner-graphs.ps1 -GraphFields` 将十二字段成功标记与按帧数/实际实例数计算的比较次数加入进程门禁。原八 worker/图字段、三个预更新缓存、九个移动缓存及完整姿态检查继续同时执行。

七个宿主各新增一个只读访问器，诊断与验证脚本增加上述检查。本批没有修改图更新算法、状态机过渡、时钟或原精度门槛。

## 已完成的验证

定向 Debug 十四实例检查通过：三 Provider 共1080帧、每帧取消重试，新十二字段907200次比较；包含开火、ADS、图隐藏恢复、60帧 Montage overlap 及原 pre-rig 姿态/曲线/属性/root 门槛。

冻结32份源文件后，Debug 和实际 ExportRelease 构建均零错误、零警告。最终24个 Godot 进程全部终态退出0，无 Godot ERROR/WARNING：

| 检查 | 两构建合计 | 范围 |
| --- | ---: | --- |
| 作者输入完整图 | 16进程 | 四布局 single/three-groups/mixed/per-call，pre-rig与最终Rig两个边界 |
| 三频完整图 | 6进程 | 30/60/120Hz，各十四独立实例，pre-rig边界 |
| 普通十角色 | 2进程 | 每角色十四实例，60Hz/480物理帧，实际移动、换类、同类复用与取消重试 |

原生矩阵共32400提交帧和同数取消重试。新增十二字段18403200次比较；包含向量的字段每次检查三个分量。此前二十字段也按原门禁同时比较，指定快照累计核验32/47字段。

两份十角色完整报告分别与上批同配置逐项相同，Debug/Optimize相同。独立 `tools/verify_lyra_linked_graph_fields.py` 核验终态日志、字段计数、完整参考分片哈希、冻结源码、实际程序集及五轮六文件 Debug 恢复；869份资源JSON、710个原UE包与9项项目配置的字节哈希通过。结果为 `artifacts/lyra-analysis/linked-graph-fields-v1-integrity.json`，`auditPassed=true`。

本批没有新UE采集、资源重导、GPU/近景、managed全量、十分钟、性能或独立游戏导出测试。普通九组单角色场景未在本批重跑。

## 覆盖限制与下一步

十二新增字段中，已有参考实际改变七项：IdleBreakDelayTime、TimeUntilNextIdleBreak、PivotStartingAcceleration、TimeAtPivotStop和三项Stride alpha。CurrentIdleBreakIndex、三个Turn字段与LeftHandPoseOverrideWeight在这些完整图参考中始终不变；通过比较不能证明它们变化时的完整图语义。

下一步增加长待机、Turn/Recovery和启用左手覆盖的真实完整图输入。当前左手宿主明确拒绝非空覆盖Sequence，需要绑定真实Source occurrence，再纳入角色共同Sync和统一事务；不能只打开权重或填入参考值。

余下十五捕获字段为八项装备/图配置、RootMotionMode及六项引擎标志：DisableHandIK、EnableLeftHandPoseOverride、Hand FKWeight、RaiseWeaponAfterFiringDuration、RaiseWeaponAfterFiringWhenCrouched、StrideWarpingBlendInDurationScaled、StrideWarpingBlendInStartOffset、WantsIdleBreak、RootMotionMode、bPropagateNotifiesToLinkedInstances、bReceiveNotifiesFromLinkedInstances、bUseMainInstanceMontageEvaluationData、bUseMultiThreadedAnimationUpdate、bUsingCopyPoseFromMesh、bQueueMontageEvents。

其中事件队列状态必须按实际生命周期实现。本机UE5.8 `AnimInstance.cpp` 的 Montage_Advance置bQueueMontageEvents=true，TriggerQueuedMontageEvents才清除；已有原生十四实例首帧显示，隐藏实例也从false变true，After保持true。不能用Main派发后的状态替代Linked状态。本批仅定位该剩余边界，尚未实现或核验它。

完整47字段变化/初始化、动态配置和绑定组件生命周期、default/self/Unlink/部分覆盖整图、同函数多调用点通用执行、共享/持久实例与其他Provider拓扑继续开放。完整Chaos/Jolt同输入物理、复杂地形、近景握持、跨平台与原暂缓项保持开放。`fullPrivateFieldParity=false`、`fullPhysicalParity=false`、`goalComplete=false`。

## 复跑

使用新EvidenceTag保存新结果，不覆盖既有成功或失败证据；Godot进程顺序执行。

```powershell
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration Debug `
  -RunTag linked-private-v2-30-full -EvidenceTag <新标签> `
  -WorkerFields -PreUpdateFields -MovementFields -GraphFields -Boundaries pre-rig,final

# 三频十四独立实例：30Hz/multi-layer-v3-30-full；
# 60Hz/multi-layer-v3-60-repeat-full；120Hz/multi-layer-v3-120-full。
# 实际Optimize将Configuration设为Optimize，脚本finally恢复六份Debug文件并核验哈希。
```

最终运行脚本与日志为 `artifacts/lyra-analysis/linked-graph-fields-v1-run.ps1` / `linked-graph-fields-v1-run.log`；各矩阵、十角色报告、构建及独立审计证据由上述integrity文件登记哈希。
