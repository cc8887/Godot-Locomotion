# 站立循环末端缺项：对角缩放和 Lean

第一百四十八批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 定位证据与诊断纠正

上一批属于实际进展：修复脚部空间和曲线存在性，但 1,260 帧配对仍有
964 帧姿势超阈值，最大 2.2357 cm / 2.4672°。本批没有放宽位置 .001 cm、
角度 .02°、缩放 1e-5 或曲线 1e-4 的门槛。

`--parity-zero-acceleration` 保留速度/反向/视角，取消显式加速度。受控
UE 配对最大误差降为 .32064 cm / .61268°，仍 964 帧失败。这是诊断
夹具，不是真实角色运动，不作为以取消 Lean 修复游戏的方案。
文件 `full-graph-godot-148-zero-accel`、`full-graph-ue-148-zero-accel`、
`full-graph-parity-148-zero-accel` 保留在 artifacts。

源码核对确认 `(N) Locomotion Cycles` 的输出应为：方向 Linked Layer →
组件空间 ik_foot_root 对角缩放 → 局部附加 Lean → Cycle 缓存。原正式
缓存路径直接返回方向层，遗漏这两个末端节点。来源闭包中已有 Lean，
但之前仅筛选六方向 WalkRun 与 Sprint F 求值/更新，资产存在不等于被消费。

同时发现上一批测试导出器的来源列表错误：`sync.Players` 是紧凑活动列表，
其下标不是 formal PlayerId；`Times/CachedWeights/Epochs` 才按 ID 索引。
此前仅输出活动数量个条目却拿完整来源的同下标标记身份，相关来源时间/
权重结论无效。本批先前基于该数据的“关键来源全部一致”判断也撤回。
骨骼、曲线和输入配对不读取这份来源列表，不受该导出错误影响。

导出器现记录完整 224 个 formal 来源的 ID、时间、缓存权重、epoch 和
是否实际 tick；九个不在该共享来源表内的原生节点不可由此推断缺失。
普通 Cycle 来源的本帧 CachedWeights 也已补写，未访问时保留旧值。

## 正式修改

- `AlsStandingCycleTail` 从原图核对末端连接、曲线驱动模式、缩放模式与
  实际参数，复用原生 Lean 网格和原始动画采样器。
- `Weight_Gait` 读取上次提交的最终曲线，经原版 Scale/Bias、Clamp 和
  不同方向插值速率产生 alpha；初始化、未访问与恢复的历史由 Cycle
  生命周期驱动，存入候选帧。没有在组件成员中偷偷推进已提交历史。
- Lean 使用自己的 PlayerId/epoch，在方向缓存来源之后提交共享 tick；
  权重为 CycleContext.Weight × alpha，采样秒数取共享同步结果。
- Cycle 缓存内先进行组件空间对角缩放，再以 alpha 应用局部附加 Lean
  和曲线。即使 Lean 输入为零，仍采样其中央姿势；不能假定中央姿势
  相对于附加基准严格为零。
- `AlsLeanBlendSpace.Apply` 支持显式附加 alpha，原 crouch/air 默认 1
  保持其既有约定。新用例检查 0/.5/1 对旋转、平移和附加缩放的影响。

本批正式接入点是 Main Movement 的 Standing Cycle 缓存。旧独立
AnimationTree 兼容入口不因此被认定为原生整图等价。

## UE 配对结果：仍未全部通过

最终 Godot 文件 `artifacts/full-graph-godot-148-final.json`。
UE 文件 `artifacts/full-graph-ue-148-tail.json`。两次 tail/final Godot 请求
SHA256 均为 `7C82022194DC0C73C344446FF29B5146B009B7B7AB248F761F6F6F72A7540D6A`，
因此缓存权重补写后的最终捕获可以复用同输入 UE 导出。

`full-graph-parity-148-final.json`：

- 六条轨迹共 1,260 帧，1,230 帧姿势在既定门槛内，30 帧仍超阈值。
- 最大位置差 `.028576905190238255 cm`，角度差 `.0586825146944919°`。
- 30/60/120 Hz 首次失败仍分别为第 16/31/61 帧，主要集中在起步初期；
  首帧可见 spine_02、手指和虚拟膝目标等差异，尚未归因或关闭。
