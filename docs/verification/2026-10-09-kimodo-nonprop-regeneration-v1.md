# 2026-10-09 非道具资源重新生成 v1

已执行一次全范围源资产重生成和两个约束输入分支补跑。最终有 **45 个当前候选身份、125/183 个源交付身份、7 条循环步态源检查通过**，其余 **58 个资产身份被阻断**。整批质量仍不完整，没有产出或激活新的 Godot 运行包。本记录不将源 BVH/NPZ 当作目标骨架、Root 分解或 Godot E2E 交付。

主目录已有修改保持；没有 worktree、提交或推送。道具动画不在本轮替换范围。

## 执行与约束

证据目录为 [nonprop-regeneration-20261009-v1](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/)。本地 Kimodo-SOMA-RP-v1.1、30 FPS、100 diffusion steps、seed **20261009**；没有绑定历史动画作为本轮候选。47 份真实推理输出包含补跑时再生成的两条基础姿态，合并后保留 45 个当前候选身份。

41 条实际提示词由原生 LLM2Vec 编码；相同文本 embedding 的复用先校验 checkpoint 身份、native PEFT 等价证据和文件哈希，复用的是编码而非运动。编码日志、模型/适配器摘要及每条提示词在 `encoder.log`、`text-embeddings/manifest.json` 与逐节点 receipt 中。

本轮独立规则快照 [rules.json](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/rules.json) 在侧移配置基础上加入前后走跑、纯侧蹲走和 Sprint 周期检查。LF/LB 的数值路径仍是纯左移，髋部朝向按原版合同区分；提示词强调持续行进与完整前向/后向交替步幅，未把 LF 当作 Idle 起跑。RF/RB 由相应左侧源程序派生，同周期 Walk 后向变体继续反向镜像。原版姿态仅为约束参考，不作为新动画交付。

主轮命令：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe -u tools/produce_als_resources.py run `
  --scope non-prop --keep-going `
  --rules artifacts/kimodo_als/nonprop-regeneration-20261009-v1/rules.json `
  --output artifacts/kimodo_als/nonprop-regeneration-20261009-v1 `
  --kimodo-repo G:/Github/kimodo
```

`--keep-going` 尝试独立分支，失败依赖仍阻断下游。保存每个节点的失败现场和阻断依赖；不会用默认站姿填空。当前 CLI 对未完整通过的批次返回退出码 1；实际缺少 rig 的负向调用退出 1、0 个交付身份、1 个失败和5 个阻断节点，见 `cli-negative-check.log`。主轮与补跑是在该退出码修正前执行，结果文件明确记录 `authoring_rejected`，不能据它们当时的进程退出 0 判断通过。

## 输入问题补跑

主轮对 `Crouch_Walk_Left` 请求了不存在于 Refactored 原版索引的 V4 独有来源；`Sprint` 原版参考也没有检出完整交替周期。改为新生成的发现候选作数值参考，在独立 `input-recovery` 目录补跑，实际节点参数保存在 [input-recovery-graph.json](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/input-recovery-graph.json)。

补跑入口为本目录 `recover_inputs.py`，逐文件校验本轮完成的源输入；本次实际重新生成 Stand/Crouch、纯侧蹲走发现/条件候选与 Sprint 发现候选。纯侧蹲走及其镜像通过。Sprint 发现候选仍没有可检测的完整交替周期，条件生成和交付被拒绝。该补跑没有降低脚滑、接缝或修正上限。

原始报告保留在 `batch-result.json`，补跑保留在 `input-recovery-result.json`；最终清单见 [generation-summary.json](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/generation-summary.json)、[authoring-bundle.json](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/authoring-bundle.json) 和 `combined-receipts.json`。

## 源检查结果

| 资源 | 本轮结果 |
| --- | --- |
| Walk_Forward | 源周期通过 |
| Walk_Left_Forward / Left_Backward / Right_Forward / Right_Backward | 生成、程序派生及源周期检查通过 |
| Crouch_Walk_Left / Right | 补跑及镜像源周期检查通过 |
| Run_Forward / Backward / Left_Forward / Left_Backward | 修复后独立作者门禁拒绝，右侧及相关依赖阻断 |
| Walk_Backward | 修复后独立作者门禁拒绝 |
| Crouch_Walk_Forward / Backward / Left_Forward | 脚部修正超过 0.15 m，分别约 0.196 / 0.207 / 0.220 m，拒绝；其变体阻断 |
| Sprint | 参考及新发现候选均未检出完整交替周期，拒绝 |
| Sprint_Acceleration、Jump_Run 及其相关派生 | 依赖 Run_Forward 未通过，被阻断 |

125 个身份只是源作者阶段 NPZ/BVH/metadata 交付；这些身份中的非步态动作也尚无目标蒙皮或普通 Demo 质量认证。七条通过步态各保留源 `gait-validation.json` 与移除的 `authoring-trajectory.json`，不是最终目标空间 Distance Matching 曲线。

最终逐版本审计 **292 份节点 receipt、814 个输出文件**：检查 SHA256、依赖身份与依赖输出摘要；全部真实生成 receipt 的 seed 为本轮 seed，没有 bound animation 输入；交付身份均在非道具目标范围内。

## Viewer 与截图

Viewer 最新合并批次为 **10-09 非道具重生成 v1（部分源通过）**，ID `nonprop-regeneration-20261009-v1-e57f09f835b2`，共 114 个候选/母动作预览条目。增量登记保持同一批次 ID，历史批次保留；条件生成成功时优先显示实际条件候选，不让其 discovery 参考覆盖该阶段。

真实浏览器按首帧按钮定位 0 秒，固定原点、cm 和同一相机。核对新 Walk、Walk_LF 的源通过状态、Run_LF 的空母动作阶段，以及新/旧 Walk_LF 母动作两栏：

- [前进 Walk 首帧](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/screenshots/walk-first-frame.jpg)
- [Walk_LF 阶段首帧](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/screenshots/walk-lf-first-frame.jpg)
- [Run_LF 候选与被阻断阶段](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/screenshots/run-lf-first-frame.jpg)
- [新旧批次 Walk_LF 母动作首帧](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/screenshots/new-vs-previous-walk-lf-first-frame.jpg)，相应 DOM 来源与 0 秒状态在同名 `.dom.txt`。

截图是 Pipeline 源 FK/历史独立对照，不是新包 Godot 运行截图。新源仍属 SOMA；生成候选显示 Root 是静止显示容器，不能据此断言已完成 pelvis 水平分解。本批目标重定向、Root 分解、ALS 原地与最终 FBX 均未产出，链路明确留空，没有拿旧批次目标制品填入。相机时间按时长比例同步，不代表支撑事件已对齐，也不以一张首帧截图证明循环观感。

## 回归与边界

140 项 Python 管线回归通过，见 `pipeline-tests-final.log`；最后 Viewer 候选优先级改动另复验相关 6 项通过。覆盖独立分支继续执行、质量失败阻断、批次锁、同批增量登记、条件候选优先级、输出哈希、周期、镜像和 Root 算法既有回归。

活动指针逐字未变，仍为 `als_nonprop_20261007_v8`；活动 pack SHA256 为 `023189034086a952ef116044c218e3e45475984f17e65ceda01d6be20f06485d`。本轮没有 UE 导入/重导，没有目标骨架转换、目标 Root 分解、最终 FBX、目标每资产根曲线或 Godot E2E。下一步需关闭仍失败的移动母动作，再重出其依赖与目标阶段；本批不能作为完整 ALS 替换包启用。
