# 第三十五批：来源通知进入实际帧事务

## 结果与边界

实际 Cycle Demo 已消费同一来源快照、Sync Tick delta、通知筛选与 State 生命周期。
Worker 只计算候选；Controller 与姿势、源时钟一起 Finalize，结果交换成功且主线程
通过角色/代际检查之后，才按原顺序调用 AnimationEventCommitted。失败帧不触发回调。
这次不是仅增加组件：双模式实际运行各提交 9 个通知，第一个出现在第 25 帧。

此项归属原完整性计划 B / P5A Task 15–21。不代表 P5A、基础移动或全角色通知完成。
当前源表仍只有 37 个播放器/求值器、59 个样本，实际 Cycle 使用其中 7 个播放器、
25 个样本；元数据涉及 30 个资产、44 个普通通知，尚没有真实 State 资产覆盖。
主 P5 的旧 v2 执行 facade 仍拒绝 v3，避免在其他通道未迁移时静默混用身份和时钟。

## 执行语义

- Sync 求值额外输出实际 Tick 顺序及 Tick 当时的 Leader 标志，包括最终 Leader
  之前的标记 Leader 尝试。转换桥不重新排序权重、选 Leader 或推进时间。
- BlendSpace 按原缓存顺序执行 All / HighestWeightedAnimation / None；同权重最高
  样本取先到者。筛选权重取播放器整体权重与上游图权重，不额外乘内部样本权重。
- 提取窗口使用同一次 Sync 的 DeltaPrevious / Delta，不能由最终姿势时间相减推导。
- 随机种子、活动 State、实例编号、来源/布局摘要与帧身份，保存在既有 Controller
  的提交/候选存储中。没有第二套永久调度器或独立生产时钟。重复、过期、跨代际帧拒绝。
- NoMergeOnConcurrentPlay 来自 UE NotifyStateBehaviorFlags；默认 State 可按 UObject
  等价延续，不把采样句柄不同等同于不同 State。独立身份含播放器与激活 epoch，编号
  作用域是角色/代际，不宣称与 UE 跨角色全局计数器的数值完全一致。
- 事件新增 NativeContext，保留实例编号、源 Tick 时间、回调时长、ActiveContext 和
  ReachedEnd。AnimationTime 仍是帧内分发偏移，不把 BlendSpace 归一化时间冒充秒数。
  原有字段及构造参数顺序不变，但 unmanaged 结构尺寸增加，调用方需重新构建；不是
  二进制兼容扩展。旧事件的默认上下文不改变摘要，新上下文纳入结果摘要。
- 候选事件最多 16 个、提取/队列引用最多 64 个，超限拒绝整帧，不静默截断。
  已有 TypedEvents 非空时拒绝覆盖另一个事件生产者；其他通道合并仍是后续工作。
- 主线程回调在视觉与诊断发布之后；回调后不再访问角色。异常记录为已提交回调失败，
  不能伪装成 Worker 回滚。提交耗时与分配统计包含回调成本。
- 普通 Apply 返回事件缓冲；手动事务需完成来源事件阶段后再 Apply/Commit。

当前 ActiveContext 仍为 Cycle 默认上下文，尚未从完整 Main/Slot 缓存图取得真实值。
尚未实现 Named Blueprint/Pivot 图事件的完整语义接收者、Montage branching point、
Slot relevance、全角色来源 readiness、所有回调重入与销毁策略；现有多订阅委托的
异常会中断该次委托链，不能称为完整 gameplay 事件总线。音频继续暂缓。

## UE 导出与门禁

正式导出器与生命周期探针增加 stateBehaviorFlags。重新导出后，移除此新增字段，
JSON 与旧版本结构完全相同：没有曲线、动画或生命周期期望漂移，assets_saved=0。
State 缺失标志、未知位和超出 byte 范围在导入阶段拒绝。

