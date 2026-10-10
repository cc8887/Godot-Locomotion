# 独立 Kimodo Air pipeline 实施与验证（2026-10-10）

## 结果和范围

已实现与平面移动、Mantle 并行的 Air 制作分支及 `--scope air` / `scripts/produce-air-als.ps1` 独立入口。当前独立图为 32 个节点、3 个生成分支、16 个 V4 / Refactored 交付身份；没有 Stand / Walk / Mantle 前置依赖。完整非道具图也将 fall / flail 路由到同一专用实现。

稳定契约见[独立 Air pipeline](../architecture/kimodo-air-pipeline.md)，不在此重复规则全文。

本次实现专用全身／Hips 三维条件、未经修复源语义检查、有界三维线性漂移分解与周期回归、同一输出与时间域的交付校验、Air 恒定平面原点分解、独立目标 FK 门禁、包载荷及运行／启用前复核。下落及 Lean 不制造地面 FootLock。没有新角色时钟、Root Motion 运行链或 C# 运行算法。

## 实际执行证据

证据目录：`artifacts/kimodo_als/air-pipeline-20261010/`。

- `plan/graph.json` / `graph.mmd`：实际 CLI `plan --scope air` 输出；实际 `inspect` 确认 16 个身份与上述节点清单，externalInputs 为零。
- `final-regression-v7.log`：101/101 相关 Python 测试通过，覆盖 Air、图／Runner／缓存、Root 所有权、发布、离线 FBX／完整编排、批次失败保留及 Mantle／曲线契约回归。
- `sdk-both-skeletons-v6.log`：随后扩展同一 SDK 测试，真实本地 Kimodo 的 SOMASkeleton30 和 SOMASkeleton77 各解析三种 Air 条件；六组均通过，并读回实际 Hips Y。未运行 diffusion 模型。
- `controlled-evidence/summary.json` 与三个动作子目录：实际执行作者→离线重定向→Air Root/Pelvis 分解→目标门禁→FBX 写出→ufbx 全物理关键帧回读。三个受控夹具均通过；每份保存源／目标报告、runtime raw、轨迹、所有权及 FBX 字节摘要。三个周期原时长为 1 秒、1 秒和 0.667 秒，68 根物理骨，共 83 个时间键；各周期全部变形骨首尾位置差为零，Z 反应保留。
- Runner 受控测试交付全部 16 个身份，验证实际源回执、motion 与轨迹摘要；对修改 NPZ 后重新计算文件哈希的负例，数组身份校验仍拒绝。
- 实际发布预检接受满足合同的受控资源包；将其骨盆抬高 100 cm 后，即使重算载荷哈希、伪造通过报告及其输入身份，独立目标几何复核仍拒绝。
- 旧三种 Kimodo 源候选均被拒绝，失败明细保存在 `controlled-evidence/<动作>/old-source-rejection.json`；旧默认组合包三份 runtime raw 也未通过新目标检查。完整坠落、横躺、周期速度接缝、缺失源回执、错误运行前合同等负例通过预期拒绝测试。
- PowerShell 原生语法解析检查通过；Python 编译检查、工作树相关 diff 空白检查通过。

受控夹具是不可变原版参考加确定性的小幅胸部／竖直扰动，用于证明算法、变换和导出链路。它们不是 Kimodo 新生成结果，不是实际游戏验收，也没有进入项目默认包。回读的三个 FBX 同样是受控验证文件。

编排／运行前拦截测试使用 mock。日志中的 `PASS build`、`PASS e2e-matrix` 或预期的 `FAIL mantle-contact-static` 是测试夹具输出，不能视为本次真实 Godot 或 Mantle 场景运行证据。

## 修正与证据边界

首次新增测试中 SciPy 的单轴 Euler 批量参数维度不符，已改为 N×1；这是夹具代码错误。核对真实 Kimodo SDK 时发现 Hips end-effector 不能仅用 `positions` 字段，改为 SDK 要求的 `local_joints_rot` / `root_positions`，并增加真实 30 / 77 骨解析测试。保留后续各轮独立日志，未更改原版对照阈值。

没有本批真实 Kimodo diffusion、新完整资源包、Godot 导入／普通 Demo、持续高台下落／低权重 Flail／预测落地混合、最终蒙皮视觉、长时间或性能验收。当前检查是专用作者语义和 FK 几何／周期边界，不能称完整生产质量验收。

当前默认仍为 `als_nonprop_20261010_mantle_high_combined_v5`；指针未修改。受控证据记录其文件 SHA256 `0d23ae140425928771591519df4b4ce5d31eefb2bea55aeaca25b99f1fb0d8f4`。原错误 Air 资产没有由本轮实现自动替换。

下一步使用独立 Air 入口真实生成并完成完整转换／FBX／Godot 检查；新包必须记录失败、保留非 Air 原版回退、以实际游戏和视觉结果决定是否选择。独立范围源闭包不能关闭整个非道具生产门禁，默认指针与正式启用继续分别执行。
