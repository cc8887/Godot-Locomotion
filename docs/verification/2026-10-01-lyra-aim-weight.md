# Lyra Provider 瞄准权重与原 Aiming 输入引脚

2026-10-01，在当前主目录继续 FullBody_Aiming 前置组件。沿用 ALS skin68/raw69/logical81 资源路线；本批没有新增动画或修改骨架。忽略 UE5.8/5.9 差异。本批验证的是原 Provider 图前权重函数和已编译的暴露输入处理器，不是完整瞄准姿态入口。

## 本批实现

`LyraAimWeightHost` 从原 Provider CDO 初始化 HipFire=0、AimOffset=1，按原 `Update Blend Weight Data(double DeltaTime)` 更新两个 double 字段。Unarmed/Pistol 的 `RaiseWeaponAfterFiringWhenCrouched=false`，Rifle 为 true，三者开火保持时间均为 double 0.5。分支优先级、严格开火时间和 RootYaw 阈值、上一 enclosing Main 的 `applyHipfireOverridePose` 曲线反馈均保持原语义。HipFire 先更新，之后 AimOffset 的 SelectFloat 读取这个新 double 值；直到原图的 float 引脚才量化。FInterpTo 近零比较使用原 `UE_SMALL_NUMBER` 的 float 常量提升到 double。

权重更新有候选/校验/提交/取消边界；重复 Prepare、外国候选、已取消候选、重试后的旧候选和重复 Commit 拒绝。组件没有动画时钟或骨架写入，尚未接 Main 图前生产更新，旧 Demo 的权重路径未替换。

`LyraAimingGraphDefinition` 校验三个 Provider 原8节点闭包：Root81→TwoWay77→Relaxed79/Idle74，两支分别经 UseCache76/75 汇入 SaveCache78→LinkedInput80。保留 PreAimPose 输入、无节点回调、非root-space旋转 additive、原 DoNotSync/循环/LOD/alpha 配置和两个 CDO 资源绑定。`Expose` 接收 double AimYaw/AimPitch/AimOffset 权重，仅在原引脚边界转 float。这个定义检查没有执行 Cache Update、源 tick 或姿态采样。

新增独立的 `UAlsLyraAimWeightLibrary` 探针。真实 Main 用 LinkAnimClassLayers 创建实际 ItemAnimLayers Provider 实例，跑 PropertyAccess 批次，ProcessEvent 调用原 Blueprint 权重函数；随后直接执行原 TwoWay77/RotationOffset79/74 的编译 exposed handler，读取 actual pins。Main 最终曲线复制只在受控 evaluateMain 帧发生，其余帧保留反馈。Main字段、最终曲线和入口访问由夹具提供；本批不是整个 Main 执行。

## 原生对照与保护

三 Provider × 30/60/120 Hz × 18秒，共11340帧。两个独立 UE5.8.1 commandlet 进程均权威退出0，第二次逐字段检查既有不可变输出一致；各摘要0错误/692警告，旧 Footstep GameplayTag 等资产警告仍在。

| 验证项目 | 结果 |
| --- | --- |
| double HipFire/AimOffset 与已提交历史 | 全11340帧逐位一致 |
| 原节点 float 权重/Yaw/Pitch 引脚 | 10866访问帧逐位一致 |
| 未访问 Aiming 入口但仍执行图前权重更新 | 474帧 |
| Main 不求值、保留上一反馈 | 1620帧 |
| 零 delta / 大 delta 插值钳制 | 108 / 33帧 |
| 两支混合区间 | 2885帧 |
| 每帧取消/重试 | 11340次 |
| 五类错误操作拒绝 | 56700次 |

保护508原包、661此前JSON字节哈希；外部插件 source/package 与探针源一致，GASP宿主插件没有替换。新增 ignored 不可变 `aim_weight_v1_{requests,policy,native}.json` 分别3346752/2277/3454179字节。JSON间的字节依赖保持。

Godot Debug 与 Optimize ExportRelease 构建0警告0错误。实际 Godot 4.7.2 .NET 权重场景退出0且无ERROR/WARNING；既有 Additives 组件场景复跑通过。原 ALS 逻辑源场景本批复跑234源/936样本，raw69/logical81/skin68，位置最大1.4163191318420816e-13cm、四元数6.58317845524286e-16，无ERROR/WARNING。新验证器检查两次UE权威退出、全部旧JSON/资产/探针源哈希和Godot成功标志；旧Additives/LeftHand验证器保持通过。

证据在 `artifacts/lyra-analysis/`：

- `aim-weight-ue-build.log`：首次编译失败，MakeShared 返回 TSharedRef 后误调用 ToSharedRef；已经修正，日志保留。
- `aim-weight-ue-build-final.log`：UAT成功退出0。
- `aim-weight-ue-export.log`、`aim-weight-ue-export-repeat.log`：原生采集及权威退出。
- `aim-weight-godot.log`：11340帧/10866引脚/56700拒绝通过。
- `aim-weight-debug-final.log`、`aim-weight-optimize.log`：0/0构建。
- `aim-weight-additives-regression.log`：旧完整 Additives 组件回归。
- `aim-weight-logical-regression.log`：原 ALS 逻辑骨架与234源采样回归。

复跑：先按既有 ExternalOnly 方式构建插件，再运行 `scripts/export-lyra-aim-weight.ps1`；Godot 执行 `scenes/tests/lyra_aim_weight_smoke.tscn`。`tools/verify_lyra_aim_weight.py --ue-log artifacts/lyra-analysis/aim-weight-ue-export.log --ue-log artifacts/lyra-analysis/aim-weight-ue-export-repeat.log --godot-log artifacts/lyra-analysis/aim-weight-godot.log` 检查交付。

## 后续边界

当前仍是12/14入口的实际组件执行组，本批不增加已完成入口数。下一步把45个现有 mesh-space additive 样本接原两套 BlendSpace 的独立 occurrence、滤波/权重历史/时钟，原缓存输入只遍历一次，两个分支各自先叠加到同一 PreAimPose，再按原 TwoWay 混合完整骨骼/曲线/属性/RootMotion。权重组件应由同一 ItemAnimLayers 实例在图前执行，统一取消/提交；实际 Aiming 放回原 Main 槽之后、FullBodyAdditives之前的边界。随后 SkeletalControls、完整Main/换类、统一事件、普通Demo、视觉与性能验收继续开放。本批没有UE连续完整瞄准姿态oracle或生产入口验收。
