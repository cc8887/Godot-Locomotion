# 冻结回放与当前 V4 转身配置分离

在 `D:\GodotALS` / `main` 延续上一批整合，未创建新工程或 worktree。
本批处理旧 P4 蹲姿转身回归及 P5A 冻结计划失配，不修改用户的未提交规划文件。

## 原因与实现

9 月 12 日对 `v4_idle_control_inputs.json` 的原生图/CDO 检查已确认：
ALS V4 四个蹲姿转身资产的 ScaleTurnAngle 为 false，四个站姿为 true。
该修正改变了 P5 图摘要，也改变了通用参考设置的蹲姿开关。
但两个旧回放入口仍把修正后的配置当成历史数据使用。

P4 夹具的来源由加载器锁定为 ALS-Refactored，而不是当前 V4 AnimBP。
本机锁定源码 `AlsTurnInPlaceSettings.h` 默认允许角度缩放，
`AlsAnimationInstance.cpp:2038` 按该开关乘以请求角度/动画角度。
冻结的 `port_oracle_v1` 使用该缩放规则。新增显式 `CreateRefactoredReference()`
供这个受来源约束的回放入口使用；V4 的 `CreateReference()` 和正式资产配置保持原规则。
四项新测试在 ±60°、±150° 蹲姿验证：选片、相位、基础播放速率保持一致，
只有两套来源规定的 YawScale 不同。

P5A v1 计划锁定的图摘要为 `44403C2869D8F615`；当前 V4 摘要为
`FD59AB9C657B60DD`。原有独立小端写入器已经证明这两者恰好相差四个蹲姿缩放位。
布局 `F2336240D749284B`、运行时绑定 `40F33E59692DFD38` 保持一致。

新增独立 `AlsP5aFrozenReferenceCompiler`，仅供历史 Oracle 和对应测试使用：

1. 用正常生产编译器编译并验证当前配置；要求版本、资产定义、布局、绑定和当前图摘要全部精确匹配。
2. 在新的数组中重建历史四个缩放位，重新计算真实历史图摘要，构造独立的冻结快照。
3. 再次精确校验历史快照。无法识别的变化直接拒绝，不把当前图重新标记成旧摘要。

普通 `Compile`、`CompileSourceAware` 的公开接口及默认行为保持不变。
测试独立重算历史图/绑定摘要，验证恰好四位变化、生产对象不被修改，
并用合法的 Presentation Yaw 变化确认历史入口仍拒绝无关图变更。

没有改写任何原始 UE 输出、portExpected、冻结计划、schema 或数值容差；
没有重新运行 UE。历史 P5A 回放恢复不等于新完整图的原生认证完成。

## 验证记录

证据目录：`artifacts/reference-replay-20260920`。

- P4/转身/P5A 事务回滚专项：112/112，通过 `core-focused.trx` 记录。
- Core Release 初次全库：2530 通过、1 失败、0 Skip，总计 2531，耗时约
  1 小时 46 分钟，见 `core-all.trx`。P5A 两个历史回放/Schema 测试类的
  31 项全部通过；唯一失败为下述零分配测量，不是历史回放失配。
- Import Release 全库：2265 通过，1 项原有 Skip，0 失败，见 `import-all.trx`。
- Godot 优化 Debug 构建：0 警告、0 错误；普通完整入口键鼠回放 360 帧通过，
  83 帧实际脚趾锚定，见 `keyboard.log`。
- 初次全库并发运行中，共同 Montage 的零分配测试记录了 3544 字节，
  同组单独复跑 7/7 通过。该组加入已有的非并行 Allocation 测试集合，
  保留原 300 帧热身、1000 帧测量及零分配阈值，不修改运行时以迁就测量。
- 重新编译隔离调整后，除上述两个 P5A 长测试类之外的 Core 全部复验：
  2500/2500 通过、0 Skip，耗时 1 分 18 秒，见 `core-final-non-p5a.trx`。
  零分配断言在该轮通过。最终覆盖为 P5A 31 项通过，加其余 Core 2500 项
  复验通过；不是将首次带 1 项失败的全库记录改写为一次全绿。
- 新增拒绝测试最初选择了本就不合法的 RateScale 变更，未到达目标校验；
  已改为可通过普通编译器的 Presentation Yaw 变更，Import 全库确认通过。

P5A 全库包含大量 CLI 反例检查，例如 Family13 在 371 个非法帧位置逐一
注入回执，每项分别检查输出不存在和已有输出不可覆盖；此外还有完整 JSON
结构变异矩阵。修复身份校验后，这些断言不再提前退出，因此全库耗时显著
增加。普通迭代应先运行受影响的 family；本批因恢复整个历史入口执行全库。
本次耗时最长的三项分别为 Family13 约 30 分 36 秒、Family5 约 30 分
1 秒、Family6 约 26 分 36 秒，均通过。
后续完整回归建议增加 `--logger "console;verbosity=normal"` 并将输出重定向
到证据日志，以便看到每项完成情况；默认 minimal 输出只在失败和结束时报告。

## 下一项实际接线

`AlsProductionMovementRuntime.CompleteEvents()` 已发布共同 Montage 的
`ActionOutcomes`，但 `AlsFrameResult.ActionPlayback` 仍保持默认值。
下一项是定义当前动作与淡出实例的可观察关系，并把真实物理实例的身份、
时间、权重和结束状态接入事务提交及失败重试验证。不能只把播放命令发出
当作完整 Roll 玩法已完成，也不能把多个物理实例重新压成同一个播放身份。

地形全程接触、起停/换髋和上下身人工验收，以及 P5B–P7 保持原范围继续推进。

后续动作摘要接线与验证见 [共同 Montage 动作摘要记录](2026-09-20-action-playback-summary.md)。
