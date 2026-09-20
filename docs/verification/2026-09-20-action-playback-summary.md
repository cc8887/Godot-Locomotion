# 共同 Montage 动作摘要接线

工作目录为 `D:\GodotALS` / `main`，延续 `729cd11`。未新建项目或 worktree，
未修改用户保留的 P4 规划文件、导出资产、冻结夹具及 schema。

## 本批实现

`AlsProductionMovementRuntime.CompleteEvents()` 现在通过 BaseLayer 的同一个
发布入口写出 `TypedEvents`、`ActionOutcomes` 和 `ActionPlayback`。发布前先
检查完整帧身份、求值状态和是否已有其他发布者，再完成所有可能失败的读取；
校验失败不会只写出部分结果。最终提交仍由原 Worker/Exchange 事务负责。

新增只读 `AlsMontageActionPlaybackReader`，不维护第二套时钟或提交历史：

- 单个 `ActionPlayback` 表示 BaseLayer 当前逻辑动作所有者。身份为共同
  Montage 的真实 InstanceId，接受新请求当帧保留零推进和零姿势权重。
- 替换、取消立即结束旧逻辑所有权；旧实例仍在共同物理队列淡出。
  `ReadInstance()` 按独立实例 ID 查询动作淡出，取消后不回退到旧实例。
- 自然淡出不提前结束逻辑动作，直到物理实例终止才输出默认摘要和完成结果。
- 时间读取真实 Montage Position，片段时间使用原 ClipStart/ClipRate 映射。
  PlayRate 包含片段倍率；BlendSeconds 读取实例当前混合时长。
  最后片段的实际消耗时间由 Traversal 区间除以 Montage 倍率得到；
  到达端点时保留原生播放的 endpoint-epsilon 采样位置，不更改时钟。
- 权重取同一帧冻结求值数据，并按该 Slot 的总权重规范化。
  整个 BaseLayer 未被访问时权重为零，动作时钟和身份仍继续存在。
  这是 Slot 内贡献，不是最终 Root/全身图所有下游分支的综合权重。
- 跨 Slot 的动作按指定 Slot 查询；多个不同组同时占有同一个 Slot 时，
  单值摘要明确拒绝歧义，不能静默挑选一个或合并实例身份。

`AlsMontageActionPlaybackCompiler` 从实际单 section、单 segment 资产读取编号，
并绑定当前 Montage 通知表的 sequence occurrence。Root/Overlay 来源布局切换
后重新编译，因此摘要与该帧的片段通知使用同一 handle；Montage 自身通知仍有
自己的 direct handle。导入测试将 handle 整体平移 1000，确认不会复用旧编号。

## 验证

证据目录：`artifacts/action-playback-20260920`。

- Core 动作/共同所有者初次专项 30 项通过；新增只读查询热路径测试保留
  300 帧热身、1000 帧测量及零托管分配断言。
- Core 最终 Release 回归（排除未改动的两个 P5A 长矩阵类）2509/2509 通过，
  0 Skip，耗时 1 分 18 秒；包含本批 9 项摘要检查，见 `core-final.trx`。
- Import 相关编译/通知专项 41 项通过，包含当前布局映射、缺失绑定和陈旧片段拒绝。
- Godot 优化构建 0 警告、0 错误。首次新增测试用 `with` 修改只读 FrameId 导致
  编译失败，已改为显式构造身份；初始失败与最终构建日志均保留。
- 真实 Roll BaseLayer 回放：30/60/120 Hz，共 1050 帧、1050 次丢弃重试，
  28 次实际采样后故障；9 次接受、3 次替换、3 次取消、3 次自然完成，
  354 帧完整 Roll 姿势，379 次回调。逐帧检查摘要、片段 handle、实际采样时间、
  冻结 Slot 权重及公开事件/结果；错误身份、已有结果的重复发布和故障后发布均拒绝。
- 隐藏 Root 分支回放：1050 帧，651 帧未访问、468 帧隐藏 Montage 推进，
  每帧重试。隐藏时摘要保持动作身份并输出零权重；恢复与重试一致。
- 普通完整入口键鼠回放：360 帧通过，83 帧实际脚趾锚定。
- 十角色 Single/Parallel 各 3621 帧通过，分别覆盖两次取消与一次提交等待。
  两种模式的 pose `BF25422552331C92`、root `8B519543E987009F`、
  result `02BF0BAAA9542349` 一致，与上一批无动作输入回归一致。

动作注入验证使用现有真实资产回放场景。普通键鼠和十角色调度回放没有新增
动作按键，因此后者验证的是新发布入口对原链路的兼容，不宣称已验收
多角色 Roll 玩法。本次没有重新运行 UE，也未重跑未改动的两类 P5A 历史
Schema/Golden 长矩阵；前一批结果见 `2026-09-20-frozen-reference-replay.md`。

## 后续

P5A 的动作摘要已接通；接下来补齐类型化 Notify 的玩法消费者与请求来源，
再接 Roll/Mantle 的碰撞安全 Root Motion。Overlay 道具玩法、完整 Ragdoll/
Get-up/Pose Recovery/Camera、当前完整图人工观感和地形验收，以及 P7 十分钟
性能认证继续按原范围推进。本批没有新增 Roll 按键或宣称完整 Roll 玩法完成。
