# 文档导航与维护

## 从哪里开始

- [代理工作约束](../AGENTS.md)：工作目录、修改边界、架构与验证原则。
- [Kimodo 跑步生产范围](architecture/kimodo-run-pipeline.md)：六个方向循环、严格源／目标检查、有界接触计划延长和已有交付复用；[首次生成与缺口](verification/2026-10-11-kimodo-run-generation.md)、[五方向约束修复与保留失败](verification/2026-10-11-kimodo-run-constraints.md)、[镜像与周期去重修复、重新验收和游戏测试](verification/2026-10-11-kimodo-run-mirror-revalidation.md)。
- [Kimodo 缺失资源派生](architecture/kimodo-derived-resources.md)：从已有生成候选独立采样静态姿态、Lean/Acceleration、已验收母动作复用与组合包完整转换；[本批证据](verification/2026-10-10-kimodo-derived-resources.md)。
- [自动翻滚恢复测试的接地前置条件](verification/2026-10-10-ragdoll-recovery-fixture.md)：旧固定 2 秒误判、夹具修正、22/22 相关运行与未接地负对照；新 Land／贴地 Roll 已默认接入。
- [Land／Roll 提示词修订与候选验证](verification/2026-10-10-kimodo-landing-prompt-regeneration.md)：可配置的蹲伏／翻滚描述、真实同 seed 对照与候选重采样，保留恢复失败和原有门禁；不新增固定动作风格检查。
- [不同高度下落与自动翻滚诊断](verification/2026-10-10-kimodo-drop-height-diagnosis.md)：修复前 3／5 m 的 Roll 悬空缺陷、同帧原始对照、8 m 自动 Ragdoll 和采样失败边界。
- [呼吸叠加动态幅度](architecture/als-secondary-motion-variation.md)：普通 ALS Demo 的 SecondaryMotion 旋转倍率、参考姿势与同一源事务；[真实对照及覆盖边界](verification/2026-10-10-secondary-motion-variation.md)。
- [独立 Air pipeline](architecture/kimodo-air-pipeline.md)：下落／高速下落／Flail 与 Air Lean 的专用生成、竖直残差所有权、周期制作和交付门禁；[实现验证](verification/2026-10-10-kimodo-air-pipeline.md)、[首次生成与接入](verification/2026-10-10-kimodo-air-integration.md)及[中段蹲缩重新生成与实际游戏验证](verification/2026-10-10-kimodo-air-crouch-regeneration.md)。
- [中高攀爬生成与实际游戏验证](verification/2026-10-10-medium-high-mantle.md)：生成 High／两 Low、目标蒙皮控制曲线、实际高度准备段补偿和可复用 Godot 高度／帧率／渲染检查。
- [最新生成资产统一默认包](verification/2026-10-10-latest-generated-default-pack.md)：最新 Idle／Walk 等与手部修复 Low 攀爬统一选择、原版回退清单和普通默认启动验证。
- [Kimodo Mantle 生成与导出](architecture/kimodo-mantle-export-policy.md)及[本批验证](verification/2026-10-09-kimodo-mantle-export-policy.md)：按动作类型分派手部/脚部条件、有界接触 IK、Root/Pelvis 特化和逐资产门禁。
- [Mantle 实际生成](verification/2026-10-09-kimodo-mantle-generation.md)：四轮真实扩散对照、Low 部分交付、High 路径拒绝及完整转换/运行证据。
- [Mantle 朝向与 Viewer 播放检查](verification/2026-10-09-kimodo-mantle-orientation.md)：纯脚本全帧方向／Root 曲线、component 与 FBX 手性、真实 Three.js 播放修复及 Godot 模型相对旋转对照。
- [Mantle 默认活动指针与游戏启动](verification/2026-10-09-kimodo-mantle-active-pointer.md)：用户选择 Mantle v4 诊断包、四个 Low 替换身份及普通 Demo 真实启动。
- [Mantle 可复用真实场景验证](verification/2026-10-09-kimodo-mantle-scene.md)：直接运行测试场景，Low 三种物理 Hz／根运动开关／单线程、候选与原版 GPU 截图及程序集完整性；不依赖 computer-use。
- [Mantle 运行时朝向与穿模复核](verification/2026-10-09-mantle-runtime-orientation-penetration.md)：修复 Root 关闭时的 mesh 基底抵消遗漏，逐帧 actor／完整蒙皮与平台检查，保留仍失败的接触质量证据。
- [Mantle 固定支撑曲线与双向交付合同](architecture/als-mantle-contact-control.md)与[曲线接触验证](verification/2026-10-09-mantle-contact-progress-curves.md)：目标蒙皮避障前移的计算、Pipeline 必须导出与 Godot ALS 必须消费的字段／身份／时间轴契约，平台局部锚点驱动同一事务的手臂 IK。
- [Mantle 曲线用途与能力门禁](verification/2026-10-10-mantle-curve-semantics-gates.md)：独立原始距离、避障曲线平台段和已实现模式声明，防止误称完整 Distance Matching／Motion Warping；实际导出及 .NET Import 对照。
- [Mantle 生产后手部蒙皮修复](architecture/kimodo-mantle-hand-skin-repair.md)与[实际导入／游戏验证](verification/2026-10-10-mantle-hand-skin-repair.md)：目标指节优化、接近／释放双骨 IK、最终 FBX 回读和两侧支撑手的真实 Godot 场景。
- [当前实施路线](../ROADMAP.md)：当前目标、优先级、开放项与证据链接。先读当前目标，再按任务定位相关章节；历史阶段记录中的“下一步”不代表当前安排。
- [Godot 资源扫描范围](architecture/godot-resource-scan-scope.md)：原版 ALS、Lyra、活动包和本次候选的导入范围、历史包重新开放命令及验证边界。
- [架构文档](architecture/)：长期设计、模块职责、接口及生命周期契约。按本次涉及的模块查阅。
- [验证记录](verification/)：每批实现范围、命令与产物、测试结果、失败修复及未验收边界。由路线图链接到相关记录，不需要逐篇加载。
- [交接记录](handoff/)：特定阶段或任务的交接背景，使用时核对当前代码与路线图。
- [主目录归并审计](verification/2026-09-25-main-directory-audit.md)：旧 worktree 的移除与 ignored 资料归档位置。
- [Kimodo 完整 ALS 资源生产设计](architecture/kimodo-als-resource-production.md)：资产作用、生成/派生原则、逐资产约束清单与验收顺序。
- [Kimodo 依赖图执行脚本](../tools/kimodo_pipeline/README.md)：按资源先后关系生产、参考姿态传递、程序化派生与可恢复执行。
- [目标竖直相位与接触 IK](architecture/kimodo-target-vertical-phase-ik.md)与[本批闭环验证](verification/2026-10-09-kimodo-target-phase-ik.md)：独立识别候选支撑，再修复接触并执行原有严格门禁，保留修复前后制品。
- [Walk 与 Idle 手臂一致性](architecture/kimodo-idle-consistent-arms.md)与[生成对照和闭环](verification/2026-10-09-kimodo-idle-consistent-arms.md)：放松摆臂提示词、正确中性基准映射及实际 Idle 相对上身检查。
- [v2 诊断包默认指针与普通启动验证](verification/2026-10-09-kimodo-idle-arms-default-pointer.md)：记录活动指针切换、诊断包加载开关、普通 Demo 启动证据和生产验收边界。
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
