# Lyra 原 Cycle 回调与源节点组件

本批实现单个真实编译 Cycle 源 occurrence 的持久状态、候选/取消/提交，以及原 `UpdateCycleAnim` 的换源、速度匹配和步幅权重。三个 Layer × 三频率共 3,780 帧与实际 UE 回调/SequencePlayer/Sync 输出逐位一致。**仅完成组件，未接独立 Demo 的生产源宿主，也未完成整个 Cycle、Linked Layer 或 Main 图。**

## 原生采集范围

只读加载 GASP58 的 `ABP_Mannequin_Base` 和实际 Unarmed/Pistol/Rifle provider。在临时 GamePreview 世界注册真实 SkeletalMeshComponent，通过 `LinkAnimClassLayers` 取得 `ItemAnimLayers` 的真实共同实例，再仅替换该临时实例的三组 Cardinal 资产为既有 ALS 重定向序列；不修改 CDO、不保存资产。组件使用原 Manny Mesh 建立实际 Main/Linked 关系，本批不对这个临时组件求值姿态。

每个 provider 的真实 Cycle source 为编译 node51 / propertyIndex66，`UpdateCycleAnim`，Locomotion SyncGroup、AlwaysFollower、循环 SequencePlayer。真实 `FPoseLink.Update` 按引擎顺序执行编译节点回调，再执行该 SequencePlayer 的 Update。带原 UE requester 的上下文收集实际惯性请求。

原生 Sync 由独立空根 Proxy 持有一个共同 Scope，经 `UpdateAnimation` 执行一次。这是受控单源遍历；没有运行整个 Main 根图或其它 Cycle 源，没有原角色 Notify 消费、最终骨骼/曲线/Root Motion 输出对照。该边界不同于原生完整 Linked 图验收。

## 实现的原规则

- 先判断蹲姿；蹲姿选 Crouch_Walk_Cardinals，其余按 ADS 选 Walk 或 Jog，再按 `LocalVelocityDirectionNoOffset` 选择方向。显式转换 UE Forward/Backward/Left/Right 与现有 Godot 枚举的不同顺序。
- `SetSequenceWithInertialBlending` 换资产时保留时钟，并请求 0.2f 秒惯性；首次空资产到第一条动画也请求。后续 SequencePlayer Update 对新片长裁剪旧时钟。本批确实覆盖 7 次这种裁剪。
- `SetPlayrateToMatchSpeed` 使用原资产 GetPlayLength 和 ExtractRootMotionFromRange(0, floatLength) 的平面距离；长度、距离、动画速度与除法为 float，FVector2D clamp 为 double，结果写回 float。RateScale 由后续 Sync 消费。
- `StrideWarpingCycleAlpha` 原属性为 double，按原 Kismet FInterpTo、float Context delta 提升到 double、目标 wall=0.5/otherwise=1、速度10插值；不提前转 float。重初始化源节点不重置所属 Layer 的此属性。
- 节点重初始化和换源保留 Marker 距离存储、重置索引；随后提交到共同 Sync。惯性 requester 的请求与惯性 Sync scope 标志分开，发出请求本身不擅自设置 tick 标志。

`LyraCycleSourceRuntime` 持有单 occurrence 的资产、时间、播放率、步幅权重和 Marker。Prepare 产生候选，调用方将 Player 送入共同 Sync，再提交匹配 player/asset/epoch/sample 的结果；Cancel 不发布状态，取消后的旧候选、重复提交、异代、非法时钟及非有限结果均拒绝。此类不创建角色之外的第二套播放时钟，也不自动求值 enclosing graph。

当前组件严格验证原 Cycle 节点的 playRateBasis=1/startPosition=0/非 matching-pose/identity ScaleBiasClamp。不是任意 SequencePlayer 设置的通用执行器；其余源回调和 BlendSpace 更新仍需实现。

## 验证

| 内容 | 结果 |
|---|---:|
| Unarmed/Pistol/Rifle × 30/60/120 Hz | 9 条轨迹，3,780 帧 |
| 三组 Cardinal、四方向、三个 provider | 36 个不同资产全部进入轨迹 |
| 实际惯性请求 | 108 次，均为原 .2f |
| 换源后的旧时间裁剪 | 7 次 |
| 零 delta | 348 帧 |
| oracle binary32 / binary64 编码核验 | 37,908 / 11,340 字段 |
| 每帧取消/重试 | 3,780 次候选完全一致 |
| 旧/重复候选拒绝，加异代/非法时间/NaN | 7,587 次，拒绝不发布状态 |

Godot 每帧从自身已提交状态计算，逐位比较 before/prepared/time、rate、double stride、惯性数量/时长、DeltaPrevious/Delta 和 Marker 索引/距离。原生动态输出不回填后续状态。静态资源定义另存片长、根位移平面距离、原 clamp，逐帧核对定义不漂移。没有改变误差阈值。

最终 Debug 和 Release Optimize 均 0 警告/0 错误，Cycle smoke 日志无 ERROR/WARNING。既有独立 Lyra Demo 的 60Hz 回归通过：870 物理帧、6 次换层、871 次逻辑姿态发布，仍为81 logical→原68 skin，左右手各780次有效应用；**这只是既有入口回归，新 Cycle runtime 尚未接 Demo。**本批没有改 Core Sync，没有重跑 ALS 全量或新人工渲染/性能验收。

第二个完整 UE 进程重导结果相同，既有 fixture 文件保留原字节；489 个 UE 包、11 个资源依赖与234个 logical clip 核验未变。无 UE 引擎源码/项目配置修改、无资产保存、无提交或推送。

最终证据：`artifacts/lyra-analysis/cycle-source-godot-final.log`、`cycle-source-godot-build-final.log`、`cycle-source-optimize.log`、`cycle-source-demo-60.log`、`cycle-source-export-repeat.log`、`cycle-source-verification.json`。首次 native 构建的 private Sync/JSON string 类型错误、未导出 FData 构造函数的链接错误，以及 Godot FileAccess 歧义/测试 Marker mask 保留位错误均已修复，原日志保留为 `cycle-source-reader-build-first/second.log`、`cycle-source-godot-build-first.log`、`cycle-source-godot-first.log`。最终通过未改 oracle 输出。

## 不可变本地资源与复跑

资源仍位于 ignored `assets/generated/lyra_als/`；仅检出代码不足以运行本批测试。

| 文件 | SHA-256 |
|---|---|
| cycle_source_requests.json | `3bb8e355401705f099173859060dbe2428654e9d3d74ae7fbebe71ba67d75932` |
| cycle_source_native_bits.json | `a9bc79c372b3072de3e17cfea7037e7ef58a066545be58742b283aefa95401f1` |
| cycle_source_definitions.json | `30b68c15ca8b11549031f070cc87a6af2a5b5e78922abe49546d0c74dba50d0d` |

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-cycle-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_cycle_source.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_cycle_source_smoke.tscn
```

下一步为实际 Cycle 内其余源及全部 Layer occurrence 的原回调/共同遍历、跨源/跨 owner 同步与通知，然后连接现有角色姿态容器和生产入口。原 Main 三个 Lean、其它状态距离匹配/Warps、原 HipFire 子图位置、完整曲线/属性容器、足部控制、Montage/Root Motion、原生完整连续主图和人工验收均仍开放。
