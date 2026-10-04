# Lyra 原 Orientation → Stride 双 Warp 与 ALS 骨架

2026-10-01，在主目录继续实施。源资产为 GASP58，原生端为安装版 UE 5.8.1，运行端为 Godot 4.7.2 .NET；继续使用原 ALS 68 skin / 81 logical，未修改或保存源骨架。在 [OrientationWarping](2026-10-01-lyra-orientation-warping.md) 后增加原 StrideWarping。输出现在为 `AfterStride`，原 Main 输入/完整遍历与生产入口仍未接入，整体移植目标保持开放。

## 原配置与脚骨

三个实际 provider 的 Cycle 编译闭包为 Root → CS-to-local → Stride → Orientation → local-to-CS → LayeredBlend → Cycle player/HipFire evaluator。两 Warp 之间没有空间转换。本批保持原节点顺序与一份 FCSPose；Godot 新 `AlsCycleWarping` 共用同一组件姿态，避免 Orientation 导出 local 后再重建缓存。

Stride 原 CDO 为 Graph 模式、minimum root speed 10 cm/s、stride scale clamp 0..1、增加/减少插值速度均 10。Floor/Gravity 为原 WorldSpace up/down，沿地面法线定向、FK thigh 补偿、FK 腿长限制、缺 root 属性禁用均开启。Float alpha 走原默认 scale/bias 路径。Runtime policy 核对原图、绑定、字段类型和字节依赖，拒绝其它配置路径。

原 foot definition 顺序为右后左：`ik_foot_r/foot_r/thigh_r`、`ik_foot_l/foot_l/thigh_l`，pelvis=`pelvis`，foot root=`ik_foot_root`。这些骨在 ALS81 上均有效，不需要新蒙皮或改名。前置 Orientation 继续使用已验证的 ALS 三节 spine 适配。

骨盆 solver 原值为 stiffness=1、damping ratio=1、alpha=0.5、max distance=10 cm、error tolerance=0.01、max iterations=3。实际 UE 全局 RK4 update rate=60、max integration iterations=4 已只读采集并进入 policy，未改 CVar。

## 求值与事务

`AlsStrideWarping` 消费 Orientation 后的 typed `RootMotionDelta`，按实际 delta 求动画根运动速度，原低速/零 delta 分支返回 scale=1，再做原 clamp 与 float FInterpTo。Root 属性 translation 按实际 scale 缩放，保持 presence、rotation、scale 与身份；pose 通过原 SkeletalControl 的 LocalBlend alpha 混合，不能对 RootMotion 再乘一次 pose alpha。

脚部先沿原 floor/gravity 投影缩放 IK 目标，再按原多脚迭代求骨盆调整，使用 FVector 精度的 RK4 spring、float 时间余量/固定步长/最大迭代和停稳门槛。保留前后 root direction、scale modifier、spring position/velocity/last position/remaining/motion/initialized 历史。骨盆限距之后补偿 thigh 旋转，并按 float FK/IK 腿长防止过伸，排序后使用原组件骨 local blend。

原 Stride Initialize 仅重置 scale modifier 和 ActualDirection/Scale，没有重置 pelvis spring。alpha 过滤不推进滤波或弹簧；缺属性返回前只恢复 manual direction/scale，不推进后续 solver。未套用 Orientation 的 update-counter gap reset。原始序列启用 RootMotion 但 delta=0 的单位属性与完全 absence 分支分别保留。

`LyraCycleLayerPoseHost` 将双 Warp history 保存在同一个候选；真实 source/common Sync 提交成功后才发布。隐藏帧无姿态求值，显式重初始化保留原节点各自的 reset 范围。每个活跃帧都在求值后取消、核对两个节点的已提交历史未变、重新求值并逐值检查结果，原不同 Sync clock/root interval 等提交门禁继续生效。两个 Warp 的 delta/component/reinitialize 必须属于同一上下文。

## 实际原生采集

外部 exporter 复制真实继承 CDO，在 transient ALS81 上运行实际 Sequence/provider、LayeredBoneBlend、local-to-CS、Orientation、Stride、CS-to-local 节点。Stride CacheBones 对原脚骨定义验证成功，调用实际 UpdateInternal 和 Evaluate，包括 LocalBlendCSBoneTransforms。没有重写 UE solver、取期望姿态作输入或保存资产。

继续使用此前真实共同 Sync 的 source previous/delta；Orientation 输入沿上一批 controlled fixture。Stride 使用明确的受控速度 0/10/40/80/150/300/800 cm/s 和 0/0.4/0.7/1 alpha，没有执行原 Main 的完整 compiled exposed handler。实际组件旋转仍覆盖上一批 yaw；pitch/roll、所有地形/非默认向量或 Manual 路径未做新原生验收。

