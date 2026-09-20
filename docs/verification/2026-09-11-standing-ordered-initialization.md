# Standing / Detail 的有序来源初始化

日期：2026-09-11。第五十九批。工作区 `../GodotALS-p5a-events-actions`，
继续保留 `d6b45e3` 以来的已有改动；本批没有提交、回退或修改 UE 资产。

## 已修正的生产缺口

上一批完成 Main 的缓存求值，但初始化链还未完整接通。本批修正其中的实际来源
消费者，不再将同帧状态重入压缩为一个位掩码：

- Standing 左右 Rotate 的播放器按 `GetInitialization()` 的真实顺序逐次初始化。
  同一状态重复进入时，独立来源 epoch 逐次增长。清除旧相关性权重仍与初始化分开，
  保留“已混合状态重入可清权重、但不一定重新初始化”的规则。
- Detail 状态更新补充固定容量的有序初始化记录及有界读取 API。容量 2 来自原生
  最多一次整机初始化和一次状态转换，并非任意降低转换上限。Walking → conduit →
  RunStart / WalkRun 只记录实际姿势状态，不初始化 conduit。生产 Detail 消费者
  使用该记录初始化四个方向来源；停止用状态编号顺序扫描位掩码作为初始化指令。
- Stop 的左右 Plant 选择器按实际初始化记录重置；整机重置策略保持原有行为。
  这些是固定求值器的选择状态，不是新增播放时钟。
- 移除 Standing / Detail 叶来源的批量冷启动预初始化。Idle 不再提前启动未访问
  的旋转、起步或 Pivot 来源；这些来源保持 epoch 0，直到真实状态初始化。

外部 Standing Cycle 来源仍沿用已有 bootstrap；它们的初始化属于独立 Save owner，
还须在完整缓存生命周期接线中处理。本批没有把这一约定包装成全图初始化已等价。

## 原生依据及修复前证据

只读本地 UE `AnimNode_StateMachine.cpp:213`、`:1186`、`:1265`：整机建立
所有 StatePoseLink，但只进入 InitialState；SetState 按当时的状态权重决定是否
初始化对应姿势链接。它会按调用顺序执行，不能将重复调用折叠成一个布尔值。
整机初始化同时清空各状态的 StateCacheBoneCounters；这是下一步条件 CacheBones
接线必须保留的行为，本批尚未实现其完整外层传播。

正式 `v4_locomotion_inputs.json` 的 Standing 图允许一帧最多 3 次转换。
两个 Rotate 条件同时为真时，首帧实际初始化顺序为 `0 → 3 → 4 → 3`，不是
按状态编号依次初始化一次。新 Godot smoke 使用真实编译图、真实旋转来源和共享
同步批次复现这条路径，没有改 UE 转换规则或使用替代动画。

`artifacts/standing-initialization-before.log` 保留修复前失败：旧预初始化基数为 1，
左转重复进入后应为 3，但旧位掩码消费者只推进到 2。仅迁移有序消费后，
`standing-initialization-after.log` / `standing-initialization-verified.log` 验证 3/2。
随后去掉无关来源预初始化，最终正确值为左 2 / 右 1：分别对应两次和一次实际进入。
这是两项独立语义修正，不是放宽断言。最终又加入 Idle 未访问来源均为 0 的检查。

该双 Rotate 输入是有意构造的生命周期边界测试，不代表正常键鼠会产生此输入，
也不证明日常滑步、交错步的所有根因已找到。

## 验证

最终优化构建成功，零警告、零错误。首次新测试构建的 FileAccess 命名歧义、
误把已有 CoreView 当作 snapshot 再调用 CreateCoreView 的错误已修正；构建失败
后没有启动旧程序集。

`standing_initialization_smoke.tscn` 最终在 30/60/120 Hz 共 210 帧通过：

- 全部路线帧覆盖同帧重复进入；首帧顺序 `0,3,4,3`，来源 epoch 为 `2,1`。
- 各频率的 Idle 预检查确认未访问 Standing / Detail 来源保持 epoch 0。
- 候选重复准备、共享来源时间及 epoch 完全一致，活动准备 0 B。
- 3 次 epoch 溢出注入全部拒绝；从未修改的已提交来源重试后，结果与有效候选相同。

最终日志 `artifacts/standing-initialization-final.log`。

Core Detail 专项 16/16，新增 3 项、扩展已有顺序/相关性重入/重试检查；
Core 常规全套 1913/1913，通过范围按已有配置排除 AlsP5aGoldenTests 与
AlsP5aTraceSchemaTests，不称为所有历史 golden 已验证。
Import Main/Standing/Crouching/cache 专项 75/75。
TRX 在 `artifacts/test-results/standing-initialization/`。

最终 Godot Main 缓存回放 1680 帧通过，六缓存、2356 次原始站姿对照、6 次 Slot
故障拒绝、事件计数及零分配保持；`standing-initialization-main-final.log`。
Standing 5040、Detail 1890、Pivot 5040、Sprint 1260 帧生产回归通过，活动 0 B；
`standing-initialization-production-final.log`。

共享来源旧/拆分/消费入口 840 帧与混合来源 1260 帧通过，13522 次贡献、38 事件、
27 次拒绝和活动 0 B 保持；`standing-initialization-shared-final.log`。
单/并行 Worker 各 180 帧、各 10 个来源事件通过，结果摘要仍为
`A9DF0647AFC3574C`，完整姿势摘要仍为 `04D4A5651B87E0E4`。
晚期来源事件故障回滚通过，事件回调泄漏为零，runtime/result/controller/pose/P4
banks 恢复。日志为 `standing-initialization-worker-single.log`、
`standing-initialization-worker-parallel.log`、`standing-initialization-worker-rollback.log`。
上述日志均在 `artifacts/`。已跟踪差异与本批涉及的未跟踪文件空白检查通过。

## 继续推进的位置

这次改动已作用于旧 Controller 及 Main 共用的来源收集路径，不只增加测试夹具。
不过它只补全上述叶来源/选择器的初始化消费：外层 Save 的初始化传播、Stop 子机
初始化到缓存的传播、Standing Cycle 外部来源初始化、状态级条件 CacheBones，
以及 Main Movement、最终 Slot/惯性化与生产提交仍需继续。清除权重位掩码仍作为
汇总数据保留，不应把它重新用作完整初始化日志。

接着完成这些生命周期与最终姿势所有权，再进入 Main/Demo；之后继续最终曲线、
动态上身、完整 Foot IK/Lock/pelvis 和原 P5A-P7。原先的平台脚锁失败和短矩阵
超预算未在本批重跑，保持未通过；没有新移动截图、UE 完整图逐帧对照或十分钟
性能采样，不宣布滑步、交错步和上身视觉验收完成。音频仍按用户要求暂缓。
