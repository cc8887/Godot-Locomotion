# 2026-10-09 目标竖直相位与 IK 闭环

本次按用户建议修改目标阶段顺序：先依据足底竖直周期极值判断候选相位，再接触 IK，最后检测实际支撑与滑移。没有重新调用生成模型；继续最新 `nonprop-regeneration-20261009-v1-e57f09f835b2` 的成功源身份。角度重定向未改，复用逐文件摘要验证过的离线 retarget 缓存；所有下游目标修复、Root 分解、曲线、最终 FBX 和 Godot 验证重新执行。

## 实现与测试

- 新增 `target_phase.py`：独立于绝对地面高度与生成接触标签，目标脚跟/脚掌高度平均、周期前三谐波、显著极值、左右交替与谷底低位候选窗口。
- `target_cleanup.py`：复用固定骨长 IK，按原版速度/方向重建世界支撑锚点，支持跨循环边界，脚踝 ≤8 cm、骨盆下移 ≤5 cm。只修腿和必要骨盆位置，上身/Root 不由该步骤重写。
- `publish.py`：默认已支持的平脚周期执行相位 → IK → Root/Pelvis → 严格目标检查，诊断保存失败制品；保存 `target-phase/` 前后 raw 和回执，进入包载荷摘要。修正诊断报告将几何和 ALS 协议的通过状态合并，正式断言保持。
- `target_gait.py` 的原版几何阈值、事件/停止/滑移断言未改。源作者门禁和正式完整闭包检查保留。
- Python 完整套件 **154 项通过**，包括漂浮高度不影响相位、修复前失败/修复后严格通过、骨长/上身不变、同相/双周期/静态拒绝、超限不修改输入、跨循环 IK。

随后修正验证目录绝对路径，以及 IK 超限时保留已成功的相位回执，相关 **28 项回归通过**，见 [定向回归](../../artifacts/kimodo_als/phase-ik-20261009-v1/final-focused-regression-v3.log)。扩展原版基线后发现两条原版蹲姿后侧移的竖直谷底不符合半周期假设；相位检测不能成为新的质量硬门禁。最终流程在相位失败/修复超限时保留输入，继续执行原有严格目标门禁，未应用修复仍明确记录失败状态。

最终生产逻辑完整 Python 套件 **155 项通过**，见 [完整 v4 日志](../../artifacts/kimodo_als/phase-ik-20261009-v1/python-regression-v4.log)。最后将相位回退测试改为真实原版蹲姿后侧移反例，8 项相位回归通过，见 [原版反例回归](../../artifacts/kimodo_als/phase-ik-20261009-v1/native-phase-fallback-regression-v5.log)。当前代码重算本批 10 个身份，前后 raw、相位及修复报告均与发布制品完全相同，见 [重复计算审计](../../artifacts/kimodo_als/phase-ik-20261009-v1/phase-ik-repeat.json)。

[原版竖直极值基线](../../artifacts/kimodo_als/phase-ik-20261009-v1/native-phase-baseline.json)记录13个平脚周期中11个可识别、两个蹲姿后侧移不可按该模型识别；后者的原版严格几何检查通过。这是**相位模型适用边界**，不是两条原版资产质量失败。

证据：[完整 Python 日志](../../artifacts/kimodo_als/phase-ik-20261009-v1/python-regression-v1.log)、[实际试修 v1](../../artifacts/kimodo_als/phase-ik-20261009-v1/actual-pilot-v1.json)、[实际试修 v2](../../artifacts/kimodo_als/phase-ik-20261009-v1/actual-pilot-v2.json)。

第一轮实际试修 6/10 通过，四个后向侧走失败于严格半周期接触事件。两侧竖直低位平台宽度不同，直接各自取边界导致接触起点错位。最终改为相对各自谷底的共同可靠低位区间，保留原测量窗口；没有修改后置事件阈值。第二轮 **10/10** Walk 身份几何通过：Refactored Forward/Left Forward/Left Backward/Right Forward/Right Backward，以及 V4 F/LF/LB/RF/RB。

最大脚踝修正 **3.2501–3.3099 cm**，所有资产骨盆下移 **0 cm**。前向停止帧 4/21、支撑块、左右接触事件、半周期与滑移检查通过。两条纯侧蹲走 `ALS_CLF_Walk_L/R` 仍无检查器对应规则，明确 `analysis_failed`，未伪造通过。

![Walk 修复前后高度与相位](../../artifacts/kimodo_als/phase-ik-20261009-v1/vertical-phase-before-after.png)

图中绿色为极值推断的候选窗口。修复前脚跟靠近地面，但脚掌约 3 cm；修复后窗口内两者同时到地面。图使用同一个目标原版时间域，纵轴单位 cm；不能作为最终蒙皮验收替代。

## 完整转换

