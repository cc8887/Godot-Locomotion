# Lyra Idle Break 与 Jump Recovery 的 ALS81 资源接入

2026-10-01，在主目录继续实现，UE 5.8.1 / GASP58，Godot 4.7.2 .NET。保持原 ALS 模型、68 蒙皮骨和 81 逻辑骨。本批补齐上一盘点发现的八个逻辑资源绑定；关闭资源、原生取样和扩展加载组件，Idle/Pivot/Air 宿主、完整状态机与生产入口仍开放。

## 资源与绑定

五条原 Idle Break 通过原 Manny→ALS IK Retargeter 生成到 `/Game/GodotLyraRetarget/IdleBreaks`，仅保存新的派生资产。三条 Jump Recovery 使用此前已有的 ALS 目标，原文件哈希保持；为它们建立 transient 69 raw /81 logical 动画，补武器和虚拟控制通道，不重新烘焙原 68 骨 skin 轨迹。

新增 ignored `assets/generated/lyra_als/locomotion_extras` 独立目录，保留旧 234 项目录和 Main Lean 三项扩展的文件字节。`LyraLogicalSourceBank.Load(includeMainLean:true, includeLocomotionExtras:true)` 得到 245 个不可变源、51 个 additive，曲线与整数属性使用同一 bank。既有入口默认资源范围保持其验证配置；后续新宿主显式加载完整资源。

`LyraLocomotionLayerInventory.BindSources` 将三个 Provider CDO 的全部 197 个原 Sequence 绑定解析为实际 bank slot，八个缺口归零。原源有时对应多个既有 ALS 目标，例如跨 Provider 复用的 HipFire；绑定保留原目标数组顺序和每个 slot，不能用“源路径唯一”假设删掉合法映射。宿主仍须按原节点绑定选择对应目标。

五条 Idle Break 没有 Notify/Marker；三条 Jump Recovery 合计十个原 Notify。源/目标的 Marker、rateScale 和完整 typed Notify 事件逐项相等，未改变事件时间或载荷。导出保留 playback 元数据，并未把这些通知接到角色统一消费者队列。原 AirIdentity→LandRecovery 编辑规则仍为 false，资源补齐不能作为启用该边的理由。

## 非零帧 additive 基底

| Provider | 原策略 | 原索引 | 实际基底时间 |
|---|---|---:|---:|
| Unarmed | ABPT_LocalAnimFrame，本序列 | 28 | 0.9011494291239772 秒 |
| Pistol | ABPT_AnimFrame，自引用 | 22 | 0.699999988079071 秒 |
| Rifle | ABPT_AnimFrame，自引用 | 28 | 0.9011494291239772 秒 |

本机 `AnimSequence.cpp::GetSequencePose` 使用 `Sequence.GetPlayLength() × clamp(frame / sampledKeys, 0, 1)`，不是 `frame / fps`。Sequence 时长来自 float 字段，原数据模型时长为 double；它们在这些资源中相差几个纳秒。首轮 Godot 在 Unarmed 骨1发现 quaternion 3.33e-10，超过原1e-10门槛。已按 Sequence 时长修正实际基底选择，旧失败目录完整移动归档到 `artifacts/lyra-analysis/idle-recovery-wrong-base-time`，未覆盖已有正确资源或放宽门槛。

采样器使用同一基底时间完成 local additive 骨骼差、曲线差和整数属性差。Unarmed 没有 RefPoseSeq，使用本动画；external exporter 原先只接受非空或自引用 RefPoseSeq，本批补其 LocalAnimFrame 原分支，不改变原 Aim/Main Lean 的策略。

新增负时间覆盖还发现 FAttributeCurve 的 before-first-key 分支虽有“默认值”注释，实际 DataPtr 始终初始化为第一键并在该分支保留。整数属性现在按执行代码返回首键值；例如 Idle Scan 的 TCFrame 在负时间为19。修正后新增3800属性值与原生一致，原73,650行曲线/属性回归仍全部通过。

