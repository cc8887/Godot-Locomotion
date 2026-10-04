# Lyra 默认 Main 的连续 worker 与零源事务

本批补齐原始 Main 完整根的初始 self、Link、Unlink、再次 Link、同类 Link 和再次 Unlink 连续参考，并新增共享 Main owner 的零 Linked 实例帧宿主。它提交真实 worker 更新，保持未遍历源和图历史，执行原 self SkeletalControls 空根。当前普通角色还没有选择这个分支；完整角色 Unlink、重新绑定共同 Sync/缓存和默认输出后的 Godot 最终 Rig 均继续开放，整个移植目标 active。

## 原生连续参考

新增独立可选 `tools/unreal/LyraDefaultMainOracle`，使用原 Main、Unarmed Provider、Manny 164 骨、实际 SkeletalMeshComponent 和最终 ControlRig。请求为三频各六秒的 Link/Unlink 生命周期，再加三频各六秒的全程 self，共六轨迹、2520 帧。位置、速度、加速度、转向、蹲伏、空中、瞄准和开火为受控物理输入；没有 CharacterMovement 积分或播放 Montage。

探针直接调用原组件/AnimInstance 更新、完整根求值和曲线发布。仅对 SkeletalControls 输入与最终 Rig 输入增加可转发计数 tap，恢复原 PoseLink，记录 Main 前/更新后/求值后字段、double 原始位、真实目标、源时钟和状态机。实际 Actor 的旋转与基础瞄准角也作为采集边界记录；不能直接用请求的未规范化 yaw 代替实际 Rotator。

| 轨迹 | 帧数 | self / 外部帧 | self 求值 |
| --- | ---: | ---: | ---: |
| 30Hz Link/Unlink | 180 | 120 / 60 | 104 |
| 60Hz Link/Unlink | 360 | 240 / 120 | 207 |
| 120Hz Link/Unlink | 720 | 480 / 240 | 412 |
| 30Hz 全 self | 180 | 180 / 0 | 155 |
| 60Hz 全 self | 360 | 360 / 0 | 309 |
| 120Hz 全 self | 720 | 720 / 0 | 618 |

2100 个 self 帧均没有 Linked 实例，十四个目标归 Main；SkeletalControls 的输入 Update/Evaluate 为零，而 Main worker 仍观测本帧位置/速度并清除 FirstUpdate。Main Locomotion 状态和 elapsed、三个 Lean 时钟在 self 期间保持前帧值。再次 Link 后输入恢复遍历。稀疏求值不阻止 worker 更新。

1805 个 self 求值的 pre-Rig 姿态均为同一参考姿态、曲线为空；这些求值中原最终 Rig 都改变了输出。**空 Layer 根截断上游，不截断根之后的 Rig。** Godot 完整 Unlink 接入不能仅返回参考姿态并跳过整个 Main。

`default-main-v5` 与 `default-main-v5-repeat` 两独立 UE 进程退出 0，request/native/closure 三份 JSON 各自字节相同。保留原 GameplayTag 资源警告；没有 Error/Fatal/Ensure。本批无资产保存或资源重导，原 870 份 JSON（869 原有 + 前批新增默认图）、710 份原包和 9 份配置保持。当前 AnimInstanceProxy 与 LinkedAnimLayer/LinkedAnimGraph 三份 UE 源逐字复制到 `artifacts/lyra-analysis/default-main-v5-engine-source/`，按 SHA256 保护。

## 实现

`LyraMainGraphStateOwner.ValidateUntraversed/CommitUntraversed` 校验真实帧 writer 和候选，拒绝夹带 Lean 源工作；只提交 Main 的 worker 状态。不用空 Lean Prepare 来模拟未访问，因为那会初始化源并改变历史。

这里的“未遍历”限定为 Update/Evaluate。原 `FAnimNode_LinkedAnimGraph::Initialize_AnyThread` 和 CacheBones 会遍历全部 Pose 输入，即使 linked 默认根不消费它们。新宿主尚未实现这两个阶段的完整默认图初始化；不能把零 Update/Evaluate 推导为初次绑定时跳过全部输入初始化。普通 Unlink 接入还需要保持此区别。

`LyraMainDefaultRootFrameHost` 绑定共享 Main owner、角色归属与十四个 self 调用，实例登记表为空。Prepare 执行 Main 更新和真实 SkeletalControls 调用路由；Evaluate 使用空根，给出 ALS 81 骨 pre-Rig 输出；Commit/Cancel 保留角色事务和身份。没有创建替代 Provider、播放器或缓存。普通上下文使用 ALS reference；新整链夹具验证 fresh context 的空曲线、属性和根运动。前批调用根夹具继续验证 prefilled/additive context 的数据保留。

