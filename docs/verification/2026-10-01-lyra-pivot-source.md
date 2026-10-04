# Lyra Pivot 双 evaluator、共享回调历史与原生 Sync

2026-10-01，在主目录继续推进 Lyra 移植，源为 GASP58 / 安装版 UE 5.8.1，运行端 Godot 4.7.2 .NET。沿用 ALS 模型、68 蒙皮骨及 81 逻辑姿态。本批完成 PivotA/B 两个真实 source occurrence、共享字段和共同 Sync 的受控连续对照；完整 PivotSM 调度、Warp/外层 HipFire 姿态及普通 Demo 尚待。

后续 [原PivotSM验证](2026-10-01-lyra-pivot-machine.md) 已完成机器Update与实际子源调度；下文显式源访问范围是本批当时状态。完整Pivot姿态与Main/Demo仍待。原skipFirst标志的精确含义是首帧选边后丢弃过渡记录，不能解释为跳过规则选择。

## 原图与执行边界

`FullBody_PivotState` 原闭包共16节点，Root→UpperBodyMask LayeredBoneBlend，Base为PivotSM，Child为独立HipFire evaluator，原 BaseFirst=false。两个状态各有自己的 SequenceEvaluator→LocalToComponent→OrientationWarping→StrideWarping→ComponentToLocal 链。PivotA源61、PivotB源67，原角色均为 **AlwaysLeader**，共同 Locomotion Sync Group；不能沿用 Start 的 CanBeLeader 绑定。

原嵌套机器初始PivotA、每更新最多一条边、跳过首次更新转移、重入时重新初始化。两条反向边共用delegate72，0.4f / TLT_Inertialization / FastFeet。新 `LyraPivotLayerGraph` 通过已保护的编译闭包及 baked 状态链接查找两个源，校验这些配置，并通过该绑定工厂创建真实双源；没有用手工权重驱动结果冒充完整机器执行。

原转移条件是 LocalVelocity·LocalAcceleration<0，且没有垂直于初始Pivot方向移动，且 LocalAcceleration·PivotStartingAcceleration<0。`IsMovingPerpendicularToInitialPivot` 是Main纯函数，读取PivotInitialDirection与LocalVelocityDirection的前后/左右组，不能当成可写布尔成员。当前捕获显式提供这些Main观察，执行真实属性访问批次和编译delegate。Main的SetUpPivotState锁存及UpdatePivotState计时尚未进入此宿主。

## 两个 occurrence 共用字段

`LyraPivotSourceRuntime` 只持有该源的序列、ExplicitTime、内部时间、Marker/Delta、相关性和初始化历史。`LyraPivotSourcePair` 共用 PivotStartingAcceleration、TimeAtPivotStop、StrideWarpingPivotAlpha 和回写的 LastPivotTime，按实际遍历顺序传递候选；没有为每个 evaluator 复制一套共享字段。

- SetUpPivotAnim：复制Main局部加速度，按加速度方向及站蹲/ADS选择Pivot序列，ExplicitTime=0、alpha=0、TimeAtPivotStop=0，并写Main.LastPivotTime=0.2。
- UpdatePivotAnim先读取当前ExplicitTime。LastPivotTime>0时允许实际换源并请求0.2f惯性混合，更新StartingAcceleration，保留ExplicitTime与其余共享字段。
- 局部速度与加速度反向时，用实际CharacterMovement的Acceleration、LastUpdateVelocity和GroundFriction调用预测，以VSizeXY结果定位Distance曲线，并将**匹配前**ExplicitTime写入TimeAtPivotStop。预测距离为0仍执行DistanceMatchToTarget，定位动画内部零距离反转点。
- 其余情况下按ExplicitTime−TimeAtPivotStop−原Offset计算alpha，使用原DurationScaled与literal double 0.2→ClampMin插值，调用AdvanceTimeByDistanceMatching。不能把Pivot的literal 0.2替换成Start使用的配置Duration。

Core新增 `AlsPivotMovementSnapshot` / PivotLocation / PivotDistance，保留float速度、归一化、速度投影、摩擦、除数及时间边界，以及double向量运算。原预测仅在归一化和Size2D处投影；最终力与位置仍保留Z，调用方再取VSizeXY，不添加摩擦钳制。

