# Mantle 生产后手部 IK 修复与 Godot 验证

本批在主目录 `F:\Github\Godot-Locomotion` 实现并烘焙生成后的手部蒙皮修复。复用 2026-10-09 v4 的实际 Kimodo 交付，不重新生成、不以原版整段动画替换、不启动 Unreal，不使用 computer-use。稳定设计见[修复合同](../architecture/kimodo-mantle-hand-skin-repair.md)。

标准 1 m Low 的九个候选 Root 关闭检查通过原有完整蒙皮／支撑门禁，移动 Low 也通过。其他高度、取消、High 回退和 Root 开启仍有失败；整包 `runtime_failed`，保持诊断状态。以下分别记录已通过与仍失败的范围。

## 输入和最终制品

作者 bundle 为 `artifacts/kimodo_als/mantle-generation-20261009-v4/full-pipeline/authoring-bundle.json`，SHA256 `0a0e7229c2920e8e79caf83c13c5081f388fd5db08160431f5bc0deabef03cc8`；receipts SHA256 `0c88379f634e924905675e93df1dcd3a58b47a12a9c4a42033843d41b2a4c43e`。两种真实 Low 对应 Refactored/V4 共四个身份。High 未通过当时的源质量门禁，仍在缺失清单中，不作为本次生成交付。

最终新包为 `assets/generated/als_nonprop_20261010_mantle_hand_v5/resource-pack.json`。原 `als_nonprop_20261009_mantle_v4` 包及活动指针保留；验证进程显式选择新包。新包仍为四个替换身份的诊断包，其他 179 个非道具源缺失，原版回退明确列出。未提交／推送／正式激活。

`conversion-v5/pipeline-result.json` 与 `conversion-final.log` 保留完整纯脚本重定向、目标腕部 IK、指节／接近／释放修复、Root/Pelvis 分解、逐资产 Root 曲线、最终 Mantle 控制烘焙及四个最终 FBX 的独立逐骨逐键回读。最终四个 target gate 与 FBX 回读通过，各资产仍为 46 键、30 Hz、1.5 s。

| 身份 | 修复前关键帧手部峰值 | 修复后关键帧峰值 | 最大腕部纠正 |
| --- | ---: | ---: | ---: |
| A_Als_Mantle_Low_Left | 6.863 cm | 0.525 cm | 6.225 cm |
| A_Als_Mantle_Low_Right | 8.213 cm | 0.525 cm | 10.000 cm |
| ALS_N_Mantle_1m_LH | 6.863 cm | 0.525 cm | 6.225 cm |
| ALS_N_Mantle_1m_RH | 8.213 cm | 0.525 cm | 10.000 cm |

指节最大纠正约 38.06°／42.35°，小于 45° 上限；固定掌面投影下的过渡手部峰值约 0.493 cm。支撑期腕部纠正为零。独立交付审计逐轨确认所有平移和缩放、Root/Pelvis、腿／躯干／自由侧旋转未变化；最终 root 所有权、IK 辅助关节、FBX 和曲线由同一矫正后的轨道重新产生。

