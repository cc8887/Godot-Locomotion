# 第二十二批：外层状态运行时与 Stop 子图接线

## 本批完成内容

上一批为实际 Demo 补入 ShouldMove。本批继续补完整性恢复计划 A/B 的上游状态语义，
复用已有原生输入导出，未重新导出 UE、修改插件或保存源资产。

新增严格编译器 `AlsGroundedMachineCompiler`，读取三台机器的编辑器图及编译状态表：

| 机器 | 状态（含 Conduit） | 边 | 每帧最多转换 | 首次更新跳过混合 |
| --- | --- | --- | --- | --- |
| Main Grounded States | 8 | 18 | 1 | 是 |
| (N) Locomotion States | 5 | 12 | 3 | 是 |
| (N) Stop States | 7 | 6 | 2 | 否 |

共 20 状态、36 条边，不是新增 20 个动画播放器。编译器保留退出顺序、源节点路径、
原生 PlayerNodeIndices 和通知索引；按共享规则的实际 BoundGraph 取表达式。
不能用编辑器节点遍历顺序替代编译后的退出优先级，也不按相同起终点合并自动/普通边。

运行时 `AlsGroundedStateMachine` 已实现：

- Conduit 的进入条件和终端边选择，保留终端边的时长/混合类型。
- 上一帧记录的状态权重与机器权重；状态重入清相关性、间断相关性重置。
- 首帧仍查找转换和触发状态事件，只跳过混合，不误实现为“不允许转换”。
- 复用既有活动转换栈，覆盖中断、重入时长缩短和新过渡完成后的清理。
- 自动退出使用相关播放器的原始累计时间与真实 Tick delta；循环跨界判定及
  CrossfadeTimeAdjustment 已实现，不误用 rate-adjusted remaining。
- 同帧刚进入的状态不使用被清除的旧相关性来立即自动退出。
- 状态进入/退出/完全混入及过渡开始/结束/中断输出到值类型候选；本地候选不分发事件。
- 惯性化请求、清理前子状态更新顺序、上下文权重与初始化集合供调用者消费。

新增 `AlsStopMachineGraph`，把 Stop 子状态机接到现有 12 个固定 Plant 采样：
Pre-Stop/Lock 保留调用方的 Detail 骨骼，Lock 写入对应 FootLock=1；Plant 按原有
Mesh Space 分支覆盖，姿势和曲线使用同一活动转换权重。左右选择器随值类型候选
保存，重入/相关性重置时重新初始化；没有增加播放时钟或独立事件队列。

## 源语义核对

源文件仍为 `assets/config/v4_locomotion_inputs.json`，SHA-256：
`C63DDF1BC78AC9B2B84F048CFAF16E4C41E514F88011FCB2CE55600ACAB2B956`。
其 UE 导出和重复校验记录见上一批，不把本轮 C# 测试称为新的 UE 原生运行轨迹。

- Not Moving → Moving：0.3 秒 HermiteCubic。
- Moving → Stop：上一帧 Moving 权重为 1 且 !ShouldMove，零时长；优先于 QuickStop。
- Moving → Not Moving：!ShouldMove，0.2 秒，触发 `->N QuickStop `（保留原名称尾空格）。
- Stop → Not Moving：上一帧 Stop 权重为 1，0.3 秒。
- abs(Feet_Position) >= 0.5 走 Foot Down → Lock；小于 0.5 走 Foot Up → Plant。
- 负数选择左脚，正数选择右脚；恰为 0 时没有合法终端边，保留 Pre-Stop。
- Main 的 Roll→Idle 自动边还带 QuickFeet profile；编译器保留该骨骼混合标识，
  没有把它静默改成普通全身混合。本轮仅实现标量状态运行时，尚无其最终骨骼消费者。

同时对照当前本地 UE 引擎代码：

- `AnimNode_StateMachine.cpp` 的 Update_AnyThread、FindValidTransition、TransitionToState，
  确认首帧事件、每帧转换上限、自动时间修正和过渡/子状态更新顺序。
- `AnimInstanceProxy.cpp` 的 GetRecordedStateWeight 从读缓冲取值，RecordStateWeight
  写另一个缓冲；不能在本帧刚切到 Stop 后用新权重立即穿越所有后续条件。
- `AnimNode_SaveCachedPose.cpp:PostGraphUpdate` 挑选最高上下文权重，严格大于才替换，
  同权重保留最早调用；仅更新一次，并通知被跳过的上下文。此缓存调度尚未接入 Demo，
  是下一步 Detail/Standing/Stop 接线必须遵守的合同，不可权重相加或重复 Tick。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Godot Debug 构建 | 0 错误、0 警告 |
| Core 常规 Debug | 1626/1626，排除 AlsP5aGoldenTests、AlsP5aTraceSchemaTests |
| Import 完整 Debug | 673/673 |
| 新 Core Release | 15/15 |
| 新 Import Release | 15/15 |
| Stop 真实资源组件 | 30/60/120 Hz，machine_bones=71400，候选重试与原地输出一致，alloc=0B |
| 既有 Plant 验证 | sample_components=4608，bone_checks=2448，selectors=246，curves=72 |
| Detail 联合组件回归 | bone_checks=71400，retries=1050，requests=38，resets=6，alloc=0B |
| Standing 联合组件回归 | source_timing=15216，timing_authority=1260，movement_frames=5040，alloc=0B |

测试覆盖快速起停、完整停止、原生状态权重时序、脚位正负及边界、Main 的 Roll 入口
优先级和 Action Conduit 门控、自动播放结束/循环、被清相关性的旧时间、过渡中断、
候选重试/事件顺序、惯性化时的旧状态更新、机器身份、非法值与零分配。
编译负例覆盖错误机器策略、状态通知、权重来源、Detail 缓存和 FootLock 写入。

首次编译校验拒绝了 Main 的 QuickFeet profile，随后补入明确标识，并加入保留测试；
未放宽成任意 profile。中断测试最初只推进一个很短的起步帧，原生重入时长缩短使
过渡当帧结束；测试改为先进入可观察的中段后验证重叠，不修改运行时去迎合错误预期。

## 尚未完成

本批是三层状态数值运行时和 Stop 子图的可执行接线，**还没有替换 Demo 的外层输出**。
Stop 组件的基姿由测试调用方提供，不是完整 UE AnimBP/角色同步回放。
实际 Demo 仍是第二十一批的临时 MovingWeight 混合，不能据此宣称起步或上身改善。

下一步先建立生产缓存调用/贡献路径，结合真正的 Main Movement/Slot 上游上下文，
把 Standing、Detail、Stop 及惯性化组装到同一 Controller/Worker 候选事务。
源播放器按已有绑定保持独立时间/事件身份；多个缓存引用不能复制播放器或重复推进。
状态通知还需 P5A 统一分发、去重和生命周期，不能直接在数值 Update 中触发 gameplay。
完整 Main 转换姿势、QuickFeet 骨骼 profile、Rotate/Slot 消费也仍需要接线与原生回放验证。

本轮没有修改相机/键鼠、没有新的可玩 Demo 多帧截图或滑移结论，没有执行 P5 全矩阵、
两个长耗时 Golden/TraceSchema 套件及十分钟最终预算。没有提交 Git 或回退用户修改。
