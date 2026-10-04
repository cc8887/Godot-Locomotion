# Lyra 完整 Main：ALS 遮罩缓存与 additive 数值修正

2026-10-02。直接在当前主目录推进，保留已有未提交修改。人物继续使用 ALS 68 skin / 81 logical；Animation Interface 保持原 14 个 typed 入口、同角色 ItemAnimLayers 组实例、统一 source Sync 和单次模型发布。按用户要求忽略 UE 5.8/5.9 差异。整个 Lyra 移植目标仍开放。

## 修正内容

此前整 Main 诊断遗漏了探针的目标骨架缓存初始化：Carrier 切换到 ALS81 后，Main/Linked proxy 的 Skeleton 仍为 Manny，LayeredBoneBlend 首次 CacheBones 按 Manny 下标生成权重；后续 PreUpdate 刷新 Skeleton，但 RequiredBones serial 没变，错误遮罩留在缓存中。此前 `natural-layer`、`inertia-input` 等采集确实执行了原根，但不能作为正确 ALS 目标遮罩的验收参考。首次失败和当时的报告全部保留，见 [历史诊断](2026-10-02-lyra-whole-main-diagnostic.md)。

探针现在在切换 Skeleton 后，先对 Main 和 Linked 调用原 InitializeObjects，再初始化 RequiredBones、根和 CacheBones；验证两个 proxy 的 Skeleton 身份，并逐帧导出实际 CurrentBoneBlendWeights，与按 ALS 骨名映射的原 profile 一致。Godot 比较器也拒绝遮罩错误的参考，不依赖外部文件名判断有效性。原资产、引擎源码和项目配置没有改动。

同时修正生产 Lyra 的两条 additive 运算：

- AimOffset 保留 mesh 空间累积后的原始 global quaternion，转回 local 后归一化，并保留 RotationOffsetBlendSpace 节点的第二次归一化。
- Main Lean 使用原 Win64 全权重 ISPC 累积次序，并保留 AnimationRuntime 和 ApplyAdditive 节点两次归一化。

依据本机 UE 源码及已保存的安装版 ISPC object 反汇编，复用 AlsQuaternion.MultiplyIsPc 的 FMA/舍入次序。新增 Core 显式全权重方法，仅在这两条 Lyra 调用路径使用；原 LocalApply/MeshApply 默认运算保持原实现。没有抑制近零角、修改惯性公式或放宽门槛。

## 已完成的连续对照

`als-mask-cache` 与 `als-mask-repeat` 两次独立 UE 进程均实际退出0；三 Provider 各60帧，全部 native 字段精确相同。Debug 和实际 ExportRelease Optimize 在三个边界全部通过：原 Main75.Source、最终 Rig 前和完整 Main 最终输出。

随后扩大到每 Provider 完整12秒的移动轨迹。三Provider 30Hz 共1,080帧、60Hz 共2,160帧，UE 均实际退出0，Debug 和实际 Optimize 三个边界全部通过。60Hz长轨迹前60帧与对应独立短轨迹逐字段完全相同；每个 Provider 的整条轨迹连续执行，没有分段重置历史。轨迹包含站立移动、蹲伏、ADS及跳跃/下落，组件旋转始终为零。

保持位置 `1e-8 cm`、四元数 `1e-10`、缩放 `1e-12` 门槛，同时比较曲线 float 位值/存在性/flags、integer attributes 身份和值与 RootMotion，并检查实际访问的 Locomotion 根姿态。Godot 仅读取预先指定的物理观察，不使用 native 状态、权重或播放器时钟驱动生产宿主。

最终完整矩阵如下。每格均包含惯性前、Rig前、完整Main最终输出三个边界，实际进程退出0，Godot无错误/警告；三Hz每构建合计7,560帧。三个UE采集进程均实际退出0，UE临时序列压缩/依赖警告保留，未声明UE零警告。

| Hz | 三Provider帧数 | Debug | 实际 Optimize |
| --- | --- | --- | --- |
| 30 | 1,080 | 三边界通过 | 三边界通过 |
| 60 | 2,160 | 三边界通过 | 三边界通过 |
| 120 | 4,320 | 三边界通过 | 三边界通过 |

