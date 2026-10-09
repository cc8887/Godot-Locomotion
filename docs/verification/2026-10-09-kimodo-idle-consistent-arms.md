# 2026-10-09 Kimodo Walk 手臂与 Idle 一致性

用户要求调整提示词、尝试约束生成手部动作，减少与 Idle 的差异。本轮在主检出执行；未修改活动指针、原版资产、道具或运行物理阈值。最终输出目录为 `artifacts/kimodo_als/idle-arms-20261009-v2/`，全流程目标为 `assets/generated/als_nonprop_20261009_idle_arms_v2/`。

## 定位与生成对照

旧 `native_gait_reference` 用 `world × inverse(ALS bind)` 投影到 SOMA，绑定基准为 A-Pose，而 SOMA 使用 T 中性骨架。输出重定向早已使用 T-neutral-aligned 基准，两端不互逆；原版低垂手臂被抬高，作为强条件又反向限制生成。最终限定肩/上臂/前臂/手输入使用输出同一个 `aligned_neutral`，独立正向重定向回读复现原上身世界旋转。根位置、下肢参考逐数对照保持，脚趾锚点与固定骨长规则不变。

实测前进参考的两侧上臂中位值约66°，修正后约23°；本批复用的生成 Idle 分别约16.27°和13.21°。固定/Idle/旧参考均来自最近成功源的明确回执，未取旧 worktree 制品。

同条件前进生成采用 RP v1.1、30 FPS、seed 20261009、100 steps、相同持续时间、根路径及 post-processing。A为最近批次真实生成，B/C/D是本轮新推理；B来自v1，C已按最终仅手臂策略重跑，D来自完整v2生产：

| 变体 | 左/右上臂中位角 | Idle 相对作者检查 |
| --- | --- | --- |
| A 旧提示词＋旧条件 | 65.45° / 66.02° | 失败 |
| B 新提示词＋旧条件 | 65.35° / 66.02° | 失败 |
| C 旧提示词＋修正条件 | 21.11° / 21.82° | 通过 |
| D 新提示词＋修正条件 | 21.29° / 21.84° | 通过 |

证据：[最终对照结果](../../artifacts/kimodo_als/idle-arms-20261009-v2/prompt-reference-ablation-final.json)、[B的推理日志](../../artifacts/kimodo_als/idle-arms-20261009-v1/prompt-reference-ablation.log)及[最终C的推理日志](../../artifacts/kimodo_als/idle-arms-20261009-v2/prompt-reference-ablation.log)。这支持姿态条件是主要原因；不能声称只改提示词改善了结果。新提示词保留完整周期和方向描述，追加肩膀放松、手臂贴近身体、轻微前后摆动。

![同原点源FK对照](../../artifacts/kimodo_als/idle-arms-20261009-v2/idle-arms-source-abcd.png)

图中所有栏保持相同源原点、正交正面投影及坐标范围，源帧42；不是最终蒙皮截图。

## 本轮完整生产

生产使用D“新提示词＋修正条件”：重新推理前进和左前两个 Walk，左后反向镜像、右侧镜像沿用原有程序化规则。43个其他成功生成输入按最近批次回执显式绑定复用；所有派生节点、作者门禁、目标转换和运行验证重新执行。不能把复用部分称为全套重新生成，也不能把新参考直接交付为生成资产。

v1曾把中性基准修正扩展到下肢，导致复用的蹲走生成与新接触参考不一致。保留失败及中断记录，停止该版本后续运行；v2把修正限定手臂并重新执行完整管线，不将v1标作闭环完成。见 [中断原因](../../artifacts/kimodo_als/idle-arms-20261009-v1/interrupted-v1.json)。

新增 `upper_body.py` 对照当前生成的 Idle，限制上臂中位/峰值抬高和手部侧向伸出量；输出轨道不被该检查修改。镜像重测，发布验证必要报告/运动身份，正式脚底/支撑/原生对照门禁保持原标准。完整设计见 [合同](../architecture/kimodo-idle-consistent-arms.md)。

本轮原生文本编码沿用上一批同 checkpoint 的已验证文件映射 BF16/PEFT 路径；新提示词实际重新编码，旧确切提示词摘要校验后复用。没有改变模型、远程编码器或原始权重，见 [编码日志](../../artifacts/kimodo_als/idle-arms-20261009-v1/encode-prompts.log)。

最终实现完整 Python 套件 **160项通过**；再加入下肢保持对照后，3项原版映射回归通过。缺失/失败/摘要变化/另一运动的上身报告均被发布验证拒绝。见 [完整套件](../../artifacts/kimodo_als/idle-arms-20261009-v2/python-regression.log)和[下肢不变回归](../../artifacts/kimodo_als/idle-arms-20261009-v2/unchanged-leg-reference-regression.log)。

源周期修复及派生后，前进上臂中位 **21.74° / 21.99°**；左前 **24.83° / 22.32°**。前进和四个侧向Walk全部通过实际Idle相对检查，10个V4/Refactored目标Walk通过原有目标门禁。新增检查没有重写输出姿态或放宽脚底阈值。

## 转换与普通移动截图

