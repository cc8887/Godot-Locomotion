# Mantle 排队通知绑定

本批将六个 Mantle Montage 的实际序列脚步事件绑定到共享通知队列，并补上 PostLocomotion slot。完整攀爬仍未接入普通 Demo，音频继续暂缓。

## 实现

- `AlsMantlingNotifyCompiler` 校验动画输入字节哈希、源集合、实际原生 FootstepEffects 类、对象身份、触发偏移、过滤策略和序列映射。三条原始序列共有九个对象，在六个 Montage 上形成十八条时间线绑定；对象身份跨播放复用，播放身份仍取共享 Montage instance。
- 从对应 T3D 对象体读取左右脚、FootstepEffectsSettings 引用、decal/particle 开关。未写出的默认值依据本机 `AlsAnimNotify_FootstepEffects.h`：Left、两个效果开关 true。High 的脚序为 Left/Right/Left，第一步禁用 decal/particle。保留这些效果引用，不创建音频或粒子/贴花实例；这些资产的场景侧效果执行仍待接。
- 共享队列支持 slot 0..4 与五位 relevance mask；沿用原生权重、概率、LOD、专用服务器和上一帧 slot relevance 策略。
- 新增同一播放 bank 的 `NotifyTraversal`：按两个已绑定 branching state 的实际边界分段，保存 HandleEvents 收集时的中断状态和 CurrentTime。EarlyBlendOut 在本帧末尾停止实例，不再抹掉此前已经收集的脚步。
- 原 `Traversal` 仍为每实例一个整帧摘要，维持动作所有者 reconcile 和播放 reader 的原有契约；没有新增播放身份或第二时钟。普通 BaseLayer 通知消费改读 `NotifyTraversal`。Ragdoll 的已有显式清除逻辑同步处理这两种视图。

源码依据为 UE `AnimMontage.cpp` 的 HandleEvents 与 Advance：每个 branching marker 的子步先收集排队通知，全部运动后才 Tick 活动状态。当前仅用于原有受限的 Mantle 两状态布局；非通用任意 callback 跳转实现。

## 验证

- Core Release Montage/branching 定向：154 通过。
- Import Release Mantling/MontageNotify 定向：99 通过，包括上一批 126 条原生状态轨迹，以及本批七项测试。
- 六个 Montage 各在 30/60/120 Hz 自然播放，收到其真实三个脚步，对象顺序、动作归属、物理播放身份和左右脚 payload 校验通过。
- 同帧推进 High 到 2.4 秒并触发提前淡出，三个此前经过的脚步仍保留；前两条携带动作状态结束标记时刻，第三条携带最终子步时刻。下一帧中断不再派发；丢弃并重试产生相同事件。
- 服务器与无关 slot 不派发、输入字节哈希不匹配拒绝。旧队列测试含暖机后零分配，但未据此宣称新增整个 Mantle 链路达到最终性能预算。
- Godot Optimize 构建：0 警告、0 错误。测试产物在 `artifacts/mantle-notify/`。
- 普通角色 headless 回归 60 Hz/480 帧通过，包含 Ragdoll、第一人称切换；`demo.log` 最终 `ALS_NATIVE_CAMERA_DEMO_OK`，实际退出 0。这是既有普通链路回归，不是新 Mantle 场景验收。

本批没有新增 UE 通知队列 oracle。上一批原生 reference 只验证动作状态与播放数值，不记录 queued notify 的列表/上下文；本批新的分段队列行为仍需独立 native queue 对照。没有全量 Core/Import 测试或 Mantle 渲染实测。

## 后续

补 queued notify 原生队列/上下文对照，并将局部资源身份映射到角色资源域，再接完整 slot 图、障碍探测、移动基座、MantlingRootMotionSource 与中断/销毁/Ragdoll 转换。效果设置目前是引用，非效果资产执行已完成。

旧物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生覆盖、旧移动 oracle 闭包问题、最终视觉与十分钟性能验收未完成。头颈、道具物理和音频暂缓。用户 plan、project.godot、头颈文件和既有 uid 不纳入提交。