新 Godot 场景 `scenes/tests/lyra_main_default_root_smoke.tscn` 消费全程 self 三频原生物理输入，1260 帧每帧先取消再重试，比较 35 个真实 worker 字段共 88,200 次。double 位级和向量原 `1e-10` 门槛保持。1082 个求值验证 ALS pre-Rig 参考与完整空数据，重复求值、update-only、未提交不可见、过期/重复/外部角色拒绝，以及 Lean 和图历史不变均通过。该默认分支还没有连接普通角色的实际 Unlink 入口。

## 最终验证

Debug 与实际 ExportRelease Optimize 各 0 警告/0 错误，Core59 通过、0 失败/跳过。两构建各 11 次 Godot 进程、共 22 次退出 0，无 ERROR/WARNING；每构建含新默认 Main1260帧/逐帧retry、原调用根矩阵、通知/Reload换装/实时通知、普通十角色/Emote及四布局完整外部 Main最终Rig4320帧/逐帧retry。指定外部实例字段门槛保持：single/per-call34/47、three-groups/mixed32/47，不能称全字段验收。

普通十角色/Emote完整报告两构建相同且与前批基线相同。独立审计核对28份当前冻结源、前批178源中173份未改源码、原资源和探针包来源；三组Optimize备份的六份Debug DLL/PDB逐字恢复。审计报告为 `default-main-v5-integrity.json`，终态标记为 `LYRA_DEFAULT_MAIN_AUDIT_OK processes=22 core=59 native=2520 self=2100 external=420 ordinaryUnlink=false`。

最终证据命名为 `artifacts/lyra-analysis/default-main-v5-*`。复现需要现有 ignored 资源、最终 Debug/ExportRelease 程序集，并使用新标签保留旧证据：

```powershell
& ./scripts/build-lyra-default-main-oracle.ps1 -EngineRoot '../UE_5.8' -UnrealProject '../GASP58/GASP58.uproject' -PackageName <new-package>
& ./scripts/capture-lyra-default-main.ps1 -PackageName <new-package> -RunTag <new-tag>
& ./scripts/verify-lyra-default-main.ps1 -Configuration Debug -EvidenceTag <new-evidence-tag>
& ./scripts/verify-lyra-default-main.ps1 -Configuration Optimize -EvidenceTag <new-evidence-tag>
```

Godot 门禁固定消费已捕获的 v5 参考；独立审计 `tools/verify_lyra_default_main.py` 固定核对本批冻结证据。

## 修正与剩余边界

第一包直接访问 ControlRig 的 protected Source，编译拒绝，改为已公开反射属性读取。v2 采样没有推进全局帧号，Main worker 被一次/帧门禁跳过，虽然图遍历成功，也不接受为连续 worker 参考；v3 后按请求帧推进并恢复全局帧号，增加本帧位置/速度/FirstUpdate 检查。原有 whole-Main 探针已包含该帧号处理，此问题限于本次新探针。

首次 managed 原生比较在 yaw 跨过 180° 时失败：请求 yaw 与 Actor 实际规范化 Rotator 不同，局部速度差超原门槛。v5 采集实际 Actor 输入后通过，未用原生输出状态填充 managed 状态或放宽精度。一次旧 v4 重复捕获在源码调整期间触发保护哈希拒绝，保留失败；最终 v5 源码冻结后两次独立捕获完全一致。首次夹具 FrameId 属性名编译错误与一次 Core logger 参数错误也保留，修正后重新运行。

本批关闭 Main 未遍历源提交和受控零 Linked 帧宿主，不关闭普通完整 Main Unlink。下一步将真实 GraphSet/绑定变化接入该帧分支，保留 Main 与 Montage、空批次 Sync 双缓冲和旧缓存/状态机历史，并继续执行 ALS 最终 Rig73；覆盖普通 Link→Unlink→reLink、部分绑定和跨帧取消，再采集 ALS 81 骨完整默认输出参考。Self Aiming 参数、任意默认非空图、重复调用点、不同 Provider 混合、共享/持久实例仍待。

原 Linked 字段34/47、完整物理314/1680差异、握持/脊柱近景及全部既有开放项保持；音频、道具物理和头颈专项继续暂缓。本批没有默认 Montage/世界销毁、Godot 默认最终 Rig、新 ALS 默认姿态原生 oracle、GPU、全量 managed、十分钟或性能验收。
