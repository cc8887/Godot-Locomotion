# Lyra Rifle 到 ALS Mannequin 的资源与换层

2026-09-30，在 `.` 主目录执行，源工程 `..\GASP58`，UE 5.8.1 与 Godot 4.7.2 .NET。生成资源位于 Git 忽略的 `assets/generated/lyra_als/`；仅有代码检出不能运行本批场景。

## 导出范围

- 以 `ABP_RifleAnimLayers` CDO 的 67 个不同资源绑定为清单：64 条普通 Rifle Sequence、1 条 Local Space Jump Recovery additive、2 个 AimOffset BlendSpace。64 条普通 Sequence、Rifle ADS 的 15 个 Mesh Space additive 样本和 Jump Recovery 已重定向/导出；RelaxedAimOffset 继承 Unarmed 的已有资源。
- 使用 GASP58 已有 `RTG_UE5Manny_UE4Manny`，源为 Manny 网格，目标为 ALS Mannequin 68 根物理骨。64 个目标 `.uasset` 保存在 GASP58 的 `/Game/GodotLyraRetarget/Rifle`；对应 FBX 2020 与源元数据侧车保存在 Godot 的 `animations/rifle/`。`rifle_catalog.json` 记录源/目标资源与字节级 SHA-256；另有曲线及 214 个 Sync Marker、512 条 Notify、播放率和 Root Motion 的源/目标对照 JSON。五份 JSON 中未改变已有文件的格式。
- `rifle_catalog.json` SHA-256 为 `2085DDD7B790301BA9AA6A92D46FF2A07854530626B631D737BDB1B2C12CDAA7`。独立复核 64 个源 `.uasset`、64 个目标 `.uasset`、64 个 FBX 与侧车中的哈希及目标骨架路径，输出 `LYRA_RIFLE_HASH_OK clips=64 artifacts=192`。

## UE 压缩断言

首次分批重定向在 `Crouch_Walk_Fwd_Pivot` 的压缩任务触发 `IsRotationNormalized()`；同资产单次重试仍失败，后续重试又成功。以批量重定向实际参数（Raw、Manny 网格、保留 Root Motion 于姿态）对其 71 个源关键帧逐骨只读采样，四元数模长平方误差最大 `3.68e-7`，缩放最小绝对值为 1。`-onethread` 不能阻止压缩 Worker 后续在别的动作再次断言，因此不视为修复。

后续其他动作还出现 `BonePose.h:639/664` 的 NaN/变换断言。`export-lyra-rifle.ps1` 只对 UE 退出码 3、存在压缩任务日志且断言为上述已识别形式时做至多 6 次连续重试；每次重启校验已保存 FBX/侧车哈希，其他错误立即退出。最终导出输出 `LYRA_ITEM_EXPORT_OK profile=rifle clips=64 notifies=512 markers=214`、退出码 0。断言的根因尚未消除，不能把重试当作引擎级修复；源动画或 Retargeter 输出的完整逐帧诊断仍需继续。

## Godot 验证

- 从 `..` 执行 `dotnet build .\GodotALS.csproj -c Debug --no-restore -v quiet`，0 警告、0 错误。Godot `--headless --editor --import --quit` 退出 0，64 个 Rifle FBX 均被导入。
- `res://scenes/tests/lyra_rifle_catalog_smoke.tscn` 首批输出 `LYRA_RIFLE_CATALOG_OK clips=64 poses=192 routes=324 skeleton=68`；三帧/动作检查位置、旋转和缩放有限、四元数归一化，并对 CDO 的地面/空中/转身入口逐个校验资源路由。324 是状态/方向条件组合数，不是独立动作数。新增特殊资源与三层元数据后，最终输出追加 `aim=15/16/3 native=16 layer=rifle playback=189 roots=189 notifies=1450 markers=650`；同时验证 Relaxed/ADS 的 0/0.5/1 混合姿态与基底恢复。
- 共用目录读取调整后，Pistol 原目录 smoke 仍输出 `clips=63 poses=189 playback=125 roots=125 notifies=938 markers=436 skeleton=68 aim=15/16/3 native=16 blend=30/60/120`，退出码 0。

复跑导出：

```powershell
.\scripts\export-lyra-rifle.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject' -BatchNew 4
```

## 特殊资源与层提供者

`rifle_special_inventory.json`、`rifle_aim_samples_catalog.json`、`rifle_jump_additive_catalog.json` 保留源/目标资产哈希、68 物理骨/79 逻辑骨和 UE 评估姿态。Jump Recovery 的源基底资产为自身、第 28 帧，目标保留同一关系；目前只导出，未强制接普通落地。AimOffset 15 个样本与 16 个三角形被 `LyraUnarmedAimOffset.LoadRifle` 读取，9 个网格和 7 个非网格输入的权重与 UE 输出逐项比对（门槛 `2e-5`）。这仅证明权重与资源，Rifle 没有新增 UE/Godot 最终叠加姿态 oracle。

