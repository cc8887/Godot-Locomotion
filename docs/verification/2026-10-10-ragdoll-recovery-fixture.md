# 自动 Roll／Ragdoll 恢复夹具的接地前置条件复核

## 根因与修正

用户提出测试本身可能有问题。本轮使用原版、当前 Air crouch 默认包和 Land／Roll v4 候选，在同一真实 Godot 场景、60 Hz、Root 开启与并行配置下复现。旧测试在物理模拟满 2 秒后立即断言接地；它没有等待接地事件，也没有证据说明任意生成姿态都应在这个固定时刻进入骨盆接地探测范围。

原版和原默认通过，v4 在第二轮失败。第二轮胶囊高度均为 1.12 m、半高 0.56 m、半径 0.35 m、地面顶面 0 m。v4 在第 120 个物理步骨盆约 0.5919 m，尚未进入该接地探测范围；前一帧存在 17 个接触点且骨盆速度约 1.278 m/s，仍在运动。接触点数量包含不同物理接触，不能直接等同于运行时 `RagdollGrounded`。

独立观察保留旧 2 秒失败，继续模拟后，v4／v5 分别在第 127／128 步观察到连续 6 帧接地，两轮起身均 accepted=2、completed=2，普通移动恢复及空中退出断言通过。观察进程刻意仍退出 1，并标记 `no_acceptance=true`；没有把这次诊断改记为原测试通过。旧失败实际上是未满足固定时刻的夹具前置条件，不能据此断言起身动作或恢复算法损坏。

`CharacterRagdollRecoverySmoke` 现保留 2 秒最短观察期，在原有全场景 22 秒预算内等待实际接地，再发送退出／起身输入。空中退出仍使用原 2 个物理步并要求未接地；接地、PlayGetUp、胶囊碰撞恢复、动作与通知所有权、恢复后的移动、重复循环及失败注入断言均保留。没有修改生产 Motor、Core 物理、接地探测范围、曲线、源资产或风格规则。

增加两个只供复核的可选参数：`--audit-ragdoll` 输出状态；`--legacy-ground-checkpoint` 重现旧固定时刻检查。默认自动矩阵不使用旧检查参数；保留其他已有用户修改。

## 验证范围

优化构建通过，0 警告／0 错误。v4 的全部 17 个恢复案例，以及普通入口、Root 开／关和两种 Flail，共 **22/22** 相关案例通过；资产载荷与动画设置未变。另有 30／120 Hz、60 Hz Root 关闭单线程的自动 Roll 恢复 **3/3** 通过。60 Hz Root 关闭单线程第二轮在第 122 步（约 2.033 秒）实际接地并正常恢复，旧两秒时刻的失败并非恢复能力缺失。

旧检查模式仍在同位置失败。负对照在胶囊确实完成启用并进入 Ragdoll 后，将原地面移到本轮预算不可达处；测试在既有 22 秒预算内以 `Recovery stalled in stage 1, cycle 0` 拒绝，未出现成功标记。前两次负夹具试运行亦保留：第一次移除过早而卡在 Roll 准备阶段，第二次落到移到 -1000 m 的地面后被原恢复断言拒绝，均未记为接地超时证明。最终采用 -10000 m 的独立负夹具；生产场景、运行机制和超时值未改。

部分 e2e 包装器退出 1，原因是未选择方向覆盖，`failed=[]`、相关案例 22/22；原完整运行／方向门禁保持未通过。所有原日志和失败矩阵均保留，没有将新测试结果覆盖旧检查结果。

最终已默认选择 `als_nonprop_20261010_landing_prompt_combined_v4` 诊断包，包含 v2 的 8 个新 Land 身份与 v4 的 2 个完整贴地 Roll 身份；保留当前 Air、Walk、Idle、Mantle 等来源及 70 个道具。1,536 个载荷摘要、123 个最终 FBX 回读证据与相对 v2 的 121 个未变运行源重新核对通过；Air 11 项是 v2 实际证据的有界复用，本轮没有重跑 Air 11 项。

默认资源扫描及 Viewer 入口已同步，清除 ALS 环境覆盖后的普通 `als_demo.tscn` 重启 180 ticks 通过，确认实际选择新包。未提交、未推送。本轮不会将修正夹具或诊断默认接入解释为完整生产验收：完整方向／运行矩阵、60 个非道具源缺口、完整蒙皮／人工／长时间／性能验收仍开放。

## 证据

- `artifacts/tests/2026-10-10-ragdoll-fixture-audit/results.json`：旧检查的三包对照。
- 同目录 `original.log`、`default.log`、`candidate-v4.log`：胶囊、物理接触、骨盆状态和真实失败。
- `candidate-v4-settling.log`、`candidate-v5-settling.log`、`observation-fixture.cs.txt`：明确不产生验收结果的继续观察与其源代码。
- `legacy-checkpoint-after-fix.log`：修正后的旧检查反例。
- `e2e-after-fixture-fix/`、`rate-mode-results.json`：新夹具的相关运行及 Hz／执行模式对照。
- `no-ground-negative-timeout.log`、`no-ground-negative.gd`：无法接地仍被既有超时拒绝；首轮 `no-ground-negative.log` 与第二轮 `no-ground-negative-validated.log` 保留试运行失败。
- `candidate-v4-delivery-audit.json`：1,536 个载荷、123 个 FBX、相对 v2 保持的 121 个其他运行源和 70 个道具。
- `verification-summary.json`、`default-selection.json`、`selected-default-restart.log`：当前夹具摘要、默认选择与普通无覆盖重启。
- [原候选生成及其历史失败](2026-10-10-kimodo-landing-prompt-regeneration.md)。
