# 分层帧真实输入与生产 Worker 接线

日期：2026-09-13。第一百二十八批，承接统一帧所有者。

## 更新边界

新增 `IAlsBaseLayerOuterGraph` 边界：BaseLayer 先完成原全局 Ground、Air、
Aiming、Jump、Idle/Turn 与蒙太奇属性更新，随后把同一候选快照传给外层。
外层准备 Aim、LayerBlending 与 Overlay 输入，返回 BaseLayer 缓存的实际
更新上下文，再开始来源图更新。帧身份和 delta 不允许被外层修改。

`AlsAimLayerFrameStage` 新增直接接收全局 Aiming 状态的入口，生产不再独立
执行第二次 aiming 插值。独立受控组合仍可用原观察值入口。

`AlsLayeredAnimationFrameRuntime.PrepareFromFrame` 以自己的上一已提交最终
曲线生成移动反馈，并把全局属性映射到 Overlay：

- VelocityBlend 直接使用本帧 Ground 属性。
- RelativeAcceleration 在现有 Ground 模型中是 Godot 局部轴；读入原图
  X/Y/Z 引脚时映射为 `(-Z, X, Y)`，比例值不做厘米缩放。
- LandPrediction 使用本帧 Air 属性，IsMoving 使用既有原始移动阈值模型。
- BasePose、EnableTransition、RotationAmount 等历史曲线仍来自上一已提交
  最终输出，不读取本帧尚未完成的 BaseLayer 曲线。

受控输入与真实输入不能在同一个所有者内切换。任一阶段失败时，所有子
候选一起取消；初始化历史、反馈和全局属性随最终帧提交。

## 生产接线

`AlsProductionMovementRuntime` 现在可以实际持有统一分层所有者，使用
223 players / 257 samples 的共享绑定，并将手部 IK 后的完整局部姿势写入
真实 Skeleton。脚部曲线、下帧移动反馈和角色朝向反馈均改为读取其最终
曲线。Worker 已传入真实 character/generation，替换角色时新实例不会
复用另一代的上身历史。

新路径启用时，旧组件后处理关闭其额外 Aim 混合，保留既有 Foot/pelvis
处理，避免同一帧叠加两次 Aim。公共移动 phase 的选择排除 Overlay 来源；
Turn 诊断仅识别实际 Turn 资产，不把 Overlay 动态过渡当作 Turn 资产。

当前通过显式参数 `--layered-frame` 在真实生产适配器/Worker 中启用。
默认 Demo 仍未切换，等待最终根生命周期、脚部和视觉验收。该参数是同一
生产路径的阶段性验证入口，不是另写一套只供测试使用的动画求值器。
当前生产默认 Overlay 仍为 Default；完整装备/切换/道具玩法保留在 P5B。

## 验证

- Debug 优化构建：0 警告、0 错误。
- 新真实输入专项：30/60/120 Hz 共 1,260 帧，含 168 帧空中、423 帧蹲伏、
  Rifle/Bow/Barrel 切换、32 个来源事件；逐帧取消/重试一致，10 次异常自动
  回滚。检查 Ground 读取上一最终反馈、Aim 与单次原输入模型结果一致、
  Overlay 各轴与落地预测来自本帧快照、最终曲线提交后原样反馈。
- 原统一所有者对照 1,260 帧保持：最终骨骼、曲线、同步和事件逐项一致。
- 新生产 single/parallel 各 600 帧通过，包含 Grounded、Jump、Fall、Land、
  Crouch 与角色代际替换。两模式 result=`946C9F387EA43F26`、
  fullPose=`6B39A65B68F3FAE3`、root=`A4F6C26CBAB8A0E7` 相同；
  28 个事件，lag/stale 为 0。新上身改变了姿势与后处理工作计数，不能要求
  与旧 BaseLayer 的 result/pose 摘要相同。
- 新生产并行晚期事务失败通过，运行时、结果、控制器和姿势均回滚。
  事件发布晚期失败通过：候选事件 1，泄漏回调 0，随机/事件身份状态保持。
- 原生产 single/parallel 各 600 帧仍保持 result=`EAAF62E4D0A80A76`、
  fullPose=`EE519FBE375F4A2B`、相同 root、28 个事件和零 lag/stale。

证据文件：

- `artifacts/layered-input-mapped.log`
- `artifacts/layered-input-owned-regression.log`
- `artifacts/layered-input-worker-single.log`
- `artifacts/layered-input-worker-parallel.log`
- `artifacts/layered-input-late-transaction.log`
- `artifacts/layered-input-late-event.log`
- `artifacts/layered-input-legacy-single.log`
- `artifacts/layered-input-legacy-parallel.log`

`layered-input-worker-single-first.log` 是场景参数解析器尚未接纳新开关时
的中间失败，后来已修正；不能当作最终结果。生产输出中的历史
`LAND_PREDICTION_FRAME_INPUT_OK ... animation=complete_base_layer` 标签尚描述
的是 LandPrediction 所在的全局/BaseLayer 输入更新；最终姿势所有者应以
`MAIN_MOVEMENT_BINDING_WORKER_OK ... owner=layered_through_hands` 为准。

## 未关闭项目

本批证明真实输入和生产发布已可运行，不证明完整移植或视觉验收完成。
接下来继续最终根初始化/重入及跨求值缓存生命周期，闭合最终 Foot IK、
Foot Lock、pelvis、平台处理，切换默认 Demo，并进行 UE/Godot 同输入、
同脚相位多帧及人工验收。当前上身、侧移换髋、交错步、起步滑步问题仍
保留，不能用 headless digest 代替其视觉结果。

原 P5A 剩余项、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/Camera、P7 十分钟性能预算继续推进。
已知全 Core 23 项失败、Import 分配稳定性及 p95 2.559ms > 2.5ms 未关闭。
音频继续暂缓。本批未修改 UE 原生插件或新增 UE 全图输出探针。
