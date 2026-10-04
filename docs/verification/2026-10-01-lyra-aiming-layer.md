# Lyra 完整 FullBody_Aiming 与共同执行组

2026-10-01，在当前主目录继续移植第13个入口 `FullBody_Aiming`。沿用原 ALS 人物 skin68/raw69/logical81；45个既有 ALS AimOffset 样本复用，不重新重定向或改写原模型。忽略 UE5.8/5.9 差异。本批关闭完整 Aiming 组件和固定 Provider 的共同 Source/Sync/事务接入，完整 Main 上身/Slot/最终足部链及普通 Demo 生产替换仍开放。

## 原图与组件

原8节点保持：Root81→TwoWay77→Relaxed79/Idle74，两支分别经 UseCache76/75 汇入 SaveCache78→LinkedInput80 `PreAimPose`。三个实际 Provider 的 Idle 分别使用 Unarmed/Pistol/Rifle，Relaxed 都使用 Unarmed。两个出现位置分别保存滤波和样本历史，不能因引用同一资源而合并时钟。源均 DoNotSync、loop、rate1、alpha1、无root-space additive；DoNotSync 源仍参加角色唯一 Sync 批次。

完整 `LyraAimingLayerHost` 消费上一 enclosing Main 的已提交 `applyHipfireOverridePose`、原图前 double 权重和 double AimYaw/AimPitch，在暴露引脚处转换到 float。每次源出现位置登记到共同批次，在 Sync 后接回真实样本时钟；隐藏入口不更新源，初始化清除滤波/样本，但不把原本保留的字段全部清零。Update-only 可以提交；失败/取消不发布权重、滤波、样本时钟或输出，已取消/提交视图不可读。

Unarmed Yaw 原设置为 float0.2秒、SpringDamper、damping1、maxSpeed0、非wrap；Pistol/Rifle 没有轴平滑。三个 Relaxed 分支因此都有独立的 Unarmed 弹簧历史。新 Core `AlsBlendSpaceSpringFilter` 按本机 UE `FFIRFilterTimeBased`/`FMath::SpringDamperSmoothing` 实现首次输入、delta≤1e-4保留、float运算顺序、扩展 InvExpApprox 和轴夹取/速度归零。没有使用 ALS 的另一种 spring 公式代替。原 weightSpeed=0 时直接使用新的样本排序，不沿用旧样本顺序；节点旧 CachedTriangulationIndex 保持-1，不把它当作跨 tick 的有效三角形缓存。

Evaluate 消费只读完整 PreAimPose，每支先按实际 BlendSpace 样本混合 mesh additive，再分别施加到同一缓存基底，最后运行原 TwoWay 绝对姿态混合。保留81骨、曲线值/presence/flags、typed整数动画属性和 RootMotion。45个 Aim 样本没有 authored float曲线，但具有整数属性，不能只移植骨骼。两个源的属性先混合、additive累加，再参与最终两支混合。共同基底的 Distance 曲线及四种 flags 原样通过。

资源目录为 Aim 追加200–244索引，既有 absolute/Lean/Recovery 索引保持；新 AnimationId 用独立后缀命名空间，避免与旧 Lean 234–236冲突。资源 JSON保持字节哈希依赖，旧文件不重排。

## 原生对照

新增独立 UE探针 `UAlsLyraAimingLibrary`，真实 Main 用原 LinkAnimClassLayers 建立实际 Provider，调用原 Blueprint 全局权重及 PropertyAccess；在临时 ALS81骨架/压缩样本上执行原 Aiming 节点、SaveCache延迟更新和 Main共同 FAnimSync。完整求值借用导出的 `ParallelEvaluateAnimation`，保留引擎内部真实 CachedPoseScope；只替换瞬时 Proxy Root，随后恢复。源资产和 GASP宿主插件不改写。

| 检查 | 结果 |
| --- | --- |
| 三 Provider ×30/60/120Hz ×12秒 | 7560帧 |
| 完整输入与输出 | 各6075姿态、492075骨、24300整数属性 |
| float引脚/权重/样本顺序/样本时钟/缓存历史 | 逐位相同 |
| 实际两支同时访问 | 1939帧 |
| 隐藏/仅更新 | 474 / 1011帧 |
| 样本历史逐项对照 | 26917项 |
| 基底 Update / Evaluate | 每访问帧一次Update；每求值帧一次Evaluate |
| 缓存输入上下文 | 采用两个分支中较大的权重，未求和或重复更新 |
| 逐帧取消重试/拒绝 | 7560次 / 62637次 |
| 输出误差最大值 | 位置2.4146694851820906e-13 cm、单位quaternion1.0254548988792393e-15、scale0 |

