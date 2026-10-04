# Lyra Linked 实例的移动组件缓存

2026-10-03，继续按 ALS 人物和原 Animation Interface / Layer 路线实施。68 skin / 69 raw / 81 logical 布局保持，沿用上批原生完整图参考；本批没有启动 UE、重导人物或改变动画资源。

## 原读取与初始化

原 `UpdatePivotAnim.bplisp` 从 `GetMovementComponent` 读取 GetCurrentAcceleration、GetLastUpdateVelocity 和 GroundFriction；`GetPredictedStopDistance.bplisp` 读取 LastUpdateVelocity、独立制动开关及四项制动参数。`GetMovementComponent.bplisp` 自身不支持线程安全调用，并通过另一个 PropertyAccess 缓存访问 Pawn 的移动组件。本机编译器在自动读取的端点不支持线程安全时，将它们归入游戏线程预更新批次。

本机 `AnimInstance.cpp` 的 InitializeAnimation 在初始化图之前执行 PropertyAccess 的 `OnPreUpdate_GameThread`。原 1/3/4/14 实例参考的首个 Before 快照中，Pivot 三缓存仍为 CDO，停止缓存已经包含实际组件参数；完整更新之后，各实例的九缓存均取本帧物理输入，隐藏实例也刷新。结合原 `GetMovementComponent` 的缓存依赖，这符合 Pivot 25–27 在移动组件引用 57 尚未建立时保留初值、停止 60–65 在其后成功读取的顺序；本批按这一具体合同实现，没有导出通用 PropertyAccess 调度器。

| 原字段 | 实际输入 | 初始化与消费 |
| --- | --- | --- |
| 25 | 组件当前加速度，double XYZ | 首批保留原 CDO；完整预更新后供 Pivot 使用 |
| 26 / 60 | 组件 LastUpdateVelocity，double XYZ | 26 首批保留 CDO，60 读取绑定组件；后续各自保存 |
| 27 / 63 | GroundFriction，float 提升到 double | 27 首批保留 CDO，63 读取绑定组件 |
| 61 | bUseSeparateBrakingFriction | 初始化及各帧预更新读取组件 |
| 62 / 64 / 65 | BrakingFriction / Factor / DecelerationWalking | 初始化及各帧预更新读取组件 |

资产 CDO 与绑定组件的配置分开。生产入口显式传入 Shooter 配置和角色实际初始速度；诊断使用原采集角色的物理配置，未读取动画缓存、骨骼结果或时钟填充运行状态。首轮诊断暴露普通 ACharacter 与 Shooter 的独立制动设置差异，修正为显式初始化输入后复测；失败日志保留。

## 生产接入

`LyraLinkedPreUpdateState` 在已有 Montage/速度/跳跃三个缓存之外，保存实际 Pivot 和 Stop 组件快照。每个实际实例按原 CDO 构造，绑定后执行一次初始化批次；同类重 Link 保留对象，换类的新实例执行初始化。Main 保存供替换初始化使用的最近提交组件快照，并用真实本帧移动观察向所有实例准备新候选。

Stop 与 Pivot 图从按调用节点路由的对应实例借用预更新快照。它们继续使用原角色共同 Sync、播放器、状态机和统一提交/取消，未增加新时钟或骨骼发布器。初始化不能重复，非有限快照拒绝；候选身份、取消重试及退休门禁沿用上批。

## 验证记录

新增 `--whole-main-movement-fields` / `verify-lyra-multi-owner-graphs.ps1 -MovementFields` 比较九字段，向量各三个分量均按原 double 位值检查，signed zero 数值等价。旧三个预更新字段和八个 worker/图字段门禁保持。九字段比较覆盖原实例 Before、Updated、After，以及取消后重试的 Before、Updated 五个阶段。

十四逐调用点实例的 Debug 定向检查通过：1080 参考帧、逐帧取消重试，九字段 680400 比较、旧八字段 604800、三个预更新字段 226800；完整原图 pre-rig 姿态、曲线、属性和 root 门槛保持，实际 60 帧 Montage overlap。

固定最终 26 份源码后，Debug / 实际 Optimize 构建均为 0 警告、0 错误。最终 64 个 Godot 进程全部成功退出 0，日志无 Godot ERROR/WARNING：

| 验证 | 两构建合计 | 范围 |
| --- | ---: | --- |
| 旧三 Hz 原生参考 | 24 进程 | 四布局 pre-rig，全部原骨骼/曲线/属性/root 门槛及逐帧取消重试 |
| 既有作者输入原生参考 | 16 进程 | 四布局 pre-rig / 最终 Rig，开火、ADS、隐藏恢复和 Montage overlap |
| 普通绑定与玩法 | 22 进程 | 原绑定、三 Provider × 三 Hz、普通十角色 |
| 无组逐调用点十角色 | 2 进程 | 每角色 14 实例、60 Hz、480 物理帧，换类及取消重试 |

原生矩阵共 77760 提交帧及逐帧重试；九个移动缓存 19245600 字段比较、旧三个预更新缓存 6415200、八个 worker/图字段 17107200。向量每次字段比较检查三个分量。指定快照中共 20 个字段经过对照，不是全部 47 项私有字段验收。二十份普通完整报告和逐调用点十角色报告与上批完全相同，Debug / Optimize 完整报告相同。

独立审计 [linked-movement-v1-integrity.json](../../artifacts/lyra-analysis/linked-movement-v1-integrity.json) 为 `auditPassed=true`：26 份冻结源码、139 份证据、最终程序集与六轮六文件 Debug 恢复一致；869 旧资源 JSON、710 原 UE 包、9 项配置、原探针/参考/NativeMath 的 SHA256 保持。Core Montage 实现及其测试源码与上批哈希相同；本批没有重跑 managed 测试。既有 UE 参考的 3254 条夹具 Warning 继续保留，本批没有新 UE 采集。`fullPrivateFieldParity=false`、`fullPhysicalParity=false`、`goalComplete=false` 保留。

实现入口为 [实例预更新](../../src/Als.Godot/Animation/Lyra/LyraLinkedPreUpdateHost.cs)、[Main 初始化和更新](../../src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs)、[Stop/Pivot 消费](../../src/Als.Godot/Animation/Lyra/LyraMainSourceScope.cs) 和 [实际角色初值](../../src/Als.Godot/Animation/Lyra/LyraCharacterAnimation.cs)。执行脚本、逐项报告和二进制哈希见上述审计及 matrix / ordinary verification 文件。

初次编译错误使用了不存在的 Montage 成员名，已修正并保留失败日志；首轮原生检查发现组件初始化配置差异，也保留失败报告。没有改精度门槛或删除 overlap/覆盖要求。

## 仍开放

这批只接九项组件缓存、指定初始化依赖及其 Stop/Pivot 消费。47 项完整私有字段、Idle/Turn/Pivot/Stride 的其他历史、不同类完整初始化字段对照、default/self/Unlink/部分覆盖整图、共享/持久实例、任意 Provider 拓扑和同函数多调用点的通用执行器仍开放。原生参考的制动标量和开关没有专门变化轨迹，动态配置切换不能据此称为完整原生验收。

绑定组件的异步销毁/替换、动画取消后物理继续推进时的新绑定初始化、未绑定 Pawn 的通用失败路径也需单独核验。完整 Chaos/Jolt 同输入物理此前仍为 314/1680 帧差异；复杂地形、近景握持、其他 Provider、GPU/人工、全量 managed、十分钟、性能、独立导出及音频/道具物理/头颈暂缓项保持。整体目标继续 active。
