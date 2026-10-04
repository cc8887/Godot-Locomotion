# Lyra 同输入真实物理轨迹与斜向碰墙

本批建立原 UE CMC 与实际 Godot/Jolt 的同输入比较，并修正接近正面撞墙时切向滑动被 Godot 默认角度门槛抑制的问题。完整物理轨迹仍未通过，角色位置、地面判断及空中碰撞积分继续开放。此前共用移动服务接入见 [场景移动](2026-10-03-lyra-scene-character-motor.md)。

ALS Mannequin 的 68 skin / 69 raw / 81 logical 资源以及角色内十四入口共享的 ItemAnimLayers 继续使用已有实现，详见 [人物与接口](2026-10-03-lyra-als-interface-review.md)。本批没有重新导出动画、修改 UE 工程或启动 UE；没有新增渲染、近景握持、复杂地形或通用 Layer 验收。

## 原生参考与真实输入

`tools/export_lyra_movement_trajectory.py` 从已有三频完整原生物理记录抽取控制输入与移动后的观测：`cmc30-ground-events`、`cmc60-ground-events-final`、`cmc120-ground-events`。这些原记录包含真实 Shooter 角色、控制器、胶囊及原 CMC；同一频率三个 Provider 的物理输入和结果逐项一致，抽取时再次校验旧审计哈希及配置。

紧凑参考为 `artifacts/lyra-analysis/character-motor-trajectory-v1-reference.json`，SHA256 `F145EF7683223E2432DFB9E1CCFE78254A5943A36C3683E507D791C565A95FD8`。30/60/120 Hz 各八秒，240/480/960 帧，合计 1680 帧。生成器拒绝覆盖已有文件。

`LyraCharacterTrajectorySmoke` 建立相同的地面、墙体和初始胶囊中心，起点来自原探针的 `(0,0,92)` cm。真实物理帧只使用作者输入的方向、控制器朝向、俯仰、ADS、蹲伏和跳跃驱动共同服务。`LyraSceneMovement.WorldSpace` 可显式传世界方向；默认局部方向保留普通玩家调用方式。原生位置、速度和地面标志只在实际移动及动画提交之后进入比较。

每帧调用原完整动画宿主，取消后重试，并检查物理变换、凭据和 MoveAndSlide 次数不变。轨迹门槛为位置 `0.01 cm`、速度 `0.001 cm/s`、加速度 `0.001 cm/s²`，地面和蹲伏标志严格相等。此前原速度内核及动画比较的门槛保留。

报告包含全部帧的输入、原生结果、实际结果、位移、请求速度和接触数量。任意不一致使 Godot 进程退出 1。脚本记录完整诊断后也退出 1；`diagnosticsCompleted=true` 仅表示数据采集完成。没有把原生位置、速度或地面判断写回 Godot 角色。

## 切向滑动修正

本机 UE 5.8 的 `CharacterMovementComponent.cpp:3649` 保留沿阻挡面的切向滑动；Godot 随附 API 的 WallMinSlideAngle 默认是十五度，并受 Grounded/FloorBlockOnWall 控制。共同移动服务将该角度设为零。

真实六角色物理测试新增一个约 1.43° 偏离墙面法线的接近方向。稳定撞墙阶段直接检查每帧实际切向位移为正，并记录与请求切向位移的残差、最小保留比例。测试专用 `--character-motor-default-wall-angle` 恢复旧默认，证明断言会捕获完全抑制：60 Hz 预期约 `0.00090530544 m`，实际为零，进程退出 1。

首版切向测试额外要求实际切向位移与请求完全符合 `2e-6 m` 门槛，120 Hz 在第145帧失败：请求 `0.0009844066 m`、实际 `0.0009784698 m`，同帧有约 `-0.0009623766 m` 竖直地面吸附。该断言把接触/吸附后的完整位移等价混入了抑制检查。修正后，抑制回归只检查实际切向保留，数值残差继续完整报告；上面的原生完整轨迹门槛没有放宽，残差没有标为已修。

首个新增测试还有局部变量同名编译失败；编译确认前误启动的一轮验证已终止，不纳入最终结果。编译失败、误启动、120 Hz 精度失败和默认角度负例日志均保留。

## 已定位的剩余差异

最终场景代码的 Debug 与实际 ExportRelease Optimize 同输入报告逐项相同，测量如下。三个频率各有一帧地面标志不同，蹲伏标志全同、加速度差为零。报告保存全部失败帧。

| Hz | 比较帧 / 未通过帧 | 最大位置差 cm | 最大平面差 cm | 最大竖直差 cm | 最大速度差 cm/s |
|---|---:|---:|---:|---:|---:|
| 30 | 240 / 240 | 12.76878131 | 0.70840248 | 12.76872177 | 480.00064607 |
| 60 | 480 / 480 | 5.84227254 | 0.62882778 | 5.84186680 | 496.33494883 |
| 120 | 960 / 960 | 2.15282791 | 0.17890929 | 2.14787577 | 496.33471313 |

