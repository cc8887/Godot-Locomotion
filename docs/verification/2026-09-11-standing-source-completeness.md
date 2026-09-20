# Standing Idle/Rotate 来源完整性

日期：2026-09-11。第三十七批，继续完整性补完计划；未 commit、revert 或覆盖既有工作。

## 缺口与实现

准备接入完整 Standing 状态图时发现，原正式来源表只有 Cycle、Detail 和 Stop，
尚未绑定 Idle、Rotate Left 90、Rotate Right 90。左右 Rotate 的 PlayRate 连接
RotateRate，bLoopAnimation 分别连接 Rotate_L/Rotate_R，不能以固定循环设置或
旧 Turn/Rotate 通道代替这些状态内的独立播放身份。这是 Standing 接线前置缺口，
不是新功能，也不表示完整转身/上身已完成。这里依据 V4 ALS_AnimBP，非 Refactored 同名规则。

- UE 导出器补 Rotate 编译身份、资产速率、通知资产闭包及 standingSourceSchemaVersion=1。
- 新 Standing 编译器核对精确图路径、骨架/动画、编译后状态归属、Idle 显式时间、
  无组同步、动态连线/getter。拒绝未支持的输入引脚、速率变换、生命周期和 evaluator 模式。
- 在来源尾部追加三个节点，旧节点/样本编号不动：37/59 扩至 40 个来源节点/62 个样本。
- Core 增加 RotateRate 与左右循环输入。动态速率覆盖默认值再除 PlayRateBasis，
  不乘行走速率；缺输入、非有限值、错误域/同步组/输入策略均原子拒绝。
- P5 通知接受合法 Rotate 的已解析循环值；常量循环来源仍严格检查，不允许用动态标志绕过。
- 实际 Cycle Idle 从同一主快照读取显式时间，姿势采样消费该时间，不建立时钟或通知。

完整来源布局现为 98 个物理条目，其中 SourceSample 49、SourceEvaluator 13；
原生图 233 个资产节点仍有 193 个未纳入此 Locomotion 来源表，不能折算为功能完成率。
同步序列资产 32、标记 40、通知定义/策略各 48。真实 Idle 显式时间仍为零。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除独立 P5A golden/schema 套件 | 1822/1822 |
| Import 全套 Debug，最终 | 808/808 |
| Core 来源/事件/独立同步 Release | 66/66 |
| Import 来源相关 Release，最终 | 149/149 |
| Godot 构建 | 0 警告、0 错误 |
| Idle 非零显式时间与姿势采样 | 10 帧，time=0.01，无 timed player/notify |
| Standing Cycle | 30/60/120 Hz，换髋/中断回滚/零分配通过 |
| Sprint | 1260 帧、9 次回滚通过 |
| Detail/GroundedCache | 真实资源组件通过，仍为 demo=not_connected |
| 实际 Cycle 单/多线程 | 各 180 帧、9 个来源事件，摘要一致 |
| late_source_event、late_transaction | 两种模式均通过，无事件泄漏 |

新增测试包含所有权、反例、正/零/负速率、左右不同循环值、失败原子性、零分配，
以及真实 Rotate 经 Sync/P5 的通知、末尾钳制和候选重试。UE AnimSequenceBase.cpp
TickAssetPlayer 的 leader 在末尾仍保留请求 DeltaTime；采样时间钳制，通知不重复。
最初“末尾 Delta 必须零”的错误测试按源码纠正，未修改运行时来迎合该假设。

Idle 测试仅在内存真实库中加入 pelvis 哨兵关键帧，区分 0 与 0.01 秒采样；
未修改资源或原生期望。它证明采样器消费时间，不是完整 UE 姿势等价证据。
本批无新连续视觉截图，不宣称起步滑移、交错步或上身问题已完全修复。

短回放 result=8CED067A081CB773、full_pose=4BE93FAC6C035F8C、
root=309E8D0E0BEEB2CB，与第三十六批一致；相机和输入文件 SHA256 未变。
证据位于 artifacts/test-results/standing-sources/ 与 artifacts/standing-sources-*.log。

首错保留：导入首轮三个旧覆盖断言失败（未绑定 196、通知 44、Idle 未绑定），
已按新增身份修正；语义首轮 80 通过、1 个末尾 Delta 假设失败。后续全套 806 通过，
一个既有零分配断言失败 2272 B；关闭分层 JIT 后原断言通过。补输入引脚反例后
最终 808/808，未放宽零分配或数值容差。

## UE 构建与加载

遵循 ue-diagnosing-plugin-build-load 技能，完成 Editor 整体构建、全插件审计、
独立 BuildPlugin、打包后审计、重复冷导出、DataValidation 和普通冷启动。
构建、独立包、审计、两次导出、DataValidation 均退出零，未复制 DLL 或改 BuildId。

- 构建指纹：CBEABE2150ECD2EE04CE979D2F459DAEE565333C8CCA9C8179A60D35ED8BAF79。
- 项目/仓库导出器源码 SHA256：C46909081BE2711CCD33E9A81A8B58DA5396E1AB6341E7AC42A5CFB6A1DF16CD。
- 两次导出/正式 JSON SHA256：2BBA74E5077C56D93A27B63FD74AC46D16920B9274C1E722747AA5131A25F00F。
- 独立包：artifacts/unreal/AlsStandingSourcesPluginValidation-20260911。
- 原图备份：artifacts/standing-source-graph-before.json。
- DataValidation：0 错误、3 条既有资源警告。
- 普通 Editor PID 35096 初始化/插件加载成功，CloseMainWindow 后取得退出码 0；
  仍有此前同样两条 AutomationTest Condition failed，不能称为完全无错误冷启动。

## 未完成与下一步

1. 将 Main/Slot 上游、完整 Standing/Detail/Stop 接入 Controller/Worker，
   移除外层临时 MoveToward Idle/Cycle 混合，不再通过调起步相位掩盖缺图。
2. 将 Rotate、Detail、Stop 等实际相关来源纳入共享时钟/缓存/唯一 P5 候选事务。
   当前 Demo 仍只有七个 Cycle timed sources；Rotate 本批仅绑定及执行链组件验证。
3. 补 Idle/Rotate ModifyCurve/Slot、状态进入/退出、Pivot 分发、惯性化及最终曲线顺序。
4. 接动态 Layering/Add/LS、Lean、YawOffset、手部 IK、Overlay，然后原 P5C/P6/P7。
   音频暂缓，已人工确认的键鼠控制保留。
5. 同输入 UE/Godot 整图逐帧对照状态、源时间、曲线、骨骼，连续截图及人工验收后
   关闭基础移动/上身项；十分钟性能预算仍为最终门禁。
