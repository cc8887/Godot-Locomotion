# Kimodo Mantle 实际生成与部分交付

本批实际运行 Kimodo-SOMA-RP-v1.1 扩散推理，覆盖 High、Low_Left、Low_Right；不是前一批的参考条件夹具。合同见[类型分派及导出](../architecture/kimodo-mantle-export-policy.md)，前一批受控结果见[夹具验证](2026-10-09-kimodo-mantle-export-policy.md)。

## 推理与源门禁

显存当时接近占满，本批使用 CPU、4 个 Torch 线程、30 FPS、种子 `20261009`、100 diffusion steps。新提示词先由本机原生 LLM2Vec / Llama / PEFT 编码，使用既有文件映射 BF16 参数降低私有内存占用；原生 PEFT 合并等价检查通过。缓存仅匹配三条完整提示词和原模型检查点，后续各轮复用同一份准确编码。没有复用旧动作作为新候选，也没有给生成节点绑定参考输入。

每轮证据位于 `artifacts/kimodo_als/mantle-generation-20261009-v<N>/`，包括 `graph.json`、`rules.json`、`batch-result.json`、`receipts.json`、实际 `generated.npz`、生成日志及失败节点下的 `mantle-validation.json`。四轮共完成 12 次实际扩散生成，保持相同提示词和种子，以区分条件与后处理变化。

| 轮次 | 条件与后处理 | High | Low_Left | Low_Right | 源交付身份 |
| --- | --- | --- | --- | --- | --- |
| v1 | 原始稀疏全身、手脚条件；原生后处理 | Pelvis 最大偏差 30.600 cm，拒绝 | 通过 | 通过 | 4 |
| v2 | 补充逐帧 `Hips` 条件；原生后处理 | Pelvis 最大偏差 21.222 cm，拒绝 | 手部漂移 2.228 cm，拒绝 | 通过 | 2 |
| v3 | 同 v2 条件；关闭原生后处理 | 手掌、Pelvis 与结束双脚均失败 | 拒绝 | 拒绝 | 0 |
| v4 | 补充间隔不超过 0.25 s 的全身条件；恢复原生后处理；High 81 帧 | Pelvis 最大偏差 16.434 cm，拒绝 | 通过 | 通过 | 4 |

High 的失败门槛始终为 15 cm，未放宽。v4 的双手位置/朝向/漂移、Pelvis 起止、结束双脚通过，仅全程身体轨迹仍超限；因此没有新 High 交付、目标曲线或最终 FBX。首轮 High 因 `playLength + 1/FPS` 的浮点截断实际生成 80 帧，现按整数采样数/FPS 算时长，生成 81 帧；重采样往返误差约 0.36 cm，不能将原有 21–30 cm 路径错误仅归因于少一帧。

最大偏差在 v4 第 73 帧、2.433 s，生成髋部较参考低 16.176 cm；这里已结束手部支撑并进入双脚完全支撑，生成动作出现一次身体下沉。逐帧对照见 v4 `high-trajectory-diagnostic.json`、`high-trajectory.csv` 与 `high-trajectory.png`，图由独立 artifact 目录的 Matplotlib 生成，不改 Kimodo 环境。它分析的是 SOMA 源髋部轨迹，不能把图中的身高当作 UE 导出的名义 Root 位移。

## 本次修正

SDK 能加载通用 `end-effector` / `Hips` 的位置与旋转，但后处理掩码没有该类型；`CorrectHipsY` 只把全身关键帧当作竖直位置硬条件。补充逐帧 Hips 条件改善模型输入，同时限制全身关键帧间隔，保留原生手脚后处理和本管线的两级有限 IK。关闭原生后处理的对照质量更差，未设为默认。

作者阶段不复制参考 Root/Pelvis 去覆盖生成输出，仍执行原来的 8 cm / 30° 手臂修复和 2 cm / 12° / 漂移检查。目标阶段与 Root/Pelvis 分解合同保持；任何源成功都必须独立通过目标检查。

完整 pipeline 新增零交付保护：全部源失败时保存 receipts 和空作者 bundle，返回 `source-quality` 拒绝，后续导出与运行受阻。不会生成一个纯原版 fallback 包来宣称零资产 FBX 验证或新生成运行通过；其他轮次有独立成功资产时继续转换。

