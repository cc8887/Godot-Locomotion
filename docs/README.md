# 文档导航与维护

## 从哪里开始

- [代理工作约束](../AGENTS.md)：工作目录、修改边界、架构与验证原则。
- [Mantle 运行时朝向与穿模复核](verification/2026-10-09-mantle-runtime-orientation-penetration.md)：修复 Root 关闭时的 mesh 基底抵消遗漏，逐帧 actor／完整蒙皮与平台检查，保留仍失败的接触质量证据。
- [当前实施路线](../ROADMAP.md)：当前目标、优先级、开放项与证据链接。先读当前目标，再按任务定位相关章节；历史阶段记录中的“下一步”不代表当前安排。
- [架构文档](architecture/)：长期设计、模块职责、接口及生命周期契约。按本次涉及的模块查阅。
- [验证记录](verification/)：每批实现范围、命令与产物、测试结果、失败修复及未验收边界。由路线图链接到相关记录，不需要逐篇加载。
- [交接记录](handoff/)：特定阶段或任务的交接背景，使用时核对当前代码与路线图。
- [主目录归并审计](verification/2026-09-25-main-directory-audit.md)：旧 worktree 的移除与 ignored 资料归档位置。
- [Kimodo 完整 ALS 资源生产设计](architecture/kimodo-als-resource-production.md)：资产作用、生成/派生原则、逐资产约束清单与验收顺序。
- [Kimodo 依赖图执行脚本](../tools/kimodo_pipeline/README.md)：按资源先后关系生产、参考姿态传递、程序化派生与可恢复执行。
- [目标竖直相位与接触 IK](architecture/kimodo-target-vertical-phase-ik.md)与[本批闭环验证](verification/2026-10-09-kimodo-target-phase-ik.md)：独立识别候选支撑，再修复接触并执行原有严格门禁，保留修复前后制品。
- [Walk 与 Idle 手臂一致性](architecture/kimodo-idle-consistent-arms.md)与[生成对照和闭环](verification/2026-10-09-kimodo-idle-consistent-arms.md)：放松摆臂提示词、正确中性基准映射及实际 Idle 相对上身检查。
- [最新非道具源生成](verification/2026-10-09-kimodo-nonprop-regeneration-v1.md)：新 seed、45 个候选、125 个源身份、7 个通过步态、58 个阻断身份及真实 Viewer 截图；记录当时的源阶段。
- [默认完整纯脚本 Pipeline](architecture/kimodo-full-offline-pipeline.md)与[转换/实际运行证据](verification/2026-10-09-kimodo-full-offline-pipeline.md)：本批源继续转换为 Godot 包，Root 曲线/最终 FBX 回读及同帧截图；质量失败与活动包分开。
- [Motion Viewer 最新生成批次](verification/2026-10-09-kimodo-viewer-latest-authoring.md)：源候选／母动作登记、批次切换、活动包区别及实际浏览器截图。
- [Motion Viewer 跨批次与阶段链路](verification/2026-10-09-kimodo-viewer-batch-stage-chain.md)：真实目标分解/原地/final 制品、Root/Pelvis 数值和批次截图。
- [侧向步态／Sprint 加速生成合同](architecture/kimodo-directional-gait-generation.md)与[真实生成结果](verification/2026-10-09-kimodo-directional-gait-regeneration.md)：纯侧移、持续发力循环、多周期／末端约束、镜像相位与仍失败的跑步范围。
- [Kimodo 非道具替换验证](verification/2026-10-07-kimodo-non-prop-replacement.md)：183 个新动画、70 个保留道具动画的发布、普通 Demo 与人工验收边界。
- [ALS 根运动开关和曲线契约](architecture/als-root-motion-policy.md)：普通 ALS 默认原地、根运动采样策略与生产时每资产独立烘焙。
- [根运动与曲线实测](verification/2026-10-07-als-root-motion-policy-and-distance-curves.md)：真实开关/翻越对照、发布 stage 的曲线一致性及回归边界。
- [非道具四方向 E2E 与截图](verification/2026-10-07-als-nonprop-directional-e2e.md)：普通前后左右输入、原点/起步修复、完整矩阵与仍未通过的滑步质量门禁。
- [普通移动、松键与根运动诊断](verification/2026-10-08-als-movement-release-diagnosis.md)：默认 v8 的骨盆偏移／脚锁折腿、胶囊位移积分对照、全阶段截图和 71 项新门禁的失败边界。
- [原版 ALS 移动与资产契约](verification/2026-10-08-als-original-locomotion-analysis.md)：原地步态、速度匹配、固定帧停止姿态、V4／Refactored 脚锁差异及替换包的原点／相位对照。
- [Kimodo 步态约束与分阶段验收设计](architecture/kimodo-als-locomotion-constraints-and-gates.md)：数值生成条件、事件选周期、DAG 门禁和普通移动／松键检查的契约；试点实施与失败边界见闭环试验。
- [Kimodo 前进步态闭环试验](verification/2026-10-08-kimodo-gait-closure.md)：三条真实新候选被作者门禁拒绝、原版起停基线、旧包负向对照与准确截图；当前没有可激活的新包。
- [离线修复与关键帧回灌](architecture/kimodo-repair-and-regenerate.md)：Kimodo 原理、周期邻域约束、Blender 开源方案核查、原生离线修复入口及尚未通过的实测。
- [周期修复／回灌后的 Walk 闭环](verification/2026-10-08-periodic-repair-feedback.md)：真实生成、目标骨架和四相位松键通过，工具链／姿态表语义修复及仍失败的方向范围。
- [前向速度与起步位移复核](verification/2026-10-08-forward-walk-speed-audit.md)：当前 v8 的 pelvis/Hips 起步异常、G 候选对照、胶囊速度和动画质量失败证据。
- [Kimodo Pipeline 原点对照契约](architecture/kimodo-viewer-comparison-space.md)与[可视化实测](verification/2026-10-08-kimodo-viewer-origin-comparison.md)：单栏/A-B/多阶段固定原点、共同相机、独立 root/pelvis 轨迹及当前活动包来源。
- [Kimodo 水平运动所有权](architecture/kimodo-planar-root-ownership.md)与[分解及最终 FBX 验证](verification/2026-10-08-kimodo-planar-root-ownership.md)：整体 root 轨迹、pelvis 残差、原地视图、每资产曲线、原生完整轨道回读与首帧截图。
- [Kimodo 完整纯脚本 pipeline](architecture/kimodo-full-offline-pipeline.md)：默认生成、重定向、Root／曲线、FBX 独立回读与 Godot 检查；源／目标失败继续转换并保留门禁。

