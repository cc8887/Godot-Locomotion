# Lyra 编译接口、共享 Layer 实例与同类重绑

2026-09-30，在 `.` 主目录实施，源为 `..\GASP58`、UE 5.8.1，运行端为 Godot 4.7.2 .NET。沿用 ALS Mannequin 的 68 根物理骨。本批完成编译接口元数据、已实现姿态组件的共同实例生命周期和同类重绑修正；完整姿态缓冲、图级 Sync/Notify/惯性化与连续主图仍开放。

## 实际编译契约

外部 exporter 新增 `UAlsLinkedLayerLibrary::ReadLinkedLayerClass`，直接读取 `IAnimClassInterface::GetAnimBlueprintFunctions()`、`GetGraphBlendOptions()`、主图 LinkedAnimLayer 节点和类默认值。Python 导出 interface、main、base 及八个武器/外观派生类，共 11 个类；未从 DSL 图名推断签名或分组。

`linked_layer_contracts.json` 保留函数和节点的原始数组顺序、输入参数类型、BlendIn/Out 时间/配置、默认与节点 Notify 标志及源 `.uasset` SHA-256。它依赖既有 `linked_layer_inventory.json` 的字节哈希。

- `ALI_ItemAnimLayers` 恰有 14 个入口，**全部 Group 为 `ItemAnimLayers`**。九个 provider 类全部实现这 14 个入口；额外 `AnimGraph` 是未实现的 stub。
- 三个姿态输入为 `FullBody_Aiming(PreAimPose)`、`FullBody_SkeletalControls(InPose)`、`LeftHandPose_OverrideState(InputPose)`；其余 11 个没有姿态参数。
- Aiming 的 `AimYaw`、`AimPitch` 均为 `double`，实现类具有对应 `double` 目标属性，其余入口没有普通参数。此处保留的是编译签名，当前 Godot Aiming 的 float 采集/求值边界没有据此改为完整 double 图执行。
- 主图恰有 14 个调用节点，一入口一节点；都指向该接口，`InstanceClass` 为空。主图本身保留默认 self-layer/fallback 语义；Godot 示例明确构造 Unarmed 初始 provider，并非复刻未装备时的 self-layer 分支。
- 两个 Notify 传播标志和 `useMainMontageData` 在所有实际 provider CDO 中为 false；14 个调用节点的两个传播标志也均为 false。这只涉及 Linked Instance 的命名通知传播，不禁止资源通知或普通角色消费者。
- 已实现类的 Aiming、Additives 和各移动入口 BlendIn/Out 为 `0.15000000596046448`；LeftHand 与 SkeletalControls 为 -1。相关 BlendProfile 路径为空。导出值已保留，尚未替代独立示例原有 crossfade，也未实现原主图的完整惯性请求路径。

Godot `LyraLinkedLayerContracts` 在 Router 加载时核对 inventory 字节依赖、11 类结构、14 个签名、输入姿态名/参数类型、共同 Group 和唯一调用节点。源结构变化立即拒绝，避免把不同分组或缺失参数仍当成当前实现。它提供不可变的 signature/call-site 数据，不代表所有 14 个入口已经完成求值。

## 实际 UE 绑定对照

`ReadLinkedLayerBindingSequence` 在独立临时 GamePreview 世界中创建 Actor 与 Manny SkeletalMeshComponent，设置真实 `ABP_Mannequin_Base`，登记组件后调用实际 `UAnimInstance::LinkAnimClassLayers`。采集期间不 tick 世界或动画、不做人工姿态求值，关闭该采集组件的 PostProcess、碰撞与初始化姿态 tick；结束注销组件并销毁临时世界，未修改项目地图或资产。

序列为 Unarmed → Unarmed → Pistol → Pistol → Rifle → Rifle → Unarmed → Unarmed。每步读取真实 14 个节点的 target instance、class、Notify 标志和组件的 linked instance 列表。使用采集内递增编号记录身份，输出与全局 UObject ID 无关。

最终 UE 输出：

```text
LYRA_LINKED_CONTRACTS_OK classes=11 hooks=14 assets_saved=0
LYRA_LINKED_BINDING_NATIVE_OK steps=8 nodes=14 owners=4 same_class_reuse=4 assets_saved=0
```

八步始终只有一个活跃 linked instance，14 个节点同一 owner。身份为 `0,0,1,1,2,2,3,3`：四次同类重绑保留原对象，三次换类创建新对象，返回 Unarmed 也创建新对象。本批没有启用 persistent/shared-linked-instance 配置或验证卸载、部分接口覆盖、多组、多角色实例共享与延迟初始化。

首个采集宿主未登记组件，实际 `LinkAnimClassLayers` 因 `IsRegistered()` 门禁直接返回，八步均为 0 linked instance/self owner，Python 验证正确失败。已改为真实登记组件并从组件取得主实例；未修改验证条件。初次 C++ 编译还误调用组件的 private 非 const linked-list accessor，已改用 public const accessor。失败日志分别保留为 `linked-binding-ue-first.log`、`linked-binding-ue-unregistered.log`、`linked-binding-reader-build.log`。

