# Kimodo 前进步态约束与闭环验证

**结果：新生成 Walk／Run／Crouch 三个候选均被作者阶段检查拒绝，0/3 母动作通过。没有新资源包达到可交付状态。** 已实现生成约束、作者门禁、目标骨架协议检查和普通起停验证；不能将这些代码／负向对照通过表述为新动画通过。

[本批总报告](../../artifacts/tests/2026-10-08-kimodo-gait-closure-v1/closure-summary.json) 记录实际执行范围、失败门禁、实现哈希和活动指针状态。默认包仍为已知有缺陷的 v8，根运动默认 OFF；未激活新包、未提交／推送。

## 实现内容

- `gait_constraints.py` 从真实 Kimodo 参考候选的完整同脚周期构造 root2d、周期全身关键帧和左右支撑脚锚。两次脚锚条件之间身体沿路径前进，脚保持世界位置／方向；通过两段腿 IK 验证可达性。输入参考明确标记未通过 master 验收，不将它当成合格资产。
- `gait.py` 从独立 FK 足部几何检测支撑，按事件选周期，检查接缝姿态／速度、近地滑移、穿地、相位和相邻周期稳定性，保存实际去除的行进轨迹。失败 receipt 阻断下游。SOMA heel 缺少直接标记时使用明确标注的几何代理，不能代替目标蒙皮。
- `gait_closure_rules.json` 继承当前非道具规则，仅为三个前进母动作启用 `gait_cycle` 和条件生成。执行器把通过的作者 receipt、轨迹和交付 NPZ 哈希绑定到资产；原有其他方向和 Pose／镜像流程尚未全部升级到新语义处理。
- `target_gait.py` 用原始资产与原图规则独立校准目标 FK 检查；发布时校验上游 receipt，检查通过后才编译支撑曲线与事件。Refactored 部分支撑为 ±0.25，V4 为 ±0.2，全支撑为 ±1；Crouch 不新增原本不存在的 FootPlanted。非试点的旧高度包络只修正反号／半幅，不声称已获得独立步态验收。
- `verify-als-gait-closure.py` 沿普通角色输入／物理／动画／显示提交路径，按实际支撑窗口或指定时刻松键，记录最终蒙皮、胶囊、根运动、可见性与截图身份。它补充原有 71 项，未替代完整矩阵。

## 实际生成与作者门禁

使用 Kimodo-SOMA-RP-v1.1、seed 42、100 steps，分别生成 5 秒／150 帧 Walk、Run、Crouch，共 450 帧。真实条件文件和输出位于 [refinement](../../artifacts/tests/2026-10-08-kimodo-gait-closure-v1/refinement/)。生成过程成功结束，随后用同一批输出作为显式绑定执行实际 DAG 检查，三个 `gait_cycle` 节点均失败并保留 `.building-*/gait-validation.json`。

| 新候选 | 实测结果 | 阻断原因 |
| --- | --- | --- |
| Walk Forward | 检测到四个周期，其中 70–105 帧单周期通过 | 相邻周期仍有速度／角速度接缝或接触不连续，未取得两段相邻合格周期 |
| Run Forward | 当前几何检查仅识别到一个完整有效接触周期 | 支撑期滑移与接缝失败，无法提供稳定完整周期 |
| Crouch Walk Forward | 检测到五个周期区间，含一个异常短周期 | 滑移、接缝、足部代理穿地或相位失败，无合格母周期 |

完整数值见 [生成后检查汇总](../../artifacts/tests/2026-10-08-kimodo-gait-closure-v1/refinement-summary.json) 和各动作 `*-refinement-audit.json`／`*-author-gate.json`。另以官方 Foot/ToeBase 关节做补充诊断，仍有滑移／接缝失败，未用更换测量点把结果改为通过。

输入条件的一致性已验证：三参考的脚锚 FK 误差约 2e-15 m，实际均无需降低髋高，且满足 sparse key 数量限制。但“输入约束可达”不等于“整个生成序列满足质量要求”；本批实际结果证明仍需进一步改善支撑期间约束和周期连续性。

