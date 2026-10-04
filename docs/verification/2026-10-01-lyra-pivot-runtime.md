# 原 Pivot 双 Warp 与外层 HipFire 完整姿态

2026-10-01，在主目录继续推进 Lyra 移植，UE 5.8.1 / GASP58 原资产，Godot 4.7.2 .NET。沿用 ALS 模型、68 skin /81 logical。本批将上一批真实 PivotSM Update 与源调度接入完整原16节点姿态链，关闭该 Provider 组件的 Update/Sync/Evaluate 对照；Main Pivot 状态根、完整主状态机、普通 Demo 和整链验收仍开放。

## 原图拓扑与实例历史

原 Root73 → LayeredBlend58：Base 为 PivotSM59，Child 为 HipFire57，`bUpdateBasePoseFirst=false`。Update 先访问独立 HipFire，再进入机器。PivotA 经过源61 → LocalToComponent63 → Orientation62 → Stride65 → ComponentToLocal64；PivotB 对应67/69/68/71/70。两条状态结果到机器，最后再混入外层 HipFire；不能把 Start 的“先 HipFire 后 Warp”顺序套入 Pivot。

`LyraPivotLayerGraph` 保存两个 Warp 节点的实际编译身份和外层初始权重。Orientation/Stride 配置加载器支持按节点身份读取同一图中的两份原设置。新 `LyraPivotLayerSourceHost` 共用已有真实机器与 source pair，先登记独立 HipFire，再登记机器选定的 Locomotion 源，由角色提供一次公共 Sync。两组 occurrence 身份、源时钟和 Marker 独立；共享 PivotStartingAcceleration/TimeAtStop/StrideAlpha 与 LastPivotTime 保持原回调顺序。

`LyraPivotLayerPoseHost` 为两个状态分别保存 Orientation 历史、Stride modifier 和 pelvis spring。实际选定源按 Sync 后内部时钟采样，生成压缩根 RootMotionDelta，再执行其本套 Warp。机器姿态随后经过原 UpperBodyMask 的 mesh-space 旋转混合、Override 曲线、按骨权重的整数属性和 typed RootMotion 混合。

本机原绑定逐帧确认：Orientation 读取 Main 的 `LocalVelocityDirectionAngleWithOffset`；Stride 读取 `DisplacementSpeed` 及本次所选源回调后的 `StrideWarpingPivotAlpha`。测试同时提供明显不同的无偏移方向，防止错误引脚恰好通过。三个绑定均保留原 double→float 边界。

机器入状态或重初始化时重置对应 Warp；隐藏帧显式进入 A、同帧 A/B 均初始化及另一状态未访问时保留历史分别验证。提交前预校验所有源结果；Prepare/求值后取消、失败求值、迟到或重复提交不发布历史。失败求值清除可见候选输出。原0.4f/FastFeet/HermiteCubic惯性请求继续输出给外层，未在 Pivot 内新增姿态 crossfade。本组件活跃提交要求本次求值成功；它没有关闭完整角色的 update-only 求值策略。

## 实际 UE 捕获

新增外部 `ReadPivotRuntimePoseTrace` 复用原机器捕获，在独立临时 GamePreview 中创建真实 Character/Movement、登记组件、原 Main 与原 Linked Provider。Manny carrier 副本切到 transient ALS81 Skeleton；42条普通序列绑定 transient ALS81 数据，两套原 Warp 的脊柱按既有策略把 spine04/05 映到 spine03并去重，UpperBodyMask 按骨名适配。

状态结果探针转发原 Initialize/CacheBones/Update/Evaluate，只记录状态 Warp 后姿态，退出前恢复原链接。实际根 Update、公共 Sync 和根 Evaluate 均由原节点执行；原转移规则、回调、源采样、Warp 和混合算子没有替换。Main 输入是显式观察，本批没有执行整个 Main 图或消费完整通知队列。

三 Provider ×30/60/120Hz各6秒，输入含首帧反向重入、隐藏/自动重入、站蹲/ADS/方向换源、零与负位移、不同速度/角度/组件旋转和 HipFire 门槛附近/负值/大于1权重。原资源和614份旧JSON逐字节保护。五份新JSON只接受重复结果语义相等并保留已有字节；两次完整导出均正常退出0、结果一致。

