# Lyra Main 的 ISPC 混合舍入修正

2026-10-03，在主目录推进。Debug/实际 Optimize 的三频、三个动画边界及逐帧取消重试全部通过，最终审计通过。本批保持 ALS skin68 / raw69 / logical81 和十四入口的 ItemAnimLayers 共享实例；普通玩家/NPC仍未加载新 CMC motor 配置。

## 根因与修正

[前批真实 CharacterMovement 验证](2026-10-03-lyra-character-movement.md)在 Pistol、120 Hz、第451帧 bone50 留下 quaternion 差 `1.6216930576451093e-10`，超过原 `1e-10` 门槛。原 UE 双骨 IK 接受 Godot 的实际输入时，全部求解结果精确相同。

本批新增可选、只读的完整阶段姿态导出，读取已经求值的候选缓冲，不增加 source tick，不输入原动画姿态。修正前该帧 Main_Lower/Main_Dynamic 全部骨骼精确同；进入 Aiming 时 pelvis 位移开始相差 `1.4210854715202004e-14` cm，Main_InertiaInput 的最大位移差增为 `2.842170943040401e-14` cm。这个差异在接近伸直的腿部 IK 中放大。

按本机安装版 UE 5.8 的实际路径修正两处：

| 原路径 | 实现修正 |
| --- | --- |
| `AnimationRuntime.ispc::BlendWith` → `VectorLerp` | double 向量、float Alpha 使用 `A + (B-A)*Alpha` 的融合乘加；精确骨遮罩算子显式选择此 ISPC 路径 |
| `AnimationRuntime.cpp::BlendTwoPosesTogether` → `BlendTransformAccumulate` | 第一姿态先 Scale；第二姿态累加的位移/缩放使用融合乘加，quaternion 保留先乘权重再按最短路径相加 |

新增 `AlsPrecisePoseBlender.AccumulateIsPc` 并用于 Aiming 两分支合成；共享骨遮罩算子的 ISPC 选择同时处理位移和缩放。标量路径保留原运算。原 IK、参考骨架、混合权重、时钟、物理查询及所有误差门槛保持。

原失败帧修正后，Aiming 输入、两分支、additive、惯性输入/输出、SkeletalControls 与最终输出，全部骨骼 P/Q/S 逐项与原记录相等。导出功能最初在逐帧取消重试时重复 CreateNew 写入而失败；已限定只导出首次求值，首次失败日志和报告保留，最终另用新路径验证。没有用该报告作为正常运行输入。

## 验证与范围

最终程序集的三频原 CMC 动画对照、动作中换层和回归由 `scripts/verify-lyra-whole-main-diagnostic.ps1` 记录。审计使用 `tools/verify_lyra_character_movement_ispc.py`；前批审计、原捕获和失败报告均保留。新审计核对原捕获 SHA、866 个旧 JSON、709 个原资产包、9 个项目/配置文件、3 个 LyraCharacter 源文件，以及程序集、日志、Debug 恢复和修正前后姿态报告。

| Hz | 每构建参考帧 | 惯性前 / Rig前 / 最终输出 |
| --- | ---: | --- |
| 30 | 720 | 两构建全部通过 |
| 60 | 1440 | 两构建全部通过 |
| 120 | 2880 | 两构建全部通过 |

每构建共 5040 个原参考帧，在每个边界逐帧取消重试。另各 2160 帧动作中换层三边界通过，包含 24 次换类、24 次同类绑定、21 次动作中和6次隐藏图换类；两构建各七项 Aiming/Lean/完整 Main 组件/惯性/通知/实际 Jolt 普通十角色回归通过，普通十角色完整报告相同。最终39个 Godot 进程退出0且无 ERROR/WARNING；24个相关 managed 测试通过、0失败/0跳过，两个构建0警告/0错误，六 Debug 文件逐次 SHA 恢复通过。

结果保存在 `artifacts/lyra-analysis/character-movement-ispc-integrity.json`：`auditPassed=true / comparisonPassed=true / completeAcceptance=false / goalComplete=false`。修正前后逐阶段报告与原120 Hz完整捕获的第451帧独立核对，原帧所有已导出阶段的全部骨骼 P/Q/S 精确相同。新的只读报告导出重试也在完整2880帧中通过。

本批重放真实 UE CharacterMovement 的移动观察和物理查询结果；Godot 自行计算状态、时钟、权重、姿态和查询起止点。普通十角色回归使用实际 Jolt，但仍是此前的简化移动配置。没有本批 UE 启动、资产重导、GPU/近景验收或实际 UE/Jolt 同输入移动等价验收。

整个迁移目标仍 active。下一阶段为玩家/NPC共用原 CMC 配置与速度内核、跳跃/空中/蹲伏净空及实际物理轨迹；复杂地形、近景握持、通用多 Group/Unlink、其他 Provider 全图、独立导出、多平台数学、性能和原暂缓项保持开放。人物与接口复核见 [ALS 与 Animation Interface / Layer](2026-10-03-lyra-als-interface-review.md)。
