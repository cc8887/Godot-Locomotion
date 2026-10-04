# Lyra 编译源库存与 Layer 归属

2026-09-30，主目录实施，UE 5.8.1 / Godot 4.7.2 .NET。本轮取得编译节点配置及完整源归属，未将新源宿主接入独立 Demo。上一轮 Main 姿态/惯性组件见 [验证记录](2026-09-30-lyra-locomotion-pose.md)。

## ALS 人物与骨架

继续使用 ALS Mannequin 网格及原 68 根蒙皮骨。现有重定向资源使用 69 根 raw 动画骨、81 根逻辑骨：新增非蒙皮 `weapon_r` 通道和 `VB IK_Hand_L_weaponSpace`，保留原 ALS 虚拟骨，最终写回原 68 skin。资源和源采样证据见 [逻辑控制骨](2026-09-30-lyra-logical-controls.md)，实际共同层与双手接入见 [运行验证](2026-09-30-lyra-logical-layers.md)。本轮重新检查相应资源哈希，没有重定向或修改网格/骨架。

这支持继续复用 ALS 人物。Manny 的额外脊柱骨和比例差异仍需要完整 Warping、脚部与武器握持的连续验收；现有双手和源采样对照不能替代这些范围。

## 原生配置发现

新增 `ReadSourceNodes` 从十个实际编译类读取 CDO 和 folded data，保留节点类型、索引、原回调名、组/角色/方法、循环/相关性、Evaluator reinitialization/teleport、Player play-rate clamp、BlendSpace 样本和原函数根属性。仅调用读取接口，不 tick 世界/动画，不保存资产。

得到 255 个源节点、239 个有回调的节点：Main 三个 BlendSpace；Base 与八个具体武器/性别 provider 各 28 源。总体为 144 SequenceEvaluator、90 SequencePlayer、21 BlendSpacePlayer。

| 源 | 类型 | Sync 组/角色 | 配置 |
|---|---|---|---|
| Start | SequenceEvaluator | Locomotion / CanBeLeader | 非循环；teleport=true；ExplicitTime 重置 |
| Stop | SequenceEvaluator | Stop / CanBeLeader | 非循环；teleport=true；ExplicitTime 重置 |
| Cycle | SequencePlayer | Locomotion / AlwaysFollower | 循环；UpdateCycleAnim |
| PivotA/B | 两个 SequenceEvaluator | Locomotion / AlwaysLeader | 非循环；teleport=false；独立来源与时钟 |
| FallLand | SequenceEvaluator | Locomotion / AlwaysLeader | 非循环；teleport=false |
| Idle Turn | SequenceEvaluator | Test / CanBeLeader | 非循环；teleport=true |
| Main Start/Cycle/Pivot Lean | 三个 BlendSpacePlayer | DoNotSync | ignoreRelevancy=true；各持有三个样本 |

UE 的 SequenceEvaluator 在 SyncGroup 中强制按推进语义处理；不能仅凭 teleport=true 跳过同步、Notify 或 Root Motion 提取。现有 ALS `AlsAssetSyncPlayer` 接口明确限定非 evaluator，且目前没有 AlwaysLeader；后续必须按实际引擎行为扩展并取得原生 tick 对照。

`GetGraphAssetPlayerInformation` 只登记 provider 的 11 个直接含源 Layer，共 20 源，遗漏 IdleSM/IdleStance、PivotSM 和 FullBodyAdditve_SM 中的八个独立源。合并 baked state 的 `playerNodeIndices` 后，每个 provider 的 14 个入口完整拥有 28 源。Idle 两层机器共享同一播放器身份，不能重复创建；Pivot 两源则必须独立。Main 三个 Lean 源从 LocomotionSM 的 Start/Cycle/Pivot 状态表取得。

索引也有两个空间：baked state 与 Layer player 表使用反序 ID；`FAnimNode_Base::GetNodeIndex()` 与 property index 相同。原 `FAnimBlueprintFunction.OutputPoseNodeIndex` 属于 property 空间。新资源同时保存这些原始值，C# 验证实际反序关系，避免将相同整数当成同一个节点。

库存不是更新遍历次序，不能将合并清单逐个 tick 来代替原图。活跃/Inactive、回调和 Sync 登记顺序仍须由实际节点访问产生。

## 动态资源与缺口

九个 provider 的全部 252 个源默认 asset 为空，动画由图暴露输入/原回调及 CDO 资源字段动态绑定。静态默认资源闭包为零，不能宣称 Layer 无需资源或全部资源已经齐备。当前 234 条逻辑序列覆盖既有 Unarmed/Pistol/Rifle 普通/Aim 资源；Shotgun/Feminine 的完整移动/Aim 资源仍待。

