# 完整姿势初始化与展示就绪（第 199 批）

## 移植完整性判断与顺序

当前不能将“已有资产和部分同名公式”视为完整 1:1 移植。默认 Demo 仍未
切换到完整上身/脚部入口；状态更新、源采样/同步、图内曲线写入、最终混合、
下一帧反馈和主线程展示都必须闭合。现用 ALS V4 资产的 AnimBP 是直接数据
基准，Refactored C++ 是算法参考，两版差异需要记录并逐项配对。

工作仍在原规划内，继续顺序为：

1. P3/P4：完成起步/停止/交错步和 A→D、D→A 不同脚相位的整角色验收。
   核对 Feet_Crossing、HipOrientation_Bias、允许过渡条件、逐骨骼混合，
   以及速度、Stride、播放倍率、同步 marker 与实际支撑窗口。
2. P4：完整上身 Layering/Add/LS、Aim/Lean、手部与脚部/pelvis 最终输出
   接入默认 Demo，保留现有键鼠回归。展示初始化属于这个接入合同。
3. P5A 剩余通用 Notify/State、Sync/ActionPlayer/Slot 消费者，P5B 全部
   Overlay/道具装备玩法，P5C Mantle/Roll/Root Motion。
4. P6 Ragdoll/Get-up/Pose Recovery 与完整 Camera，最后 P7 十个全质量
   角色、Release 十分钟性能和人工效果验收。音频继续暂缓。

基础移动的缺口不推迟到 P6；实验性动态平台接触改进也不称为原生逐帧等价。

## 本批实现

原实现每次提交都设 VisualReady，即使完整脚部尚无上一帧最终 pelvis，
首帧没有射线/IK 也会显示。现在生产图从当前候选 RigInput 输出
PresentationPending，仅在 ExecuteRig 且 FootTransformsValid 为 false 时
等待。未访问的正常移动分支不要求这个 Rig 就绪；非 Refactored 脚部入口
保持原展示时机。

标志由同一 Worker 候选帧发布，经过原有身份、代际、结果/脚查询校验后
进入主线程诊断。初始化帧仍提交 Motor、曲线、脚部历史、事件及帧号，
只在初始化完成后设置 VisualReady 并显示。未强行改 FootTransformsValid、
跳过动画帧或移动组件原点。角色替换仍先完成代际恢复并恢复 Gather，
随后由新的完整姿势触发展示，因此不会因等待可见性而卡住下一帧初始化。

## 验证

- 优化 Debug Godot 构建：零警告、零错误；脚部候选帧专项 12/12。
  包括冷首帧、不合法初始 pelvis 延迟、提交和取消历史。
- 真实平台 360 帧：第 1 帧 PresentationPending/隐藏，第 2 帧至末帧
  就绪且显示。独立捕获同时保留未显示的首帧骨骼、完整足部蒙皮与命中。
  与第 198 批基线除新增展示字段和进程对象身份之外，全部字段完全相同。
  第一帧鞋底仍为 -20.845 mm，首次显示帧两脚最低点约为 0；这是正确展示
  初始化状态，不是将首帧几何穿地或起步滑步物理修复。
- 完整生产重建测试 single/parallel 各 960 帧，均观察到创建、替换各一帧
  pending，其后正常显示。每次最多一个可见角色；旧代际拒绝一次；lag、
  stale、missing 为零，321 个脚锁帧、37 个事件，双模式摘要完全一致：
  result `334ABDF9B965A46B`，full pose `3F518B1D4B79AAC5`，
  root `3C8B520C47ECA5C7`。
- BaseLayer 原入口 180 帧重建通过，pending 为零。
- 完整脚部 parallel 第 13 帧发布前故障注入通过：结果未发布，姿势、
  控制器、脚锁/Rig、最终历史与同步状态回滚，未遗留 pending 查询。
- 最初 180 帧完整脚部短场景验证了两次展示初始化，但脚锁覆盖为零，
  整体退出 1；保留该日志，随后按已有完整覆盖选项跑 960 帧，未放宽门槛。

证据：`artifacts/presentation-199-initialization-report.json` 及其
`.pose-comparison.json`；`presentation-199-capture.json/.log`；
`presentation-199-replacement-full-{single,parallel}.log`；
`presentation-199-base-replacement.log`、`presentation-199-late-failure.log`。
分析器为 `tools/diagnostics/analyze_presentation_initialization.mjs`，只剥离
明确新增的 Presentation 字段，复用原有逐字段比较器，不排除其他差异。

本批没有新增渲染截图、UE 运行对照、十角色或性能验收。未启用默认完整
入口、未改变原 ALS 计算和实验接触策略；未提交、回滚或清理其他修改。
下一项是起步/停止/左右换髋的完整移动接触窗口，而非继续扩大静止平台
测试来代替动作效果验收。
