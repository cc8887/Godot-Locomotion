# 方向状态事件与 Pivot 生产反馈

日期：2026-09-11。工作区：`../GodotALS-p5a-events-actions`。
完整性修复第四十一批。基础移动视觉、完整 P5A 和最终 ALS 验收保持未完成。

## 实际改动

1. 从现有 V4 原生导出交叉编译六方向状态进入事件、24 条转换的事件绑定，
   校验六条 Pivot 转换、髋枚举顺序和 EventGraph 的实际执行连线。
   不把 Core 枚举顺序当作 UE 的 TrackedHipsDirection 顺序。
2. Pivot 使用来源配置的严格速度比较与 0.1 秒 Delay。重复通知重新设置 Pivot，
   但不重启正在执行的同一个 Delay；没有增加通用输入延迟或禁止转换中断。
3. 方向进入事件先于转换开始事件；跳过首帧转换混合仍保留状态进入，但不产生
   Pivot 转换开始事件。事件按顺序保留，不按通知编号去重。
4. 实际 Standing/Detail 消费上一帧已提交 Pivot；本帧图更新产生的事件与延迟结果
   保存在候选值中。Stop 消费事件更新的上一帧髋方向，不再每帧直接映射方向枚举。
   离开 Standing 后继续推进待完成的延迟，不冻结、重启或遗失计时。
5. Controller 提交/回滚包含反馈状态；Worker 的失败检查增加反馈状态比较。
   事件缓冲容量不足时，整对进入/转换事件拒绝，不留下半条转换的修改。
6. 连续截图记录新增 Standing/Detail 状态、Pivot 输入、反馈计时与方向事件，
   可和同一帧的来源时间、通知、骨骼输出联合检查。

## 来源与边界

- 依据现有 `v4_locomotion_inputs.json`、`v4_locomotion_source_graph.json`、
  `v4_locomotion_detail_graph.json` 和本机 UE 源码。
- `AnimNode_StateMachine.cpp` 的首次更新、TransitionToState 决定生成通知顺序；
  `AnimInstanceProxy.cpp` / `AnimNotifyQueue.cpp` 的直接队列入口不对这类事件去重。
- `KismetSystemLibrary.cpp` 的 Delay 不同于 RetriggerableDelay；`DelayAction.h`
  使用 float 剩余时间逐帧递减。当前在动画图更新之后推进潜伏动作阶段，依据本机
  `LevelTick.cpp` / `LatentActionManager.cpp` 的执行结构，未新增完整 AnimBP
  事件时序原生探针，不宣称已经完成逐帧 UE 调度等价验证。
- 这是实际角色的内部方向反馈，不是所有图生成 Named Notify 的通用 P5 分发完成。
  完整 Main/Slot 事件生命周期、统一正式快照、缓存重入和上游权重仍未关闭。
- Walking 没有直接进入 Pivot 的 Detail 边；Running 才有该出口。实际跑步制动
  过程中 ActualGait 已降至 Walking、但 Detail 尚在 Running 时，仍可按原图优先级
  进入 Pivot。不能仅凭当前步态枚举判断是否应播放 Pivot。

## 失败记录

- 最初编译器把 Custom BlendMode 当成 Custom Transition Logic。实际 .75 秒边
  使用 StandardBlend + Custom 曲线，已纠正编译器，未修改来源资产。
- 最初非 Standing 测试夹具未设置 PlayRate，导致 FallLoop 校验失败；补有效输入。
- 最初仅 Walking 的夹具要求进入 Pivot，实际产生 7 个事件但 Detail 为零帧。
  核对原图后分离 Walking 负例与 Running 正例，没有改变状态规则或放宽角度阈值。
- 保留 `direction-feedback-production.log`、`direction-feedback-production-retry.log`
  和早期集成日志；本批未修改 UE 原始数据、P4 oracle 或既有容差。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Godot 构建 | 0 警告、0 错误 |
| Core 常规，排除独立 P5A golden/schema 套件 | 最终 1848/1848 |
| Import | 823/823 |
| 方向反馈专项 | Core 12/12、Import 8/8 |
| 实际 Controller Pivot | 30/60/120 Hz，5040 帧，正负例、事件顺序、前帧输入、离图延迟及姿势重试一致 |
| Standing / Detail / Sprint | 5040 / 1890 / 1260 帧，通过 |
| 实际 Worker | 单/多线程各 180 帧，摘要一致 |
| 事件 / 整帧晚期失败 | 两种模式分别第 25 / 13 帧，零回调泄漏且状态/姿势回滚 |
| P4 全脚本 | 图、姿势、地形/平台、回滚及零分配检查通过 |

Worker 摘要保持：result `A9DF0647AFC3574C`，full_pose `04D4A5651B87E0E4`，
pose `2DED5435A66BCAEC`，root `309E8D0E0BEEB2CB`。短 Worker 场景未覆盖全部
Pivot 行为，不能用这个摘要代替长 Controller/视觉场景的新增路径验证。

记录：`artifacts/test-results/direction-feedback/`、
`artifacts/test-results/direction-feedback-final/`、`artifacts/direction-feedback-*.log`。

## 连续视觉回放

步行和跑步均为 720 帧、120 张截图，分别位于 `artifacts/direction-feedback-walk/`
和 `artifacts/direction-feedback-run/`。最大单帧脚旋转为 10.743 / 12.546 度，
原 30 度守卫未放宽。已查看跑步 246、252、258、264 帧，画面非空且实际换向。

跑步第 256/436 帧生成 Pivot，第 257/437 帧进入 FirstPivot；该状态共 74 帧。
第 258 帧实际共享来源有 player 15/16、sample 37/38，时间约 .291667 秒，
更新权重约 .418381/.438957，不是只有诊断状态改变。该回放不覆盖 SecondPivot。

起步低脚位移代理仍为步行 7.9046、跑步 9.8983 cm/帧，StartupAccepted 均为 false。
这是代理而不是精确支撑脚滑移；本批未改善起步指标，也不宣布上身和交错步视觉验收。

## 后续实施

1. 接完整 Main/Slot 和通用图状态事件，补真实上游缓存权重及惯性化上下文；验证
   来源重入与 Pivot/潜伏动作在实际 UE 完整图中的时序，避免内部反馈长期成为旁路。
2. 统一全图最终 Weight_Gait、Mask_Sprint、RotationAmount、YawOffset、Stride/速率。
   当前仍有取上一帧 Cycle 曲线而非最终输出曲线的缺口。
3. 补动态 Layering/Add/LS、Lean、上身/髋部和完整 Foot IK/手部 IK，以同输入的
   状态、时间、曲线、姿势及连续截图进行验收，不以测试数量代替视觉质量。
4. 按原计划推进 P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 Ragdoll/
   Get-up/Pose Recovery/完整 ALS Camera，最后 P7 十分钟 Release 性能预算。

未提交、未回退用户修改，未修改已人工认可的相机/键鼠控制；音频仍暂缓。