匹配和推进共用36条实际静态距离codec缓冲，通过原Start/Stop两个资源适配器读取，不读取期望时钟或回调输出。两源提交先全部预校验，再共同发布；预校验可被外层角色事务单独调用。隐藏/初始化、取消、第二源Prepare异常、迟到或错误Sync结果均不能发布半帧状态。相同PlayerId的两源绑定直接拒绝。

## 原生连续捕获与结果

外部 `ReadPivotSourceTrace` 在独立GamePreview世界创建真实Character及登记的SkeletalMeshComponent，绑定原Main与原Linked provider。在同一个实例里用实际pose links执行两个原回调，用同一个根Sync Scope登记，再一次native Sync。预测通过实际AnimationLocomotionLibrary UFunction执行，转移条件通过原编译delegate执行；没有替换原规则或源函数。

输入明确提供Main观察、源活跃/权重/初始化及遍历顺序；不运行完整Main更新或PivotSM，不做骨骼求值，也不消费通知。三Provider×30/60/120Hz共3780帧，两份相互重叠的AlwaysLeader与反序遍历均有覆盖。

```text
LYRA_PIVOT_SOURCE_NATIVE_OK traces=9 frames=3780 packages=508 assets=36 assets_saved=0
LYRA_PIVOT_SOURCE_GODOT_OK traces=9 frames=3780 active=7236 hidden=324 dual=3564
assets=36 setups=432 matched=4572 advance=2664 inertia=144 changedRules=252
retained=720 rejected=29493 exact_bits=true shared_provider=true
```

预测位置/距离、源选择、ExplicitTime/内部时钟、播放率、Marker/Delta、共同字段、赢家leader、原规则与惯性duration按float/double位模式精确比较；未放宽阈值。每帧取消重试；错误结果包括第二源错误、缺失/重复、epoch、SampleStart、非有限时间及非法Marker，提交前完整预校验不发布。

两次正常UE捕获退出0，新fixture仅接受语义完全相同的重复结果，已有字节保持。每次保护508个UE包和605份旧JSON。最终UE日志0错误/无ensure/assert；961条Warning日志行含汇总重复，其中950为既有无效Footstep GameplayTag，其余为既有编辑器toolset/映射警告，未改源GameplayTag或资产。

| 回归或构建 | 结果 |
|---|---|
| Core预测、Asset Sync、距离匹配/evaluator相关 | 56通过，0失败，0跳过 |
| Start原生源回归 | 3780帧、36资源、精确位模式通过 |
| Stop原生源回归 | 3780帧、36资源、外插和保留时钟通过 |
| Main/Start/Cycle/Stop共同历史 | 3780帧、9105姿态、737505骨、取消重试与原误差门槛通过 |
| Debug / ExportRelease Optimize | 均0错误/0警告 |
| 导出脚本语法及diff | 通过 |

Main旧库存日志仍报告历史静态missingSequences=8：该旧fixture未被重写；上一资源扩展已在完整bank解决这八项，本批Pivot不消费这些条目。

最终汇总：`artifacts/lyra-analysis/pivot-source-final-verification.json`。新ignored资源为 `assets/generated/lyra_als/pivot_source_{requests,definitions,native}.json`，保持原字节依赖。外部插件源码、打包源码与工作目录C++/头文件哈希相同，无UE工程源码或配置更改，无资产保存。

## 失败记录与复跑

首次采集误把上述Main纯函数当作成员设置，commandlet返回-1，保留 `pivot-source-ue-observation-failed.log`；修改输入为真实两个方向成员后成功。没有UE崩溃。首次Godot错误是把Stop的零距离分支套到Pivot，原生首帧ExplicitTime=0.49166685而本地为0；按当前原函数修正，保留 `pivot-source-godot-first.log`。最终测试包含赢家leader、第二源Prepare异常与预校验门禁。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-pivot-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_pivot_source_smoke.tscn
python tools/verify_lyra_pivot_source.py
```

验证器需要保留本批命名的运行/构建日志与TRX；单独运行一条smoke不会生成全部验收证据。

下一步由原PivotSM决定真实状态、相关性、遍历和惯性，再接两套Warp与外层HipFire，接Main Pivot状态根/锁存/计时/共享scope。Idle/Air、完整LocomotionSM、统一Notify/Montage、最终足部、普通Demo与整链人工/性能验收仍开放；本批不代表完整Pivot或整个Lyra移植通过。
