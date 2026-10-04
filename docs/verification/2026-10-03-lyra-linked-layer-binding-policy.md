# Linked Animation Layer 普通绑定策略

后续原生复核已修正本页的部分覆盖假设：普通共享实例退休后，其他节点可读到空目标，不能始终保留旧类。多组、None、default/self/Unlink 的实例归属现已完成 10 布局/93 步原生对照；当前代码与范围以 [原生矩阵](2026-10-03-lyra-layer-binding-native-matrix.md) 为准。以下为前批八项测试与单组参考的历史记录。

2026-10-03，在主目录直接推进 ALS 人物与 Lyra Interface/Layer 路线。本批抽取普通绑定策略并接入现有 Lyra Main；整个移植目标保持进行中。没有启动 UE、修改或重导人物、动画、原资产或配置，也不展开 5.8/5.9 差异。

## 实现

新增 `src/Als.Core/Animation/AlsLinkedLayerBindings.cs`。组件使用不可变函数合同、调用点和类定义，不依赖 Godot；绑定候选归属于一个角色管理器，预校验后统一提交，取消不发布或消耗实例身份。它描述实例归属，不承担图执行、姿态采样、Sync 或 Notify 消费。

按本机 UE 5.8 的 `AnimInstance.cpp` 中 `PerformLinkedLayerOverlayOperation`、`LinkAnimClassLayers`、`UnlinkAnimClassLayers`，以及 `AnimNode_LinkedAnimLayer.cpp` 和 `AnimNode_LinkedAnimGraph.cpp` 的初始化路径，实现：

| 原规则 | 本批行为 |
| --- | --- |
| 两层 TMap：实现类、函数 Group | 分别保留首次插入顺序；函数和组名按 FName 大小写语义比较 |
| 命名组 | 同一类、本次操作同一组共用实例；不同类不共享 |
| 无组/None | 按调用节点分别创建实例，包括同一个函数的重复调用点 |
| 同类重绑 | 无组逐调用点复用；命名组按第一个目标实例判断，原操作跳过整个组 |
| 部分实现 | 该节点不参与覆盖；若原共享实例被其他节点退休，其目标可失效，详见后续原生矩阵 |
| Unlink | 匹配请求类才恢复调用点默认类，其余保持；分桶仍取请求类上的函数 Group |
| 命名组通知标志 | 类 CDO 标志与组内所有调用点标志 OR；无组及直接默认实例采用调用点标志 |
| self/default | 无接口或无默认类时回到 Main；指定 Main 类且调用点有默认外部类时，按原 Reinitialize 路径逐调用点重建默认实例 |

不能把任意现有同类实例按组键合并。同类命名组按首调用点判断复用；此前把部分覆盖视为始终保留旧共享实例的解释错误，后续原生矩阵已纠正。不同默认类初始化能形成有效的混合目标状态。

`LyraLinkedLayerContracts.CreateBindings` 将当前真实编译合同转换为通用策略输入。`LyraMainLocomotionHost` 的创建及换类发布使用该策略，并验证结果仍为当前实际可执行的十四入口、单个 `ItemAnimLayers` 实例。原 Main、Montage、共享 Sync、姿态算子、角色 epoch 和旧候选拒绝机制继续负责图生命周期。

## 验证

最终八项 Core 测试通过，包含多命名组、无组重复调用、角色隔离、部分覆盖、默认恢复、命名组首调用点复用、CDO/调用点通知标志、取消重试、外国/旧候选、self/default 细节及 FName 命名规则。Debug 和实际 ExportRelease 构建均零错误、零警告。

`LyraLinkedLayerBindingSmoke` 还用原有真实 UE `LinkAnimClassLayers` 八步参考，逐节点比较通用策略的类、通知标志和实例身份，并验证取消重试；原四实例与四次同类复用形成双向一致身份映射。该原生参考只有 Lyra 单组十四入口，不能证明其他组策略的 UE 运行等价。

