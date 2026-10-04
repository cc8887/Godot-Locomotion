# Lyra Linked 实例首次根访问更新

2026-10-03，继续在主目录推进 ALS 人物上的 Lyra 移植。按本机 UE 5.8 原源码与既有多实例原生快照，实现每实例首次根访问的 worker 候选；不展开 5.8/5.9 差异。本批不关闭全部私有字段或完整移植目标。

## 实例状态与执行顺序

新增 `LyraLinkedWorkerHost`，由每个实际 `LyraItemLayerGraphInstance` 持有。Main 创建每次 Prepare 独立的帧凭据；实际访问 Linked 根时准备一次候选，同实例的后续函数借用该候选。没有实际根访问的实例保留历史，初始化与游戏线程预更新不能替代实际访问。所有实例沿角色原事务共同预校验、提交或取消。

原五个通用字段是 HipFireUpperBodyOverrideWeight、AimOffsetBlendWeight、TimeFalling、HandIK_Right_Alpha 与 HandIK_Left_Alpha。Aiming、Additives 与 SkeletalControls 借用所在实例的值；移动根读取自己的实例 HipFire 权重。独立组件原调用路径仍保留，组件回归分别验证。

AimYaw/AimPitch 保留为对应 Aiming 根访问时传播的 double 参数，不能用同组第一个函数的参数代替。LandRecoveryAlpha 仍由原 Additives 图回调更新，与通用 worker 状态分别持有。本批八项断言涵盖这五个通用字段、两个参数和一个图字段，并非将八项全归为 worker 属性。

完整 Main 的实际遍历先访问 SkeletalControls，再到 Aiming/LeftHand、移动根及 Additives。Main Idle 设置 RootYaw、Main Pivot 设置枢轴历史等根回调先于对应 Linked 根的 worker 更新；同组实例若已被其他函数访问，保留其第一次读取的值。所有源仍进入角色共同 Sync，未增加实例独立时钟、同步批次或骨架发布器。

候选失败时撤销 worker、参数、图状态和曲线反馈，取消重试使用新的候选帧凭据。实例退休沿原统一 Cancel 路径处理；同类 Link 保留真实对象与私有历史，不同类替换不要求旧私有历史继续存在。

## 验证边界

Debug/实际 ExportRelease 的 28 项原生动画对照已通过：三 Hz、四布局的 pre-rig 连续轨迹，以及 30 Hz 单组/每调用点的最终 ALS Rig 输出。累计 64800 个提交参考帧及同数取消重试，八项字段合计 14601600 次比较。完整姿态/曲线/属性/root 使用原门槛。两种构建均 0 错误、0 警告。

两构建的普通三 Provider 三 Hz 换类、十角色，以及原 Aiming/Additives/SkeletalControls 组件与每调用点十角色均已通过。最终共 58 个 Godot 进程终态退出 0，无 Godot ERROR/WARNING。两构建二十份普通完整报告与前批逐项同，Debug/Optimize 完整报告相同，每调用点十角色完整报告也相同；真实模型每角色发布 480 帧，68 骨局部发布位置/旋转差为 0，报告仍明确 `productionAccepted=false` 和 `nativeWholeMainParity=false`。

独立 `tools/verify_lyra_linked_worker_runtime.py` 审计通过，证据为 `artifacts/lyra-analysis/linked-worker-v1-integrity.json`。六轮实际 ExportRelease 的六个 DLL/PDB 替换与 Debug 恢复逐文件 SHA256 相同；869 份旧 JSON、710 个原 UE 包及 9 项项目配置保持原字节，原采集包、全部引用的三频参考及 NativeMath 未变。保存了编译时源快照和当前源哈希；收尾仅纠正诊断注释，独立检查限制两份诊断源的差异仅该注释，全部执行语句不变。

首个诊断构建两处枚举类型名称错误和首轮正负零位比较失败均已修正，失败日志保留；没有改变生产数学或原姿态门槛。两个早期单组/每调用点的成功定位进程也保留，不计入最终 58。最终 verifier Python 及 PowerShell 语法检查通过，没有本批 UE 启动/重导、GPU/人工观感、全量 managed、十分钟或性能验收。

`--whole-main-worker-fields` 对原实例 Before/Updated/After 快照逐项断言八字段。每帧先执行候选并取消，再重试和提交，包含取消后 Before 的再次比较。原快照只用于断言，不驱动状态、权重、动画时间或姿态。非零字段按 double 位比较；数值零允许正负零相等，没有增加误差容限。原完整骨骼/曲线/属性/root 比较门槛保持。

原生参考为已存在的三频多实例完整图采集，包含 Unarmed/Pistol/Rifle 及 1/3/4/14 实例。这里只核验该输入范围，未新增开火/ADS/隐藏后恢复的专门 UE 采集。诊断场景使用受控物理观察和已有碰撞服务，不等同实际 Chaos/Jolt 运动等价；普通场景另外使用真实 Godot 物理与 ALS 模型。

## 保留的开放项

八字段对照不等于完整私有字段验收。全部 PropertyAccess 缓存、IdleBreak/Turn/Pivot/Stride 等图历史的统一逐值审计，以及实际开火、ADS、隐藏恢复和不同类替换的更完整连续参考仍需推进。default/self/Unlink/部分覆盖和不同 Provider 拓扑的完整图、共享/持久实例策略、同一函数多调用点的通用执行器也保持开放。

人物继续复用 ALS 68 skin/69 raw/81 logical；Lyra 动画已重定向至目标骨架，Rig 显式使用 ALS 参考配置。完整物理轨迹此前 314/1680 帧差异、复杂地形、近景握持、其他 Provider、GPU/人工观感、全量 managed、十分钟/性能/独立导出及音频/道具物理/头颈暂缓项不因本批关闭。

## 复跑

成功和失败证据不得覆盖，使用新标签；Godot 验证顺序执行。

```powershell
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration Debug -RunTag multi-layer-v3-30-full -EvidenceTag <新标签>-30 -WorkerFields -Boundaries pre-rig
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration Optimize -RunTag multi-layer-v3-30-full -EvidenceTag <新标签>-30 -WorkerFields -Boundaries pre-rig
# 60 Hz 使用 multi-layer-v3-60-repeat-full，120 Hz 使用 multi-layer-v3-120-full。
# 最终 Rig 边界使用 -Layouts single,per-call -Boundaries final。
```

`-Layouts` 与 `-Boundaries` 限定明确范围，缺省仍为原四布局三边界。普通回归沿原 `verify-lyra-linked-layer-bindings.ps1`。本批完整运行源与备份在 ignored artifacts，独立封口工具针对本批标签；新批次需先准备对应新标签的运行与审计配置，不覆盖旧文件。
