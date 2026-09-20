# Standing YawOffset 输入与曲线写入

日期：2026-09-12。第七十批，工作区 `../GodotALS-p5a-events-actions`，
分支 `feature/p5a-events-actions`。保留既有未提交工作，本批未 commit、revert 或合并。

## 原计划归属与实际改动

本批对应补完计划 A 中的方向 ModifyCurve、YawOffset 数据流，属于 P3/P4 的
基础移植完整性，不是额外新增玩法。当前尚未完整等价，继续补齐所缺语义。

`tools/unreal/export_yaw_inputs.py` 只读导出两个 CurveVector、UpdateRotationValues、
UpdateGraph、EventGraph、移动条件宏、MovementState 枚举及四个默认值。
正式数据位于 `assets/config/v4_yaw_inputs.json`。曲线使用原生键，不把 0.25 度
采样表当作运行时近似查表。36 个有效轴键的 InterpMode 默认均为 Linear；
保留源数据中的非对称值，不按想象中的“标准 ALS 数值”改写资产。

严格编译器核查原生引脚 ID 和双向连线：FB.X/FB.Y 分别给 FYaw/BYaw，
LR.X/LR.Y 分别给 LYaw/RYaw。
输入为 `NormalizedDeltaRotator(Velocity.ToRotation(), Character.GetControlRotation()).Yaw`。
UpdateGraph 在 Grounded 的 ShouldMove 真分支执行 UpdateMovementValues 后执行
UpdateRotationValues；宏的 WhileTrue 是条件真时每次执行的分支，不是 ChangedToTrue。
四个初始值均为零。Godot 按其坐标系转换符号，使用同帧实际速度与 command ViewYaw，
不拿 CharacterYaw 或平滑 AimRelativeYaw 替代控制器角。

Core 新增限定归一化角域的原生线性曲线求值器。全局四分量停止更新时保留历史；
六个方向节点仅在其 Update 访问时捕获各自分量，重初始化保留 CurveValues。
状态姿势的 MultiWayBlend 后写 YawOffset，然后执行普通过渡曲线混合和外层 Idle
混合。ModifyCurve 使用 `current + alpha * (new - current)`，保留端点浮点运算，
不存在的曲线也会插入，包括零值。没有用骨骼 ChangeDirection profile 去加权曲线。

Godot 实际 Standing 方向缓存消费这些状态值；Cycle、Detail 和 Main 的曲线布局
均可携带 YawOffset。输入与骨骼/曲线候选一起回滚，不新增独立可提交时钟。

## 验证

- UE 完整 Editor target 构建与三插件审计通过，构建状态指纹仍为
  `848B287B80EB5ED57BA738D464221067E2D606509C7AC16794E8DF67D1CB20B8`。
  构建/审计日志前缀：
  `../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260911T162227813Z-7066cadcdc9642d2be8e5ef35abb9a19`。
- commandlet 最终导出退出零，报告零错误、零警告，未保存资产。2882 个原生向量
  采样覆盖 -180 到 180 度；5764 个有效分量与 Core 求值逐项相等，最大差为零。
  此结论限定当前导出点，不等于完整动画图或任意浮点输入的逐位等价证明。
- 普通 D3D12 Editor 重启、再次导出及正常关闭退出零。两个 CurveVector 的完整
  数据与采样完全一致；导出的旋转图、更新门控与宏再次通过同一严格编译器。
  原始图文本并非字节一致：普通 Editor 加载后不再包含部分未连接的 BreakVector、
  BreakRotator 等节点。保留两份原文，不抹平差异。启动日志仍有两条既有
  `LogAutomationTest: Error: Condition failed`，不称为无错误启动。
- 新 Core 专项 4 项，连同方向输入/来源曲线专项 22/22；Import Yaw 专项 14 项，
  含原生采样、连线/门控/数据变异拒绝及坐标边界；方向编译器再增加 4 项写入合同。
  全套 Core 常规 1950/1950（沿用排除 P5A golden/schema 的入口），Import 1197/1197。
- 优化 Godot 构建零警告、零错误。真实资源 30/60/120 Hz 共 1260 帧，六方向及
  6300 次曲线检查通过；210 个初始候选均为参考姿势、空来源曲线与存在的零
  YawOffset。36 次分支检查、六次故障重试、已提交曲线恢复及采样零分配通过。
- Standing/Detail/Pivot/Sprint 回归通过，方向回放仍含九次换髋和 371 个等待帧。
  Main 六缓存入口 1680 帧、2360 次原始姿势检查及故障恢复通过。
- Worker 单/并行各 180 帧一致：结果 `21E164D829153157`，完整姿势
  `CF9225D4DE9B2C8B`，每模式十个来源事件。晚期来源事件及整体事务回滚通过。
  摘要保持不是最终朝向已消费新曲线的证据；本批没有改变角色朝向反馈。

TRX：`artifacts/test-results/standing-yaw-inputs/`。
Godot 日志：`artifacts/standing-yaw-cache.log`、`standing-yaw-cycle.log`、
`standing-yaw-main.log`、`standing-yaw-worker-single-final.log`、
`standing-yaw-worker-parallel-final.log`、`standing-yaw-worker-rollback-final.log`。
UE 日志：`artifacts/ue-yaw-inputs-complete.log`、`ue-yaw-inputs-editor-restart.log`；
Editor 再次导出：`artifacts/ue-yaw-inputs-editor-repeat.json`。

保留首次导出 UTF-16 解码失败、C# Math 命名空间歧义构建失败和旧 Worker 快照
断言失败。Worker 断言修正为同时比较新增同帧相对角，没有去掉原输入一致性检查。
一次只读 PowerShell 最大误差统计误用了只读 `$Error`，该次统计作废；更正任务
变量后得到上述零误差，不影响已经通过的原生编译验证和测试。

## 本批没有关闭的工作

YawOffset 已产生并携带，但最终图曲线到角色朝向的反馈仍未接通；因此不能宣布
横移侧身已经修好。旧未求姿势的扁平曲线查询不包含完整图内写入；外层曲线
presence、完整 Main/Demo owner、真实 Slot/Montage、最终惯性化仍需闭合。
全局 Yaw 当前由 Standing owner 保存，尚未统一站姿/蹲姿之间共享的 AnimInstance
变量历史；其他方向图的独立输入、完整计数/重入/更新上下文也继续核对。

下一阶段先闭合主图所有权，再用最终已提交曲线反馈角色旋转，继续完整动态
Layering/Add/LS/Lean 和 Foot IK/Foot Lock/pelvis。P5A-P7 的原玩法与最终验收保留，
音频暂缓，已确认的键鼠不改。本批无新增 UE 完整图逐帧轨迹、人工移动截图、
全套 P4 路线或十分钟性能采样。起步滑步、换髋视觉、上身、平台脚锁和性能门禁
仍未关闭，组件通过不替代 Demo 视觉验收。
