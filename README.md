# Godot ALS

最新 Lyra 普通 NotifyState 实时派发已接角色：Begin 使用旧未匹配库存，切换数组后实时 Tick，普通通知和 Montage Ended 共用活动库存；已提交 Reload 通知内换装与后续装备查找修复。两次 UE 参考 13 类/47 回调字节同，Core607、Debug/实际Optimize最终46进程全部通过，三频新角色1260帧/retry及原消费者/窗口/Main/Rig回归；178源码、869 JSON/710原包/9配置和程序集恢复审计通过。见 [实时状态派发验证](docs/verification/2026-10-03-lyra-notify-live-dispatch.md)。ALS68/69/81和Interface/Layer路线保持；任意原状态BP、世界销毁、通用图、完整物理/近景及全部暂缓项仍开放，整个目标active。

最新 Lyra Montage NotifyState 收尾：保留 ALS68/69/81 与现有 Interface/Layer 路线，已接普通角色的 Ended 前倒序状态回调/交换删除，按物理实例及 Montage 对象来源筛选。原生11类/31回调和三频适配取消重试通过；旧窗口失效经上一程序集复核，保留旧资源并新增15轨迹/33235帧/45资产/60轨道精确原生参考。最终Debug/实际Optimize共32进程、Core557、两构建零错误零警告，以及140源码/原资源/程序集恢复审计通过，见 [状态收尾验证](docs/verification/2026-10-03-lyra-montage-notify-termination.md)。完整Begin/Tick/Blueprint状态、世界销毁、通用图和完整物理/近景仍开放，字段仍34/47，整个目标active。下方NotifyState结束待办以本批指定收尾范围为准。

最新 Lyra 同步 Montage 回调：保留 ALS68蒙皮/raw69/logical81和当前 Interface/Layer 实例路线；请求与weight回调直接更新同一候选bank，新实例可在同帧更新并Advance，外部效果随取消/提交或物理凭据发布。两独立UE参考27轨迹/5670帧字节同；最终Debug/实际Optimize共26进程通过，Core549、两构建零错误零警告；108冻结源、两轮六文件恢复及869 JSON/710原包/9配置独立审计通过。见 [同步回调验证](docs/verification/2026-10-03-lyra-montage-immediate-callbacks.md)。仅关闭指定typed同步bank与效果协议；任意世界状态回调、销毁、Section/NotifyState结束、剩余Linked配置/通用图及完整物理和近景验收仍开放。字段仍34/47，整个目标active。下方请求/weight同步回调待办属于前批状态。

最新 Lyra Montage 物理实例委托：继续使用 ALS68蒙皮/raw69/logical81和原 Interface/Layer 实例路线；按物理实例捕获委托，候选绑定随角色提交/取消，发布后回调可在同一 bank 嵌套停止、重播和重绑，原 Emote 已消费真实实例委托。修复零时长 Stop 的 Playing 清除顺序及嵌套停止被旧快照覆盖的问题。两独立 UE 参考12轨迹/5880帧字节同；最终 Debug/实际 Optimize 各12进程通过，共24进程无 Godot 错误警告，Core540、两构建0错误0警告；三频六角色移动、十角色换层/E、原 ALS 与完整 Main/Rig 回归通过，完整报告跨构建和普通基线同。89冻结源、两轮六文件 Debug 恢复及869 JSON/710原包/9配置独立审计通过，见 [实例委托与回调变更验证](docs/verification/2026-10-03-lyra-montage-bank-callbacks.md)。**仅关闭本批捕获委托、指定发布后 bank 变更与 Emote 接入；weight/请求阶段同步回调、弱对象/销毁、Section 跳转和 Ended 的 NotifyState 结束顺序仍开放。**下一步同步候选 bank 变更与事务化外部效果，再余下 Linked 配置/左手源和通用图；字段仍34/47，完整物理314/1680差异、近景及全部暂缓项保持，整个目标 active。本批无资源保存/重导、GPU/全量managed/十分钟/性能验收。下条任意回调改 bank 的待办范围以本批发布阶段为准。

最新 Lyra 四类 Montage 实际事件：保留 ALS68蒙皮/raw69/logical81与原Interface/Layer实例路线；新增四容器、原weight→Advance生产者时序，Emote改消费真实物理实例事件与取消重试凭据。两独立UE参考21轨迹/8823行完全同，单Section生产事件72项与受控容器callback23项；Debug/实际Optimize最终24进程退出0无Godot错误警告，新参考逐帧retry、原Emote54轨迹27720帧、三频六角色真实移动10080/两类retry、普通十角色/E与原ALS及完整Main最终Rig回归通过，Core200/构建0错误0警告。两构建完整报告同，普通报告同前批；70冻结源、两轮六文件恢复与869 JSON/710原包/9配置独立审计过，见 [人物、接口与真实Montage事件](docs/verification/2026-10-03-lyra-montage-delegates.md)。**仅关闭四容器内核、指定单Section生产者与原Emote消费者；任意回调改bank/再播放停止重绑、弱对象与销毁、真实Section切换及Ended的NotifyState结束顺序仍开放。**明确Linked字段仍34/47；其余配置、非空左手源、default/self/Unlink/部分覆盖整图、其它Provider与完整物理314/1680差异、近景及暂缓项保持，整个目标active。无资源保存/重导、GPU/managed全量/十分钟/性能。下条通用四容器待办为前批范围。

