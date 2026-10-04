# Lyra Start 方向历史与完整 Layer 资源闭包

2026-10-01，继续在主目录实现，UE 5.8.1 / GASP58 原资产只读，Godot 4.7.2 .NET。保持 ALS 原人物模型、68 根蒙皮骨和当前 81 根逻辑骨。此批补 Start 的 BecomeRelevant 方向锁存，并查清三个 Provider 的十个 Locomotion Layer 完整编译闭包；Idle/Pivot/Air 的真实执行、完整 LocomotionSM 和生产入口仍待实现。

## Start 历史修正

上一批对照覆盖 StateResult 的 UpdateStartState / UpdateStopState 和 Hold/Accumulate 顺序，尚未覆盖 Start 的 BecomeRelevant。当前只读 folded-data 导出确认 Start10 绑定原 SetUpStartState；它把当帧 Main 的 LocalVelocityDirection 锁存到 StartDirection，后续更新保留此值，供原状态转换条件使用。它与 Start Provider 的 SequenceEvaluator 选源历史是两个独立字段。

原生采集通过真实 FPoseLink 更新执行该函数，并从 Main 的 NodeRelevancy subsystem 读取 HasJustBecomeRelevant，不手动生成 oracle。Godot 将 StartDirection、上一 Start 遍历相关性及权重纳入同一个候选，和 Main/source/Sync 一起预校验、提交或取消；每次逐根更新前后均与 UE 对照。

首次 Godot 在 joint/0/126 失败：实现误将单独 pose-link Initialize 当作 Main 的相关性历史清零。UE 实际保留实例级 NodeRelevancy 历史；仅初始化链接不会重新执行 SetUpStartState。本批按真实连续遍历和 ZERO_ANIMWEIGHT_THRESH 修正触发条件，没有改原生轨迹或阈值。全隐藏的 scope 仍提交空遍历历史；随后恢复相关性才重新锁存。完整状态机的遍历 counter、选边和权重生成仍须在后续宿主接入时验收。

## 闭包与资源缺口

新增 ReadAnimationLayerGraph 的显式 IncludeStateRoots 选项，旧调用默认输出保持。除普通 PoseLink、UseCachedPose 外，按实际 baked machine 的 StateRootNodeIndex 展开嵌套状态根；编译索引与 property 索引使用原 class 的反向映射，不能直接把 layer 元数据中的 property 索引当成 node 索引。每个节点保留 InitialUpdate / BecomeRelevant / Update 函数身份。

只读 CDO 导出保留 Blueprint 自有字段类型、全精度值、原数组顺序和动画资源路径。Unarmed/Pistol/Rifle 各十个 Layer 合计 30 个闭包、210 个节点、72 个源出现位置、24 条编译状态链接；原对象包的保护范围从 492 增至 500。Godot 新 typed inventory 校验闭包、回调身份、源所属 Layer 与所有依赖哈希，它不拥有时钟，也不代替宿主执行。

Pivot 实际有 PivotA/PivotB 两个 evaluator，均执行 SetUpPivotAnim / UpdatePivotAnim，另有 HipFire evaluator；不能把整个 Pivot 当成单播放器。Idle 包含嵌套 IdleSM 和 IdleStance。Idle_Breaks 的原顺序是 Unarmed [Scan, Fidget]、Pistol [Scan]、Rifle [Fidget, Scan]。

当前 81 骨逻辑资源表缺以下八个源的目标绑定：

| Provider | 缺少的绑定 |
|---|---|
| Unarmed | IdleBreak_Scan、IdleBreak_Fidget、Jump_RecoveryAdditive |
| Pistol | IdleBreak_Scan、Jump_RecoveryAdditive |
| Rifle | IdleBreak_Fidget、IdleBreak_Scan、Jump_RecoveryAdditive |

三条 Jump Recovery 已有此前导出的 ALS 资源，本批缺的是当前逻辑资源库绑定；五条 Idle Break 需新增重定向与导出。下一步以独立扩展目录并入同一 81 骨 bank，保留旧 234 项目录及依赖文件字节。Pivot 的原方向资源已在当前逻辑目录，不能把它列为缺资源。原 AirIdentity→LandRecovery 编辑规则为 false，资源齐全也不能擅自启用该边。

## 验证

| 检查 | 结果 |
|---|---|
| 联合运行 | 三 Provider ×30/60/120Hz，3780 帧、9105 次逐根更新/姿态、737505 骨 |
| Start 历史 | 27 次 BecomeRelevant、9 次方向变化、2235 帧锁存值不同于当帧速度方向；逐根前后值及每帧取消重试通过 |
| 模式/时钟 | 1488 Hold 反馈、1197 Accumulate 反馈；7560 Lean 时钟及14709样本时钟逐位同 |
| 姿态 | Start 最大位置 2.299679001209009e-13cm、Cycle 2.109680835400689e-13cm、Stop 9.099386826154683e-14cm；最大 quaternion 1.7886740406278828e-15，scale 差0 |
| Root Motion | 9000 present /105 absent，TRS 差0；曲线和整数属性严格对照 |
| 生命周期 | 24537 坏操作、9个误合并 Stop group、45个坏状态观察拒绝；逐帧准备/求值后取消再重试 |
| 资源 | 114 实际压缩根/1026 probe 精确同；500 个原 UE 包哈希保持，28 份旧 fixture 文件字节保持 |
| 回归/构建 | 最终旧状态根3780帧/9105姿态通过；Core相关70通过0失败0跳过；Debug和ExportRelease Optimize均0警告0错误 |

保持原姿态门槛 1e-8cm /1e-10 quaternion /1e-12 scale。两个独立 UE 进程采集均退出0，immutable save 比较语义并保留第一次文件字节；各自 summary 为0 errors /2202 warnings，无 Python Error /ensure /assert。原压缩、GameplayTag 等警告未修。Godot 最终联合和回归退出0，无 ERROR/WARNING。

首次 C++ 构建因只读属性指针类型不匹配失败，修正局部指针类型后成功；首次 Godot 相关性触发误判日志保留。上批短资源切换且物理 delta 为零的 Notify ensure 诊断仍未关闭。此批没有修改 UE 引擎或保存原资源，没有重跑普通 Demo、渲染、完整视觉矩阵、多角色/并行、打包或性能；ALS R2–R7 和用户暂缓项保持开放。

## 证据与复跑

最终汇总：`artifacts/lyra-analysis/state-history-final-verification.json`，包含资源、日志与旧 fixture 的哈希。新资源在 ignored `assets/generated/lyra_als/main_state_history_*.json` 和 `locomotion_layer_closures.json`。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-state-history.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_state_history.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_state_history_smoke.tscn
```

接下来补八个逻辑资源绑定，实施 Pivot 双 evaluator 与 Idle/Air 原更新/姿态宿主，再接完整状态机、Notify/Montage、外层惯性、最终足部和 Godot 实际角色观察。此批只关闭 Start 锁存组件与静态资源盘点。
