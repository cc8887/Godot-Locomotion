# Mantle 原始关键帧到完整姿态

日期：2026-09-24。工作目录 `D:/GodotALS`，分支 `main`。

## 实现和数据身份

新增 `AlsMantlingPoseCompiler`，读取已经只读导出的
`refactored_mantle_animation_inputs.json` 和 `refactored_mantle_root_tracks.json`。
它生成不可变原始姿态资源和每个调用者独立的 sampler；不读取 oracle，也不套用
V4 的 manifest 身份。输出明确使用原生 UE 厘米/骨局部坐标，供 Mantle 运动管线使用。
此资源不是完整 animation-set manifest，不包含曲线求值、事件时间线或场景骨架适配。

编译器检查源文件间 SHA256、序列/骨架身份、时间参数、根关键帧和六个 Montage
的段绑定。根运动与完整姿态必须来自同一版本的数据。骨架包含 68 个实体骨、
79 个逻辑骨，采用 Refactored 自己的 11 个虚拟骨；验证实体骨名、父级、映射及
虚拟骨拓扑。原始 float 通道保留其精度和四元数符号，singleton/空 scale 按已有
规则展开，缺轨仍由所选参考姿态补齐。

采样复用 `AlsRawAnimationPoseData`、`AlsPreciseRawSequenceSampler` 与
`AlsPrecisePoseRetargetModel`：选择原始键 → 每个键生成虚拟骨 → 原生插值 →
重定向 → 根据调用上下文锁根。不会以原生已求值姿态作为输入。

当前编译入口按实际 Mantle 闭包支持一个骨架、非 additive、None retarget source；
遇到不同源参考选择、跨骨架、transform curves 或 animated attributes 明确拒绝。
根锁模式支持 RefPose/AnimFirstFrame/Zero。每个 sampler 不拥有播放时钟、同步、
通知或动作身份，宿主仍须提供这些生命周期。

## 验证结果

`dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Release --no-restore
--filter FullyQualifiedName~AlsMantling`：37 项通过，0 失败/跳过。
包括 15 项本批测试及此前设置、根轨道、运动源和重定向测试。
`dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

独立 oracle 的 3 序列 × 20 个上下文/时间组合，共 60 姿态、4,740 骨 TRS，
其中 660 个是虚拟骨。涵盖 raw、retargeted、asset_root_lock、extract_root_lock，
检查 oracle 源 SHA256 和逐骨身份。全部从原始关键帧开始：

- 最大位置差 `4.5844548931525988e-14 cm`。
- 四元数分量最大差 `3.3306690738754696e-16`（允许等价四元数符号）。
- 缩放差 0；重复同一次请求结果完全相等。

非恒等 OrientAndScale 的独立原生覆盖仍未补齐：这些真实资产的源参考与目标参考
相同，不能用本批结果替代该分支验证。

每个来源由四个并行 owner 共享只读关键帧，各倒序采样 31 个时间点，全部与正序
串行采样位值一致。另有原始 pelvis 键变更测试：修改 authored 数据会改变结果，
没有 oracle 输入。12 项非法数据拒绝覆盖 hash、缺失/重复来源、外来骨架、父级、
重复轨道、根数据分歧、通道长度、additive、源参考及 Montage 缺失/段变更。

证据：`artifacts/mantle-pose/pose-first.trx`（初版 14 项通过）和
`artifacts/mantle-pose/mantle-final.trx`（最终 37 项通过）。无失败复跑、容差放宽
或 native 资产改动。本批未启动 UE、未跑新的 Godot 场景或截图，没有全量测试。

## 后续与保留项

下一步补齐曲线原始输入/求值、完整 manifest/骨架到场景适配、PostLocomotion slot、
Notify/Notify State，然后把 pose 与已实现 motion 绑定到真实 Mantle 探测和动作
生命周期。尚未出现可在普通 Demo 触发的完整 Mantle，不据此宣布 gameplay 完成。

上一批默认移动测试的旧 oracle/recovery 闭包不匹配仍未修复；复杂相机、物理稳定性
旧 9/12、Flail 0/3、最终视觉验收及十分钟性能预算继续保留。头颈、道具物理及音频
继续暂缓。

用户计划和头颈诊断文件未动。本轮还观察到 `project.godot` 和一批 `.cs.uid`
出现外部改动；本批没有启动 Godot 编辑器或编辑这些文件，全部保留且不纳入提交。