最新 Linked Montage 事件阶段：每实际实例独立空 bank，全部实例更新排队阶段，候选随角色验证/取消/提交；普通发布后按 Linked→Main 派发，Main 资源通知先于现有 Emote 消费者。原 Main-only 探针字节保持，新原生三 Provider×single/per-call 六轨迹2160帧覆盖组件派发/只派Main/隐藏恢复。Debug/实际 Optimize 最终28次运行、32400参考提交帧逐帧retry、Linked1825200/Main170640阶段比较与普通10560角色帧通过，Core56/构建0错误0警告；49冻结源、七轮六文件恢复、869 JSON/710原包/9配置独立审计过，见 [事件阶段与资源接口映射](docs/verification/2026-10-03-lyra-linked-montage-events.md)。**只关闭当前实例排队阶段，明确范围34/47；通用四类委托容器/回调重入、剩余十三字段、Main共享Montage模式变化、非空左手源、default/self/Unlink/部分及通用图继续开放。**下一步剩余配置与真正委托队列，再左手源和通用调用；ALS68/69/81、原完整物理314/1680差异、近景与暂缓项保持，整个目标 active。0资产保存，无重导/GPU/managed全量/十分钟/性能。下条十四剩余字段是前批范围。

最新 Linked 长待机/转身/左手设置：继续复用 ALS 68 skin/69 raw/81 logical；普通角色新增物理帧边界的左手覆盖开关，每实际实例保存设置，同类保留、换类原默认初始化、pending/外部owner/退役写入拒绝。新 UE 六轨迹4320帧覆盖双向 Turn、IdleBreak索引及左手0/1权重；Pistol原单项索引0保留。Debug/实际 Optimize 最终30进程通过，41040参考帧逐帧retry，新开关1944000比较；新普通十角色三频33600角色帧及两构建完整报告同，默认报告同前批，六轮程序集恢复与资源哈希独立审计通过。见 [长待机与左手设置](docs/verification/2026-10-03-lyra-linked-idle-turn.md)。**指定快照共33/47字段，原十二图字段均有变化证据；非空左手Sequence、余下十四配置/引擎字段、通用Layer和完整物理仍开放，目标 active。** 本批0资产保存，无资源重导/GPU/性能验收；下条五项缺少变化为前批范围。

最新 Linked 图私有状态对照：现有 Idle/Turn/Pivot/Stride/左手候选接入逐实例检查，Debug/实际 Optimize 最终24进程通过，32400参考帧逐帧取消重试，新增十二字段18403200比较；十角色报告同前批，资源/程序集恢复独立审计通过。详见 [图状态对照](docs/verification/2026-10-03-lyra-linked-graph-fields.md)。**指定快照共32/47字段；五项新增字段缺少变化轨迹，十五配置/引擎字段、完整物理及通用Layer仍开放，目标 active。**本批没有新UE采集、资源重导、GPU或性能验收。

基于 Godot 4.7.2 .NET 的 ALS V4 角色移动与动画示例。主场景为 `res://scenes/demo/als_demo.tscn`，包含 Mannequin、动作、Overlay、脚部处理和相机。项目仍在开发中，当前进度见文末及 [ROADMAP](ROADMAP.md)。

最新 Linked 移动缓存：九项加速度/LastUpdateVelocity/制动配置已按原初始化与全实例预更新接入，Stop/Pivot 消费各自实例快照；原 CDO、绑定组件配置及首次批次顺序分开。Debug/实际 Optimize 最终64个 Godot 进程通过，77760参考帧/逐帧retry，九字段19245600、旧三缓存6415200及八字段17107200比较；二十普通报告和逐调用点十角色同前批，六轮程序集恢复与资源哈希审计过，见 [移动缓存验证](docs/verification/2026-10-03-lyra-linked-movement-cache.md)。ALS68 skin/69 raw/81 logical保持。**指定快照仅核验20/47字段，完整私有历史、动态组件/配置、default/self/Unlink/部分整图及完整物理仍开放，目标 active。**未新UE采集/资源导出、GPU、全量managed、十分钟或性能；下方九缓存尚待为前批范围。

最新 Linked 预更新：每实际实例已分开保存游戏线程预更新和首次根访问 worker 历史，Idle 使用原 Montage/速度/跳跃三个批量缓存；Montage 快照包含本帧 prerequisite 请求。新增原生参考实际覆盖开火、ADS、隐藏恢复和重叠，Debug/实际 Optimize 最终64个 Godot 进程通过，77760参考帧/逐帧重试、原八字段17107200及新三缓存6415200比较；6项定向 Core、普通报告同前批、六轮程序集恢复和资源哈希独立审计通过，见 [预更新验证](docs/verification/2026-10-03-lyra-linked-preupdate.md)。ALS68 skin/69 raw/81 logical继续复用。**仅关闭指定三个缓存及上述轨迹；47字段全量、default/self/Unlink/部分和不同拓扑整图、完整物理仍开放，目标 active。**UE参考0保存，原夹具3254条Warning保留；无本批GPU/全量managed/十分钟/性能。下条58进程及开火/隐藏待验为前批范围。

