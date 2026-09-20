# 第十二批：Detail 非循环同步

日期：2026-09-10。工作区：`D:\GodotALS-p5a-events-actions`。
归属：完整性补完 A 的 Detail 源时间，以及原 P5A 的 Sync 接线前置。
这是已有规划内的继续实施，不是新增一套独立生产动画系统。

## 实现和边界

- 在现有 `AlsSyncRuntime` 中增加 `TryEvaluateLengthGroup`，支持非循环、无有效标记、
  所有成员均为 CanBeLeader 的组。旧循环标记同步入口没有更改行为。
- 每个源保留 OccurrenceHandleId / AnimationId / PlaybackEpoch，最多 128 个成员。
  按 UE 的 LeaderScore 排序，包括原生不稳定排序的同权重顺序，不按资源 ID 决定 Leader。
- Leader 使用有效播放倍率推进并钳制到动画端点；Follower 使用 Leader 的前后归一化
  时间映射。惯性化上下文、上一帧 Leader 分数和加入时位置覆盖设置共同决定是否重同步。
- 保留 UE 的 Tick delta 语义：Leader 到端点时仍记录请求推进量；Follower 即使非循环，
  也保留速率符号修正。不能直接用 CurrentTime-PreviousTime 代替此记录。
- 每次主动画更新中，空组也必须求值，以清除历史。返回状态是候选值；失败不写调用方
  输出、不发布候选。它没有自有全局时钟，不接管事件分发。
- 原生 Detail 导出新增编译后资产路径、RateScale、标记数量。严格编译器校验资产
  一致性、有限倍率、乘积溢出和无标记前提，显式保留资产倍率，不假定恒为 1。
- `DetailMachineSmoke` 移除独立累加时间的测试输入，消费状态初始化/清权重信号、
  清理前子状态更新次序及惯性化同步上下文，经共享 Sync API 更新 Run Start / Pivot 1 /
  Pivot 2 三组，再用同一批源时间采样姿势与曲线。
- 测试中的 16 个槽位和历史复制仅为组件宿主。正式 P5 bindings、源身份表、GroupState
  存储、Gather/Worker/Commit、事件消费和晚期失败统一回滚尚未接入，不能算 P5A 完成。

## UE 原生对照

新增 `AlsLengthSyncCommandlet`，直接调用 UE 5.9 的
`UE::Anim::FAnimSync::TickAssetPlayerInstances`，加载真实 ALS 的四个 Accel 动画和
长度不同的 Run BasePose。没有用 Godot 公式生成期望值，也没有改动或保存 UE 资产。

9 个场景 x 30/60/120 Hz x 50 帧，共 27 条轨迹、1,350 帧：Leader 切换、同权重与
插入次序、惯性化重同步及分数抑制、覆盖位置、空组后重入、反向/零速率、端点、
零 delta、共享动画的独立实例、8/32 个成员排序。

比较 Leader、完整输出排序、前后时间比、每个源前后时间和 Tick delta，容差 5e-6。
同一套轨迹分别运行逐帧原生输入和连续自主时间回放两种模式；后者仅在初始化/epoch
变化时读取原生时间，其他帧使用 Core 上一帧的结果，避免隐藏累积偏差。两者均通过。
每帧同时验证从同一已提交状态重试得到相同候选结果。

按 `ue-diagnosing-plugin-build-load` 技能完成完整 Editor target 构建与插件审计后，
才冷启动原生探针。技能引用的两个 superpowers 技能未安装，本批以实际命令退出码、
审计、结构比较和原生轨迹测试作为验证证据，不声称执行了它们。

- 构建日志前缀：`20260910T061639429Z-34b0bacf9fd0451aba7c5a0cbf422789`。
- Build fingerprint：`7C212B8655F607880783DABE0E01344735F25FC098C37CDDF74C83AC8EF0D807`。
- 同步探针日志：`artifacts/length-sync-native-buffer-fixed-20260910.log`。
- 图导出日志：`artifacts/detail-sync-metadata-native-20260910.log`。
- 两次有效 commandlet 均退出 0，0 errors / 0 warnings，assets_saved=0。
- 未执行 GUI 重启、项目数据验证和打包，不将本批作为 UE 插件发布验收。

先前探针有路径错误；修正路径后的探针又因重复交换同步缓冲而产生空输出。
Core 对照测试拒绝了该无效结果。已移除重复交换并增加每帧输出数量检查；无效输出
保留在 `artifacts/length-sync-invalid-double-flip-20260910.json`，不计入成功证据。

有效 fixture `tests/Als.Core.Tests/Fixtures/P3/v4_length_sync_native.json` SHA256：
`C66141B04B51AC950BCE0F0BC8F47100B783F9516D8C7F6DC4CB7D7372F91FE3`。

Detail 图去掉三个新字段后与上批结构完全一致。16 个实际播放器资产倍率均为 1，
标记数量均为 0。更新前图保留在 `artifacts/detail-before-sync-metadata-20260910.json`。
新图 SHA256：`986232C061E0F3D6A118E829B29AD92A2AAAB8E0D4647F83AF6FEFA65D04772D`。

## 回归结果

- Core Locomotion + 旧 Sync + 新 LengthSync：392/392，其中新同步 15 项。
- Import 全量：600/600，新增资产倍率/路径/标记元数据验证共 8 项。
- Godot 编译：0 warnings / 0 errors。
- Detail 状态/同步/姿势/惯性化联合测试：30/60/120 Hz，六状态，71,400 次骨骼检查，
  1,050 次源时间/epoch/同步历史/姿势候选重试，38 个请求，6 次重新初始化。
- 各频率预热后 1,000 次联合热路径，包括 Sync 与源历史复制，为 0 B 托管分配。
- Standing Cycle、Stop Plant、Detail Pose 回归通过；Camera/Input 文件哈希与上批一致。

```text
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=length_runtime demo=not_connected
```

日志：`artifacts/detail-machine-length-sync-20260910.log`、
`artifacts/standing-cycle-length-sync-regression-20260910.log`、
`artifacts/stop-plant-length-sync-regression-20260910.log`、
`artifacts/detail-pose-length-sync-regression-20260910.log`。

## 下一步与未完成项

本批证明的是限定模式的同步运行时，以及 Detail 组件宿主消费该结果。
不是整张 UE AnimBP 的状态/曲线/最终骨骼轨迹对照，也没有更改可玩 Demo 输出。
尚不能报告起步滑步、交错步或上身表现已修复；未跑全量 Core/P5A/P7 或新的 Demo 截图。

继续先补实际状态/源时间原生轨迹，再把新同步分支、Detail 与外层 ShouldMove /
Not Moving / Moving / Stop / Feet Position 纳入正式播放身份和提交事务。
现有 P5 单组 bindings 和 Base22/总49旧布局不能覆盖 26 Cycle + 12 Stop + 16 Detail
的采样身份，必须按完整源图修订，而不是合并为一个合成姿势身份。
随后补 Pivot 源事件、动态 Layering/YawOffset/IK 曲线消费，再继续 P5B/P5C/P6/P7。
这些是原范围内未完成的实现和接线，不以新增 Root Motion 或统一换向冷却替代。
