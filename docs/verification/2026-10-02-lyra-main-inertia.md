# Lyra Main75 惯性接入及联合原生验证

2026-10-02。本批完成原 Main Inertialization75 的完整通道组件及实际 Main 姿态宿主接入。只关闭该节点、受控真实叶子下的原节点组合和当前宿主事务门禁；整个迁移目标、完整原生 Main 和普通 Demo 未关闭。

## 实际运行路径

Main 五 Slot → FullBody84 → Inertialization75 → RotateRoot72 → Provider SkeletalControls。最终 ControlRig73 仍为后续工作。

`LyraMainInertialization` 复用 Core 精确姿态惯性核，补齐曲线存在性/flags、整数属性和 generated RootMotion 的原历史与应用路径。输入和历史为 ALS logical81、厘米和 double pose；最终 skin68 的发布点不变。原节点合同校验 Source84、空 BlendProfile、空过滤集合、ResetOnBecomingRelevant=false、ForwardRequestsThroughSkippedCachedPoseNodes=true 等设置。

Main 宿主在候选 Prepare 时复制已提交历史，收集实际图及活跃 Slot 的惯性请求；单次源 Sync 后，在 FullBody 之后求值。Update-only 保留累积时间和请求，重复 Evaluate 不再次累积 delta。角色组件世界变换和附着身份进入历史；提交/取消与 Main、Layer、cache、Montage bank 共用事务。最终曲线反馈仍在完整外层求值后提交。

当前原45 Montage 的 blend mode 均未产生实际惯性 Slot 请求。联合原生探针由真实原请求接收节点消费受控图请求，实际 Main 宿主另产生395条原图请求。不能把这些结果写成所有 Montage 惯性 blend mode 已验收。

## 精度根因及修正

原门槛保持 P1e-8 cm / quaternion1e-10 / scale1e-12；根属性平方门槛保持1e-16 / 1e-20 / 1e-24。没有改动既有 oracle 或放宽阈值。

此前不足一个 double ULP 的上游误差，经惯性 `acos(W)` 变成约3e-8的角差。相同原生输入注入仅用于隔离诊断，最终验收使用实际 Godot 上游输入。

- Win64 scalar FQuat Normalize 使用 `(X²+Z²)+(Y²+W²)` 的分组。
- 原 ApplyAdditive 中，FAnimationRuntime::AccumulateAdditivePose 归一化一次，节点自身再归一化一次。Main 上层 Dynamic 和 Recovery 均保留两遍；RootMotion 属性有独立路径。
- 原 LayeredBoneBlend 的 ISPC 乘法和 FastLerp 保留实际融合乘加顺序及 float 补数边界。
- full-weight local additive 走 ISPC AccumulateWithAdditiveScale；partial/profile 分支仍按原 scalar 路径。姿态与 RootMotion 的计算路径分别保留。
- ConvertPoseToMeshRotation 的两个初始乘积分别舍入，ConvertMeshRotationPoseToLocalSpace / ConvertPoseToAdditive 则将 W 项融合到已舍入 X 项。不能仅因同为 ISPC 就共用同一乘法顺序。

依据安装版 UE AnimationRuntime.cpp/ispc、AnimNode_ApplyAdditive.cpp、TransformVectorized.h 及实际对象反汇编。Shipping/Development 的 AVX512 AnimationRuntime 对象 SHA 相同。Core 的 mesh additive 差值新增显式 ISPC 参数，默认 scalar 路径保留；Lyra 资源选择实际原生路径。

只读算术诊断工具 `tools/analyze_lyra_main_inertia_precision.py` 不生成或修改验收数据。两段没有活动 Montage 的隔离样本，在补齐两次归一化后324个 quaternion 分量全部精确同。

## 原生采集与资源保护