最新实例更新：每实际 Linked 实例在候选帧首次真实根访问准备 worker 状态，同组借用、隐藏保留，Aiming/移动根/手部/Additives 使用自身实例数据。Debug/实际 Optimize 最终58进程通过，三频四布局及选定最终Rig边界共64800参考帧/逐帧重试、八字段14601600比较；普通换类/十角色和原组件回归、二十完整报告同前批、六轮程序集恢复及原资源哈希独立审计通过，见 [首次访问更新记录](docs/verification/2026-10-03-lyra-linked-worker.md)。复用ALS人物68 skin/69 raw/81 logical。**八字段是当前核验子集，全部私有字段、更完整开火/隐藏恢复、default/self/Unlink/部分整图及完整物理仍开放，目标 active。**没有本批UE启动/重导/GPU/全量或性能验收；下条 worker 尚待的描述属于前批。

最新多实例接入：十四入口已按真实绑定结果路由到 1/3/4/14 个实际图实例，私有源参加角色一次 Sync，完整姿态/曲线/属性/root 返回 Main，并统一提交或取消。原 ALS 人物与 68 skin/69 raw/81 logical 保留。Debug/实际 Optimize 共 180 个 Godot 进程通过，72 项三 Hz 三边界原精度及逐帧重试累计 181440 参考帧；四布局普通换层/十角色、原动作中换类、独立 scope 回归和十一轮程序集恢复通过，原资源哈希保持，见 [多实例运行记录](docs/verification/2026-10-03-lyra-multi-owner-runtime.md)。**全部实例 worker 字段及首次访问更新、default/self/Unlink/部分覆盖整图与完整物理仍开放，整个目标 active。**没有本批 UE 启动/重导/GPU/全量或性能验收；下方“生产仍单组”是各前批的验收范围。

前批 Main 状态拆分：Main 更新、直接 Lean、根回调历史及曲线反馈已由角色唯一对象持有，新 Linked 实例在构造时借用；换层保持实际对象，退休取消及其他角色认领均有门禁。Debug/实际 Optimize 共 42 个 Godot 进程通过，原精度三 Hz 三姿态边界及逐帧重试保持，完整普通报告与前批一致；五轮程序集恢复、869 JSON/710 原包/9 配置独立封口通过，见 [Main 归属验证](docs/verification/2026-10-03-lyra-main-state-owner.md)。该批只关闭 Main 归属拆分；ALS 人物继续复用，没有该批 UE 启动/重导或 GPU/完整物理重验。

前批多组 Layer 参考：在 ALS 81 logical 骨架上，三种 Provider 的 1/3/4/14 实例布局完成 30/60/120 Hz 原 Main 连续整图对照，另一个 60 Hz 原进程完整重复；共 48 条轨迹、38880 帧、1044 次同类 Link。十四入口均有正权重执行，实例私有字段/时钟逐帧不同，本轨迹四布局完整输出相同；重复请求和十二轨迹逐字哈希同，原资源保持。下一步按实际实例分配私有图宿主并统一收集源，见 [多组原生整图参考](docs/verification/2026-10-03-lyra-multi-layer-native-graphs.md)。该参考批没有 Godot 运行/新 AnimBP 保存或完整物理验收。

最新接口生成：原编译合同已接通用模型和生成器，入口枚举、三份 Pose 输入及双 double Aiming 参数由原 JSON 生成，并接普通 Main。13 项导入测试、独立新入口/双 Pose/五标量 ABI 编译运行及 Debug/实际 Optimize 共 40 次原绑定/角色/姿态回归通过；原资源保持，通用多组图执行和完整物理等价继续开放，见 [生成合同与参数接入](docs/verification/2026-10-03-lyra-layer-contract-codegen.md)。可用 `scripts/generate-lyra-layer-interface.ps1 -Check` 检查生成结果。

最新 Layer 绑定：普通实例归属已完成十种布局、93 步真实 UE 对照，两次独立捕获字节相同。按原生结果修正空类 Unlink 和共享实例退休后的空目标；18 项 managed、Debug/实际 Optimize 共 40 项场景/原 Main 姿态对照通过。ALS 人物保持 68 skin/69 raw/81 logical，869 份旧 JSON、710 个原包和项目配置哈希不变。生产图仍执行原十四入口单组；通用签名生成、多组姿态/回调及共享持久实例继续开放，见 [原生绑定矩阵](docs/verification/2026-10-03-lyra-layer-binding-native-matrix.md)。

最新穿透恢复：原CMC的MTD、拉回/膨胀检查、组合恢复与原位移重试已接现有物理owner。新增原配置资源；原96组及追加墙面/双墙16组全部在原门槛通过，实际16次单面恢复与8次组合MTD。完整运动仍314/1680帧失败，复杂凹面/多shape、代理及其余恢复分支尚需原生专项，见 [穿透恢复记录](docs/verification/2026-10-03-lyra-character-penetration.md)。

最新空中移动：普通玩家/NPC已接原PhysFalling的顶点分步、有限AirControl、碰撞剩余时间和着陆Walking分步。三个Provider的96组原生专项一致，Godot按原门槛通过88组，8组初始穿透恢复仍失败；三频完整轨迹未通过帧从841降至314/1680，Grounded与蹲姿差异均0，仍有地面碰撞速度与120Hz位置累积差。继续复用ALS人物68/69/81骨布局及十四入口共享Layer实例，见 [空中移动记录](docs/verification/2026-10-03-lyra-character-air.md)。完整物理等价、复杂地形和通用Layer继续开放。

