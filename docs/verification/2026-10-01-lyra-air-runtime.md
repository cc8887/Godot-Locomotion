# 五个 Lyra Air Layer 的实际源与姿态执行

2026-10-01，在主目录继续实现。运行端 Godot 4.7.2 .NET，原生对照端安装版 UE 5.8.1 / GASP58。按当前路线保留原 ALS 模型、68 根蒙皮骨与 81 根逻辑姿态通道。本批关闭五个 Air **Provider 子图组件**，完整 Main、普通 Demo 与整个移植目标仍未完成。

## 原图与实现

读取现有编译节点、Layer 闭包和真实继承 CDO：三种装备具有相同的四节点拓扑，每个入口为 Root → LayeredBoneBlend → Base / HipFire。保留原 UpperBodyMask、mesh-space 旋转、Override 曲线、typed 整数属性、根骨 RootMotion 混合规则以及 HipFire 先于 Base 的更新顺序。

| Layer | Root / Blend / Base / Hip | Base 行为 |
| --- | --- | --- |
| JumpStart | 85 / 83 / 84 / 82 | 非循环 SequencePlayer |
| JumpStartLoop | 101 / 99 / 100 / 98 | 循环 SequencePlayer |
| JumpApex | 89 / 87 / 88 / 86 | 非循环 SequencePlayer |
| FallLoop | 97 / 95 / 96 / 94 | 循环 SequencePlayer |
| FallLand | 93 / 91 / 92 / 90 | 非循环 SequenceEvaluator，Locomotion / AlwaysLeader |

FallLand 的原 BecomeRelevant 设置 ExplicitTime=0，随后 Update 使用 `GroundDistance` 曲线和 Main.GroundDistance 做 DistanceMatchToTarget。曲线名不是地面 Start/Stop 的 Distance。保持静态距离 codec 原样，不裁剪合法负 ExplicitTime；内部采样时间和公开 ExplicitTime 分别持有。四个 Player 使用原 rate/basis/start；Rifle FallLoop 的资产 RateScale=2 仍参与实际 tick。

十个 source occurrence 分别持有内部时钟、公开时钟、Delta、cached weight、marker 和初始化待消费状态。隐藏初始化保留原 evaluator 时钟与 layered weight；HipFire 不被访问时保留其 ResetPending，不能由 Base 更新代为消费。Visited 与父上下文 Active 分开，父 inactive 仍可能遍历和推进源。

新增 `LyraAirLayerGraph`、`LyraAirLayerSourceHost`、`LyraAirLayerPoseHost` 与不可变资源工厂 `LyraAirResources`。各宿主登记实际源记录，由外层汇总后执行一次 Sync；没有在 Layer 内建立另一个 Sync 时钟。候选先统一预校验，再提交或全部取消；支持 Update without Evaluate，重复求值不会推进源。源配置变化（拓扑、角色、同步方式、播放变换、显式帧、回调等）直接拒绝。

这些组件尚未接入生产 `LyraItemLayerInstance` / Main Source Scope；此处同 Provider 的五个实际入口一起运行，仍由测试提供 Main 字段和根访问顺序。不能由本批推导地面 / 空中已共同选主，也不能宣称 14 个接口入口已全部执行。

## 实际 UE 对照

外部 exporter 新增 `UAlsLyraAirLibrary::ReadAirTrace`。在临时 GamePreview 世界登记真实 Main 组件，调用实际 LinkAnimClassLayers，取得一个 `ItemAnimLayers` 实例；直接运行五个原 Provider 根的 Initialize / CacheBones / Update / Evaluate，并通过 Main 的真实 Sync 更新一次。

动画适配为已有标定策略的 transient ALS81，骨遮罩按目标骨名映射。控制输入是 Main.IsCrouching / GroundDistance、HipFireUpperBodyOverrideWeight、实际访问权重 / 顺序 / 初始化 / 父 Active；输出来自原节点的内部 / 公开时钟、Delta、cached weight、marker、完整姿态、曲线、typed 属性和 RootMotion。未注入期望时间或姿态。Main 更新函数、状态选择和最终混合不属于这次探针。

三 Provider × 30/60/120 Hz × 6 秒，共 3780 帧、7650 次姿态、21 个目标动画。两次独立采集均正常退出 0；第二次对四份 JSON 做不可变内容比较，最终文件字节保持一致。508 个原包和此前 629 份 JSON 逐文件哈希保护通过；没有保存 UE 资产、修改引擎或 GASP58 源码 / 配置。

