# 非道具 ALS E2E 与四方向截图验证

本批按用户要求把普通前后左右移动作为验收重点。结论是：导出造成的身体位移已修复，但新资源仍有显著滑步，不能验收正常移动。v11 仅通过环境变量接入真实测试进程，未写默认包指针；默认仍为用户此前激活的 v8。该旧包也存在本次发现的问题，不因保留指针而被重新认证。

## 范围与真实环境

在主目录 `F:/Github/Godot-Locomotion` 使用 Godot 4.7.2 Mono、.NET 8 与 UE 5.8 GASP58 原生重定向。保留全部已有修改；未提交、推送、创建 worktree 或修改旧原生对照阈值。完整制作脚本的步骤在本次分阶段实跑，未声称从零重跑全部神经网络生成：复用 48 个本目录可追溯的候选绑定，重新执行必要的程序化依赖和原生重定向。

测试包为 `assets/generated/als_nonprop_20261007_v11/resource-pack.json`：183 个非道具动画身份，70 个道具身份保持原字节；816 个 payload 和 34 个独立根运动曲线，245 份原依据未变。相对 v8 重新原生重定向 84 个身份，其余 99 个复用经哈希校验的输出。依据见 [资产审计](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/asset-integrity-final-before.json)。Distance Matching 仍仅备曲线数据。

## 已确认的故障与修复

1. **原地循环残留裁剪起点位移**：循环只减首尾位移差，未减裁剪片段的起点 XZ 常量；Walk/Run/Sprint 及 Pose、Lean、Acceleration、风格表和镜像继承数米偏移。新增最终骨盆到根距离断言后，v9 在 walk/forward 约 2.438 m 失败。`motion.loop_candidate` 修正原点后沿 DAG 重做 80 个输出，再执行真实 UE 重定向。
2. **非循环冲刺起步仍携带行进轨迹**：v10 的 sprint/forward 最终骨盆距根约 3.782 m，新断言失败。新增 `in_place` 节点，按生成时同一 linear/accelerate 数值路径逐帧扣除平移，保留动态起伏；重做两个起步输出形成 v11。不能用线性首尾差替代加速路径。
3. **根运动关闭误关玩法通道**：旧策略同时关闭了 Roll/P5 的公共玩法状态，导致 `recovery-auto-landing` 在 64 项矩阵中失败。修正为玩法事件与采样开关独立，关闭只不提取／消费根运动；原动作断言不变。局部三项复验通过，包括自动落地和开／关根策略。历史 [失败矩阵](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v9-v2/summary.json) 和 [修复复验](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/repair-hard-landing/summary.json) 保留。
4. **Roll 候选的固定根约束不产生玩法行程**：重新生成向前总行程 1.8 m 且末端姿态随路径移动的一个 master，两身份原生重定向；没有放宽完成后角色移动超过 1 m 的既有断言。

首次原生重定向输出父目录缺失、诊断场景无渲染信号却解绑、v9／v10 四方向失败和中途终止的 v11 矩阵均保留独立日志。最后一项是发现滑步后主动终止的旧验证驱动，不具有完整证书。

## 普通移动与截图

`AlsDirectionalMovementSmoke` 从普通 `AlsDemoEntry` 进入，使用真实 Input 单例与玩家输入 owner；胶囊、运动图、统一时钟、提交和最终蒙皮沿用普通路径。仅将测试地形换为相同平面，方便度量。

walk/run/sprint/crouch/aim-walk/aim-run/aim-crouch 各前后左右，共 28 段，每段 240 个 60 Hz 物理帧；稳态为 phase90–209。三个独立进程分别单线程、并行和真实渲染。检查实际速度与输入投影、站蹲、帧身份、最终骨盆距离、蒙皮脚底穿地和持续双脚悬空。

完整 v11 矩阵中，三进程均完成 6720 帧；身体／物理字段逐帧相同。稳态最大水平骨盆偏移约 8.01 cm，脚底最低穿入约 0.000033 cm 数值噪声。每段在 phase120/135/150/165 保存真实 Godot PNG，共 112 张；加上 idle、站蹲／动作与翻越，本轮保存 210 张截图。显示异常门禁通过，不能由此声称步幅或滑步通过。

![行走、跑步、蹲走四方向近景](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/directional-movement-closeups.png)

[完整运行报告](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/summary.json)、[逐帧对照](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/directional-report.json) 和四张 28 格截图表保留。同一固定相机下的静帧用于观察姿态；滑步判断使用连续蒙皮采样，不能从单张截图推断脚速度。

## 仍然失败的足部质量

同一程序、同一平面、输入和物理频率读取原版动画，作为实际运行对照。计算连续两帧均接近地面的最终足底质心表面相对速度：最低支撑点低于 2 cm，质心距地面低于 5 cm，取稳态速度中位数。此指标是近地滑步代理，不是接触力或完全锁脚判据。

