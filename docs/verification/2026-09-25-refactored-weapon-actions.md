# 四武器外层 Action 编译和运行时

接续 `b7ab263`，直接在主目录 main 实现，未修改 UE 插件或普通 Demo。

## 实现

`AlsRefactoredWeaponActionProfile` 从实际 `Overlay` 图编译 Root→GameplayTagsBlend→默认状态机及三个动作分支。验证 tag 顺序/绑定、runtime 与 authored pose links、回调、固定帧和曲线字面量，拒绝未消费节点与未知连接表达式。默认分支必须对应已验证的原武器机器。

四图实际混合时间均为 Default/Mantling/GettingUp/Rolling = .3/.3/0/.3 秒，不是其他道具 Overlay 的 .5/.2/0/.2。

| 武器 | Mantling 帧 | GettingUp 帧 | Rolling 帧 | 动作曲线覆盖 |
|---|---:|---:|---:|---|
| Bow | 3 | 8 | 0 | Mantling 左臂 additive=.5；Get-up/Roll 左臂=3 |
| PistolOneHanded | 3 | 7 | 3 | Get-up/Roll 右臂=3 |
| PistolTwoHanded | 3 | 7 | 2 | Get-up/Roll 右臂=3 |
| Rifle | 11 | 7 | 9 | Get-up 右臂=3；Roll 双臂=3、双臂 additive=0 |

动作固定帧预采样保留原 79 骨厘米布局、float 时间换算、曲线 absence，再应用实际 ModifyCurve。曲线布局包含原机器和动作分支所需全部名称。

`AlsRefactoredWeaponOverlayRuntime` 复用已有 ActionBlend/ActionMix 和武器 machine/source/pose。默认分支相关或旧分支需零权重更新时才更新机器；完全隐藏时不登记播放器输入、不推进机器，外层动作继续混合。恢复相关时沿用原机器 serial-gap 重置，隐藏期间显式重置保留 pending。外层候选、机器和源更新共同预检/提交/取消，共享 SourcePlayer bank 仍由宿主提交。

Evaluate 使用更新阶段权重和共享播放器时间，不另建时钟。采样 owner 变化或求值异常后不发布姿态、不允许提交，必须 Cancel 后重试。允许下游完整 Slot 覆盖时只更新不求值。可读取本帧 Machine 供已有通知绑定消费，但此类不直接发送 gameplay 通知或播放 Montage。

## 验证

新增八个测试：四图固定帧/源姿态/曲线覆盖逐值检查，24 种标签、混合时间、曲线、帧、链接和绑定变异拒绝；四图各 50 个提交帧，覆盖三个动作完整权重、隐藏时钟保持、隐藏重置、重新进入、逐帧取消重试、采样 owner 改变后的失败隔离及 update-only 提交。

首轮 `artifacts/refactored-weapon-actions/actions.trx` 八项失败：校验器把 StateMachine.Body 中嵌套子图的绑定当作外层绑定拒绝。该节点内部由已有 SourceProfile 校验，修正分工后 `actions-final.trx` 八项通过；失败日志保留。

最终相关 Import 回归 `artifacts/refactored-weapon-actions/related.trx`：144 通过、0 失败。Godot Optimize 构建 0 warning、0 error。

## 尚未验证/完成

本批是原图资源编译和运行时集成，测试新增动作部分尚非 UE 完整 Action 连续 oracle。下一步导出四武器 Action 切换、隐藏、零权重旧分支更新、隐藏 reset 和重新进入的原生记录，比较整图 pose/curve、实际 state clock 和源更新时间；需要时据此修正运行时。原图 AnimGraph→LinkedLayer 入口的整体封装也待验收。

因此完整 Overlay 连续证据仍为 9/13，不把代码存在当成四武器整图完成。普通 Demo 未切到完整 Refactored 链；Ragdoll/Get-up/Pose Recovery 整体验收及真实 Locomotion、统一宿主、Turn/dynamic transition 调用端、Mantle gameplay、完整相机、十分钟性能等旧缺口保留。没有 UE 新启动/导出、Godot 场景、全量或性能测试。用户修改未动，音频、道具物理、头颈诊断继续暂缓。
