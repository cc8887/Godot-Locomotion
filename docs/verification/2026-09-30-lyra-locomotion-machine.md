# Lyra 原主图状态规则与选边组件

本批在主目录继续推进 Lyra 移植，导出端为本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET。新增当前编译 AnimBP 的状态目录、typed Locomotion 规则和按原顺序选边组件；实际 UE 编译规则和 `FindValidTransition` 对照通过。

**本批关闭规则/选边组件门禁，尚未替换独立 Demo 的源宿主或状态切换代码。** 下一步需要共同接入真实源权重、Sync/Notify 和标准混合/惯性请求；完整原图、连续主图姿态及人工/性能验收仍开放。上一批 81 骨 Layer 与双手运行入口保留，见 [运行记录](2026-09-30-lyra-logical-layers.md)。

## 当前编译数据

`ReadRuntimeGraph` 从当前 `IAnimClassInterface` 和 CDO 读取 baked states/transitions 与 StateMachine/Inertialization 节点设置，未根据编辑图的展示顺序重新排序。

新增不可变资源位于被忽略的 `assets/generated/lyra_als/`：

- `runtime_graph.json`：Main 与九个 provider class，共 10 类、37 台机器、111 个 baked state、198 条边；保留 profile 路径、delegate/source 节点索引和原始节点设置。
- `locomotion_rules_native.json`：576 条原生规则输入与 1,536 次实际 compiled handler 结果；绑定 graph 字节 SHA-256 和 Main `.uasset` SHA-256。
- `locomotion_selection_native.json`：480 条实际 `FindValidTransition` 结果；绑定 graph 和上述规则文件的字节 SHA-256。

重复导出仅接受相同语义，已有文件字节保留。Main/九 provider 包在导出前后核对；最终独立复核原 485 个包、11 个依赖 JSON、234 个 raw clip 哈希未变，保存资产数为零。

Main `LocomotionSM` 的顺序为：Idle、Start、Cycle、Stop、Pivot、JumpSelector、JumpStart、JumpApex、FallLand、EndInAir、JumpStartLoop、FallLoop。共 12 状态/36 边，JumpSelector 与 EndInAir 为 conduit。

Main 节点实际配置：一次更新最多一条内容状态转换；`bSkipFirstUpdateTransition=true`；隐藏后重入重新初始化；初始状态不能为 conduit。这里的 skip 是首帧切到目标后丢弃混合记录，不是禁止首帧选边。组件按该语义处理。

所有 Main 边采用 HermiteCubic，四条为 `TLT_Inertialization`：Start→Cycle 的换层边、Stop→Idle 的换层边、Stop→Idle 的 stance/ADS 边、Pivot→Cycle 的换层边，原 duration 均为 binary32 的 0.2 秒。其余边为标准混合。

Main 最终 Inertialization 位于 FullBody Additives/Slot 之后、RootYaw 之前，默认 profile 为空，无过滤骨/曲线，不因重新相关而重置，允许请求经过被跳过的缓存节点转发。该节点与状态机标准过渡是不同边界；本批没有把所有过渡替换成惯性化。

九 provider 各有 FullBodyAdditve_SM、IdleSM、IdleStance、PivotSM。PivotSM 的两条边引用原 Manny `FastFeet` BlendProfile，目录保留此引用；尚未导出并适配 profile 骨权重/逐骨时间，不能静默按统一 duration 求值。

## 组件语义

`LyraRuntimeGraphCatalog` 编译固定布局和原 exit 顺序，校验 class/inventory 哈希、状态/edge 身份、生命周期设置及原 Main predicate 绑定。Main 暂不支持 custom curve/profile；Linked 子图的 FastFeet 作为明确资源引用保留。

`LyraLocomotionMachine` 接收显式 typed 观察，包含原加速度/速度/近战组合、反向 dot/wall、换层、stance/ADS、起跳/下落/地面、方向和计时、Pivot notify、真实 relevant source 和 Sync 有效性。选择器按 baked exits 顺序递归穿越 conduit，使用终端边的 duration/type；路径按原 UE 顺序记录。

自动剩余时间边使用真实相关源的 binary32 length/time，保留 `CrossfadeTimeAdjustment`，标准混合的有效 duration 扣除该调整。不能将任意当前播放源当成最相关源，也不能仅因资源有 marker 就声明当前 Sync Group 有效。

状态 elapsed 独立于源播放器时间，保持原 float 更新边界。组件支持 reset、候选拷贝和取消后重试。它尚不产生原生状态姿态权重栈，不执行 Player/Linked graph/Notify，也不独立发布骨骼。

当前已证实的旧示例差异包括：

