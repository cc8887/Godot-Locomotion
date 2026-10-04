# Lyra 完整 Stop provider 与 Main Stop 状态入口

2026-10-01，在 `.` 主目录实施。源为 GASP58 / 本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET。继续使用 ALS 68 skin / 81 logical；本批完成 Stop 四节点姿态闭包和 Main StateResult18→Linked19 的组件对照，尚未接入共同三根或生产玩法。

## 资源与 Animation Interface / Layer 的实现边界

沿用 `logical_controls/calibration.json` 的 Manny→ALS 标定、原 IK Retargeter 输出和实际武器控制通道。42 个 transient ALS81 源包括 36 个 Stop 和六个站蹲 HipFire。姿态采样走原 RAW 通道；距离匹配读取各 transient 源完成压缩后的实际 Distance buffer，RootMotion 使用实际 legacy 压缩根通道。资源文件在 ignored `assets/generated/lyra_als`，只有代码检出不足以运行。

ALS 缺少的 `weapon_r`、武器空间虚拟骨等控制信息在逻辑骨架中保留，最终蒙皮仍为原 68 根。已验证的上身 Mask、整数属性与曲线策略只有在九个实际 provider 的 LayeredBoneBlend 配置逐项一致后才复用。Stop 不包含 Start/Cycle 的 Orientation/Stride Warp，不能把它们直接套入 Stop。

既有编译接口导出包含 `ALI_ItemAnimLayers` 的 14 个入口、输入 pose/参数、共同 `ItemAnimLayers` group、默认 Notify 标志和 BlendIn/Out。Godot 的契约与 Router 保持这些身份、输入缓冲和共享实例生命周期。本批新增 `LyraStopLayerSourceHost` 与 `LyraStopLayerPoseHost`，参与外部共同 Sync、保留独立 evaluator/HipFire 时钟，输出 81 骨 pose、曲线、整数属性和 typed RootMotion；不自行写 Skeleton3D。

`LyraMainStopHost` 是本批完整 Main Stop **组件宿主**：一次完整 Main 候选→原 Stop 状态更新语义→Stop provider→Sync 后 Evaluate→统一预校验与提交。接入既有 `LyraMainSourceScope` 时，应直接使用 Stop provider 和状态回调上下文，由已有 Main owner 更新一次；不能再把此独立宿主连进去重复更新 Main。最终角色继续单次发布蒙皮骨。

## 原生 StateResult 回调与历史

外部 helper 校验实际 Main StateResult18、Linked19、Stop state index3 和目标 provider。真实遍历 StateResult18 的 OnUpdate `UpdateStopState`，再访问 Linked19 与实际四节点 Stop 根。该回调调用 UE `IsStateBlendingOut`，读取实际 state-machine identity、当前 state 和代理上一帧记录的 Stop weight；“上一权重”与当前 root weight 分开。

完整 Main 更新先清除 RootYaw 模式。Stop 活跃且没有混出时，原回调设模式2，供下一帧消费；混出时保留模式0。Godot 将这个 post-graph 值与源历史一起提交，而不是提前修改已发布 Main。实际 linked `.15` 秒初始惯性请求在子图更新后发出，每个实例只消费一次；本批核对请求与生命周期，没有验收外层惯性化姿态。

原生采集使用实际注册 Character/Movement/Main/Linked、PropertyAccess 批次、完整 `BlueprintThreadSafeUpdateAnimation` 和共同 Sync。机器当前 state2/3、上一 Stop weight、root 相关性/权重和初始化仍是**显式机器观察边界**，不是实际 LocomotionSM 的规则选择或完整机器运行。没有手算替代原生回调输出。

## 短资源遗留时钟的 RootMotion

首轮 Godot 在 `unarmed/30Hz/frame98` 拒绝 RootMotion 的 previous time：旧时钟 `1.333254098892212` 秒超过新资源长 `1.2666666507720947` 秒，而真实 delta 为 `-0.6332539916038513` 秒。UE 接受该非循环 evaluator 反向区间，压缩根取样在 codec 的相对帧位置处钳制，保留原 previous 参与推进。