旧 Jump catalog 的 `rootLockFirstFrame` 是旧提取 helper 得出的派生值，当前关闭 root-motion provider 的提取路径得到 additive 根单位值。三条原 `.uasset` 字节未变；本批保留新旧派生值供诊断，对所有序列真实 RAW/动画求值逐骨检查。它们实际强制 RefPose root lock，未使用这份旧值作为基底或最终姿态。

## 压缩与从零生成

首次普通批量重定向仍触发此前 Rifle 导出记录过的压缩 Worker /BonePose NaN 断言；失败在保存五个目标之前。随后一次生成正常完成并保存五个新目标，不能据此宣称原引擎问题已修复。

诊断证明命令行 `ExecCmds` 暂停参数在该 commandlet 没有生效，实际读回值为0。导出脚本改为在 UE Python 中调用真实 Console API，将 Editor.AsyncAssetCompilation 设置为2并验证读回；这暂停后台对中间数据的处理，完成目标数据后显式 finish_source_compression。

另用五个全新未保存的验证目标执行完整原 IK RetargetBatch，从源重新生成981个采样键，与 canonical 目标的全部 RAW 数据精确相同；正常结束，0 errors /10 warnings，无 ensure/assert。验证目标没有保存 `.uasset`，508个受保护包哈希保持。本项证明此导出流程的从零生成与压缩通过，未修改 UE 引擎，也不宣称普通默认编辑器下所有批量压缩问题已经修复。

## 验证

| 检查 | 结果 |
|---|---|
| 完整资源加载 | 245源、三个Provider、197个Sequence默认绑定，missing0，仍为81逻辑/68skin |
| 新资源原生 | 475 RAW/求值样本，包括负时间、首尾、越界、非整帧和原基底时刻；重复及独立 occurrence 一致 |
| 旧资源扩展库回归 | 原234项936样本与Main Lean24样本，共960样本通过 |
| 最终采样最大误差 | position1.4163191318420816e-13cm、quaternion6.58317845524286e-16、scale0；门槛仍1e-8cm/1e-10/1e-12 |
| 新标量/属性 | 475行、曲线不存在性严格同，3800项整数属性逐值同；三种非零帧基底通过 |
| 原标量/属性回归 | 73,650行、139,227曲线值、589,200属性值、936联合采样通过，六类坏资源拒绝、采样分配0 |
| 共同Main宿主回归 | 三Provider×30/60/120Hz共3780帧/9105姿态/737505骨，Start锁存、时钟、Root Motion与取消重试通过 |
| 静态/字节保护 | 508 UE包（原500、此前Jump目标3、新Idle目标5）、旧234源及32份旧fixture字节保持 |
| 构建 | external exporter成功；最终Debug和ExportRelease Optimize均0警告0错误 |

两份独立正确基底的 UE 复导均正常退出0、summary各0 errors /34 warnings，数据语义相等且immutable save保留第一份字节。最后一份明确读回 compilationMode2。Godot 最终新增及回归均退出0、无ERROR/WARNING；UE原警告未修。

没有本批普通Demo、渲染、人工全矩阵、多角色/并行、打包或性能验收。完整 Idle/Pivot/Air 执行、LocomotionSM 选边/权重/混合、外层惯性、统一Notify/Montage、最终足部与Godot实际角色观察仍待；ALS R2–R7和用户暂缓项保持开放。

## 证据与复跑

最终汇总 `artifacts/lyra-analysis/idle-recovery-final-verification.json`。首次压缩断言、metadata旧helper差异、重复源绑定误判、错误基底时间和负时间属性失败日志均保留。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-idle-recovery.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\verify-lyra-idle-generation.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_idle_recovery.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_idle_recovery_resources_smoke.tscn
```

下一步使用完整 source bank 实施 Pivot 双 evaluator、原 Setup/Update/通知历史和嵌套机器姿态；再补 Idle/Air 宿主及完整主图执行。
