# Mantle Montage 与 PostLocomotion 插槽

日期：2026-09-24。主目录 `D:/GodotALS`，分支 `main`。

新增 `AlsMantlingMontageCompiler/Profile`，将真实 6 个 Montage 与已编译的
3 个姿态/曲线来源绑定，复用 `AlsMontageRuntime` 的实例身份、时钟、组仲裁和
淡入淡出，以及 `AlsMontageSlotPose` 的姿态/曲线混合，不创建第二套播放器。
Core 增加明确的 `AlsMontageSlot.PostLocomotion`（图插槽 ID 4），普通序列
Montage 和 SlotPose 的合法插槽范围同步扩展；旧四个插槽 ID 不变。

编译器读取 Refactored 骨架的 SlotGroups：PostLocomotion 属于 Locomotion 组，
原生数组索引 2。检查一段、一次循环、零 trackStart、单个 Default 终止 section、
持续时间、源动画覆盖、正 segment rate/RateScale、显式混合策略及无自定义曲线。
未支持的 BlendMode/Profile、其他 section/slot 等明确拒绝。

真实 High Montage RateScale 为 1.2，其余为 1；六个 BlendIn 均为 .2 秒
HermiteCubic，High BlendOut 是 .2 秒 Cubic，其余是 .2 秒 HermiteCubic。
当前六个源动画的 EnableRootMotion=false，所以 Montage 不抢占普通 root extraction
owner；攀爬位移仍需由 MantlingRootMotionSource 路径控制。

Profile 内的 dense animation/action/montage/group ID 属于自己的 Refactored
资源作用域。不得直接将其塞入现有 V4 bank，后续整合须显式重映射资源与组身份。
姿态来源实现 `IAlsMontagePoseSource`，只接受 PostLocomotion 非 additive 资源，
按共享 Montage 的 clip 时间采样原始 pose 和同次曲线。

## 通知时序的实际缺口

核对本机 ALS 原生源码：
`Notifies/AlsAnimNotifyState_SetLocomotionAction.cpp` 和
`Notifies/AlsAnimNotifyState_EarlyBlendOut.cpp` 的构造都设置
`bIsNativeBranchingPoint=true`。导出 event 的 tickMode=Queued 不能覆盖类原生标志。
现有 `AlsMontageNotifyCompiler` 只接受 Queued，并明确要求 branching 使用独立同步路径。

因此本批**没有把这两类状态伪装成 queued 通知**，也没有宣称已完成通知绑定。
EarlyBlendOut 在 BranchingPointNotifyTick 内按 input/locomotion/rotation/stance 的 OR
条件匹配，在精确 MontageInstanceID 上执行 Stop，继承原 Montage 的 BlendOut option，
仅替换 duration。SetLocomotionAction Begin 写 tag，End 只在当前 tag 仍相等时清空。
下一步须实现原生阶段顺序、跨状态边界子步以及指定实例回调，含中断/结束清理与回滚。

## 验证

- Release Import Mantling：54 项通过（本批 7 项，含参数化拒绝）。
- 六个真实 Montage 各运行 240 帧：首帧 Play 后冻结输出保持不变，下一帧按
  RateScale 推进；经历淡入、完整权重、淡出、自然移除，并恢复上游 pose/curve。
  完整权重阶段输出逐骨与当前 clip 源采样比较，曲线完全相等。
- 替换保留 outgoing physical fade 和新实例；Discard 不修改已提交状态，
  相同帧重试可成功提交。拒绝错误 slot/section/profile/rate/custom curve。
- Core Montage 相关回归 151 项通过。
- Godot 优化构建 0 警告、0 错误。

首轮测试编译使用了错误属性名和 FrameIdentity 参数顺序，已修正。
首次执行测试错误地假设所有 BlendOut 都是 Cubic，出现 53 过/1 失败；
重新核对真实 6 行导出后修测试为 High/Low 差异，生产编译器无需改变。
证据在 `artifacts/mantle-montage/`：`montage-final.trx` 保留这次失败，
`montage-verified.trx` 为最终54通过，`montage-core.trx` 为151通过。

本批没有新增原生 Montage 逐帧 oracle，不宣称已验证整条 Mantle 原生时序等价；
无 UE 插件/资产变更、无新 Godot 场景/截图、无全量回归。
普通 Demo 未接 Mantle；完整 manifest/资源合并、逻辑骨架适配、通知同步路径、探测及
动作/运动源生命周期仍待完成。之前的非恒等 Orient 原生覆盖、旧移动 oracle 闭包问题、
物理9/12、Flail0/3、复杂相机、最终视觉及十分钟性能预算保留。
头颈、道具物理和音频暂缓；用户计划/project.godot/头颈诊断/.cs.uid 修改未触碰。
