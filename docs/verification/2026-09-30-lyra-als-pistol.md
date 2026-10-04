# Lyra Pistol 到 ALS Mannequin 的资源与换层

2026-09-30，工作目录 `.`，源工程 `..\GASP58`，UE 5.8.1、Godot 4.7.2 .NET。生成资源在被 Git 忽略的 `assets/generated/lyra_als/`；代码检出本身不能运行此示例。普通 ALS Demo 未切换到 Lyra。

## 资源与骨架

- 以 `ABP_PistolAnimLayers` CDO 的实际属性路径为清单，导出 63 条普通 Sequence 的 ALS 目标 FBX/侧车，逐条记录源/目标 `.uasset`、FBX 哈希及原始曲线、209 个 Sync Marker、508 条 Notify、播放率和根位移元数据。与此前 Unarmed 62 条同绑一个 ALS Mannequin 的 68 根物理骨和一个 Godot `AnimationPlayer`；63 条每条取 3 帧、共 189 帧 × 68 骨做有限数值门禁。
- Pistol ADS AimOffset 有 15 个源 Mesh Space additive 样本，重定向到 ALS 79 逻辑骨/68 物理骨并保存哈希和 UE 评估姿态。源原生三角网格为 16 个三角形；Godot 对 9 个网格及 7 个非网格输入的样本权重与 UE 比较，单项误差不超过 `2e-5`。Pistol 非 ADS 的 `RelaxedAimOffset` 在 CDO 中指向 Unarmed AimOffset；ADS 使用 Pistol 自己的样本。Pistol Jump Recovery 是 Local Space additive，源 `baseAsset` 指向自身且 `baseFrame=22`；目标也保留自引用，仅导出资源，尚未接动作图。
- 现有 ALS 模型/骨架足以承载本批地面、空中、转身和 AimOffset 姿态，无需在切层时重建角色或物理体。它缺少 Lyra 后处理所需的 `weapon_r`、`spine_04/05`、`VB IK_Hand_L_weaponSpace` 等目标；当前截图没有武器模型和手部 IK，不能据此宣布完整 Pistol 角色等价。

## Interface 与运行

`LyraLinkedLayerRouter` 读取 CDO 绑定，把 `FullBody_Idle/Start/Cycle/Stop/Pivot`、五个空中状态和四向 Turn 路由到当前 Profile。`LyraUnarmedMotion.LinkLayer` 在现有角色上替换 Aiming/HipFire 提供者，保留 `CharacterBody3D`、`Skeleton3D`、主运动状态及共享 Cycle 播放器；`Q` 可在独立场景切 Unarmed/Pistol。Pistol 的 `IdleAimOffset` 与 `RelaxedAimOffset` 现按源 `AimOffsetBlendWeight` 在同一基础姿态上连续混合；`ALI_ItemAnimLayers` 的 SkeletalControls、FullBodyAdditives、LeftHandPose 及主图中的 Warping、FootPlant、上下身完整拓扑仍待实现。

Pistol 原 `pistol_walk_right_cycle` 仅有一个 `L` Marker。UE `FAnimGroupInstance::Prepare` 接受来源标记集合，并在 Sequence 资源改变时重置 marker tick record；Godot 现对这条源序列使用真实单标记集合，换源重建记录，没有伪造 `R` 标记。`Walk_Left → Walk_Right → Walk_Left` 在 30/60/120Hz 各逐帧 3 秒通过。

## 导出与验证

当 UE GUI Editor 仍占用项目内旧插件 DLL 时，以 `build-gasp58-exporter.ps1 -ExternalOnly` 编译版本 2 外置插件，并用 `-ExternalPlugin` 仅为命令行导出进程指定它；不会停止 GUI Editor，也不会替换锁定的项目 DLL：

```powershell
$ue58 = '../UE_5.8'
$gasp = '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-pistol.ps1 -EngineRoot $ue58 -UnrealProject $gasp
.\scripts\build-gasp58-exporter.ps1 -EngineRoot $ue58 -UnrealProject $gasp -ExternalOnly
$plugin = '.\artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
.\scripts\export-lyra-pistol-special.ps1 -EngineRoot $ue58 -UnrealProject $gasp -ExternalPlugin $plugin
```