- Start 的 Pivot 与换层边优先于其他退出；旧示例曾先处理 Stop/空中条件。
- Pivot 的通知退出在 baked 顺序上先于无加速度 Stop。
- Start 的 RootYaw >60° 退出要求真实 `Locomotion` Sync Group 有效。
- stance/ADS/LinkedLayerChanged 是独立原规则，部分边采用惯性化。
- 经过 EndInAir 到 Cycle 的终端 duration 实际为 0.2 秒，旧示例使用零混合。

这些差异需要在源遍历、缓存和混合一起接入时替换；本批未宣称旧 Demo 已具有该行为。

## 实际 UE 对照

原生 helper 创建临时 GamePreview world、Actor/mesh 和真实 Main 动画实例。规则输入写入该实例；直接调用真实 TransitionResult 的 `GetEvaluateGraphExposedInputs().Execute`。规则探针没有模拟或重写 Blueprint 条件。

576 个输入涵盖 12 个状态、布尔组合、四方向、反向/零 dot、60°、0.15 秒、10 cm/s、0.4 秒、200 cm 及附近值。32 条非 automatic 边共 1,536 次 compiled handler 判断，与 Godot 全部相同。恒真边与没有活跃 notify 的 Pivot 通知边单独计数；其余判断均覆盖 true/false。

另对十个真实状态的 480 个输入调用原 `FAnimNode_StateMachine::FindValidTransition`。实际 selected edge、target、CrossfadeTimeAdjustment 和 source transition 路径全部一致，183 个选择经过 conduit。这也覆盖了原 conduit entry handler 和同优先级的实际顺序。

**此原生选择探针没有活跃 source、Sync 或 notify 状态。** 四条 automatic 边及 Sync 有效/Notify 活跃的正向门禁由受控检查覆盖，尚非完整生产条件下的 UE 连续原生对照。首次更新/候选恢复也由组件检查，未做该范围的新增 UE 连续生命周期轨迹。

## 验证与命令

```text
LYRA_LOCOMOTION_MACHINE_OK nativeCases=576 nativeRules=1536
nativeSelections=480 nativeConduits=183 states=12 edges=36 boundaries=25
hz=30,60,120 frames=1890 realStates=10 rejected=6 retry=True
scope=rulesAndSelection
```

受控 9 秒轨迹在 30/60/120Hz 分别覆盖十个真实状态，合计 1,890 次更新；它使用显式受控源时间，不是实际 Godot 角色移动或原图姿态运行。25 项边界检查包含竞争优先级、近战、ADS、换层惯性边、marker 门禁、自动剩余时间调整、conduit 路径、首帧和候选取消重试。六个内存中变异合同均拒绝；没有改写原资源。

新增测试入口为 `scenes/tests/lyra_locomotion_machine_smoke.tscn`。复现资源时依次构建外部 exporter、执行 `export-lyra-runtime-graph.ps1`、`export-lyra-locomotion-rules.ps1`、`export-lyra-locomotion-selection.ps1`，三个脚本均需 `-EngineRoot` 和 `-UnrealProject`。随后构建 Godot 工程并运行该 smoke。

最终 Debug 与 Release Optimize 均零错误/零警告，Godot 最终 smoke 无 ERROR/WARNING。三个 UE 导出正常退出 0、日志未出现 Error；源项目的 GameplayTag/启动/Editor unavailable 警告仍存在，日志 occurrence 计数分别为 1189/20/20，包含重复汇总行，未修这些源项目问题。

汇总：`artifacts/lyra-analysis/locomotion-machine-final-verification.json`。关键日志为 `runtime-graph-export-first.log`、`locomotion-rules-export-first.log`、`locomotion-selection-export-first.log` 和 `locomotion-machine-smoke-final.log`。

## 修正与后续

外部 reader 首次编译使用 FString 接收当前 JsonObject 的 SharedString key，先后产生类型转换/不存在 ToString 的编译错误；按实际本机头文件改为 `ToView()` 后构建通过。首次直接构造 FAnimationPotentialTransition 因安装版未导出其构造函数而链接失败，改用 `FStructOnScope` 经原生反射 struct ops 构造；仍执行原引擎的 FindValidTransition，没有修改引擎代码。

Godot 首次目录门禁误把所有 provider profile 当成空，发现真实 FastFeet 后改为保留 Linked profile 引用，同时保留 Main 无 profile 门禁；首次失败日志保留，未删除 profile 或提高原生比较阈值。

下一步将 Main 状态选择和真实 Linked owner 源遍历/相关权重连接，复用原 HermiteCubic transition stack并按实际四条边发送惯性化请求；Idle/Pivot 子图请求还需向原最终节点转发。随后补完整曲线/attribute、原共享 Sync/多源 Notify、Warping/脚部与连续原生轨迹。没有新增 GPU 渲染、完整人工矩阵、十分钟或性能验收；Lyra 支线及 ALS R2–R7 保持开放。