第一轮外部插件构建误用 protected 的 FAnimationBaseContext 构造函数，已改为公开 FAnimationUpdateContext，失败日志 `air-runtime-ue-build-first.log` 保留。最终完整构建成功，0 compiler warnings / errors。两次 UE 采集各保留 776 条既有 GameplayTag / 插件等警告，无 Python Error、ensure、assert 或 Fatal；不能称 UE 零警告。最终 Godot 输出无 ERROR / WARNING。

## Godot 结果

最终 `lyra_air_runtime_smoke.tscn` 一次共同 Sync，source ID 使用 700+ 原节点索引，逐帧检查取消重试、求值后取消、异代结果拒绝、全体预校验与重复提交拒绝；另五个独立宿主每四帧跳过求值，最终历史与全求值宿主逐帧一致，恢复求值时姿态也一致。

| Layer | 姿态数 / 骨数 | 最大位置差 cm | 最大 quaternion 差 |
| --- | --- | --- | --- |
| JumpStart | 1620 / 131220 | 8.06e-14 | 6.80e-16 |
| JumpStartLoop | 1611 / 130491 | 1.16e-13 | 7.94e-16 |
| JumpApex | 1605 / 130005 | 1.23e-13 | 7.80e-16 |
| FallLoop | 1410 / 114210 | 1.20e-13 | 7.16e-16 |
| FallLand | 1404 / 113724 | 5.91e-14 | 6.20e-16 |

原姿态门槛保持位置 1e-8 cm、quaternion 1e-10、scale 1e-12；scale 差为零。37800 条 source 观察的时钟 / Delta / weight / marker 逐 float bits 比较；9054 项曲线的值 / 存在性 / flags 和 30600 项整数属性通过。7650 份真实 RootMotion 属性均通过严格值与身份检查，本轨迹未覆盖 RootMotion 缺失分支。

覆盖包括 1620 个多根活跃帧、11250 次隐藏根观察、78 次隐藏初始化、2046 次父 inactive 的实际根访问、1920 次 update-only 求值跳过、578 次合法负 ExplicitTime、470 次非默认 RateScale 访问与 3636 次异代结果拒绝。循环播放器已运行，但本轨迹没有精确撞到公开时间等于片长的循环边界，不冒充该边界专项。

Debug 与 ExportRelease Optimize 最终构建 0 警告 / 0 错误。相关回归均正常退出 0：

- 四个实际地面根：3780 帧 / 8400 姿态 / 680400 骨，11340 Lean 时钟 / 22134 样本逐位同。
- 连续 Main 机器：7560 帧 / 十状态 / 9924 源更新；仍明确 `sourceObservation=native`。
- Layer 重绑：八步 / 14 入口 / 四 owner / 四次同类复用 / 六坏合同拒绝。
- ALS 逻辑资源：234 源 / 936 native 样本，raw69 / logical81 / skin68。

最终审计 `tools/verify_lyra_air_runtime.py` 已运行，通过资源依赖、源包、旧 JSON、外部编译源码一致性、两次正常退出、覆盖、日志及构建检查。汇总为 `artifacts/lyra-analysis/air-runtime-final-verification.json`，本批没有新增全量测试、渲染、人工视觉、十分钟性能或普通 Demo 验收。

## 资源与复跑

四份新 ignored JSON 位于 `assets/generated/lyra_als`，遵循字节哈希依赖，不能格式化：

| 文件 | SHA-256 |
| --- | --- |
| air_runtime_requests.json | d3d0c7f733a0f403a13cb261445821de5274ce89b51c6e0e7b9de84aa632e004 |
| air_runtime_distance.json | af8428156833a8298b29dc5373cbbfc276830acf75abc1828d35e33d00b7664d |
| air_runtime_roots.json | 9c81931866e5585cf1e899416bdbb3bf9e0843671503c054bee3ca9bad39d15f |
| air_runtime_native.json | 84afc3f6602119f847447d4d53de17e8c03d37abf88cdc3029b1e58291aca544 |

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-air-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_air_runtime_smoke.tscn
python tools/verify_lyra_air_runtime.py
```

构建命令仍需从 .. 使用可用 SDK 执行；项目固定的旧 SDK 不应为了复跑被改写。仅代码检出没有 ignored 资产时，不能运行新增场景。

下一步完成原 Idle 子图 / 嵌套机器，再统一地面、空中与 Idle 的资源地址、调用生命周期和 Main Source Scope；让连续 Main 机器消费 Godot 自身源 / Sync / 通知历史并求最终姿态，之后接 Notify/Montage、完整后处理、普通角色 Gather/Worker/Commit 与人工 / 性能矩阵。全部 ALS R2–R7 和用户暂缓项保持原状态。