另一次元数据进程在 Mocara shutdown 处超过两分钟未退出，核实本次进程 PID/脚本后停止，保留 `linked-contracts-exit-stall.log`。本批 wrapper 随后仅在采集命令中禁用 Mocara，无 UE 工程配置修改。最终两个进程均正常退出 0，无 Python Error/ensure/assert；项目既有 GameplayTag 和编辑器插件警告仍保留。构建及采集见 `linked-binding-reader-build-registered.log`、`linked-contracts-export-registered.log`、`linked-contracts-ue-full.log`、`linked-binding-ue-full.log`。

## Godot 实现与验证

`ILyraItemAnimationLayers` 增加源动画类身份。`LyraItemLayerInstance` 共同持有当前已实现的 HipFire、Aiming、LeftHand、Additives、HandRetarget 子图状态及播放配置，实例生命周期对应实际 `ItemAnimLayers` 组。角色仍保存运动、RootYaw、播放时钟等现有宿主状态；尚未将各移动入口的源播放器和状态完全迁入组内原图。

同类重绑在构造新组件、恢复旧姿态之前返回，因此不清空 Additives、滤波历史或可见姿态，也不增加 revision。换类先验证资源并完整构造候选，再恢复旧组件基底、切路由并替换共同实例。类默认 Notify 标志与主图各调用节点标志按 UE 的 OR 规则保留在实例上；尚未接为新角色级传播队列。

`lyra_linked_layer_binding_smoke.tscn` 使用实际 UE oracle 的八步序列，驱动真实 ALS Skeleton3D 和层内 Additives；每次重绑前进入 AirIdentity，再验证同类身份/状态/可见 68 骨逐值不变、角色 phase/播放时间/累计求值数不变，换类初始状态为 Identity。14 个 hook 和 Notify 标志逐步对照原生 owner。六类坏合同（旧哈希、改组、缺入口、错姿态参数、float 替代 double、重复调用节点）全部拒绝。

```text
LYRA_LINKED_BINDING_OK steps=8 hooks=14 owners=4 sameClassReuse=4
rejected=6 group=ItemAnimLayers parameters=double,double skeleton=68
```

普通 Rifle 三频率轨迹在实际落地时追加同类重绑，保持 AirIdentity、可见姿态和 revision；其余六次 Q 换层、五段空中、ADS、转身和通知覆盖不变：

| Hz | 物理帧 | Layer/Cycle 换源 | 求值次数（含初始化） | 同类重绑 |
|---|---:|---|---:|---|
| 30 | 435 | 6/6 | 436 | 通过 |
| 60 | 870 | 6/6 | 871 | 通过 |
| 120 | 1740 | 6/6 | 1741 | 通过 |

五项组件/目录回归为 LeftHand/Additives 的五配置 2100 帧、HandRetarget 324 native cases、Unarmed/Pistol/Rifle 三套目录；另补 Pistol 60Hz 的两次换层/两次 Cycle/30 ADS 帧回归。连同绑定与三频率场景共十份运行日志，均退出 0、有唯一成功标志且无 Godot ERROR/WARNING。最后 .NET build 0 警告/0 错误，editor import 退出 0。检查汇总保存在 `linked-contracts-final-verification.json`。

未新增渲染/人工观感、十分钟或性能验收，普通 ALS 主 Demo 未切 Lyra。UE oracle 证明绑定对象与生命周期，不包含移动状态/姿态时钟、惯性、曲线和通知的连续原生等价；Godot state/pose 保留检查是实际 Godot 行为，不能冒充该范围的 UE 姿态 oracle。

## 资源与复跑

最终独立复核 11 个源类 `.uasset` 哈希仍与导出时一致，记录 `linked-contracts-asset-hashes.json`。既有资源 JSON 未格式化或覆盖；两份新 JSON 在 ignored `assets/generated/lyra_als/`，仅有代码检出无法运行新增场景。

| 文件 | SHA-256 |
|---|---|
| `linked_layer_contracts.json` | `D582A274A0693618886E57887719BA90ABD7C1F7DE26C3DBA6D707C236D8BB64` |
| `linked_layer_binding_native.json` | `BD3F17E33883D1894B3DC1DDC81B96C84F49870B6BF035562578A96666C1464B` |

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-linked-layer-contracts.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_linked_layer_binding_smoke.tscn
```

下一步：以这些实际签名和共同实例为基础迁移输入姿态/曲线/属性缓冲、活跃源登记与一次 Sync、按原拓扑求值及统一通知/提交；再接武器空间 FK 左手目标、左右手 IK、Warping 与完整足部。当前层绑定、手部首节点和既有普通玩法验证不能关闭完整 Lyra 支线或 ALS R2–R7。
