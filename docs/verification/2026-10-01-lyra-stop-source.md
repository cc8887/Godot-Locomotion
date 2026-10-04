# Lyra Stop 源回调、停止预测与原 Sync

2026-10-01，主目录继续实施。使用本机 UE 5.8.1、GASP58 和 Godot 4.7.2 .NET；沿用 ALS 人物与骨架及此前 68 skin / 81 logical 路线。本批只关闭 Stop 的单个 evaluator occurrence。没有把它接入普通玩法、共同 Main 三根或完整 LocomotionSM。

## 当前原图与回调

只读取得九个 Linked provider 的实际编译闭包：FullBody_StopState 的 Root56 → LayeredBoneBlend54，Base 为 Stop evaluator55，Child 为 HipFire evaluator53。118 节点类对应 property61/63/62/64；不能用旧函数元数据中的 root index 猜测真实 pose link。原 UpperBodyMask、mesh rotation、Override curve、按根骨混合 Root Motion、HipFire 先更新的策略有加载门禁。Stop 根没有 Lean、OrientationWarping 或 StrideWarping。Main Stop 为 root18 → Linked19，下一阶段应直接接它；本批尚未执行该完整根。

Stop 是非循环 SequenceEvaluator，SyncGroup `Stop`、CanBeLeader、ExplicitTime 重初始化；HipFire 是独立 DoNotSync evaluator。真实 FPoseLink 通过 NodeRelevancy subsystem 调用原 SetUpStopAnim/UpdateStopAnim，本批只遍历 Stop source。Main 的方向/站蹲/ADS/HasVelocity/HasAcceleration 是显式受控输入，没有改写回调去代替原 Blueprint。

Setup 仅在原相关性恢复时执行，按 Crouch → ADS → Jog 选择四方向资源；使用 Main 的带 offset cardinal。ShouldDistanceMatchStop 为 HasVelocity && !HasAcceleration：不匹配时 Setup 反查 Distance 的零点，匹配时保留显式时间；连续 Update 不重新选资源。Update 在匹配且预测距离 > 0 时反查目标距离，其余情况调用 AdvanceTime，非循环显式时间按片长钳制。原旧 DSL 的 false 分支指向一个未展开的 exec-ref，首轮误读为不推进；原生逐帧结果和专用条件探针证明它也进入 AdvanceTime，已修正实现。

LyraStopSourceRuntime 保留 explicit/internal 两种时间、物理访问/相关性、初始化 pending、缓存权重、Marker 与 Delta。隐藏帧更新事务身份，保留源时钟；取消和重试不发布状态。source 仅注册到外层共同 Sync，组件不建立另一套时间。当前 36 条 ALS Stop 资源全部是 UniformIndexable Distance，原 buffer 时间和值按 float bits 导出；四条资源有小幅下降尾段，保留原值与原二分搜索，没有把曲线修成严格单调。MatchToTarget 使用 -DistanceToTarget，原几乎零差门槛、Lerp 和区间外外推；ExplicitTime 与同步内部时间分别验证。

## 移动组件输入与运算

GetPredictedStopDistance 读取实际 CharacterMovement.LastUpdateVelocity、bUseSeparateBrakingFriction、BrakingFriction/GroundFriction、BrakingFrictionFactor、BrakingDecelerationWalking。它与 Main 的当前 WorldVelocity/HasVelocity 是不同边界，不能用当前速度代替。探针在注册 Character/Main/Linked 上设定真实组件值，执行 Linked PropertyAccess 的 game pre/post 与 worker pre 批次，再调用原纯函数及节点回调；专用 ShouldDistanceMatchStop 探针确认每帧原条件与输入相同。

AlsGroundMovementPrediction 按安装引擎 AnimationLocomotionLibrary 源码移植：double 世界向量，只取 XY；长度收窄为 float，方向使用 float 逆长度；摩擦、制动、速度、停下时间保留 float 边界和原结合顺序，最终位置及 VSizeXY 为 double。负摩擦因子、摩擦与减速按原规则取零。静态 bank 只有实际 codec 与 markers，没有预期时钟或预测结果。

## 共用 Sync 的遗留 evaluator 时间

实际 30Hz Unarmed 轨迹98从长序列换到短序列，准备时仍为旧内部时间 1.3332541 s，新片长1.2666667 s，原正 delta tick 后回到0.7000001 s。之前的 Core 将准备时间、历史区间起点、PreviousRatio 都要求位于新片长/0..1，拒绝了合法原生数据。