普通序列分批导出最终报告 `LYRA_PISTOL_EXPORT_OK clips=63 notifies=508 markers=209`；特殊资源脚本六个 UE 命令行进程分别报告 inventory、defaults、`aim clips=15`、`jump clips=1`、`grid rows=9`、`interp-grid rows=7`，均退出 0。首次 Aim 批次曾在写出 15 样本后触发 UE `SkeletonToCompactPose.IsValidIndex` ensure 并退出 1，复跑同资产验证退出 0；Jump 校验起初误要求 `baseAsset=null`，按源自引用关系修正后退出 0。保留这些失败作为过程证据，不能把首次失败算作成功。

本机只安装 .NET SDK 10.0.300，仓库 `global.json` 指定 8.0.100；从父目录 `..` 执行 `dotnet build .\GodotALS.csproj -c Debug --no-restore -v quiet` 最终为 0 警告/0 错误。Godot `lyra_pistol_catalog_smoke.tscn` 输出 `clips=63 poses=189 playback=125 roots=125 notifies=938 markers=436 skeleton=68 aim=15/16/3 native=16 blend=30/60/120`；Unarmed 目录 smoke 回归通过。独立场景 `--lyra-pistol-switch-smoke --lyra-unarmed-hz=30|60|120` 各退出 0，Unarmed→Pistol→Unarmed 两次换层，三档 ADS 分别 15/30/60 帧，Pistol Cycle/Pivot/Stop 和原 Unarmed Idle 均到达；渲染版另输出两张 [移动姿态](../../artifacts/lyra-analysis/pistol-runtime-switch.png) 与 [ADS 姿态](../../artifacts/lyra-analysis/pistol-runtime-ads.png) 截图，已目视确认 ALS Mannequin 可见且骨骼非空。

## Pistol Aiming 连续权重（2026-09-30 续）

当前 GASP58 `ABP_ItemAnimLayersBase` 只读导出的 `Update Blend Weight Data` 与 `BlueprintThreadSafeUpdateAnimation`，与 2026-09-13 BlueprintLisp 快照逐字一致，SHA-256 分别为 `c1ad3ddde48cbfc20a637ae4bbea22773b84724d8bbe62031026d48a97000019` 和 `aa9611bfad0315cc52ec49465a6251da08c24b885ef5b9918af2d185831d4de2`；`UnrealEditor-Cmd` 退出 0，未修改 UE 资产。原图每帧先更新 HipFire 和 Aim 两个权重：蹲姿禁举枪或站立地面 ADS 时为 0/1；近期射击、空中/蹲姿 ADS 或 HipFire 曲线触发时为 1/1；其余按 `FInterpTo` 分别以速度 1 回零和速度 10 趋向 HipFire 权重或 1，选择条件是 `abs(RootYawOffset)<10 && HasAcceleration`。此前 Godot 把 `IsOnGround` 单独并入首分支，导致地面 HipFire 强制归零；已修为原图的站立 ADS 合取条件。

Godot Pistol 层现以同一帧基础骨骼姿态分别求值 Relaxed/ADS Mesh Space AimOffset，然后按 `AimOffsetBlendWeight` 混合 68 根 ALS 骨；Unarmed 两端资源相同，仍按原图每帧求值。目录 smoke 校验 0/0.5/1 三个姿态权重、基底恢复，以及 30/60/120Hz 的三类分支、HipFire 衰减和 RootYaw 门控。原 `--lyra-pistol-switch-smoke` 三频率仍保有 2 次 Cycle 换源；另以 `--lyra-pistol-aim-blend-smoke` 运行移动中 ADS 释放，三频率分别出现 22/47/99 帧非端点权重。首次把释放窗口并入原换层 smoke 时 Cycle 换源降为 1 而失败，拆成两条受控轨迹后各自通过，没有放宽原换层门槛。

60Hz OpenGL `--lyra-pistol-aim-blend-capture` 输出 [中间权重姿态](../../artifacts/lyra-analysis/pistol-runtime-aim-blend.png)，SHA-256 `C750DB735B3BA2F1C6299B867240B351763030B375A453712F10A8385D8A6BFC`；画面中 ALS 模型、Pistol 动作和阴影可见。此批验证覆盖权重规则、运行时过渡和单帧渲染；仍缺 UE 同输入连续主图姿态、曲线和 FootPlant/IK 结果对照。

尚无 UE/Godot 同输入连续完整主图姿态、通知、根位移对照，也没有武器附着和人工玩法/性能验收；不能将本批资源及单场景换层结果计入普通 ALS R2–R7 关闭。