来源图 SHA256：028EC19ED977349B2DDE3227A650E09E7D9F98227B0A674A61D616DB954C8744。
生命周期 SHA256：FCE42F77C625D022E7F20EEEBFB91F6E90BAFDC53C84F4AD28115551D529F576。
生命周期仍为 169 案例、5 策略、1183 回调，独立重复导出字节一致。
日志和重复输出位于 artifacts/source-events-native-*，此前第三十四批输出不删除。

按 ue-diagnosing-plugin-build-load 技能执行完整项目 Editor 构建、三插件审计、
独立 BuildPlugin、打包后审计、资产验证和非 NullRHI Editor 冷启动，未复制 DLL、
修改引擎或编辑 BuildId。全构建日志前缀：
Saved/Logs/PluginBuild/20260910T164432709Z-38353d7980fa49eb81eacc67e89285e9。
BuildId=015ca4ed-618b-4c74-9b03-a4854c06ae7b；输入 fingerprint：
D2EF297CD2EEBED5F3B7840DF44D5731B46789E3BB3200958288899AF1358A06。
独立包 artifacts/unreal/AlsSourceEventsPluginValidation-20260911 成功，约 70 秒。
DataValidation 688 资产，退出 0、0 错误、3 个既有警告。正常 Editor 完成加载，
ExecCmds Quit 未自行退出，随后向本次创建的进程发送 CloseMainWindow，正常退出 0。
仍有原有两条 AutomationTest Condition failed 和 AI/Navmesh/材质等旧警告，不能
报告无错误 Editor 验收。技能引用的两个 superpowers 技能未提供，保留首错并逐项核验。

## 回归证据

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests | 1804/1804 |
| 新增来源事件用例 | 24/24，包含重试 10000 次零分配 |
| 事件/Sync/来源执行 Release | 120/120 |
| Import 全套 Debug | 768/768 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |
| 实际 Cycle single / parallel | 各 180 帧，9 通知，主线程/代际/顺序逐项检查 |
| 双模式 late_source_event | 第 25 帧候选 1 通知，泄漏 0，状态/RNG/编号共同回滚 |
| 双模式旧 late_transaction | 第 13 帧运行时/结果/Controller/姿势/P4/Sync 回滚通过 |
| Standing 实际资源 | 30/60/120 Hz，9 次换髋，15216 来源时间、1260 权威时间检查，零分配 |
| Detail / Grounded Cache 组件 | 通过，仍明确标注 demo=not_connected |
| 旧 P4 图、姿势、生命周期、脚部放置、P5A 绑定 | 通过 |

TRX：artifacts/test-results/source-events/。实际双模式日志：
artifacts/source-events-verified-{single,parallel}-{normal,late_source_event,late_transaction}.log。
共同 result=D8AD4733E2E9D424，新事件导致结果摘要改变；full_pose=076E345A0151A012、
pose=D6B3D85394700CC6、root=309E8D0E0BEEB2CB 与上一批一致。
这证明通知接入且既有姿势未回归，不证明完整图接线或滑步已经修复。

保留首次失败：新结构属性/存储字段契约未同步（分别修正预期的新增字段）；旧资源
测试省略事件阶段并重复帧号（改为真实单调身份、显式完成事件，不放宽运行时守卫）；
事件失败注入未开启旧回滚诊断（补开启条件，重跑全部状态检查）；命令数组括号错误
导致模式参数串接。另有初期编译错误已修正。没有删除失败日志或放宽原数值门禁。

相机与输入源码哈希保持上一批数值。未重跑多帧视觉截图或 P7 十分钟全质量矩阵，
未 commit、revert 或删除用户改动。

## 下一步

直接使用已接通的事件阶段，推进 Main/Slot、Standing/Detail/Stop 与真实贡献权重、
Pivot/状态事件、来源初始化、惯性化、Lean/Sprint Impulse 的实际生产组装。
以 UE/Godot 同输入下的状态、源时间、曲线、骨骼和多帧图像作为基础移动验收，
随后补动态 Layering/Add/LS/Hand IK/YawOffset、全部 Overlay/道具，再推进 P5C、P6、P7。
不再重复实现已经有原生证据的通知组件，也不把 Mantle/Ragdoll 缺失归因为普通滑步。