本批Debug/实际Optimize的30个最终玩法进程通过，全部完整报告一致；42个最终玩法/诊断进程无Godot错误警告，独立审计与五轮Debug程序集恢复通过。严格精度失败仍保留。

前批地面移动：普通玩家/NPC已接真实胶囊扫掠与原MoveAlongFloor/StepUp顺序，站立/蹲姿30cm台阶、55cm高障碍、低屋顶与取消重试专项通过；Debug/实际Optimize共30项玩法回归通过，完整报告相同。原生32项仍有8项速度/法线精度差异，见 [地面扫掠与台阶](docs/verification/2026-10-03-lyra-character-ground-sweep.md)。

前一批地面策略：原Shooter的短胶囊地面查询、边缘支撑、1.9–2.4cm高度区间接入普通玩家/NPC，新增实际屋顶阻挡、站蹲保持和边缘落下测试；原生144项查询仍有三项接触点精度差异，三频完整同输入轨迹仍未通过，见 [地面查询与高度策略](docs/verification/2026-10-03-lyra-character-floor.md)。前一批切向抑制修正与原轨迹基线见 [真实轨迹](docs/verification/2026-10-03-lyra-character-trajectory.md)。

Lyra最新验证：继续复用ALS人物68根蒙皮骨和69 raw/81 logical，14个typed Layer入口共享装备实例，Main/Montage在换类时保留。普通玩家/NPC现已共用原Shooter配置、地面速度和空中积分服务；425组原计算及Debug/实际Optimize的三频物理、三频十角色、Root Motion/Warp/Emote共18项场景通过，真实渲染七图抽查，见 [场景移动接入](docs/verification/2026-10-03-lyra-scene-character-motor.md)。此前三个动画输出边界的原精度通过见 [混合修正](docs/verification/2026-10-03-lyra-ispc-mixing.md)，资源与14个Layer的对应见 [ALS人物与接口复核](docs/verification/2026-10-03-lyra-als-interface-review.md)。实际UE/Jolt完整运动等价、复杂地形/StepUp、近景握持及通用多Group/Unlink继续开放。

## 运行