第三轮后台 worker 在加入保护之前已导入旧模块，首个 continuation 仍创建了纯原版 fallback 诊断包并开始运行。发现后只停止该任务的进程树，完整旧证据保存在 v3 `full-pipeline-stale-zero-delivery/` 并标记 `invalid_for_generated_asset_validation`；旧包的零资产 FBX 汇总改为明确无交付、未验证，Viewer 只保留三个被拒绝的实际源候选。重新加载新代码后的 v3 `full-pipeline/result.json` 为 `source-quality` / 0 deliveries，后续阶段受阻；不计任何该轮导出或运行通过。

最终 **177 项 Python 测试通过**，见 v4 `python-tests-final.log`。覆盖真实 SDK 的 Hips 全帧条件、手部位置/旋转加载、High 帧数、全身条件间隔、原版姿态/有界 IK 负例、部分交付继续转换及零交付禁止伪验收。首次全套测试未添加现有离线 ufbx 依赖路径，3 项依赖错误保存在 v2 `python-tests.log`；补齐 `PYTHONPATH=artifacts/kimodo_als/offline-python-deps` 后通过，失败记录未覆盖。

## 转换与运行

v1、v4 各四个 Low 身份、v2 两个 Low_Right 身份均通过独立目标门禁、逐资产 Root/接触曲线导出与最终 FBX 逐物理骨骼逐键回读。最新包为 `assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json`，四个目标证书、四份 Root 曲线及四份独立提取 Root 曲线，目标失败清单为空。各轮转换结果位于 `full-pipeline/conversion/pipeline-result.json`，逐资产证书位于相应诊断包 `target-mantle/`，最终回读位于 `root-ownership/*/fbx-readback.json`。

运行验证串行使用同一项目的 Godot 导入缓存，避免资源扫描更改另一轮候选的可见性。v1 默认运行完整 71 个场景；后续成功分支仅复核全部 16 个 Mantle 场景和 `rendered-mantle`，每轮仍执行 Optimize 构建、Godot 导入、P2B 和六域源请求刷新。显式场景选择保存在 runtime report，不作为完整矩阵验收，也不改变默认 CLI 的 71 场景路线。

v1 完整执行 71 个场景，69 项通过。失败为 `recovery-interrupt`（退出时落地状态不匹配）与 `rendered-release-forward`（第 35 tick 截图未呈现请求的已提交 pose）。方向三模式原版对照通过，但本包的方向资产为原版 fallback，不是新 locomotion 验收；资产载荷和动画设置均无变更。详细失败、截图身份及原版对照证据位于 v1 `full-pipeline/runtime/e2e-matrix/`。

v2、v4 的全部 16 个 Mantle 场景及 `rendered-mantle` 各 17/17 通过，资产载荷和动画设置均无变更。覆盖低/高攀爬的三种物理 Hz、Root Motion 开/关、移动目标、空中、阻挡、快速目标、取消与销毁。源拒绝不因原版 fallback 场景通过而改判，High 相关运行场景使用原版 fallback，不能计作新 High 验证。各轮 `runtime_failed` / `pipeline_complete_rejected` 是明确诊断部分包的验收拒绝，不应改写为完整生产通过。

为避免只渲染原版 High，再单独运行新 v4 包的 Low 场景：`full-pipeline/runtime/rendered-low/result.json` 为 exit 0、`MANTLE_DEMO_OK case=low`、60 张实际截图，包及动画设置摘要保持；查看第 60/84/120 帧确认抓沿、上台与回到站立的不同姿态。本次仅有限帧视觉检查，没有人体动作风格、全种子、全角色或人工操作验收。汇总证据为 v4 `verification-result.json`，最终包 SHA256 为 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`。

## 交付边界

最新源批次 v4 只接受两个 Low 母动作及四个 V4/Refactored 身份。其余 179 个非道具身份含 High 两个身份保持缺失/fallback，包不能作为完整生产包启用。当前活动指针保持 `als_nonprop_20261009_idle_arms_v2`，新包 `diagnostic` / `productionReady=false`，未提交或推送。

Viewer 登记四轮真实候选与通过的母动作；失败 High 只具有候选阶段，不补造后续阶段。后续仍需解决 High 中间阶段的竖直轨迹偏差、进行生成动作的人工视觉验收，并另行实现实际 Motion Warping 消费协议。