新包 [als_nonprop_20261009_phase_ik_v1](../../assets/generated/als_nonprop_20261009_phase_ik_v1/resource-pack.json)，源 bundle 摘要 `dbd57e6594a66c659702949759d85c806eeabfa0da9dea66cdf8e6ee58e9d01d`，回执摘要 `55fdf0609ac3d8697a0c27c005f8be9a477bd988743a1abe6470951ae546528d`。

实际 **125/183** 非道具身份转换完成，58 个源身份缺失沿用明确原版回退；70 个道具保持原字节。最终 **125/125 FBX** 独立 ufbx 全物理骨骼/采样键回读通过。34 个声明根运动曲线与50 个额外提取根轨迹分别保存；10 个已支持目标 Walk 的几何和 ALS 协议全部通过，剩余目标检查失败只有上述两个不支持身份。详见 [转换结果](../../artifacts/kimodo_als/phase-ik-20261009-v1/conversion/pipeline-result.json)和[转换日志](../../artifacts/kimodo_als/phase-ik-20261009-v1/conversion.log)。

## Godot 验证和交付边界

首轮路径失败保存在 `runtime-v1/`：请求输出环境变量要求绝对路径，验证入口接收相对输出目录导致六域请求失败，进而旧请求摘要导致 Demo 拒绝初始化。修正 `verify_runtime` 在入口统一解析绝对证据目录，使用新 `runtime-v2/` 重跑，不覆盖失败日志。优化构建 **0 警告/错误**，Godot 实际导入、P2B 审计和六域请求均通过，普通 ALS Demo 已使用本包初始化并完成受控测试。

完整矩阵 **71/71 场景执行，69/71 场景检查通过**，失败 `recovery-auto-roll` 与 `rendered-directional`。三 Hz 单线程/并行摘要一致，资产载荷和开关未变，保存 **224 张矩阵截图**（渲染方向在第90张后失败，未凑齐112张）。[原始矩阵汇总](../../artifacts/kimodo_als/phase-ik-20261009-v1/runtime-v2/e2e-matrix/summary.json)保持失败，`executedAllScenarios=true`、`completeMatrix=false`，不产生完整替换证书。除了运行失败，58个源缺失也使诊断入口总体退出1，不能用场景通过改写为发布成功。

`rendered-directional` 在 `aim-run-left` 的方向对齐断言失败，alignment=0.973（原阈值0.99）。记录显示测试目标方向在稳态由 `[-0.925,0,-0.380]` 变为 `[-0.972,0,-0.235]`；渲染测试仍捕获桌面鼠标，OrbitCamera 输入会旋转方向，专用追踪相机并未隔离该控制输入。仅在 `AlsDirectionalMovementSmoke` 关闭鼠标捕获，保留所有物理/脚底断言；新的优化构建0错误/警告。

独立 `directional-repair-v3/` 三模式运行本身均完成28段/6720帧，但渲染后10段没有正常绘制回调，只保存72/112张，截图断言仍失败。这一失败保留，不将运行完成当作截图通过。测试观察器增加每张截图的请求/观察/提交/显示帧和新读出的骨骼坐标；矩阵脚本核对112个目标采样时刻、文件身份和与观察帧一致的骨骼。

`directional-capture-v4/` 数值三模式3/3、28段原版滑移对照及112个截图元数据通过，但**实际标签复核失败**：`walk-left-120.png` 的HUD仍显示Frame600/sample119，当前提交应为Frame601/sample120。物理回调内主动绘制早于场景绘制更新，不能只依赖CPU元数据认证图片。`directional-deferred-v5/` 专用采样标签更新，但普通HUD仍滞后一帧。各轮记录保留，最终版本已移除主动/延迟强制绘制。

中间曾误判v5/v6的3D画面冻结：Pillow对RGBA差值调用`getbbox()`默认只检查透明度，而截图透明度不变。改为RGB后确认v4至v8相邻采样、站立/蹲姿的3D像素均不同，撤回旧画面复用结论；真正发现的问题为漏采和标签滞后。见 [RGB像素审计](../../artifacts/kimodo_als/phase-ik-20261009-v1/image-rgb-audit.json)。

最终 `directional-native-v8/` 使用正常 `FramePostDraw` 回调，不调用 `ForceDraw` 或新增角色求值/时钟。仅渲染测试限制每次呈现最多一步物理工作，原60Hz物理时间域不变；测试窗口置顶、视口持续更新，退出恢复设置。图像增加3D区域像素摘要，与提交身份、骨骼和普通HUD共同复核。实际保存 **112/112 张**，28段方向/站蹲/步态场景通过；与同包单线程/并行记录的角色物理数据完全一致，三模式原版相对脚底对照均通过。见 [最终定向对照](../../artifacts/kimodo_als/phase-ik-20261009-v1/directional-quality-v8.json)。该单场景诊断总体仍退出1，原因是部分源包及非完整矩阵，不能据此宣称全量包通过。

