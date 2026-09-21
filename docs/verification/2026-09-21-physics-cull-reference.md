# 原生接触检测距离与实际参数

本批延续 `64614c7` 的检测上下文。没有改 Godot 查询或扩大 margin；整链有效结果仍为 `2026-09-21-physics-contact-shock.md` 的 7/12，五项失败未关闭。

## 导出与对照

新增 `PhysicsCullOutput` 导出模式。创建项目配置下的隔离 UWorld，单线程推进一帧后读取实际 solver detector settings。单独的 native midphase 观察类继承 `FParticlePairMidPhase`，直接调用原生 Init/GenerateCollisions，在 GenerateCollisionsImpl 边界记录距离和原生 scale；不复制距离公式生成预期值，不执行窄相或求解。

324 组：30/60/120 Hz × 两端动态或一端 rigid-kinematic 三种状态 × 27/250/1000 cm 三种 body bounds × 四种 PreV × 正常/零 multiplier/零扩展上限三种配置。另一端 5000 cm bounds 用于证明非动态体尺寸不参与 scale。当前 V 故意设为 (0,0,-999)，PreV 独立设置；对照源端不读取 Core 输出。

Import 对照 324 组 scale 和 distance 全部 float 精确相等。实际参数如下：

| 参数 | 实际值 |
| --- | ---: |
| detector.BoundsExpansion | 3 cm |
| detector.BoundsVelocityInflation | 1 |
| detector.MaxVelocityBoundsExpansion | 3 cm |
| detector.bAllowMACD | true |
| CullDistanceReferenceSize CVar（实际存储 inverse size） | 0.0099999997764825821 |
| MinCullDistanceScale | 1 |
| Solver.Collision.CullDistance override | -1（不覆盖） |

这里的对照明确关闭 MACD/CCD；不能将全局 allowMACD=true 解释为每个刚体都启用 MACD。rigid-kinematic 的 PreV 由真实 generic handle 读取。

继续核对 `PBDRigidsEvolutionGBF.cpp:1278` 发现运动目标的重要区别：原生 ApplyKinematicTargets 在更新目标速度后直接将它写入 PreV。因此动态体用上一帧速度，受外部目标驱动的 kinematic 用本帧目标速度，静态体用零。新增 `AlsContactCullDistance.PreVelocity` 明确这一选择并验证移动/停止不滞后一帧；这是源码规则与单元测试，尚不是 native 运动目标逐帧对照。

既有 `v4_physics_inertia_reference.json` 的完整 rig 原生 local bounds 已覆盖 Mannequin 19 身体、AnimMan 21 身体，最大完整边长分别约 55.16/50.59 cm。其尺寸 scale 均为 1；因此非 MACD 下为 3 cm 基础距离加最多 3 cm 速度扩展。不能改用子形状半径、COM 变换后的惯量 extents 或静态地板尺寸。

新参考 `assets/config/v4_physics_cull_reference.json`：155531 bytes，SHA256 `5BECF4FF46401D310FA59E1E617E17C828C923BA31FAC18BF92BCF8680476778`。独立冷启动重导至 artifacts/repeat.json，字节一致。未修改旧参考。

## 构建与验证

- 首轮编译因 FVec3f 到 FVector 缺少显式转换失败；已修正并完成整个 Editor 目标重建，保留首轮日志。
- 成功构建/审计日志前缀：`20260921T095832999Z-4a81637efa864b38bb65d43a4268f340`，位于 UE 项目 `Saved/Logs/PluginBuild`。
- build state fingerprint：`4FBBEF4D1F8296609B2353E40F152668E4589AF56301B930B05007FC058D8CC7`。ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp manifests、DLL、BuildId、receipt 一致。
- 三个 canonical/mirror 文件 SHA256 一致。两次导出退出 0，`ALS_PHYSICS_CULL_OK assets_saved=0`；参考可解析且 324 行对照通过。
- DataValidation 退出 0，0 error/3 既有 warning（旧导航与 PawnActionsComponent 相关），未修改 UE 资产。
- 普通 Editor 重启加载导出类并记录 `ALS_CULL_EDITOR_RESTART_OK`，退出 0；仍有两条既有初始化 `LogAutomationTest: Error: Condition failed`，未宣称修复。
- 固定 JIT、串行 Release 全量：Core 2739 通过（沿用 Golden/TraceSchema 排除规则）；Import 2370 通过、1 项既有跳过。`core.log` / `import.log` 均退出 0。Godot 优化构建通过，0 warning/0 error。

过程产物：`./artifacts/physics-cull-reference-20260921/`。距离 helper 尚未用于 Godot 查询，本批未重跑十二项整链；普通 demo 未接新后端。

下一步用已验证参数和现有 whole-particle bounds 接入分离几何与流形激活/失效；继续以 30 Hz 平台回归和五项整链失败为门槛。盒/凸包首次流形点数/点序也仍欠缺。之后推进普通 Ragdoll owner、pelvis/胶囊/相机、Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算。
