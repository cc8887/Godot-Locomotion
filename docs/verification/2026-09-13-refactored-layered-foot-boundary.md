# 完整 Refactored 脚部接入分层帧与物理查询边界

日期：2026-09-13，第一百七十八批。原 P4 最终脚部链补完。

## 已实现的接线

AlsRefactoredFootAnimationFrame 现在统一管理最终曲线、锁脚目标、脚部
有效性和 Rig 候选。全局阶段使用上一已提交最终根的 FootLeft/RightIk、
FootLeft/RightLock、目标骨和骨盆旋转更新 based lock，使用本次全局阶段
保存的 PoseState/预测计算骨盆输入。原始作者曲线仍经上一批的源层映射。

首次 pending 更新不执行脚部射线；下一次更新按原
RefreshFeetOnGameThread 检查最终骨盆是否 EqualsNoScale(Identity)。
一旦有效，后续不重复该首次有效性检查。有效性也随候选统一提交，
取消后不泄漏。真实骨架绑定按 FName 大小写无关身份匹配，拒绝歧义重复。

以导入的 precise FBX 初始局部姿势生成 UE 厘米/分量空间参考，用它绑定
原 Rig 的脚高、腿长和骨索引。本帧脊柱处理后的姿势转换到相同空间；
Rig 依次执行脚偏移、骨盆弹簧、左腿、右腿，转换回 FBX 局部姿势后进入
手部 IK，再与替代分支混成最终根。最终根的目标骨和骨盆进入下一帧
反馈。新 Rig 不覆写 IK 目标骨，因此不需要旧 V4 控制器的目标骨绕行混合。

实际 AlsLayeredAnimationFrameRuntime 新增明确的两段接口：

1. PrepareFootQueries 完成 Base、Overlay、分层及脊柱求值，生成本候选的
   精确射线请求，保留全部候选状态。
2. 主线程物理阶段消费请求，返回带完整请求身份和序号的观测。
3. ResumeFootQueries 完成脚、手、替代分支及最终根；所有子所有者验证
   后才允许统一提交。在查询间隙不能提交，也不能读取陈旧的脚部姿势。

取消会丢弃全部候选；查询序号不会回退。即使同帧再次产生完全相同的
射线，取消前的返回值也不能满足新候选。普通分支隐藏时保留 Rig 动态
历史，同时跟踪实际替代分支最终骨骼；恢复不会取到旧普通分支目标。

## 整图发现的更新权重问题

真实回放第 65 帧，LayeredBoneBlend 产生 1.0000001 的 Overlay 更新权重，
原校验把它当非法输入。已直接核对本机 UE 源码：

- AnimNode_LayeredBoneBlend.cpp 的 Update_AnyThread 将 ChildWeight 原值
  传入 FractionalWeightAndRootMotion。
- AnimNodeBase.h 第 281 行起仅相乘，不把绝对更新权重限制到 1。
- AnimNode_AssetPlayerBase.cpp 保存该值，AnimInstanceProxy.cpp 第 848 行等
  将它原值写入 FAnimTickRecord.EffectiveBlendWeight。

因此修正当前 Overlay 状态/来源/共享收集、来源 Tick 构建与 AssetSync
玩家校验，允许有限非负的绝对更新权重，不插入 clamp。BlendSpace 内部
归一化采样权重仍保持原来的约束。新增测试验证略大于 1 和 2 的玩家
权重原样保存，以及 1.25 的同步 leader 分数。其他尚未触发的路径仍需
逐项审计，不能据此宣布整个项目所有权重边界已经完成。

## 验证

新增 refactored_layered_foot_smoke 使用真实 V4 动画图、79 骨逻辑骨架、
真实停止/转身 Montage 和 Godot 射线。一个所有者在主线程计算作为对照，
另一个所有者的两个动画阶段在工作线程计算；物理查询均在主线程。
运动输入、组件运动、预测胶囊观测及 ragdoll 快照是受控输入。

