# 1.5 m Mantle 女角色持续支撑时的腕部扭曲诊断

用户确认默认女角色在腿已经抬上台面、手仍持续支撑时出现明显扭曲。真实 Godot 同帧骨骼对照显示：换模接触求解只保证手能到达台沿，没有保留自然肘弯；保持掌面锚点朝向时，额外姿态补偿集中到腕部，已有 twist 辅助骨没有分担新增旋转。本轮完成诊断与证据记录，未修改运行算法或替换资产，不能表述为修复通过。

## 输入与实际复现

在主目录 `F:/Github/Godot-Locomotion` 使用 Godot 4.7.2 Mono、当前默认女模型 `girl_high_als_retarget.tscn` 和活动诊断包 `als_nonprop_crouch_slip_20261011_v2`；包 SHA-256 为 `e25c96e7433315a4acb7c458addcd8d04cee0800239f14a4d783820796b4f0af`。新运行限定 1.5 m、Default Overlay、60 Hz、Root 关闭、并行模式。

`medium-high` 女角色与人偶各运行 360 帧，保存共 120 张真实 GPU 截图；旧接触门禁两项都通过，完整蒙皮最大穿入分别为 0.43613 cm 和 1.60750 cm。另以只读 GDScript 在原场景发布后采集两模型各 360 帧的实际 Skeleton3D 世界姿态、局部旋转和 `GetBoneRest`；其普通轨迹与 GPU 运行逐帧完全一致。运行期间源文件、配置、模型、1768 个登记载荷和程序集冻结摘要未变。见[运行结果](../../artifacts/tests/mantle-wrist-twist-20261012-v1/rendered-1p5/results.json)、[输入 manifest](../../artifacts/tests/mantle-wrist-twist-20261012-v1/rendered-1p5/manifest.json)与[观察器一致性](../../artifacts/tests/mantle-wrist-twist-20261012-v1/observer-validation.json)。

1.5 m 台面实际探测高度约 151.9 cm，按既有 125 cm 分界正确选择 High。高度入口为 0.093333334 s，播放倍率 1.2。对上一轮冻结的 2 m 轨迹作同样计算，也出现女角色支撑臂完全伸直；此问题不限于 1.5 m 高度分界。

## 提腿后同帧数据

tick 113、montage 1.7333328 s，左手权重为 1，右手已释放。肘弯以 0° 表示伸直、90° 表示直角弯曲；腕部总角度是手相对前臂的局部旋转与参考姿态之差，不是单独的轴向扭转。

| 测量 | 显示女角色 | 同帧驱动人偶 |
| --- | ---: | ---: |
| 上下臂总长 | 44.3448 cm | 57.3151 cm |
| 左肘弯角 | 约 0° | 93.40789° |
| 肩到手距离／臂长 | 约 1.00000 | 保持肘弯 |
| 腕部局部旋转相对参考姿态 | 125.96517° | 107.92186° |
| 女角色腕部局部姿态相对驱动人偶之差 | 42.29259° | 基准 |
| 女角色掌面世界旋转相对驱动人偶之差 | 约 0.000026° | 基准 |
| 女角色身体接触修正 | 11.83142 cm | 无换模修正 |

1.5 m 满权重支撑的 99 个左右手观测中，女角色 48 个肘弯小于 1°；人偶为零，最小仍有 85.88654°。这里按单侧手观测计数，不能误称为 48 个独立帧。2 m 女角色相应为 49/101 个。

实际骨骼读回显示，tick 85／95／105／113 的左上下臂 twist 骨局部旋转相对参考姿态仅约百万分之几度，且与驱动局部旋转一致；它们随父骨运动，但没有收到新增腕部旋转的分配。手和 twist 的参考轴与驱动误差也仅约百万分之几度。数据见[角度与逐侧样本](../../artifacts/tests/mantle-wrist-twist-20261012-v1/arm-diagnostics.json)、[实际 twist 局部读回](../../artifacts/tests/mantle-wrist-twist-20261012-v1/twist-local-diagnostics.json)、[女角色骨骼读回](../../artifacts/tests/mantle-wrist-twist-20261012-v1/girl-twist.json)及[人偶骨骼读回](../../artifacts/tests/mantle-wrist-twist-20261012-v1/mannequin-twist.json)。