新增明确的 `retainedEvaluatorClock` 提取参数，只允许非循环、反向或零 delta 的遗留区间；普通 player 的区间门禁保留。实际压缩取样允许结束点之外的正时间，并保持原 codec 帧位置钳制。静态 378 个 Root probe 和全部连续 RootMotion TRS 严格相同；没有放宽误差、覆盖旧 oracle 或把源码公式写入原生期望。

本批这个普通正 delta 更新产生合法反向源区间，UE 退出0。上一批额外“资源切换恰逢物理零 delta”触发的 UE Notify ensure 仍保留为未关闭诊断，本批没有修复或绕过它。

## 验证

| 检查 | 结果与范围 |
|---|---|
| 两类组件 | 各 Unarmed/Pistol/Rifle ×30/60/120Hz，3780 物理帧、3672 姿态、297432 骨、108 hidden |
| Stop provider | 36 个 Stop 资源全部实际选中，216 Setup、1584 保留选源帧；937 explicit/internal 不同帧，1 个遗留 previous 超新长度 |
| Main Stop | 实际 Main 选中21个 Stop 资源；2463 累积模式、1209 混出、1260 上一权重为零的活跃帧，9 条初始 Linked 请求 |
| HipFire | 两组件各2268 tick、204 cached weight 微小帧；门槛、隐藏、切站蹲与重初始化时钟逐位一致 |
| 最终姿态 | provider 最大位置 `9.271153338872217e-14 cm` / quaternion `7.776515945702112e-16`；Main Stop `9.099386826154683e-14 cm` / `9.746367512157514e-16`；scale 0 |
| RootMotion / 数据 | 两组件各3672 present RootMotion，TRS差0；各14688整数属性，曲线分别4080/4176值；typed identity、存在性保留 |
| Main / 源 | Main 标量/spring/flags、回调模式、Stop/HipFire clocks/marker/delta、预测距离逐位同；Main 向量最大 `1.3642420526593924e-12 cm` |
| 事务 | 两组件各13500坏操作拒绝；每帧取消重试、求值前拒绝提交、迟到候选/坏epoch、求值后取消、重复求值覆盖，无历史提前发布 |
| Core | 70项相关测试通过，0失败/0跳过；含新增遗留根区间门禁、Sync、evaluator与停止预测 |
| 回归 | 完整 Main update2520帧、Stop单源3780帧、既有共同 Start/Cycle3780帧/6300姿态/510300骨通过 |
| 构建与保护 | UE外部 exporter、Debug、ExportRelease Optimize均成功；.NET 0警告/0错误。492包、依赖JSON与旧12份fixture字节保持 |

原姿态门槛保持 `1e-8 cm / 1e-10 quaternion / 1e-12 scale`，静态 root probe 逐值相同。两类 exporter 各两次独立进程退出0，复导资源语义相同并保留原字节。UE summary 分别 **0 errors / 1456 warnings** 与 **0 errors / 1011 warnings**；原压缩依赖/GameplayTag等 warning 保留，无 Python Error/ensure/assert。首次根提取失败及静态 validator 对 `.33` authored double 未先转原 float 的校验问题已修正；保留首轮日志。

证据为 `artifacts/lyra-analysis/stop-runtime-*` 和 `main-stop-runtime-*`，汇总 `stop-runtime-final-verification.json`。没有保存 UE Skeleton/Sequence/AnimBP 或修改引擎源码；没有本批普通 Demo、渲染、并行、多角色、打包或性能验收。

## 复跑与剩余项

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-stop-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-main-stop-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_stop_runtime.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_stop_runtime_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_stop_runtime_smoke.tscn
```

只关闭完整 Stop provider 与 Main Stop 状态入口组件。下一步 Stop 纳入同一 Main/source owner，与 Start/Cycle 共同登记一次 Sync，并按真实状态根执行回调和合成；继续 Pivot/Air/Idle、完整 LocomotionSM、Linked热换类、统一 Notify/Montage、外层惯性、FootPlacement/LegIK、Godot live gather、普通玩法与整链验收。ALS R2–R7、用户未提交修改和暂缓项保留。