## 各类内容如何维护

| 内容 | 维护位置 | 更新方式 |
| --- | --- | --- |
| 每次工作都适用的约束 | 根目录 `AGENTS.md` | 仅在约束改变时更新，不追加进度 |
| 当前目标、完成范围、待办与暂缓项 | `ROADMAP.md` | 更新对应当前条目，附证据链接，避免重复整段验证日志 |
| 稳定架构、所有权、时序和接口契约 | `docs/architecture/` | 按主题维护，具体改动引用对应验证记录 |
| 测试数量、命令、日志、哈希、截图、失败与限制 | `docs/verification/` | 每批写入对应记录，明确适用代码与覆盖范围 |
| 一次性历史快照 | `docs/history/` | 保留取证，不作为当前指令，不继续追加 |

任务收尾时，将详细证据写入验证记录，并更新路线图对应的当前状态与链接。不要把同一段进展再复制到 `AGENTS.md`。已有验证记录覆盖的事实直接引用；缺失事实补入对应记录。

## 本次整理保留的历史

[2026-10-07 整理前的 AGENTS 快照](history/2026-10-07-agents-before-cleanup.md) 是原文件的完整、字节一致副本，仅用于避免整理时遗失历史证据。它包含互相覆盖的阶段状态和旧指令，不能作为当前执行依据，也不需要日常读取。

本次只精简代理入口并增加导航，未改写已有路线图或验证记录，也未重新验收其中记载的功能。后续如整理路线图中的历史长日志，应将独有事实补入对应验证记录，在当前条目只保留状态、剩余工作和证据链接。
