# 地面全局输入更新与统一候选历史（第八十八批）

承接第八十七批的正式宏、状态默认值及步幅/倍率函数，将 UpdateMovementValues
放到 BaseLayer 的全局输入阶段。站立/蹲伏使用同一次计算结果，地面和空中共用
Lean 历史；输入候选与姿势候选一起提交或取消。此步属于 P3/P4 和所需 P5A
前置能力，完整 BaseLayer 尚未替换生产 Demo 的 Standing 入口。

## 来源、顺序和门控

新增 `AlsGroundedUpdateCompiler`，检查正式输入文件内 UpdateMovementValues
的 31 个非注释节点、变量/函数归属及 31 条计算和执行连接。执行顺序为：

1. 计算 VelocityBlend，四通道分别插值并写回。
2. 使用新 VelocityBlend 计算 DiagonalScaleAmount。
3. 计算并写回 RelativeAccelerationAmount，再由其 UE Y/X 分量构造 LR/FB
   目标，对共享 LeanAmount 插值。
4. 更新 WalkRunBlend、StrideBlend、StandingPlayRate、CrouchingPlayRate。

编译器同时核对 UpdateGraph 的 Grounded 分支、ShouldMoveCheck 和条件宏的
WhileTrue 连接。WhileTrue 来自 true 分支序列的第二个输出；DoOnce 控制的是
ChangedToTrue/False，并不限制每帧 WhileTrue。这次只实现这条地面输入执行链，
没有把宏的切换回调、旋转更新和完整 UpdateGraph 标记为完成。

`AlsGroundedAnimationInputModel` 使用实际速度、加速度、加速/制动上限、角色
旋转、调用方提供的网格竖直缩放及上一已提交帧反馈。初始值来自正式 CDO。
地面 ShouldMove 为真才更新地面输入；地面停步保留其余地面值和 Lean。
空中保留地面值，包括源图未在空中重写的 ShouldMove，由空中模型更新共享
Lean。落地从这份共享历史继续插值，不恢复另一份地面旧 Lean。

反馈按名称读取 Mask_LandPrediction、Weight_Gait、BasePose_CLF，保留存在性并
验证已提交帧身份。仅这些源 GetCurveValue 消费者在曲线缺失时使用零，未将
所有曲线的“缺失”和“存在且为零”合并。重复消费曲线名、未提交或跨身份的
反馈会被拒绝。

## 映射入口与旧入口

`AlsBaseLayerFrameRuntime.PrepareFromFrame` 在来源相关性判定前准备全局输入。
即使 Slot 隐藏整个来源，允许更新的全局输入仍继续；节点自身的来源时钟、
缓存和过渡历史继续遵守其相关性规则。

Standing 接收统一的 VelocityBlend、WalkRunBlend、Stride 和 StandingPlayRate，
这一入口绕过旧局部输入的重复插值、再次归一化与零值前向回退。Crouching
接收同份速度权重、Lean、Diagonal、Stride 和 CrouchingPlayRate。原姿势消费者
所需的其他归一化不在本次删除范围内。

提交前，CommittedGroundInput/CommittedGlobalInput 保持原值；取消或晚期姿势
失败不发布候选。两个子运行时验证提交后再一并提交输入历史。

生产 Worker/Demo 仍使用旧 Standing 更新入口；其完整来源库与新共享定义已
加载，但尚未消费这个统一 BaseLayer 输出。旧入口保留用于回归，不能据此
宣称生产角色已消除重复输入计算，也不能宣称滑步、交错步或上身姿态修复。

## 验证

- 地面新增 13 项加已有空中 13 项专项通过：覆盖 30/60/120 Hz、初始插值、
  停步/空中保持、共享 Lean、落地续接、具名反馈及非法身份；九类源图变异
  验证顺序、轴、变量归属、宏及 WhileTrue 门控不能被静默替换。
- Core 契约、FrameExchange 和 StandingCycle 相关测试 61 项通过。
- Godot 构建通过，零警告、零错误。
- 映射 BaseLayer 集成 3360 帧通过：地面更新 1350 帧、保持 2010 帧、来源隐藏
  时地面值改变 126 帧、Standing 消费候选检查 3089 帧；144 帧保留总和小于
  0.99 的起步插值权重，未被再次归一化。
- 每帧取消重试一致，12 次最终 Slot 晚期故障、24 个守卫检查通过；79 次状态
  变化、271 个来源隐藏帧、15 次重入、65 个来源事件、6 次跨接收者请求转发。
  空中输入隐藏更新 145 帧、上一帧掩码反馈 14 帧保持通过。
- 未映射 BaseLayer 回归 3360 帧通过，来源事件 51、请求 66，6 次晚期故障及
  12 个守卫检查保持原结果。
- 生产 single/parallel 各 180 帧回归，结果摘要 `21E164D829153157`，完整姿势
  摘要 `CF9225D4DE9B2C8B`。此项证明旧入口未回归，不代表完整新图生产接通。

日志：`artifacts/grounded-global-runtime.log`、`grounded-global-legacy-base.log`、
`grounded-global-production-single.log`、`grounded-global-production-parallel.log`。
映射测试的物理输入与 Slot/Montage 是受控夹具；真实步幅函数的 UE 原生对照
沿用第八十七批 720 组结果，本次未运行完整 UE 角色逐帧对照。
`hidden_end_events=0` 仍不能证明被隐藏的 NotifyState 正向 End 场景已覆盖。

## 下一步和仍未关闭的工作

继续补齐 JumpPlayRate、源图条件切换/旋转所需状态与最终曲线消费者，将
统一 Main Movement/BaseLayer 候选接到真实 Worker/Demo 的晚期提交边界。
真实网格缩放、角色运动快照和最终反馈必须来自该角色的对应帧，不能沿用
测试夹具。随后闭合 YawOffset 角色旋转反馈、动态上身 Layering/Add/LS、Aim、
脊柱/手部以及脚部锁定/pelvis/平台策略，再执行真实移动多帧图像和支撑脚
轨迹验收。

P5A 通用动作/真实 Montage、P5B 全部 Overlay/道具、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera 和 P7 十分钟性能仍在原计划内。
音频暂缓。没有修改 UE 插件或资产，没有提交、回退或合并现有工作区修改。
