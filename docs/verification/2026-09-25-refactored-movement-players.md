# Standing / Crouching 移动播放器绑定

在主目录 main 新增原图播放器编译和显式宿主 ID 绑定：Standing 24、Crouching 6；不包含上一批四个 RotateInPlace 播放器。校验完整嵌套图中的源资产、属性绑定、同步组、循环政策和回调，不推测状态权重。

Standing 六个 WalkRun BlendSpace 接 StrideBlendAmount / WalkRunBlendAmount / PlayRate；两条 Sprint 序列接站立速率；16 个起步/急转节点引用四个加速资源，但保留各自 property identity、Run Start / First Pivot / Second Pivot 同步组和 .1/.15/.25 秒起点。Crouching 六条序列接蹲伏速率。重初始化输入显式保留原图起点，避免共享播放器输入默认零将起点覆盖。

排查发现：暴露的 StartPosition 常量引脚覆盖 authored Node 默认零；JSON 导出的单精度数不能用重新序列化后的 double 精确比较。另 Sprint_Acceleration 的 PlayRateBasis 实际为 0.8333330154418945，不能假定全部为 1。按本机 UE `Runtime/Engine/Private/Animation/AnimNode_SequencePlayer.cpp` 的 PlayRate / PlayRateBasis 计算有效速率；原图 clamp 为恒等。未修改资产或通用同步算法。

## 验证

`artifacts/refactored-movement-players/` 保留最初 startPosition 失败以及修正后 playRateBasis 失败记录；`movement-basis.trx` 两项绑定检查通过。最终 `related.trx` 13 项通过，包含新增五项、此前 Rotate 六项和共享播放器两项。

新增验证包括 30 个真实源各三帧（共 90 提交帧，每帧取消重试并比较姿态/曲线/时钟），序列重置起点和非循环末尾保持；原图五种 start/basis/binding/loop/callback 变异拒绝。BlendSpace 使用原三角化与同步资源绑定，但本批没有新增 UE 连续移动原生对照，不能声称整个移动图等价。

`dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet` 通过，0 警告、0 错误。未运行 UE/Godot 场景、全量测试、十分钟性能或打包。

## 尚未完成

真实 Standing/Crouching 状态条件、状态回调、缓存姿态、惯性化和完整 pose 图仍须继续，随后接统一角色宿主的 Overlay / Notify / Montage / root motion。普通 Demo 尚未切换完整 Refactored 链；Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 与性能等旧缺口保留。音频、道具物理、头颈诊断仍暂缓，用户已有修改未纳入本批。