最终 Debug 与实际 ExportRelease Optimize 均验证：各十一项场景（原绑定参考、三种初始装备×三 Hz、十角色）及三 Hz×三个原 Main 姿态边界，共四十个最终 Godot 进程全部退出零，无 ERROR/WARNING。九组普通角色每构建合计 5040 帧及 5040 次取消重试、54 次换类/54 次同类复用、51 次帧中重绑拒绝。十份单/多角色完整报告在两构建逐项相同。

三 Hz 的原动作中换层参考每构建合计 7560 帧，在惯性前、Rig 前及最终三个边界全部严格通过，并逐帧取消重试。各频率参考仍包括 24 次换类、24 次同类绑定、21 次动作中换类、6 次隐藏图换类；本批不改变原参考或误差门槛。这些参考重放原移动观察，不是新 UE/Jolt 完整同输入物理等价验收。

独立 `tools/verify_lyra_linked_layer_bindings.py` 核验日志、报告、程序集、四组 Optimize 运行后的六文件 Debug 恢复及 869 份既有 Lyra JSON 字节哈希，最终 `linked-layer-bindings-v2-integrity.json` 的 `auditPassed=true`。原新多组/无组/Unlink 运行验收与通用图执行标志仍为 false，`goalComplete=false`。未做本批 UE 运行、资产导出、GPU/近景、完整物理轨迹、全量 managed 或性能测试。

## 证据与复跑

```powershell
# 从 .. 执行 dotnet，避免仓库旧 SDK 固定版本。
dotnet test ./tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AlsLinkedLayerBindingsTests
dotnet build ./GodotALS.csproj -c Debug --no-restore
dotnet build ./GodotALS.csproj -c ExportRelease --no-restore

# 每次使用新的 EvidenceTag，保留已存在的日志与报告。
& ./scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Debug -EvidenceTag linked-layer-bindings-v2
& ./scripts/verify-lyra-linked-layer-bindings.ps1 -Configuration Optimize -EvidenceTag linked-layer-bindings-v2
```

当前上述标签已经使用，不应覆盖。最终三频率原动作中换层参考分别为 `rebind30-final`、`rebind60-first`、`rebind120-final`，沿用 `verify-lyra-whole-main-diagnostic.ps1` 的三个姿态边界及 `-Retry` 门禁；实际物理运动验收与这些重放原移动观察的动画对照区分。

首次测试命令把重定向通配符作为 dotnet 参数导致 MSBuild 参数错误，未运行测试；修正命令后六项通过。早期场景实际退出零，但验证脚本使用了错误成功标记，首轮验证报告明确失败；修正标记后保留日志并换新标签。进一步源码核查补齐默认重建分支和命名规则测试后，八项通过，使用新 V2 构建做最终场景验证。

独立审计首轮错误地把 60 Hz 的帧中拒绝次数 5 用于所有频率，触发断言而未生成通过报告。按原场景每 113 帧、余数 7 的调度分别核验 30/60/120 Hz 的 3/5/9 次后通过；没有修改场景输入、行为或门槛。最终审计日志为 `linked-layer-bindings-v2-integrity-final.log`。

## 尚待完成

这是普通绑定策略的抽取和 Lyra 生产适配，尚不是通用多 Layer 图执行器。现有资源加载器仍校验固定十四入口/单组；通用签名生成、Pose/属性参数包装、多组实际图宿主、默认/self/Unlink 的实际回调和混合请求、共享/持久实例子系统、完整继承拓扑及 Shotgun/Feminine 等完整图仍需实现。

多组、无组、部分覆盖、default/self 和 Unlink 新矩阵需要独立真实 UE 运行参考，不能仅以本批源码规则和 Core 测试关闭。保持原 ALS 68 skin/69 raw/81 logical 与全部原资源；完整物理轨迹已有 314/1680 帧失败，地形、近景、性能、多平台/独立导出及原暂缓项继续保持开放。
