# Lyra 原空中移动顺序接入

本批将默认重力的原 `PhysFalling` 移动顺序接入普通玩家与 NPC 的同一物理服务。仍沿用 ALS 人物68 skin / 69 raw / 81 logical、已重定向动画和十四个入口的 `ItemAnimLayers` 共享实例。人物资源与 Interface / Layer 方案见 [复核记录](2026-10-03-lyra-als-interface-review.md)。完整移植、完整物理等价和通用 Layer 仍开放。

## 原生参考与资源保护

只扩展自有 `LyraWholeMainOracle` 探针，以保护成员访问调用本机 UE 5.8 原 `PhysFalling(delta,0)`。命中后执行原 `ProcessLanded` 和原 `PhysWalking` 剩余时间。原引擎与三份 Lyra 角色源码未修改，胶囊、变换、模式、CurrentFloor、移动基座/骨名、强制地面刷新、速度/加速度/analog、蹲姿、bJustTeleported、顶点次数和随机流均保存恢复，临时墙角/斜坡 Actor 销毁。

`cmc60-air-v2` 正常退出0，原 UE Condition/PostLoad 警告保留。三个实际 Provider 各96组结果完全相同；原250地面速度、175空中内核、144地面查询、32地面扫掠和各60帧原物理输入前缀完全相同。运行配置继续使用既有 `character_motor_v2.json` 与 `character_floor_v1.json`，没有更换人物或动画资产。

96组为两种胶囊半高90/65cm × 四种帧长30/60/120Hz及100毫秒 × 十二类输入：自由制动/控制、两种顶点、上升/下降撞墙、带输入/无输入着陆、初始穿透、终端速度、墙角与斜坡下降。控制随机种子12345，实际原 CVar `p.ForceJumpPeakSubstep=1`。本矩阵没有 Root Motion。

V1墙角的高度和斜坡速度方向使部分频率没有发生所需碰撞。V2将墙角起点改为(580,1010,half+30)cm，并使斜坡输入向上坡下降；没有改变原计算或比较门槛。V1资源/日志保留，V2实际接触覆盖另验。

参考 `artifacts/lyra-analysis/character-air-v2-reference.json` 的 SHA256 为 `BB5CD5CEC8EB78526C442BDEC2457AF082D7A2F68259D99E4A3EED91180F64C0`。导出拒绝覆盖并校验三 Provider 和旧前缀。诊断只用 authored 位置、初速、输入、胶囊与场景几何驱动物理；原 after/output 仅在实际移动后用于比较。

## 生产路径

`LyraCharacterAirMovement` 持有实际 CharacterBody3D、地面查询和胶囊扫掠 owner。每帧先计算一次横向 AirControl/boost，按原 MaxSimulationTimeStep / MaxSimulationIterations 分步，进行 CalcVelocity、重力/终端速度与中点位移。跨顶点按原 float 顶点时间缩短 tick、退回时间与 iteration；NoAirControl 分支保留原缩短前 GravityTime，不能在顶点后重新选择 boost。

命中后保留原 lower hemisphere / edge / walkable landing 判断、边缘地面回查、有限 AirControl、斜坡抑制、第二面调整与剩余时间。着陆先进入 Walking 并调整地面，再按原时间步推进地面速度和移动。空中不会发布局部 landing 查询为 CurrentFloor；仍在 Falling 时保持空地面缓存。

两处专项发现并修正：`GetMaxSpeed` 在 Falling 时始终使用 MaxWalkSpeed，只有 Walking/Navigation按蹲姿选择；100毫秒着陆的剩余时间也必须分步。首轮96组28失败，修正后只余8组初始穿透。失败报告保留。

内部速度使用原厘米/Z-up double；实际 Jolt 查询和组件变换仍经过 Godot float 边界。物理凭据发布 AirSweepApplied、实际 Substeps、ApexSplits、LandingRemaining、接触列表和最终速度。动画 Cancel/retry 复用既有凭据，不再执行内部扫掠、着陆或消耗物理随机流。玩法 Grounded 消费自己的模式凭据；不使用未被本路径更新的 MoveAndSlide `IsOnFloor` 缓存。

Root Montage 的横向速度覆盖和保留重力接入该 owner；已有 Root/Warp/Emote 场景属于回归，**不是本批原生 Root Motion 空中专项**。RMS的顶点时间退回、动态物体目标速度、物理交互力、水/自定义重力/Navigation，以及完整 Walking 失去支撑的模式退回/ledge/base 生命周期仍待。随机逃离分支按原 LCG 实现，但96组没有实际消耗随机流，不能称 virtual ditch 随机逃离已验收。普通Godot owner的默认种子来自实例ID，与UE NAME_None初始化并非相同，控制夹具才显式固定12345。原生顶点次数的跨帧完整生命周期也仍需专项。