相关回归覆盖 AimOffset 7,560帧、Main Aiming Scope 11,340帧、Main Lean Composition 2,100帧、惯性宿主40,320帧，以及普通十角色的换层/重试/武器入口。Debug 各测试实际退出0，首次脚本因下述标记错误退出1；修正后 Optimize 五项全部通过、脚本退出0。Debug 和 ExportRelease Optimize 构建均0警告/0错误。优化验证实际加载 ExportRelease 的六个 DLL/PDB，finally 恢复 Debug 并按SHA256校验。

## 证据

- `artifacts/lyra-analysis/whole-main-{als-mask-cache,als-mask-repeat}-{request,native,closure}.json`：独立正确目标遮罩的短轨迹。
- `artifacts/lyra-analysis/whole-main-movement-{30,60,120}-condensed-{request,native,closure}.json`：每 Provider 完整连续轨迹与采集保护清单。
- `artifacts/lyra-analysis/whole-main-{debug,optimize}-full{30,60,120}*-verification.json` 与对应三个边界日志：实际比较结果，60Hz Optimize 包含回归。
- `artifacts/lyra-analysis/whole-main-debug-math-regressions-*`：保留首次 Debug 标记错误报告和实际成功日志。
- `artifacts/lyra-analysis/whole-main-godot-build-continuous-{debug,optimize}.log`。
- `scripts/verify-lyra-whole-main-diagnostic.ps1` 与 `scenes/tests/lyra_whole_main_diagnostic_smoke.tscn`：可重复执行的实际 Godot 门禁。
- `artifacts/lyra-analysis/whole-main-rounding-audit.py`：原失败报告保留，逐项核对实际Debug成功日志、独立native等价、三Hz两构建与保护清单。

最终审计输出 `artifacts/lyra-analysis/whole-main-rounding-final-integrity.json`：863个旧JSON、709个原uasset、项目描述及8份配置、5个当前探针源文件全部SHA256匹配；六份Debug DLL/PDB已经恢复并与最终Debug报告匹配。六份三Hz/两构建报告全部通过；Debug首次标记错误的 false 报告没有覆盖，实际成功日志单独审计。60Hz各Provider实际访问十个Locomotion状态入口及其余四个原Layer入口。

## 验证基础修正与历史失败

第一次完整60Hz采集在求值后的 JSON 序列化阶段触发 UE 字符串归档2GB上限，进程退出3，未生成可接受 native 文件。保留 `movement-60-full` 失败日志。改用紧凑 JSON，并按独立 Provider 分别序列化完整轨迹；没有拆分同一角色的时钟历史。

首次 Debug 回归报告把惯性宿主成功标记写成 `LYRA_MAIN_INERTIA_POSE_HOST_GODOT_OK`，而实际标记是 `LYRA_MAIN_INERTIA_HOST_GODOT_OK frames=40320`。该测试实际退出0，但脚本因此退出1；原 false 报告保留。脚本已改正，最终审计必须同时核对实际成功标记、退出码和错误/警告，而不能把报告中的 false 静默改成 true。

## 范围与后续

本批连续参考由原 Main 根、真实 Linked Layers、统一 Sync、cache、Slot、惯性、SkeletalControls 与显式 ALS 参考最终 Rig 联合执行，输入仍是受控物理观察。UE 为静态平面世界，Godot 比较器为解析平面；没有 UE CharacterMovement 真实移动仿真，也没有本批新的 Jolt/GPU 对照。组件旋转为零、没有播放 Montage 动作、没有联合原生换层/通知交错验收。

下一步补旋转观察桥与转向轨迹，再扩大动作/换层的完整 Main 连续原生矩阵；复杂地形、近景握持/材质、性能、Shotgun/Feminine 完整图和通用多Group/self/Unlink 仍开放。现有普通入口、装备、root物理与通知的已验证范围以各自记录为准。音频、道具物理和头颈专项暂缓要求保持。
