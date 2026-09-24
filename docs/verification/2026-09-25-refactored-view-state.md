# Refactored View / Spine / Head 状态移植

在主目录 main 新增 `AlsRefactoredViewModel`，直接对照本地 ALS 原始 `AlsAnimationInstance.cpp` 的 RefreshView、RefreshSpine、InitializeHead、RefreshHead。状态为不可变值类型，计算返回候选；图宿主负责回调顺序和整帧提交/取消，不在模型内部自动调用 Head。

## 保留的原生行为

- 世界角度先 double 相减，再经过 UE_REAL_TO_FLOAT 对应边界；内部角度为度，不混用旧模型的弧度接口。
- 动作存在时冻结 View 的相对 yaw/pitch/PitchAmount，但曲线控制的 HeadBlendAmount 和 Spine 更新继续进行。
- Aiming 或第一人称允许脊柱旋转。允许状态反转时重映射 amount 的 scale/bias，避免中途反转导致跳变；进入半衰期 .1 秒，退出 .7 秒并按视角速度缩短。
- 退出瞄准时维护世界朝向；支持旋转基底补偿，并把相对角色的偏移约束到 ±30 度。最终脊柱量受 ViewBlock/PoseAiming 控制。
- Head 的显式 Initialize 只置初始化标记。Refresh 执行平台补偿、角色偏航速度补偿、[-180,180] clamp；速度朝向模式使用输入/目标朝向、20 度每秒转向阈值和 175 度防反侧规则。
- 视角朝向模式使用 ±175 度目标、90/160 度换侧滞后与输入判定、10 度完成阈值。普通头部偏航使用非角度绕回的临界阻尼弹簧，保留换侧速度状态。
- 第一人称只有俯仰 damper 改用真实 delta；偏航弹簧仍用游戏 delta，与原源码一致。
- 复用已有 `AlsRefactoredRigMath` 的 float InvExpApprox，未复用旧通用 Pow/Exp 插值。标量角度重映射为严格 `>175`，不是向量实现的 `>=175`。

Head 的五个半衰期为显式输入；本批测试采用 C++ 头文件默认值，不将其冒充实际设置资产导出。也尚未执行 Editor/game world/settings 有效性等宿主门控，调用方必须在正确的图访问位置调用这些独立操作。

## 验证

- 新增 12 项测试：动作冻结但曲线继续、零时间权限反转连续性、旋转平台/世界偏移上限/快速视角退出、第一人称权限与曲线 clamp、175/176 标量边界、换侧滞后、真实/游戏时间差异、速度朝向与180度、显式初始化/零半衰期，以及三种频率的候选重试。
- 30/60/120 Hz 各四秒，共 840 帧，交替瞄准/动作/第一人称/移动基底/±179度视角；同一输入和已提交状态重新计算结果完全一致。不是物理场景或原生 golden 对照。
- Core Release View/Aim 定向 **186 通过、0 失败**，包含上述 12 项。首次新增 11 项通过后追加标量边界，再运行该回归。
- Godot Optimize 构建 **0 warning、0 error**。
- 日志/TRX：`artifacts/refactored-view-state/`。没有新 UE 导出、Import 全量、Godot 运行或视觉验收。

## 下一步

先导出实际 Head 设置与 UE 连续 View/Spine/Head 状态轨迹，验证数值和状态切换，再接实际 Head 图 OnUpdate/OnBecomeRelevant 回调顺序、BS_Als_Look 原始采样与 mesh-space additive。普通 Demo 仍使用既有路径，本批不能声明头颈拉伸问题已修复，也不能声明完整 Head/View 已完成。后续完整宿主、Mantle/恢复、物理稳定性、Flail 和十分钟预算等工作仍保留；头颈专项、道具物理及音频按用户要求暂缓。
