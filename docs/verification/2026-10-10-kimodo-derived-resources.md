# 2026-10-10 缺失 Kimodo 资源派生

本次在主目录检查并保留已有修改，实现从上游真实生成输出独立采样静态参考姿态、Lean/Acceleration，以及显式复用通过源检查的方向母动作。未提交、推送或修改活动包指针。稳定契约和可执行命令见 [上游复用与采样设计](../architecture/kimodo-derived-resources.md)。

## 实际生产

基底为当时活动的 `als_nonprop_20261010_landing_prompt_combined_v4`，保留其 123 个已交付身份。显式输入是 `artifacts/kimodo_als/nonprop-regeneration-20261009-v1/receipts.json` 与 `artifacts/tests/2026-10-08-directional-gait-regeneration-v1/final-profile-results-v2.json`，逐文件和 receipt 身份、rig 验证。没有读取归档 worktree，也没有把原版约束参考或失败的循环作为通过母动作。

| 新增语义 | 数量 | 制作方式 |
| --- | ---: | --- |
| Lean_Pose、Run_Forward_Pose、Run_Left_Pose、Run_Right_Pose、Crouch_Walk_Pose | 5 | 前进跑、左前跑、前进蹲走生成候选的第二原生周期 1/4 相位采样，右跑姿态镜像 |
| Lean 中性及四方向、Acceleration 四方向 | 9 | 同一采样 Lean 基底的已有确定性 offset |
| Crouch_Walk_Left_Forward、Left_Backward、Right_Forward、Right_Backward | 4 | 复用已有通过源作者门禁的精确母动作及镜像／反向派生结果 |

新增 **18 个去重语义、32 个 V4/Refactored 资产身份**，新 diffusion 为 **0 次**。姿态/叠加覆盖 28 身份，四个侧蹲走覆盖 4 个 Refactored 身份。Run 与 Crouch 采样帧分别为 30（1 秒）与 42（1.4 秒）；源局部旋转和竖直 Root 保持，只移除原数值平面路径。

作者选择了 39 个缺失派生目标，32 个完成。其余分支记录两个缺失上游输入和两个原周期修复失败；失败与依赖阻断保留。循环未通过不阻断独立静态资源，但不会批准跑步镜像循环或跑步跳跃过渡。

新诊断组合包为 `assets/generated/als_nonprop_20261010_derived_v2/resource-pack.json`：155 个替换身份，剩余 28 个身份／15 个语义。剩余为 Run 六个循环、Crouch_Walk 前后和纯左右四个循环、两个 Jump_Run、Sprint、Sprint_Acceleration、Walk_Backward。

## 验证与修复

- 最终代码相关 Python **95/95** 通过，包含独立采样、原源不变、篡改拒绝、完整组合来源、零新增拒绝、旧循环失败保留，以及 Air、方向、Root 和最终 FBX 回归。日志 `artifacts/kimodo_als/derived-20261010-tests-v3.log`。
- 155 个资源纯脚本重定向全部完成，逐资产 Root/Pelvis、曲线、最终 FBX 全骨骼／采样键回读 **155/155** 通过；十个既有 Walk 和四个新增侧蹲走目标步态门禁 **14/14** 通过。
- 首轮 v1 的保留输入合并错误保留日志；修复字典迭代并补回归。v2 首次完成全部转换和最终 FBX，但组合包被 Viewer 的单源批次归属检查拒绝；修复为独立组合目标批次，不放宽同批次检查。使用验证摘要的 resume／finish-existing-pack 继续剩余阶段，未重新生成或覆盖旧批次。
- Godot 优化构建、资源扫描、实际导入、P2B 与六域源请求绑定通过。普通候选 Demo、60 Hz 单线程／并行蹲站、单线程／并行／GPU 方向移动 **6/6** 通过。方向场景各 6,720 帧，GPU **112 张**截图与请求／提交帧身份一致；有限查看侧蹲／侧跑各一帧，不代表人工整动作验收。
- 同场景原版基线通过，三种模式各 28 段方向的足底原阈值对照全部通过。相关矩阵仍明确 `completeMatrix=false`、`executedAllScenarios=false`，还有 28 个缺失源，因此总报告与完整包装保留失败状态；没有减少门槛或伪造完整矩阵通过。
- 独立 `derived-delivery-audit.json` 验证十个采样交付和十八个 offset 交付；offset 的 Root、骨盆旋转及实际源脚 FK 保持，**1,706 个**包载荷摘要一致。它不替代最终蒙皮检查。

- 保留资源的 Air 场景 **11/11** 通过，包括三 Hz 单线程／并行、组合姿态／所有权／Falling Lean 与两种 GPU 运行，22 张截图保留。
- Low Mantle 静态检查 **9/11** 通过。Root 开启案例的蒙皮峰值 4.843 cm，原版 GPU 对照 2.315 cm，均超过原 2 cm 阈值。原活动包复跑相同 Root 开启案例，360 帧全部骨骼世界位置与新包差异 **0**，蒙皮峰值同为 4.843 cm；证明该失败没有由新增派生资源引入。对照文件 `artifacts/kimodo_als/derived-20261010-root-on-baseline.json` 与独立日志保留，摘要与比较收录到交付审计。
- 11 个高度／移动平台／取消检查全部运行：十个正常案例的运行、接触及原 2 cm 蒙皮检查通过；关闭手锁的负对照仍越过原蒙皮阈值。负对照和完整脚本的失败状态保留。

最终 `runtime/runtime-result.json` 为 `runtime_failed`，完成报告 `resumed-result.json` 为 `pipeline_complete_rejected`：六项相关矩阵没有执行全部 71 场景，源闭包仍缺 28 身份，Mantle 失败和负对照仍保留。不得把新增资源交付通过升级为完整生产包验收。

## 证据与验收边界

作者报告与 receipt：`artifacts/kimodo_als/derived-20261010-v2/batch-result.json`、`receipts.json`。组合源和转换位于其 `full-pipeline/`，最终回读见 `conversion/final-fbx-progress.json`，实际运行位于 `runtime/`。完整首轮日志、继续执行日志分别为 `derived-20261010-v2.log`、`derived-20261010-finish-v2.log`，旧失败没有覆盖。

包保持 `diagnostic`、`productionReady=false`、`activated=false`。静态采样只证明生成来源、精确截取／冻结及转换完整性，不宣称源循环通过、动作风格、全蒙皮或人工验收。还有 15 项缺失语义；没有将新候选设为默认，也没有执行全量 71 项、长期或性能验收。
