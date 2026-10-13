# 普通移动上身风格、Sprint 单周期修复与游戏接入

本轮接续单周期裁剪和最终 Easing 修复：四个 Sprint／加速身份已经通过源风格、循环、接触与原累计预算。新增联合接缝／支撑求解，再走纯脚本重定向、Root／骨盆分解、逐资产曲线、最终 FBX 回读、Godot 导入及实际运行。

## 接入范围

- `ALS_N_Sprint_F`、`A_Als_Sprint`：原第 18–36 帧，0.6 秒／19 键单周期。
- `ALS_N_Sprint_F_Impulse`、`A_Als_Sprint_Acceleration`：同样制作一个循环，保留原加速语义、时间和速度合同。
- `ALS_N_SecondaryMotion`、`A_Als_Idle`：复用已验证的固定肩／大臂版本。只固定 gait 大臂而保留旧 Idle 叠加，实际仍有约 8° 大臂变化，所以需同时处理这一依赖。

Sprint 专项 V8 合成以执行期间新启用的 `als_nonprop_crouch_stop_mantle_20261013_v1` 为底，159 个其他动作语义摘要一致；四个新 Loop 与两个 Idle 独立验收。它的实际 Sprint 蒙皮速度降到 24.147 cm/s，原 25 cm/s 门禁通过。大臂变化约 0.000029°，双手位移 16.91／17.21 cm；但该专项包的普通 Walk／Run／Crouch 仍是旧风格，因此未设为默认。

最终 V10 按用户最初确认的普通移动及关联姿态范围合成：从此前已验证的 `als_nonprop_modular_locomotion_20261013_feminine_v1` 选择 114 个风格身份（含 Idle），再加入四个新 Loop，共 118 个选择身份。原活动包中四个最新蹲停修复明确优先于旧风格包同名资源，六个最新 Mantle 和其他范围外动作一并保持，共 61 个作者来源不变。补入已有 Run／后退 Walk 后合成 179 个生成非道具身份，余下四个 Jump Run 不属于本轮普通移动制作范围。70 个道具保留清单必须一致。

保持核对结果为 55 个最终姿势完全相同（含最新 Mantle／蹲停），另六个 Masculine／Injured／HandsTied 姿态表仅更新四个下肢叶子 twist 的旋转。这是表格对共享 Stand／Crouch 下肢的派生依赖：逐槽与本包已验证参考姿态精确相同；移除这四骨旋转差后，原表所有主骨骼、位置、缩放及其他元数据与活动包完全一致。原“61 项完整姿势相同”的拒绝保留在 `pose-retention.json`，准确的依赖保持核对在 `pose-retention-repair.json`，不把原拒绝改为通过。

## 门禁和修复

作者原接缝门禁现位于最终风格及共享端点／接触联合修复之后。大臂固定、小臂摆动在这个最终循环上制作，接受后只复核，避免再次编辑破坏循环。源预算仍为旋转 20°、踝 15 cm、Root 5 cm。

目标修复不依赖声明的接触曲线：首先用自身 FK 低带，否则用实际字节绑定的通过源 FK 相位并以目标摆幅交叉确认。脚掌稳定候选和低带收尾帧仍受原 20°／8 cm／5 cm 预算限制；骨盆三维总位移不能扩大为“每轴 5 cm”。候选独立从原目标出发，按原门禁余量选择，最后重新测量目标和真实蒙皮。

`AlsDirectionalMovementSmoke` 采集时复用既有 `ForceDraw` 显式绘制方式，在请求的实际提交帧结束后绘制。没有增加动画时钟、暂停物理或补造截图；提交／展示身份、骨骼、像素变化和轨迹一致性的断言保持。

## 实际失败保留

V3 渲染只采集 6／112 张截图，保留失败。V4 四个源通过，但目标 Sprint 脚跟滚动仍失败。V5 虽然 165 个 FBX 和 24 个目标步态通过，实际 girl 模型 Sprint 近地蒙皮速度仍为 27.645 cm/s，超过原 25 cm/s；单线程／并行／GPU 三种模式一致失败，不能用代理量通过代替真实模型通过。

V5 已获得 112 张身份一致 GPU 截图，三种模式身体／物理轨迹一致；其滑步拒绝仍保留。随后加强目标修复候选选择，在原预算内联合使用脚掌朝向和最小可达骨盆位移，再合成最新活动资源。

## 证据

制作及完整失败证据：`F:/Github/Kimodo-Pipeline/workspace/runs/joint-loop-contact-20261013-v1/`。