| 频率 | 每个所有者帧数 | 实际射线数 | 锁定非零帧 | 普通分支隐藏帧 | 根混合帧 | 旧返回拒绝 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 30 Hz | 150 | 806 | 21 | 16 | 25 | 4 |
| 60 Hz | 300 | 1622 | 71 | 31 | 52 | 8 |
| 120 Hz | 600 | 3256 | 142 | 61 | 106 | 16 |

两种执行方式的最终姿势、曲线、Rig/锁脚历史、同步和事件相同；工作线程
所有者每帧取消完整求值再重试，合计 1050 次。查询间隙提前提交被拒绝。
60 Hz 追加最终姿势检查，216 帧普通根的最终骨盆位置与脚部处理前不同。
这证明新 Rig 实际影响完整图的输出，不只是新增了诊断状态。

日志：artifacts/refactored-layered-feet-178-hz-30.log、-hz-120.log，
60 Hz 的 -60-final-pose.log；追加首次骨盆有效性检查后使用 -60-validity.log。
后一个守卫不改变前两档已验证的有效骨盆路线；30/120 未因该守卫重跑。
初次骨名大小写失败、权重诊断失败保留在 -60-first/-bind/-diagnostic.log。
随后 -60-weight.log 暴露测试接收器错误地要求真实 Montage 始终为空，
已改为验证正式 Slot 权重合同；-60-slots.log 起通过。Math 命名空间
编译首错已修正，最终构建零警告、零错误。

新增/相关 Import 137 项通过，Core 来源/同步/锁脚专项 91 项通过，结果为
artifacts/tests/refactored-layered-feet-178-import-validity.trx 和
refactored-layered-feet-178-core-targeted.trx。

旧生产入口 single/parallel 各 960 帧通过，日志 -single/-parallel.log。
result DB9FEFC95ADA4B15、pose EFEF274D127B1A99、
full_pose 765E1669B4501131、root 3C8B520C47ECA5C7 保持；
预测/PoseState EFC740AA927A17E5 保持。此处仍使用旧脚部消费者，验证
兼容回归，不是新 Rig 已进入 Demo 的证据。

## 全量回归仍未通过

本批还实际运行了全仓测试，不能用上述专项覆盖其失败：

- Core：2488 通过、23 失败。1 项为旧 P4 蹲姿转身 turnYawDelta 对照；
  22 项属于 P5A Golden/Schema 家族，当前绑定快照与冻结 native plan
  不符，部分下游错误消息断言也因此失败。已确认计划构建器比较固定的
  manifest/layout/version/binding/graph 摘要；未改冻结基准来消除失败。
- Import 未优化运行：1188 通过、1 跳过后主机栈溢出；优化运行：2163
  通过、1 失败、1 跳过后同样栈溢出，故也不是完整通过。溢出定位到
  AlsMainGroundedCachedGraphTests.SharedNativeTraversalReachesNestedMachinesAcrossFrameGapsAndSuppression。
  优化运行还暴露冻结 public surface 与现有 SourceProfile 属性不符。
- 上述 TRX：-core-full.trx、-import-full.trx、-import-optimized.trx。
  所有进程已结束。尚未做隔离版本 A/B，不将这些问题全部断言为本批之前
  已存在，也不放宽 golden 数值容差、删除失败案例或扩大线程栈掩盖问题。

这些是后续必须解决的验收债务。当前变更不经过旧 P4 的 TurnRotateModel
调用链，也未修改 SourceProfile 公共属性或冻结计划摘要，但仅调用路径
核对不足以替代相关问题的完整诊断。

## 接下来

新 Rig 已接实际分层运行时，尚未接 Demo 的 ProcessThreadGroup 分发器。
本测试用显式 Task 边界验证线程所有权，不能称为十角色调度/性能已完成。
下一项将相同两段接口接到真实 Motor→动画前半段→主物理查询→动画后半段
与提交，处理角色世代、失败恢复和销毁。随后进行同输入 UE 配对、多帧截图、
平台、起步滑步、交错步和换髋验收，并解决上面的全量回归问题。
第 616 帧旧失败、默认完整入口、P3/P4 整角色及 P5A–P7 仍开放；音频暂缓。
本批未改 UE 插件/原生资产/已确认键鼠逻辑，未提交或回滚用户修改。