本轮实际交付/转换 **123/183** 非道具身份，源失败/缺失 **60**，70个道具逐字保持。上一批诊断转换的两条纯侧蹲走在本轮普通DAG因缺少对应不可变原版参考阻断；未用参考姿态直接交付或跳过检查补齐数量。Run/Sprint等母动作的既有源质量失败继续保留。

最终 **123/123 FBX** 全物理骨骼逐采样回读通过，4586个采样键、311848个骨骼样本，位置误差0cm、旋转最大2.442e−13°。34份声明根运动曲线、50份额外提取轨迹、Root/Pelvis分解和目标相位/IK均重新执行。1493个包载荷摘要通过，10份上身回执进入包载荷；默认指针摘要保持 `21d5dc2c1c87143ae6ca86dbbc0795677fd71a32a15425143dfdd46194b039ec`。见 [最终载荷审计](../../artifacts/kimodo_als/idle-arms-20261009-v2/final-payload-audit.json)。

独立读取最终runtime raw的上臂FK：10个Walk全部与交付源倾角一致，最大中位值24.83°；转换未再次抬高手臂。见 [目标上身回读](../../artifacts/kimodo_als/idle-arms-20261009-v2/upper-body-target-readback.json)。该数值检查不覆盖运行Layer叠加，因此仍使用普通Demo最终蒙皮截图复核。

Godot优化构建、实际导入、P2B和六域请求通过。前进/左前原版与新包受控起停共 **4/4 进程**、**2/2 脚底原版对照**通过，保存152张实际截图。覆盖起始tick1–5、按键前29/后31、稳态、松键209/210/211及后续Idle；首帧是正常隐藏冷姿态，不用于可见动作验收。只覆盖scheduled tick210松键，没有测试全部支撑窗口。见 [起停汇总](../../artifacts/kimodo_als/idle-arms-20261009-v2/walk-release-screenshots/summary.json)。

| 方向 | 起步脚底滑移中位 cm/s | 稳态 | 松键 | Idle |
| --- | --- | --- | --- | --- |
| 前进 | 13.207 | 5.049 | 0.00037 | 0.00027 |
| 左前 | 35.186 | 6.325 | 0 | 0 |

左前起步通过既有原版相对上限73.620cm/s，不是低于固定25。没有降低接触或滑移断言。

![前进同帧旧包/新包](../../artifacts/kimodo_als/idle-arms-20261009-v2/forward-old-new-arms.png)

![左前同帧旧包/新包](../../artifacts/kimodo_als/idle-arms-20261009-v2/left-forward-old-new-arms.png)

实际检查两方向启动/稳态/松键后/Idle：旧包在稳态和刚松键时明显展臂，新包保持手臂低垂、轻微摆动，接近Idle；这些采样帧中的展臂失败已消除。拼图使用同一个视口裁剪区域，不按人物姿态重新居中；每张原始PNG与帧/摘要绑定，见 [拼图溯源](../../artifacts/kimodo_als/idle-arms-20261009-v2/runtime-comparison-provenance.json)。这不代表全方向、全动作的人工验收。

## 完整矩阵与最终边界

完整71场景全部执行，**70/71通过**，唯一失败为 `recovery-auto-roll`。30/60/120Hz单线程与并行摘要一致；单线程、并行、正常渲染三种方向模式各28段原版最终脚底对照通过。矩阵保存246张PNG，其中正常绘制方向112张，截图身份/采样帧检查通过；载荷无变更，动画设置未改变。见 [矩阵汇总](../../artifacts/kimodo_als/idle-arms-20261009-v2/full-pipeline/runtime/e2e-matrix/summary.json)、[逐场景及截图身份](../../artifacts/kimodo_als/idle-arms-20261009-v2/full-pipeline/runtime/e2e-matrix/results.json)与[方向脚底质量](../../artifacts/kimodo_als/idle-arms-20261009-v2/full-pipeline/runtime/e2e-matrix/directional-quality.json)。另目验本批左右Walk的正常绘制sample120，手臂保持低垂；未把这些离散帧扩展为全时段人工验收。

自动翻滚在第二次恢复 `cycle=1` 检测接地状态与退出场景不符，位置为 `(10.28468, 0.93768036, 3.634816)`；真实引擎进程退出1。保留 [失败日志](../../artifacts/kimodo_als/idle-arms-20261009-v2/full-pipeline/runtime/e2e-matrix/recovery-auto-roll.log)，本轮未修改恢复物理或放宽断言。

整链完成且明确拒绝发布：`pipeline_complete_rejected`、`productionReady=false`、`activated=false`。实际包只替换123个非道具身份，剩余60个在诊断矩阵中回退原版，所以运行检查不能认证全新Run/Sprint/蹲走等资产；缺失清单、源失败及转换结果见 [完整管线结果](../../artifacts/kimodo_als/idle-arms-20261009-v2/full-pipeline/result.json)。目标包最终SHA256为 `2348d56fa56f97d4008a4fd5f3adb5984a5ccaca84a2eb34a43938990b60c741`；活动指针仍指向 `als_nonprop_20261007_v8`，不是本批。新Walk改善已验证，但完整替换、自动恢复、更多种子与全动作观感仍需后续验收。
