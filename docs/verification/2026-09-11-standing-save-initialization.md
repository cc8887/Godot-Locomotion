# Standing / Stop / Detail 的 Save 初始化传播

日期：2026-09-11。完整性恢复第六十二批。

## 本批变更

此前 Main 已初始化 Standing Save，但 Standing / Stop 进入状态时只登记 Update
缓存读取，没有完整传播 Initialize 到 Detail 和 Cycle。Cycle 来源仍靠收集阶段
启动初始化，无法证明共享 Save 的来源初始化所有权。

本批按实际 compiled 读取关系接通：

- Main 初始化 Standing Save 后，显式建立 Standing Idle，不等到 Update 再重建。
- Standing 的 Moving 初始化读取 Detail；Stop 初始化其内嵌状态机，初始状态及
  后续状态初始化按顺序读取 Detail。初始化后的 Stop 可以当帧不参与 Update。
- Detail Save 实际转发 Initialize 时建立 Walking，并初始化其 Cycle 读取。
  Detail 后续状态进入仍请求相应读取，由 Save 自己的计数决定是否再次转发。
- Main 整合 owner 在 Cycle Save 初始化回调中建立九个正式外部来源的初始时间、
  epoch 和缓存权重，包括六个 WalkRun、Lean、Sprint、Sprint Impulse。
  此路径启用严格检查，不能回退到启动初始化；每帧检查全部九个 epoch 与真正
  Save 初始化次数一致。没有因同步批次为空或相关性中断而无条件重置来源。
- 独立 Initialize 回调进入条件 CacheBones。内嵌 Stop 的实际初始化不被后续
  Standing 更新观察再次清空计数；Save 骨骼计数仍独立，不被初始化强行作废。
- 初始化结果由即时回调消费，返回帧复用已有 Standing / Stop / Detail 结果槽。
  若某机初始化后未 Update，Godot 消费者保留该初始化快照及已清理的 Stop 选择器，
  不回退到上一帧姿态状态，也不再额外调用 Initialize 来重造快照。

依据为本地 `AnimNode_StateMachine.cpp:213`、`:385`、`:1260` 的初始化、相关性
与 SetState 子图遍历，以及正式 pose-cache graph 的读写关系。本批进一步只读
核对了先 Initialize 子图、再条件 CacheBones 的顺序；没有启动或修改 UE。
来源初始化使用现有正式来源绑定及反向起点算法，不修改原始资产。

## 验证范围

新增 Import 测试 9 项：五项真实 Save 传播/计数/失败重试、一项初始化后当帧未更新
的三次转换链边界，以及三项显式初始化骨骼缓存。三次转换链为可控边界夹具，
不是声称普通键鼠输入会产生同样路线。

本批 Core 常规 1920/1920，Import 全套 1153/1153 通过；随后补入最后一个边界
测试并完善未更新快照的返回/消费，最终 Main / Standing / Bone 专项 62/62。
没有在这次局部完善后重复整套 Core / Import。TRX 位于
`artifacts/test-results/standing-save-initialize/`，最终专项为 `final.trx`。
Core 常规仍按既有范围排除 P5A golden / trace schema，不把其结果扩大为全阶段验收。
最终优化构建零警告、零错误。

实际资源 Main 六缓存回放 30/60/120 Hz 共 1680 帧通过，每条路线仅一次 Cycle
Save 初始化、九来源 epoch 全程受其控制。故意令最后一个来源 epoch 溢出，三个
频率均拒绝候选；部分来源已经初始化的失败不会泄漏到重试。包含来源、骨骼、
姿势及事件的重试一致，活动准备 0 B。2359 次原始 Standing 骨骼/曲线比较、
六次 Slot 故障拒绝保持通过。增加的三次原始姿势检查来自初始化故障后的正常重试。
Save 骨骼刷新仍各 19 次、状态刷新 56/56/59 次。

最终日志 `artifacts/standing-save-main-verified.log`；前期 `standing-save-main-first.log`
及 `standing-save-main-final.log` 保留。旧 Main 非缓存姿势入口也通过 1680 帧，
同样覆盖三个来源初始化故障拒绝，日志 `standing-save-main-legacy.log`。
Standing 长回放覆盖 Detail 1890 帧、Standing / Pivot 5040 帧、Sprint 1260 帧，
来源时间权威、转身 Slot、重试、活动零分配通过，日志 `standing-save-standing.log`。
长回放和旧入口在最后的未更新快照局部完善前运行；最终 Main 已包含该完善。

最终代码的单/并行 Worker 各 180 帧、各 10 个来源事件通过，结果摘要
A9DF0647AFC3574C、完整姿势摘要 04D4A5651B87E0E4 保持。晚期来源事件故障
回滚通过，回调泄漏零，runtime/result/controller/pose/P4 banks 恢复。日志为
`artifacts/standing-save-worker-single-verified.log`、`standing-save-worker-parallel.log`
和 `standing-save-worker-rollback.log`。已跟踪差异及本批未跟踪文件空白检查通过。

## 保留失败与实现边界

最初尝试在 Standing 返回帧中另外嵌入完整初始化结果，扩大了已有的大尺寸值类型
快照，嵌套调用发生栈溢出，测试主机中止。没有增加线程栈、放宽门槛或把已完成的
43 项当作全批通过；`first.trx` 保留中止记录。改为即时回调和紧凑标记后专项通过。
测试夹具还出现 int 到原生 short counter 的编译错误，已改成显式 checked 转换。

这批关闭 Main 整合中的 Standing / Stop / Detail 来源初始化传播和 Cycle 外部
来源启动补偿，不代表完整 Cycle 整体初始化完成。其方向机、滤波器、Sprint 混合
和内部缓存仍需由整个 Cycle 的实际生命周期统一；下一批继续这一点，以及 Crouch
根状态在 Save 初始化时的来源建立，再接 Main Movement、真实 Slot / Montage、
最终惯性化与生产提交。旧独立 Standing / Demo 路径仍保留临时来源启动逻辑，待
统一 owner 接入后移除；不能提前删除其初始化而制造未初始化来源。

后续最终曲线反馈、动态上身、完整脚部约束和原 P5A-P7 范围全部保留，音频仍延后。
本批 Main owner 是整合夹具，Slot 姿势仍为替身；没有新原生整图对照、移动截图、
平台路线或十分钟性能验收。既有平台脚锁失败、短矩阵超预算及视觉问题均未关闭。
未修改已确认键鼠输入、UE 资产/插件，未提交、回滚或合并工作区。
