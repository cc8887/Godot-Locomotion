# Run 镜像修复、重新验收与游戏测试

## 范围与输入

用户要求修复重复端点统计与镜像相位问题，重新检测跑步资源并导入游戏实测。实现位于主目录，未提交、推送或切换默认包。当前默认为 `als_nonprop_crouch_slip_20261011_v2`，本轮以其 161 个交付作为基线保留蹲走、其他动作与道具，重出六个 Run 方向的十二个 V4／Refactored 身份。

证据在 `artifacts/kimodo_als/run-phase-fix-20261011-v1/` 与 `run-phase-fix-20261011-v2/`。旧 [镜像诊断](2026-10-11-kimodo-run-constraints.md#镜像失败根因追加诊断)、42 次推理输出、旧通过回执及失败包均保留。最终候选路径为 `assets/generated/als_nonprop_run_phase_fix_20261011_v2/resource-pack.json`；六个方向的源门禁、十二个 Run 目标门禁及相关游戏检查已通过，整包仍为诊断包。

## 检查器与制作修复

`gait.py` 的周期样本中位数、分位数和支撑数量排除重复末帧；姿态、有限差分速度、接触连续性与穿地检查仍包含端点。循环接触检测继续读取真实相邻重复，保存 `measurementDomain=unique_cycle_frames`。门限未放宽：源近地中位速度 0.25 m/s、接缝速度差 0.75 m/s、角速度差 6 rad/s，周期制作预算 20 度／15 cm／5 cm；目标脚部／髋部修复仍为 8 cm／5 cm。

`cycleAnchorSide` 使确定性镜像保留原时间边界并交换左右支撑，重新测量实际事件、几何和曲线。时间反转仍独立测量落地。已有母动作在复用或作为镜像上游之前，额外执行当前检查器的完整单周期轨迹回读；仅有旧 `pass=true` 或正确摘要不再充分。

已测量支撑的水平残差修复锁定足底中点，固定骨长 IK 保留足部世界旋转、脚趾与竖直摆动。锚点从内部重复周期计算，跨周期边界统一；计划可在原八分之一周期限额内延长，仍不能代替输出验收。所需额外旋转、脚部位移及髋部降低与周期、接缝修复累计后执行原限额。

左前／左后各实际重新执行一次 diffusion，保留原提示词、模型 `Kimodo-SOMA-RP-v1.1`、seed 20261012、30 FPS、100 steps，仅使用已有 `preserveReferenceHeading` 保持参考髋线及 Root2D 朝向一致。第一版整链还实际重新生成后退一次；前进复用已有真实候选，右侧直接从重新验收的左母动作镜像。共三次新 diffusion，最终第二版复用这些候选，没有再执行推理。

最终制作参数固化于 `run_rules.json`：前进 4 谐波／ramp 2／周期 0／最小膝弯 21 度／支撑残差锁定；后退保持 2 谐波／ramp 4／周期 1／支撑 padding 1／膝弯 15 度；左前保留参考朝向、3 谐波／ramp 3／周期 2／padding 0／稳定膝 pole／目标相位 3 谐波；左后保留参考朝向、3 谐波／ramp 3／周期 0／padding 1／膝弯 20 度／flat stance／支撑残差锁定／目标相位 2 谐波。两侧直接镜像各自母动作。

## 失败保留与验证

原三个前进／左侧通过回执在新统计下均暴露源滑移，后退旧资源仍通过。原候选的邻近修复参数探索、支撑残差修复失败和新生成候选搜索均记录到第一版目录，不使用提高上限的方式关闭失败。

第一版六方向全部通过新源门禁，173 个最终 FBX 回读通过；前进两身份目标修复需要髋部降低 5.0405 cm，超过原 5 cm，故保持两项目标失败。其真实 Godot 构建、导入、P2B、六域绑定、六个相关场景、三模式方向足底原版对照通过，整包诊断拒绝仍保留。第二版只将前进源最小膝弯调至 21 度，使两套骨架在原限额内可达；不改目标门限。

管线完整 Python 回归 248/248 通过，日志为 `run-phase-fix-20261011-v1/final/python-full-suite.log`。新增对照覆盖循环统计随起帧变化的独立帧计数、坏运动即便重绑定通过摘要仍被拒绝、镜像保留逐帧相位、足部旋转与固定骨长、累计修复超限负对照。相关专项 92/92，日志为同目录 `python-tests.log`。最终 21 度配置的 Run／周期修复／镜像专项 25/25，日志为 `run-phase-fix-20261011-v2/final-config-tests.log`；六个最终动作独立复验通过，10 cm 根轨迹正弦摆动即便重新绑定摘要也全部被拒绝，见 `source-negative-controls.log`。

## 最终运行与剩余边界

最终版本已执行纯脚本重定向、Root/Pelvis 分解、逐资产曲线，32/32 目标步态门禁及 173 个最终 FBX 全关键帧回读通过。六个新 Run 方向的源独立周期结果如下；速度为作者足部近地中位数，不是游戏最终蒙皮速度。

| 方向 | 起始支撑脚 | 左／右近地速度 cm/s | 接缝速度差 m/s |
| --- | --- | --- | --- |
| 前进 | 左 | 23.186／11.107 | 0.659654 |
| 后退 | 左 | 24.436／19.786 | 0.745534 |
| 左前 | 左 | 21.398／17.205 | 0.572868 |
| 右前 | 右 | 17.583／21.362 | 0.572259 |
| 左后 | 左 | 11.066／6.603 | 0.705364 |
| 右后 | 右 | 8.290／10.829 | 0.703020 |

Godot Optimize 构建、新资源导入、P2B 及六域运行绑定通过。相关六个场景 6/6：普通 `als_demo.tscn`、60 Hz 单线程／并行蹲站、单线程／并行方向移动及真实渲染方向场景。候选包通过 `ALS_RESOURCE_PACK_PATH` 进入游戏；普通默认指针未切换。三种方向模式各 28 段原版最终蒙皮足底对照通过，6,720 帧身体／骨骼／物理轨迹完全一致；112 张 GPU 截图身份通过。有限查看前进、后退、左右移动各一张 phase 150 截图，角色、地面和新包 HUD 正常绘制，不代表完整人工动作验收。

`production/full-pipeline/runtime/e2e-matrix/directional-quality.json` 保存完整对照。单线程最终蒙皮足底近地中位数如下，单位 cm/s；并行及真实渲染模式通过同一原门限。

| 场景 | 原版 | 新包 |
| --- | --- | --- |
| 前进跑步 | 3.346 | 0.381 |
| 后退跑步 | 4.994 | 6.089 |
| 左移跑步 | 14.914 | 0.0007 |
| 右移跑步 | 15.271 | 0.0015 |
| 瞄准前进跑步 | 3.347 | 0.382 |
| 瞄准后退跑步 | 4.989 | 6.158 |
| 瞄准左移跑步 | 14.062 | 0.084 |
| 瞄准右移跑步 | 15.463 | 0.0014 |

后退有残余滑步，接缝接近源原门限；当前是固定种子和受控输入的通过，不保证其他种子、长时间、复杂地形或完整美术／性能验收。

Air 独立游戏回归 11/11。Mantle 静态 9/11，保留 Root 开启时 4.843 cm 穿台及支撑手高度超限、原版渲染对照 2.315 cm 穿台；平台正常十项通过，关闭手锁负对照按原 2 cm 蒙皮门槛拒绝，最大穿台 4.717 cm。两组 Mantle 与 Air 的输入冻结检查均通过，不把这些既有问题归入 Run 修复成功。

最终 [完成审计](../../artifacts/kimodo_als/run-phase-fix-20261011-v2/completion-audit.json) 中 `runSourceTargetFbxPassed=true`、`relatedRunRuntimePassed=true`。新包 1,894 个载荷及基线 1,768 个载荷摘要全部正确，161 个保留动作的 runtime raw 逐文件一致，70 个道具、默认指针、基线包和动画设置保持；当前默认制作参数与本轮规则快照一致。最终包 SHA-256 为 `a16ce5a24d1f77e57666c931301177b6c1f2ca22f35d99dc7b2cf8823ff74e31`。

整包入口仍为 `pipeline_complete_rejected`、运行总结果 `runtime_failed`：六个选定场景及方向质量全部通过，但诊断部分包不能通过完整矩阵入口，且 Mantle 的上述失败仍在。未执行全部 71 场景，未切换默认包、提交或推送，`productionReady=false`。当前候选缺 10 个身份／5 个语义：`Jump_Run_Left`、`Jump_Run_Right`、`Sprint`、`Sprint_Acceleration`、`Walk_Backward`，每种各两套版本；Run 六方向已无缺口。

整链 [结果](../../artifacts/kimodo_als/run-phase-fix-20261011-v2/production/full-pipeline/result.json)、[运行记录](../../artifacts/kimodo_als/run-phase-fix-20261011-v2/production/full-pipeline/runtime/runtime-result.json)、[相关场景汇总](../../artifacts/kimodo_als/run-phase-fix-20261011-v2/production/full-pipeline/runtime/e2e-matrix/summary.json)、[方向蒙皮及轨迹对照](../../artifacts/kimodo_als/run-phase-fix-20261011-v2/production/full-pipeline/runtime/e2e-matrix/directional-quality.json) 分别保留对应范围的通过与失败，不能用 Run 专项通过覆盖整包拒绝。