原误差门槛保持：位置1e-8cm、单位quaternion1e-10、scale1e-12；曲线值/flags、属性presence/value 和源时钟保持原精确门禁。不可变 native元数据的 `twoSources=6291` 计算的是保留的非零节点权重，包含旧非活跃字段；实际同时访问按 `mixed=1939` 和本批 Godot active 分支计数验证，不将前者作为执行覆盖数量。

两个独立 UE5.8.1采集均权威退出0，第二次不可变输出一致；各汇总0 error /839 warnings，原资产与编辑器警告保留。508个原包、664个此前 JSON以及原探针源哈希保持。新增 ignored `aiming_layer_v1_requests/policy/native.json` 分别3401576/50917/200178538字节；实际运行需本地 ignored资源，只有代码的检出不构成可运行交付。

## 共同 Source 与事务接入

`LyraItemLayerGraphInstance` 同一个 ItemAnimLayers 实例拥有 Aiming、LeftHand、Additives与十个移动入口。Aiming 使用 typed调用节点/实例/epoch/候选检查，独立的两源在原延迟输入之前登记，保持DoNotSync；Locomotion/Lean/Recovery随后进入同一次角色 Sync。Provider图前 HipFire 新权重用于原移动源，缓存输入子图用原最大上下文权重更新一次，Additives保持其外层原0.65分支权重。曲线反馈在 enclosing Main最终边界提交，update-only保留旧反馈。

组合测试从既有 gameplay observation 独立计算 Main与13入口，补充实际 AimPitch扫动；不把原生 state/clock/pose输入给运行时。它显式提供受控 PreAimPose，未把左手边界冒充完整 Main的上身/Slot输出；Additives仍作为原后续入口单独返回。每帧共同批次、角色间隔离、取消/重试和调用身份门禁检查通过。

组合覆盖：11340帧、9762姿态、13783 AO tick、25864样本、2608两支访问、9762实际瞄准姿态变化、2780上一Main曲线真正驱动权重、165隐藏、207晚期取消重试、1392错误调用拒绝。旧 Main十入口+LeftHand+Additives路径复跑11340帧/9762姿态与12595根姿态原生对照，Recovery530 tick/456姿态保持。ALS旧逻辑源234源/936样本、raw69/logical81/skin68复验保持。Debug和Optimize ExportRelease均0警告0错误。

这里关闭固定类13/14执行入口的组件/共同调度；没有新 Main+Aiming联合原生oracle、完整 Slot/惯性/最终 IK输出、换类/普通 Demo、多角色渲染、人工观感或性能验收。SkeletalControls剩余入口与上述原目标继续开放。

## 失败与复验

保留首次UE CachedPoseScope链接失败、ParallelEvaluate尝试中的私有Root访问编译失败；改用真实导出求值路径后最终构建成功。Godot首轮检查节点缓存失败后核实实际 tick与旧节点字段关系，随后发现零平滑速度仍沿用旧样本顺序，按原分支修正。组合首次取消暴露延迟创建的初始节点状态，已改为实例建立时初始化。移动夹具零AimPitch造成样本覆盖不足，增加真实俯仰扫动，未改算法或原生误差门槛。相应失败日志保留。

证据目录 `artifacts/lyra-analysis/`：

- `aiming-layer-ue-build.log`、`...-parallel.log`：两次UE编译失败；`...-root.log`：最终UAT成功。
- `aiming-layer-ue-export.log`、`...-repeat.log`：两个有效采集与权威退出0。
- `aiming-layer-godot-first/cache.log`：首次缓存/样本顺序失败；`...-final.log`：完整组件通过。
- `aiming-layer-main-scope-first/owner/count.log`：初始化与覆盖诊断；`...-final.log`：共同执行组通过。
- `aiming-layer-main-additives-final.log`、`aiming-layer-logical-regression.log`：旧路径回归。
- `aiming-layer-debug-final.log`、`aiming-layer-optimize-final.log`：0/0构建。

复跑 `scripts/export-lyra-aiming-layer.ps1`，运行 `scenes/tests/lyra_aiming_layer_smoke.tscn` 和 `scenes/tests/lyra_main_aiming_scope_smoke.tscn`，再执行 `python tools/verify_lyra_aiming_layer.py`。保持原资产和旧哈希文件，不覆盖既有不可变输出。
