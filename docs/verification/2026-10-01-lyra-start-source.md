# Lyra Start 原 evaluator 回调与共同 Sync

2026-10-01，在主目录实施；使用 GASP58、本机 UE 5.8.1、Godot 4.7.2 .NET。继续沿用 ALS 模型、68 skin / 81 logical，本批使用已经重定向到 ALS 骨架的 36 个 Start 动画。完成原 Start 单源 occurrence；完整 Lyra 移植继续开放。

## 原回调与相关性

真实注册的 Main/Linked Item 实例经原 FPoseLink 调用编译的 SequenceEvaluator：`SetUpStartAnim` 仅在 BecomeRelevant 执行，按 IsCrouching → ADS → Jog 的优先级及 **LocalVelocityDirection（带 offset）** 选取 cardinal，并将显式时间和 Start alpha 归零。连续 `UpdateStartAnim` 不重新选资源，方向/站蹲/ADS 改变本身不是重新选源的条件。与 Cycle 使用无 offset 方向、连续换源的规则不同。

相关性来自实际物理更新的节点访问和权重，阈值为原 `ZERO_ANIMWEIGHT_THRESH`。隐藏物理帧及微小权重后的恢复触发 Setup；连续相关时 Initialize 不清 NodeRelevancy subsystem，因此不会无条件重选或把显式时间归零。原 evaluator 的 Initialize 设置 pending reinitialization，清 Marker 索引；隐藏 Initialize 的 pending 状态保留到下次实际 update。

本机 `AnimNode_SequenceEvaluator.h` 明确覆盖 `GetAccumulatedTime()`，返回 **GetExplicitTime()**。原 Blueprint 的 Start alpha 因而读取显式时间。源码同时维护独立的 InternalTimeAccumulator，之后由共同 Sync 更新；两者不能合并。`LyraStartSourceRuntime` 保留两个时间、物理访问历史、缓存权重、Marker 与 Delta。Prepare/Commit/Cancel 包括隐藏帧和 pending 初始化，取消不发布选源、相关性或时钟历史。活跃结果交给已有 `LyraEvaluatorSourceTick`，注册到角色外层的共同 Sync；组件不自行建立第二套同步时钟。

## Distance 曲线与真实 codec

`AdvanceTimeByDistanceMatching` 只有 delta>0、distance>0 才推进；按原 float 1/30 秒步进采样，逐步累计曲线距离，最后计算有效 rate，经 double FVector2D 限幅后转回 float，再写显式时间。原 Start 的 lower clamp 是 `Lerp(StrideWarpingBlendInDurationScaled, Clamp.X, StartAlpha)`，保留该参数与运算顺序。距离由 Main double 显式收窄为 float。

新增可复用 `AlsDistanceMatching.AdvanceNonLooping`，距离采样由静态资源实际 codec 提供。当前 36 个 ALS Start 资源稳定后全部为 **UniformIndexable**：导出精确压缩样本、sampleRate、float bits、range、length、rateScale、Marker 以及真实 instance 的 double policy；采样使用原 SamplePoint/floor/alpha/float Lerp。静态 bank 没有原生预期时钟、rate 或 callback 输出。RawRichCurve 是显式备用路径，未以本批稳定后的 36 条轨迹验收该分支。

首轮加载时压缩尚未完成，静态读取 RawRichCurve，实际节点更新随后改用压缩曲线，出现 1 ULP 时间差。独立曲线探针在单独加载时与 raw 一致；**同一原节点物理帧**探针才证明 default 曲线已改变、compressedValid=true。现 `ReadDistanceSequenceData` 通过 `WaitOnExistingCompression(true)` 接受已有内存压缩结果后导出；采集结束重新读取所有静态数据，必须完全相同。没有修改/保存 Sequence 或引擎代码，错误轨迹与诊断证据均保留。

## 换资产时的 Marker 清理

