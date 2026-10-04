# Lyra Main Pivot 状态根、完整 Provider 与 Lean

2026-10-01，在主目录继续原 Lyra → Godot 移植。使用安装版 UE5.8.1/GASP58 原类与资产、Godot4.7.2 .NET、ALS68 skin/81 logical。本批关闭实际 Main Pivot 根及完整子图的独立 Update/Sync/Evaluate 对照；四根共用 owner、整个 LocomotionSM、普通 Demo 与整链目标仍开放。

## 实现

`LyraMainPivotHost` 一次执行现有完整 Main 更新，然后按原 StateResult20 的 SetUpPivotState/UpdatePivotState 顺序锁存 PivotInitialDirection、递减 LastPivotTime。仅正值递减，不钳制负值；pose-link Initialize 不清除 Main NodeRelevancy 历史。之后 Linked21 的原完整 Pivot Provider 执行，源回调可回写同一候选 LastPivotTime；最后通过 ApplyAdditive23 合成实际 Lean22。采用 Main 的 AccelerationDirection、LocalVelocity/Acceleration、Displacement/Speed、WithOffset方向和组件变换，物理 Pivot 预测来自实际 movement 快照。

沿用原16节点 Provider、真实 PivotSM 与双 evaluator、两套独立 Orientation/Stride 历史及 Warp 后外层 HipFire。Pivot 源与 Lean 统一登记、仅一次共同 Sync，再采样与求值。Main、根回调、Provider、Warp、Lean、源历史统一预校验与提交，失败或取消不发布。保留原机器0.4f/FastFeet/HermiteCubic、源换序列0.2与 Linked 首次0.15请求及其发生顺序。

极小权重首次访问 PivotB 可以不触发 BecomeRelevant，序列仍为 None。原回调仍执行，但 evaluator 不登记 tick，Evaluate 输出参考姿态、无曲线/整数属性/RootMotion。新运行时保留该分支及其时钟/Marker，没有虚构序列或同步结果。本输入中三种装备各出现一帧，共3帧；Lean 仍按真实节点执行。

## 原生采集与结果

外部插件 `ReadMainPivotTrace` 创建临时 GamePreview Character/Movement、Main、同一个实际 Linked Provider 和 transient ALS81 序列。验证实际 StateResult20→ApplyAdditive23→Linked21/Lean22 链接及原回调；根探针只转发原节点并记录回调后观察，退出前恢复原链接。原 Main 宏、源回调/机器/Sync/采样/Warp/混合由 UE 执行。状态根遍历、活跃及权重仍为显式状态机观察，不代表整个 Main 状态机驱动。

Unarmed/Pistol/Rifle ×30/60/120Hz各6秒，3780帧/3528活跃输出/252隐藏帧。逐帧对照 Warp 后机器和 Lean 后根输出，共7056姿态/571536骨。位置最大 `1.1435103132435445e-13 cm`、最终 quaternion 最大 `9.586729210015212e-16`、scale差0；门槛仍为 `1e-8 cm /1e-10 /1e-12`。曲线值/flags与存在性、28200项整数属性及 RootMotion 身份/TRS通过。三帧无源分支按原生缺失属性对照，其余3525帧保留 RootMotion。42条压缩根的378个 probe 精确相等。

Main 标量/方向锁存/计时、机器/源/Marker/引脚和 Lean 时钟/样本逐位对照。Main 派生向量沿用现有 `1e-10` 分量门槛，最大 `8.526512829121202e-14 cm`。Lean3780次时钟/7722样本检查通过。36个 Pivot 资产、60转移/3首帧转移/81自动重入/225 Setup、216根相关性锁存/2211方向保持、1443负timer/1179负值保持、219惯性请求均覆盖。85860次错误候选/Sync/求值/提交拒绝；每帧 Prepare与晚期求值取消重试、重复 Evaluate 和全历史不发布通过。

## 失败证据与资源边界

初次导出因极小权重访问无序列/无tick被错误拒绝；诊断后按原 evaluator None 行为修复，保留 `main-pivot-ue-{empty-first,missing-tick,null-sequence}.log`。首次 Godot 对照误将 Main 派生加速度向量套入标量逐位比较，改为共用已有 Main 向量比较器；未修改标量或姿态门槛。对应构建遗漏一个新增参数也已修复，保留编译失败日志。

之后60Hz连续换源的 Marker 不同，实际原因是 UE controller 创建 transient 序列时重排了同一时刻的左右脚 Marker。旧 metadata 中同一时刻 R/L，实际参与 Sync 的副本为 L/R；旧输入轨迹未触发该持续历史差异。新 exporter 在采集前后读取并核验真实序列的 Marker 顺序，不再沿用旧资产清单。旧619份JSON不改；首版新 native 以原字节归档到 `artifacts/lyra-analysis/main-pivot-native-stale-marker-order.json`（SHA256 `28dfed0987e78be0f4bfc1f6a61b5f736d5b85c23309c89473cbe9accbc0c13b`）。该修订属于捕获合同纠错，不调整原资源或运行时来匹配错误清单。

一次诊断采集与外部插件打包重叠导致模块不可加载，保留 `main-pivot-ue-plugin-build-overlap-failed.log`；等待构建正常退出后重跑成功。临时诊断输出已从源码移除，最终源码与外部 package/source逐字节一致。

两次正确 native 采集正常退出0，重复结果一致、保持新资源已有字节；508个UE包与619份之前JSON字节哈希全部相同，无资产保存。两次UE日志各1495条Warning行（含汇总）：909条Footstep GameplayTag、572条transient ConditionalPostLoad、3条合法空序列及11条既有编辑器提示；不是零警告采集。无引擎或 GASP 项目配置/源码修改。

## 验证与仍开放的范围

- Core预测/Sync/距离匹配/evaluator/Warp/根提取：87通过/0失败/0跳过。
- 最终旧完整Pivot Provider、双源及原机器两套7560帧通过。
- 最终 Main Start/Cycle Lean与Start/Cycle/Stop共同历史3780帧/9105姿态通过。
- ALS资源/接口复跑：234源936采样、14入口8重绑、245源197绑定missing0通过。
- Debug与ExportRelease Optimize：0错误/0警告；最终Godot日志无ERROR/WARNING。
- 外部UE插件完整构建、导出脚本解析与保护哈希通过。

本批 Main Pivot 宿主仍是独立组件。不能在四根共同图中为每个根各运行一次 Main；下一步须将 Pivot 的实际节点访问、锁存和timer回写纳入现有单 Main/单 Lean/单 Sync 的 `LyraMainSourceScope`。完整 LocomotionSM 选边/状态权重/最终混合、Idle/Air、统一 Notify/Montage、最终足部、生产 Gather/Worker/Commit、普通 Demo、多角色/并行、渲染观感、打包和十分钟性能均未据此验收。完整 Lyra 目标继续开放。

新增 ignored 资源：`assets/generated/lyra_als/main_pivot_{requests,native}.json`；沿用原 `pivot_runtime_{distance,roots}.json`，不改其哈希依赖。仅代码检出不能运行。完整证据汇总：`artifacts/lyra-analysis/main-pivot-final-verification.json`。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-pivot.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_pivot_smoke.tscn
python tools/verify_lyra_main_pivot.py
```

验证器依赖本批命名的构建/回归日志与TRX，不以 marker 成功行代替全部门禁。
