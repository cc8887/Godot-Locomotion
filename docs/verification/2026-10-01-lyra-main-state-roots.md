# Lyra 实际 Start/Cycle/Stop 状态根

2026-10-01，在主目录 `.` 继续实现。UE 5.8.1 / GASP58 原资产只读，Godot 4.7.2 .NET；保持 ALS 原68 skin与81 logical。上批共同宿主的 Start/Cycle入口止于 ApplyAdditive13/17，本批补实际 StateResult10/14，与原 Stop StateResult18共同更新、Sync和求值。仅关闭这三个状态根组件，完整 LocomotionSM与生产入口仍开放。

## 实际绑定与共享模式

新 `ReadMainStateRootsTrace` 在原注册 Character/Movement/Main/Linked 实例上运行完整 Main更新后，遍历实际 StateResult10→ApplyAdditive13→Linked11/Lean12、StateResult14→ApplyAdditive17→Linked15/Lean16、StateResult18→Linked19。没有另起 Main、Linked或Sync；继承上批114份原压缩根及原 RAW姿态、两条Warp、HipFire、Lean和独立Stop组。

只读 folded-data绑定证明：Start状态1的 Update函数是原 `UpdateStartState`，Cycle状态2没有 Update函数，Stop状态3使用原 `UpdateStopState`；三根的 entry/exit函数均为None。这些函数名及子链接由C++检查并导出，不根据目录缺少某个DSL文件推断Cycle行为。

本机 `AnimationStateMachineLibrary.cpp` 的 `IsStateBlendingOut` 从更新上下文找到所属机器，读取该状态的上一权重；条件是 `previousWeight > 0 && currentState != stateIndex`。Godot保留这个原条件：Start未混出写Hold，Stop未混出写Accumulate，Cycle保持当前共享值。写入发生在状态根对子图的更新之前。每根的modeBefore/modeAfter与原生真实回调逐项对照，最后的值与Main/source历史一并提交，下一帧完整Main更新实际消费它。

新增 `LyraMainStateRootContext` 保存同一机器的current与上一Start/Cycle/Stop权重；与原Stop观察不一致、缺失、越界和非有限值在准备任何历史之前拒绝。启用状态根的scope要求该观察；旧组件夹具维持各自已验证的入口，用于回归。取消和重试不会发布候选模式，求值后取消重试也逐帧验证。

## 观察边界

当前机器current、上一权重、root order/weight/active/reset仍是显式外部观察，本批轨迹覆盖current1/2/3。UE在两份实际状态权重buffer中登记这些观察，再通过真正StateResult链接执行回调。没有调用完整状态机的选边/权重生成、状态进入或退出生命周期、最终状态混合。

因此本批验收的是实际状态根与共享Main反馈。状态索引来自原12状态机器，但不表示12状态全部执行。当前活跃scope仍要求Evaluate后Commit；普通ALS Demo及独立Lyra Demo没有在本批改入口或重跑。

## 验证

| 检查 | 结果 |
|---|---|
| Provider/频率 | Unarmed/Pistol/Rifle ×30/60/120Hz，共3780物理帧 |
| 实际状态根 | Start3150、Cycle3150、Stop2805；9105次逐根更新/姿态，737505骨 |
| 交叠与隐藏 | 2079三根同活跃、63 Stop单独、126全部隐藏；六种顺序与独立reset |
| 模式覆盖 | 423次Hold→Accumulate、210次Accumulate→Hold；最终Hold1488帧，下一帧Main消费Hold1488帧、Accumulate1197帧 |
| Start混出条件 | 1239活跃帧有上一权重且当前状态不同，864活跃帧上一Start权重为零 |
| Main/源 | 已比较Main标量/spring/flags、逐根模式、源/HipFire/Lean时钟及marker/delta逐位一致；Main向量最大1.3642420526593924e-12cm |
| 最终根姿态 | Start位置2.299679001209009e-13cm / quaternion1.7886740406278828e-15；Cycle2.109680835400689e-13cm / 1.7111937024914371e-15；Stop9.099386826154683e-14cm / 8.280938314956994e-16；scale0 |
| Root/data | 9000 present / 105 absent RootMotion，TRS差0；36420整数属性和6228曲线值，原typed身份/存在性保持 |
| Lean/惯性 | 7560 occurrence clock checks、14709 sample checks逐位同；108惯性请求的顺序/候选生命周期一致，未验收外层惯性姿态 |
| 事务门禁 | 24537坏操作拒绝、9个误合并Stop组绑定拒绝；45个无效状态观察拒绝；每帧准备取消/求值后取消再重试 |
| 静态资源 | 114实际压缩根的1026个probe精确同；492个UE包SHA保持，25份既有fixture字节保持 |
| 回归/构建 | 旧共同三根3780帧/9105姿态通过；Core相关70通过0失败0跳过；Debug与ExportRelease Optimize均0警告0错误 |

姿态门槛保持原 `1e-8 cm / 1e-10 quaternion / 1e-12 scale`。运行实现使用独立原资源与算法，不读取连续oracle作为输出。两个独立UE进程复导数据语义相同，immutable save保留第一次的文件原字节；均退出0，summary为0 errors / 2202 warnings，无Python Error/ensure/assert。Godot联合与回归退出0，最终无ERROR/WARNING。原UE压缩/GameplayTag等warning未修，不称UE无警告。

首次C++构建因局部RootIndex遮蔽和私有GetUpdateFunction访问失败，改不同局部名及项目现有只读folded-data成员指针路径后成功，无UE源码修改。首次Godot停在静态哈希门禁：新测试已选新root文件，但仍校验旧请求文件。修正测试资源引用后通过，没有改变资源、算法或门槛；保留两轮失败日志。上一批短资源切换/物理零delta的Notify ensure诊断仍未关闭。

## 证据与复跑

证据前缀 `artifacts/lyra-analysis/main-state-roots-*`；最终汇总 `main-state-roots-final-verification.json`包含资源/日志哈希。新增三个JSON在ignored `assets/generated/lyra_als/main_state_roots_*.json`，已有JSON没有重排或格式化。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-state-roots.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_main_state_roots.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_state_roots_smoke.tscn
```

## 后续

继续补Idle/Pivot/Air实际source与状态根库存，接真实LocomotionSM选边、遍历权重与最终状态合成；再统一Notify/Montage、外层惯性、最终FootPlacement/LegIK、Godot live gather和生产入口。完整视觉、人工矩阵、多角色/并行、打包与性能仍需对应范围验收。本批没有UE包保存、引擎修改或生产Demo/render；ALS R2–R7、用户修改与暂缓项保留。