`LyraWeaponAnimationLayers` 共用于 Pistol/Rifle；`ILyraItemAnimationLayers` 提供状态资源、转身资源、Aiming/HipFire 和实际 CDO 播放参数。`LyraItemAimingLayer` 在同一基础姿态上混合共享的 Relaxed 与各武器 ADS，主角色保留 RootYaw、运动状态、单 Cycle 播放器与 typed 通知队列。Rifle 的播放设置独立读取并校验；当前 Start/Pivot 0.6–5、Cycle 0.8–1.2 与 Unarmed 相同，但不把继承相同当作未来不需读取的理由。

特殊资源独立哈希复核输出 `LYRA_RIFLE_SPECIAL_HASH_OK artifacts=32`，涵盖 16 个源和 16 个目标 `.uasset`，并验证两个 catalog 的 inventory 字节哈希。脚本初次重定向同 Pistol 曾出现 `SkeletonToCompactPose.IsValidIndex` ensure；已有资源复跑以退出 0 作为通过依据。

本轮再次执行完整特殊导出脚本，六个 UE 进程分别输出 `LYRA_ITEM_SPECIAL_OK profile=rifle samples=15 baseFrame=28 assets_saved=0`、`LYRA_ITEM_DEFAULTS_OK profile=rifle`、`aim clips=15`、`jump clips=1`、`grid rows=9`、`interp-grid rows=7` 并退出 0；日志保存在 `artifacts/lyra-analysis/rifle-special-reverified.log`。复跑之后再次检查 32 个资产哈希和 Godot Rifle 目录，均通过。

```powershell
.\scripts\export-lyra-rifle-special.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' `
  -ExternalPlugin '.\artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
```

## 独立角色运行

`scenes/demo/lyra_unarmed_demo.tscn` 在完整资源存在时默认绑定三层；Q 按 Unarmed → Pistol → Rifle → Unarmed 循环，HUD 显示当前层及操作。同一角色保存物理体与骨架，绑定共 189 条普通 Sequence、1450 条资源级 Notify、650 个 Marker；本帧实际活跃播放源消费既有 typed 队列。尚非完整多源共享图 Sync 或角色事务。

`--lyra-rifle-switch-smoke --lyra-unarmed-hz=30|60|120` 每档 14.5 秒，调用普通 Q 使用的循环方法六次。检查同一角色/骨架身份、逐帧 68 骨有限与旋转归一化，覆盖 Rifle Start/Cycle/Pivot/Stop、蹲伏 Cycle、ADS Idle、五个空中阶段、右转及回 Unarmed Idle。结果：

| Physics Hz | 帧数 | Layer 切换 | Cycle 换源 | ADS 帧 | Aim 中间权重帧 | Rifle 通知 | 转身反馈帧 |
|---|---:|---:|---:|---:|---:|---:|---:|
| 30 | 435 | 6 | 6 | 77 | 22 | 47 | 17 |
| 60 | 870 | 6 | 6 | 155 | 47 | 47 | 36 |
| 120 | 1740 | 6 | 6 | 310 | 99 | 47 | 72 |

日志：`artifacts/lyra-analysis/rifle-switch-{30,60,120}-verified.log`。同代码 Pistol 60Hz 回归仍为两次 Layer、两次 Cycle 换源、30 ADS 帧；Pistol 与 Unarmed 目录 smoke 通过。最终 .NET build 0 警告/0 错误，Godot 导入与最终场景日志无 ERROR/WARNING。

60Hz OpenGL 另运行同轨迹及 `--lyra-rifle-switch-capture`，输出 Cycle、Crouch、ADS、Jump、Turn 五张 1280×720 截图并逐张抽查，ALS 网格与动作可见。图位于 `artifacts/lyra-analysis/rifle-runtime-*.png`，渲染运行退出 0、相同计数；这是渲染证据，不是完整人工玩法签收。画面目前没有武器模型和左手 IK。

截图 SHA-256 清单：`artifacts/lyra-analysis/rifle-render-hashes.json`。复跑场景：

```powershell
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . res://scenes/demo/lyra_unarmed_demo.tscn -- `
  --lyra-rifle-switch-smoke --lyra-unarmed-hz=60
```

渲染验证去掉 `--headless`，在 `--` 之前加 `--rendering-method gl_compatibility --resolution 1280x720`，之后加 `--lyra-rifle-switch-capture`。

首轮编译局部名冲突、旧 Cycle 数量门禁、Pivot 测试误要求反转后的方向、ADS 轨迹在 Stop 完成前结束等失败保留。生产 Cycle 门禁已改为每个已绑定 Profile 的完整方向集合；Pivot 测试按主图真实 outgoing 方向校验，ADS 轨迹增加落地后窗口并保持既有覆盖门槛。未修改 UE 源码。

完整 `ALI_ItemAnimLayers` 姿态图、SkeletalControls/FullBodyAdditives/LeftHandPose、武器附着、Warping/足部、UE/Godot 连续主图姿态 oracle、人工玩法与性能验收仍未完成。普通 ALS Demo 未切到 Lyra，原 ALS R2–R7 关闭状态不变。
