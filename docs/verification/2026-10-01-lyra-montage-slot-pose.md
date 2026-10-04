# Lyra 五个 Slot 的完整 ALS81 姿态混合

2026-10-01，当前主目录、安装版 UE 5.8.1 与 Godot 4.7.2 mono。此次完成实际 Montage 冻结帧的完整 Slot 求值组件及原节点对照。整个 Lyra 移植目标继续开放；普通 Demo 和完整 Main 尚未接入活动 Slot。

## 实现

新增 `LyraMontageSlotPose`，消费原 `AlsMontageRuntime` 的冻结 `AlsMontageFrame`、帧身份和原五槽 ID。使用上一批 `LyraMontageTrackSampler` 采样原45个 Montage 的60轨道；没有新增播放器时钟、Sync、通知或骨架 writer。资源仍是原300源库和 ALS logical81/skin68。

求值保留原生两条路径：

- 普通路径按总槽权重归一化所有 non-additive/additive 标量，SourceWeight 保持原值。非 additive 按原冻结顺序混合，源姿态位于末尾；单姿态不额外归一化旋转，多姿态才归一化。
- 同槽任一冻结 Montage 有 ActiveBlendProfile，整个槽进入 profile 路径。FastFeet 按目标骨名映射的15个0.5因子及其余1因子计算权重；non-additive 按骨归一化，源骨权重取1减去其余骨权重。曲线和属性使用独立标量归一化；additive 保留未经总权重归一化的权重。
- local additive 与 mesh rotation additive 按原顺序累积；mesh 路径先将整个基底转为组件旋转，再还原为局部旋转。profile 路径逐骨执行 BlendFromIdentity，普通路径保留 relevant/full-weight 快路。
- 完整传递曲线 presence/flags、整数属性、RootMotionDelta presence/TRS。属性区分共享、唯一和 Override；唯一 Root Motion 从默认 identity 插值，共享值按标量累积后归一化。Montage 曲线覆盖 Sequence 曲线仍由同一 sampler 完成。
- 输入只读、输出独立。结果在全部采样和混合成功后复制到调用者输出；排他 scratch、防重入和帧身份校验不推进物理 bank。

既有 ALS 的 `AlsMontageSlotPose` 未改动。本批实现使用实际 float 除法，不将槽归一化替换为预先计算 reciprocal 后相乘。

## UE 原节点采集

新增 `AlsLyraSlotPoseLibrary`、`export_lyra_slot_pose.py` 和包装脚本。探针在临时 GamePreview 世界登记真实组件并创建原 `ABP_Mannequin_Base`，取得原编译节点81/71/2/74/84，逐个执行实际 `FAnimNode_Slot` Initialize/CacheBones/Update/Evaluate。冻结帧由原 Montage_UpdateWeight、Montage_Advance 和 UpdateMontageEvaluationData 生成；原 RootMotionFromMontagesOnly 配置为3。

原45个 Montage、55个目标 Sequence、Skeleton 和 FastFeet 均在 transient package 使用；Montage 轨道按原源路径绑定 ALS81 序列，FastFeet 按骨名重建。采集不保存 `.uasset`，不部署项目插件，不修改 Engine 或 GASP58 内容。

节点输入为三条受控真实 non-additive 轨道：FingerGuns Emote_MW、Pistol Reload Emote_MW 和 Dash Backward，以原物理位置0.27、delta0.037采样。它们覆盖输入 Root Motion 有/无及与 Montage 属性的共享/唯一组合。五槽独立求值；UpperBodyAdditive 在此组件夹具也接受该受控输入，**不是完整 Main 的 additive reference 输入及上下游联合执行**。

沿用上一批原45资产、三频率正常及反向/零delta/大delta轨迹，共15,870帧。每帧记录五个实际节点的权重，选定帧记录姿态和源实际求值次数。非采样帧仍执行 Update，物理 Montage 每帧只推进一次。

两个独立 UE 进程实际退出0，第二次完整捕获与首次 JSON 语义相同。两份日志各120条 Warning、0条 Error；保留已有 Editor、GameplayTag 和 transient 动画导入依赖警告，不能称 UE 零警告。原664包和768份既有资源 JSON 的字节 SHA256 保持；本批和前两批 Montage 探针的 source/package 镜像哈希亦验证一致。

