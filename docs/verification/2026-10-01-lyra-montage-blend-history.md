# Lyra Montage 冻结混合历史与 FastFeet 目标骨策略

2026-10-01，当前主目录直接推进完整 Lyra Main。继续复用 ALS 模型、69 raw / 81 logical / 68 skin。此次完成真实 Slot 求值需要的冻结混合历史与按骨权重计算，完整 Slot 姿态和生产接入保持开放。

## 实现

`AlsMontageRuntime` 在一次物理 Montage tick 后冻结 alpha、begin/desired weight、切换前的 linear start alpha、实际 blend option、active profile ID 和物理 Montage position。每条 Slot track 共用同一物理历史。Play/Stop 发生在冻结之后，改变候选而不改变本帧快照；提交、取消、自动淡出、Ragdoll 和缩短已有淡出均保留对应历史。

首次 Stop 使用 outgoing Montage 的 BlendOutProfile；缩短已停止实例只更新 start alpha 和待重置范围，保留原 profile/option。返回完整快照避免用 shaped weight 反推 linear alpha。Core 新增 `AlsMontageBlendProfile.BoneWeight`，实现原 `UBlendProfile::CalculateBoneWeight` 的 TimeFactor/WeightFactor 分支及 binary32 运算顺序。Lyra catalog 加载新策略时，把原 45 个 Montage 的 profile 引用绑定到实际 runtime 定义；已有时钟与 Slot 权重计算复用。

`LyraMontageBlendProfiles` 检查原目录、骨架、依赖字节哈希、原始条目、目标因子与每个物理资产的 In/Out 绑定。策略仍作为 ignored 资源提供。未生成新策略时原目录可用于既有资源/Slot Update 组件；后续完整 Profile Slot 求值须要求策略存在，不能把缺失资源解释为无 Profile。

## ALS 适配

原 FastFeet 为 TimeFactor，49 条显式源骨条目均为 0.5，其中15条可按名字绑定 ALS81；其余目标骨的默认因子为1。15条包括左右 thigh/calf/foot/ball、第一根 thigh/calf twist，以及 ik_foot_root/l/r。

34条条目属于 Manny 独有的腿部修正或第二 twist 骨。首次采集正确拒绝这些未映射条目，失败日志 `lyra-montage-blend-unmapped-first-ue.log` 保留。最终策略逐条保存 bone、scale、最近保留祖先和目标索引，并要求原条目和该目标祖先因子严格相同。例：ankle_bck_l→Foot_L、calf_twist_02_l→calf_l，均为0.5。

Godot loader 和独立 verifier 重新沿原164骨层级验证每条记录及因子；不能只信导出记录或省略不匹配条目。该适配保留 ALS 实际骨的原过渡时序，不增加模型蒙皮骨、不把多个0.5相加，也不声明 ALS 获得 Manny 修正骨的变形效果。后续 Slot pose oracle 必须使用这份明确目标策略。

## 原生与运行验证

新增独立 `AlsLyraMontageBlendLibrary`，读取原 `FMontageEvaluationState` 和原 BlendProfile，真实调用 Montage_UpdateWeight / Montage_Advance / UpdateMontageEvaluationData；完整权重计算调用 Engine 原生函数。未修改或复用改写已有被哈希固定的 Montage 探针。命令发生在冻结之后，包含早期中断、显式缩短已停止实例、较长重复 Stop、组替换、叠加和正常自动结束。

| 检查 | 最终结果 |
|---|---|
| 30/60/120Hz 实际 Montage | 13,440帧，45个资产均访问 |
| 冻结快照 | 13,528份，position/weight/alpha/begin/desired/start alpha/option/profile逐位同 |
| 活跃 Profile | 466份，137份发生0<start alpha<1的反转 |
| ALS81骨权重 | 37,746项逐位同 |
| 独立计算边界 | 1,728例；两模式、三个实际支持的blend option、阈值邻居/零/负/大于1因子/提前反转，逐位同 |
| 双 Slot 物理历史 | 3,038份，共用同一冻结 blend与position |
| 取消后重试 | 13,440帧，候选/实例身份/历史一致，命令不修改冻结快照 |
| Core相关测试 | 214通过、0失败、0跳过，含新增3项历史/别名门禁 |
| Debug / ExportRelease Optimize | 两构建0警告0错误，两次真实Godot运行退出0且无ERROR/WARNING |
| 原Slot两夹具回归 | 各13,440帧；原clock/weight/source context门槛保持 |
| 300源资源回归 | 510新/1,435旧姿态、9,960新/74,099旧标量行通过 |
| 不可变证据 | 664包路径、759旧JSON字节SHA保持；新探针source/package镜像一致 |

两个独立 UE 进程最终实际退出0，新 JSON 语义相同且未改已有文件字节；每份日志20条Warning，属于既有环境提示，不能报告UE零警告。未保存UE资产，未修改Engine或GASP58工程配置。

首次探针构建有 protected访问/共享引用用法错误，后续一次误直接访问private数据；均已改用受保护访问器，失败日志保留。修正后的完整外部BuildPlugin最终成功。独立计算与连续快照对照没有放宽误差、跳过骨或更改原生预期。

新 ignored 数据为 `montage_blend_v1_requests.json` 2,056,739字节、`montage_blend_v1_native.json` 3,264,684字节、`montage_blend_v1_policy.json` 11,962字节。可用 `export-lyra-montage-blend.ps1` 重采；`tools/verify_lyra_montage_blend.py` 审计包/历史/目标映射、源镜像、编译及实际退出。最终摘要为 `artifacts/lyra-analysis/lyra-montage-blend-verification.json`。

Optimize运行将明确allowlist的三个ExportRelease DLL/PDB暂放实际Godot加载的Debug目录，记录与ExportRelease相同的DLL SHA；finally按哈希恢复原Debug产物。最终verifier核对实际运行标志、三个优化DLL哈希和恢复结果。

## 余下完整目标

当前 MainPoseHost 五槽仍为inactive；此次尚未实现 Slot 对骨骼/曲线/typed属性/RootMotion属性的完整混合。下一步用本次冻结快照与300源构造真实五槽的原生姿态对照：保留有Profile时整个Slot切换混合路径、非additive按骨归一化、additive顺序、曲线/属性整体权重，以及原源裁剪和更新上下文。

随后完成原 Main 拓扑接线、主惯性化、最终ControlRig、完整Provider换类、统一Notify和根运动物理消费，接普通Demo、真实碰撞、多角色、渲染与性能验收。本次未运行完整Main场景、普通Demo、人工观感或性能矩阵；固定Provider和组件通过不能代替完整移植验收，整个线程目标继续开放。