现在玩家历史记录 IsNonLoopingEvaluator。输入只为无 Marker 的非循环 evaluator 放行有限的遗留 accumulator，普通 player 和循环 evaluator 保留原界限。超出片长的历史样本必须与其单源 typed owner 的 time/previous/delta 精确对应；超1的历史组比率还必须精确对应该原 leader 的时间/片长，伪造比率或丢弃类型标记仍失败且不写输出。既有算法与比较门槛没有改变。Stop commit 只允许实际 preparation 产生、delta=0 时保留的内部时间例外，不任意接受越界结果。

另给轨迹98注入零 delta 的诊断在 UE 触发 AnimSequenceBase.cpp:410 的 Notify 区间 ensure：上一位置1.33、Notify 内部当前位置钳成1.27，但 bPlayingBackwards=false。UE 返回了 scalar trace，进程退出1；该诊断及输入/静态数据保存在 `artifacts/lyra-analysis/stop-source-*-zero-switch-diagnostic.json` 和 `stop-source-zero-switch-ue.log`。**它不计入正常 native 验收，也没有修改 UE 或禁用 Notify 来隐藏 ensure。** Core 三项遗留时钟测试检查 evaluator 时钟/区间与类型拒绝，不能称为该 UE Notify 边界的整链修复。正常最终 fixture 恢复原正 delta，原请求与静态数据字节保持，并重新独立采集。

## 本批验证

| 检查 | 结果 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780物理帧，3672 active / 108 hidden，36资源全部实际播放 |
| 原相关性与选源 | 216 Setup，1584帧保持先前资源；每帧取消重试 |
| 预测与源结果 | 预测 double、explicit/internal、rate、cached weight、Delta/Marker 逐位一致 |
| 分支 | 1476目标匹配、1224零预测推进、972不匹配推进、660显式时间外推、937显式/内部时间不同；1次新片长外的 prepared accumulator |
| 拒绝 | 22140次旧/重复、错误epoch、非有限Delta、错误时间、有效Marker越界和隐藏旧候选 |
| Core | 73通过，0失败/跳过；含原同步、独立source、evaluator preparation、预测及新遗留时钟门禁 |
| Godot 回归 | Start源3780帧；loop/nonloop evaluator；Start/Cycle共同Main 3780帧/6300姿态/510300骨，原阈值和Root TRS差0保持 |
| 构建 | Debug / ExportRelease Optimize，0错误0警告；运行使用Debug |
| 资源 | 489原包导出前后哈希一致；最终两个独立正常UE进程退出0，immutable fixture语义与字节相同；原共同宿主12份资源字节核对 |

最终运行日志没有 Godot ERROR/WARNING。正常 UE 采集0 errors，781既有资源/GameplayTag等 warnings，未把它们称作本批修复。汇总、日志 SHA、fixture SHA 和明确验收范围见 `artifacts/lyra-analysis/stop-source-final-verification.json`。

失败与修正日志保留：两次 C# 编译错误（Core Math 名称、不存在的 profile.Name）、误要求 Distance 单调、误读 false exec-ref、对无 Marker 的无效存储进行错误拒绝测试、首次短资源被 Sync 拒绝、一次导出未完成即运行、Python tuple/list immutable比较错误，以及上述 UE 零 delta ensure。marker错误测试现显式设 Initialized=true；没有改变真实 inactive Marker 的语义。未覆盖旧生成资产，新 Stop 的未验收捕获保留在 artifacts。

## 复跑与后续

需要本机 ignored `assets/generated/lyra_als/stop_layer_graph.json`、`stop_source_requests.json`、`stop_source_native.json`、`stop_source_definitions.json` 与既有 Lyra/ALS 资源。仅代码检出不能运行。

```powershell
.\scripts\export-lyra-stop-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '../GASP58/GASP58.uproject'
python .\tools\verify_lyra_stop_source.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_stop_source_smoke.tscn
```

下一步是 Stop 原 LayeredBoneBlend/HipFire/pose/curve/attribute/RootMotion、Main Stop18→Linked19，再进入同一 Main/source 三根与实际 LocomotionSM。完整其它provider、最终状态混合/外层惯性、统一Notify/Montage、最终脚部、Godot gather、生产入口、人工/视觉/性能继续开放。本批没有新 Demo运行或渲染验收，没有commit/push；原用户修改及暂缓项保留。