最终包 SHA256 `ebe46b6687f41242ca98fdba81e79d9fb36ab5824a4aff797c5c1dc639f92ce9`，830 个载荷逐字节审计通过。旧 v4 SHA256 仍为 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`，其 829 个载荷也逐字节保持；数量是本批观察，不是固定库存门禁。详见 `delivery-audit.json`、`delivery-audit-final.log`。

这里的约 5 mm 残差保留在报告中，不能表述为零穿模，也不能由这些关键帧数字直接宣称运行通过。实际运行采用既有完整蒙皮 2 cm 门槛及独立腕部／掌面门禁。

## 真实 Godot 结果

`runtime-final` 执行默认 `full_pipeline.verify_runtime`：Optimize 构建 0 警告／错误、Godot 资源导入、P2B 编译、六类请求及绑定最终化完成。实际运行的是 `mantling_demo_smoke.tscn`，通过普通 Demo 入口、Jolt 查询、共享 Montage、角色提交和最终 Skeleton 写入观察完整蒙皮。另有 `ordinary-demo-final.log` 的普通 `als_demo.tscn` 新包 180 帧启动，退出 0、无 ERROR；该次启动不等于人工完整攀爬验收。

验证结束后另行启动普通交互 Demo，`ordinary-demo-interactive.log` 确认显式选择 v5 新包、Root 关闭；Godot 游戏进程具有 `Godot ALS (DEBUG)` 窗口，错误日志为空。该进程留供现场查看，默认资源指针仍保留 v4。窗口启动不作为人工完整动作验收。

`runtime-final/mantle-contact-static/results.json` 的 11 个基本运行场景都完成既有动作断言；其中九个候选 Root 关闭的全部几何／接触／朝向／身份检查通过：

| Low 支撑手与模式 | 30 Hz 峰值 | 60 Hz 峰值 | 120 Hz 峰值 |
| --- | ---: | ---: | ---: |
| 左手，Default，并行 | 0.703 cm | 1.492 cm | 1.483 cm |
| 右手，Injured，并行 | 0.493 cm | 1.531 cm | 1.507 cm |

60 Hz 单线程 Default 为 1.492 cm，其全部帧 JSON 与并行逐字节一致。GPU Default／Injured 也分别为 1.492／1.531 cm。九项的完整支撑 wrist 位置误差最大约 0.000384 mm，掌面误差 ≤0.000032°，不可达残差为零，静态 actor yaw 为 0°。这些峰值包含接近、支撑、释放和结束阶段，没有只截取支撑区间。

另两项照实失败：Root 开启候选峰值 3.320 cm，仍有支撑时序差异；原版 Root 关闭 GPU 对照峰值 3.787 cm。不能因为原版对照失败就提高候选门槛。

`runtime-final/mantle-contact-platforms/results.json` 的七个运行／策略检查通过，但完整蒙皮仅 moving-low 通过。保留每项结果：

| 场景 | 完整蒙皮峰值 | 几何结果／含义 |
| --- | ---: | --- |
| moving-low，1 m | 1.469 cm | 通过；平台局部 wrist 漂移约 0.0457 mm |
| short-low，0.75 m | 3.516 cm | 失败；进动作混合期间 middle_03_l，接触权重约 0.62–0.84；完整支撑后约 0.493 cm |
| cancel，0.75 m | 20.090 cm | 失败；取消后 ball_l 的恢复姿态穿模，手部控制已解除 |
| destroy，0.75 m | 3.516 cm | 失败来自销毁前同一短台阶进动作；销毁后手部控制解除 |
| no-hand-lock，1 m | 3.111 cm | 预期负对照失败；证明离线资产修复仍需平台支撑定位 |
| high，原版回退 | 11.736 cm | 失败，不计为生成 High 交付 |
| moving-high，原版回退 | 11.627 cm | 失败；平台局部支撑漂移约 0.127 mm，不能覆盖释放手等蒙皮失败 |

静态 4,320 帧及额外 2,520 帧，共 6,840 个诊断帧 committed/presented 身份一致；两份 runner 的冻结输入检查均保持不变。五个局部 E2E 基本动作场景完成，但诊断包缺失 179 源身份，整体 E2E 保留失败。上述其他真实几何失败也继续阻断 `runtime_passed`，没有正式激活。

保存三组各 60 张 GPU PNG，共 180 张。有限查看候选左右支撑的请求 tick 60／78 四张；相位截图用于视觉审查，不声明与同名物理 tick 严格逐像素同步，也不作性能证书。对比图与两侧动画预览分别为 [`before-after.png`](../../artifacts/kimodo_als/mantle-repair-20261010/before-after.png)、[`low-mantle-ik-preview.gif`](../../artifacts/kimodo_als/mantle-repair-20261010/low-mantle-ik-preview.gif)。完整美术观感、所有台阶高度／入口混合、取消恢复和 Root 开启接触仍需后续验收。

## 回归和失败保留

`python-tests-final-v3.log`：42/42 通过，覆盖实际生成手部、身体／偏移／缩放不变、支撑腕部固定、过渡掌面投影、45°／10 cm 预算、不可达输入拒绝、门禁报告不能扩大阈值／省略过渡，以及既有 Mantle、控制曲线、Root 所有权和离线管线。新增拒绝检查没有放宽原门槛。

补充 `hand-tests-deterministic-final.log`：5/5，通过同一真实输入再次修复的完整 raw 语义摘要与报告相同，包含 skin 子集缓存已暖后的求解；不是增加五个不同验收用例。

前几轮证据保留在同一证据根目录：

- `runtime-v1`：支撑和释放已改善，接近仍约 3.75 cm；运行不合格。
- `runtime-v3`：30 Hz 两侧通过，60/120 Hz 在一半接触权重处约 2.42–2.48 cm；运行不合格，触发固定掌面下的过渡指节约束。
- `als_nonprop_20261010_mantle_hand_v4`：严格过渡投影发现释放键约 4.70／5.11 cm，四个 target gate 保留失败；后续让非支撑期手臂负责实际抬离，再求满足固定掌面基底的有界指节，保持原预算。
- `conversion-v2` 为中断的预算试验，未作为最终制品；未删除覆盖该试验目录。
- 初始 `low-probe-v1/v2` 缺 compiled 资源／运行绑定摘要不一致；正常执行 P2B 和六域请求后关闭，未复制旧 compiled 资源或跳过绑定校验。
- 第一轮局部 E2E 参数使用不存在的场景名，保留 `runtime-v1/e2e-matrix.log`；后续实际五个准确场景名执行结果单独保留。诊断包源闭包不完整仍返回失败，局部 `_OK` 不提升为正式全矩阵通过。

## 复现

证据根目录为 `artifacts/kimodo_als/mantle-repair-20261010`；最终转换、测试与运行分别使用独立日志。Windows 使用项目 .NET 8 SDK、隔离 ufbx 依赖和现有 Godot 4.7.2 Mono。

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe -m tools.kimodo_pipeline.offline_pipeline `
  --authoring artifacts/kimodo_als/mantle-generation-20261009-v4/full-pipeline/authoring-bundle.json `
  --receipts artifacts/kimodo_als/mantle-generation-20261009-v4/receipts.json `
  --output artifacts/kimodo_als/<fresh-run>/conversion `
  --pack assets/generated/als_nonprop_<fresh-version> `
  --kimodo-repo G:/Github/kimodo --deps artifacts/kimodo_als/offline-python-deps
```

完成 Optimize 构建、资源扫描、Godot `--editor --import`、P2B 编译和六域运行绑定导出后，用以下脚本选择新包直接运行既有完整 Demo 的攀爬场景：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-kimodo-mantle-scene.py `
  --pack res://assets/generated/als_nonprop_20261010_mantle_hand_v5/resource-pack.json `
  --output artifacts/tests/<fresh-run>/mantle-static
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-mantle-contact-cases.py `
  --pack res://assets/generated/als_nonprop_20261010_mantle_hand_v5/resource-pack.json `
  --output artifacts/tests/<fresh-run>/mantle-platforms
```

默认完整生产验证入口 `full_pipeline.verify_runtime` 自动执行这些导入与检查。`runtime-final` 使用该入口，五个局部基本动作场景为 `mantle-{30,60,120}-low-off`、`mantle-60-high-off`、`mantle-60-low-on`；专用 runner 继续独立执行两侧 Low 三种 Hz、单／并行、GPU、移动／高度／取消／销毁和原版对照。所有失败继续传播，正式启用保持既有完整门禁。
