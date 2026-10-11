# Kimodo 跑步生产范围

入口为 `tools/produce_als_resources.py run --scope run`，默认读取 `tools/kimodo_pipeline/run_rules.json`。只选择非道具 `gait_cycle` 中的六个 Run 语义，共十二个 V4／Refactored 身份；不包含参考 Pose、Jump_Run 或 Sprint。沿用共有 DAG、作者门禁、纯脚本转换、Root/Pelvis 分解、逐资产曲线、最终 FBX 回读和 Godot 验证。

前进、后退、左前与左后分别生成。原版 Left_Forward／Left_Backward 都沿 +X 横移，髋部朝向不同；右侧默认从通过作者检查的左侧母动作镜像，保留原时间边界并交换循环支撑脚。源和镜像都重新检查实际几何，不将右脚起始的镜像裁到原动作另一脚的相位；时间反转分支仍独立重新选择实际落地。镜像仍失败时，右侧 Run 可显式设置 `independentGeneration=true`，但必须使用 `native_als` 或 `generated_mirror` 数值条件和生成后源门禁；不会自动把失败镜像改为未约束的随机动作。`generated_mirror` 的 `constraintSource` 必须是对应已验收左侧母动作，验证其实际运动摘要和轨迹后重复镜像为条件；该条件明确不可交付，只有新的真实 diffusion 输出通过源和目标门禁后才能交付。后退采用 -Z、3.5 m/s、20/30 秒原生周期。目标检查以对应原版 Run_Backward 校准足底、脚趾支撑和事件；已有前向门槛保持。

源检查采用 `soma-gait-authoring-v3-unique-period`：周期首尾包含同一相位，只用于姿态／速度接缝、接触连续性及穿地检查；滑移中位数、分位数和支撑样本数量只计算每周期独立帧。循环接触检测仍读取相邻重复周期，不能因去重丢失端点支撑。`cycleAnchorSide` 明确保存起始支撑脚，其他脚事件与 FootPlanted 符号仍使用实际左右标签。复用或镜像已有回执时，运动摘要绑定之外还重新检查完整轨迹与当前几何门限，旧 `pass=true` 不能绕过新统计。

左侧 Run 可使用 `preserveReferenceHeading`，保留参考髋线的逐相位朝向，并让 Root2D 条件与同一姿态一致。`repairStanceResidualLock` 在周期修复后锁定实际观测支撑阶段的足底中点水平残差，保留足部世界旋转、脚趾和竖直摆动；`repairStanceResidualPaddingFrames` 默认零，最多为周期八分之一。内部重复周期提供跨边界锚点，固定骨长 IK 不裁剪不可达目标。额外旋转、脚部位移和必要的髋部降低与此前周期／接缝修复预算累加，仍受原 20 度、15 cm、5 cm 限额及独立输出门禁约束。

目标周期修复现在识别原版 `contactMode`。脚趾支撑使用实际 ball 竖直极值及低速窗口，避免把低位快速 toe-off 当成整个支撑区间；窗口仍独立于绝对地面高度。IK 锚定同一球部足底点，保留原足部旋转；只有脚跟会穿地时才进行最小俯仰校正，新增的俯仰修复上限为 20 度。原脚部位移 8 cm、髋部降低 5 cm 限额及严格目标门禁保持。平脚修复沿用既有实现，不自动把所有动作改成平脚。

`gaitClosure.<semantic>.contactPaddingFrames` 可将独立测得的循环接触计划在两侧延长整数帧，默认零，上限为周期帧数的八分之一。这是 IK 的制作计划，不是接触标签或验收凭据；源输出仍必须重新通过完整实际 FK、交替支撑、近地滑移和接缝门禁。周期旋转拟合 20 度、脚锚 15 cm、髋部降低 5 cm 的原限额保持。回执同时保存 `measuredContactIntent`、`repairContactIntent`、padding、实际修复限额和独立输出检查。

`footBoundaryContextFrames` 可在周期边界及其邻域追加左右脚的实际 Kimodo 末端位置／朝向条件，包含当时的摆动脚；它与支撑锚点取并集，共用同一 IK 后的姿态与 Root。各稀疏类型仍少于 20 帧，超预算或不可达条件在 diffusion 前拒绝。全身位置条件不能代替脚部朝向条件。`generationCfgWeights=[text,constraints]` 或分支 `cfgWeights` 可选择原生 separated CFG；只接受两个有限非负数，省略时保持模型默认配置。

原生条件可选择 `constraintSeamSmoothingFrames` 与 `constraintSeamSmoothingMask`（`all`／`upper_body`）。接缝由实际交替支撑识别，半径最多为周期八分之一，参考旋转／Root 修正最多为 20 度／5 cm；上身模式逐帧保留骨盆、腿链及 Root。该资源始终只作条件，不能直接交付。

实际生成输出可选择 `repairSeamSmoothingFrames`，以同样的有界姿态邻域结合固定骨长 IK 保留测得的支撑脚世界位置，调整摆动脚接缝。最终修正预算为原周期修复上界加本次接缝额外上界，合计仍不得超过 20 度、15 cm、5 cm。`repairBothSupportSeams` 可处理两个测得的落地相位，额外上界从处理前后实际最大差计算；它不保证镜像通过。`flatStance` 和 `stabilizeKneePoles` 是制作参数，不能代替生成输出、镜像输出和目标骨架各自重新验收。

目标竖直相位的闭区间为 `[0.35,0.65]`，使用直接区间比较避免 `0.35-0.5` 的浮点边界误拒绝。前进 Run 没有 Walk 的固定停步帧消费者，其脚跟落地搜索可跨循环第零帧；真实脚跟平面穿越、支撑和半周期检查仍必需，Walk 原固定帧合同保持。实际对照与失败证据见[约束修复记录](../verification/2026-10-11-kimodo-run-constraints.md)。

`--upstream-receipts` 可复用明确已有生成候选，用于调整修复配置时避免重复 diffusion。未通过循环的候选只作为修复输入；`reuse_motion` 不会批准它。`--base-pack` 验证并保留已有交付，独立新源完成后继续整链。没有任何新增源交付时，管线明确停在 `source-quality` 失败，不能用纯旧资产包冒充本批跑步验证。

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe tools/produce_als_resources.py run `
  --scope run --kimodo-repo G:/Github/kimodo `
  --output artifacts/kimodo_als/<new-run> `
  --base-pack assets/generated/als_nonprop_20261010_derived_v2/resource-pack.json `
  --runtime-case ordinary-default-entry `
  --runtime-case stance-60-single --runtime-case stance-60-parallel `
  --runtime-case directional-single --runtime-case directional-parallel `
  --runtime-case rendered-directional
```

省略 `--runtime-case` 时仍执行完整矩阵。相关场景验证不能代替全量生产验收；候选不会自动成为默认包。实际批次、失败方向和覆盖边界记录到验证文档。
