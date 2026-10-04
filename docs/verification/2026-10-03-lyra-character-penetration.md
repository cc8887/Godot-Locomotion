# Lyra 穿透恢复与组合MTD

本批沿 [原空中顺序](2026-10-03-lyra-character-air.md) 继续实现原CMC的SafeMove穿透恢复。ALS人物68 skin / 69 raw / 81 logical及十四入口的ItemAnimLayers实例保持现有生产实现。本批增加本地恢复配置资源，没有重新导出人物或动画；完整运动等价与通用Layer目标仍开放。

## 原生配置与参考

只扩展自有LyraWholeMainOracle探针，读取原Shooter实例的恢复上限与运行中的CVar。原角色源码、引擎源码及原资产未修改。`cmc60-penetration-v1`正常退出0，三个Provider的配置一致，原425速度内核、144地面、32扫掠、96空中与各60物理前缀保持。

| 原参数 | 原值cm |
| --- | ---: |
| MaxDepenetrationWithGeometry | 500 |
| MaxDepenetrationWithGeometryAsProxy | 100 |
| MaxDepenetrationWithPawn | 100 |
| MaxDepenetrationWithPawnAsProxy | 2 |
| p.PenetrationPullbackDistance | 0.125 |
| p.PenetrationOverlapCheckInflation | 0.10000000149011612 |

实际p.InitialOverlapTolerance=0，p.MoveIgnoreFirstBlockingOverlap=0，角色ROLE_Authority=3。运行资源 `assets/generated/lyra_als/character_penetration_v1.json`，SHA256 `6DBC173E4FD76446F5520BAE91D2420DA238FDF3034AC9EC298F4BBE307CDD44`，引用原motor文件、原生采集和closure字节哈希。导出拒绝覆盖，不能只更新代码而遗漏这份被忽略的资源。

随后`cmc60-penetration-v2`增加初始墙面穿透与双墙穿透，两个半高90/65cm与30/60/120Hz/100毫秒，共新增16项。三个Provider的112组结果完全相同，过滤两种新case后原96组逐项相同；原425/144/32与各60物理前缀也保持。V2正常退出0，原UE Condition/PostLoad警告保留。

新参考 `artifacts/lyra-analysis/character-air-v3-reference.json`，SHA256 `033DDC09521EBB722935CAC4A4549172F917168356651D2A0431691811BEA4FC`。V1/V2旧参考与失败记录保留，未重写或放宽门槛。

## 生产执行

LyraCharacterSweep从真实GetRestInfo接触点和法线，以及实际胶囊support，计算穿透深度；没有传入原录制MTD。按原法线×(深度+Pullback)计算调整，并选geometry/pawn上限。先对调整后的膨胀胶囊实际IntersectShape；无重叠时按原无扫掠Teleport执行。否则依次尝试调整扫掠、第二MTD组合、调整加原位移、沿退出方向的原位移，再重试原请求。

恢复扫掠按原normal与单位移动方向的点积判定是否允许离开初始重叠。仍向内的接触保持阻挡，查询排除自身。当前Godot接口通过排除对应collider RID处理允许退出的初始接触；单RID含多个shape或凹面时的后续重入还需要更细的接触过滤，**不是完整复杂网格/凹面重叠的原生验收**。当前MTD胶囊support基于现有单位尺度直立角色；缩放/任意倾斜胶囊也需要独立扩展与原生验证。

原SafeMove实际移动成功后置Recovered，Air和Grounded累计本次物理步的teleport标志；地面重建碰撞速度时排除恢复位移。失败恢复不会置标志或重试原移动。恢复仍只发生在物理owner中，动画重试复用已发布凭据。代理上限虽已导出，当前普通角色为Authority，尚未接多人网络角色映射或proxy专项；CharacterBody3D按Pawn分类，其他自定义Pawn分类尚需通用映射。

实际诊断记录每行teleport/swept/combined/adjusted/original次数。112组产生16次无扫掠恢复（原地面8+新墙面8）和8次组合MTD；其他三种恢复分支未在本矩阵实际触发，不声称其已验收。膨胀重叠拒绝和第二MTD组合在双墙case实际发生，恢复后原请求继续推进。

## 严格结果与范围

112组全部通过，包含原96组及新增16组。最大位置差0.000322293762cm，最大速度差0.000166961807cm/s，位置门槛0.01cm、速度0.001cm/s及模式/顶点次数/随机状态精确相同均未改变。20次顶点拆分、28次着陆和6组多接触保持真实记录；随机逃离仍没有实际消耗随机流。旧8组初始地面穿透的0.1151cm位置/1.01355cm/s速度差消除。

完整同输入轨迹仍失败，三频5/26/283帧，合计314/1680；与上一批完整结果相同，Grounded/蹲姿差异0，加速度差0。此次恢复矩阵通过不能关闭普通连续运动中的地面碰撞速度与120Hz位置累积差。旧144地面查询3项失败、32扫掠8项失败继续保留。

最终Debug与实际ExportRelease Optimize均0警告/0错误。两构建各九旧场景、三频地面与三频台阶，共30个最终玩法进程退出0；两套112项专项也退出0。另两套地面、两套扫掠和六条完整轨迹按原失败门槛退出1。最终42个玩法/诊断进程无Godot ERROR/WARNING；三频物理/十角色/地面/台阶、Warp/Emote及查询/完整轨迹报告在两构建逐项相同。

`character-penetration-v2-integrity.json` 独立审计通过，重算112组位置/速度/模式/顶点/随机状态、16/8恢复次数及完整轨迹门槛；校验五轮六个Debug DLL/PDB恢复、869既有JSON、709原包、9配置及3原角色源码保护，自有探针源码与实际V2构建包字节相同。运行新资源与原CDO的三Provider逐项一致，原96/425/144/32与60帧前缀保持。首次96通过和首次112通过记录均保留，最终只计`cmc-penetration-v2-final`的两构建矩阵。

复现资源采集：

```powershell
./scripts/build-lyra-whole-main-oracle.ps1 -PackageName package-cmc-penetration-v2
./scripts/capture-lyra-whole-main.ps1 -RunTag cmc60-penetration-v2 -PackageName package-cmc-penetration-v2 -Hz 60 -Case physics -FrameLimit 60
python tools/export_lyra_character_penetration.py --native-tag cmc60-penetration-v2
```

命令用于已准备前序动画资源与物理参考的首次生成；当前工作目录已有上述包、捕获与资源，应按哈希复用，所有同名证据拒绝覆盖。当前源码含112项，导出器既接受历史V1的96项，也会过滤V2追加case后验证原96项保持。新参考由V2包和 `tools/export_lyra_character_air_v3.py`生成，完整验证使用证据标签 `cmc-penetration-v2-final`。

尚需完成MTD上限/代理/自定义Pawn、复杂多shape/凹面过滤、缩放胶囊与剩余恢复分支、完整Walking ledge/模式退回/移动基座、Root/RMS空中专项，以及通用多Group/default/self/Unlink、Shotgun/Feminine全图、近景握持、独立导出和性能。没有本批GPU、全量managed、十分钟或人工全矩阵验收；音频、道具物理与头颈暂缓。
