# 外层 Aim 与脊柱分支组合

日期：2026-09-13。第一百二十五批，承接 Post Layering 三路真实姿势。

## 本批实现

`AlsAimLayerCompiler` 严格编译原 ALS V4 AnimGraph 中从 Post Layering 到
`AnimGraphNode_TwoWayBlend_6` 的段落：

- A 分支：`UseCachedPose_7` → Mesh Space Additive，附加来源是原
  AimOffsetBehaviors，权重为 `Enable_AimOffset`。
- B 分支：`UseCachedPose_23` → LocalToComponent → pelvis、spine_01、
  spine_02、spine_03 四个 ModifyBone → ComponentToLocal。
- 两路由上一已提交曲线 `Enable_SpineRotation` 混合，再送入手部 IK。
- 两个 Use 节点（13、7）都指向 Post Layering 缓存（22）；AnimGraph
  缓存队列仍只有这一个源。不是为 Aim 重新采样一份 BaseLayer。

编译检查包括真实接线、骨骼顺序、组件空间加法、固定骨骼控制 Alpha、
原 alpha scale/bias/clamp、LOD、回调、缓存绑定和根缓存队列。ModifyBone
的 Alpha 在原图中没有暴露成引脚，固定值来自导出的节点属性；初次额外
引脚检查错误地要求该 pin 必须存在，已修正。保留失败 TRX，最终结果见下。

`AlsAimLayerRuntime` 使用既有缓存遍历器选择最大的更新请求，保持相等时
的先访问者。例如两分支各 0.5，上游更新权重是 0.5，不是 1；Aim 来源
另外乘它自己的加法权重。外层分支/加法不相关时，Aim 不更新也不采样。

脊柱使用完整双精度 TRS：仅把请求骨骼及祖先转换到组件空间，按原顺序
施加同一个 SpineRotation，再转换相关骨骼回局部空间；未访问的局部骨骼
保持原姿势。原 UpdateAimingValues 输出 yaw/4，四个骨骼各施加一次。
Godot 的旋转轴按现有坐标转换取 -Y。没有添加额外角度限制或手臂偏移。

直接参照本地 UE 源码：

- `Engine/Private/Animation/AnimNode_ApplyMeshSpaceAdditive.cpp`：更新 Base、
  条件更新 Additive，按 Mesh Space Additive 混合并归一化输出。
- `AnimGraphRuntime/Private/AnimNodes/AnimNode_TwoWayBlend.cpp`：相关性、
  两分支的独立权重和单分支直通。
- `AnimGraphRuntime/Private/BoneControllers/AnimNode_ModifyBone.cpp`：
  组件空间前乘旋转。
- `Engine/Public/BonePose.h`：惰性组件空间转换、SafeSetCSBoneTransforms、
  转回局部空间时的 TRS 与归一化顺序。

`AlsAimLayerFrameStage` 将既有 AimingInputModel、七个 evaluator 的 Aim
状态历史、真实 Aim 资产采样和新外层输出一起验证/提交/取消。被遮蔽期间
全局 aiming 属性继续更新。Overlay 的 AimSweepTime 也改用同一候选属性。

受控组合入口增加 `--aim-layer`，将上述输出反馈到下一帧的分层和 Overlay。
先准备 Aim，再处理 Post Layering 的来源；移动/Overlay 仍只推进一次共享
采样批次。测试场景使用受控 Actor/视线输入，不改默认 Demo 的键鼠实现。

## 验证

- Debug 优化构建：0 警告、0 错误。
- 新编译/运行时专项 15 项，连同已有 Aim/LayerBlending 回归共 65 项通过，
  1 个既有普通 Editor 重复检查跳过。包含最大请求权重、组件空间四段
  旋转、曲线 presence、晚期失败取消，以及新段 2,000 次热循环零分配。
- 原真实 Post Layering 组合 1,260 帧回归保持。
- 新真实 Aim/脊柱组合在 30/60/120 Hz 下共 1,260 帧，逐帧取消/重试一致。
  Aim 相关 1,184 帧、隐藏 76 帧；脊柱相关 260 帧，其中 184 帧两分支混合。
  另有 11 次外层晚期失败，确认 Aim 状态、全局 aiming 输入及上游提交身份
  不泄漏；已有 26 次 Overlay、11 次 LayerBlending 失败检查继续通过。
- 组合共 33 个来源事件，其中 6 个过渡资产事件；3 个状态机通知/播放请求。
- 原生产 single/parallel 各 600 帧回归通过，28 个事件、lag/stale 均为 0。
  result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
  root=`A4F6C26CBAB8A0E7` 保持；这不表示新段已在 Worker 启用。

最终证据：`artifacts/test-results/aim-layer-verified.trx`、
`artifacts/outer-aim-shared-final.log`、
`artifacts/outer-aim-post-layering-regression.log`。
生产日志：`artifacts/outer-aim-worker-single.log`、
`artifacts/outer-aim-worker-parallel.log`。
`aim-layer-final.trx` 是错误要求隐藏 Alpha 引脚存在时的失败记录，不能当作
最终结果；没有放宽运行时数值断言来处理该失败。

## 完成边界

本批的真实组合输出已经到达手部 IK 之前，但尚不是完整 AnimGraph：

1. 两个手部 TwoBoneIK、Foot IK/FootLock/pelvis 的完整最终消费仍待接入。
2. 本段使用真正的缓存更新权重，并复用上游本帧已求出的同一份姿势；全局
   初始化/重入、根分支切换、跨求值作用域的缓存生命周期和统一生产帧所有者
   仍待完成。不能把它称为整个最终根的初始化/缓存语义已经关闭。
3. 尚未新增该完整外层段的独立 UE 逐骨骼探针。已有节点源码核查、组件
   检查和 Godot 真实组合不等价于 UE 全图输出对照。
4. 默认 Demo 仍是 75/109 BaseLayer 输出；新组合不在生产 Worker 启用。
   因此双臂、换髋、交错步与滑步视觉问题继续保留，尚无本批人工验收。

下一项继续原 P4 的手部 IK 与最终根管理，完成最终姿势/曲线统一发布和
Worker/Demo 接线，再闭合脚部与 UE/Godot 多帧、人工验收。原 P5A 剩余项、
P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6 物理恢复/Camera、P7
十分钟预算都保持原范围。既有全 Core 23 项失败、Import 分配稳定性及
p95 2.559ms 超过 2.5ms 的记录未关闭；音频继续暂缓。
