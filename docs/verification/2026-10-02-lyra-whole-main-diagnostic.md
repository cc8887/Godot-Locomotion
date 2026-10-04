# Lyra 完整 Main 连续对照诊断

后续发现本记录的探针在首次 CacheBones 时仍缓存 Manny Skeleton，实际 ALS 上身遮罩不正确；这些采集保留作诊断历史，不能作为目标骨架验收。目标缓存和 Lyra additive 数值次序已修正，正确 ALS81 参考下短轨迹及完整60Hz轨迹三个边界通过，最新范围见 [遮罩与数值修正](2026-10-02-lyra-whole-main-rounding.md)。下文为修正前的原始结果。

2026-10-02。直接使用当前主目录；本批完整 Main 严格对照尚未通过，整个移植目标继续开放。人物保持 ALS 68 skin / 81 logical，三种原 Provider 共用当前 typed ItemAnimLayers 架构。按用户要求不展开 UE 5.8/5.9 差异。

## 原生执行范围

新建可选 `tools/unreal/LyraWholeMainOracle` 探针及 `tools/unreal/capture_lyra_whole_main.py`，实际执行原 ABP_Mannequin_Base 完整根节点、动态 Linked Layers、原 Update、一次共享 Sync、cache、Slot、惯性、SkeletalControls 与 ALS 参考最终 ControlRig。Unarmed/Pistol/Rifle 各 60Hz × 60 帧，共 180 帧。输入为预先指定的物理观察，不由原生输出的状态、权重、时钟或姿态驱动 Godot。

原动画离线重定向数据在 UE 临时重建为 245 个 ALS81 序列；通知仅复制到 Transient 对象，原资源未保存。源资源映射涵盖 player/evaluator/BlendSpace、Idle_Breaks 数组及 additive base；骨名适配沿当前 ALS 路线。探针验证实际根节点及 Linked 目标身份，并要求发生真实 Layer 更新/求值，避免仅采到参考姿态。

修正了两处探针生命周期错误：动态链接后重复 InitializeRootNode 会恢复 self layers，改为仅初始化已绑定根；直接代理求值缺少 CachedPoseScope，改用原 ParallelEvaluateAnimation 入口。撤除每帧强写 HipFireUpperBodyOverrideWeight，保持该变量的原生历史。Main_InertiaInput 为实际 node75.Source 的转发探针，未替换其算法。

两独立 UE 进程 `natural-layer` 和 `inertia-input` 实际退出 0。去除后者新增的 Main_InertiaInput 记录后，所有原有 native 输出与更新记录完全相同。Tap 记录按入口分桶，不作为全图访问次序的证明。

## Godot 结果

Debug 与实际 ExportRelease Optimize 构建均 0 错误 / 0 警告。验证脚本运行优化 DLL，并在 finally 恢复六个 Debug DLL/PDB，SHA256 一致。

| 比较边界 | Debug | Optimize | 范围 |
| --- | --- | --- | --- |
| Main75.Source 惯性前 | 退出0，通过 | 退出0，通过 | 三 Provider / 180 帧完整姿态与通道 |
| FullBody_SkeletalControls 最终 Rig 前 | 退出1，失败 | 退出1，失败 | 首个失败 unarmed/53/bone45 |
| 完整 Main 最终输出 | 退出1，失败 | 退出1，失败 | 同一首个失败 |

保留位置 1e-8 cm、四元数 1e-10、缩放 1e-12 门槛；曲线 float 位值、存在性/flags、integer attributes 身份和值、RootMotion 同时比较。惯性前完整 180 帧通过，各已访问的 Locomotion 根姿态亦检查。失败骨为 lowerarm_twist_01_r，位置与缩放差0，四元数差 `1.472930935098454E-08`，未放宽门槛、未跳过该骨。

定位到上游四元数末位舍入差异被 Main75 近零角度计算放大：原 AnimNode_Inertialization 用上一旋转乘当前逆旋转，随后 2*acos(W)；此轨迹 native 的 W 略小于1而 Godot 略大于1，在夹取边界产生不同角度。惯性前正常精度门槛无法暴露该 ULP 差异。MeshApply 和 AimOffset 归一化两项实验均未解决完整输出失败，已撤回，当前生产运行算法保留此前基线。

## 证据与保护范围

- `artifacts/lyra-analysis/whole-main-{natural-layer,inertia-input}-{request,native,closure}.json`
- `artifacts/lyra-analysis/whole-main-native-{natural-layer,inertia-input}.log`：独立原生采集。
- `artifacts/lyra-analysis/whole-main-{debug,optimize}-restored-verification.json` 与三个边界日志：实际退出码与第一失败。
- `artifacts/lyra-analysis/whole-main-final-integrity.json`：863 旧 JSON、709 原 uasset、项目描述及8份配置、5个探针源文件哈希当前匹配；独立原生输出等价。
- `artifacts/lyra-analysis/whole-main-godot-build-final-{debug,optimize}.log`。
- `scripts/build-lyra-whole-main-oracle.ps1`、`scripts/verify-lyra-whole-main-diagnostic.ps1`、`scenes/tests/lyra_whole_main_diagnostic_smoke.tscn`。验证脚本在最终输出不通过时整体退出1，失败不会被报告为验收通过。

原项目描述/配置、引擎源码和原资产未修改；可选插件临时构建后归入 artifacts/unreal。早期未发生真实 Linked 求值的输出、缺少缓存 scope 的崩溃、强写历史的采集及各失败编译/试验日志均保留，不能使用这些早期记录代替当前证明。UE 临时序列重建有压缩/依赖警告，本批不声明 UE 零警告。

## 限制与后续

本批为静态平面、零组件旋转、指定物理观察的短轨迹，没有真实 UE CharacterMovement 仿真；Godot 比较器使用解析平面而非 Jolt。移动参数为此受控用例的默认配置，扩展前需采集原生实际物理快照。没有播放 Montage 动作，也未覆盖三Hz、换层、完整通知/动作交错、复杂地形、GPU渲染、性能或全范围验收。旧有生产 Jolt/普通入口验证见各自记录，本批没有重跑。

下一步沿 LeftHand/cache/UpperBody/AimOffset 到 Main75.Source 捕获原始数值位，找到首次舍入分歧并修正对应运算次序；禁止抑制原近零角或放宽门槛绕过问题。修正后再扩大整图连续轨迹及动作/换层矩阵。单组14入口方案已用于当前 Lyra，多个 Layer Group、默认 self、Unlink 及任意动画接口的通用支持仍需独立实现与验收。