需要 Windows、Godot 4.7.2 .NET、.NET 8 SDK 和 PowerShell 7。**首次 clone 后，必须先按[资产准备](#资产准备)从有权使用的 UE 源工程导出并导入资产；Git 仓库本身不能直接运行 Demo。**UE 5.9 ALS 源工程与 UE 5.8 GASP58 的 ALS V4 内容均有对应流程。

在仓库根目录创建本机配置，填写 `ALS_UE_PROJECT_ROOT`、`UE_ENGINE_ROOT` 和 `GODOT_EXECUTABLE`。`GODOT_ALS_ROOT` 与 `ALS_UE_PROJECT_FILE` 会由配置自动计算；`ALS_REFERENCE_ROOT` 仅用于相关原生对照验证。`.env.local.ps1` 已被 Git 忽略。

```powershell
if (-not (Test-Path .env.local.ps1)) { Copy-Item .env.local.ps1.example .env.local.ps1 }
# 编辑 .env.local.ps1 后加载配置
. ./.env.local.ps1
```

完成下文的资产准备，确认 `assets/generated/als_v4/compiled/als_animation_set.tres` 存在后，再构建并启动：

```powershell
dotnet build GodotALS.csproj -p:Optimize=true
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT
```

也可以用 Godot 打开根目录的 `project.godot`，按 F5 运行。C# 程序集需先构建，导入插件菜单才能执行。`p4_locomotion_demo.tscn` 是旧诊断场景。

Lyra 的 Win64 RootYaw 数学桥需先构建一次：运行 `./scripts/build-lyra-native-math.ps1 -EvidenceTag local-first`，再构建 C# 项目。它使用 MSVC x64 Build Tools，与 UE 向量版三角函数保持相同舍入；生成的 DLL 位于被忽略的 `assets/generated/lyra_als/native_win64/`。现有编辑器插件在 Windows x64 导出时把 DLL 放到程序旁边。其他平台使用标量计算，尚未通过本机 Win64 原生精度门禁。转向验证见 [完整 Main 转向](docs/verification/2026-10-02-lyra-whole-main-turning.md)。

## 资产准备

`assets/generated/als_v4/` 被 Git 忽略。首次 clone 缺少以下文件（路径均相对于该目录）：

| 文件 | 数量 | 来源 |
| --- | ---: | --- |
| `als_manifest.json` | 1 | P2A 导出清单 |
| `animations/*.fbx` | 126 | P2A 动画 |
| `meshes/skeletal/*.fbx` | 7 | P2A 骨骼网格 |
| `meshes/static/*.fbx` | 4 | P2A 静态网格 |
| `textures/*.png` | 4 | P2A 纹理 |
| `compiled/als_animation_set.tres` | 1 | P2B 编译资源，Demo 启动时加载 |

清单的 `files[]` 记录 141 个 FBX/PNG 的路径、大小和 SHA-256；仓库中的 `reference/als-v4-export.lock.json` 锁定该批清单。`.import` 与 `.godot/imported` 由 Godot 生成，不需要下载。若已合法持有与仓库锁定批次相同的本地导出文件，可放入上述目录，跳过 P2A，直接运行 P2B。

自行准备包含 `AdvancedLocomotionSystemV.uproject`、`Content/AdvancedLocomotionV4` 和 `Plugins/ALS/ALS.uplugin` 的 UE 5.9 ALS V4 源工程；[ALS V4 的 Fab 页面](https://www.fab.com/listings/ef9651a4-fb55-4866-a2d9-1b38b028f9c7)可供确认内容来源。加载 `.env.local.ps1` 后，在仓库根目录运行：

```powershell
$lockedManifestSha = (Get-Content reference/als-v4-export.lock.json -Raw | ConvertFrom-Json).manifestSha256
.\scripts\verify-p2a.ps1 -EngineRoot $env:UE_ENGINE_ROOT `
  -UnrealProject $env:ALS_UE_PROJECT_FILE -UpdateAssetLock
$exportedManifestSha = (Get-FileHash assets/generated/als_v4/als_manifest.json -Algorithm SHA256).Hash.ToLowerInvariant()
if ($exportedManifestSha -cne $lockedManifestSha) {
  throw '导出批次与仓库锁不一致；请核对 UE 源工程/版本，不要继续导入或提交新锁。'
}
.\scripts\verify-p2b.ps1 -GodotExecutable $env:GODOT_EXECUTABLE -CleanImport
if (-not (Test-Path assets/generated/als_v4/compiled/als_animation_set.tres)) {
  throw 'P2B 未生成 Demo 所需的动画资源。'
}
```

P2A 构建并部署 UE 导出插件，完成两次导出和确定性校验；`-UpdateAssetLock` 会发布输出并更新本地锁文件。P2B 构建 .NET 项目，执行 Godot 清洁导入及资产验证。若导出哈希不同，不要提交更新后的锁文件。详细流程见 [P2A 导出](docs/architecture/p2a-full-ue-export.md)和 [P2B 导入](docs/architecture/p2b-godot-import-closure.md)。

### GASP58 的 ALS V4 资源

若源工程是 `GASP58.uproject`（UE 5.8，含 `Content/AdvancedLocomotionV4`），使用资产专用插件和独立的本地锁。先执行 P2A、P2B，再生成六组与本次清单绑定的原始动画源：

```powershell
$gaspProject = '..\GASP58\GASP58.uproject'
$ue58 = '../UE_5.8'
$gaspLock = Join-Path (Get-Location) 'artifacts\gasp58\als-v4-export.lock.json'
.\scripts\verify-p2a.ps1 -EngineRoot $ue58 -UnrealProject $gaspProject `
  -BuildScript (Join-Path (Get-Location) 'scripts\build-gasp58-exporter.ps1') `
  -ReadyMarker 'GODOT_ALS_V4_EXPORTER_READY engine=5.8.1 plugin=1.0.0' `
  -CommandletName AlsV4AssetExport -AssetLockPath $gaspLock -UpdateAssetLock
.\scripts\verify-p2b.ps1 -GodotExecutable $env:GODOT_EXECUTABLE -AssetLockPath $gaspLock
.\scripts\export-gasp58-raw-sources.ps1 -EngineRoot $ue58 `
  -UnrealProject $gaspProject -GodotExecutable $env:GODOT_EXECUTABLE
```

原始源写入被忽略的 `assets/generated/als_v4_raw/`，运行时只在定义摘要匹配时读取；仓库锁与旧原生对照夹具保持原样。GASP58 的内容不包含另一路 `/ALS` Refactored 资产，[本次验证记录](docs/verification/2026-09-29-gasp58-export.md)列出资源来源、校验结果和适用边界。

### Lyra 玩家与 NPC 示例

普通入口使用原 Main/Linked Layer、ALS 人物和共用 Shooter 移动服务，支持三种装备与最多十角色：

```powershell
& $env:GODOT_EXECUTABLE --path . -- --locomotion=lyra --lyra-profile=rifle --lyra-characters=10
```

此入口另需本地 `assets/generated/lyra_als/character_motor_v2.json`、`character_floor_v1.json`、`character_penetration_v1.json`。这些资源从原Shooter CDO/PhysicsVolume/CVar只读采集生成，与动画资产一起保留；准备流程与原样本见 [场景移动接入](docs/verification/2026-10-03-lyra-scene-character-motor.md)、[地面策略](docs/verification/2026-10-03-lyra-character-floor.md)及[穿透恢复](docs/verification/2026-10-03-lyra-character-penetration.md)。已有资源应校验后复用。下文独立资源示例和早期状态是历史记录。

### Lyra 独立示例

`scenes/demo/lyra_unarmed_demo.tscn` 使用 ALS Mannequin 模型和骨架播放从 GASP58 Lyra Manny 重定向的 Unarmed/Pistol/Rifle 动画。资源就绪后按 `Q` 循环切换 Unarmed → Pistol → Rifle → Unarmed，鼠标右键进入 ADS；Relaxed/ADS AimOffset 按 Lyra 原权重连续混合。普通 `als_demo.tscn` 不受此支线影响。本地资源均位于被忽略的 `assets/generated/lyra_als/`，检出代码后需按 [Unarmed](docs/verification/2026-09-30-lyra-als-unarmed.md)、[Pistol](docs/verification/2026-09-30-lyra-als-pistol.md)、[Rifle](docs/verification/2026-09-30-lyra-als-rifle.md)、[逻辑骨资源](docs/verification/2026-09-30-lyra-logical-controls.md)及 [Layer 接入](docs/verification/2026-09-30-lyra-logical-layers.md)验证记录准备资源。双手 IK 已接独立示例；武器附着、Warping 和完整 Lyra 主图仍在路线中。

历史 [左手姿态层与 FullBody Additives](docs/verification/2026-09-30-lyra-pose-layers.md) 保留三层关闭左手覆盖的默认值，Shotgun 两个启用姿态在 ALS 骨架上通过组件验证。旧 DSL 的“Additives 禁用落地边”解释已由实际编译 handler9纠正为IsOnGround；[新Additives执行组](docs/verification/2026-10-01-lyra-additives-layer.md) 已完成三状态落地恢复及Main共同Sync组件验证，尚待完整主图位置与旧Demo切换。Shotgun完整玩法及SkeletalControls仍待。

后续已接 [Hand IK Retargeting](docs/verification/2026-09-30-lyra-hand-retarget.md) 首节点，按原右手优先权重修正 ALS 的 gun/IK 目标；324 组实际 UE 节点对照与三频率普通场景回归通过。其余手部求解与足部控制继续推进。

[编译接口与 Layer 实例](docs/verification/2026-09-30-lyra-linked-layer-contracts.md) 已接入：14 个入口按实际 `ItemAnimLayers` 组共同持有已实现姿态组件，同类重绑保留原实例和层内状态。实际 UE 八步绑定与 Godot 对照、三频率场景回归通过。

[原 Cycle 源组件](docs/verification/2026-10-01-lyra-cycle-source.md) 已通过三个 Layer、三频率共3780帧原生回调/源更新/同步对照，保留换源时钟、惯性请求与double步幅权重；每帧取消重试和坏提交拒绝通过。该组件尚未接独立 Demo 的生产源宿主，完整 Layer/Main 图与统一通知仍在推进。

后续 [Cycle 根源遍历](docs/verification/2026-10-01-lyra-cycle-layer-sources.md) 补齐同一Layer中HipFire evaluator和循环源的登记顺序、共享Sync与统一提交；普通/隐藏重初始化两组7560帧逐位一致。完整Warp姿态、原子曲线/属性容器和生产源替换仍待接入。

[Cycle 姿态组件](docs/verification/2026-10-01-lyra-cycle-layer-pose.md) 已接双源共同Sync候选后的81骨取样、原LayeredBoneBlend与曲线/整数属性混合；九轨迹3528活跃帧/285768骨及72原生数据探针通过，保留原精度门槛和取消重试。该阶段输出止于Warp前，未包含 generated RootMotion属性或Orientation/Stride Warp。

后续 [RootMotion 属性组件](docs/verification/2026-10-01-lyra-root-motion.md) 已接真实共同 Sync 的 previous/delta、未锁定根轨道区间、正反循环累积和原 LayeredBoneBlend typed Transform 混合。189 源/3024 区间、3528 活跃帧与72原生属性探针通过；3255 帧属性存在，其中299帧为单位变换，严格门槛下 root TRS 误差均0。当前输出仍在 Warp 之前，Orientation/Stride Warp、完整 Main 与生产宿主仍待接入。

后续 [OrientationWarping 组件](docs/verification/2026-10-01-lyra-orientation-warping.md) 已在同一候选接原组件空间 Warp 与节点历史，并显式适配 ALS 三节脊柱。九轨迹3528活跃帧/285768骨与3255 root属性严格对照通过，2661帧姿态和2395帧 root translation 实际改变；取消重试与旧入口回归通过。当前到 `AfterOrientation`，方向/组件输入仍为受控输入，原 Main 更新、Stride 和生产接入尚待完成。

后续 [双 Warp 组件](docs/verification/2026-10-01-lyra-stride-warping.md) 已接同一组件姿态中的原 Orientation→Stride，包括步幅滤波、RootMotion 缩放、RK4 骨盆调整和原腿长限制。3528活跃帧/285768骨/3255 root属性严格对照通过，Stride 相较 Orientation 改变2613姿态帧/2349 root帧；Core16及旧入口回归通过。当前到 `AfterStride`，原 Main 速度/方向/alpha 更新、最终足部链和生产接入仍待完成。

后续 [原 Cycle 根图](docs/verification/2026-10-01-lyra-cycle-runtime.md) 已接 Main 的不带 Offset 方向、速度和本帧 Layer 回调 alpha，并对照同一次原编译根 Update→共同 Sync→Evaluate。三个 provider 三Hz共3528活跃帧/285768骨、曲线/属性及3255 root属性通过；源与引脚位值一致，root TRS差0，取消重试/31689坏操作拒绝、Core19和旧入口回归通过。当前为 `OriginalCycleRoot` 局部组件；Main输入仍是受控快照，完整角色更新、其它入口、统一通知/足部和生产源替换继续推进。

[Layer 姿态缓冲](docs/verification/2026-09-30-lyra-pose-buffers.md) 已接独立示例：当前 HipFire/LeftHand/Aiming/Additives/RootYaw/Hand Retarget 通过独立输入输出组合，按原主图 Additives 0.65→RootYaw 顺序执行并一次发布最终层姿态。1260 帧缓冲隔离、原组件对照及三频率换层通过；完整曲线/属性、Slot/惯性化、共享 Sync/Notify 与剩余 IK 继续推进。

[右手 TwoBoneIK](docs/verification/2026-09-30-lyra-right-hand-ik.md) 已接同一缓冲链：按原 CDO 开关和禁用曲线控制，Unarmed 关闭、Pistol/Rifle 启用，保留右手末端旋转。648 组实际 UE 节点对照、108 组 Godot 输入、三频率运行及五张渲染图检查通过；左手武器空间虚拟目标和完整手足链仍待。

[武器空间通道](docs/verification/2026-09-30-lyra-weapon-space.md) 已完成全部 189 普通/45 Aim 序列的原生采样和 432 组 CopyBone 对照。[ALS 控制骨适配](docs/verification/2026-09-30-lyra-logical-controls.md) 已导出并编译 69 raw/81 logical 源姿态，原 68 根蒙皮骨保持原映射；936 组源采样 UE 对照通过。weapon 和左手虚拟目标在关键帧采样时进入姿态，随后独立插值。[81 骨 Layer 运行链](docs/verification/2026-09-30-lyra-logical-layers.md) 已接源播放、当前姿态层、RootYaw、CopyBone 和左右手 IK，最后发布 68 根 skin 骨；48 组实际 UE 四节点手控整链、三频率换层运行和五张渲染图检查通过。完整主图/Sync/Notify/惯性化与足部仍待。

[原主图状态组件](docs/verification/2026-09-30-lyra-locomotion-machine.md) 已按当前 compiled exit 顺序实现 12 状态/36 边的规则与选边，1536 次 UE 实际编译判断和 480 次原生选边对照通过。它尚未替换示例的状态/源宿主；真实源权重、共享 Sync/Notify、原标准混合与惯性请求需要共同接入。

[主状态姿态组件](docs/verification/2026-09-30-lyra-locomotion-pose.md) 已实现原 HermiteCubic 重叠过渡、源更新上下文和四条最终惯性请求；真实 UE Main 节点的三频率 840 帧、81 骨受控合成对照通过，曲线/状态权重精确相同。ALS 惯性化与 Standing 原生回归通过。它尚未接入示例的实际源宿主，完整 Linked 子图、Sync/Notify 与主图连续验收继续推进。

[编译源库存](docs/verification/2026-09-30-lyra-source-nodes.md) 已核对十类 255 个源节点、完整 Layer 与嵌套机器归属。Start/Stop/Pivot 的 evaluator、Pivot/FallLand 强制选主及 Main 三个 Lean 播放器配置已保留；evaluator/强制选主已完成下述组件对照，完整真实源宿主仍待接入。

[Main Lean 资源](docs/verification/2026-10-01-lyra-main-lean-resources.md) 已补齐三个原 BlendSpace 样本：同一 ALS81 bank 显式加载后含 237 源，原 68 skin 采样不变，局部 additive 与 Aim mesh additive 分开处理。24 原生源采样、33 静态 BlendSpace 及扩展库中的旧 936 源采样通过。已有独立 Demo 入口未切换到该扩展库。

后续 [Main Lean 播放器](docs/verification/2026-10-01-lyra-main-lean-runtime.md) 已实现原 speed=3/EaseInOut 平滑、三个 occurrence 独立历史与共同 Sync 候选；三频率 2100 物理帧/2121 次更新的权重、变化率和时钟与真实 UE 编译节点逐位一致，并行求值、取消重试及 update-only 通过。首次重定向 ensure 已修复，全新目录的三个专用验证目标/24 组姿态与现有目标完全一致。Main 旋转与 ApplyAdditive 组件进度见下段，完整 Main/其余 Layer、统一通知和生产接入继续开放。

[Main 旋转与 Lean 合成](docs/verification/2026-10-01-lyra-main-lean-composition.md) 后续已完成原 UpdateRotationData 和三个 ApplyAdditive 组件：确认 BreakRotator 的 float Yaw → double 运算、首次保留速度和旋转先于站蹲刷新；2100 物理帧/2121 根输出/171801 骨、966 曲线、8484 整数属性与 2121 RootMotion 属性通过严格对照，取消重试/update-only/并行通过。编译链接纠正为 Start Lean12、Cycle16、Pivot22。基底是显式完整源姿态边界，尚非三阶段完整 linked provider/Main 或生产接入。

[Main观察与Cycle接入](docs/verification/2026-10-01-lyra-main-observation-cycle.md) 后续已完成原前六段更新顺序与真实Character/Movement输入边界：2520帧/15120阶段标量与标志逐位一致；共同候选宿主把新方向、速度、站蹲直接交给原Cycle callback和双Warp。三provider×三Hz联合3780帧/3528次Cycle输出/285768骨、14112整数属性与RootMotion严格通过，取消重试/预校验提交及旧Cycle/Lean/Demo回归通过。后五段 Main 更新进度见下段；实际主图状态权重/惯性/Lean 根、Godot gather、通知和生产入口继续推进。

[完整 Main 更新与 Cycle](docs/verification/2026-10-01-lyra-main-update-cycle.md) 随后补齐原 BlendWeight、RootYaw、Aiming、JumpFall 和 FirstUpdate 清除，直接对照 UE 原 `BlueprintThreadSafeUpdateAnimation`。三频率 2520 帧的标量、弹簧和标志逐位一致，含关闭 RootYaw 的 Hold、Dashing+Accumulate、实际 Montage 地面/空中门控和权重衰减；真实 Linked Cycle 联合 3780 帧/3528 次姿态/285768 骨严格通过，所有历史随同一候选提交或取消。此项完成完整更新函数与所测 Cycle 根，Main 状态权重/惯性/Lean 主根、其余 Layer、Godot gather、统一通知/Montage、最终足部和生产接入仍开放。

[Main Cycle 与 Lean 原根合并](docs/verification/2026-10-01-lyra-main-cycle-lean.md) 后续已接真实 Linked Cycle 与 Lean 同一次 Sync/共同提交，保留 ALS 68 skin/81 logical。三provider×三Hz 3780帧/3528最终输出/285768骨、14112属性和3406 RootMotion严格通过；首次Linked BlendIn与Lean时钟逐位一致，取消重试及旧入口回归通过。实际LocomotionSM权重、其它入口、外层惯性/Notify/Montage、最终足部及生产接入继续开放。

[Start 原源回调与同步](docs/verification/2026-10-01-lyra-start-source.md) 已完成三provider×三Hz 3780帧的单源对照，沿用36个ALS骨架动画；原重新相关选源、显式/同步双时间、实际压缩Distance曲线和Marker逐位一致，取消重试及旧同步链回归通过。完整Start Layer根、Main状态遍历和生产接入仍待。

[源曲线与骨骼属性](docs/verification/2026-09-30-lyra-source-curves.md) 已完整导出 234 条序列的 159 条曲线和 936 条 Pelvis 整数属性，并接联合姿态取样接口。73,650 个原生时间样本逐值一致，ALS 原生回归及独立示例 60Hz 通过；完整 Layer 曲线/属性传递、真实源时钟与共享 Sync 仍待接入。运行需补齐新增的 ignored `logical_controls/curve_bank.json`。

[SequenceEvaluator 与非循环 Marker](docs/verification/2026-09-30-lyra-evaluator-sync.md) 已实现原生 evaluator 更新、AlwaysLeader 固定分数、惯性重入及 Start/Pivot 边界，同步候选保留 occurrence 的 Marker 存储。54 条三频率轨迹、9,450 帧逐位一致；Core 138 项、ALS 原生六组和现有独立 Demo 60Hz 回归通过。该组件尚未接真实主图/Layer 源宿主，完整 Notify/Sync 与资产切换仍待；Main Lean 组件进度见上文，复跑 evaluator 对照需要新增 ignored evaluator request/native JSON。

## 操作

| 输入 | 行为 |
| --- | --- |
| `W` / `A` / `S` / `D` | 移动 |
| `Alt` / `Shift` | 行走 / 冲刺 |
| `Ctrl` / `Space` | 切换蹲伏 / 跳跃 |
| `V` / 鼠标右键 | 切换旋转模式 / 瞄准 |
| 鼠标移动 | 控制视角 |
| `B` / `T` | 切换第一、第三人称 / 左右换肩 |
| `R` / `X` | 翻滚 / 取消当前动作 |
| `G` | 进入或退出 Ragdoll |
| `Q` / `E` | 切换 Overlay 与道具 |
| `Esc` | 切换鼠标捕获 |

## 验证

资产导入成功后，可运行键鼠与动作输入冒烟测试，以及 .NET 测试：

```powershell
& $env:GODOT_EXECUTABLE --path $env:GODOT_ALS_ROOT --rendering-method gl_compatibility `
  res://scenes/tests/p4_keyboard_mouse_smoke.tscn
& $env:GODOT_EXECUTABLE --headless --path $env:GODOT_ALS_ROOT `
  res://scenes/tests/action_input_smoke.tscn -- --hz=60
dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj -c Release
dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release
```

完整的阶段验收与历史限制见 [ROADMAP](ROADMAP.md) 和 `docs/verification/`。

## 许可

原创代码及相关文档采用 [MIT 许可](LICENSE.md)。ALS V4、Epic/Fab 等第三方资产及其导出或转换文件不属于 MIT 许可范围，适用各自的来源条款。

[Epic 内容许可](https://www.unrealengine.com/eula/content)限制源格式内容的对外分发；[Fab 标准许可](https://www.fab.com/eula)允许按条款与项目协作者私下共享，但不允许单独公开再分发资产。因此本仓库提供导出工具，不上传这批源资产。具体边界见 [资产许可](ASSET_LICENSE.md) 和 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 开发状态

普通 Demo 已接入站立与蹲伏移动、Overlay、Roll、Mantle、Ragdoll/起身和 ALS 相机。后续重点是完整动画链路与复杂地形的对照验收、物理稳定性和长时间性能测试；当前范围与优先级以 [ROADMAP](ROADMAP.md) 为准。