- `delivery-joint-v4/production/receipts.json` 和 `final-source-summary.json`：四身份通过源，首尾姿态／FK 差为零，双手源位移约 20 cm。
- `delivery-target-phase-v5/runtime/e2e-matrix/`：代理量通过、真实蒙皮拒绝和 112 张真实截图。
- `delivery-active-v8/`：Sprint 专项完整通过与真实上身测量，未启用。
- `delivery-active-v9/`：姿态表下肢组装后 twist 不一致，被原门禁拒绝。
- `delivery-active-v10/`：普通移动范围最终增量合成、保持／依赖核对、最终 FBX／运行证据。
- `final-focused-regression-v4.log`：相关 Python 197／197，通过修复预算、拒绝伪相位、原图接入、来源碰撞／篡改与组装后 twist 修复检查。

## 默认游戏已接入

按用户“继续实现、跑通并接入正式游戏”的授权，`assets/config/als_resource_pack.json` 已选择 `res://assets/generated/als_nonprop_upright_joint_active_20261013_v10/resource-pack.json`。普通入口仍为 `scenes/demo/als_demo.tscn`，保留原 `allowDiagnosticPack:true`；资产包仍 `productionReady:false`，没有调用严格生产激活或扩大门限。原指针与启用回执保存在 `artifacts/tests/upright-loop-active-20261013-v10/activation-receipt.json`，相同回执也在生产证据 `delivery-active-v10/activation-receipt.json`。

| 验证 | 实际结果 |
| --- | --- |
| Python 相关回归 | 197 项修复／生产图 + 16 项原生回读／释放顺序，共 213／213 |
| 目标步态／twist、最终 FBX | 38／38、128／128、179／179 |
| Optimize 构建、Godot 导入、六域绑定 | 通过 |
| 本轮选择的游戏场景 | 14／14；完整矩阵未执行，聚合 E2E 仍拒绝 |
| 三种模式真实女模型足底 | 各 28 条件，身体／物理轨迹精确一致；Sprint 24.501 cm/s，原限 25 cm/s |
| 实际 GPU 捕获 | 方向 112、站停 18、蹲停 18、Mantle 60 张，身份通过 |
| Air、静态 Mantle、平台／取消 Mantle | 各 11／11；输入未变 |
| 双模型蹲停额外复验 | 16／16，八对单／并行各 600 帧一致，静止骨盆高度通过 |
| 所有文件载荷摘要 | 2,223 项完整核对通过 |
| 普通默认入口 | 无包／角色环境覆盖，180 帧启动通过；随后启动图形游戏，日志确认 V10 和 girl 模型 |

真正展示的 28 个移动条件中，大臂相对胸部变化最大约 0.000043°，手部运动范围至少 16.86 cm；前向蹲伏保持约 53.60° 前倾。站立移动更接近 Idle。测量见 `delivery-active-v10/runtime-style-observation.json`；已检查 `final-style-contact-sheet.jpg` 中四种步态的四帧截图，未代替完整人工风格验收。加速与 Sprint 对应四个源的双手范围约 20 cm，源首尾姿势／FK 差为零，离散和实际插值接缝均通过原作者门禁。

默认启动证据位于 `artifacts/tests/upright-loop-active-20261013-v10/default-demo.log`，图形游戏日志和启动进程记录为 `game.log`／`game-process.json`。游戏日志的按需哈希标记为 `verified=143 skipped=2080`，全载荷 2,223 项另由启用脚本完整核对，不能把按需检查数量描述为整包全部已加载。

Viewer 最终批次为 `als_nonprop_upright_joint_active_20261013_v10-412e6ae00180`，179 个资产对应 716 个阶段条目。全部原拒绝、失败输出和输入绑定保留；当前仅余左右 Jump Run 的四个身份，属于本轮普通移动之外。完整 71 场景矩阵、人工风格、多 seed、长时间和性能验收仍开放。相关默认接入成功与严格整包 `runtime_failed` 分别记录，不把局部通过改写为完整生产接受。

后续按用户 commit 授权分别提交生产工具与消费侧接入。Godot 提交仅包含五行显式渲染采集修正、最终默认指针、本记录以及路线图／导航的相关段落；其他共享文件改动和已暂存的工具迁移删除保持。生成资源遵循原 Git 忽略规则，仅保留在本机；提交代码及指针不代表干净检出自带运行资产。生产工具部分暂存版本另由 Git blob 加载执行 270／270 相关回归，避免未提交的 Python 模块补齐依赖；这不替代整包、消费侧干净检出或人工验收。本次不推送。

可复用操作见[单周期制作方法](../../../Kimodo-Pipeline/docs/architecture/kimodo-single-loop-crop.md)与[生产端验证](../../../Kimodo-Pipeline/docs/verification/2026-10-13-joint-loop-game-integration.md)。
