# Land／Roll 提示词修订与真实候选验证

## 范围与当前状态

按用户要求，先通过 prompt 描述落地蹲伏和翻滚表演，不新增固定屈膝角度、骨盆高度或特定翻滚姿势的文件验收门禁。原有转换、Root/Pelvis 所有权、最终 FBX 回读、导入和实际运行断言保持。

提示词调整和六轮真实候选的完整生成、转换、最终 FBX 回读、Godot 导入与相关运行已完成。Land 的缓冲描述已加强；Roll 最终配置保留 v4 能产生完整贴地翻滚的文案。下文保留首轮旧夹具的失败和当时未接入决定；后续已证实固定两秒检查是夹具前置条件误判，修正后相关 22/22 与额外三种 Hz／模式通过，默认现已选择 `als_nonprop_20261010_landing_prompt_combined_v4` 诊断包。见[夹具根因、负对照及最终接入证据](2026-10-10-ragdoll-recovery-fixture.md)。

## 实现与提示词

`tools/kimodo_pipeline/graph.py` 的通用生成节点支持规则文件 `motionPromptOverrides`，按 `semanticName` 查找。优先级为分支显式 prompt、规则覆盖、合同 `promptDraft`。没有改写原合同或复制运动算法到 Godot／Core。

`tools/kimodo_pipeline/non_prop_rules.json` 当前文案为：

- Light：`A person lands softly on both feet, bending both knees and lowering the hips into a shallow squat to cushion the landing. Both feet stay planted as the person smoothly rises into a balanced standing stance.`
- Heavy：`A person lands on both feet in a deep squat, bending the knees fully and dropping the hips low between the feet to absorb a heavy impact. The feet stay flat on the floor as the person pauses briefly in the deep crouch, then rises smoothly into a balanced standing stance.`
- Roll：`A person performs one forward shoulder roll along the ground. Starting in a low crouch, the person tucks the head and knees, rolls over one shoulder and across the upper back, then brings both feet underneath the body and rises into a balanced standing stance.`

Light／Heavy 及各自 Additive 沿用同源制作；V4 和 Refactored 对应身份均交付。候选组合包仅替换 8 个 Land 身份和 2 个 Roll 身份；默认运行包未替换。骨架、站姿基底、条件、重定向、Root 规则、原动画时域、Root Motion 默认关闭配置和自动 Roll／Ragdoll 速度规则保持。

## 真实生成对照

| 轮次 | 本轮真实生成 | seed | 实际观察／结果 |
| --- | --- | --- | --- |
| v1 | Light、Heavy、Roll；交付 10 身份 | 20261009 | Land 出现更多屈膝；Roll 有完整翻转，但开头明显腾空。相关运行 7/7、Air 11/11；未选默认。 |
| v2 | Light、Heavy、Roll；交付 10 身份 | 20261009 | Heavy 改为低髋深蹲、短暂停顿；较长 Roll 文案未产生完整翻滚。相关运行 7/7、Air 11/11；未选默认。 |
| v3 | 简化 Roll；交付 2 身份 | 20261009 | 较早贴地，但主要是蹲伏移动，未完成翻滚。相关运行 4/4；未选默认。 |
| v4 | 复用 v1 Roll 文案、重新采样；交付 2 身份 | 42 | 3／5 m 实际画面可贴地翻滚；相关运行仅 3/4，自动 Roll→Ragdoll 恢复第二循环失败，未选默认。 |
| v5 | 同一 Roll 文案、重新采样；交付 2 身份 | 43 | 贴地翻滚画面改善；恢复第二循环仍失败，Root 关闭及单线程对照也失败，未选默认。 |
| v6 | 明确起始低蹲、双手触地；交付 2 身份 | 42 | 改善贴地，但主要蹲伏移动、未完成翻滚；相关运行 3/4，同一恢复第二循环失败，未选默认。 |

共 10 次真实 diffusion；依赖站姿和 rig 采用摘要核对后的明确复用，不将它们计作本轮新生成。每轮均有独立 authoring、转换包和组合包，未用旧 worktree 资产覆盖主目录。

前三轮与旧真实生成的参数差异只有 prompt，实际条件 JSON 字节摘要一致。v4／v5／v6 相对旧源另改变候选 seed，仍复用完全相同的条件、模型、30 FPS、3 秒时长、100 steps 和基底；不能把它们表述为严格的“只改 prompt”因果对照。v6 与同 seed 的 v4 相比则只有 prompt 变化，实际条件摘要相同。本机原生 LLM2Vec／BF16 checkpoint、PEFT 合并一致性和冻结文本嵌入身份均验证；未换编码器或补姿态条件。

