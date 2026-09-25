# Refactored 移动方向子图原生连续对照

## 覆盖与结果

原 UE Standing/Crouching 实例保持真实 Movement States 根、共享 cache、Sync 与姿态求值，显式设置非零 VelocityBlend、动态 Stride/WalkRun/PlayRate、Gait、SprintAcceleration、SprintBlock、RotationYawOffsets、换髋锁和交错步。脚本 `tools/unreal/export_refactored_direction_sources.py` 新增 30/60/120 Hz × 两 stance 共 1680 帧；其中 1640 帧有源播放器更新，1272 帧有多个活动过渡。

每场景六方向状态均覆盖；Standing 八个实际移动源（六 BlendSpace、两个 Sprint Sequence）及 Crouching 六个 Sequence 全部参与。共 9239 个玩家 tick、9411 次缓存求值、132720 个骨骼姿态参与对照。C# 从输入独立计算状态、权重、源时钟和姿态，没有使用原生输出驱动采样。

| 项目 | 最大绝对差 | 沿用预算 |
| --- | ---: | ---: |
| 玩家累计时间 | 0 | 2e-6 |
| 玩家/缓存权重 | 0 | 2e-6 |
| 骨骼位置 | 7.097004612433011e-6 cm | 1e-4 cm |
| 四元数同半球分量 | 1.1394327932567894e-8 | 2e-6 |
| scale | 1.643672321582912e-7 | 2e-6 |
| 曲线值 | 2.3841858e-7 | 2e-6 |

曲线名称存在性逐帧一致；不只比较非零值。时钟只比较本帧实际更新的玩家，原生隐藏节点的旧 CachedBlendWeight 不视为新 tick。结果在既定预算内，未宣称 pose 逐位相同。完整角色仍未由此证明完成。

## 原生对照发现并修复的问题

首次 `native-initial.trx` 六项均失败，文件保留：

- Standing 30/60、Crouching 三频率均首次在两秒处显式重新初始化时出现时钟差。此前只重置玩家节点，Sync 组仍保留上一实例历史。`AlsRefactoredSourcePlayerRuntime.Prepare` 增加显式 `reinitializeInstance` 候选选项：本帧不沿用组同步历史；提交才清理保留样本；隐藏玩家保留 pending reset，在下一次 tick 使用原始起点重新初始化，epoch 单调增加。局部节点 reinitialize 不清除整个 Sync 组。
- Standing 120 第 185 帧全部速度通道为零，第 186 帧零 delta 恢复时，BlendSpace 125 原生时间 .5582982，移植侧变成 0。原 UE BlendSpace 节点在隐藏时保留自身 BlendSampleDataCache，之前 C# 仅保存上一帧参与 Sync 的样本，导致历史丢失。现在单独保留每节点已提交样本，重新加入组时只补回样本缓存，并使玩家级同步 marker 仍为 Invalid（新组成员）；不复活旧组 leader，不擅自推进隐藏时钟。

修复由本地 `BlendSpace.cpp` 零 delta follower 分支和现有 Sync 模型共同确认。`native-history.trx` 六项通过。最终每 17 帧和全部实例重置帧额外取消/重试，玩家、样本、组历史、pose、curve 完全一致；新增独立测试验证 reset 撤销不泄漏、隐藏玩家延迟 reset、空输入 reset 后恢复及 epoch 单调增加。相关方向测试调用端同步传递整实例 reset。

## 构建、导出和资产

按 `ue-diagnosing-plugin-build-load` 技能执行完整 Editor 目标构建及四插件审计，4 actions 成功；没有叶模块 DLL 复制、BuildId 修改或 Live Coding。构建前确认 EngineAssociation 与 UE 5.9，未有运行中的 Editor。

构建记录：`../AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260925T024719755Z-937b03fc87f2453a832e0a8de6ec8076-*`。

BuildId：`c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`；审计指纹：`FB0BF00FA8B23DDABC72AA350F2DA6DB39DB26D8F5673C1C8BF5058BEA3EF664`。

Cold commandlet 退出 0；普通 Editor PID 19052 退出 0。两次 1680 帧导出对应文件逐字节一致：

- `assets/config/refactored_direction_sources_Standing.json`，18389412 bytes，SHA256 `119B17D3AECDD627764B3CC3424E4BBB5D446593577CD2D9EF5A313F19D4A974`。
- `assets/config/refactored_direction_sources_Crouching.json`，17269916 bytes，SHA256 `96F80425F6F577DDE5706141A0B66D90390FB159F5942E73EC5D148AADB052C3`。

导出器使用 opt-in `directionSources`，新增 Direction Gait 输入、Sequence/BlendSpace 时钟和 SaveCachedPose 权重采集。旧 Default 单独冷重导退出 0，SHA256 仍为 `B385B21EC70E2CA8274C9C8C78E691717E84762F7F98BF7144728242020D0003`，与入库参考字节一致。

DataValidation 退出 0，0 errors / 3 条旧 warnings。普通 Editor 仍有两条旧 Condition failed 和五类旧 warnings，未声称修复这些问题。没有资产保存，没有新打包验收（项目没有本阶段要求的 UE 打包流水线），没有 Godot 场景或十分钟性能运行。

## 测试及下一步

`artifacts/refactored-direction-moving-native/related.trx` 33 项通过：新移动原生对照 6、共享播放器 3（含新 reset 1）、既有原生 Sync 连续 1、武器完整 Overlay 原生 12、方向姿态 11。最终调用端 reset 修正的专项回归 `reset-callers.trx` 21 项通过（方向姿态 11、方向源 10；与前述回归有重叠，不累加为独立用例数）。Godot Optimize 构建 0 warning / 0 error。

方向子图的非零移动姿态证据现在补齐；仍是受控 Parent 输入的子图，尚无实际 Parent 更新/ActivatePivot 消费、Movement Details/Standing/Crouching 主状态与 Stop States 的完整闭环，外层缓存及 inertialization 消息仍须统一。下一步推进这些，而非把本子图通过视为完整 ALS 已完成。

普通 Demo 尚未切入完整 Refactored 链；Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终性能等旧缺口保留。保留用户未提交修改；音频、道具物理及头颈诊断继续暂缓。
