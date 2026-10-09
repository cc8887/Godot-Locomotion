# Kimodo 完整纯脚本 Pipeline：转换与实际运行

本批将用户“每次跑完整 pipeline，包括资产转换”的要求落实到默认生产入口，并继续处理 10 月 9 日最新生成批次。没有再次调用模型推理；使用已保存、逐文件验证摘要的 125 个源交付身份，完成目标转换、Root/Pelvis 分解、独立轨迹文件、最终 FBX 回读、Godot 导入与运行。生产执行完成与动作质量通过分别报告。

## 默认行为

`tools/produce_als_resources.py run` 默认非道具范围，执行作者 DAG → 纯 Python 目标重定向 → Root/Pelvis 分解及曲线烘焙 → 最终 FBX → 独立 ufbx 回读 → Godot 优化构建/导入/六域请求 → 非道具运行矩阵。独立源分支继续执行；依赖失败的下游保持阻断。局部生成须显式使用 `--authoring-only`。

`scripts/produce-non-prop-als.ps1` 同步使用该完整入口，不启动 UE。源或质量不合格仍对成功交付执行后续转换和诊断运行，保留失败状态，最终非零退出。正式 stage 的完整身份要求、激活门禁和全部原场景断言保持；诊断包不能被激活。

设计与依赖安装见[完整离线入口契约](../architecture/kimodo-full-offline-pipeline.md)和[命令说明](../../tools/kimodo_pipeline/README.md)。

## 本批实际制品

最终诊断包为 `res://assets/generated/als_nonprop_20261009_offline_v6/resource-pack.json`。目录位于主检出的 ignored 资源区，代码检出本身不包含可运行交付。

| 检查 | 结果 |
| --- | --- |
| 非道具源交付/转换 | 125/183；61 个 V4，64 个 Refactored；58 个身份仍被源质量/依赖阻断 |
| 物理骨骼 | 68 根完整局部 TRS；虚拟骨骼由运行时重建 |
| 最终 FBX 独立回读 | 125/125；4656 个采样键、316608 个骨骼采样；位置误差 0 cm，旋转最大 2.442e−13° |
| 每资产曲线 | 34 份运行根轨迹、50 份提取的作者根轨迹；源步态去掉的行进轨迹另行保存 |
| 道具 | 70 个身份保留原字节，不用于本批角色动作测试 |
| 目标步态门禁 | 12 个失败：10 个支撑/半周期检查未通过；2 个纯侧蹲走尚不受检查器支持，明确记录分析失败 |
| Python | 全管线 147 项通过；最后辅助代码改动后额外 7 项离线转换回归通过 |
| Godot | 优化构建 0 错误/警告；实际导入、P2B 审计、六域请求通过；普通 ALS Demo 使用本包初始化成功 |

转换保留源采样时间、原绑定姿态、原网格实际大小写骨名和 UE 原导出坐标声明，FBX take 使用 `Unreal Take`。早期失败版本 v1–v5 保留：检查器不支持身份、最终元数据路径、take 名、米/厘米比例、骨名大小写及 FBX 轴声明均已定位修复；最终 v6 独立对照原始网格全部绑定姿态和轴声明，实际 Godot 载入成功。没有将失败版本覆盖为成功证据，也没有导入 UE 后再导出。

目标 Walk_F 的 pelvis-root 水平峰值由 2.417 cm 降为 1.957 cm；Walk_LF 由 1.956 cm 降为 1.941 cm。剩余值属于分解保留的局部自然运动，并非整段平移。Root/Pelvis 分解保留身体 FK，重建误差不超过 1.8e−12 cm。不能把“整段平移归 Root”误写成“pelvis 每帧水平残差必须为零”。

数值证据：[FBX 汇总](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/offline-fbx-summary.json)、[转换结果](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/conversion-v4/pipeline-result.json)、[最终运行结果](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/runtime-v6/runtime-result.json)。4 个真实动画的重新导出字节一致性另存于 `fbx-repeat/report.json`。

## 实际移动与截图

完整 71 场景全部执行，场景运行检查 **71/71 通过**；另一次原版方向基线通过。前后左右 28 段动作的最终蒙皮脚底原版对照在单线程、并行、渲染三模式均通过，三模式身体/物理帧序列完全一致；平台 30/60/120 Hz 单线程/并行摘要一致。矩阵保存 246 张真实截图，未检测到运行资源或动画开关变化。

