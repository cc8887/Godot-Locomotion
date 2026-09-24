# 起身中切换 Overlay 的运行时回归

本批继续在 `.` 收尾 Ragdoll/Get-up 的 Overlay 边界。没有改动生产播放算法、原生资产或道具物理附件；补充普通输入路径的集成回归。

## 原生行为依据

本机 `../AdvancedLocomotionSystemV/Plugins/ALS/Source/ALS/Private/AlsCharacter_Actions.cpp` 的 `StopRagdollingImplementation()` 在落地退出时调用一次 `SelectGetUpMontage()` 并播放，随后阻止移动输入、标记 GettingUp。起身不随每帧 Overlay 重新选择。具体 V4 的 13×2 Montage 选择及 Notify Begin/End 仍沿用上一批实际导出结果，见 `2026-09-24-overlay-get-up.md`。

## 新增覆盖

`CharacterRagdollRecoverySmoke --overlay-cycle` 连续完成13次地面 Ragdoll→Get-up→恢复行走，然后验证空中退出：

- 每次起身约1/3秒，通过普通 E 键切换到下一种 Overlay，包含 Barrel→Default 回绕。
- 等生产动画提交新 Overlay 后，检查 Get-up 的 ActionDefinitionId、OccurrenceHandleId、PlaybackEpoch 都不变，播放时间继续前进。
- 检查同一提交的新 Overlay 道具状态。此检查只涉及已有动画附件切换，不是道具物理验收。
- Override 仍由进入时所选 Montage 的 Notify 决定。Default/Feminine→Injured 不应凭新 Overlay 凭空生成3；Barrel→Default 也不应过早清除旧 Montage 的3。
- 每轮结束检查动作/Notify 所有权清空、override=0、持续 W 恢复位移。下一轮再按新 Overlay 选择 Montage。
- 每轮完成后回到开阔测试点，避免13轮累计位移离开地板。该模式不混用自动落地/中途打断/生命周期场景；这些仍由已有独立参数覆盖。

同时修正旧 smoke 的 override 断言：判断依据改为起身进入时的 Overlay，而非动作结束时可能已经改变的当前 Overlay。

## 验证记录

- Optimize build 通过，0 warning、0 error。
- Parallel60 从 Default 开始，13次起身/13次切换/13次完成，随后空中退出通过，0 error：`artifacts/get-up-overlay-cycle60-final.log`。
- Single30 OpenGL 从 Rifle 开始，13次起身/13次切换/13次完成，随后空中退出通过：`artifacts/get-up-overlay-cycle30-rendered.log`。首次起身 BeforePublish 注入一次故障，恰好一次诊断、正常重试，无额外错误。
- 渲染生成6张图，目录 `artifacts/get-up-overlay-cycle30-captures`；检查03/04/06，确认 Rifle 坐起、切换 Pistol1H 后起身继续、随后移动。HUD 的 Errors 1 是上述故障注入，不是未解释的异常。
- Parallel120 Bow 起身打断回归通过：accepted3/completed2/interrupted1，随后空中退出、override清零，0 error；`artifacts/get-up-overlay-interrupt120.log`。

首轮 `get-up-overlay-cycle60.log` 在七轮切换后地面断言失败；该轮没有循环重置测试位置。失败日志保留，不作为最终通过记录。新增循环重置后完整遍历通过，未降低地面判断标准。

本批未重跑 Core/Import 全量或 UE 导出：改动仅在 Godot 集成测试。沿用上一批的原生映射/Notify 资产，未修改生产算法。

## 范围限制

不据状态回归宣称13种 Overlay 全部视觉轨迹与 UE 逐帧一致。静态物理9/12、Flail0/3旧稳定性指标保持原状，未重跑旧矩阵。Mantle、完整 ALS Camera、十分钟性能预算仍待完成；头颈拉伸与道具物理继续按用户要求暂缓。
