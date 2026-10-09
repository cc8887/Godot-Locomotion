# 2026-10-09 Kimodo 侧向步态与 Sprint 加速修订

已修改生产规则和执行节点，并实际运行新生成／修复／派生流程。最终配置的 13 个方向母动作中 **8 个通过源资产作者检查，5 个仍失败**。没有发布或激活新 Godot 包；本记录不宣称目标骨架或普通 Demo E2E 通过。

设计与原生用途见 [生成合同](../architecture/kimodo-directional-gait-generation.md)。修改前检查并保留主目录已有修改；没有提交、推送或创建 worktree，也没有覆盖既有运行资产。

## 原因与修订

旧侧移路径把 Left_Forward／Left_Backward 当作对角移动；原版两者都是纯侧移，区别是髋部姿态。旧流程从固定百分比裁剪并补接缝，没有证明完整交替周期。Sprint_Acceleration 被归类为起跑，使用加速路径及站姿开头；原版绑定实际开启循环，并以持续发力姿态参与 Movement 组。新规则使用纯侧向路径、稳态提示词、周期参考和移动中的 Sprint 条件，取消上述 Idle 起跑语义。

生成实验还确认：约束帧本身能满足 FK 目标，但稀疏帧之间会发生支撑漂移；只有两帧脚锚并不足以保证整个支撑区间。已加入区间内多点、连续三周期的四相姿态、真实末帧相位与手部旋转条件，并将周期时长和步幅共同校准。

另外修复了 NPZ 读写后重复 SO(3) 投影导致验收摘要变化的链路问题。新回归证明保存后的已验收母动作仍能派生；实际修改的母动作仍被拒绝。

## 最终实测

证据根目录为 `artifacts/tests/2026-10-08-directional-gait-regeneration-v1`。目录名称保留本次试验的开始日期，最终收尾为 10 月 9 日。

最终 DAG 和实际输入绑定：`final-run-graph-v2.json`、`final-bindings-v2.json`。GPU 使用本地 Kimodo-SOMA-RP-v1.1、30 FPS、seed 42、100 diffusion steps。文本 embedding 来自原生 LLM2Vec 编码，精确提示词、权重、输入文件、模型实现及输出摘要均保存在执行 receipt；未切换其他编码器或模型。

| 母动作 | 最终源作者检查 | 方式／失败 |
| --- | --- | --- |
| Walk_Left_Forward | 通过 | 真实条件生成与有界修复 |
| Walk_Right_Forward | 通过 | 左侧镜像，重新测相位并修复派生周期 |
| Walk_Left_Backward | 通过 | 左前母动作反向播放＋镜像，重测支撑 |
| Walk_Right_Backward | 通过 | 从已验收左后母动作派生 |
| Crouch_Walk_Left_Forward | 通过 | 真实条件生成与有界修复 |
| Crouch_Walk_Right_Forward | 通过 | 程序化镜像及独立检查 |
| Crouch_Walk_Left_Backward | 通过 | 同周期反向镜像及独立检查 |
| Crouch_Walk_Right_Backward | 通过 | 从已验收左后母动作派生 |
| Run_Left_Forward／Right_Forward | 失败／依赖阻断 | 周期修复后的接缝或滑步检查仍失败 |
| Run_Left_Backward／Right_Backward | 失败／依赖阻断 | 支撑滑移及接缝仍失败；原生周期与前向变体不同 |
| Sprint_Acceleration | 失败 | 仍存在周期接缝问题，不能作为循环母动作发布 |

汇总见 [final-authoring-summary.json](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/final-authoring-summary.json)，完整输出路径、文件摘要及 receipt 见 [最终运行结果](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/final-profile-results-v2.json) 和 [执行日志](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/final-profile-v2.log)。每个通过的节点均保留 `motion.npz`、`gait-validation.json`、`authoring-trajectory.json`；派生修复也保留修复前后检查。

8 个通过的源母动作已按语义名称整理到 [accepted-masters 清单](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/accepted-masters/manifest.json)。整理时逐文件验证原执行 receipt 的 SHA256，保存的是精确字节副本；清单明确标记 `productionReady=false`，供目标转换与复验使用。

失败探索没有覆盖最终日志：发现候选、步幅回灌、原版姿态投影、多个 seed、完整支撑窗口、手部约束和修复搜索分别保存在独立目录。`final-candidate-search-results.json` 记录最后三类失败母动作的有界搜索仍未通过，未以最少失败数替代通过标准。

最后另做一次约束密度诊断：同一模型、提示词、seed 与迭代数，把 19 帧全身／手部条件预算集中到一个周期及两个重复边界，脚锚仍使用独立检测的支撑区间。三条母动作重新生成，并分别用 2／3／4／5 阶周期拟合检查，**0/3 通过**；该诊断未替代最终规则。见 [密集周期结果](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/dense-cycle-probe-results.json) 和 [独立日志](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/dense-cycle-probe.log)。这说明仅增加姿态采样密度还不足以关闭当前滑移与速度接缝。

前一组有界修复搜索中，左前跑的周期速度接缝约 1.172 m/s、右脚近地滑移中位数约 0.445 m/s；左后跑分别约 0.898 m/s、2.451 m/s；Sprint 加速分别约 2.517 m/s、左脚 0.279 m/s。现有上限分别为 0.75 m/s 和 0.25 m/s。逐帧复核发现，后向侧跑有摆动脚低于近地窗口却快速移动的区间；接缝的最大差异集中在小腿或摆动脚。失败并非仅由首尾姿态值不相等造成，不能只把末帧复制成首帧来发布。

## 原点与视觉证据

输出了固定原点、同一正交投影和 cm 单位的 0、1/4、1/2、3/4、末帧对照，共 40 个源 FK 采样姿态，以及 8 个循环的动画预览。灰色为原版 ALS 骨架，彩色为新 SOMA 源骨架；没有逐帧跟随或各栏重新居中。

- [站立侧向走路对照](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/source-fk-previews/walk-fixed-origin.png)
- [蹲姿侧向走路对照](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/source-fk-previews/crouch-fixed-origin.png)
- [8 个循环动画预览](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/source-fk-previews/accepted-loops-fixed-origin.gif)
- [预览来源及摘要](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/source-fk-previews/preview-metadata.json)

这些是作者源 FK 预览，已标注不是 Godot 截图。它们展示接缝和相位，并暴露与原版的姿态／骨架比例差异；不代表目标蒙皮或人工观感验收。

## 回归与剩余工作

Python 全量相关测试 **131 项通过**，日志 `tests-full-v3.log`。覆盖纯侧移语义、Sprint 循环与 5 m/s 标定、三周期数值条件、手部旋转、目标周期重定时、世界脚锚、原文件哈希／安全路径、磁盘读写后派生、修改后拒绝、镜像重测以及目标几何协议。原三个前进试点和新增侧移／Sprint 共 **16 个原版身份通过目标几何正向检查**，完整结果见 [native-positive-controls.json](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/native-positive-controls.json)；原有损坏数据负向检查继续保留。

本批没有改 C# 运行逻辑，也没有运行 Godot 构建、目标重定向、最终 FBX 回读或普通 Demo 移动矩阵。现有活动包未获新质量认证。后续先解决四个侧跑及 Sprint 加速的剩余失败，完整母动作集通过后再重出依赖、目标资产与每资产根曲线，执行原版对照和普通 Demo 移动／松键／截图矩阵；当前不能宣称整套资源已替换完成。
