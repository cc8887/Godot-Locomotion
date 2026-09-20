# 站立方向循环与换髋：本轮实现和验收

本轮在 `feature/p5a-events-actions` 工作区实施，未提交；保留此前未提交的 Lean 基姿势修复及其他工作区修改。原问题尚未全部关闭，尤其是起步滑移。

## 实际启用的改动

- `p4_locomotion_demo.tscn` 现在加载 `p4_cycle_locomotion_profile.json`。旧 `p3_locomotion_profile.json` 和 P5 编译布局保留兼容，不把新图冒充为旧 P5 occurrence layout。
- 导入器解析六套原始 WalkRun 混合空间，编译 F、B、LF、LB、RF、RB 的 WalkPose/Walk/RunPose/Run 四个端点，并将 Pose 资产加入实际加载闭包。
- 站立图不再走通用 Idle 中心缩放的方向圆环。每方向使用 Stride 和 WalkRun 权重，再按四向 VelocityBlend 合成；Idle 作为独立起停混合，Sprint 仍为单独分支。
- 接入 Feet_Crossing 和 HipOrientation_Bias 的采样入口、六方向状态、完整状态权重条件、0.75 秒 ChangeDirection 曲线。右向到左向先保持 RF/LB 同髋组合，再在许可窗口换为 LF；反向对应 LF/RB 到 RF。
- 依据 UE `UBlendProfile::CalculateBoneWeight` 的 WeightFactor 语义实现腿部倍率 2；15 个指定腿部/IK 骨骼与躯干使用不同归一化过渡权重。
- 步幅来自原始 StrideBlend_N_Walk/Run；读取当前混合姿势的 Weight_Gait 计算播放速率。动画速度来自已有设置，使用原始片段时长计算循环推进，不再把每个源动画都当作一秒时长推进。
- 当前站立采样调用已有 `AlsSyncRuntime.TryEvaluateGroup`，使用 Left/Right 标记映射各循环片段的采样时间。图和脚部曲线消费者读取同一个准备帧；基础混合贡献不再强行压成三个动画 ID。
- 新图的相位、VelocityBlend、等待状态和换髋进度存入准备/提交值状态；回滚恢复图的采样参数。空中分支保留上次站立循环状态。
- 新 Demo 不再执行此前的 0.2 秒 HipBias 插值与统一方向角速度限制；旧布局的兼容路径仍保留这些旧逻辑。

## UE 数据验证

使用 `ue-diagnosing-plugin-build-load` 规定的完整 Editor target 构建和插件审计后，运行只读导出。Python 无法读取受保护的 FloatCurve，因此使用 UE 原生 ObjectExporterT3D，保留原始文本、插值、切线、边界模式和逐骨骼配置，没有保存 UE 资产。

正式数据：`assets/config/v4_locomotion_curves.json`；脚本：`tools/unreal/export_locomotion_curves.py`，输出路径由 `ALS_LOCOMOTION_CURVES_OUTPUT` 指定。四条曲线各保留 201 个 UE 原生采样校验点。当前消费其中三条，共 603 点在 Godot 图初始化时校验，误差上限 0.00005，覆盖 T3D 文本浮点精度。DiagonalScaleAmount 的两个原始键与 Oscillate 边界已导出，但尚未接入运行时对角补偿。

## 回归结果

- `dotnet build GodotALS.csproj --no-restore -v quiet` 通过。
- Import 测试 523 项通过，包括旧 P5 布局和新的六方向 Pose 资源闭包。
- Core 测试排除 Golden/TraceSchema 的组合通过；该过滤不代表外部 UE trace/golden 验收已执行。
- `standing_cycle_smoke.tscn`：30/60/120 Hz，每种频率三种换向时机；9 次换髋，222 个等待帧，9 次姿势及周期状态回滚，Turn 返回 Idle，稳定控制器路径 0 B 托管分配，603 个曲线校验点通过。
- 原 P4 图、脚部单线程/并行、晚期事务失败回滚及组件 Aim/Turn/Rotate 回归通过。组件 Aim 压测改为每次先恢复动画基姿势，符合实际“先求动画，再做一次加法修正”的调用顺序，不再在同一骨架上累计一万次加法旋转。
- 渲染回放：步行横移、普通 Alt、快速 Alt、跑步横移，各 720 帧、120 张截图。最大单帧脚旋转分别为 20.049、14.674、16.598、14.046 度，均通过原 30 度突跳门禁。跑步场景不作为原地转身覆盖证明；原地转身由步行/独立图测试覆盖。
- 已人工查看横移换向、跑步和前后对比联系表。镜头与输入文件 SHA256 与修改前一致。

最终主要证据位于 `artifacts/cycle-verified-strafe`、`artifacts/cycle-final-alt`、`artifacts/cycle-verified-rapid` 和 `artifacts/cycle-verified-run-complete`。这些文件被 Git 忽略；可用可视烟测和 `analyze-p4-movement-capture.ps1` 重新生成。

## 未通过项和剩余边界

起步滑移尚未验收通过。`measure-p4-cycle-capture.ps1 -CaptureDirectory artifacts/cycle-verified-strafe -MaximumStartupSlideCentimeters 3` 明确返回失败：起步 61 到 72 帧中，低位脚的水平单帧位移峰值为 6.824 cm，旧回放约 10.95 cm。3 cm 是本轮显式设置的工程验收门槛，不是 UE 官方标准；下降约 38% 不等于没有滑步。

两次实际换髋从 frame 292 和 499 开始，相对于 A/D 输入切换约等待 0.85 和 1.30 秒，Feet_Crossing 许可条件满足；这不是固定按键延迟。

本轮还不具备完整 ALS 等价性：

1. Idle/Moving 仍是简化混合；ShouldMove、Stop/Pivot、Feet_Position 的 Foot Up/Down/Plant/Lock 状态未移植。需要继续处理支撑脚选择与起停过渡，不能靠继续加大方向平滑掩盖起步滑移。
2. 普通方向转换目前统一使用 0.5 秒 Cubic；完整源图中各边的 0.5/0.7 秒配置和所有优先级/可中断规则尚未逐边复刻。0.75 秒换髋门控与骨骼 Profile 是已接入的重点。
3. 同步使用稳定的 Walk F 参考时钟和已有标记映射器；尚未接入完整 P5 Runtime 的动态 leader、Notify/Action、事件权威与新图 occurrence 布局。旧 P5 回归通过只证明旧绑定未被破坏。
4. HipOrientation_Bias 采样入口存在，但当前基础移动图没有完整 Overlay gameplay 合成，所以默认仍为零。不得宣称武器/Overlay 偏向已完成实机验证。
5. 动态分层曲线、DiagonalScaleAmount、YawOffset 反馈、蹲姿原始步幅图、完整动作/Root Motion，以及十分钟性能预算仍属于后续工作。新图零分配测试不代表总 CPU/GPU 预算已经达标。

因此本轮应认定为“方向混合与曲线门控已接入、换髋延迟恢复、起步滑移改善但未关闭”，不能认定为整套 ALS 移植或上述问题全部完成。