女角色腕部轴向分量峰值约 82.49°，人偶约 91.80°，因此不能将上述 126° 称为纯轴向扭转，或声称女角色所有旋转分量都大于人偶。主要异常是完全伸直后的腕部整体姿态补偿及接缝形变。早期 TSCN basis 解析误转置所得参考角度已作废并单独保留，正式角度使用引擎直接读回的参考姿态。

## 代码与因果链

1. [`PrepareContactRoot`](../../src/Als.Core/Locomotion/AlsSkeletonPoseRetargetModel.cs) 按上下臂全长建立可达球，将身体移到刚好能够到达的位置。边界对应完全伸直；没有为支撑保留肘弯，也没有联动肩锁骨姿态。女角色臂长较短，提腿后肩随身体上升，固定手锚点更容易将求解推到该边界。
2. 同类的 `Solve` 使用当前肘位置作为双骨 IK 的弯曲参考。满接触时直接令手的世界旋转等于驱动掌面旋转。当前臂姿态改变、掌面保持时，手相对前臂的局部旋转必须额外补偿；没有腕部关节活动范围或扭转分配。
3. [`AlsTwoBoneIk`](../../src/Als.Core/Locomotion/AlsTwoBoneIk.cs) 的目标距离达到全长时显式输出共线肩／肘／手；[`AlsRigTwoBoneIk.Apply`](../../src/Als.Core/Locomotion/AlsRigTwoBoneIk.cs) 对其余子骨保留原局部姿态。上下臂 twist 是实体分支骨，单纯传播父骨不会自动分摊手腕相对前臂的旋转。驱动侧 [`AlsMantlingHandSolver`](../../src/Als.Core/Locomotion/AlsMantlingHandSolver.cs) 同样只解上臂／前臂／手，当前源支撑姿态本身已有较大的腕部旋转。

已有 twist 骨和蒙皮权重，并不等于动作运行时已经驱动其扭转。当前证据优先指向求解及旋转分配；未进行独立的逐顶点权重 A/B，也未量化扭转修复后的体积损失，因此不能将所有视觉程度归因于某一组权重。

## 验证缺口与修复方向

[`verify-mantle-contact-models.py`](../../scripts/verify-mantle-contact-models.py) 当前检查掌面／手腕锚点、骨长、可达残差、身体修正预算、2 cm 全蒙皮穿模、平台漂移与结束落点，未检查支撑肘弯余量、腕部局部姿态、twist 分配或接缝形变。这解释了本次旧门禁两项通过、用户仍看到扭曲的现象，也限制了[上一轮 88/88](2026-10-12-mantle-root-model-contact.md)的验收含义。

后续先将接触适配从全长可达扩展到具有肘弯余量的支撑姿态，联动肩／身体；再以实际骨轴分解腕部 swing/twist，将适当旋转分配到已有实体 twist 骨并约束腕部；补同帧原生／目标骨骼对照和连续 GPU 接缝检查，保持现有贴沿、穿模、骨长及提交／回滚门禁。Low 支撑释放连续性和原版自由指节穿模仍是独立开放问题。

实际查看了女角色和人偶抓沿／提腿后各两个 GPU 样本；截图是有限抽查，采集文件名与 HUD 帧可能存在渲染队列差异，同帧数值以 JSON tick 为准。未重跑完整 Mantle 矩阵、Root 开启／全部 Overlay／地形、UE 整角色、长期性能或完整制作 Pipeline；本轮没有完成这些验收。

诊断完成后的收尾核对发现，共享 `GodotALS.dll` 在观察器结果保存后发生更新；1790 项冻结输入中仅该文件改变，求解源码、配置、模型与载荷保持一致。见[单独保存的结束后输入审计](../../artifacts/tests/mantle-wrist-twist-20261012-v1/post-diagnosis-input-audit.json)。上述通过及骨骼数值仅认证保存的诊断版本，更新后的程序集没有复测；本次没有重写运行期间的通过报告来掩盖版本差异。

## 复现

输出使用新目录以保留原报告。只读观察器与分析脚本位于[本次证据目录](../../artifacts/tests/mantle-wrist-twist-20261012-v1/)，未接入生产求值链。

```powershell
$mantlePython = 'F:/Github/Kimodo-Pipeline/.venv-win/Scripts/python.exe'
& $mantlePython scripts/verify-mantle-contact-models.py `
  --pack res://assets/generated/als_nonprop_crouch_slip_20261011_v2/resource-pack.json `
  --output artifacts/tests/mantle-wrist-new-diagnosis `
  --cases medium-high --models girl mannequin --roots off --hz 60 --render
```
