# 最终根选择器与普通分支生产接入

第一百三十四批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 完成内容

继续 P3/P4 完整最终动画图：补原 ALS AnimGraph 最后一个
BlendListByEnum，将 `--foot-ik-frame` 的实际 Foot IK 输出接到根选择器的
普通分支。最终姿势、曲线反馈和根混合状态随同一角色候选提交/取消，
不再从脚部输出直接绕过根节点。

新增 `AlsRootPoseCompiler` 从正式 `v4_layering_inputs.json` 校验 Root →
BlendListByEnum → 普通 Foot IK / Ragdoll States 的连接与 MovementState
输入、编译节点身份、枚举映射、混合模式和无自定义回调的条件。
普通/Ragdoll 时间取实际暴露引脚 0.4/0.5 秒，不使用编辑器后备 0.1/0.1。
定义由 `AlsMovementGraphDefinition` 加载，生产根使用同一正式定义。

新增 Core `AlsRootPoseRuntime`：

- 普通状态走默认 pin 0，Ragdoll 走 pin 1；其他枚举值也按原默认分支。
- 冷启动直接取当前目标分支；之后采用 StandardBlend/HermiteCubic。
- 反向中断从当前权重继续，剩余时间乘目标权重差，不重新开始完整时长。
- 正权重子分支按原顺序更新，非目标分支标记 Inactive；保留父级 inactive
  和独立 RootMotionWeight。零时间切换保留旧分支的一次零权重更新请求。
- ChildUpdateMode=Default，激活子分支不强制重新初始化。根初始化计数
  真正改变时重置根混合状态；仅 CacheBones 改变不重置该状态；跳过帧号
  本身不作为根重新初始化依据。
- 单分支满权重直接复制，双分支按 UE 原顺序加权骨骼并归一化旋转，
  曲线使用 Lerp 并保留缺失标记。活动分支必须提供真实布局的姿势/曲线，
  缺失源抛错，不能用参考姿势填充并宣称 Ragdoll 已绑定。
- Prepare/Evaluate/ValidateCommit/Commit/Cancel 保存候选与已提交状态；
  失效身份、源数据缺失、取消和同帧重试不能提前改变历史。

直接只读核对本机 UE 5.9：

- `AnimGraphRuntime/Private/AnimNodes/AnimNode_BlendListBase.cpp` 的
  Initialize、CacheBones、Update 与 Evaluate；
- `AnimNode_BlendListByEnum.cpp` 的默认枚举映射；
- `Engine/Private/Animation/AnimationRuntime.cpp` 的
  BlendTwoPosesTogetherInPlace，包括保留第一权重、计算第二权重及曲线 Lerp。

本批没有新增 UE 运行时 oracle，也没有修改/启动 UE 原生插件。

## 生产接入与验证

`AlsLayeredAnimationFrameRuntime` 的 native-foot 路径创建根所有者，将其
普通子上下文传入后续更新，Foot IK 后执行根求值。复制最终根曲线作为
下一次更新反馈，根校验加入提交前检查，Discard 同时取消根候选。
目前只绑定普通分支；动作/Ragdoll 未绑定状态保持显式拒绝。

根的已提交身份和混合状态进入控制器事务诊断。生产回放逐帧断言根身份
等于已提交可视帧、普通分支权重为 1、Ragdoll 权重为 0；晚期失败回滚
同时比较根身份与状态，不只比较始终为 1 的普通权重。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 优化 Debug 构建 | 0 错误、0 警告 | `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` |
| Core 根与已有 BinaryBlend 专项 | 21/21 | `artifacts/test-results/root-pose-core-final.trx` |
| 正式根图编译专项 | 6/6 | `artifacts/test-results/root-pose-import.trx` |
| 生产 single/parallel | 各 960 帧，与上一批姿势/结果摘要一致 | `artifacts/root-pose-worker-single.log`、`root-pose-worker-parallel.log` |
| 增加根身份断言后的最终生产回归 | 并行 960 帧通过 | `artifacts/root-pose-final-parallel.log` |
| 最终晚期姿势与事件失败 | 根身份/状态、控制器、姿势恢复，事件回调泄漏为 0 | `artifacts/root-pose-final-late-transaction.log`、`root-pose-final-late-events.log` |

Core 包括 30/60/120 Hz 的 0.4/0.5 秒过渡、冷 Ragdoll/默认枚举、混合反向、
Inactive/RootMotionWeight、零时间切换、初始化/骨骼缓存计数区别、失效代际、
缺失活动源、候选丢弃及同帧重试。热身后 2,000 帧混合根求值无托管分配。
这些双分支测试使用受控小骨架，不能充当真实 Ragdoll gameplay 验收。

生产共同摘要仍为 result=`D898A6B5BD5DE295`，
fullPose=`7B82A91E8A09C723`，root=`DB5B813964D3479C`，
sampledPose=`6804D603D2523040`。脚锁活动 316 帧、地形偏移 910 帧，
37 个事件，lag/stale=0，代际替换通过，旧脚部写入为零。本批不改变普通
分支满权重时的最终动作，因此没有重复渲染截图或声称视觉问题又获修复。

## 仍未完成

这不是“整个根生命周期完成”。根运行时提供两子分支初始化/CacheBones
需求标记，连续普通生产路径传递同一套遍历计数；但完整子图重新初始化、
热换骨架回调传播、非活动后普通状态机重入，还需真实两个分支一起接入
并验证。现有组合仍不能接受任意外部重新初始化调度。

下一步补齐此处需要的 Ragdoll States 姿势源/快照接口与普通分支相关性
生命周期，随后继续平台、UE 同输入多帧支撑脚轨迹与人工对照，达到要求
后切换默认入口。完整 Ragdoll 物理、Get-up/Pose Recovery 仍归原 P6，
不能把根的受控双源混合测试等同于这些行为已完成。

默认 Demo 仍为 BaseLayer；起步滑步和交错步尚未通过 UE/人工效果验收。
P5A 剩余通用 Notify/State/Sync/ActionPlayer/Slot、P5B 全 Overlay/道具、
P5C Mantle/Roll/Root Motion、P6 完整物理恢复/Camera、P7 十分钟性能
预算保持原范围。既有 Core 23 项失败、Import 分配不稳定和 p95=2.559ms
超 2.5ms 未在本批关闭。音频暂缓，未 commit/revert/merge。
