# Lyra Main 旋转历史与 Lean ApplyAdditive

2026-10-01，直接在 `.` 主目录继续移植，源为 GASP58 / 本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET。本批完成原 `UpdateRotationData` 与三个编译 `ApplyAdditive` 的组件组合，继续使用 ALS 68 skin / 81 logical。完整 Main、其余 provider 根图及生产入口仍开放，不把这段组件对照写成完整角色验收。

## 实际链接与输入精度

外部 exporter 的 `ReadMainLeanCompositionTrace` 从真实 Main 编译属性读取三个 ApplyAdditive 的 Base/Additive 链接及默认配置。此前资源记录把 Start/Pivot 的 Lean 编译索引写反，本批已纠正文档；已有三个独立源的数值对照和资源字节不变。

| 阶段入口 | ApplyAdditive | Linked Base | Lean | Lean property index |
|---|---:|---:|---:|---:|
| FullBody_StartState | 13 | 11 | 12 | 90 |
| FullBody_CycleState | 17 | 15 | 16 | 86 |
| FullBody_PivotState | 23 | 21 | 22 | 80 |

三个根均为 Float alpha=1、scale=1/bias=0、LOD=-1，map/clamp/interp 禁用，无节点回调。采集按 [22,16,12] 顺序，因此基底适配的顺序是 Pivot/Cycle/Start。

原 `UpdateRotationData` 的真实运行边界为：

1. PropertyAccess 在相应 GameThread / WorkerThread pre 批次读取真实 owning actor rotation。
2. WorldRotation 保存 FRotator 的 double 分量；两次 Kismet `BreakRotator` 的 Yaw 输出均先窄化 **float**，再提升 double 相减，保持原始差，不 unwind。
3. `SafeDivide` 使用 float GetDeltaSeconds 提升的 double 分母；分母恰为 0 时返回 0，不另加小 delta 门槛。
4. speed 乘站立 `.0375` 或 crouch/ADS `.025`。原旋转函数位于 UpdateCharacterStateData 之前，必须使用当时的 crouch/ADS 字段，不能提前换成稍后刷新的站蹲状态。
5. 首次更新只清零 YawDeltaSinceLastUpdate 和 AdditiveLeanAngle，保留已计算的 YawDeltaSpeed；最后 Main 更新函数负责清除 IsFirstUpdate，旋转函数本身不清除它。
6. Lean 的 double 变量到原 BlendSpace float pin 再窄化一次。

本机 KismetMathLibrary.h/inl 的 BreakRotator 签名和连续 native 结果共同证明上述 float 边界。首次输入 actor yaw=173°，首次 Lean 为 0，但 30Hz speed 为 `5189.9997293204215`；不能为了初始化稳定而把它也归零。三个频率另含 ±179.9° 跨界、两次连续 first 标志、后期 first 标志、六次零 delta 和三次 1e-6 delta。

Godot 消费的是 native 采集的 **actor rotation 输入快照**，自行执行旋转算法；没有把 RotationData 输出当输入，也没有用 native 骨骼姿态驱动计算。本批尚未验证 Godot Actor quaternion → 该 double 快照的采集边界，亦未执行完整 Character/Main 更新函数链。

## 原根节点与完整数据组合

native 采集创建真实 Main 实例，刷新上述 PropertyAccess 批次，调用实际 Blueprint `UpdateRotationData`。三个原 ApplyAdditive 根通过 FPoseLink 初始化、CacheBones、Update，再共同 Sync 和 Evaluate；Lean 使用原 exposed handler、原配置以及既有 transient ALS81 三样本。

Base 的原 LinkedAnimLayer 链接仅在采集实例中替换为明确的源姿态边界：真实重定向的 Unarmed Pivot/Cycle/Start 序列，显式 sample time，完整姿态/曲线/属性。**这不是三个完整 Linked provider 的执行对照。**Base adapter 先用真实 GetAnimationPose 取样，再在指定帧用真实 RootMotionProvider 的正反/跨循环区间覆盖根属性；native 默认 GetAnimationPose 在有 root motion 的序列上即使区间为 0，也保留 present Identity 属性。所有输出元数据逐项与 base 输入完全相同。

`LyraMainLeanCompositionHost` 共同持有旋转历史与三个源：Prepare 计算候选旋转并登记 Lean，外部一次 Sync 后 Resolve；各 occurrence 的私有 additive/staged 缓冲支持并行取样和 LocalApply，输出保留完整曲线、整数属性和 typed RootMotion 存在性。输入/输出缓冲不能重叠，非法输入或求值失败不发布局部 pose。统一 Commit 发布旋转及源历史，Cancel 同时取消两者。调用者仍需在完整角色宿主中预校验其它子图并串行协调提交。

