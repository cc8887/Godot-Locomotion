# 原 Cycle 根遍历与两个源的统一提交

本批从真实编译 `FullBody_CycleState` 根节点采集 Initialize/CacheBones/Update，并实现 Godot 对应源收集宿主。Cycle 与 HipFire 的原回调、登记顺序、权重、隐藏/重初始化、共同 Sync 和统一提交已对照。两组共18条/7,560帧逐位一致。**这是 Cycle 源执行闭包；尚未实现/验证其完整姿态、Warps 变换、角色 Notify 消费或独立 Demo 的生产源接入。**

## 编译闭包和索引

Base、Unarmed/Pistol/Rifle/Shotgun 及 Feminine 共9个 provider 的实际 CDO 闭包各8节点，均只读导出。编译属性数组下标、编辑/node下标分别记录，不能混用。

| node index | 类型 | 实际子链接 |
|---:|---|---|
| 52 | Root | 46 |
| 46 | Component→Local | 45 |
| 45 | Stride Warping | 48 |
| 48 | Orientation Warping | 47 |
| 47 | Local→Component | 50 |
| 50 | LayeredBoneBlend | Base51，BlendPose49 |
| 51 | Cycle SequencePlayer | 原 UpdateCycleAnim |
| 49 | HipFire SequenceEvaluator | 原 UpdateHipFireRaiseWeaponPose |

原 `OutputPoseNodeIndex=65` 与 FPoseLink.LinkID 使用属性数组下标；Root 的 node index 是 `118-1-65=52`。新合同经实际 OutputPoseNodeProperty 查找并转换，所有链接同时保留 propertyIndex/nodeIndex。旧 immutable `source_nodes.json` 保留原字段，未覆盖或格式化。

原 LayeredBoneBlend 使用 UpperBodyMask、mesh-space rotation、Root mask权重0、无限LOD，且 `bUpdateBasePoseFirst=false`。因此 Update 先遍历 HipFire，再遍历 Cycle；Sync 则先处理 Locomotion 组，后处理独立 HipFire。输入与输出 sample 范围会重排，宿主按源身份接受结果，并按 occurrence 的输入范围重定位校验；同时校验共同输出的完整连续范围。

## 原生范围

与上一批一样，在临时 GamePreview 世界注册实际 Manny Mesh + Main，并通过真实 LinkAnimClassLayers 取得 ItemAnimLayers 实例。仅临时实例绑定既有 ALS 重定向 Cycle/HipFire 序列，保持各 provider 已建立的资产副本选择；Unarmed HipFire 蹲姿使用原 UnarmedRemaining 副本，Pistol 使用自身副本，不因源路径相同而任意替换。

这次通过实际 Cycle 根 FPoseLink 运行全部8节点的初始化、骨缓存和 Update，原 HipFire 回调按 crouching 选择姿态，原 Cycle 回调仍选择四向/站蹲/ADS 动画。一个独立空根 Proxy 持有共同 Sync Scope，每物理帧一次正常 UpdateAnimation tick；两个真实源分别进入分组和独立部分。原 Warping 节点参与了原生 Update 遍历，**未调用其 Evaluate，不构成 Warping 姿态验收**；Godot 宿主当前也只执行这个闭包中的源收集。

## Godot 状态与事务

`LyraCycleLayerGraph` 验证原8节点拓扑、双索引和 blend/source策略；`LyraCycleLayerSourceHost` 持有同一 linked owner 中两个 occurrence。Cycle 复用上一批源 runtime；HipFire 通过原 evaluator adapter 从显式时间0准备真实独立 tick。调用方把两条记录提交给角色共同 Sync，此宿主不自行另建同步时钟。

- 相关性判断使用局部 float BlendWeight 严格大于 `1e-5f`；先转换原 double 属性，再判断。乘上外层权重后即使最终源权重小于门槛，仍按原 LayeredBoneBlend 登记。
- 整层隐藏不 tick，但保留资产、时钟、Marker 物理存储、上一 tick Delta 和 cached blend weight。原 AssetPlayer.Initialize 只重置 Marker 索引和 full-weight 历史，不清 cached weight；宿主不擅自清零。
- 隐藏期间初始化会把 Cycle 时钟重置到原0起点、保留所属 Layer 的 stride alpha，以及 Marker 距离和上一 Delta；evaluator 的 Initialize 不直接清时钟。候选随共同帧接受后才发布。
- 先验证 Cycle、HipFire 及所有结果范围，再提交两个源。故意注入后一个 HipFire 的异代错误时，前一个 Cycle 不发布状态。取消、重试、陈旧候选均按共同帧处理。
- Cycle 保留惯性请求输出；本批没有消费角色惯性/Notify队列。Warp Root Motion 属性、pose/curve/attribute混合需要后续完整源姿态链。

