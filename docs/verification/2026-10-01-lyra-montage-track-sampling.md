# Lyra Montage 物理时间区间与完整 ALS81 轨道采样

2026-10-01，在当前主目录推进完整 Lyra Main。保留 ALS 模型、69 raw / 81 logical / 68 skin。本批完成五个 Slot 求值的实际源采样前置：物理 Montage 冻结时间记录、原轨道时间映射、提取标志、Montage 曲线覆盖、整数属性与压缩 RootMotion 属性。完整 Slot 混合和 Main 接线仍开放。

## 实现及原生语义

`AlsMontageRuntime` 的实例和冻结 Evaluation 增加 `AlsMontageDeltaTimeRecord`。实际播放时记录更新前位置并累计本次移动；零 delta 的播放帧重置 delta，停止播放后的保持/淡出帧保留上一记录。一个物理实例的所有 Slot track 共用同一记录，冻结后的 Play/Stop 不修改它，取消/重试和上一已提交 bank 的寿命保留。

原末端算法先限制移动量，再加到当前位置。由于 float 加法舍入，结果可能仍在 section 终点内一个 ULP，此时自动淡出时间仍为正数。新实际边界样本 `AM_MM_HitReact_Left_Lgt_01`：previous=0.03700000047683716、move=1.0296666622161865、remaining=1.1920928955078125e-7。原 Core 直接限制最终位置导致该帧提前终止；已按实际顺序修正。section-end 姿态的 0.00005f 偏移仍在移动记录之后执行。

原 Montage 总长度与单条轨道长度也可能相差一个 float ULP。`AlsMontageTrack` 与原资产/实例保留 ClipEnd，按原 `(ClipEnd-ClipStart)/ClipRate` 裁剪物理位置后映射时间；根运动范围沿用同一轨道映射。未指定 ClipEnd 的既有 ALS 动作保持原无额外上限的布局。原 GrenadeToss 在总末端的目标时间为 0.9666666388511658，不能用未裁剪结果 0.9666667 替代。

新增 `LyraMontageTrackSampler` 消费上述真实冻结记录，使用现有300源 bank；它没有播放器或独立时钟。源姿态按实际提取标志锁根，additive 基底沿用同一锁根语义。Sequence 属性/曲线先采样，再按 `FBlendedCurve::Combine` 由 Montage 曲线覆盖同名值及 flags。现有 Sequence 没有的 DisableRHandIK、ScaleDownWeaponR 以及 Montage 的 DisableLHandIK 等进入同一曲线布局，不能在层合成中静默丢失。

Montage rich curve 使用当前 UE 采集实际保留的 NestedLerp 运算边界；第一次用 ALS 的 Reassociated 分支导致 Rifle Equip 曲线几 ULP 不一致，修后原精确门槛通过。源曲线、原 ALS 曲线策略和既有哈希资源没有改写。

`FAnimTrack` 映射后以 double 当前时间减去物理 float delta，再在 SetPrevious 处收窄；delta 不另乘 clipRate。RootMotionDelta 属性使用本批55条目标动作的原压缩根轨道，仍保留 EnableRootMotion 的存在性。提取锁根标志不禁止 root provider 属性生成；这两者分别对照。运行时只读取 policy/root 资源和实际 bank，不读取 native oracle 驱动动画。

## UE 采集与资源

新增独立 `AlsLyraMontageSamplingLibrary`，未改被既有 JSON 哈希固定的 Montage/Blend/ControlRig 探针。第一部分实际调用原 AnimInstance Montage 更新与冻结，第二部分使用原 `FAnimTrack::GetAnimationPose` 和 `UAnimMontage::EvaluateCurveData`，把原 segment 的动画引用替换为已有派生 ALS81 transient Sequence。原人物/Main/动作资源没有保存或修改。