实际 `FAnimGroupInstance::Prepare` 按持久 occurrence 匹配上一组玩家，资产改变或上一物理帧缺席时清 Marker 索引。共同 Core Sync 原先仅按组存在性/Marker 集合变化清理，本批补齐 occurrence/资产身份清理。

旧动画 Marker 索引可能超出新动画的 Marker 数量；该存储应在新动画 tick 前清理。输入门禁只放行能够与上一有效 occurrence 记录**精确对应**、且在旧 sequence 范围内的存储，随后原组 Prepare 清理；无对应历史、改变距离或超出旧范围的数据仍拒绝，不修改输出缓冲。三项新的回归覆盖正常换源、篡改存储及越界存储；独立 source 未扩大此例外。

## 连续对照与范围

| 检查 | 结果 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780 物理帧，3672 active / 108 hidden，36 资产全部实际播放 |
| 回调与源结果 | 216 次 Setup、144 次资产改变、1584 帧保持先前选源；alpha、explicit/internal 时间、rate、cached weight、Delta/Marker 全部 float/double bits 一致 |
| 不同时钟 | 152 帧显式时间与 Sync 时间不同，分别逐位检查 |
| 边界 | 各 108 次零/tiny delta，576 次负距离，18 次连续相关 Initialize、9 次隐藏 Initialize；三Hz首帧/隐藏/微小权重恢复 |
| 事务 | 每物理帧取消/重试，22140 次旧/重复/错误 epoch、时间、Delta、Marker 及隐藏旧候选拒绝 |
| Core / Import 回归 | 53 / 15 通过，无失败/跳过；含 Standing 独立/共享原生六组与共享 Source scope |
| Godot 回归 | Main Cycle＋Lean，循环/非循环 evaluator 原生轨迹，60Hz Rifle 换层示例 |
| 资源与采集 | 两个独立 UE 进程正常退出 0，静态 codec 和 native 语义相同且原文件字节保留；492 包及旧 Main fixture 字节门禁 |

最终 Debug 与 ExportRelease Optimize 构建、运行日志与 SHA 见 `artifacts/lyra-analysis/start-source-final-verification.json`；独立资源验证器为 `tools/verify_lyra_start_source.py`。运行使用 Debug。UE 原资源加载的 GameplayTag/旧插件警告仍保留，未将其算作修复；最终 Godot 日志无 ERROR/WARNING。

失败与采集修订记录保留在 `artifacts/lyra-analysis/start-source-*`：首夹具缺失重新入状态 Initialize 导致 native marker 时间越界；首次采集误把 evaluator 虚函数 Getter 当作内部时间；Core Math 命名空间编译错误与一次错误 SDK 工作目录；源换资产未清 Marker、压缩竞态导致 float 差、旧 Marker 越过新数量而被门禁拒绝。没有放宽比较门槛或删减 case。三个本批新诊断 fixture 已归档到同一 artifacts 目录；未覆盖此前 generated 资产。

## 复跑与下一步

需要本机 ignored `assets/generated/lyra_als/start_source_requests.json`、`start_source_native_bits.json`、`start_source_definitions.json` 及原 Lyra 资源闭包，仅代码检出不能运行。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-start-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_start_source.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_start_source_smoke.tscn
```

本批关闭所测 **Start 单源回调/时间与共同 Sync 组件**。Main 输入、根相关性、图权重与 Initialize 仍为明确受控边界；尚未来自真实 LocomotionSM 的整体遍历。完整 FullBody_StartState 的 HipFire/双 Warp/姿态/曲线/属性/RootMotion、真实 Main Linked Start＋Lean、其它 provider 根、外层惯性、统一 Notify/Montage、最终足部、Godot gather、生产入口及整链/视觉/性能仍待。普通 Demo 没有替换，当前组件未验收活跃 update-only、不同 Marker 集合或所有无初始化的隐藏换源；ALS R2–R7 与用户修改/暂缓项保留。
