# Mantle 实体 twist 重建与实际发布验证

本轮落实“先补齐 twist 重建”：独立 Pipeline 烘焙 ALS 八个实体辅助骨，纯 .NET Core 补偿 Mantle 手部 IK 与显示模型比例 IK 新增的轴向旋转。新候选为 `als_nonprop_mantle_twist_20261012_v2`，六个 Mantle 身份复用原先验收的源动作，无新 diffusion。完成转换、Root/Pelvis 分解、逐资产曲线、最终 FBX 回读、Godot 导入及相关实际场景。默认仍为 `als_nonprop_crouch_slip_20261011_v2`，模型仍为保留比例的女角色；没有提交、推送或生产启用。

算法契约见[独立 Pipeline 的 twist 重建](../../../Kimodo-Pipeline/docs/architecture/kimodo-twist-reconstruction.md)。主要代码是 Pipeline 的 `twist_reconstruction.py`／`offline_retarget.py`、stage 与运行前门禁，以及 Core 的 `AlsTwistBoneReconstruction`、`AlsMantlingHandSolver` 和 `AlsSkeletonPoseRetargetModel`。Godot 只提供骨名与参考姿势，不复制算法。补偿只改变叶子旋转；没有新时钟、历史累积、骨长拉伸或独立提交链路。

## 交付与来源

- [生产结果](../../../Kimodo-Pipeline/workspace/runs/mantle-twist-20261012-v2/reexport-result.json)：六个 Mantle 重定向，保留 155 个目标输入；仍缺 22 个非道具身份。源质量回执、NPZ／BVH、raw／FBX 逐文件验哈希，来源不变。
- [逐姿势对比](../../../Kimodo-Pipeline/workspace/runs/mantle-twist-20261012-v2/pose-comparison.json)：六个最终 Mantle 各只改变八根 twist 的旋转；主骨、指骨、平移、缩放与时间轴保持。其他 155 个最终 raw 语义逐一相同，70 个道具保持。
- 161 个最终 FBX 全实体骨／全键回读通过；[六个 Mantle 再次独立回读](../../../Kimodo-Pipeline/workspace/runs/mantle-twist-20261012-v2/fresh-mantle-fbx-readback.json)通过。High 为 68 骨／81 键。
- 六个 `targetTwistChecks` 及原目标 Mantle 检查通过。High 左前臂烘焙的参考局部偏转峰值 48.712°，最大单键步进 14.403°；旧轨道为恒定参考姿势。新门禁从最终 raw 重新求解，不能用“报告声称通过”和重新计算字节哈希掩盖恒定轨道。
- 制作包 SHA256 `36709198b5cf4ebe13fa83c1b26723b1593055a3d3f4abb84e6e7ee5f717e077`；Godot 六域请求绑定后消费包 SHA256 `bae7794e563c1bbb584408d684f79adc9849f619ecd536576ee15458264363f0`。消费侧 1,774 个登记载荷完整性通过，最终数量以[完整报告](../../artifacts/tests/mantle-twist-20261012-v1/final-report.json)为准。

首轮在 Python 后端遗漏 Kimodo 模块搜索路径，已完成转换但 Root 阶段失败；`mantle-twist-20261012-v1/conversion.log` 和未完成包保留。v2 在完整 Kimodo 环境验哈希恢复已完成的转换，并另建包，没有覆盖失败证据。模型和源资产均未替换。

## 代码与受控回归

Python 39/39：纯旋转／非轴向 swing、真实骨段轴与左右镜像、非恒等参考旋转、跨 ±180° 连续、缺失／错误叶子与纯 180° swing 拒绝、恒定轨道即使重签哈希仍拒绝、原 FBX／stage／有界手修复。日志在[生产测试](../../../Kimodo-Pipeline/workspace/runs/mantle-twist-20261012-v2/production-tests.log)。

Release Core 76/76，包括实际 IK 改变 twist、保留已有原版分布、无变化保持原值、无历史的重复求值、错误姿势原子拒绝、double／float 路径和启用 twist 的比例 IK 零分配，及既有 Mantling／换模回归；见 [Core 日志](../../artifacts/tests/mantle-twist-20261012-v1/core-tests-final.log)。Release Import 23/23，motion／root／contact／montage 原断言通过；见 [Import 日志](../../artifacts/tests/mantle-twist-20261012-v1/import-tests.log)。Optimize 构建通过；当前没有重跑全部 Import，旧五项摘要失败记录仍保留。