Main 三个源共同使用 `BS_MM_Rifle_Jog_Leans`，位置样本为 -20、0、20。这三条序列尚未进入当前逻辑源库：

- `MM_Rifle_Jog_Leans_Left`
- `MM_Rifle_Jog_Lean_Center`
- `MM_Rifle_Jog_Lean_Right`

它们位于 Main 状态内的合成分支，归属与 Linked Layer 不同。后续需单独补资源、原 additive 语义与原生 BlendSpace 对照，再接状态姿态合成。

## Godot 接入路线与当前实现

`LyraSourceNodeCatalog` 编译不可变源配置、原直接 Layer player 表、回调和完整 Layer 库存。它验证三个 JSON 字节依赖、源类哈希、两种索引、Sync enum、时间数值与嵌套源归属。它没有动画时钟，不提前激活隐藏或不可达分支，目前仅由新验证场景读取。

Animation Interface 继续由 typed hook/契约表达，原 14 个签名、输入姿态和 Aiming double 参数已导出；共同 `ItemAnimLayers` 实例及同类复用/换类重建已按真实 UE 对照接入。具体进度见 [Interface/Linked Layer 契约](2026-09-30-lyra-linked-layer-contracts.md)。各移动入口当前还是资源 resolver 与旧源宿主，不能把 14 个路由等同完整 14 图求值。

后续实际宿主顺序：

1. 角色主图更新与原状态回调，按 Main pre-cleanup 更新计划访问实际 Linked Layer；每个共同 Item 实例拥有 28 源及嵌套机器历史，Main 独立拥有三个 Lean 源。
2. 各实际 source 节点执行原初始化/相关性/update 回调，登记同一角色 Sync 范围；完整登记后统一选主/tick，再读取实际源时间与事件候选。
3. 以姿态、带存在性的曲线及 typed 属性作为完整帧缓冲，按原状态内部 HipFire/Main Lean 分支、Main 权重与后续 Layer/Slot/控制节点顺序求值。HipFire 不能只按最终 Phase 在 Main 混合后统一叠加。
4. 源和 Montage 通知进入同一角色候选队列；准备、可选求值、提交/取消统一覆盖 Main 与 Item 实例。提交成功后才发布骨骼及副作用。

先补 Lean 与源曲线，再扩展 evaluator/AlwaysLeader Sync，并取得真实 source tick 原生对照；随后连接已验证 Main pose 组件和完整子图。现有 Demo 的单 phase 时钟与 linear occurrence crossfade 尚未被本轮替换。

## 本轮验证

- 外部 UE 插件 BuildPlugin 最终退出 0，未修改引擎源码或 UE 项目配置。
- UE 只读采集退出 0，有唯一 `LYRA_SOURCE_NODES_OK classes=10 sources=255 packages=15 assets_saved=0`；无 Error/ensure/assert。
- 日志仍有 1189 次 Warning 行，其中 1178 次为 GameplayTag（含总结重复），其余为原项目 Editor/toolset/DSL 警告。未把大量资产加载警告写成零警告，也未修复原项目标签。
- `source_nodes.json` 为 230629 字节，SHA-256 `de6ddf4929675c90b174bc0a64fe1d13a131f0315f528f2e3e79a31a17d322c0`。已有 JSON 没有格式化或覆盖。
- Python 复核本库存 15 包及旧 calibration/类资源并集共 489 包、11 个依赖、234 条 clip 哈希全部一致。`source-nodes-verification.json` 明确区分静态默认闭包与动态节点。
- 新 Godot scene 退出 0，唯一 `LYRA_SOURCE_NODE_OK classes=10 sources=255 nestedOwners=9 missingLean=3 rejected=6 scope=configurationAndOwnership`，无 ERROR/WARNING。六类错误为旧依赖、非法 Sync role、错误 native/property ID、未知 Layer source、丢失嵌套 Pivot source、重复 source ID。
- Debug 与 Release Optimize 构建均 0 警告/0 错误；Python 编译与 `git diff --check` 通过。

最初 C++ 编译误把回调访问器当成 public、随后误判 GetData 可见性、再遇到模板成员指针推导错误，三次失败完整保留。最终使用显式类型的基类成员指针读取 protected folded-data 路径，没有引擎修改、非法派生对象转换或复制时钟公式。

日志位于 `artifacts/lyra-analysis/source-nodes-*`，失败构建为 `reader-build.log`、`reader-build-final.log`、`reader-build-access.log`，最终为 `reader-build-typed.log`。本批未做生产角色运行/渲染、新 source tick/native Sync 对照、完整主图连续 oracle、全量测试或性能验收；旧 Main/手部/运行验证不能合并成该范围通过。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-source-nodes.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_source_nodes.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_source_node_smoke.tscn
```