`export_lyra_montage_sampling.py` 重建已有55条动作控制骨及外部 Rifle ADS 基底，等待压缩完成后读取原压缩根数据；没有新建或保存 Content 资产。原 RAW AnimationData provider 的既有正确开关和串行压缩模式继续使用。新 ignored 文件位于 `assets/generated/lyra_als/montage_sampling_v1_{requests,native,track_requests,tracks,roots,policy}.json`，存在时只能语义复核，不能覆盖历史。

两次独立 UE 导出均实际退出0，数据复核相同。每份完整日志120条 Warning，主要是已有 Editor/原骨属性，以及 transient import dependency 提示；没有 Python Error/assert/ensure failure，不能报告 UE 零警告。外部导出器完整 BuildPlugin 最终成功；首次 `TObjectPtr` 的 auto 指针推导编译失败日志保留，改为明确原指针类型后完整重建。

## 最终验证

| 检查 | 结果 |
|---|---|
| 三频率原连续 Montage 与新增边界 | 15,870帧、14,350物理冻结记录，position/weight/previous/delta逐位同 |
| 物理时间边界 | 43停止后保留、411零delta、135反向、3,308双轨共享历史 |
| 整帧取消重试 | 15,870帧，候选与冻结历史相同，命令不能修改本帧记录 |
| 45 Montage / 60原轨道 | 2,522完整 ALS81姿态，普通/local/mesh additive、非零基底、提取开关和原末端均覆盖 |
| 完整轨道数据 | 1,535存在曲线值、20,176整数属性布局项严格同；未知/缺失属性拒绝 |
| RootMotion 属性 | 149源样本、合曲线前后298次对照，presence/identity正确，P/Q/S最大差均0 |
| 骨骼 | 位置最大1.1675408533417185e-13 cm、quaternion最大5.564975606931872e-16、scale差0，原门槛保持 |
| Core相关测试 | 243通过、0失败、0跳过，包含 Montage/ActionLifecycle/Mantling；本批新增4项回归 |
| Debug / ExportRelease Optimize | 两构建0警告0错误，实际Godot两运行退出0，无ERROR/WARNING |
| 既有 Montage 回归 | Blend历史及Slot v1/v2各13,440帧，原精确门槛保持 |
| 300源资源回归 | 新510/旧1,435姿态、9,960/74,099标量行及原完整属性值通过 |
| 不可变证据 | 664原/派生包路径、762既有JSON字节SHA保持，新增探针source/staged/package镜像相同 |

失败的前五次 Godot 日志保留：片段末端舍入、属性 FName 大小写比较、Montage cubic策略、轨道长度边界及 native float length与RAW double长度比较已逐项修正。没有放宽骨骼/曲线/root门槛或更改原生预期；属性比较遵循现有原 FName 不区分大小写规则。

Optimize实际运行将明确的六个 DLL/PDB 临时放到 Godot Debug加载目录，记录三DLL与ExportRelease相同的SHA；finally按哈希恢复Debug。`tools/verify_lyra_montage_sampling.py` 验证数据/原包/旧JSON/镜像、UE实际退出、编译、两实际运行、四回归及TRX。最终摘要为 `artifacts/lyra-analysis/lyra-montage-sampling-verification.json`。

## 余下完整目标

五个 Slot 的完整普通/BlendProfile姿态混合仍未实现，`LyraMainPoseHost` 当前槽仍 inactive。本批没有将单轨道采样当作 Slot 混合或生产完成。下一步将真实冻结 bank 与本批 sampler 接入原五槽，对照完整骨骼/曲线/属性/RootMotion混合及FastFeet：Profile影响整槽路径、非additive按骨归一化、additive顺序、scalar通道权重和原源裁剪必须保留。

随后完成 Main 的原拓扑接线、主惯性、最终ControlRig、完整Provider换类、统一Notify、RootMotion物理消费及普通Demo、真实碰撞、多角色、渲染和性能验收。未运行新的完整Main联合oracle、普通Demo、人工观感或性能矩阵，整体目标继续开放；没有提交或推送。