```text
LYRA_PIVOT_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3528
logical=81 sequences=42 packages=508 assets_saved=0
LYRA_PIVOT_RUNTIME_GODOT_OK traces=9 frames=3780 poses=3528 bones=571536
assets=36 hidden=252 hipFire=2157 tiny=582 transitions=810 firstTransitions=108
automatic=81 setups=927 matched=2277 advance=1251 hiddenResets=9 bothReset=108
rejected=58191 rootNonzero=1339 rootIdentity=2189 rootProbes=378
exact_pins=true independent_warps=true retry=true production=false
```

每帧分别对照机器 Warp 后与外层 HipFire 后81骨，共7056姿态/571536骨，位置最大差 `1.1616137819346588e-13 cm`，最终 quaternion 最大差 `8.89722066373406e-16`，scale差0。原门槛 position `1e-8 cm`、quaternion `1e-10`、scale `1e-12` 未变。曲线值/flags、整数属性存在性/身份/值、源/Marker/Delta、机器/惯性请求和引脚位模式通过；7056份 typed RootMotion 同身份/TRS门槛通过。42条源的378个静态压缩根 probe 精确相等。

两个阶段各有14112份整数属性，72份已有原生属性混合探针仍通过。每帧 Prepare取消重试、重复Evaluate、求值后故障/取消重试及全历史不发布检查通过。58191次拒绝含旧候选、活跃未求值提交、两源epoch/时间/asset/range/Marker/Delta错误、缺失/重复结果、隐藏求值及重复提交。

## 回归与边界

| 门禁 | 结果 |
|---|---|
| Core预测/Sync/距离匹配/evaluator/Orientation/Stride/根提取 | 87通过，0失败，0跳过 |
| 原PivotSM普通/首帧重入 | 共7560帧，两套精确位模式通过 |
| 原双Pivot source | 3780帧/7236源/36资产通过 |
| 完整Start与Stop Provider | 各3780帧/3672姿态通过 |
| Main/Start/Cycle/Stop共同历史 | 3780帧/9105姿态/737505骨通过 |
| Debug、ExportRelease Optimize | 均0错误/0警告 |
| 外部UE插件构建/源码一致/保护哈希 | 通过 |

首次原生导出因常规 Layer 图未包含状态机编译子链接而被16节点闭包门禁拒绝，日志 `pivot-runtime-ue-closure-failed.log` 保留。改用已有 `IncludeStateRoots=true` 导出后成功；没有删去状态或放宽闭包条件。首次 Godot 完整对照通过。

最终两次UE日志0错误/无ensure/assert，仍有1934条Warning日志行（包含汇总）：1395条无效Footstep GameplayTag，528条transient复制对象ConditionalPostLoad依赖，以及11条已有编辑器/toolset/映射提示。这不是零警告采集；未修改原资源、项目配置或UE引擎代码。旧Main库存仍显示历史8项missing，旧JSON保持；完整扩展bank已在此前资源验证中解决这8项。

没有普通Demo运行、渲染/人工观感、全量、多角色/并行、打包、十分钟或性能验收。本批不包含真实Main Pivot状态根/相关性回调/方向锁存/计时、与其余主分支共同owner、整个LocomotionSM选边/状态权重/最终混合、统一Notify/Montage或最终足部。普通Lyra示例仍为此前简化入口，完整Lyra目标和ALS R2–R7保持开放。

## 资源与复跑

新ignored资源：`assets/generated/lyra_als/pivot_layer_graph.json`、`pivot_runtime_{requests,distance,roots,native}.json`。只检出代码无法运行；完整验证汇总为 `artifacts/lyra-analysis/pivot-runtime-final-verification.json`。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-pivot-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_pivot_runtime_smoke.tscn
python tools/verify_lyra_pivot_runtime.py
```

验证器还需要本批命名的构建/回归日志和TRX。下一步接真实Main Pivot状态根及其Main共享反馈/公共scope，再继续Idle/Air、完整主机器和最终层/通知/足部/生产验收。