## 真实 Godot 骨骼、蒙皮与事务

新包实际导入、P2B 编译、六域绑定和曲线／twist／Air 前检通过。完整适配器仍返回 `runtime_failed`：选择的八个 E2E 场景全部通过，但没有选择完整方向覆盖；原矩阵门禁保持拒绝。Air 11/11、Low 接触静态 11/11、平台与中断场景 11/11 通过。见[适配器结果](../../artifacts/tests/mantle-twist-20261012-v1/pipeline-runtime/runtime-result.json)，不是完整生产认证。

| 检查 | 实际结果 |
| --- | --- |
| 1 m Low、1.5 m 与 2 m High；女角色／原人偶；Root 开／关；单／并行，60 Hz | 24/24，8,640 帧；包、代码和 DLL 冻结输入不变 |
| 1.5 m 双模型真实 GPU | 2/2，720 帧、120 张截图；与对应 headless 完整数值帧逐一相同 |
| 实际发布的八根 twist | headless 24/24、GPU 2/2；轴向、连续性与 High 支撑响应通过 |
| 单／并行 | 12 对完整帧逐一相同，含双骨架与局部／参考旋转 |
| 注入发布前 worker 故障，女模型，Root 开／关，单／并行 | 4/4；68 骨显示姿势、接触、身体修正及发布帧精确保留；恢复后支撑清除 |
| 原人偶关闭手锁负对照，Root 开／关 | 2/2 检出，最大穿入 4.717 cm，原 2 cm 门槛保持 |
| 扩展女模型关闭手锁负对照，Root 开／关 | 0/2：该模型没有在此夹具上产生穿模；旧包同样 0/2，失败保留，未削弱断言 |

24 个组合中，女模型 High 最大穿入约 0.440 cm，原人偶约 1.608 cm；贴沿误差、掌面、骨长及落点继续按原门槛验收。全范围八骨相邻帧最大步进 16.227°，最大非轴向残量 0.0893°；没有半比例角度在 ±180° 处分支翻转。报告分别在 [运行矩阵](../../artifacts/tests/mantle-twist-20261012-v1/candidate-matrix/results.json)、[发布 twist](../../artifacts/tests/mantle-twist-20261012-v1/twist-published.json)、[GPU twist](../../artifacts/tests/mantle-twist-20261012-v1/twist-rendered.json)和[回滚](../../artifacts/tests/mantle-twist-20261012-v1/rollback/results.json)。

复现定位帧为女模型 1.5 m、60 Hz、tick 113、montage 1.7333328 s、左手权重 1。左上臂 twist 局部偏转 20.628°，左前臂 41.250°，替代旧版近 0°；肘弯仍约 0.000046°，腕骨相对参考的完整 SO(3) 偏转仍为 125.965°。后者不是单独的轴向角度。辅助骨重建不修改主骨，也不自动修复完全伸肘及腕部摆转；这仍是下一处姿态问题。

复现命令在 Godot 主目录执行，产出目录必须更换：

```powershell
python scripts/verify-mantle-contact-models.py --pack res://assets/generated/als_nonprop_mantle_twist_20261012_v2/resource-pack.json --output artifacts/tests/<fresh>/matrix --cases low medium-high nominal-high --models girl mannequin --roots off on --hz 60 --modes single parallel
python scripts/verify-mantle-twist.py --runtime-dir artifacts/tests/<fresh>/matrix --output artifacts/tests/<fresh>/twist.json
```

## 剩余边界

本轮完成实际骨段轴的 twist 重建与相关运行补偿。半比例、局部轴向与连续通过不等于原版 twist 等价、支撑关节限位、蒙皮体积／自交或人工美术验收。需要继续保留肘弯、联动肩／身体、约束腕部摆转并重做姿态对照；既有释放身体跳变、原版自由指节、其他动作和完整 R6 仍开放。女模型负对照需要能稳定揭示贴沿错位或失锁的独立夹具，当前失败不能提升为通过。

默认旧包及其烘焙轨道保持；运行 IK 增量补偿已进入普通代码。要观察完整新烘焙结果，需显式选择本候选。未将新包正式启用，22 个源缺口、完整方向／原生／Overlay／地形／装备、长时间、人工及性能验收没有关闭。旧诊断和已失败阶段证据保留。