源缺失使本包必须使用 `--diagnostic-partial-pack`：执行全部场景但不产生完整替换证书，`executedAllScenarios=true`、`completeMatrix=false`，总退出码仍为 1、`runtime_failed`。该模式验证替换身份与缺失身份恰好覆盖非道具合同；不会降低任何物理、脚部或原版对照阈值。部分模式/身份使用原版回退，不能把本结果称为 183 个全新动作通过。

[完整运行矩阵](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/runtime-v6/e2e-matrix/summary.json)、[原版方向滑步对照](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/runtime-v6/e2e-matrix/directional-quality.json)。

另执行前进、左前 Walk 原版/新包对照，共 4 个真实 Godot 进程、152 张截图。采样覆盖启动 tick 1–5、输入开始 29/31/35、稳态、松键 209/210/211 及后续停止/idle；截图请求、实际观察、提交与显示帧身份一致。只覆盖 scheduled 松键 tick 210，没有宣称全部支撑侧/所有方向验收。

受控起停/最终蒙皮脚底数值检查 4/4 通过，原版对照 2/2 通过。前进稳态脚底中位滑移 4.277 cm/s、左前 5.375 cm/s；两方向松键/idle 中位滑移 0。左前启动中位值 42.106 cm/s，仅通过原版相对限制，不能说所有窗口都低于 25 cm/s。

**视觉验收失败：新 Walk 稳态出现明显展臂。** 源和转换后的上臂方向角一致：前进中位约 65°，原版约 23°；左前约 70°，原版约 21–26°。源上身本身已偏离自然摆臂，转换没有将这个角度凭空放大；运行层叠加对最终展臂程度的影响尚未单独闭环。接下来需核查 ALS 参考姿态到 SOMA 中性姿态的约束映射，并增加上身姿态门禁后再生成。不能用脚底/位移数值通过代替最终动作视觉通过。

![同帧原版与新 Walk：启动、稳态、停止](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/walk-release-screenshots-v6/forward-original-new-comparison.png)

[Walk 对照数值](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/walk-release-screenshots-v6/summary.json)、[上身 FK 审计](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/upper-body-visual-audit.json)。额外两项默认 Run 松键诊断 2/2 场景通过、36 张截图，但 Run 使用缺失身份的原版回退，不能作为新 Run 验收。

## Viewer 与当前边界

批次 `nonprop-regeneration-20261009-v1-e57f09f835b2` 保留真实源 generated/master，并合并同批 V4 目标 retarget/root/runtime/fbx 阶段，共 358 个预览条目，其中 244 个 V4 目标阶段条目。目标包与源输入路径绑定检查会拒绝把旧批次接到本批；负向拒绝记录见 `viewer-wrong-batch-negative.log`。六域请求更新 pack 摘要后再次刷新 Viewer，避免预览指向旧字节版本。64 个 Refactored 目标 FBX 已实际导出/回读，尚未作为上述 V4 Viewer 阶段条目展示。

本机 `/api/stages` HTTP 200 已验证；本轮内嵌 Browser 到 Windows localhost 的连接失败，未声称本轮 Viewer UI 截图通过。上述截图来自真实 Godot 渲染并实际检查。

矩阵完成后重新核对 1455 个发布载荷，0 个摘要变化；最终 pack SHA256 `6798d316744d7dde6b0794448ad33768b1326ada52314bdf6254d31cb90d83cc`。原活动指针仍为 v8，SHA256 `21d5dc2c1c87143ae6ca86dbbc0795677fd71a32a15425143dfdd46194b039ec`；不自动激活质量失败包。当前仍缺 58 个源身份、目标支撑检查失败、上身视觉失败，离线后端也尚未加入接触保持重定向 IK。距离匹配播放控制、完整新跑步验收与全套新资源人工验收没有完成。

[整链汇总](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/full-pipeline-result.json)记录 `pipeline_complete_rejected`、`activated=false`；[最终载荷审计](../../artifacts/kimodo_als/nonprop-regeneration-20261009-v1/offline-final-payload-audit.json)保留矩阵后的字节级证明。Python/PowerShell 入口与文档均已修改，未提交或推送。