100毫秒只在独立同一 owner 的96组诊断中验证；普通场景服务当前仍限制单帧不超过0.05秒，不称其已支持普通玩法100毫秒帧。

## 严格结果

最终96组有88组在位置0.01cm、速度0.001cm/s、Grounded/顶点次数/随机状态精确相同的门槛下通过，16次顶点拆分、28次着陆、6组多接触。8组失败全部是初始地面穿透：最大位置差0.115105876cm，最大速度差1.013552386cm/s。完整结果返回1、comparisonPassed=false。Jolt BodyTestMotion recovery 仍未等价于原 CMC MTD / bJustTeleported 生命周期；没有替换为录制恢复量或放宽门槛。

连续实际轨迹只接受 authored 控制，比较实际组件位置/Body.Velocity/观察加速度；没有用内部精确速度替换这些观察。原位置0.01cm、速度/加速度0.001、Grounded/蹲姿一致门槛保留。

| 频率 | 帧数 | 旧地面批失败帧 | 本批失败帧 | 最大位置差cm | 最大速度差cm/s |
| --- | ---: | ---: | ---: | ---: | ---: |
| 30Hz | 240 | 120 | 5 | 0.000695665481 | 0.002666262395 |
| 60Hz | 480 | 240 | 26 | 0.002464999534 | 0.003784863380 |
| 120Hz | 960 | 481 | 283 | 0.010591867699 | 0.228917797925 |

三频Grounded与蹲姿差异均0，加速度差0。30/60Hz全位置均在门槛内，但地面碰撞后的速度仍超门槛；120Hz仍有累积位置与速度差。其第479帧先在地面撞墙出现0.01887168cm/s速度差，下一帧虽已进入新空中路径，切向速度仍继承17.04597324对原17.05093278cm/s的差异；184帧速度和160帧位置超门槛，取并集283帧。合计314/1680帧失败，**完整同输入运动未通过**。首轮30/60Hz结果及修正前的96组28失败报告保留。旧144地面查询仍3失败，32地面扫掠仍8失败。

## 验证与后续

证据标签 `cmc-air-v2-final2`，相关脚本为 verify-lyra-character-{motor,floor,ground,air}.ps1 与 compare-lyra-character-trajectory.ps1。优化运行将实际 ExportRelease 六个 DLL/PDB装入Godot的Debug加载目录，finally恢复原字节；不能只根据优化编译成功判断优化运行通过。

最终Debug与实际Optimize构建均0警告/0错误；每构建九项原场景、三频八角色地面和三频五角色台阶，累计30个最终玩法进程退出0。另两套96项空中、两套144项地面、两套32项扫掠与六条连续轨迹正常完成，均按原门槛返回1。最终42个进程无Godot ERROR/WARNING。三频物理/十角色/地面/台阶、Warp/Emote以及全部查询和连续轨迹的完整报告在两构建逐项相同。

最终 `character-air-v2-integrity.json` 与 `character-air-v2-integrity.log` 独立审计通过：重算96组位置/速度/模式/顶点/随机状态和三频轨迹门槛，检查记录哈希、实际程序集、五轮六个Debug DLL/PDB恢复，保护868旧JSON、709原资产包、9配置及3原Lyra角色源码；原425/144/32与60物理前缀保持。旧地面和扫掠查询除新证据tag外，与前批完整结果相同。自有探针源码与实际构建包的源码字节一致，GASP58暂存插件已移出。

主要证据为 `character-{motor,floor,ground,air,trajectory}-{debug,optimize}-cmc-air-v2-final2-verification.json`；所有初版、V1及修正前报告保留。历史 `cmc-air-v2-final` 九项Debug回归也通过，但最终验收只计本段final2程序集的30个玩法进程。

新Core内核提取后的既有 `AlsCharacterVelocityTests` 两项通过，包含250+175原生值的原精度门禁；未放宽阈值。没有本批新人物/动画导出、GPU图像、全量managed、十分钟或人工全矩阵验收。

下一步应处理原 MTD/穿透恢复和地面碰撞速度的实际精度。当前源核查已确认 MovementComponent 的 PenetrationPullbackDistance 为0.125cm，CharacterMovement 再按 geometry/pawn 与 proxy 身份限制最大恢复距离，并通过 bJustTeleported 避免恢复位移参与速度重建；后续需要导出这些原配置并覆盖真实恢复/重试顺序。随后再扩展完整模式切换、移动基座、复杂地形与 Root Motion 空中原生专项。通用 Interface/Layer 的多Group、无组逐调用点实例、default/self/Unlink及 Shotgun/Feminine 完整图、近景握持、材质、独立导出和性能继续开放；音频、道具物理、头颈暂缓。
