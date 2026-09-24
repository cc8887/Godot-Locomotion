# Get-up 生命周期与动画到物理桥接

## 本批实现

继续在 `D:/GodotALS/main` 工作，优先关闭上批 Get-up 的停用、暂停和角色替换边界。未新增项目目录，未改 UE 插件、资产或导出。本批没有接入 Overlay 起身变体。

发现并修复两个可复现的状态残留：

1. G 退出 Ragdoll 已生成 Get-up 请求，但尚未第一次 Gather 时执行 `SetActive(false)`，动画生命周期被清空，motor 的请求及输入锁却仍存在。新增统一清理，在 gameplay 停用、generation 退休、销毁时清除 Get-up 请求、输入锁及观察状态。调度暂停 `SetSchedulingActive(false)` 则保留这些状态，恢复时继续原动作。
2. 播放 Get-up 时替换角色，暂存输入的 ActionRequest 虽已清空，GameplayAction 仍为 GettingUp。迁移新 generation 时显式清除这个旧动作标记，避免新角色继承旧起身状态。

`CharacterRagdollRecoverySmoke` 新增 `--lifecycle=pending|active|suspend|generation`。测试在真实 Demo 中触发边界、保持 W，检查停用期间帧/位置/动作回调不推进，恢复移动、动作只结束一次，并继续完成两次 Ragdoll/Get-up 与空中退出。generation 用例校验恰好一次预期 GenerationMismatch，不将其当未知错误忽略。

## 暂停测试额外发现的物理桥接问题

Single30 起身暂停恢复后，在动画帧 168 出现多个骨骼 Scale=`0.9999905`。旧桥接把每个动画骨骼送进场景碰撞 transform 校验：三轴各偏离 1 约 9.5e-6，但 determinant 约偏离 2.85e-5，超过其 1e-5 限制，导致身体历史失败。

本机 UE 代码依据：

- `Engine/Source/Runtime/Engine/Private/Animation/AnimInstanceProxy.cpp`，SlotEvaluatePose 在 SourceWeight 不超过 ZERO_ANIMWEIGHT_THRESH 时不加入 source。
- `Engine/Source/Runtime/Engine/Private/Animation/AnimationRuntime.cpp`，BlendPosesTogetherIndirect 按权重累加 TRS，只在多 pose 时归一化旋转，不重新归一化 scale。

因此保留动画输出，不修改混合公式。动画物理祖先链改为显式逐轴单位缩放容差 `AlsPoseBlender.WeightThreshold`（1e-5），保留已有单位旋转与有限值检查；最终 actor world 仍通过原有 Orthonormalized 输出刚体。场景碰撞体与 component world 的严格校验不变。**这是动画桥接接受范围的明确调整**，不表示支持缩放物理身体，也没有修改 solver 或休眠门槛。

扩展两套模型的 pose smoke：接受上述近单位动画缩放并检查刚体输出，同时拒绝 `1.00002` 以及原有 `1.2` 真缩放。仅验证这里记录的近单位情况，不宣称已支持任意带缩放骨骼。

## 验证记录

| 检查 | 结果 / artifacts 日志 |
| --- | --- |
| Optimize build | 0 warning、0 error |
| Parallel60 请求生成后停用 | 两次后续起身 + 空中退出通过；accepted=2 completed=2 retired=0；`get-up-lifecycle-pending.log` |
| Parallel120 播放中停用 | accepted=3 completed=2 retired=1，恢复普通移动并重复起身通过；`get-up-lifecycle-active.log` |
| Single30 调度暂停恢复 | 原动作完成而不退休，accepted=2 completed=2；`get-up-lifecycle-suspend-fixed.log` |
| Single30 首次起身失败重试后再暂停恢复 | accepted=2 completed=2 retired=0，恰好一次预期故障，两循环及空中退出通过；`get-up-lifecycle-suspend-final.log` |
| Parallel60 Get-up 角色替换 | accepted=3 completed=2 retired=1，恰好一次预期 generation mismatch；`get-up-lifecycle-generation-final.log` |
| 原有 Roll generation 回归 | accepted=1 interrupted=1 synthetic_ends=1，重复退休忽略；`get-up-lifecycle-roll-generation.log` |
| 两模型物理 pose 回归 | 8 cases / 160 handoffs / 192 captures / 64 rejected，最大位置误差4.952927e-6m；`get-up-lifecycle-physics-pose-fixed.log`、`get-up-lifecycle-physics-pose.json` |

先失败后修复的证据保留：`get-up-lifecycle-pending-before.log`、`get-up-lifecycle-generation-check.log`、`get-up-lifecycle-suspend.log`。generation 首次还遇到测试将预期分类计数误报为错误，随后改为精确检查 GenerationMismatches；physics pose 首次命令遗漏必需的新报告路径，补参数后通过。这些失败不计作通过。

本批为 Godot host 变更，未重新执行 Core/Import 全量、UE 构建/导出、渲染截图或十分钟性能测试；上一批 Core2914/Import2486+1skip 仅作历史记录。旧物理稳定性矩阵静态9/12、Flail0/3未重跑，不能据生命周期回归宣称关闭。

## 后续

继续 Overlay 专用起身变体与自动 Ragdoll 触发；暂停时仍在重试、事件回调内停用/销毁等更复杂交错并未穷举。Mantle、完整 Camera、性能预算与全部既有规划继续保留。头颈拉伸和道具物理按用户要求暂缓。

用户 P4 规划文件及三份未提交头颈诊断产物保留且不纳入提交；P4 SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