观察器负向检查拒绝缺图、观察帧错配、骨骼错配和重复旧3D摘要，见 [截图断言负向验证](../../artifacts/kimodo_als/phase-ik-20261009-v1/capture-validator-negatives.json)。最新优化构建 **0 警告/错误**，见 [v8构建](../../artifacts/kimodo_als/phase-ik-20261009-v1/render-native-build-v8.log)。

矩阵中 `recovery-auto-roll` 失败：第二轮自动 Roll → Ragdoll 在既有固定检查时刻仍未满足接地断言，位置 `(9.783997, 0.94245666, 3.7269883)`。独立同场景原版与修复前 v6 均通过，新包重复得到同一失败，详见 [三包复现](../../artifacts/kimodo_als/phase-ik-20261009-v1/auto-roll-diagnostics-v1/results.json)。不能当作路径问题、跳过或通过重跑覆盖该失败。

[修复前后 runtime raw 对照](../../artifacts/kimodo_als/phase-ik-20261009-v1/pre-post-runtime-raw-diff.json)表明125个身份中115个完全相同，只有上述10个Walk改变，翻滚/起身 raw 未改。复现支持这是 Walk 姿态/支撑曲线变更进入物理流程后的回归；具体哪项接触或初始物理状态导致该时刻未接地，尚未定位。该运行失败仍阻止新包验收，本次不修改物理代码或延长接地断言时限。

额外 Walk 对照使用 `verify-als-gait-closure.py --modes walk --directions forward,left-forward --supports scheduled --release-ticks 210 --render`。原版/新包各两方向共 **4/4** 进程通过，原版相对脚底对照 **2/2** 通过，保存 **152 张实际截图**；另输出两张同帧拼图。覆盖 tick 1–5、输入前29/输入后31、稳态、209/210/211 松键和后续 idle，截图观察/提交/显示身份一致。首帧为仍隐藏的冷姿态，tick2 开始可见；没有将该首帧空画面当作资产。没有执行全部松键支撑窗口。

| 方向 | 启动脚底中位滑移 cm/s | 稳态 | 松键 | idle |
| --- | --- | --- | --- | --- |
| 前进 | 11.726 | 4.677 | 0.00049 | 0.00027 |
| 左前 | 33.748 | 6.207 | 0 | 0 |

左前启动通过原版相对上限 **73.620 cm/s**，并非低于固定25。严格比较及帧呈现证据见 [Walk 汇总](../../artifacts/kimodo_als/phase-ik-20261009-v1/walk-release-screenshots/summary.json)。

另按角色组件原点复核骨盆水平运动，排除胶囊世界行进：新前进/左前 Root Motion 与 Root 采样均关闭，Root 组件偏移峰值 **0 cm**；骨盆相对 Root 水平峰值 **2.101/1.923 cm**，输入启动29–90帧最大单帧变化 **0.384/0.234 cm**。本候选两段测试未重现历史大幅起步偏移；不是对尚未替换的默认 v8 作出通过声明。见 [运行原点审计](../../artifacts/kimodo_als/phase-ik-20261009-v1/runtime-origin-audit.json)。

上述四段Walk在40–209帧的目标输入方向各只有一个固定向量，未受方向渲染场景发现的桌面鼠标干扰；其既有截图身份检查通过。

![前进原版/新包同帧对照](../../artifacts/kimodo_als/phase-ik-20261009-v1/walk-release-screenshots/forward-original-new-comparison.png)

![左前原版/新包同帧对照](../../artifacts/kimodo_als/phase-ik-20261009-v1/walk-release-screenshots/left-forward-original-new-comparison.png)

实际检查前进和左前的启动、稳态和 idle 图：**新 Walk 稳态展臂仍是视觉失败**，松键初段仍可见，最终 idle 收回。本批的接触 IK 未改变上身轨道，与 [上一批上身问题证据](2026-10-09-kimodo-full-offline-pipeline.md)一致；没有用局部脚底数值通过替代动作视觉验收。

最终审计 **1495 个包载荷摘要一致**、70个道具未变、125个最终FBX回读通过，上身/Root未被接触IK改写。包摘要 `a42449f4e7b4a1f3acfa6117b46cb4a3e2099329e46f66e915952d9bb3dbc1c4`，见 [载荷审计](../../artifacts/kimodo_als/phase-ik-20261009-v1/final-payload-audit.json)与[交付结果](../../artifacts/kimodo_als/phase-ik-20261009-v1/result.json)。

本包保持诊断状态。相位检测通过不等于真实支撑验收，腿部接触修复不解决源动作的展臂问题；自动翻滚后的接地回归、58 个缺失源身份及两个目标检查覆盖缺口仍开放。默认活动 v8 不因局部成功自动切换。
