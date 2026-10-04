# Lyra Montage 的 ALS81 动作资源

## 本批范围

沿用 [Montage/Slot 更新合同](2026-10-01-lyra-montage-slot-update.md)，补齐全部 45 个 Montage、60 条轨道引用的 55 条 Sequence。资源进入现有 `LyraLogicalSourceBank`，完整扩展库现在为 300 条源：原 234、Main Lean 3、Locomotion Extras 8、本批动作 55。

本批关闭资源和源采样对照。完整 Slot 姿态混合、FastFeet 按骨混合、Montage/Sequence 通知消费、根运动物理消费及 Main/普通 Demo 生产接入继续开放。`MainPoseHost` 的生产槽仍 inactive；没有将资源通过写成完整移植验收。

## 资源与骨架

- 原 ALS 人物蒙皮及 68 根 skin 骨保持。扩展 Skeleton 为 69 raw、81 logical，包含独立 `weapon_r` 和 12 个虚拟骨；最终 skin writer 保持 68 骨。
- 55 条动作中，28 条普通动画、24 条 local-space additive、3 条 mesh-space additive。保留原 additive 类型、参考姿态策略、帧号、映射后的基底引用、RootMotion/root lock 设置、原曲线和整数属性。
- 48 条源有真实 weapon 轨道；7 条 Emote 原本没有该轨道。后者保留 absence，由扩展 Skeleton 的参考 atom 参与源求值，未制造 float 常量关键帧。它们的新武器空间 VB 仍在源关键帧阶段生成。
- `MM_HitReact_Back_Lgt_01` 的外部 additive 基底复用现有 `rifle_idle_ads`，不新增重复来源。UE 扩展后的该基底全部 raw 轨道、103 个键、帧率和长度与既有资源逐值一致。
- 55 条 Sequence 共 200 个 `IntegerAnimationAttribute`、3 条 `DisableLHandIK` float 曲线、0 条 transform curve。其原 Sequence Notify 事件数确认为 0；Montage 自身的通知合同仍由先前目录保留，尚未接统一消费者。

非零参考帧按原 UE `GetSequencePose` 的 double 运算计算：`SequencePlayLength × clamp(Frame / SampledKeyCount)`。没有统一改成 frame-zero 或 `frame / fps`。

| 源 | 基底策略 | 帧 | 基底秒数 |
|---|---|---:|---:|
| Rifle GrenadeToss Additive | LocalAnimFrame | 29 | 0.934444417556127 |
| Pistol Fire | AnimFrame，自引用 | 24 | 0.6666666865348816 |
| Rifle Fire | AnimFrame，自引用 | 22 | 0.5333333611488342 |
| Shotgun Fire | AnimFrame，自引用 | 28 | 0.6666666865348816 |

## 实现边界

`LyraLogicalSourceBank.Load(includeMontageActions:true)` 加载新资源，默认入口和旧资源数量不变。它复用原精确 RAW sampler、retarget、local/mesh additive、曲线和属性 bank，没有增加动画时钟或共享可变采样 scratch。仅 Montage 扩展允许经过目录和 clip 双重核对的 weapon absence；旧资源仍要求原真实轨道存在。

`LyraMontageCatalog.BindSources` 将物理 Montage 的 Sequence ID 显式绑定为逻辑源 slot，核对每条轨道的 additive 类型及 clip 起点。各 occurrence 继续持有独立 sampler；还没有把这些姿态混入五个真实 Slot。

派生 UE 目标只保存于 `/Game/GodotLyraRetarget/MontageActions/LY_*`。原资源不保存。导出过程读回 `Editor.AsyncAssetCompilation=2`，避免新目标 additive 基底尚未恢复时发生异步压缩。additive 策略只在本次新建目标上恢复，已有目标按原合同校验。