- 曲线 presence 全部一致，最大数值差 `2.384185791015625e-7`。
- 比较器按设计退出 1。修复前后各自与同属性 UE 输出配对，不能宣称两批
  所有反馈属性完全相同，更不能用误差下降冒充全部姿势验收。

新增 `scripts/compare-full-graph-ticked-sources.mjs`，按导出资产清单的
反射属性顺序映射原生节点，检查 Godot 实际 tick 子集。
`full-graph-sources-148-final.json`：4,252 次 tick 时间误差为零，4 次
缓存权重超 1e-6，最大 `.013902425765991211`；在两个 30 Hz 轨迹的
第 61 帧出现。该检查也退出 1，不代表完整原生访问集、采样/通知或姿势
已经等价，剩余权重差继续调查。

UE 启动前完整 Editor/插件构建及审计通过，未修改原生插件源或 DLL。
BuildId `4855b08e-0078-431b-b819-d1a7a48b513d`，日志前缀
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T033004735Z-1a16bfd2f7d34fb8a669f72933657a80`。
两次冷导出退出 0，0 error/0 warning；本批没有重做普通 Editor/打包，
原生二进制与上一批重启验证相同，不能称新夹具已经普通 Editor 复验。

## 回归与真实渲染

优化 Debug 构建 0 警告、0 错误。Lean/落地 Lean/对角缩放专项 63 项通过，
包含零分配验证，TRX `artifacts/test-results/standing-tail-148-final.trx`。

生产 single/parallel 各 960 帧均通过：result=`2B28803B8320B08E`，
fullPose=`B38B5115864A6F67`，sampledPose=`2289B640B5632F8B`，
root=`DB5B813964D3479C`；37 事件，316 锁脚帧、910 偏移帧，lag/stale=0。
日志 `standing-tail-production-single-148-final.log`、
`standing-tail-production-parallel-148-final.log`。
旧摘要断言失败日志保留；新摘要对应正式姿势和来源权重的有意修复，
不是 UE 等价判据。两个线程模式已使用同一新摘要完成所有后续断言。

完整根隐藏/恢复 840 帧通过，195 隐藏、429 混合、18 次晚期失败、每帧
重试，9 次恢复；受控物理和快照未冒充实际 Ragdoll。日志
`standing-tail-root-dispatch-148.log`。晚期姿势与来源事件失败回滚通过，
事件回调泄漏为零，日志 `standing-tail-late-transaction-148.log`、
`standing-tail-late-events-148.log`。

默认 BaseLayer 单线程 180 帧兼容回归通过，75/109 来源、lag/stale=0，
日志 `standing-tail-default-demo-148.log`。默认路径也消费 Standing 缓存，
因此会得到本次末端修复；它仍不包含完整上身/脚部所有者。

真实渲染横移回放完成 720 帧、120 张截图，最大相邻帧脚转角 14.117°，
未触发原有 30° 诊断。已查看连续接触表
`artifacts/standing-tail-visual-148/movement-contact-sheet.png`；可见髋、
腿和上身持续变化，没有在这些采样中观察到整体腿翻转。该观察不证明
支撑脚滑移、换髋延迟或手臂动作完全正确。

## 后续与边界

继续起步 30 帧小姿势分歧、4 次来源权重分歧与完整站立链审计。已确认
原图 Sprint 输入还有按 RelativeAccelerationAmount.X 混合的
`ALS_N_Sprint_F_Impulse`，目前未由正式方向来源采样器消费，需补独立
身份、更新顺序、同步和姿势混合。这是 P3 缺项，不推迟到 P5 玩法阶段。

默认仍为 BaseLayer，完整分层/脚部入口在 UE、实际 Motor/平台支撑及
人工效果验收后再默认启用。P5A 剩余项、P5B Overlay/道具、P5C 动作/
Root Motion、P6 物理恢复/Camera 和 P7 十分钟预算继续保留；音频暂缓。
原 Core 23 项失败、Import 分配不稳定及旧性能超预算未在本批整体关闭。