## 目标骨架与普通 Godot 对照

目标 FK 独立协议检查：原版三前进 **3/3**，旧 v8 与 v11 各 **0/3**。见 [目标检查证据](../../artifacts/tests/2026-10-08-als-target-gait/)。新候选在前置作者门禁失败，因此没有进行其 UE 重定向、发布或 Godot 验收。

原版真实 Godot 基线完成 **12 场景、7200 帧，全部通过**。Walk／Run 各覆盖四个实际 FootPlanted 支撑窗口；Crouch 原序列没有该曲线，改用四个松键时刻，不将站立协议强加到蹲姿。[原版基线报告](../../artifacts/tests/2026-10-08-gait-runtime-closure-original-phases-v1/summary.json)

原版与旧 v11 的 Run 同条件对照各 600 帧，共 **76 张截图**，请求帧、实际提交／显示、骨骼、输入和 HUD 标签全部一致。胶囊位置逐帧相同；v11 起步近地滑移中位数为 162.17 cm/s（本项限值 122.16），稳态为 170.41 cm/s（限值 25），且右脚缺少独立有效近地样本，质量检查失败。右脚最低点可接地但足底采样质心偏高，不能简化成整只脚悬空。

![原版与旧 v11 的普通移动和松键对照](../../artifacts/tests/2026-10-08-gait-runtime-closure-render-v2/movement-release-comparison.png)

该图上排原版、下排旧 v11，**不是本批新生成候选**。完整报告与逐帧来源见 [运行检查汇总](../../artifacts/tests/2026-10-08-gait-runtime-closure-render-v2/runtime-closure-summary.json)。9 个观测破坏样本、6 个截图破坏样本均被拒绝。

本轮纠正此前首帧穿地诊断：生产代码原本就隐藏首次冷提交，下一有效提交才显示完整 rig。此前记录的 −2.066／−3.010 cm 来自尚未呈现的骨骼。检查现在要求首个 cold 帧同时隐藏、未就绪且 rig 未求值，第二帧起必须可见且完成求值；首个可见帧及随后帧仍执行原 −2 cm 阈值。持续隐藏、cold 却可见、后续 invalid 均失败，原始负高度和旧失败记录保留。另修正松键事件发出帧 HUD 提前一帧显示 RELEASED 的问题，v2 截图使用实际输入标签。

## 生成环境与验证边界

首次新生成在 Windows `torch_cpu.dll` 中异常退出，错误码 0xc0000005，没有正常 Python traceback。资源余量当时不足，但未把 OOM 当作已证实根因。没有关闭用户其他程序或更换模型。

后续通过 [文件映射编码脚本](../../artifacts/tests/2026-10-08-kimodo-gait-closure-v1/stream_text_embeddings.py) 使用原始 BF16 权重和原生 LLM2Vec／supervised adapter，预编码确切提示词，再卸载编码器，以显式缓存运行 CPU 动作推理。一个实际投影的 MNTP 合并与安装的 PEFT 逐值相同。旧 demo 缓存的同提示词 embedding 不是逐值相同（余弦约 0.99997），且旧缓存缺少权重回执，因此不把它当严格等价 oracle，也不声称本轮是仅改变一个因素的 A/B。生成 receipt 绑定当前 checkpoint、缓存 manifest 与 payload 哈希，不允许缺失提示词时切换编码器。

保留首次环境崩溃、文件映射 fsync／PEFT 接口／文本编码读取错误及修复日志。最终 78 项 Python 检查通过；Godot Optimize 构建零警告／错误，并执行上述真实运行。没有执行新的完整 71 项、全方向新包矩阵或人工最终动画验收。

## 后续执行点

继续针对当前失败帧改善支撑期目标、原地运动与步幅／参考速度校准、周期边界的速度连续性，再用同一门禁重测。检查阈值保持，不能把某个单周期通过扩大为整条 master 通过。只有母动作通过后，才进入全部依赖派生、目标重定向、最终蒙皮与普通 E2E；当前不能激活这些新候选。