## 已执行验证与失败

生成图／批处理相关 Python 回归 27/27。每轮源资产继续执行纯脚本重定向、Root/Pelvis 分解、独立曲线与最终 FBX 回读；六轮组合包各 123 个 FBX 全键回读通过。

v2 完整相关检查含优化构建、Godot 导入、P2B、六域 raw 请求、7 个相关游戏案例、Air 11 项、实际 2 m 移动／静止、3／5／8 m 移动和 3 m 并行观察。6 次高度观察共 1,214 帧／168 张 GPU 截图；相对 v1 的触地前胶囊位置和速度差为 0。3 m 单／并行 198 帧、17 个观测骨骼位置和四元数分量差为 0。

v3–v6 相对 v2 仅替换 Roll，重新执行完整转换／导入、根运动开关、自动进入、普通入口和 3／5 m 单线程、3 m 并行观察；Air／Land 既有运行证据由保持检查限定复用，不标成新跑的 11 项。v6 三次 GPU 观察共 608 帧／84 张截图，单并行 198 帧、17 个观测骨骼位置／四元数分量差为 0，触地前胶囊物理差为 0。最终 v6 保持检查通过：1,536 个载荷摘要、相对 v2 的 121 个其他运行源、70 个道具和 123 个最终 FBX。

v4 的 `recovery-auto-roll` 在 cycle 1 报 `Ragdoll ground state differs from exit scenario`。相同参数两次重跑均在同一位置失败，v2 对照同参数通过。该失败由新 Roll 候选引入，保留原日志及失败矩阵，没有修改接地断言、延长夹具等待或记为偶发通过。v5 的同一失败亦在 Root 关闭和单线程对照复现，证明该现象不能由开启 Root 或并行求值单独解释。

v6 同一断言在 cycle 1、位置 `(11.017781, 0.6305151, 3.4389915)` 失败；普通入口及 Root 开／关案例通过，相关 4 项合计 3/4。画面主要蹲伏移动，未完成翻滚，所以未保留该文案为最终配置。此处动作表现是本次人工观察，不是新增文件风格门禁；恢复失败的具体物理原因尚未确认，不能声称已修复。

收尾核对默认指针字节保持，重新收敛资源扫描、将 Viewer 最新入口恢复到原默认包，并清除 ALS 环境覆盖后重新启动普通 `als_demo.tscn` 180 ticks，通过且日志确认仍选择原 Air crouch 包。未提交、未推送。

部分 e2e 包装器同时保留退出码 1 和 `Directional coverage not selected`；这与 v4 的实际失败分别记录。完整方向矩阵未执行、60 个非道具源缺口仍在，整体运行／正式生产门禁保持未通过。未新增风格门禁，完整蒙皮接触／穿模、人工风格、长期和性能验收仍开放。

## 证据入口

- 各轮目录：`artifacts/kimodo_als/landing-prompt-20261010-v1` 至 `landing-prompt-20261010-v6`。
- 每轮 `graph.json`、`selection.json`、`receipts.json`、`actual-conditions-comparison.json`：实际文字、参数、基底复用与条件对照。
- 每轮 `combined/conversion/pipeline-result.json` 和 `final-fbx-progress.json`：最终回读结果。
- v2 `combined/delivery-audit.json`：1,536 个载荷摘要、113 个其他运行源与 70 个道具保持；`combined/runtime/result.json`、`runtime-observations.json`：实际范围。
- v4 `combined/runtime/e2e-matrix/recovery-auto-roll.log`、`recovery-failure-audit/results.json`、`candidate-decision.json`：失败、复现／旧包对照和未接入决定。
- v4 `roll-before-after.gif`：旧资产与该候选同帧的实际 GPU 对照；仅说明视觉变化，该候选仍被恢复失败阻挡。
- v5 `recovery-root-policy-audit/results.json`：Root 关闭／单线程仍失败的对照。
- v6 `combined/runtime/result.json`、`combined/delivery-audit.json`、`runtime-observations.json`、`candidate-decision.json`：最终交付检查、实际 3/4、物理／并行保持和未接入决定；`roll-v6-whole-review.png` 为实际画面。
- v6 `preserved-default-resource-scan.json`、`preserved-default-restart.log`：候选验证结束后的原默认正常启动与扫描范围。
- [修复前高度／路由／根因诊断](2026-10-10-kimodo-drop-height-diagnosis.md)。原记录的后续约束／风格门禁建议已由本轮用户要求取代。