| 场景 | 原版 cm/s | v11 cm/s |
| --- | ---: | ---: |
| 行走前进 | 0.87 | 56.19 |
| 跑步前进 | 3.35 | 215.87 |
| 跑步后退 | 4.99 | 261.16 |
| 跑步左移 | 14.91 | 323.62 |
| 跑步右移 | 15.27 | 340.04 |
| 蹲走前进 | 1.47 | 18.20 |
| 蹲走左移 | 3.07 | 154.74 |

以 `max(25 cm/s, 原版同场景中位数 × 2)` 拒绝严重回归，三个进程均为 28 段中 24 段失败，4 段通过这个宽松的回归拒绝门槛；通过项仍不等于无滑步。所有段有足够近地采样，未用零样本通过。移动状态完全 planted 的采样为零，也没有将其当成零漂移合格。参见 [足部对照及输入哈希](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/directional-quality.json)。

残余问题已经超出位移导出错误。生成候选／任意时长裁剪闭环并未保证支撑期足部速度匹配 ALS 原地 motor；原生目标 raw 的脚轨迹也有较大残余速度。同步制作仅按每脚最低高度选 marker，缺少交替支撑语义：WalkForward 的左右间隔原版约 50% 周期，新包仅 2.94%；RunForward 约 50% 变为 20.83%。这是生产算法缺少步态语义检查的明确证据，不能仅修改播放速度或 marker 就宣称修复全部滑步。见 [相位诊断](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/sync-phase-diagnostic.json)。

离线尝试 Kimodo MotionCorrection 和复用 Core 双骨 IK 的原生校准。前者源空间近地速度变差并产生循环接缝，后者改善部分脚速度但仍有约 5.71 cm 不可达残差，未达到实际验收。它们仅保留在 ignored 证据目录，没有进入生产、覆盖 FBX／raw 或加入运行脚锁来掩盖资源问题。

## 发布门禁与回归

生产脚本已串联 67 项 E2E 和实际 GPU 截图。runner 另外启动原版四方向对照，验证三个新包进程的身体／物理数据一致，并写足部质量报告及 trace 哈希；即使 67 个运行场景全通过，质量失败仍返回非零。`publish activate` 需要完整矩阵、三帧率并行摘要、同包／同报告哈希，并重新计算足部对照，不能用局部 `_OK` 标记激活有滑步的候选。

Python 38 项回归通过，包含数米原点修复、数值加速路径、缺样本拒绝、严重滑步拒绝及禁止失败包激活。Core Release 相关 100 项通过；优化构建零警告／错误。Import Release 相关 123 项最后一次 118 通过、5 失败，均为 `AlsMantlingHostPoseTests` 原输入 definition digest 不一致，未重写旧夹具或放宽断言。初轮另有一次零分配采样失败，独立三项与整体复跑恢复通过，首轮失败日志保留。

最终完整矩阵：**67/67 运行场景通过，零引擎 ERROR；足部质量失败，runner 退出 1，打印 `ALS_NON_PROP_E2E_FAILED`**。30/60/120 Hz 单线程／并行平台 pose/root/result 摘要均一致。整轮后 816 payload、34 曲线、默认设置和本批二进制／实现快照未变。实际调用 `publish activate` 以失败质量报告作为证据，明确拒绝，包清单与默认指针字节不变。见 [严格日志和发布拒绝复核](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/strict-log-and-release-review.json) 与 [拒绝日志](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/matrix-v11-quality-final/activation-rejection.log)。生命周期 generation 场景的内部受控错误计数为 1，不是 Godot ERROR；没有跳过引擎错误来通过矩阵。

完整矩阵复现命令（本候选应以足部质量失败退出）：

```powershell
& G:/Github/kimodo/.venv/Scripts/python.exe scripts/verify-als-nonprop-e2e.py `
  --pack res://assets/generated/als_nonprop_20261007_v11/resource-pack.json `
  --output artifacts/tests/<fresh-run>/matrix --headless-fixed-fps 60
```

实现／二进制快照见 [snapshot](../../artifacts/tests/2026-10-07-als-nonprop-e2e-v1/implementation-snapshot.json)。矩阵运行中将 runner 的 ERROR 筛选收紧为任何引擎 ERROR 均失败；整轮结束逐日志复核确认为零，不影响已收集结果。真实运行二进制、生成资产和物理／姿态断言没有在运行中改动。

现实帧时尖峰、长时间压力、复杂地形、自然跌倒方向、人工实体键鼠、美术观感及完整旧动画 native oracle 不由此次平面受控测试认证。后续重点应先重制 Walk/Run/Crouch master 的完整步态周期、接触期位移与交替支撑，再使镜像、Pose、风格和加法基底沿 DAG 失效重出；原生目标和普通最终蒙皮都须过同一门禁。
