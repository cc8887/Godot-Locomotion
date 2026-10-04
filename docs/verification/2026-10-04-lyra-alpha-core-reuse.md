# ALS 与 Lyra Rig 共用 Alpha 内核

沿用当前 ROADMAP 的 locomotion 范围、ALS 人物与 Pistol/Rifle。此次定位到 ALS Core 已有 `AlsOverlayPoseWeights.Alpha`，而 Lyra FootPlant 的两个 AlphaInterp 指令另有相同计算；复用原算法，不增加另一套插值公式。

## 实现

`AlsInputScaleBiasClamp.Apply` 放在纯 .NET Core，提取原 ALS 的映射→Scale/Bias→可选 Clamp→非对称速度插值。原 float 运算顺序、平方距离阈值、首次更新、零 delta、未钳制历史保持。策略继续使用现有 `AlsOverlayAlphaPolicy`，保持现有 ALS 调用方兼容。

`AlsOverlayPoseWeights.Alpha` 调用共享内核后才将节点最终输出钳制到 0～1；未钳制插值历史仍保留。Lyra Rig 调用同一内核，直接使用未钳制输出，两个指令各自的候选历史、Clone/CopyFrom/Reset 和资源参数校验仍由原宿主管理。没有把 Rig 输出误当动画权重钳制，也没有引入新时钟或额外求值。

只读参考为本机 UE5.8 `Engine/Source/Runtime/Engine/Private/Animation/InputScaleBias.cpp` 的 `FInputScaleBiasClamp::ApplyTo`。本批没有 UE 启动、构建、保存或资源导出，没有资产 JSON 格式化、提交或推送。

## 验证

新增 Core 11 项覆盖未钳制输出与 ALS 最终权重区别、非对称速度/零速度、映射/缩放/钳制顺序、退化输入范围、取消重试、极小距离/零 delta 和非法输入不改历史。相关 Core 62 项及现有 ALS Overlay Import 9 项均通过，0失败/0跳过。Debug/ExportRelease 构建均0警告/0错误。

Debug 和实际 Optimize 的原 Rig 动态轨迹各3360帧/19217实际调用/3360取消重试/18重置/27拒绝/684328比较通过，最大 vector/float 差均0；各 float 使用原逐位门槛。完整 Main＋Rig 各7560帧/7296姿态/7560重试也通过，使用既有解析碰撞夹具，不能据此称真实 Jolt 全轨迹或完整 UE 整图原生验收。

两构建的 ALS 普通场景各1700帧通过，Lyra 普通十角色各480帧/4800最终蒙皮发布通过；两份完整报告与上一地形批次逐项相同。真实 Jolt 地形30/60/120Hz每构建各450/900/1800帧、两角色，共6300最终蒙皮发布，实际台阶/斜坡/落差/蹲姿/ADS/两武器及重试通过；六份完整报告在两构建间及与上一批逐项相同。

最终共14个成功 Godot 进程：两构建各4个 Rig/普通回归＋3个地形回归，日志无ERROR/WARNING。两轮 Optimize 验证逐文件恢复六个Debug DLL/PDB。独立审计 `tools/verify_alpha_core_reuse.py audit` 与 `artifacts/lyra-analysis/alpha-core-v1-audit.json` 通过：6实施/验证源保持冻结，其余4640基线含原资源 JSON和配置不变。原 GASP58/UE没有写入或启动，本批没有重新执行前批的710原包专项审计。

初次 Import 命令在项目目录受到旧 SDK8.0.100 固定版本影响而无法启动；失败日志保留，随后在既有父目录使用已安装 SDK运行成功，未修改 global.json。没有全量managed、十分钟、性能或跨平台验收。

## 范围

本批仅关闭 ALS/Rig Alpha 插值复用。寄存器内存与编译 Rig 遍历仍在 Lyra 适配层，是后续通用 Core 审计候选；不得把这一项说成全部通用引擎机制已迁完。URO、全部 UE 调度及额外 Provider 继续后移，暂缓项保持。

没有新增 GPU截图或实体键鼠验收。前批九张地形/蹲姿/握持样本仍为既有视觉证据；Windows 控制接口的 GetCursorPos 0x80070005 尚无恢复或实体输入回报，完整目标保持开放。
