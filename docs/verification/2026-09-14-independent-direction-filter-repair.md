# 独立方向 BlendSpace 历史补完（第 186 批）

## 结论与规划归属

这是 P3 移动图与 P5A 独立来源历史的完整性修复。相同资产和公式并不代表
运行时等价：此前六个 WalkRun BlendSpace 共用输入滤波历史，而 UE 每个
节点持有自己的 BlendFilter。新方向首次参与混合时错误地继承旧方向的
步幅/步态历史，改变采样权重、同步位置、最终曲线和停止选脚。

本批已经修复生产图，未增加输入延迟、角度限制或强制脚锁，也未调整验收
阈值。实际跑步横移的 720 帧/120 PNG 回放通过，最大单帧脚旋转由 41.10°
降至 13.266°。这关闭该固定回放中的突转复现，不等于所有脚锁、上身、
平台和完整角色已经人工验收。默认完整入口仍未切换，P5A–P7 继续保留。

## 首个分歧与源码证据

新增 `--production-graph-capture`，在真实 Motor/Worker 同一候选帧中记录
全局输入、MainMovement/BaseLayer/PreFoot、曲线、来源身份/时间/权重及
停止通知和 Montage。快照随姿势一起提交，取消不发布；未开开关时不创建
这些诊断对象。采集前后原 720 帧输入、结果、脚部/曲线/锁历史完全相同。

旧实现与原始 UE 图在第 247 帧首次分离：方向过渡权重均为约 0.00325926，
但 compiled node 202（WalkRun_BL）的归一化时间为 0.650656879，UE 为
0.698426962。差 0.047770083 是**归一化位置差，不是秒**。同一帧来源权重
相同，UE 新节点使用当前 Stride=0.21862252；Godot 错用了旧节点滤波结果。
错误继续反馈，最终第 610 帧 Godot 选择 Stop L，UE 选择 Stop R。

引擎依据：`AnimNode_BlendSpacePlayer.cpp` 的 Reinitialize 清理自身样本缓存
并初始化自身 BlendFilter；`BlendSpace.cpp` 的 TickAssetPlayer 使用本次
Instance.BlendSpace.BlendFilter 过滤输入，再生成样本权重。

修复点：Standing 候选帧增加六份有界值类型滤波历史；初始化清空，只有
实际访问的来源推进，未访问的来源保留。Forward 同时遵循 Sprint 分支
访问条件。来源 Tick、缓存姿势、曲线和直接采样对照读取同一方向的数据。
旧全局 Filter 字段保留作旧诊断展示，已不再决定这些生产采样结果。

## 验证与准确边界

- 构建通过，0 warning/0 error；14 项滤波/方向输入测试通过。
- 实际 720 帧共 2280 个已提交 Tick 与 UE 对照：归一化时间最大差 0，
  权重最大差 2.98023e-7。该检查不证明所有未访问节点或 evaluator 等价。
- 第 1–685 帧 MainMovement/BaseLayer：最大位置差 0.000042817 cm，
  旋转差 0.000068045°，共享 V4 曲线差 2.38419e-7；原门槛全部通过。
  两边第 610 帧同选 Stop R，第 611 帧开始播放，前 685 帧播放完全相同。
- 第 686 帧 Godot 开始 TurnIP_L90，现有 UE 探针只分发 Stop。它还会影响
  Stop 淡出，因此第 686–720 帧姿势及后续 Montage 不在本次配对通过范围。
  原全量比较器仍报告 35 帧姿势失败，没有覆盖或放宽。
- 六个 Godot Refactored 专用曲线（FootLeft/RightIk、FootLeft/RightLock、
  PoseGrounded、PoseMoving）在 V4 图没有同名输出。完整曲线检查仍失败；
  有限范围报告明确列出这些未比较项，不能据共享曲线通过宣称整图一致。
- 30/60/120 Hz、左右方向共 2100 帧受控完整 V4 图，逐帧取消重试、最终
  姿势/曲线和停止生命周期均通过；该输入与第 185 批原生对照兼容。
- 60 Hz 单角色并行：360 帧、两次取消、一次提交等待通过。
  10 角色 Single/Parallel 各 3621 帧，同样通过且姿势/结果摘要完全相同。
- UE 四插件部署审计通过，复用第 185 批完整 Editor 构建，未改 UE 插件。
  冷启动与普通 Editor 各 720 帧退出 0，解析后完整输出相同。
  普通 Editor 仍有两条既有 LogAutomationTest Condition failed，非干净日志。
- 旧 `standing_direction_cache_smoke` 失败：测试用导入骨架构造参考数组，
  当前运行时输出使用 RAW 逻辑骨架（运行时 79 骨、旧测试 68 骨）。保留最初 IndexOutOfRange 日志，并加
  明确长度诊断；该旧测试没有计入本批通过。旧 Core 23 项失败亦未清账。

检查了实际第 246、252、618 帧截图；截图检查不替代 UE 后处理及人工验收。
六份历史增大候选帧体积，十角色短测通过不代表 P7 十分钟性能预算已通过。

## 可复查产物与下一项

- 修复前：`artifacts/movement-186-sources/`、`movement-186-source-clocks.json`。
- 修复后：`artifacts/movement-186-independent/`（含输入、各阶段和 120 PNG）。
- 原生：`movement-186-independent-native.json`、`-editor.json`。
- 对照：`movement-186-independent-clocks.json`、`-main.json`、`-scoped.json`。
- 回归：`stop-full-186-independent-parity.json`、`-lifecycle.json`，
  `graph-dispatch-186-independent.log`、`graph-dispatch-186-ten-*.log`。
- 有限范围比较工具：`tools/diagnostics/compare_production_direction_replay.mjs`。

接下来补 UE 整图 Turn/Rotate 的实际请求与播放消费者，对齐停止→转身的
完整后段；继续相同输入下上身、脚部/平台及接触窗口对照。通过后再切换
默认完整入口。随后按既定 P5A 通用动作、P5B 全 Overlay/道具、P5C Mantle/
Roll/Root Motion、P6 Ragdoll/Get-up/Recovery/Camera、P7 顺序推进。
