# Movement / Lean 的 update-only 生命周期

本批直接修改 `.` 的 main，补齐统一 Standing 宿主之前的已记录缺口：更新动画但跳过骨骼求值时，仍提交 Lean 滤波、BlendSpace 三角形缓存和 Movement 混合权重历史。

## 原生依据和实现

只读核对本机 UE 源码：`AnimNode_BlendSpacePlayer.cpp` 的 UpdateInternal 创建 tick；Evaluate_AnyThread 读取 BlendSampleDataCache 生成姿态。`BlendSpace.cpp` 的 TickAssetPlayer（517 行起）在 550/552 行执行 FilterInput 和 UpdateBlendSamples_Internal。因此缓存推进不能依赖是否求骨骼姿态。

- BlendPoseSource 提取共用的样本解析函数，计算三角形、权重、显式样本时间，不采样骨骼。
- BlendEvaluatorRuntime 在 Prepare 中保存该帧样本和候选三角形缓存；Evaluate 仅采样已解析的结果，重复调用不推进历史。
- BlendEvaluatorRuntime 和 MovementCacheRuntime 允许从成功 Prepare 直接 Commit；滤波、缓存和 alpha 都推进，Pose/Curves 仍不可读取。
- 区分主动跳过求值与求值失败。失败会使姿态失效并拒绝提交，直到有效重试或 Cancel；未放宽失败发布门禁。
- Cancel 不发布候选；reinitialize 在无求值帧同样重置历史。拒绝旧 frame 和错误提交 frame。

## 验证

Release 相关测试 15 通过、0 失败、0 跳过：

`artifacts/tests/movement-update-only/movement-update-only.trx`

- 既有原生滤波参考 6720 帧：新增稀疏求值运行时，每七帧才求值一次，每帧验证 native 滤波输出及三角形缓存，涵盖重置和取消重试；恢复求值时与每帧求值运行时的完整姿态、曲线严格一致。
- 新增 30/60/120 Hz 三组 Movement 生命周期，共 630 帧，每九帧求值一次，验证 alpha 和 Lean 历史与全求值路径严格一致；覆盖零 delta、隐藏期间重置、取消、错误 frame、求值失败后恢复及陈旧姿态读取拒绝。
- 原 Movement 姿态与 Standing 连续遍历回归通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v minimal`：0 警告、0 错误。

本批未启动 UE 或 Godot 运行时，未新增原生导出、全量测试、画面或性能验收。原生数据为已有冻结参考，不能据此宣称完整 Standing 图与 UE 等价。

## 下一步和保留范围

继续统一 Standing 的 Parent、遍历、播放器、动作通知、Montage/Slot 与提交回滚，然后进行完整原生连续对照和普通 Demo 接入。本批未完成该统一宿主，也未改变普通 Demo。Crouching、完整角色接入以及所有 Ragdoll/Get-up/Pose Recovery 和性能未完成项继续保留；道具物理、音频、头颈调查仍按用户意见暂缓。用户原有三处 tracked 修改及未跟踪诊断产物未纳入本次提交。