AnimationData 插件必须加载，才能读取 Emote 的序列化模型；其默认 provider 会在 PostLoad 把普通 raw 动画转换成 Sequencer 模型，而后者的旧轨道 API 返回空列表。本批增加仅 commandlet 且显式 `-AlsRawTrackDataModel` 生效的设置：加载该模块的序列化类，临时移除其 Sequencer provider，核对实际工厂选择为 `UAnimDataModel`，退出时恢复。该设置在外部导出插件启动阶段生效，普通 Editor 启动不启用。

没有修改 UE 安装目录源码、GASP58 C++、项目配置或原资产。外部插件 scaffold、Godot 资源加载代码及派生资产有本批变更。

## 验证结果

| 检查 | 结果 |
|---|---|
| 资源闭包 | 300 源、45 Montage、55 动作、60 轨道，全部绑定 |
| 新 UE oracle | 510 个 RAW/求值姿态样本；负时间、首尾、非整帧、越界、实际 segment 边界和 additive 基底时刻 |
| 原 skin 保持 | 扩展后与原 ALS79 求值的前 68 骨逐通道差异为 0 |
| Godot 旧姿态回归 | 原 936、Main Lean 24、Extras 475，共 1,435 个样本，在 300 源库中通过 |
| Godot 标量/属性 | 新 9,960 行、旧 74,099 行；140,736 个曲线值、658,280 个整数属性值，presence、flags、值均通过 |
| 重复/独立采样 | 所有新旧姿态样本重复采样及独立 occurrence 严格一致 |
| 最大姿态误差 | P=1.4163191318420816e-13 cm、Q=6.58317845524286e-16、S=0；仍使用 1e-8 cm/1e-10/1e-12 门槛 |
| Debug 与优化宿主 | 两次 Godot 结果一致，实际退出 0，无 ERROR/WARNING；Debug 和 Debug Optimize 构建均 0 警告/0 错误 |
| 原标量独立回归 | 原 234 源 73,650 行、六类无效资源拒绝、标量采样分配 0 的既有 smoke 通过 |
| UE 重复导出 | 两次最终导出均实际退出 0；immutable save 核对全部新 JSON 语义相同并保留首份字节 |
| 字节保护 | 664 个 UE 资产路径（原 609、新目标 55）和 699 份既有 JSON SHA256 保持 |
| 新 JSON | 60 文件，57,553,772 字节；包括 55 clips 和 inventory/catalog/native/playback/bindings |

两份最终 UE 日志各有 113 次 `Warning:`，包括 transient ImportData 的 ConditionalPostLoad dependency 和既有 Editor/映射/提示警告；没有最终 Python/Windows error、assert 或 ensure。没有宣称 UE 零警告。

没有本批普通 Demo、渲染、人工观感、打包、多角色/并行或性能测量。优化构建与采样一致性不代表性能验收。未新增完整 Main/Slot 连续 pose oracle。

## 失败与证据

首轮曲线 FName 大小写误校验、误读 Sequencer 的空轨道、provider 顺序假设、C++ 匿名 namespace 定义错误和原 Emote absence 拒绝均保留失败日志，未放宽数值门槛或跳过资源。三批早期派生目标各 55 个包按逐文件 SHA256 验证归档至 `artifacts/unreal/lyra-montage-actions-*-first/`，只移除本批已知 allowlist 中的派生包；原资产未移除。

最终汇总：`artifacts/lyra-analysis/lyra-montage-resources-verification.json`。原数据：`assets/generated/lyra_als/montage_actions/`；不可统一格式化其 JSON。

复跑命令：

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-montage-resources.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -LogName lyra-montage-actions-resources-final-ue.log
.\scripts\export-lyra-montage-resources.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -LogName lyra-montage-actions-repeat-ue.log
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_montage_resources_smoke.tscn
python .\tools\verify_lyra_montage_resources.py
```

下一步将本批源绑定到原五 Slot 的姿态求值：同一冻结物理 bank、原非 additive/两类 additive 顺序、FastFeet profile、完整通道和 hidden/update-only/取消重试，再接真实 Main 宿主。统一通知、根运动物理消费和普通 Demo 接入保持后续独立验收。