三个 provider × 30/60/120Hz，共九轨迹、3,780 物理帧、3,528 活跃帧和 252 隐藏帧。新增 Stride controls 包含部分 alpha 1,431 帧、alpha=0 693 帧、speed=0 555 帧，原属性 presence 为 3,255 帧、absence 273 帧。

相较同输入的 Orientation 原生输出，Stride 实际改变 2,613 帧姿态、26,295 个骨结果及 2,349 帧 root translation。verifier 对这些变化数量设门禁，且检查 alpha=0 时完整输出不变、所有 curve/整数属性不变、root 非 translation 字段不变，避免节点旁路成为假通过。

两次独立 UE 进程都退出 0、资源语义一致并保留原字节；最终日志无 ensure/assert/Python Error，源项目既有 GameplayTag/插件警告保留。489 个源包哈希未变，`assets_saved=0`。仅构建外部 exporter，没有修改引擎源码、GASP58 源码或工程配置。

## 最终验证结果

Godot 用自己的 source callback/common Sync、真实采样与两个 Warp controller 逐帧求值；整个候选的重复/取消重试与 24,129 次坏操作拒绝通过。

```text
LYRA_CYCLE_LAYER_POSE_GODOT_OK frames=3528 bones=285768 curves=105 attributes=14112
dataProbes=72 positionCm=1.0772985490388227E-13
quaternion=9.354905487120434E-16 scale=0 retry=true stage=AfterStride

LYRA_STRIDE_GODOT_OK frames=3528 bones=285768 rootPresent=3255
rootPositionCm=0 rootQuaternion=0 rootScale=0 retry=true
stage=AfterStride production=false
```

原 `1e-8 cm / 1e-10 quaternion / 1e-12 scale` 精度门槛保留。Core 新 Stride 7 项、原 Orientation 7 项和既有 RigTwoBoneIK 2 项共 16 项通过；既有 Standing 原生连续 6 项通过，均无失败/跳过。Debug 与 Release Optimize 均 0 错误、0 警告。

原 Orientation、RootMotion、authored pose 和 source 隐藏重初始化场景回归通过。旧 Demo 60Hz 完成 870 物理帧、六换层、871 pose commits；这是旧入口回归，不能证明双 Warp 已接生产。所有上述 Godot 运行退出 0，无 ERROR/WARNING。本批没有新渲染、人工/性能/全量验收。

首 Core 14 项测试有 1 项失败：将 1/60 秒的 float 物理比值误要求字面量 0.5，实际为相邻 float。改测试输入为准确可表示的 dt=1/64、speed=160、root distance=5，独立预期 ratio=160/320=0.5；未改算法或阈值。首次日志/TRX `stride-core-first.*` 保留。原生构建、首次导出和 Godot 对照没有失败。

| 新资源 | SHA-256 | 字节数 |
|---|---|---:|
| stride_policy.json | `6e083682541e140e56dbb5456d8881f6f12e4c288cdcf37888263fc874f75b0e` | 1,630 |
| stride_requests.json | `a4b6956db1268f84f0991dca34fb44f74313054d2abceef0ba78b4cdd931c307` | 87,515 |
| stride_native.json | `01cefa4e9e10e1b43077da40f5121e79188ee2e42f7da055dc072f20adca3673` | 53,646,567 |

资源在 ignored `assets/generated/lyra_als/`，仅代码检出无法运行。证据位于 `artifacts/lyra-analysis/`：`stride-godot-final.log`、`stride-orientation-regression.log`、`stride-root-regression.log`、`stride-pose-regression.log`、`stride-hidden-regression.log`、`stride-demo-60.log`、`stride-core-final.trx`、`stride-standing-native.trx`、`stride-debug-final.log`、`stride-optimize.log`、`stride-reader-build.log`、`stride-export-first.log`、`stride-export-repeat.log`、`stride-ue-first-full.log`、`stride-ue-full.log`、`stride-verification.json`。

下一步接原 Main 的速度/方向/alpha 更新和真实完整 Cycle 根遍历，而后继续其余 Layer/Main、partial marker sets、Main 三个 Lean、统一 Notify/Montage、FootPlacement/LegIK、生产宿主及完整主图原生/视觉/性能。仅关闭当前受控输入的双 Warp 局部组件。

复跑：构建外部 exporter → `scripts/export-lyra-stride.ps1` → `tools/verify_lyra_stride.py` → `scenes/tests/lyra_stride_warping_smoke.tscn`。