既有 Main75 V1 包含三 Provider×30/60/120Hz、40,320物理帧、11,250次原生输出。同帧两次真实 ParallelEvaluate，实际45 Montage、五 Slot、原 ApplyAdditive/LayeredBlend/RotateRoot、owner cache及 Recovery 源。请求、组件平移/转向和瞬移为明确的受控输入；主状态机、完整 Linked source Sync、Notify、ControlRig 和碰撞不在此 oracle 范围。

两个既有独立 UE 采集退出0，语义一致。本批复用已固定探针追加 stages_v2 的首条完整1,920帧阶段诊断，采集1,250输出，实际退出0；逐输出与原V1同区间完全相同，证明阶段采集未改变结果。旧 Debug/Stages V1 和所有失败数据保留不覆盖。

外部 source/package 镜像、所有新旧探针 SHA、664个源/目标包和778个既有 JSON依赖由收尾工具核对。未部署 GASP58 插件、保存 uasset 或修改 Engine；新增阶段请求仍使用已有只读探针。

## 验证范围

| 门禁 | 结果 |
| --- | --- |
| 联合原节点组件 | 40,320帧、22,500次完整比较、1,822,500骨；三Provider三频率通过 |
| 曲线与整数属性 | 49,536曲线、74,784整数属性；值/存在性/flags及整数精确同 |
| 姿态最大差 | P2.929642751054232e-14 cm，Q1.1102230328969627e-16，S0 |
| RootMotion | 16,680次完整比较，P/Q/S差0 |
| 组件事务 | 40,320逐帧取消重试、36故障注入、11,250异代拒绝；两轮求值遵循原历史 |
| 实际 Main 宿主 | 40,320帧、39,375姿态、354,375最终反馈曲线；395真实图请求、4,263活动惯性求值 |
| 宿主事务 | 每帧取消重试、810重复求值、101,988坏操作拒绝，10,269 FullBody全覆盖、18空bank对照 |
| 旧活动槽回归 | 47,610帧、39,888次比较、3,230,928骨；原门槛通过，根属性差0 |
| Core / Standing | 数学及惯性52项、Standing独立/共享三频率原生6组通过，无失败/跳过 |
| 构建 | Debug、ExportRelease Optimize均0警告0错误 |
| Optimize 实际运行 | 原节点组件与实际 Main 宿主各40,320帧通过；加载三份 ExportRelease DLL，结束恢复六份 Debug DLL/PDB，逐文件 SHA 验证通过 |

Optimize 的两个实际 Godot 场景、最终证据汇总和 Debug 六个 DLL/PDB恢复由 `scripts/verify-lyra-main-inertia-optimize.ps1` 与 `tools/verify_lyra_main_inertia.py` 执行；收尾报告为 `artifacts/lyra-analysis/lyra-main-inertia-verification.json`。报告仅在所有必需门禁通过后写出。本记录不以构建成功代替 Optimize 实际运行。

收尾工具实际退出0，全部上述门禁及资源哈希通过。两个原生 Main75 进程各818条既有 Warning，新增阶段诊断336条 Warning，均0 Error；Godot 最终 Debug/Optimize 场景均无 ERROR/WARNING。初次 verifier 将 JSON double 请求直接与 native float duration 比较而失败，修为明确 float 边界后通过；失败日志保留，原数据和算法门槛未变。

## 保留失败及剩余工作

frame162/bone52、frame612/bone66、frame951/bone74和修订期间mesh乘法顺序错误的失败日志均保留。真实输入诊断由首720帧64次失败降到0，扩至1,920帧后也为0；最终整个40,320帧门禁通过。诊断程序会计数失败后退出0，因此收尾工具还要求 failures=0，不能只看进程状态。

当前真实宿主仍为受控玩法观察、固定Provider和既有实际source范围，不是完整原生Main oracle或生产换类。ControlRig73启用、所有初始化/重入边界、Provider换类/多角色身份、统一Notify、root物理、普通Demo、渲染/性能及人工地形/握持矩阵继续按 ROADMAP 推进。无本批全量、十分钟或渲染验收，没有提交或推送。
