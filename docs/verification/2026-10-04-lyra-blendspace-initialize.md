# ALS 人物上的 Lyra BlendSpace 初始化

在主目录继续原完整 Lyra 移植目标，保留 ALS skin68/raw69/logical81 与原十四入口。本批证据和验收边界如下；完整目标仍 active。

## 本批实现

Provider 的实际 Initialize 现在访问 Aiming 的两个 RotationOffsetBlendSpace 79/74；Main 的实际阶段回调访问三个 Lean 播放器 22/16/12。初始化执行原 exposed-input 规则，归一化时间回到原固定 start=0，清空样本缓存和滤波器，重置 Marker 索引与 full-weight 历史。Delta、Marker 距离、缓存权重、三角形索引保持；Aiming 的 ActualAlpha 与 LOD enabled 保持。初始化不会运行 worker、Setup/Update、共同 Sync、姿态求值或通知。

各宿主的候选重初始化共用相同字段规则，实际 Update 才更新权重并锁存 full-weight；隐藏帧、重复求值与 Cancel/Commit 继续归入角色事务。Main Lean 的无滤波轴输出在共同 Sync 更新，初始化为零。

当前这些资源按既有无标记长度同步策略运行。基础节点的 Marker 存储由实际宿主保留，不把它伪装成当前 Sync 合同的 Sequence Marker 参数；样本时钟沿既有共同 Sync 更新。本批没有修改 Core 的通用同步接口或把 marked BlendSpace 的完整储存语义视为已验收。

未访问的 Main 状态不会提前初始化 Lean；受控检查使用真实 Main 阶段回调，未增加生产 seed API。原候选状态进入路径保留，不把所有状态进入直接改写为提交状态。

## 原 UE 证据

可选 LyraBlendSpaceInitializeOracle 加载原 Main、原三 Provider 和 Manny164，在临时 GamePreview 世界取得真实实例。分别对 Main 三源和 Provider 两源设置两轮非零样本、归一化时间、权重、Delta、Marker、三角形缓存、滤波输出；Aiming 另设置 ActualAlpha=.42 与 LOD enabled=true，Main 将原 AdditiveLeanAngle 设为23.5/-17.25。

随后调用原 Initialize_AnyThread 和 CacheBones_AnyThread，读取前后字段，没有重写初始化算法、修改引擎或保存资产。两个最终 UE 进程的 requests/native/closure 逐字相同，共15节点、30初始化记录。原滤波器默认二维，输出归零；样本清空、full-weight 清除、PreviousBlendSpace 指向原 expose-input 资产，缓存索引和 Delta/权重/Marker 距离保持，ActualAlpha 与 LOD enabled 保持。CacheBones 后快照相同。

Godot 对照把相同种子写入实际宿主并执行真实阶段入口，比较上述可表示字段；资产与滤波配置核对原不可变定义。Aiming 原生参数参考为默认0/0；非零接口 scalar 的完整原生传播不因本批关闭。FFIRFilterTimeBased 的 private FilterData/CurrentTime 等没有直接读取，managed 初始化清空自身滤波状态；此项不作为全部私有字节对照。原资产的0 start/1 play-rate配置保持，非默认 start/负速不在本批验收范围。

## 验证与保护

最终Debug/实际ExportRelease Optimize均0错误0警告，关联Core163项通过。每构建35个主矩阵场景加四布局完整Main、共39个进程，两构建合计78个进程全部退出0且无Godot ERROR/WARNING。新30种子记录、原五类瞄准/倾斜连续参考、原Sequence/阶段、初始self/Link/Unlink、取消重试、普通十角色/Emote及四布局final Rig均通过。瞄准每构建7560帧，Main Lean与合成各2100帧，Start/Cycle Lean各3780帧；四布局每构建4320帧及同数retry继续对照既有完整Main原生参考。原字段34/47与多组32/47的比较范围保持。初始/解绑计数及普通十角色/Emote完整JSON同两构建和source-initialize-v2基线。

独立审计artifacts/lyra-analysis/blendspace-initialize-v3-integrity.json通过20份当前冻结源、其余1463条本批基线文件、870资源JSON、710原包、9项宿主/配置、23份逐字原UE源码副本和三轮Optimize六文件恢复。Optimize主DLL与Debug不同。原生参考用blendspace-initialize-v2与-repeat，最终运行与恢复用v3；Core沿未改动的Core代码使用v2-core.trx。原生最终两进程各744条原加载/GameplayTag Warning，无Error/Fatal/Ensure。

第一轮原生采集 blendspace-initialize-v1 误断言三维滤波器而失败，已核对原声明默认二维后修正；失败 log/native/request 保留。第一次构建误填 csproj 路径，保留 missing-project.log，正式两构建使用根 GodotALS.csproj。

v2 的连续 Aiming 回归因误将 BlendSpace Marker 传给仅支持 Sequence 的显式存储参数，触发 InvalidSyncGroup。核对原无标记长度路径后修正为基础节点保留独立存储；失败场景日志与20源、12程序集归档。最终 runtime 使用 v3，原生参考使用修正后的 v2/v2-repeat，没有放宽比较阈值或删除回归断言。

## 保持开放

本批关闭原固定配置下的可表示 BlendSpace 初始化字段和实际 Main/Provider 接线。全部 Source private 状态、完整 private filter、非零 Aiming scalar 原生传播、骨控制初始化、阶段与 Update/Evaluate 的缓存统一、后续完整重初始化、RequiredBones/LOD 与 Rig Construction 仍开放。self scalar/部分绑定/重复函数调用/其它 Provider、原 Linked字段34/47、UE/Jolt物理314/1680差异、近景握持、复杂地形及性能保持；音频、道具物理和头颈暂缓。无本批新GPU/全量managed/十分钟/性能或完整物理等价验收，无提交推送。