每帧并行求值后取消、检查两份历史未发布、重试并重新对照 pose，再提交；update-only 对照宿主每 17 帧求值一次，也重新比较选中帧的 pose。旧候选/重复提交、隐藏求值、重叠缓冲和 NaN 曲线等坏操作拒绝。另有功能断言验证显式 absent 输入 RootMotion 不会被组件创造为 present；该 absent 断言是 Godot 行为，native 本批源边界均产生 present 属性。

```text
LYRA_MAIN_LEAN_COMPOSITION_GODOT_OK frames=2100 ticks=2121 bones=171801
curves=966 attributes=8484 rootPresent=2121 rootIdentity=576 rejected=14718
positionCm=1.2763915006709595E-13 quaternion=5.715222817774257E-16 scale=0
rootPositionCm=0 rootQuaternion=0 rootScale=0 rotationAndClocks=exactBits
retry=true updateOnly=true parallel=true production=false wholeMain=false
```

原 pose 门槛 position `1e-8 cm`、quaternion `1e-10`、scale `1e-12` 保留；旋转六个 double 字段和源/sample 的已比较 float pin、weight、weightRate、clock、delta 逐位一致，root TRS 差为 0。数据覆盖为三频率各 10 秒的 2100 物理帧、2121 次活跃根输出，含源相关性重叠、隐藏和初始化。

## 资源、回归与失败证据

当前新增 active 资源为 ignored `main_lean/composition_v2_requests.json`、`composition_v2_native.json`、`composition_v3_policy.json`。V2 requests/native 记录正确的编译分支基底与 root 数据；V3 policy 明确 BreakRotator float → double 的公式与本机源 SHA，不依赖 expected pose。两次独立 V2 native 采集语义一致，原文件字节保留。所有 492 个保护源/目标包和旧 234 logical clip 哈希不变，UE 两次正常退出 0，`assets_saved=0`，无 ensure/assert/Python Error；既有项目插件/SDK 等警告保留。本批没有 UE 源码、GASP58 插件/配置或资产修改。

首次 native 工具编译遇到 FBasePose 名称与引擎模板冲突，以及 FStructProperty GetCPPType 的参数遗漏，已修正，失败日志保留。首次 source-pose 边界采集后，按实际编译链接调整基底顺序并补 root 区间，保留早期结果。首 Godot 运行把默认 present Identity root 误认为 absent，第二次把 BreakRotator 当 double 输出；两次失败都保留，没有提高阈值或覆盖 native 期望。修正 source adapter 的 Godot 输入生成和原 float 运算边界后通过。

早期三份 composition JSON 及 V2 policy 共四文件已逐一 SHA256 校验后移到 `artifacts/lyra-analysis/main-lean-composition-initial/`，`archive-provenance.json` 保留原路径/哈希，未覆盖或格式化它们。当前脚本只写 active V2 requests/native 和 V3 policy。

最终 Debug / Release Optimize 均 0 错误/0 警告。因共用 native reader 被重构，原 Lean runtime 已重新执行 UE 采集并通过既有 JSON 语义/字节保留门禁；Godot 原 Lean 2100 帧、Cycle root 3528 活跃帧/285768 骨和 root TRS0、独立 Rifle 60Hz 870 物理帧/六次换层/871 pose 回归通过。最后四份 Godot 日志均退出 0，无 ERROR/WARNING。本批没有 Core 算法修改；未额外重复此前 Core 79 门禁，也未做全量/渲染/人工观感/十分钟/性能验收。

覆盖/资源哈希由 `tools/verify_lyra_main_lean_composition.py` 独立核对，结果见 `main-lean-composition-resource-verification.json`；最终门禁与代码哈希见 `main-lean-composition-final-verification.json`。主要运行日志为 `main-lean-composition-ue-full.log`、`main-lean-composition-godot-retry-final.log`、`main-lean-composition-source-regression.log`、`main-lean-composition-cycle-regression.log`、`main-lean-composition-demo-regression.log`。

```powershell
.\scripts\export-lyra-main-lean-composition.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_main_lean_composition.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_lean_composition_smoke.tscn
```

下一步完成 Main 的位置/速度/加速度、站蹲/ADS 与回调更新顺序，将这段接真实 Cycle/provider 和 Main 状态权重/惯性根遍历；随后完成其余 provider 根、共同 Notify/Montage 与最终 FootPlacement/LegIK、生产替换和全链原生/视觉/性能验收。原 ALS R2–R7、用户暂缓项及其它未提交修改保留，完整 Lyra 目标继续开放。
