# Lyra 原始源曲线与骨骼属性

本批关闭的是 234 条已保存 ALS 目标序列的曲线/属性导出与 RAW/additive 源取样组件。`LyraLogicalSourceSampler` 已提供同时间的 81 骨姿态、曲线和属性联合取样接口。普通 Lyra 示例仍使用原先的源播放宿主；完整 Linked Layer 源时钟、共享 Sync、Notify、图级曲线/属性混合与事务尚未接入，整条 Lyra 移植继续开放。

## 实际资源

189 普通序列与 45 Aim 序列共带 159 条 float 曲线，六个名称为 `DisableLegIK`、`Distance`、`GroundDistance`、`RemainingTurnYaw`、`TurnYawWeight`、`blendParent1`。保留原键时间、数值、插值、切线、外推、作者 type flags 和实际运行 element flags；当前原始运行 element flags 都为 0。

逐资产检查发现全部 234 条序列各有四条骨骼属性，共 936 条：Pelvis 上的 `TCHour`、`TCMinute`、`TCSecond`、`TCFrame`，实际类型均为 `/Script/Engine.IntegerAnimationAttribute`，运行 namespace 为 `bone`。776 条仅单键，另 160 条有多个动态键，不能因为它们是时间码就静默删除。当前没有 transform curve。

模型继续使用 ALS 的 68 根蒙皮骨；既有 69 raw/81 logical 骨布局保留。属性加载同时校验原始骨索引与该布局，不把 Pelvis 属性误绑到扩展控制骨。

只读导出直接调用实际 `UAnimSequence::GetBonePose` 与 `GetAnimationPose`，记录 RAW 与最终 additive 曲线和属性。两个 transient 序列夹具覆盖 source-only、base-only、共同曲线、负值及非零切线；没有保存夹具或修改 UE 原资产。

新增 ignored 资产：

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `logical_controls/curve_bank.json` | 2220366 | `576d74b050582df1359a77d14ae657fd1f88861ec7210b1418061efe3fb77ccb` |
| `logical_controls/curve_native.json` | 88590188 | `667c23d5cf8d65d621b37f9a755531171d344899cb58061f9d480054c5257a8a` |

文件绑定既有 catalog/calibration 和银行字节哈希。导出拒绝覆盖内容不同的已有文件；本批未重写旧 JSON。导出前后 475 个包哈希一致；结合编译源库存复核共 489 个包、11 个依赖和 234 条逻辑 clip，全部一致。

## 原生求值边界

本机 UE RAW 路径由 `FEvaluationContext` 将 double seconds 转成 `FFrameTime`，数据模型再转回 seconds，最后进入 float 曲线/属性采样。联合取样使用此前已验证的 `RoundSubframe` 模型时间边界，避免直接 seconds→float 的 ULP 差异。

原 ALS 的 `AlsNativeRichCurve` 默认使用已验证的重新结合 Bezier 运算。当前 Lyra RAW 原生参考需要 `CurveEvaluation.h` 的六次独立 float Lerp：例如 `jump_fall_land/DisableLegIK` 在 35/120 秒，重新结合结果为 `0.90771496`，原生为 `0.907715`。新增显式 `NestedLerp` 配置用于此源库，保留 ALS 默认路径；本结论来自实际 73,650 行原生参考，不推断所有 UE 构建均采用相同编译运算顺序。

当前 additive 全部为 frame-zero sequence base：曲线做 source−base、presence 与 flags 做并集；base-only 曲线不能丢弃。骨骼整数属性保留类型、骨和 namespace，按实际 StepInterpolate 取键，再使用原整数 MakeAdditive 减法。未知属性类型、加权切线、未支持外推、transform curve 等显式拒绝，需要实际原生算子后再扩展。

本次只实现原始源上的取样。`LyraCurveSample.ToInertial` 遇到非零 element flags 会拒绝转换，防止现有无 flags 惯性缓冲静默丢失信息。没有把上述源属性声明为已经穿过所有 Layer、Slot 或最终图。

## 验证

- UE 导出退出 0，唯一 `LYRA_SOURCE_CURVES_OK clips=234 curves=159 native=73650 fixtures=2 assets_saved=0`。未修改引擎源码/项目配置；新增外部导出插件只读探针并完整构建。既有 UE 警告保留，不声明 UE 日志零警告。
- Godot 原生源对照：234 条真实序列与两个夹具，73,650 个时间点，139,227 个有效曲线值、589,200 个属性值。数值采用 float 逐位/整数精确比较，presence/flags/类型/namespace/骨身份均通过；曲线原键和相邻 float、区间中点及 30/60/120 Hz 采样覆盖。属性用这些共同时间点对照，未单独新增所有属性键的前后边界。
- 936 次联合取样的 81 骨输出逐值等于旧独立姿态接口；六类坏资源（旧绑定、未知属性类型、缺骨、缺属性、加权切线、丢失 additive base）均被拒绝。热身后曲线与属性重复取样 10,000 次，托管分配 0 字节；不是整角色性能验收。
- 原逻辑源原生回归 936 组通过，最大位置差 `1.4163191318420816e-13 cm`、四元数差 `6.58317845524286e-16`、缩放差 0。Core RichCurve 四项与 Standing 独立/共享 30/60/120 Hz 原生六组全部通过。
- 普通独立 Lyra 示例 60 Hz headless 回归：870 个物理帧、六次换层/六次 cycle switch、871 次姿态提交、双手有效应用各 780 次，同类 Layer 复用通过；无 Godot ERROR/WARNING。这次运行验证资源加载与既有播放路径，没有验证新完整源宿主。
- Debug 与 Release Optimize 构建均 0 警告/0 错误，Python 编译及 diff whitespace 检查通过。未新增渲染截图、全量测试、完整角色 UE 连续轨迹、人工验收或十分钟性能运行。

早期失败均保留：首次误用 deprecated float PlayLength 与 RAW double 长度比较、随后拒绝丢失四条真实属性；曲线先暴露直接 float 时间的偏差，再暴露 Bezier 一 ULP 差。修正使用原模型时间及显式求值路径，没有放宽任何原生门槛。

日志与资源报告位于 `artifacts/lyra-analysis/source-curves-*`。失败 Godot 日志为 `source-curves-godot-first.log`、`source-curves-godot-clock-first.log`；失败 UE 日志为 `source-curves-ue-first.log`、`source-curves-ue-attributes-gate.log`。

## 复现

在既有 234 条逻辑源资源完成后执行：

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-source-curves.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_source_curves.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_source_curve_smoke.tscn
```

后续按 `ROADMAP.md` 补 Main 三个 Lean 样本、实际 source owner/player/evaluator tick、AlwaysLeader 与共享 Sync，再将联合源数据按原子图拓扑送入 Main/Linked Layer 曲线属性混合、Notify、惯性与统一提交。不得用旧示例的线性 crossfade 代替原图这些语义。
