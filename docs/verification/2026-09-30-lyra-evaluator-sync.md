# Lyra SequenceEvaluator 与非循环 Marker 原生对照

在主目录 `.` 实施，源为 GASP58、本机 UE 5.8.1，运行端 Godot 4.7.2 .NET。本批完成真实 SequenceEvaluator 更新到共享 Sync 的组件对照，覆盖 Lyra Start/Pivot 所需的非循环 Marker 边界。完整 Layer 源宿主、节点回调、真实主图连续执行仍未接入。

## 实现与实际原生调用

- `AlsSequenceEvaluatorPreparation` 按本机原节点执行 explicit time 限幅、三种重初始化、循环最短时间差和播放率计算。Teleport 在具名组或 Graph 方法下仍推进；独立 teleport 登记零播放率 tick。调用方持有 occurrence 时钟，本组件不建立第二套时钟。
- `AlsAssetSyncRuntime` 支持 `AlwaysLeader` 的固定分数 2，保留原生 score-only 不稳定同分排序；Sequence evaluator 不执行普通播放器的惯性重入比例重定位。此前历史分数门禁也已修正，以容纳强制选主及已有合法大于 1 的权重。
- 非循环 Sequence 保留 `-1` 起止边界、正反播放、边界限幅、严格时间比较、按原生条件保持 follower 时间和选主回退。原生 Marker position 的 `IsValid` 要求前后两个名称都非 None；单侧边界可编码位置，但不能结束组内选主搜索。
- Source occurrence 可以传入持久 `MarkerRecord`。Native Reset 仅清除索引，保留距离存储；取消、隐藏、重初始化后不能把这些存储全部归零。输入存储越界或非有限值拒绝整批，结果仍为候选。
- `LyraEvaluatorSourceTick` 读取已导出的编译节点方法、组、角色、循环及 evaluator 设置，输出实际送入 Sync 的 tick record。Graph 使用外层 scope role。原生轨迹在配置受控 overrides 后直接消费此 adapter 的 record，不重新拼装 evaluator record。

外部 exporter 的 `ReadEvaluatorSyncTrace` 使用两个实际 `FAnimNode_SequenceEvaluator_Standalone` 和一个实际 Sequence tick，真实 `FAnimSyncGroupScope` / 可选惯性 scope、`Update_AnyThread`、正常 `FAnimInstanceProxy::UpdateAnimation`、`PostUpdate` 和 `FAnimSync`。读取节点内部累计时间、实际 tick、原生组和 Marker 输出。没有运行原编译 Linked 图或模拟其节点回调。

`FMarkerPair` 构造只初始化索引；为使受控边界 probe 可复现，采集宿主显式将首次未使用的距离存储设为零，此后 Reset 和所有更新完全交由原节点。未把未初始化内存当作原生语义。无效 `-2` 距离输出为零；有效记录和边界的距离保留实际 binary32。

## 连续范围与严格比较

两组各 27 条轨迹，30/60/120Hz，每条 2.5 秒。九种输入包括 teleport、独立推进、强制选主、同分、角色换主、惯性重入、Graph scope、Marker leader 和极小 delta；含负/超长 explicit time、零/终点、零 delta、隐藏空帧及三种重初始化。

第一组使用实际 FallLand、Turn 和 Jog/Walk Cycle，覆盖无 Marker 的非循环与有 Marker 的循环。第二组使用实际 Jog/Walk Start/Pivot，所有源非循环，四条资源都进入轨迹。

Godot 从自己的已提交状态连续计算下一帧，不从 oracle 回填时钟或 Marker。比较时钟、prepared time/rate、DeltaPrevious/Delta、leader 身份/排序索引/分数、比例、Marker 索引/距离和组位置；有编码位置的单侧边界 alpha 也比较。每帧以同一已提交输入重试，整个候选逐值相同。

JSON 增加 `*Bits` 字段保留原生 float 位，解决普通 JSON 丢失负零的问题；所有上述数值逐位比较，没有提高阈值或删掉失败帧。

| 轨迹 | 帧 | 源 tick | 空帧 | 分数 2 的最终 leader 帧 |
|---|---:|---:|---:|---:|
| 循环/无 Marker | 4,725 | 13,608 | 189 | 2,347 |
| 非循环 Start/Pivot | 4,725 | 13,608 | 189 | 1,222 |

