# Lyra FootPlant 完整输出与 Main73 宿主接入

2026-10-02；直接在主目录实施，无 worktree、提交或推送。沿用 ALS 模型、蒙皮与 81 通道目标布局，不修改 UE 资产或源码，不启动 UE。完整 Lyra 迁移目标仍进行中。

## 已实现与验收边界

`LyraFootPlantRigOutputTransfer` 按原优化 PoseAdapter 的目标骨身份与父序，先 materialize global 再 local。69 个映射目标骨保留 Rig 缓存语义，12 个未映射目标通道保留原输入 local；父名不匹配的条目使用 Rig 父空间，不能统一按 ALS 父重新转换。部分权重按 `ConvertPoseToAdditive` → local `AccumulateAdditivePose` 执行，保留原正规化次数、scale reciprocal 与 shortest quaternion 路径。关闭与全权重使用原 1e-5 门槛。

此原 VM 没有曲线/动画属性写入单元，曲线值/存在性/flags、整数属性身份与 root transform 属性保留完整通道。部分权重的 root 属性仍经过 additive arithmetic；原生样本有 2154 个 root 属性、2148 个非零平移 root、8616 个整数属性。当前原生输出样本 curve flags 均为 0，不称本批新增了非零 flags 原生覆盖。

`LyraFootPlantRigPoseHost` 将节点 BoolBlend、RigDelta、原 VM 寄存器、98 项层级缓存、五个弹簧及两个 Alpha 的历史放在同一角色候选里。Prepare 从已提交状态克隆，Construction 与输入属性传播属于本帧；每次 Evaluate 从 prepared 状态开始，失败候选拒绝提交，Cancel 整体丢弃。update-only 可以提交更新/初始化而不求姿态；没有独立 Sync 时钟。

`LyraMainPoseHost` 新增显式 `enableFinalFootPlant`，默认 false。启用后两条路径均为 Main Slot/原 linked layers → Main75 inertia → RootYaw72 → provider SkeletalControls → ControlRig73 → 最终反馈。Rig Bool 使用已提交 `DisableLegIK <= 0 && !UseFootPlacement`，isCrouching/HasVelocity 使用当前 Main 观察；`EnableControlRig=false` 不替代原最终 Bool 绑定。Main 与 Rig 统一预校验/提交/取消，缺少碰撞 provider 使本帧失败。

## 验证结果

输入和期望完整输出仍来自未加额外观察代码的 `footplant_rig_ground_v2_native.json`，SHA256 `3ddc3ba4b9851a3e6cba3d5ae0b3c5ca64c30c98f75be86e4bb32417e5008ec9`。本批只离线生成 `rig_output_v1_native.json` 和 6468 字节运行 policy；运行宿主读取实际程序/图/设置/骨架合同，不读取 native 输出。观察版 inputs-v1 的完整求解仍不能替代原 oracle。

| 门禁 | Debug | Optimize |
| --- | --- | --- |
| 构建 | 0 错误/0 警告 | 0 错误/0 警告 |
| 原 Rig 轨迹 | 2520 帧、2154 姿态、174474 骨 | 相同 |
| 部分/关闭输出 | 156/153 | 相同 |
| 原完整 VM | 2001 solves、683343 指令访问 | 相同 |
| 检查/通道检查 | 10279440/43080 | 相同 |
| 最大位置/四元数分量差 | 2.842170943040401e-14 cm / 2.220446049250313e-16 | 相同 |
| Main73 组合 | 7560 帧、7296 姿态、三 profile×30/60/120Hz | 相同 |
| Main 取消重试 | 每帧，共 7560 | 相同 |
| Main 部分/关闭/隐藏 | 2097/1479/264 | 相同 |
| Main Locomotion 被真实 Slot 覆盖 | 4689 | 相同 |
| Main 晚期碰撞失败/重复求值 | 63/75 | 相同 |

Rig 输出门禁同时执行独立 immediate backend 和实际 pose host，逐帧取消重试，并在部分帧重复 pose host 求值，验证 complete pose/curve/integer/root 通道。记录命中 provider 先核对实际计算的 query 参数，不能提供数学/骨骼答案。原位移和四元数门槛分别保持 1e-8 cm/1e-10，无放宽。

Main 门禁用真实生产类、两个独立 physical Montage runtime、原请求前 12 秒、独立完整 Main 基线再接 Rig 作为边界对照；输入来自受控观察，碰撞是解析平面。启用最终 Rig 的 pose 与该独立组合逐值相同，5817 帧姿态改变，源/Sync 历史与基线一致。包含缺 provider、晚期碰撞异常、无反馈提交、隐藏求值、旧视图失效及统一取消。只验证组合位置与事务，不是新 UE 完整 Main 连续 oracle 或 Godot 真实碰撞验收。

Optimize 还复跑旧 immediate solver：2520 帧/6552576 比较、maxP2.84217e-14 cm、已比较 Q 差 0。运行结束恢复六个 Debug DLL/PDB，SHA256 全部一致。旧 669 个 UE 包、808 份 JSON 和原探针/外部 package 源哈希均保持。

## 失败证据与修正

- 首次完整输出的姿态/通道比较已通过，末尾计数断言错误：164 个严格 0<alpha<1 样本中，4 个极小值按原门槛关闭、4 个近 1 值按原门槛全权重，实际 partial 为 156，关闭为 153。只修覆盖计数，未改算法/误差门槛。日志 `rig-output-godot-first.log` 保留。
- 新 pose host 编译遇 ref struct 输入引用逃逸，增加 `scoped in` 明确返回缓冲不引用输入变量；失败 `rig-output-debug-build-host.log` 与成功修订日志均保留。该失败构建后误运行的旧二进制日志 `rig-output-godot-host.log` 不作为验收证据。
- Main 首次重复求值测试误要求整 Main 输出不变。既有 inertia 会在零 pending delta 再捕获求值历史；调整为重复独立完整 Main 基线，再接独立 Rig 精确比较，并断言两边 pending delta 为 0。未修改原 inertia 行为/门槛，首次失败 `rig-output-main-godot-first.log` 保留。Rig 自身相同输入重复求值的姿态仍逐值不变。

## 可复查记录与后续

- `artifacts/lyra-analysis/rig-output-debug-build-main-fixed.log`
- `artifacts/lyra-analysis/rig-output-optimize-build.log`
- `artifacts/lyra-analysis/rig-output-godot-host-fixed.log`
- `artifacts/lyra-analysis/rig-output-main-godot-fixed.log`
- `artifacts/lyra-analysis/rig-output-godot-optimize.log`
- `artifacts/lyra-analysis/rig-output-verification.log`
- `artifacts/lyra-analysis/lyra-rig-output-verification.json`
- `tools/prepare_lyra_rig_output.py`、`tools/verify_lyra_rig_output.py`
- `scripts/verify-lyra-rig-output-optimize.ps1`

下一依赖是 Godot 真实物理实例、厘米/米与组件/世界转换、Traversable 通道映射、自体排除、候选生命周期及斜坡/地形接触验证。原 Rig 仍使用 authored Manny 参考腿长；ALS 比例 profile、普通 Demo 默认启用、动态 provider 换类/多角色、生产通知/root physics 消费、新完整 Main native 对照及视觉/性能验收仍开放。