新 ignored 资产：`slot_pose_v1_requests.json` 2,358,945字节、`slot_pose_v1_native.json` 260,661,172字节。运行时代码不读取 native oracle；只有 smoke/verifier 用它对照。

## 最终验证

| 范围 | 结果 |
| --- | --- |
| 实际物理帧 / 五槽权重 | 15,870帧 / 79,350行，clock、freeze、SourceWeight、SlotWeight、TotalWeight逐位一致 |
| 实际节点姿态 | 17,015份原生姿态，81骨完整比较，包含72份FastFeet、246份总权重归一化 |
| additive / 源隐藏 | 1,187份local、246份mesh；534份源完全隐藏，原源求值次数一致 |
| 完整数据 | 两次候选共37,338曲线值/flags、136,120属性布局项；11,710份原生Root Motion输出对应23,420次对照 |
| 取消与重复 | 15,870帧取消重试，重复Evaluate逐值相同，31,740次旧帧身份拒绝；命令不改变冻结输出 |
| 最大骨骼误差 | P=1.2347916251785851e-13 cm，Q=8.684435308193858e-16，S=0 |
| Root Motion误差 | P/Q/S全部0，presence逐项一致 |
| 构建与实际优化宿主 | Debug、ExportRelease Optimize均0错误0警告；两套实际Godot退出0，无ERROR/WARNING |
| Core回归 | Montage / ActionLifecycle / Mantling共243通过、0失败、0跳过 |
| 既有运行回归 | Main两套各11,340帧（含实际最终反馈）、15,870帧轨道采样、300源完整资源、13,440帧Slot v2均通过 |

沿用位置1e-8 cm、四元数1e-10、缩放1e-12门槛；曲线按binary32位与flags比较，整数属性精确比较。未改 oracle、删除 case 或放宽阈值。

优化测试将 ExportRelease 的六份DLL/PDB临时放入Godot实际加载的Debug目录，记录三DLL哈希与ExportRelease相同；结束按六文件原哈希恢复。恢复后的Debug文件与 `slot-pose-debug-assemblies/` 逐项一致。

汇总：`artifacts/lyra-analysis/lyra-slot-pose-verification.json`。验证入口 `python tools/verify_lyra_slot_pose.py` 检查捕获依赖、旧资源/原包、探针镜像、两次UE实际退出、Debug/Optimize实际宿主、回归日志和Core TRX。

## 失败证据与剩余工作

首轮 C++ 因缺少 FMontageEvaluationState 完整声明失败，第二轮误用了不存在的 AnimMontageInstance header；已改为原 `AnimMontageEvaluationState.h`，最终完整 BuildPlugin 成功。首次 C# 编译把实际 ProfileId 写为 ActiveProfileId，已修正。三个 UE 构建日志和 `slot-pose-debug-first.log` 保留；首轮实际 Godot 完整姿态对照直接通过。

本批仅关闭五槽完整求值组件。`LyraMainPoseHost` 当前仍使用 inactive Slots；Main 原生联合执行、活动槽影响的真实源 Update/缓存选主/相关性/RootMotion modifier、node75主惯性和最终ControlRig73继续开放。

下一步将同一物理bank的五槽源权重接入原Main Update遍历，再按原位置求值：UpperBody/UpperBodyAdditive→Split→PreAim→Aiming→HitReact→Recovery additive→FullBody→主惯性→RootYaw→SkeletalControls→ControlRig。需先处理FullBody/HitReact覆盖时下游源不再Update/Evaluate，以及缓存的实际访问顺序；不能只在现有完整基底上事后叠加Montage。

完整换类、统一Notify、Root Motion真实碰撞消费、普通Demo、渲染/握持/地形、性能与整链原生验收仍待。原ALS R2–R7及用户未提交修改保持。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' -UnrealProject '../GASP58/GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-slot-pose.ps1 -EngineRoot '../UE_5.8' -UnrealProject '../GASP58/GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . scenes/tests/lyra_montage_slot_pose_smoke.tscn
python tools/verify_lyra_slot_pose.py
```