60 Hz 首次实际比较的480帧全部未满足完整门槛，蹲伏标志全同、加速度差为零，地面标志一帧不同。最大位置差约5.84675 cm、竖直差5.84187 cm、平面差0.66909 cm，最大速度差约496.335 cm/s发生在落地帧。角度修正后的独立60 Hz诊断首次碰墙切向位移更接近原生，完整比较仍失败。

源代码与实际记录揭示了后续边界：

- 原 `MIN_FLOOR_DIST=1.9f`、`MAX_FLOOR_DIST=2.4f`，`AdjustFloorHeight` 只有超界才移动到平均间隙2.15 cm。原起点中心92 cm保持；当前 Godot 初始化 FloorSnap 落到约90.04688 cm。需要基于真实胶囊地面查询实现原间隙与净空处理。
- 原 `PhysFalling` 初始运动使用中点位移；命中后以 `Velocity * timeTick` 重新构造剩余滑动，另有受限 AirControl 和多接触处理。当前一次 MoveAndSlide 将同一个中点运动交给 Jolt 完成剩余滑动，这一分支尚未对齐。
- 原 PrimitiveComponent 的 `PullBackHit` 回退碰撞时间，公式为 `Clamp(0.1f,0.1f/Dist,1.f/Dist)+0.001f`。Jolt 的 SafeMargin 恢复不能代替该规则。首次空中撞墙的原生与实际行进比例不同仍须实现。
- 原跳跃顶点分步将速度显式设为零并退款剩余时间，落地后剩余时间进入 Walking。当前没有这条完整外层调度；不能用最终位置偏移补偿或重放地面标志代替。

下一步应在同一角色物理事务内落实真实扫掠、原地面间隙、命中时间与剩余时间积分，再跑同一输入轨迹，随后扩大斜坡、台阶、双墙与移动平台。Main/Linked 共同提交、通知与 RootMotion/Warp 的凭据协议需要同时保留。通用多Group/default/self/Unlink、近景握持和其余路线仍开放，整体目标继续 active。

## 最终回归与审计

证据标签为 `cmc-trajectory-v1-wall-contacts-final`。Debug 与实际 ExportRelease Optimize 各九项场景回归通过：三频六角色物理、三频普通十角色，以及60 Hz的RootMotion/Warp/Emote。18个进程退出0且没有Godot ERROR/WARNING；每种构建30480次发布移动，物理和普通完整报告在两构建中逐项相同。

斜向撞墙门禁每种构建在30/60/120 Hz分别覆盖18/36/72帧。最大切向残差约0.000936/0.000927/0.006836 mm，完整数值保留。新程序集中的默认角度负例退出1、实际切向位移为零；负例的预期错误日志独立保留。

两个构建另各跑三项完整轨迹比较，共3360次实际移动和3360次动画取消重试。六个进程均正常保存全部比较数据、无Godot ERROR/WARNING，但因不满足上表门槛退出1。比较脚本也退出1，`comparisonPassed=false`。这六项没有计入场景回归通过数量。

最终构建均0错误0警告，两个Optimize脚本结束后分别逐文件恢复六个Debug文件。独立审计 `tools/verify_lyra_character_trajectory.py` 通过，输出 `artifacts/lyra-analysis/character-trajectory-v1-integrity.json`，核验参考来源、全部逐帧比较结果、四份矩阵摘要、源码及两种程序集。首次审计因负例错误消息新增frame字段而字符串匹配失败，修正匹配后重新通过，失败日志保留。

旧866 JSON、709原UE包、9项目配置及3份原角色源码逐文件哈希保持；V2配置和425组原生采集、夹具保持。原速度/空中内核及其测试等八份已有源文件与上一审计精确相同，本批没有另跑managed测试。旧审计的程序集哈希属于历史，新审计验证当前DLL。最终记录仍为 `auditPassed=true / comparisonPassed=false / nativeWorldTrajectoryParity=false / goalComplete=false`。

## 重跑

当前主目录已具备紧凑参考和本地 ignored 资源。重新运行须选择新的证据标签，并先成功构建相应程序集：

```powershell
./scripts/verify-lyra-character-motor.ps1 -Configuration Debug -EvidenceTag local-trajectory-check
./scripts/compare-lyra-character-trajectory.ps1 -Configuration Debug -EvidenceTag local-trajectory-check
./scripts/verify-lyra-character-motor.ps1 -Configuration Optimize -EvidenceTag local-trajectory-check
./scripts/compare-lyra-character-trajectory.ps1 -Configuration Optimize -EvidenceTag local-trajectory-check
```

Optimize 使用实际 ExportRelease 程序集，结束后恢复六个 Debug 文件并逐个验证哈希。比较脚本返回非零时，读取其完整诊断和逐帧报告；仅代码的检出仍不能运行这些场景。