## 验证结果

Unarmed/Pistol/Rifle ×30/60/120Hz 每组9条、3,780帧，包含低/高/负 blend输入、float门槛两侧、零delta、四向/ADS/蹲姿、层隐藏和重初始化。第二组在隐藏窗额外执行真实根初始化。

| 项目 | 普通隐藏 | 隐藏中重初始化 |
|---|---:|---:|
| 物理帧 | 3,780 | 3,780 |
| Cycle tick | 3,528 | 3,528 |
| HipFire tick | 2,205 | 2,205 |
| 整层隐藏帧 | 252 | 252 |
| 隐藏中初始化 | 0 | 18 |
| 仍登记的最终微小权重源 | 225 | 225 |
| 实际惯性请求 | 108 | 108 |
| 坏/旧候选拒绝 | 5,985 | 5,985 |

每帧从 Godot 自身已提交状态计算；原生输出只用于严格对照。比较 before/prepared/time、资产、rate、double stride、惯性请求、blend/cached source weight、两个源的 DeltaPrevious/Delta、Marker 索引/距离，以及隐藏帧持久状态。所有输入候选每帧取消重试，记录一致。新增两组共158,976个binary32/22,680个binary64编码字段核验；没有放宽阈值。

两组在各自第二个完整 UE 进程重复导出，guard确认内容相同并保留旧字节。489包/11依赖/234 logical clips核验未变。Debug/Release Optimize 0警告0错误；最终两个Godot日志无ERROR/WARNING。上一批单源3,780帧回归通过。既有60Hz Demo回归870帧/六次换层/871次81 logical→68 skin发布、左右手各780次应用通过，**这不证明新源宿主已接Demo**。没有Core算法修改、新ALS全量、新渲染或人工/性能验收。

最终日志：`cycle-layer-godot-history.log`、`cycle-layer-hidden-godot-first.log`、`cycle-layer-source-regression.log`、`cycle-layer-godot-build-history.log`、`cycle-layer-optimize.log`、`cycle-layer-demo-60.log`；原生重复为`cycle-layer-export-repeat.log`、`cycle-layer-hidden-export-repeat.log`。资源审计为`cycle-layer-verification.json`。首native构建在运行前发现root/property索引混用，已按本机源码及实际RootProperty修正；未将首构建记作运行验收。

## 不可变资源与复跑

仍位于 ignored `assets/generated/lyra_als/`，仅代码检出不足以复跑。

| 文件 | SHA-256 |
|---|---|
| cycle_layer_graph.json | `3c8f054d612a7da91f7938e18aff802d5846c2f4f58cf562be2b2af3dba0572d` |
| cycle_layer_requests.json | `6f883e5a3d806e815928143e5b557d43992c6c40d79fa9a2e45922c626231837` |
| cycle_layer_native_bits.json | `0cec5340f0b9d88c52f9061d2b5e8eb60d2c8b520302b0d78b93543246cfd922` |
| cycle_layer_hidden_reset_requests.json | `3508180c22beae7b54a42e5a908ce9ae4012665738ebafd1b6ba32c7465e21eb` |
| cycle_layer_hidden_reset_native_bits.json | `c7607af47baf0edcf4caa7203df9797b076a09bc8a30adffb201a26928300ac8` |

```powershell
.\scripts\export-lyra-cycle-layer.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-cycle-layer.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -HiddenReset
python tools/verify_lyra_cycle_layer.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_cycle_layer_sources_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_cycle_layer_sources_smoke.tscn -- --lyra-cycle-hidden-reset-smoke
```

后续仍须完整Curve/Attribute/Pose容器、局部HipFire混合→Orientation/Stride原姿态链与Root Motion属性、其余源回调/Main三个Lean、不同Marker集合的共同组与跨图/owner遍历、统一通知及生产接入，最后完成连续Main原生与人工验收。原Demo的全局HipFire位置与单phase clock尚未替换，不能把组件通过写成完整Lyra移植完成。