两组共 54 条、9,450 帧、27,216 tick。非循环 oracle 有 5,327 条带边界的输出；资源核验检查 261,576 个原生 float 位字段与数字值一致。两组均在最终 exporter 的第二个完整 UE 进程重导后结果语义完全相同，已有文件字节保留。

## 最终验证与失败留档

- Core Sync/独立源相关 64 项通过；进一步覆盖 source 初始化及 Asset Notify 消费的相关 138 项通过。新增坏 occurrence 存储原子拒绝与 2,000 次非循环正反边界 tick 零分配检查。
- 原 ALS Standing 三频率、独立/共享六组 native 测试全部通过，原阈值不变。
- 当前独立 Lyra Demo 60Hz 870 物理帧、六次换 Layer/六次换 Cycle、871 次完整逻辑姿态、左右手各 780 次有效求解通过。这是现有生产链回归，尚未证明新 evaluator adapter 已接普通 Demo。
- Debug、Release Optimize 构建均 0 错误/0 警告；最终两个 Godot Sync smoke 和 Demo 日志无 ERROR/WARNING。
- `verify_lyra_evaluator_sync.py` 独立核对 489 个包、11 个依赖、234 个 clip（233 个唯一源路径）、两个 request/native 哈希与完整输入身份。UE 无资产保存、项目配置或引擎源修改；新增 C++ 位于外部 exporter。

保留的关键失败包括：旧同分排序预期、普通 JSON 负零、deprecated Proxy tick wrapper 的二次 buffer flip、非循环首轮错误选主及重置距离归零。分别见 `evaluator-sync-core-tie-first.trx`、`evaluator-sync-godot-zero-first.log`、`evaluator-sync-ue-doubleflip-first.log`、`evaluator-nonloop-godot-first.log`、`evaluator-nonloop-godot-validity.log`。最后两项按本机源码修复，未调整 oracle 或严格比较。

最终日志为 `evaluator-sync-godot-final.log`、`evaluator-nonloop-godot-storage.log`、`evaluator-sync-asset-final.log`、`evaluator-sync-standing-native.log`、`evaluator-sync-demo-60.log`；资源检查为 `evaluator-sync-verification.json`。汇总在 `artifacts/lyra-analysis/evaluator-sync-final-verification.json`。

## 不可变资源与复跑

以下新增文件在 ignored `assets/generated/lyra_als/`。旧无位编码的 `evaluator_sync_native.json` 保留为历史证据，没有覆盖或格式化；仅检出代码无法复跑这些 smoke。

| 文件 | SHA-256 |
|---|---|
| evaluator_sync_requests.json | `44ed7b96cc406cfbe2cf6e456ae9dff8f5e6b00844454c4ca6266923d096fa10` |
| evaluator_sync_native_bits.json | `47d8ab358ffc86b21a38aece980fe129c546edf7b0fc141881a625f667b0ac9e` |
| evaluator_nonloop_requests.json | `69bbac40673c3e4602d108df105f422a153ad2fbe21ac8d157dad1a5831a562e` |
| evaluator_nonloop_native_bits.json | `beb86ef6f971d6a18d1cc78bd00dee0f7e01d881fd467a88fbe905bfb8382ae6` |

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-evaluator-sync.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-evaluator-sync.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -NonLoop
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_evaluator_sync_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_evaluator_sync_smoke.tscn -- --lyra-evaluator-nonloop-smoke
python tools/verify_lyra_evaluator_sync.py
```

## 后续边界

尚未完成真实 Layer 遍历处的源登记、252 个动态默认资产节点的实际绑定/节点回调、原 SequencePlayer 的完整更新入口、完整父子图共同 Source scope、真实 Notify/Root Motion 消费、跨资源切换和所有 presence/属性混合。三个 Main Lean 样本仍未补齐。

当前 marked group 仍要求相同完整 Marker 名集合；非循环 marked BlendSpace、Transition/Exclusive 角色、镜像与 phase-matching 仍不支持。本批已覆盖导出库存实际出现的三种 role，不能将其推广成通用 UE 动画运行时验收。

下一步将这些组件接入真实编译 source occurrence 和共同 owner 的状态历史，再推进原子 Update/Sync/Evaluate/Commit 与原 Layer 图、主图的连续 native 对照。完整 Lyra 支线及 ALS R2–R7 保持开放，无新渲染/人工完整矩阵、十分钟或性能验收。
